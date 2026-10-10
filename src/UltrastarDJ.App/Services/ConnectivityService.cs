using System.Net.NetworkInformation;
using Microsoft.Extensions.Logging;
using UltrastarDJ.App.Localization;

namespace UltrastarDJ.App.Services;

/// <summary>
/// Is YouTube reachable? Probed at start, when the OS reports a network change, and every 15 s — "network up" is not
/// enough (Wi-Fi without internet). Toasts on every change (and at start when offline); <see cref="Changed"/> lets
/// USDB grey out and reconnect.
/// </summary>
public sealed class ConnectivityService : IDisposable
{
    // Google's no-content endpoint on the YouTube host: tiny, and exactly the host the app needs.
    private static readonly Uri Probe = new("https://www.youtube.com/generate_204");
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);

    private readonly NotificationService _notifications;
    private readonly ILogger<ConnectivityService> _log;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(5) };
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _probeGate = new(1, 1);

    public ConnectivityService(NotificationService notifications, ILogger<ConnectivityService> log)
    {
        _notifications = notifications;
        _log = log;
    }

    /// <summary>Null until the first probe finished.</summary>
    public bool? IsOnline { get; private set; }

    /// <summary>Raised on a thread-pool thread when the state flips (not for the first probe).</summary>
    public event Action<bool>? Changed;

    /// <summary>First probe (awaited, so startup can act on it), then background watching.</summary>
    public async Task StartAsync()
    {
        await CheckAsync().ConfigureAwait(false);
        NetworkChange.NetworkAddressChanged += OnNetworkChanged;
        _ = Task.Run(() => PollAsync(_cts.Token));
    }

    public async Task CheckAsync()
    {
        if (!await _probeGate.WaitAsync(0).ConfigureAwait(false))
        {
            return; // a probe is already running
        }

        try
        {
            bool online = await ProbeAsync().ConfigureAwait(false);
            bool? before = IsOnline;
            if (before == online)
            {
                return;
            }

            IsOnline = online;
            _log.LogInformation("Connectivity: {State}", online ? "online" : "offline");
            if (online && before is false)
            {
                _notifications.Success(L.T("net.back_online"), L.T("net.back_online_detail"));
            }
            else if (!online)
            {
                _notifications.Warn(L.T("net.offline"), L.T("net.offline_detail"));
            }

            if (before is not null)
            {
                Changed?.Invoke(online);
            }
        }
        finally
        {
            _probeGate.Release();
        }
    }

    private async Task<bool> ProbeAsync()
    {
        try
        {
            using HttpRequestMessage request = new(HttpMethod.Head, Probe);
            using HttpResponseMessage response = await _http.SendAsync(request, _cts.Token).ConfigureAwait(false);
            return true; // any answer from YouTube means we are online
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }

    private void OnNetworkChanged(object? sender, EventArgs e) => _ = CheckAsync();

    private async Task PollAsync(CancellationToken ct)
    {
        using PeriodicTimer timer = new(Interval);
        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                await CheckAsync().ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    public void Dispose()
    {
        NetworkChange.NetworkAddressChanged -= OnNetworkChanged;
        _cts.Cancel();
        _http.Dispose();
        _cts.Dispose();
        _probeGate.Dispose();
    }
}
