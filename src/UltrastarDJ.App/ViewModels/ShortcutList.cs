using UltrastarDJ.App.Localization;

namespace UltrastarDJ.App.ViewModels;

public sealed record ShortcutRow(string Keys, string Text);

public sealed record ShortcutGroup(string Title, IReadOnlyList<ShortcutRow> Rows);

/// <summary>
/// Every keyboard shortcut, for the overview ("?", the status bar's Shortcuts, the macOS app menu). The keys are handled
/// in DjWindow, LibraryView and BeamerWindow; this list is what the DJ reads — keep the two in step.
/// </summary>
public static class ShortcutList
{
    public static IReadOnlyList<ShortcutGroup> Groups =>
    [
        Group("shortcuts.group_search",
            ("/  ·  Cmd+F", "shortcuts.search"),
            ("↓  ·  Enter", "shortcuts.search_to_list"),
            ("Esc", "shortcuts.search_clear")),
        Group("shortcuts.group_song",
            ("↑ ↓", "shortcuts.song_move"),
            ("Enter", "shortcuts.song_preview"),
            ("G", "shortcuts.song_game"),
            ("Q", "shortcuts.song_queue"),
            ("I", "shortcuts.song_details"),
            ("H", "shortcuts.song_favourite")),
        Group("shortcuts.group_preview",
            ("Space", "shortcuts.preview_play"),
            ("Shift+↑  ·  Shift+↓", "shortcuts.preview_volume"),
            ("Shift+Q", "shortcuts.preview_queue"),
            ("Shift+G", "shortcuts.preview_game")),
        Group("shortcuts.group_game",
            ("Cmd+P", "shortcuts.game_play"),
            ("Cmd+E", "shortcuts.game_stop"),
            ("Cmd+S", "shortcuts.game_home"),
            ("Cmd+G", "shortcuts.game_ready"),
            ("Cmd+↑  ·  Cmd+↓", "shortcuts.game_volume")),
        Group("shortcuts.group_panels",
            ("1 – 6", "shortcuts.panels"),
            ("7", "shortcuts.settings"),
            ("Esc", "shortcuts.close"),
            ("?", "shortcuts.overview")),
        Group("shortcuts.group_single",
            ("Space", "shortcuts.single_pause"),
            ("Esc", "shortcuts.single_esc"),
            ("↑ ↓", "shortcuts.single_volume")),
        Group("shortcuts.group_display",
            ("F", "shortcuts.display_fullscreen"),
            ("Esc", "shortcuts.display_leave"),
            ("Cmd+P  ·  Cmd+E", "shortcuts.display_game")),
    ];

    private static ShortcutGroup Group(string title, params (string Keys, string Text)[] rows)
        => new(L.T(title), [.. rows.Select(r => new ShortcutRow(Shortcuts.Show(r.Keys), L.T(r.Text)))]);
}
