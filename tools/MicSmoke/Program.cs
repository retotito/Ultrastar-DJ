using Microsoft.Extensions.Logging;
using UltrastarDJ.Audio;
using UltrastarDJ.Audio.Mics;
using UltrastarDJ.Core.Players;
using UltrastarDJ.Audio.Monitor;
using UltrastarDJ.Audio.PortAudio;
using UltrastarDJ.Core.Game;

// Headless audio-input smoke test.
//   dotnet run --project tools/MicSmoke                       list devices
//   dotnet run --project tools/MicSmoke -- mics [seconds]     open every 2-ch input as L/R players, print levels + notes
//   dotnet run --project tools/MicSmoke -- monitor <output>   same, plus mic monitoring on <output device name>
//   dotnet run --project tools/MicSmoke -- latency <output> <input> [L|R]
using ILoggerFactory loggers = LoggerFactory.Create(b => b.AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "HH:mm:ss.fff "; }).SetMinimumLevel(LogLevel.Debug));
ILogger log = loggers.CreateLogger("smoke");
using PortAudioBackend backend = new(loggers.CreateLogger<PortAudioBackend>());

foreach (AudioDeviceInfo d in backend.Devices)
{
    log.LogInformation("{Kind} {Name}: in {In} / out {Out} @ {Rate} Hz{Def}", d.IsInput ? "IN " : "OUT", d.Id, d.MaxInputChannels, d.MaxOutputChannels, d.DefaultSampleRateHz,
        d.IsDefaultInput ? " (default in)" : d.IsDefaultOutput ? " (default out)" : "");
}

string mode = args.Length > 0 ? args[0] : "list";
if (mode == "list")
{
    return;
}

if (mode == "latency")
{
    string outDev = args[1];
    string inDev = args[2];
    MicChannelSide ch = args.Length > 3 && args[3].Equals("R", StringComparison.OrdinalIgnoreCase) ? MicChannelSide.Right : MicChannelSide.Left;
    LatencyTest test = new(backend, loggers.CreateLogger<LatencyTest>());
    LatencyTest.Result r = await test.RunAsync(outDev, new MicBinding(inDev, ch), trials: 5);
    log.LogInformation("RESULT: median {Median:F0} ms (trials: {Trials})", r.MedianMs, string.Join(", ", r.TrialsMs.Select(t => t.ToString("F0"))));
    return;
}

// mics / monitor: one player per channel of every stereo input that is not a virtual/loopback device,
// plus the default (built-in) mic as a mono cross-check when a slot is free.
List<MicSlot> slots = [];
int player = 1;
foreach (AudioDeviceInfo d in backend.Devices.Where(d => d.MaxInputChannels >= 2 && !d.Name.Contains("Recorder", StringComparison.OrdinalIgnoreCase)))
{
    if (player > 4)
    {
        break;
    }

    slots.Add(new MicSlot(player++, new MicBinding(d.Id, MicChannelSide.Left), 1.0, 0.0));
    if (player > 4)
    {
        break;
    }

    slots.Add(new MicSlot(player++, new MicBinding(d.Id, MicChannelSide.Right), 1.0, 0.0));
}

if (player <= 4 && backend.Devices.FirstOrDefault(d => d.IsDefaultInput) is { } builtin)
{
    slots.Add(new MicSlot(4, new MicBinding(builtin.Id, MicChannelSide.Mono), 1.0, 0.0));
}

foreach (MicSlot s in slots)
{
    log.LogInformation("P{Player} ← {Device} {Channel}", s.PlayerId, s.Mic.DeviceId, s.Mic.Channel);
}

using MicEngine engine = new(backend, loggers.CreateLogger<MicEngine>());
engine.Start(slots);

using MonitorMixer mixer = new(backend, loggers.CreateLogger<MonitorMixer>());
if (mode == "monitor")
{
    mixer.Start(args[1], 0, engine.Pipelines);
}

int seconds = mode == "mics" && args.Length > 1 ? int.Parse(args[1]) : 20;
DateTime end = DateTime.UtcNow.AddSeconds(seconds);
IReadOnlyList<PitchSample> latest = [];
engine.Analyzed += s => latest = s.ToArray();
log.LogInformation("Sing! ({Seconds}s)", seconds);
while (DateTime.UtcNow < end)
{
    await Task.Delay(250);
    string line = string.Join(" | ", latest.OrderBy(s => s.PlayerId).Select(s =>
        $"P{s.PlayerId} {Bar(s.Level)} {(s.MidiNote >= 0 ? NoteName(s.MidiNote) + $" ({s.FrequencyHz:F0}Hz c{s.Clarity:F2})" : "—")}"));
    Console.WriteLine(line);
}

engine.Stop();

static string Bar(double level)
{
    // dB scale: -60 dBFS = empty, 0 dBFS = full.
    double db = level <= 0 ? -60 : Math.Max(-60, 20 * Math.Log10(level));
    int n = (int)Math.Clamp((db + 60) / 60 * 12, 0, 12);
    return $"[{new string('#', n)}{new string('.', 12 - n)}]{db,4:F0}dB";
}

static string NoteName(double midi)
{
    string[] names = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];
    int m = (int)Math.Round(midi);
    return $"{names[((m % 12) + 12) % 12]}{m / 12 - 1} us{PitchMatching.MidiToUsPitch(m):+0;-0}";
}
