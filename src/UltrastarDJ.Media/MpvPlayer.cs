using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using UltrastarDJ.Media.Native;
using static UltrastarDJ.Media.Native.LibMpv;

namespace UltrastarDJ.Media;

/// <summary>Process-wide settings for every <see cref="MpvPlayer"/>.</summary>
public sealed record MpvOptions(string? YtDlpPath);

/// <summary>
/// <see cref="IMediaPlayer"/> over one libmpv handle. All mpv option/property names live here and nowhere else.
/// Threads: caller (control), mpv event loop (property/state updates), render (software frames).
/// </summary>
public sealed class MpvPlayer : IMediaPlayer
{
    // Below this much cached media, a network source is not "ready" to start without stalling.
    private const double ReadyCacheSec = 2.0;
    // If the cache never reaches the threshold (short clips, throttled CDN), start anyway after this.
    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(8);
    private const int MaxRenderWidth = 1280;
    private const int MaxRenderHeight = 720;

    private static readonly Dictionary<nint, MpvPlayer> Instances = [];
    private static readonly Lock InstancesLock = new();

    private readonly ILogger _log;
    private readonly bool _video;
    private readonly nint _handle;
    private readonly nint _self;
    private readonly Thread _eventThread;
    private readonly FrameSourceImpl? _frames;

    private nint _renderContext;
    private Thread? _renderThread;
    private readonly AutoResetEvent _renderSignal = new(false);
    private volatile bool _disposing;

    private readonly object _stateLock = new();
    private TaskCompletionSource? _loadTcs;
    private bool _fileLoaded;
    private bool _started;
    private bool _pause = true;
    private bool _pausedForCache;
    private bool _eof;
    private double _timePosSec;
    private double? _durationSec;
    private double _cacheSec;
    private bool _isNetwork;
    private MediaState _state = MediaState.Idle;
    private double _volume = 1.0;
    private bool _muted;
    private double _speed = 1.0;
    private string _audioDevice = "auto";
    private double _levelRms;
    private long _lastMeterTicks;
    private int _videoWidth;
    private int _videoHeight;

    /// <summary>Creates and initialises one libmpv handle.</summary>
    /// <param name="name">Role name for logs ("game-audio", "preview", …).</param>
    /// <param name="video">Whether this player renders video (creates a software render context).</param>
    /// <param name="options">Process-wide mpv settings.</param>
    /// <param name="log">Logger.</param>
    public MpvPlayer(string name, bool video, MpvOptions options, ILogger<MpvPlayer> log)
    {
        Name = name;
        _video = video;
        _log = log;

        CLocale.EnsureCNumeric();
        _handle = Create();
        if (_handle == 0)
        {
            throw new MediaException("mpv_create failed (is LC_NUMERIC 'C'? is libmpv loadable?)");
        }

        ConfigureAndInitialize(options);

        _self = (nint)GCHandle.Alloc(this, GCHandleType.Weak);
        lock (InstancesLock)
        {
            Instances[_self] = this;
        }

        _eventThread = new Thread(EventLoop) { Name = $"mpv-events:{name}", IsBackground = true };
        _eventThread.Start();

        if (video)
        {
            _frames = new FrameSourceImpl();
            CreateRenderContext();
        }

        _log.LogDebug("{Player}: libmpv {Version:X} ready (video: {Video})", Name, ClientApiVersion(), video);
    }

