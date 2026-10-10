using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UltrastarDJ.App.Services;
using UltrastarDJ.App.Localization;

namespace UltrastarDJ.App.ViewModels;

/// <summary>Songbook panel: start/stop the guest server, show the URLs, manage the party PIN.</summary>
public sealed partial class SongbookPanelViewModel : ViewModelBase, IDisposable
{
    private readonly SongbookService _songbook;
    private readonly NotificationService _notifications;
    private bool _loading;

    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _busy;
    [ObservableProperty] private bool _pinEnabled;
    [ObservableProperty] private bool _autoStart;
    [ObservableProperty] private bool _requestsOpen;
    [ObservableProperty] private string _connectVia = WifiChoice;
    [ObservableProperty] private string _publicUrl = "";
    [ObservableProperty] private string _publicStatus = "";
    [ObservableProperty] private string _pin = "";
    [ObservableProperty] private string _port = "";
    [ObservableProperty] private string _urls = "";

    public SongbookPanelViewModel(SongbookService songbook, NotificationService notifications)
    {
        _songbook = songbook;
        _notifications = notifications;
        _songbook.Changed += OnChanged;
        Refresh();
    }

    private void OnChanged() => Dispatcher.UIThread.Post(Refresh);

    private void Refresh()
    {
        _loading = true;
        IsRunning = _songbook.IsRunning;
        PinEnabled = _songbook.PinEnabled;
        AutoStart = _songbook.AutoStart;
        RequestsOpen = _songbook.RequestsOpen;
        Pin = _songbook.Pin;
        Port = _songbook.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        Urls = IsRunning ? string.Join("\n", _songbook.Urls()) : "";
        ConnectVia = _songbook.PublicLink ? PublicChoice : WifiChoice;
        PublicUrl = _songbook.PublicUrl ?? "";
        PublicStatus = _songbook.PublicStatus;
        _loading = false;
    }

    [RelayCommand]
    private async Task ToggleAsync()
    {
        Busy = true;
        try
        {
            if (IsRunning)
            {
                await _songbook.StopAsync();
            }
            else
            {
                await _songbook.StartAsync();
                if (_songbook.LastError is { } error)
                {
                    _notifications.ShowError(L.T("songbook.could_not_start"), error);
                }
            }
        }
        finally
        {
            Busy = false;
        }
    }

    [RelayCommand] private void RegeneratePin() => _songbook.RegeneratePin();

    partial void OnPinEnabledChanged(bool value) { if (!_loading) { _songbook.SetPinEnabled(value); } }
    partial void OnAutoStartChanged(bool value) { if (!_loading) { _songbook.SetAutoStart(value); } }
    // ── How guests connect: one at a time ──
    private const string WifiChoice = "Wi-Fi";
    private const string PublicChoice = "Public link";
    public IReadOnlyList<string> ConnectChoices { get; } = [WifiChoice, PublicChoice];
    public bool IsPublic => ConnectVia == PublicChoice;
    public bool IsWifi => !IsPublic;

    partial void OnConnectViaChanged(string value)
    {
        OnPropertyChanged(nameof(IsPublic));
        OnPropertyChanged(nameof(IsWifi));
        if (!_loading && value is not null)
        {
            _ = SwitchAsync(value == PublicChoice);
        }
    }

    // Running: stops the active way in and starts the other (a few seconds); the Start button waits meanwhile.
    private async Task SwitchAsync(bool toPublic)
    {
        Busy = true;
        try
        {
            await _songbook.SetPublicLinkAsync(toPublic);
        }
        finally
        {
            Busy = false;
        }
    }

    partial void OnRequestsOpenChanged(bool value) { if (!_loading) { _songbook.SetRequestsOpen(value); } }
    partial void OnPortChanged(string value)
    {
        if (!_loading && int.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out int port))
        {
            _songbook.SetPort(port);
        }
    }

    public void Dispose() => _songbook.Changed -= OnChanged;
}
