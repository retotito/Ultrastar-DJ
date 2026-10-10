using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
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
    // macOS animates leaving fullscreen; a move during the animation is undone by it.
    private static readonly TimeSpan AfterFullScreenExit = TimeSpan.FromMilliseconds(800);

    private readonly ISettingsStore _settings;
    private readonly MediaService _media;
    private readonly PlayersService _players;
    private readonly IServiceProvider _services;
    private readonly ILogger<DisplayService> _log;
    private readonly NotificationService _notifications;
    private readonly Dictionary<DisplayId, BeamerWindow> _open = [];
    // Where each open display belongs (its screen, normal bounds, mode) — kept while the screens stay the same, so
    // the OS moving a window off an unplugged screen does not overwrite it.
    private readonly Dictionary<DisplayId, WindowPlacement> _home = [];
    // Displays whose screen is unplugged: parked next to the DJ window until it is back.
    private readonly HashSet<DisplayId> _lost = [];
    private HashSet<ScreenArea> _screens = [];
    private Window? _owner;
    private DisplaysDocument _doc;

    public DisplayService(ISettingsStore settings, MediaService media, PlayersService players, IServiceProvider services, ILogger<DisplayService> log,
        NotificationService notifications)
    {
        _notifications = notifications;
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
    public event Action<DisplayId, string>? ScreenLost;
    public event Action<DisplayId, bool>? FullScreenChanged;
    public event Action<DisplayId>? PlacementChanged;

    /// <summary>
    /// Called once by the composition root. Beamers are independent windows (an owned window would always stay in
    /// front of the DJ window), so the DJ window closes them itself — before the app tears down media.
    /// </summary>
    public void AttachOwner(Window owner)
    {
        _owner = owner;
        _screens = [.. Areas(owner)];
        owner.Screens.Changed += (_, _) => OnScreensChanged();
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
        // Where it was last time (on the projector, maximised / fullscreen); its screen not connected → next to the
        // DJ window as on a first open.
        if (!WindowMemory.Restore(window, _doc.Get(id).Placement))
        {
            PlaceOnOwnerScreen(window, id);
        }

        WindowMemory.Track(window, p =>
        {
            // Closed while its screen is unplugged: next time it opens on its screen again, not next to the DJ window.
            if (_lost.Contains(id) && _home.TryGetValue(id, out WindowPlacement? home))
            {
                p = home;
            }

            _doc = _doc.With(_doc.Get(id) with { Placement = p });
            _settings.Save(SettingsName, _doc);
        });

        window.PropertyChanged += (_, e) =>
        {
            if (e.Property == Window.WindowStateProperty)
            {
                RememberHome(id, window);
                FullScreenChanged?.Invoke(id, window.WindowState == WindowState.FullScreen);
                PlacementChanged?.Invoke(id);
            }
        };
        // Dragged to the projector: the panel shows the screen it is on now.
        window.PositionChanged += (_, _) =>
        {
            RememberHome(id, window);
            PlacementChanged?.Invoke(id);
        };
        window.Resized += (_, _) => RememberHome(id, window);
        window.Opened += (_, _) => RememberHome(id, window);
        window.Closed += (_, _) =>
        {
            vm.Dispose();
            _open.Remove(id);
            _home.Remove(id);
            _lost.Remove(id);
            _log.LogInformation("Display {Display} closed", (int)id);
            OpenStateChanged?.Invoke(id, false);
        };

        _open[id] = window;
        window.Show();
        _log.LogInformation("Display {Display} opened", (int)id);
        OpenStateChanged?.Invoke(id, true);
    }

    private static List<ScreenArea> Areas(Window window)
        => [.. window.Screens.All.Select(s => new ScreenArea(s.WorkingArea.X, s.WorkingArea.Y, s.WorkingArea.Width, s.WorkingArea.Height, s.Scaling, s.DisplayName))];

    private static string NameOf(Screen? screen) => screen is null || string.IsNullOrWhiteSpace(screen.DisplayName) ? "Screen" : screen.DisplayName;

    private void RememberHome(DisplayId id, BeamerWindow window)
    {
        // Not while parked, and not while a screen change is still unhandled (the OS already moved the window).
        if (_lost.Contains(id) || !window.IsVisible || !_screens.SetEquals(Areas(window)))
        {
            return;
        }

        WindowMode mode = window.WindowState switch
        {
            WindowState.FullScreen => WindowMode.FullScreen,
            WindowState.Maximized => WindowMode.Maximized,
            _ => WindowMode.Normal,
        };
        // Normal bounds only: fullscreen bounds are not what it returns to (as in WindowMemory).
        WindowPlacement? before = _home.GetValueOrDefault(id);
        PixelPoint pos = window.WindowState == WindowState.Normal || before is null ? window.Position : new PixelPoint(before.X, before.Y);
        Size size = window.WindowState == WindowState.Normal || before is null ? window.ClientSize : new Size(before.Width, before.Height);
        Screen? screen = window.Screens.ScreenFromPoint(new PixelPoint(pos.X + 20, pos.Y + 10)) ?? window.Screens.ScreenFromWindow(window);
        _home[id] = new WindowPlacement(pos.X, pos.Y, Math.Round(size.Width), Math.Round(size.Height), mode,
            screen?.DisplayName, screen?.WorkingArea.X ?? 0, screen?.WorkingArea.Y ?? 0);
    }

    /// <summary>
    /// A screen was plugged in or out. A display whose screen is gone leaves fullscreen and parks next to the DJ window
    /// (macOS would put it fullscreen over the DJ window); when its screen is back it returns there, fullscreen again.
    /// </summary>
    private void OnScreensChanged()
    {
        if (_owner is null)
        {
            return;
        }

        List<ScreenArea> now = Areas(_owner);
        HashSet<ScreenArea> before = _screens;
        _screens = [.. now];
        HashSet<string> returned = [];
        foreach ((DisplayId id, BeamerWindow window) in _open.ToList())
        {
            if (!_home.TryGetValue(id, out WindowPlacement? home))
            {
                continue;
            }

            string screen = home.Screen ?? "Screen";
            bool connected = home.IsConnected(now);
            if (!connected && _lost.Add(id))
            {
                _log.LogWarning("Display {Display}: screen {Screen} disconnected — parked next to the DJ window", (int)id, screen);
                Park(id, window);
                ScreenLost?.Invoke(id, screen);
            }
            else if (connected && _lost.Remove(id))
            {
                _log.LogInformation("Display {Display}: screen {Screen} is back", (int)id, screen);
                returned.Add(screen);
                GoHome(window, WindowPlacement.Fit(home, now, window.MinWidth, window.MinHeight)!);
                _notifications.Success($"Display {(int)id}: {screen} connected", "The display is back on its screen.");
            }
        }

        // Screens no display belongs to, plugged in or out: a hint either way (displays got their toast above).
        static bool Kept(ScreenArea s, IEnumerable<ScreenArea> other) => other.Any(o => o == s || o.Name is not null && o.Name == s.Name);
        foreach (ScreenArea added in now.Where(s => !Kept(s, before)))
        {
            string name = added.Name ?? "Screen";
            if (!returned.Contains(name) && !_home.Values.Any(h => h.Screen == name))
            {
                _notifications.Info($"Screen connected: {name}", "Open a display under Game Displays and drag it there.");
            }
        }

        foreach (ScreenArea removed in before.Where(s => !Kept(s, now)))
        {
            string name = removed.Name ?? "Screen";
            if (!_home.Values.Any(h => h.Screen == name))
            {
                _notifications.Info($"Screen disconnected: {name}");
            }
        }
    }

    // Out of fullscreen first; then next to the DJ window, at its first-open size.
    private void Park(DisplayId id, BeamerWindow window)
    {
        bool wasFull = window.WindowState != WindowState.Normal;
        window.WindowState = WindowState.Normal;
        void Move()
        {
            if (_open.ContainsKey(id) && _lost.Contains(id))
            {
                window.Width = InitialWidth;
                window.Height = InitialHeight;
                PlaceOnOwnerScreen(window, id);
            }
        }

        if (wasFull)
        {
            DispatcherTimer.RunOnce(Move, AfterFullScreenExit);
        }
        else
        {
            Move();
        }
    }

    private static void GoHome(BeamerWindow window, WindowPlacement p)
    {
        window.WindowState = WindowState.Normal;
        window.Position = new PixelPoint(p.X, p.Y);
        window.Width = p.Width;
        window.Height = p.Height;
        if (p.Mode != WindowMode.Normal)
        {
            // Once it sits on its screen: fullscreen goes to the screen the window is on.
            DispatcherTimer.RunOnce(() => window.WindowState = p.Mode == WindowMode.FullScreen ? WindowState.FullScreen : WindowState.Maximized,
                AfterFullScreenExit);
        }
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
            window.ToggleFullScreen();
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
