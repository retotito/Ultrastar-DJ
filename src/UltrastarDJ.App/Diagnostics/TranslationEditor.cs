// Developer builds only: not in the app that ships.
#if DEBUG
using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.VisualTree;
using UltrastarDJ.App.Localization;
using UltrastarDJ.Core.Localization;

namespace UltrastarDJ.App.Diagnostics;

/// <summary>
/// Developer tool (Settings → Developer → Edit translations, developer builds only): every text with its English and
/// its translation, searchable in all languages, missing ones and broken {0} placeholders marked. Save writes the
/// language file into the project (<c>src/UltrastarDJ.App/Languages</c>, to commit) and next to the running app, and
/// reloads — the app shows the change at once. "Cut-off texts" lists texts that do not fit their place in the open
/// windows; clicking one finds its translation.
/// </summary>
internal sealed class TranslationEditor : Window
{
    private static TranslationEditor? _open;

    private readonly ComboBox _language = new() { MinWidth = 200 };
    private readonly TextBox _search = new() { PlaceholderText = "Search key, English or translation", MinWidth = 320 };
    private readonly CheckBox _onlyProblems = new() { Content = "Only missing / placeholder problems" };
    private readonly TextBlock _status = new() { VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7 };
    private readonly ObservableCollection<Row> _rows = [];
    private readonly ListBox _list = new();
    private readonly ListBox _clipped = new() { MaxHeight = 160 };
    private List<Row> _all = [];
    private bool _dirty;

    public static void Open(Window owner)
    {
        if (_open is not null)
        {
            _open.Activate();
            return;
        }

        _open = new TranslationEditor();
        _open.Closed += (_, _) => _open = null;
        ((Window)_open).Show(owner);
    }

