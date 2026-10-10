using System.Globalization;
using Avalonia;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Markup.Xaml;

namespace UltrastarDJ.App.Localization;

/// <summary>
/// A translated text in XAML: <c>Text="{l:T settings.title}"</c>. A binding to the key on <see cref="Translations"/>,
/// so it changes as soon as the language does.
/// </summary>
public sealed class T(string key) : MarkupExtension
{
    public string Key { get; } = key;

    public override object ProvideValue(IServiceProvider serviceProvider)
        => new Binding($"[{Key}]") { Source = Translations.Instance, Mode = BindingMode.OneWay };
}

/// <summary>
/// One of two translated texts by a bool: <c>Text="{l:Pick Testing, True=audio_input.stop, False=audio_input.test}"</c>.
/// Follows both the value and the language.
/// </summary>
public sealed class Pick(string path) : MarkupExtension
{
    public string Path { get; } = path;
    public string True { get; set; } = "";
    public string False { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider) => new MultiBinding
    {
        Bindings =
        {
            new Binding(Path),
            new Binding($"[{True}]") { Source = Translations.Instance },
            new Binding($"[{False}]") { Source = Translations.Instance },
        },
        Converter = new Multi(values => (values[0] is true ? values[1] : values[2]) as string ?? ""),
    };
}

/// <summary>
/// A value in a translated pattern: <c>Text="{l:Format Count, queue.title}"</c> with "QUEUE ({0})" in the language
/// file. Numbers in the language's format.
/// </summary>
public sealed class Format(string path, string key) : MarkupExtension
{
    public string Path { get; } = path;
    public string Key { get; } = key;

    public override object ProvideValue(IServiceProvider serviceProvider) => new MultiBinding
    {
        Bindings =
        {
            new Binding(Path),
            new Binding($"[{Key}]") { Source = Translations.Instance },
        },
        Converter = new Multi(values => values[1] is string pattern && values[0] is not null && values[0] != AvaloniaProperty.UnsetValue
            ? string.Format(Translations.Instance.Culture, pattern, values[0])
            : ""),
    };
}

/// <summary>
/// A value shown through the language file — enum entries, choices in a list:
/// <c>Text="{l:TOf ., Prefix=difficulty.}"</c> shows key "difficulty.Medium" for Medium.
/// </summary>
public sealed class TOf(string path) : MarkupExtension
{
    public string Path { get; } = path;
    public string Prefix { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider) => new MultiBinding
    {
        Bindings =
        {
            new Binding(Path),
            new Binding(nameof(Translations.Code)) { Source = Translations.Instance },
        },
        Converter = new Multi(values => values[0] is null || values[0] == AvaloniaProperty.UnsetValue ? "" : Translations.Instance[Prefix + values[0]]),
    };
}

/// <summary>
/// The converter behind <see cref="Pick"/>, <see cref="Format"/> and <see cref="TOf"/>: takes the raw values (null and
/// unset included — Avalonia's FuncMultiValueConverter drops the whole call when one value is null).
/// </summary>
internal sealed class Multi(Func<IList<object?>, string> convert) : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture) => convert(values);
}
