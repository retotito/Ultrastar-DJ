using UltrastarDJ.App.Localization;
using System.ComponentModel;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using UltrastarDJ.Core.Localization;

namespace UltrastarDJ.App.Localization;

/// <summary>A UI language: its code (file name) and its name in itself, so everyone finds theirs in the list.</summary>
public sealed record UiLanguage(string Code, string NativeName)
{
    public override string ToString() => NativeName;
}

/// <summary>
/// The app's texts in the chosen language (docs/04-ui.md "Languages"). One JSON file per language in
/// <c>Languages/</c> next to the app — key → text, English (<c>en.json</c>) is the source and the fallback. XAML binds
/// through <see cref="T"/>: <c>{l:T settings.title}</c>; code calls <see cref="L.T"/> / <see cref="L.F"/>.
/// Switching raises "Item[]", so every bound text follows at once (no restart); texts code built earlier (a toast
/// already shown, a status line) change with their next update.
/// </summary>
public sealed class Translations : INotifyPropertyChanged
{
    public const string English = "en";
    /// <summary>The layout test language (developer mode): longer, accented, bracketed (<see cref="Pseudo"/>).</summary>
    public const string PseudoCode = "qps";

    public static IReadOnlyList<UiLanguage> Languages { get; } =
    [
        new("en", "English"),
        new("de", "Deutsch"),
        new("fr", "Français"),
        new("it", "Italiano"),
        new("es", "Español"),
        new("pt", "Português (Brasil)"),
        new("pl", "Polski"),
        new("zh", "中文（简体）"),
        new("ja", "日本語"),
        new("ko", "한국어"),
        new("th", "ภาษาไทย"),
        new("ru", "Русский"),
        new("hi", "हिन्दी"),
    ];

    /// <summary>What the language lists offer: the languages, plus the layout test language in developer builds.</summary>
    public static IReadOnlyList<UiLanguage> Choices { get; } =
#if DEBUG
        [.. Languages, new(PseudoCode, "Pseudo — layout test")];
#else
        Languages;
#endif

    public static Translations Instance { get; } = new();

    public UiLanguage Current => Choices.FirstOrDefault(l => l.Code == Code) ?? Languages[0];

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        // Accents, Chinese, Japanese and Thai stay readable in the file (not \u escapes).
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private Dictionary<string, string> _english = [];
    private Dictionary<string, string>? _current;
    private TextTable _table = new(new Dictionary<string, string>(), null);

    private Translations()
    {
        Folder = Path.Combine(AppContext.BaseDirectory, "Languages");
        // Core, Media and Infrastructure texts (song checks, error explanations, player names) read through here too.
        Core.Localization.Text.Lookup = key => _current?.GetValueOrDefault(key) is { } t && !string.IsNullOrWhiteSpace(t) ? t : _english.GetValueOrDefault(key);
        Core.Localization.Text.Culture = () => Culture;
        Reload();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>After a language switch or an edit, on the UI thread (for texts built in code).</summary>
    public event Action? Changed;

    /// <summary>Where the app reads the language files.</summary>
    public string Folder { get; }

    public string Code { get; private set; } = English;

    /// <summary>
    /// Numbers and dates: the system's own format when it speaks the chosen language (Swiss German keeps "28'777"),
    /// else the language's ("28 777" in French).
    /// </summary>
    public CultureInfo Culture => Code == PseudoCode || CultureInfo.CurrentCulture.TwoLetterISOLanguageName == Code
        ? CultureInfo.CurrentCulture
        : LanguageCulture();

    // The app runs with InvariantGlobalization (Directory.Build.props): there is no culture data for "de", "fr" …,
    // and asking for one throws. Then the neutral format (28,804) — never an exception inside a text.
    private CultureInfo LanguageCulture()
    {
        try
        {
            return CultureInfo.GetCultureInfo(Code, predefinedOnly: false);
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.InvariantCulture;
        }
    }

    public string this[string key] => _table.Get(key);

    /// <summary>The system's language if the app has it, else English — the first-start choice preselected.</summary>
    public static string SystemLanguage()
    {
        string two = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        return Languages.Any(l => l.Code == two) ? two : English;
    }

    public void SetLanguage(string code)
    {
        Code = code;
        Reload();
    }

    /// <summary>Reads the files again (after the translation editor saved) and refreshes every text.</summary>
    public void Reload()
    {
        _english = Read(English) ?? [];
        _current = Code switch
        {
            English => null,
            PseudoCode => _english.ToDictionary(kv => kv.Key, kv => Pseudo.Of(kv.Value)),
            _ => Read(Code),
        };
        _table = new TextTable(_english, _current);
        // Avalonia's indexer bindings listen for "Item" (CommonPropertyNames.IndexerName); WPF-style code for "Item[]".
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Code)));
        Changed?.Invoke();
    }

    /// <summary>Every key with its English text and its text in <paramref name="code"/> (null = not translated).</summary>
    public IReadOnlyList<(string Key, string English, string? Text)> Entries(string code)
    {
        Dictionary<string, string> other = code == English ? _english : Read(code) ?? [];
        return [.. _english.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => (kv.Key, kv.Value, other.GetValueOrDefault(kv.Key)))];
    }

    public Dictionary<string, string>? Read(string code)
    {
        string path = Path.Combine(Folder, code + ".json");
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path));
        }
        catch (JsonException)
        {
            // A broken file must not take the app down: its texts show in English.
            return null;
        }
    }

    /// <summary>Writes a language file, keys sorted (stable diffs), to every folder given.</summary>
    public static void Write(IReadOnlyDictionary<string, string> texts, string code, IEnumerable<string> folders)
    {
        SortedDictionary<string, string> sorted = new(texts.Where(kv => !string.IsNullOrWhiteSpace(kv.Value)).ToDictionary(), StringComparer.Ordinal);
        string json = JsonSerializer.Serialize(sorted, WriteOptions) + Environment.NewLine;
        foreach (string folder in folders)
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, code + ".json"), json);
        }
    }
}

/// <summary>Texts in code: <c>L.T("toast.output.lost")</c>, <c>L.F("library.count", shown, total)</c>.</summary>
public static class L
{
    public static string T(string key) => Translations.Instance[key];

    /// <summary>A text with {0} placeholders, numbers formatted for the language ("28'777", "28 777", "28,777").</summary>
    public static string F(string key, params object?[] args) => string.Format(Translations.Instance.Culture, Translations.Instance[key], args);
}