    private void ConfigureAndInitialize(MpvOptions options)
    {
        // Options that must be set before mpv_initialize.
        Set("terminal", "no");
        Set("osc", "no");
        Set("input-default-bindings", "no");
        Set("input-vo-keyboard", "no");
        Set("idle", "yes");
        Set("keep-open", "no");
        Set("hwdec", "auto-safe");
        Set("vo", _video ? "libmpv" : "null");
        Set("audio-client-name", "Ultrastar DJ");
        Set("ytdl", "yes");
        if (!string.IsNullOrEmpty(options.YtDlpPath))
        {
            Set("script-opts", $"ytdl_hook-ytdl_path={options.YtDlpPath}");
        }
        // Load paused so Ready never leaks sound before Play().
        Set("pause", "yes");

        Check(Initialize(_handle), "mpv_initialize");
        RequestLogMessages(_handle, "warn");

        Observe("time-pos", MpvFormat.Double);
        Observe("duration", MpvFormat.Double);
        Observe("pause", MpvFormat.Flag);
        Observe("paused-for-cache", MpvFormat.Flag);
        Observe("eof-reached", MpvFormat.Flag);
        Observe("demuxer-cache-duration", MpvFormat.Double);
        Observe("dwidth", MpvFormat.Int64);
        Observe("dheight", MpvFormat.Int64);
        // RMS meter as an audio filter; reset=1 gives per-frame values. Read by polling (see EventLoop).
        SetProp("af", "@meter:lavfi=[astats=metadata=1:reset=1:measure_overall=none:measure_perchannel=RMS_level]");
    }

    public string Name { get; }
    public TimeSpan Position => TimeSpan.FromSeconds(Volatile.Read(ref _timePosSec));
    public TimeSpan? Duration => _durationSec is { } d ? TimeSpan.FromSeconds(d) : null;
    public MediaState State => _state;
    public double LevelRms => Volatile.Read(ref _levelRms);
    public IFrameSource? Frames => _frames;

    public double Volume
    {
        get => _volume;
        set { _volume = Math.Clamp(value, 0, 1); SetDouble("volume", _volume * 100); }
    }

    public bool Muted
    {
        get => _muted;
        set { _muted = value; SetProp("mute", value ? "yes" : "no"); }
    }

    public double Speed
    {
        get => _speed;
        set { _speed = Math.Clamp(value, 0.5, 2.0); SetDouble("speed", _speed); }
    }

    public string AudioDevice
    {
        get => _audioDevice;
        set { _audioDevice = string.IsNullOrEmpty(value) ? "auto" : value; SetProp("audio-device", _audioDevice); }
    }

    public event Action<MediaState>? StateChanged;
    public event Action<string>? ErrorOccurred;
    public event Action? EndReached;

    // ── Control ───────────────────────────────────────────────────────────

    public async Task LoadAsync(MediaSource source, MediaLoadOptions options, CancellationToken ct = default)
    {
        TaskCompletionSource tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_stateLock)
        {
            _loadTcs?.TrySetCanceled(CancellationToken.None);
            _loadTcs = tcs;
            _fileLoaded = false;
            _started = false;
            _eof = false;
            _cacheSec = 0;
            _isNetwork = source is MediaSource.YouTube;
            _timePosSec = 0;
            _durationSec = null;
            SetStateLocked(MediaState.Loading);
        }

        SetProp("vid", options.Video && _video ? "auto" : "no");
        SetProp("aid", options.Audio ? "auto" : "no");
        SetProp("start", options.StartAt > TimeSpan.Zero ? F(options.StartAt.TotalSeconds) : "none");
        SetProp("end", options.EndAt is { } end ? F(end.TotalSeconds) : "none");
        SetProp("ytdl-format", YtdlFormat(options));
        SetProp("pause", "yes");
        Volume = _volume;
        Speed = 1.0;

        Command("loadfile", source.ToMpvUri(), "replace");
        _log.LogInformation("{Player}: loading {Source} (audio:{Audio} video:{Video} start:{Start:F2}s)",
            Name, source, options.Audio, options.Video && _video, options.StartAt.TotalSeconds);

        using CancellationTokenRegistration reg = ct.Register(() => tcs.TrySetCanceled(ct));
        Task done = await Task.WhenAny(tcs.Task, Task.Delay(ReadyTimeout, ct)).ConfigureAwait(false);
        if (done != tcs.Task)
        {
            bool loaded;
            lock (_stateLock)
            {
                loaded = _fileLoaded;
            }

            if (!loaded)
            {
                // Still no FILE_LOADED: keep waiting for mpv's verdict (error or load).
                await tcs.Task.ConfigureAwait(false);
            }
            else
            {
                _log.LogWarning("{Player}: cache only {Cache:F1}s after {Timeout}s — starting anyway", Name, _cacheSec, ReadyTimeout.TotalSeconds);
                lock (_stateLock)
                {
                    tcs.TrySetResult();
                    RecomputeStateLocked();
                }
            }
        }