    private TranslationEditor()
    {
        Title = "Translations";
        Width = 1100;
        Height = 760;
        this[!BackgroundProperty] = this.GetResourceObservable("BrushSurfaceContainerLow").ToBinding();

        _language.ItemsSource = Translations.Languages;
        _language.SelectedItem = Translations.Languages.FirstOrDefault(l => l.Code == Translations.Instance.Code && l.Code != Translations.English)
            ?? Translations.Languages[1];
        _language.SelectionChanged += (_, _) => Load();
        _search.TextChanged += (_, _) => Filter();
        _onlyProblems.IsCheckedChanged += (_, _) => Filter();

        Button save = new() { Content = "Save" };
        save.Click += (_, _) => Save();
        Button show = new() { Content = "Show this language in the app" };
        show.Click += (_, _) =>
        {
            if (_language.SelectedItem is UiLanguage l)
            {
                Translations.Instance.SetLanguage(l.Code);
            }
        };
        Button clipped = new() { Content = "Cut-off texts" };
        clipped.Click += (_, _) => FindClipped();

        StackPanel bar = new() { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 0, 8) };
        bar.Children.Add(_language);
        bar.Children.Add(_search);
        bar.Children.Add(_onlyProblems);
        StackPanel actions = new() { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 0, 8) };
        actions.Children.Add(save);
        actions.Children.Add(show);
        actions.Children.Add(clipped);
        actions.Children.Add(_status);

        _list.ItemsSource = _rows;
        _list.ItemTemplate = new FuncDataTemplate<Row>((row, _) => RowView(row));
        _clipped.ItemTemplate = new FuncDataTemplate<Clipped>((c, _) => new TextBlock
        {
            Text = c is null ? "" : $"{c.Where}   needs {c.Needs:0} px, has {c.Has:0} px   “{c.Text}”",
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        _clipped.SelectionChanged += (_, _) =>
        {
            if (_clipped.SelectedItem is Clipped c)
            {
                _search.Text = c.Text.Length > 40 ? c.Text[..40] : c.Text;
            }
        };
        _clipped.IsVisible = false;

        DockPanel root = new() { Margin = new Thickness(12) };
        DockPanel.SetDock(bar, Dock.Top);
        DockPanel.SetDock(actions, Dock.Top);
        DockPanel.SetDock(_clipped, Dock.Bottom);
        root.Children.Add(bar);
        root.Children.Add(actions);
        root.Children.Add(_clipped);
        root.Children.Add(_list);
        Content = root;
        Load();

        Closing += (_, e) =>
        {
            if (_dirty)
            {
                Save();
            }
        };
    }

    private static Control RowView(Row? row)
    {
        if (row is null)
        {
            return new TextBlock();
        }

        TextBox edit = new() { Text = row.Text ?? "", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinWidth = 380 };
        edit.TextChanged += (_, _) => row.Owner.Changed(row, edit.Text ?? "");
        TextBlock problem = new() { Foreground = Brushes.IndianRed, FontSize = 12, Text = row.Problem, IsVisible = row.Problem.Length > 0 };
        row.ProblemChanged = p =>
        {
            problem.Text = p;
            problem.IsVisible = p.Length > 0;
        };

        Grid grid = new() { ColumnDefinitions = new ColumnDefinitions("260,*,*"), Margin = new Thickness(0, 2) };
        grid.Children.Add(new SelectableTextBlock { Text = row.Key, FontSize = 12, Opacity = 0.6, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 8, 0) });
        TextBlock english = new() { Text = row.English, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 12, 0) };
        Grid.SetColumn(english, 1);
        grid.Children.Add(english);
        StackPanel right = new() { Spacing = 2 };
        right.Children.Add(edit);
        right.Children.Add(problem);
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);
        return grid;
    }

    private string Code => (_language.SelectedItem as UiLanguage)?.Code ?? Translations.English;

    private void Load()
    {
        if (_dirty)
        {
            Save();
        }

        _all = [.. Translations.Instance.Entries(Code).Select(e => new Row(this, e.Key, e.English, Code == Translations.English ? e.English : e.Text))];
        Filter();
    }

    private void Filter()
    {
        string q = _search.Text?.Trim() ?? "";
        bool problems = _onlyProblems.IsChecked == true;
        _rows.Clear();
        foreach (Row r in _all.Where(r => (!problems || r.Problem.Length > 0)
            && (q.Length == 0 || r.Key.Contains(q, StringComparison.OrdinalIgnoreCase) || r.English.Contains(q, StringComparison.OrdinalIgnoreCase)
                || (r.Text?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false))))
        {
            _rows.Add(r);
        }

        int missing = _all.Count(r => string.IsNullOrWhiteSpace(r.Text));
        _status.Text = $"{_rows.Count} shown · {_all.Count - missing} of {_all.Count} translated" + (_dirty ? " · not saved" : "");
    }

    private void Changed(Row row, string text)
    {
        if (text == (row.Text ?? ""))
        {
            return;
        }

        row.Text = text;
        row.ProblemChanged?.Invoke(row.Problem);
        _dirty = true;
        _status.Text = "not saved";
    }

    private void Save()
    {
        Dictionary<string, string> texts = _all.Where(r => !string.IsNullOrWhiteSpace(r.Text)).ToDictionary(r => r.Key, r => r.Text!);
        List<string> folders = [Translations.Instance.Folder];
        if (ProjectFolder() is { } project)
        {
            folders.Add(project);
        }

        Translations.Write(texts, Code, folders);
        _dirty = false;
        Translations.Instance.Reload();
        _status.Text = $"Saved {texts.Count} texts to {string.Join(" and ", folders.Select(f => Path.GetFileName(Path.GetDirectoryName(f))))}/{Code}.json";
    }

    // The project's language folder, found from the running build (bin/Debug/net10.0 → …/src/UltrastarDJ.App/Languages).
    private static string? ProjectFolder()
    {
        for (DirectoryInfo? d = new(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            string candidate = Path.Combine(d.FullName, "src", "UltrastarDJ.App", "Languages");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    // Every visible single-line text in the app's windows that is wider than its place.
    private void FindClipped()
    {
        List<Clipped> found = [];
        if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            foreach (Window w in desktop.Windows.Where(w => w is not TranslationEditor and not UiInspectorWindow && w.IsVisible))
            {
                foreach (TextBlock tb in w.GetVisualDescendants().OfType<TextBlock>())
                {
                    if (tb.IsEffectivelyVisible && tb.TextWrapping == TextWrapping.NoWrap && tb.Text is { Length: > 0 } text && tb.Bounds.Width > 0)
                    {
                        double needs = new TextLayout(text, new Typeface(tb.FontFamily, tb.FontStyle, tb.FontWeight), tb.FontSize, null).Width
                            + tb.Padding.Left + tb.Padding.Right;
                        if (needs > tb.Bounds.Width + 1)
                        {
                            found.Add(new Clipped($"{w.Title} › {PathOf(tb)}", text, needs, tb.Bounds.Width));
                        }
                    }
                }
            }
        }

        _clipped.ItemsSource = found;
        _clipped.IsVisible = found.Count > 0;
        _status.Text = found.Count == 0 ? "No cut-off texts in the open windows" : $"{found.Count} cut-off texts — click one to find it";
    }

    private static string PathOf(Visual v)
    {
        List<string> parts = [];
        for (Visual? p = v.GetVisualParent(); p is not null && parts.Count < 3; p = p.GetVisualParent())
        {
            if (p is Control { Name: { Length: > 0 } n })
            {
                parts.Add(n);
            }
            else if (p is UserControl or Window)
            {
                parts.Add(p.GetType().Name);
            }
        }

        parts.Reverse();
        return parts.Count > 0 ? string.Join(" › ", parts) : v.GetType().Name;
    }

    private sealed class Row(TranslationEditor owner, string key, string english, string? text)
    {
        public TranslationEditor Owner { get; } = owner;
        public string Key { get; } = key;
        public string English { get; } = english;
        public string? Text { get; set; } = text;
        public Action<string>? ProblemChanged { get; set; }

        public string Problem => string.IsNullOrWhiteSpace(Text) ? "missing — shows in English"
            : !Placeholders.Match(English, Text) ? "placeholders differ from English ({0}, {1} …)"
            : "";
    }

    private sealed record Clipped(string Where, string Text, double Needs, double Has);
}
#endif
