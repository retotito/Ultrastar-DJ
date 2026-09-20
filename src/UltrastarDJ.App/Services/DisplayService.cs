using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using UltrastarDJ.App.ViewModels;
using UltrastarDJ.App.Views;
using UltrastarDJ.Core.Abstractions;
using UltrastarDJ.Core.Displays;

namespace UltrastarDJ.App.Services;

public sealed class DisplayService : IDisplayService
{
    private const string SettingsName = "displays";

    private readonly ISettingsStore _settings;
    private readonly AppOptions _options;
    private readonly MediaService _media;
    private readonly PlayersService _players;
    private readonly IServiceProvider _services;
    private readonly ILogger<DisplayService> _log;
    private readonly Dictionary<DisplayId, BeamerWindow> _open = [];
    private Window? _owner;
    private DisplaysDocument _doc;
    private IReadOnlyList<ScreenInfo> _screens = [];

    public DisplayService(ISettingsStore settings, AppOptions options, MediaService media, PlayersService players, IServiceProvider services, ILogger<DisplayService> log)
    {
        _settings = settings;
        _options = options;
        _media = media;
        _players = players;
        _services = services;
        _log = log;
        _doc = settings.Load(SettingsName, DisplaysDocument.Default());
    }

    public event Action<DisplayId, bool>? OpenStateChanged;

    public IReadOnlyList<ScreenInfo> Screens => _screens;

    /// <summary>Called once by the composition root; screens are enumerated relative to the DJ window.</summary>
    public void AttachOwner(Window owner)
    {
        _owner = owner;
        owner.Opened += (_, _) => RefreshScreens();
    }

    public void RefreshScreens()
    {
        Avalonia.Controls.Screens? screens = _owner?.Screens;
        if (screens is null)
        {
            _screens = [];
            return;
        }

        _screens = screens.All
            .Select((s, i) => new ScreenInfo(
                i,
                s.DisplayName ?? $"Screen {i + 1}",
                s.Bounds.Width,
                s.Bounds.Height,
                s.IsPrimary))
            .ToList();
        if (_log.IsEnabled(LogLevel.Debug))
        {
            _log.LogDebug("Screens: {Screens}", string.Join(" | ", _screens.Select(s => $"{s.Name} {s.WidthPx}x{s.HeightPx}{(s.IsPrimary ? " (primary)" : "")}")));
        }
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
    }

    public bool IsOpen(DisplayId id) => _open.ContainsKey(id);

    public void Open(DisplayId id, ScreenInfo screen)
    {
        if (_open.ContainsKey(id))
        {
            return;
        }

        Screen? target = _owner?.Screens.All.ElementAtOrDefault(screen.Index);
        // Resolved here, not injected: PlaybackService depends on IDisplayService (would be a constructor cycle).
        PlaybackService playback = _services.GetRequiredService<PlaybackService>();
        BeamerViewModel vm = new(id, _media.Game.Frames, playback, _players, this);
        BeamerWindow window = new()
        {
            DataContext = vm,
            Title = $"Ultrastar DJ — Beamer {(int)id}",
        };

        if (_options.BeamerDebug || target is null)
        {
            window.Width = 960;
            window.Height = 540;
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else
        {
            // Position on the target screen before going fullscreen so the OS fullscreens the right monitor.
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Position = target.Bounds.Position;
            window.Width = target.WorkingArea.Width / target.Scaling;
            window.Height = target.WorkingArea.Height / target.Scaling;
            window.WindowDecorations = WindowDecorations.None;
            window.Opened += (_, _) => window.WindowState = WindowState.FullScreen;
        }

        window.Closed += (_, _) =>
        {
            vm.Dispose();
            _open.Remove(id);
            _log.LogInformation("Beamer {Display} closed", (int)id);
            OpenStateChanged?.Invoke(id, false);
        };

        _open[id] = window;
        _doc = _doc.With(_doc.Get(id) with { ScreenName = screen.Name });
        _settings.Save(SettingsName, _doc);

        if (_owner is not null)
        {
            window.Show(_owner);
        }
        else
        {
            window.Show();
        }

        _log.LogInformation("Beamer {Display} opened on {Screen}", (int)id, screen.Name);
        OpenStateChanged?.Invoke(id, true);
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
