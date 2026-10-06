using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UltrastarDJ.App.Services;

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
        Pin = _songbook.Pin;
        Port = _songbook.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        Urls = IsRunning ? string.Join("\n", _songbook.Urls()) : "";
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
                    _notifications.ShowError("Songbook could not start", error);
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
    partial void OnPortChanged(string value)
    {
        if (!_loading && int.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out int port))
        {
            _songbook.SetPort(port);
        }
    }

    public void Dispose() => _songbook.Changed -= OnChanged;
}