        await tcs.Task.ConfigureAwait(false);

        if (options.Autoplay)
        {
            Play();
        }
    }

    public void Play()
    {
        lock (_stateLock)
        {
            _started = true;
        }

        SetProp("pause", "no");
    }

    public void Pause() => SetProp("pause", "yes");

    public void Unload()
    {
        Command("stop");
        lock (_stateLock)
        {
            _fileLoaded = false;
            _started = false;
            _timePosSec = 0;
            SetStateLocked(MediaState.Idle);
        }
    }

    public void Seek(TimeSpan position) => Command("seek", F(position.TotalSeconds), "absolute");

    /// <summary>Audio outputs known to mpv (<c>audio-device-list</c>).</summary>
    public IReadOnlyList<AudioOutputDevice> ListAudioDevices()
    {
        string? json = TakeString(GetPropertyString(_handle, "audio-device-list"));
        if (json is null)
        {
            return [AudioOutputDevice.Auto];
        }

        List<AudioOutputDevice> list = [];
        using JsonDocument doc = JsonDocument.Parse(json);
        foreach (JsonElement e in doc.RootElement.EnumerateArray())
        {
            string id = e.GetProperty("name").GetString() ?? "";
            string desc = e.TryGetProperty("description", out JsonElement d) ? d.GetString() ?? id : id;
            list.Add(id == "auto" ? AudioOutputDevice.Auto : new AudioOutputDevice(id, desc));
        }

        return list;
    }

    // ── Event loop ────────────────────────────────────────────────────────

    private unsafe void EventLoop()
    {
        while (true)
        {
            MpvEvent* ev = WaitEvent(_handle, 0.05);
            if (ev is null)
            {
                continue;
            }

            // Rate-limited inside; runs on idle wakeups and between bursts of property events alike.
            PollMeter();

            switch (ev->EventId)
            {
                case MpvEventId.Shutdown:
                    return;
                case MpvEventId.None:
                    if (_disposing)
                    {
                        return;
                    }

                    break;
                case MpvEventId.PropertyChange:
                    OnPropertyChange((MpvEventProperty*)ev->Data);
                    break;
                case MpvEventId.FileLoaded:
                    OnFileLoaded();
                    break;
                case MpvEventId.EndFile:
                    OnEndFile((MpvEventEndFile*)ev->Data);
                    break;
                case MpvEventId.LogMessage:
                    MpvEventLogMessage* m = (MpvEventLogMessage*)ev->Data;
                    _log.LogWarning("{Player}: mpv[{Prefix}] {Text}", Name, Utf8(m->Prefix), Utf8(m->Text)?.TrimEnd());
                    break;
            }
        }
    }

    private unsafe void OnPropertyChange(MpvEventProperty* p)
    {
        string? name = Utf8(p->Name);
        bool available = p->Format != MpvFormat.None && p->Data is not null;
        switch (name)
        {
            case "time-pos":
                if (available)
                {
                    Volatile.Write(ref _timePosSec, *(double*)p->Data);
                }
                break;
            case "duration":
                _durationSec = available ? *(double*)p->Data : null;
                break;
            case "demuxer-cache-duration":
                _cacheSec = available ? *(double*)p->Data : 0;
                EvaluateReadiness();
                break;
            case "pause":
                if (available)
                {
                    lock (_stateLock) { _pause = *(int*)p->Data != 0; RecomputeStateLocked(); }
                }
                break;
            case "paused-for-cache":
                if (available)
                {
                    lock (_stateLock) { _pausedForCache = *(int*)p->Data != 0; RecomputeStateLocked(); }
                }
                break;
            case "eof-reached":
                if (available)
                {
                    lock (_stateLock) { _eof = *(int*)p->Data != 0; RecomputeStateLocked(); }
                }
                break;
            case "dwidth":
                if (available)
                {
                    _videoWidth = (int)*(long*)p->Data;
                }
                break;
            case "dheight":
                if (available)
                {
                    _videoHeight = (int)*(long*)p->Data;
                }
                break;
        }
    }

    private void OnFileLoaded()
    {
        lock (_stateLock)
        {
            _fileLoaded = true;
            SetStateLocked(MediaState.Buffering);
        }

        EvaluateReadiness();
    }

    private void EvaluateReadiness()
    {
        TaskCompletionSource? tcs;
        lock (_stateLock)
        {
            if (!_fileLoaded || _loadTcs is null || _loadTcs.Task.IsCompleted)
            {
                return;
            }

            double remaining = _durationSec is { } d ? d - _timePosSec : double.MaxValue;
            // mpv only caches network sources; local files are ready as soon as they are loaded.
            bool ready = !_isNetwork || _cacheSec >= ReadyCacheSec || _cacheSec >= remaining - 0.1;
            if (!ready)
            {
                return;
            }

            tcs = _loadTcs;
            tcs.TrySetResult();
            RecomputeStateLocked();
        }

        _log.LogDebug("{Player}: ready (cache {Cache:F1}s, duration {Duration:F1}s)", Name, _cacheSec, _durationSec ?? 0);
    }

    private unsafe void OnEndFile(MpvEventEndFile* e)
    {
        if (_disposing)
        {
            return;
        }

        string? error = e->Reason == MpvEndFileReason.Error ? ErrorMessage(e->Error) : null;
        TaskCompletionSource? pendingLoad;
        lock (_stateLock)
        {
            pendingLoad = _loadTcs;
            _fileLoaded = false;
            if (e->Reason == MpvEndFileReason.Eof)
            {
                SetStateLocked(MediaState.Ended);
            }
            else if (error is not null)
            {
                SetStateLocked(MediaState.Error);
            }
            else if (e->Reason == MpvEndFileReason.Stop)
            {
                SetStateLocked(MediaState.Idle);
            }
        }

        if (error is not null)
        {
            _log.LogError("{Player}: playback failed: {Error}", Name, error);
            pendingLoad?.TrySetException(new MediaException(error));
            ErrorOccurred?.Invoke(error);
        }
        else if (e->Reason == MpvEndFileReason.Eof)
        {
            EndReached?.Invoke();
        }
    }

    private void RecomputeStateLocked()
    {
        if (!_fileLoaded || _state is MediaState.Loading or MediaState.Error or MediaState.Ended)
        {
            return;
        }

        if (_loadTcs is { Task.IsCompleted: false })
        {
            SetStateLocked(MediaState.Buffering);
        }
        else if (_pausedForCache && !_pause)
        {
            SetStateLocked(MediaState.Buffering);
        }
        else if (_pause)
        {
            SetStateLocked(_started ? MediaState.Paused : MediaState.Ready);
        }
        else
        {
            SetStateLocked(MediaState.Playing);
        }
    }

    private void SetStateLocked(MediaState state)
    {
        if (_state == state)
        {
            return;
        }

        _state = state;
        _log.LogDebug("{Player}: state → {State}", Name, state);
        ThreadPool.UnsafeQueueUserWorkItem(static s => s.Item1.StateChanged?.Invoke(s.Item2), (this, state), preferLocal: false);
    }

    private void PollMeter()
    {
        // 50 ms resolution is plenty for a VU bar; skip when nothing plays.
        long now = Environment.TickCount64;
        if (now - _lastMeterTicks < 50)
        {
            return;
        }

        _lastMeterTicks = now;
        if (_state != MediaState.Playing)
        {
            Volatile.Write(ref _levelRms, 0);
            return;
        }

        ParseMeter(TakeString(GetPropertyString(_handle, "af-metadata/meter")));
    }

    private void ParseMeter(string? json)
    {
        if (json is null)
        {
            return;
        }

        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            double sumSq = 0;
            int n = 0;
            foreach (JsonProperty prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Name.EndsWith(".RMS_level", StringComparison.Ordinal)
                    && double.TryParse(prop.Value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double dbfs))
                {
                    double lin = double.IsNegativeInfinity(dbfs) || dbfs < -90 ? 0 : Math.Pow(10, dbfs / 20);
                    sumSq += lin * lin;
                    n++;
                }
            }

            Volatile.Write(ref _levelRms, n == 0 ? 0 : Math.Sqrt(sumSq / n));
        }
        catch (JsonException)
        {
        }
    }

    // ── Software rendering ────────────────────────────────────────────────

    private unsafe void CreateRenderContext()
    {
        byte* apiType = stackalloc byte[3] { (byte)'s', (byte)'w', 0 };
        MpvRenderParam* p = stackalloc MpvRenderParam[2];
        p[0] = new MpvRenderParam { Type = MpvRenderParamType.ApiType, Data = apiType };
        p[1] = default;
        Check(RenderContextCreate(out _renderContext, _handle, p), "mpv_render_context_create");
        RenderContextSetUpdateCallback(_renderContext, &OnRenderUpdate, _self);

        _renderThread = new Thread(RenderLoop) { Name = $"mpv-render:{Name}", IsBackground = true };
        _renderThread.Start();
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static void OnRenderUpdate(nint userData)
    {
        MpvPlayer? self;
        lock (InstancesLock)
        {
            Instances.TryGetValue(userData, out self);
        }

        self?._renderSignal.Set();
    }

    private unsafe void RenderLoop()
    {
        byte* format = stackalloc byte[5] { (byte)'b', (byte)'g', (byte)'r', (byte)'0', 0 };
        int* size = stackalloc int[2];
        MpvRenderParam* p = stackalloc MpvRenderParam[5];
        nuint stride = 0;
        void* buffer = null;
        int bufW = 0, bufH = 0;

        try
        {
            while (!_disposing)
            {
                _renderSignal.WaitOne(250);
                if (_disposing)
                {
                    break;
                }

                if ((RenderContextUpdate(_renderContext) & RenderUpdateFrame) == 0)
                {
                    continue;
                }

                (int w, int h) = TargetSize();
                if (w != bufW || h != bufH)
                {
                    if (buffer is not null)
                    {
                        NativeMemory.Free(buffer);
                    }

                    bufW = w;
                    bufH = h;
                    stride = (nuint)(w * 4);
                    buffer = NativeMemory.AlignedAlloc(stride * (nuint)h, 64);
                }

                size[0] = w;
                size[1] = h;
                p[0] = new MpvRenderParam { Type = MpvRenderParamType.SwSize, Data = size };
                p[1] = new MpvRenderParam { Type = MpvRenderParamType.SwFormat, Data = format };
                p[2] = new MpvRenderParam { Type = MpvRenderParamType.SwStride, Data = &stride };
                p[3] = new MpvRenderParam { Type = MpvRenderParamType.SwPointer, Data = buffer };
                p[4] = default;

                int err = RenderContextRender(_renderContext, p);
                if (err < 0)
                {
                    _log.LogWarning("{Player}: render failed: {Error}", Name, ErrorMessage(err));
                    continue;
                }

                _frames!.Publish(new FrameRef((nint)buffer, w, h, (int)stride));
            }
        }
        finally
        {
            if (buffer is not null)
            {
                NativeMemory.Free(buffer);
            }
        }
    }

    private (int, int) TargetSize()
    {
        int w = _videoWidth > 0 ? _videoWidth : MaxRenderWidth;
        int h = _videoHeight > 0 ? _videoHeight : MaxRenderHeight;
        double scale = Math.Min(1.0, Math.Min((double)MaxRenderWidth / w, (double)MaxRenderHeight / h));
        // Even dimensions keep the 4-byte stride aligned and avoid odd-size swscale paths.
        return (Math.Max(2, (int)(w * scale) & ~1), Math.Max(2, (int)(h * scale) & ~1));
    }

    private sealed class FrameSourceImpl : IFrameSource
    {
        public event Action<FrameRef>? FrameReady;
        public void Publish(FrameRef frame) => FrameReady?.Invoke(frame);
    }

    // ── Plumbing ──────────────────────────────────────────────────────────

    private static string YtdlFormat(MediaLoadOptions o)
    {
        string h = o.MaxHeight.ToString(CultureInfo.InvariantCulture);
        if (!o.Video)
        {
            return "bestaudio/best";
        }

        return o.Audio
            ? $"bestvideo[height<={h}][vcodec^=avc1]+bestaudio/bestvideo[height<={h}]+bestaudio/best[height<={h}]"
            : $"bestvideo[height<={h}][vcodec^=avc1]/bestvideo[height<={h}]";
    }

    private static string F(double v) => v.ToString("F3", CultureInfo.InvariantCulture);

    private void Set(string name, string value) => Check(SetOptionString(_handle, name, value), $"option {name}={value}");

    private void SetProp(string name, string value)
    {
        int err = SetPropertyString(_handle, name, value);
        if (err < 0)
        {
            _log.LogWarning("{Player}: set {Name}={Value} failed: {Error}", Name, name, value, ErrorMessage(err));
        }
    }

    private unsafe void SetDouble(string name, double value)
    {
        int err = SetProperty(_handle, name, MpvFormat.Double, &value);
        if (err < 0)
        {
            _log.LogWarning("{Player}: set {Name}={Value} failed: {Error}", Name, name, value, ErrorMessage(err));
        }
    }

    private void Observe(string name, MpvFormat format) => Check(ObserveProperty(_handle, 0, name, format), $"observe {name}");

    private unsafe void Command(params string[] args)
    {
        nint[] ptrs = new nint[args.Length + 1];
        try
        {
            for (int i = 0; i < args.Length; i++)
            {
                ptrs[i] = Marshal.StringToCoTaskMemUTF8(args[i]);
            }

            fixed (nint* pp = ptrs)
            {
                int err = LibMpv.Command(_handle, (byte**)pp);
                if (err < 0)
                {
                    _log.LogWarning("{Player}: command {Command} failed: {Error}", Name, string.Join(' ', args), ErrorMessage(err));
                }
            }
        }
        finally
        {
            foreach (nint p in ptrs)
            {
                if (p != 0)
                {
                    Marshal.FreeCoTaskMem(p);
                }
            }
        }
    }

    private static void Check(int err, string what)
    {
        if (err < 0)
        {
            throw new MediaException($"{what}: {ErrorMessage(err)}");
        }
    }

    private static unsafe string? Utf8(byte* s) => s is null ? null : Marshal.PtrToStringUTF8((nint)s);

    public ValueTask DisposeAsync()
    {
        if (_disposing)
        {
            return ValueTask.CompletedTask;
        }

        _disposing = true;
        lock (_stateLock)
        {
            _loadTcs?.TrySetCanceled(CancellationToken.None);
        }

        _renderSignal.Set();
        _renderThread?.Join(2000);
        if (_renderContext != 0)
        {
            unsafe
            {
                RenderContextSetUpdateCallback(_renderContext, null, 0);
            }

            RenderContextFree(_renderContext);
            _renderContext = 0;
        }

        // Order matters (client.h): render context before the core; the event thread must be out of
        // mpv_wait_event before terminate_destroy, so "quit" → wait for MPV_EVENT_SHUTDOWN → destroy.
        Command("quit");
        if (!_eventThread.Join(3000))
        {
            _log.LogWarning("{Player}: event loop did not exit after quit", Name);
        }

        TerminateDestroy(_handle);

        lock (InstancesLock)
        {
            Instances.Remove(_self);
        }

        GCHandle.FromIntPtr(_self).Free();
        _renderSignal.Dispose();
        _log.LogDebug("{Player}: disposed", Name);
        return ValueTask.CompletedTask;
    }
}
