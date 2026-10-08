using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using UltrastarDJ.App.ViewModels;
using UltrastarDJ.App.Views;
using UltrastarDJ.Core.Abstractions;
using UltrastarDJ.Core.Displays;
using UltrastarDJ.Core.Players;

namespace UltrastarDJ.App.Services;

public sealed class DisplayService : IDisplayService
{
    private const string SettingsName = "displays";
    // Small enough to sit next to the DJ window; the DJ drags it to the projector and goes fullscreen there.
    private const double InitialWidth = 960;
    private const double InitialHeight = 540;
    // Beamer 2 opens this far down-right of beamer 1 so both title bars stay reachable.
    private const int CascadePx = 48;

    private readonly ISettingsStore _settings;
    private readonly MediaService _media;
    private readonly PlayersService _players;
    private readonly IServiceProvider _services;
    private readonly ILogger<DisplayService> _log;
    private readonly Dictionary<DisplayId, BeamerWindow> _open = [];
    private Window? _owner;
    private DisplaysDocument _doc;

    public DisplayService(ISettingsStore settings, MediaService media, PlayersService players, IServiceProvider services, ILogger<DisplayService> log)
    {
        _settings = settings;
        _media = media;
        _players = players;
        _services = services;
        _log = log;
        _doc = settings.Load(SettingsName, DisplaysDocument.Default());
        players.Changed += OnPlayerChanged;
    }

    /// <summary>A player without a mic cannot sing: take it off its beamer (also when the mic was unplugged).</summary>
    private void OnPlayerChanged(PlayerConfig player)
    {
        if (player.Mic is not null)
        {
            return;
        }

        foreach (DisplayId id in (ReadOnlySpan<DisplayId>)[DisplayId.Beamer1, DisplayId.Beamer2])
        {
            DisplayConfig cfg = _doc.Get(id);
            if (cfg.PlayerIds.Contains(player.Id))
            {
                SetPlayers(id, cfg.PlayerIds.Where(p => p != player.Id).ToList());
            }
        }
    }

    public event Action<DisplayId, bool>? OpenStateChanged;
    public event Action<DisplayId, bool>? FullScreenChanged;
    public event Action<DisplayId>? PlacementChanged;

    /// <summary>
    /// Called once by the composition root. Beamers are independent windows (an owned window would always stay in
    /// front of the DJ window), so the DJ window closes them itself — before the app tears down media.
    /// </summary>
    public void AttachOwner(Window owner)
    {
        _owner = owner;
        owner.Closing += (_, _) =>
        {
            foreach (BeamerWindow window in _open.Values.ToList())
            {
                window.Close();
            }
        };
    }

    public DisplayConfig GetConfig(DisplayId id) => _doc.Get(id);

    public void SetPlayers(DisplayId id, IReadOnlyList<int> playerIds)
    {
        // A player can be on one display only: remove from the other display first.
        DisplayId other = id == DisplayId.Beamer1 ? DisplayId.Beamer2 : DisplayId.Beamer1;
        DisplayConfig otherCfg = _doc.Get(other);
        _doc = _doc
            .With(otherCfg with { PlayerIds = otherCfg.PlayerIds.Except(playerIds).ToList() })
            .With(_doc.Get(id) with { PlayerIds = playerIds });
        _settings.Save(SettingsName, _doc);
        PlayersChanged?.Invoke();
    }

    public event Action? PlayersChanged;

    public bool IsOpen(DisplayId id) => _open.ContainsKey(id);

