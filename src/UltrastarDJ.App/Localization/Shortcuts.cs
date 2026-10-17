using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using System.Text.RegularExpressions;

namespace UltrastarDJ.App.Localization;

/// <summary>
/// Keyboard shortcuts as people read them (docs/04-ui.md "Keyboard shortcuts"): written "Cmd+P" here, shown "⌘P" on
/// macOS and "Ctrl+P" on Windows and Linux. The command key itself is ⌘ on macOS, Ctrl elsewhere.
/// </summary>
public static class Shortcuts
{
    /// <summary>
    /// "Cmd+P" → "⌘P" / "Ctrl+P", "Shift+Q" → "⇧Q" / "Shift+Q". Key names as the keyboard says them in the app's
    /// language — "Strg" and "Leertaste" on a German one; macOS keeps its symbols (⌘ ⇧, Enter as ↩).
    /// </summary>
    public static string Show(string keys)
    {
        string text = Regex.Replace(keys, @"\bSpace\b", L.T("keys.space"));
        if (OperatingSystem.IsMacOS())
        {
            return Regex.Replace(text, @"\bEnter\b", "↩").Replace("Cmd+", "⌘", StringComparison.Ordinal).Replace("Shift+", "⇧", StringComparison.Ordinal);
        }

        return Regex.Replace(text, @"\bEnter\b", L.T("keys.enter"))
            .Replace("Cmd+", L.T("keys.ctrl") + "+", StringComparison.Ordinal)
            .Replace("Shift+", L.T("keys.shift") + "+", StringComparison.Ordinal);
    }

    /// <summary>The command key is held: ⌘ on macOS, Ctrl elsewhere (and not the other one).</summary>
    public static bool IsCommand(KeyModifiers m) => OperatingSystem.IsMacOS()
        ? m.HasFlag(KeyModifiers.Meta) && !m.HasFlag(KeyModifiers.Control)
        : m.HasFlag(KeyModifiers.Control) && !m.HasFlag(KeyModifiers.Meta);

    /// <summary>A tooltip text plus its shortcut in brackets: <c>Converter={x:Static l:Shortcuts.Append}, ConverterParameter=Cmd+P</c>.</summary>
    public static readonly IValueConverter Append = new FuncValueConverter<string?, string, string>(
        (text, keys) => string.IsNullOrEmpty(keys) ? text ?? "" : $"{text} ({Show(keys)})");
}

/// <summary>A translated tooltip with its shortcut: <c>ToolTip.Tip="{l:TK game_player.stop_show_scores, Keys=Cmd+E}"</c>.</summary>
public sealed class TK(string key) : MarkupExtension
{
    public string Key { get; } = key;
    public string Keys { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider)
        => new Binding($"[{Key}]") { Source = Translations.Instance, Mode = BindingMode.OneWay, Converter = Shortcuts.Append, ConverterParameter = Keys };
}