    public void Open(DisplayId id)
    {
        if (_open.ContainsKey(id))
        {
            return;
        }

        // Resolved here, not injected: PlaybackService depends on IDisplayService (would be a constructor cycle).
        PlaybackService playback = _services.GetRequiredService<PlaybackService>();
        BeamerViewModel vm = new(id, _media.Game.Frames, playback, _players, this, _services.GetRequiredService<AppSettingsService>(),
            _services.GetRequiredService<SyncTestService>(), _services.GetRequiredService<SongbookService>());
        BeamerWindow window = new()
        {
            DataContext = vm,
            Title = $"Ultrastar DJ — Display {(int)id}",
            Width = InitialWidth,
            Height = InitialHeight,
        };
        PlaceOnOwnerScreen(window, id);

        window.PropertyChanged += (_, e) =>
        {
            if (e.Property == Window.WindowStateProperty)
            {
                FullScreenChanged?.Invoke(id, window.WindowState == WindowState.FullScreen);
                PlacementChanged?.Invoke(id);
            }
        };
        // Dragged to the projector: the panel shows the screen it is on now.
        window.PositionChanged += (_, _) => PlacementChanged?.Invoke(id);
        window.Closed += (_, _) =>
        {
            vm.Dispose();
            _open.Remove(id);
            _log.LogInformation("Display {Display} closed", (int)id);
            OpenStateChanged?.Invoke(id, false);
        };

        _open[id] = window;
        window.Show();
        _log.LogInformation("Display {Display} opened", (int)id);
        OpenStateChanged?.Invoke(id, true);
    }

    public string? ScreenText(DisplayId id)
    {
        if (!_open.TryGetValue(id, out BeamerWindow? window) || window.Screens.ScreenFromWindow(window) is not { } screen)
        {
            return null;
        }

        string name = string.IsNullOrWhiteSpace(screen.DisplayName) ? (screen.IsPrimary ? "Main screen" : "Other screen") : screen.DisplayName;
        string text = $"{name} · {screen.Bounds.Width}×{screen.Bounds.Height}";
        bool withDj = _owner is not null && _owner.Screens.ScreenFromWindow(_owner) is { } djScreen && djScreen.Bounds == screen.Bounds;
        return withDj ? $"{text} — same screen as Ultrastar DJ" : text;
    }

    public bool IsFullScreen(DisplayId id) => _open.TryGetValue(id, out BeamerWindow? w) && w.WindowState == WindowState.FullScreen;

    public void ToggleFullScreen(DisplayId id)
    {
        if (_open.TryGetValue(id, out BeamerWindow? window))
        {
            window.WindowState = window.WindowState == WindowState.FullScreen ? WindowState.Normal : WindowState.FullScreen;
        }
    }

    private void PlaceOnOwnerScreen(BeamerWindow window, DisplayId id)
    {
        Screen? screen = _owner is null ? null : _owner.Screens.ScreenFromWindow(_owner) ?? _owner.Screens.Primary;
        if (screen is null)
        {
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            return;
        }

        // Position is in physical pixels, Width/Height in DIPs.
        PixelRect area = screen.WorkingArea;
        int w = (int)(InitialWidth * screen.Scaling);
        int h = (int)(InitialHeight * screen.Scaling);
        int cascade = id == DisplayId.Beamer2 ? (int)(CascadePx * screen.Scaling) : 0;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Position = new PixelPoint(
            area.X + Math.Max(0, (area.Width - w) / 2) + cascade,
            area.Y + Math.Max(0, (area.Height - h) / 2) + cascade);
    }

    public void Close(DisplayId id)
    {
        if (_open.TryGetValue(id, out BeamerWindow? window))
        {
            window.Close();
        }
    }

    /// <summary>Persisted shape of the displays settings document.</summary>
    public sealed record DisplaysDocument(DisplayConfig Beamer1, DisplayConfig Beamer2)
    {
        public static DisplaysDocument Default()
            => new(DisplayConfig.Default(DisplayId.Beamer1), DisplayConfig.Default(DisplayId.Beamer2));

        public DisplayConfig Get(DisplayId id) => id == DisplayId.Beamer1 ? Beamer1 : Beamer2;

        public DisplaysDocument With(DisplayConfig cfg)
            => cfg.Id == DisplayId.Beamer1 ? this with { Beamer1 = cfg } : this with { Beamer2 = cfg };
    }
}
