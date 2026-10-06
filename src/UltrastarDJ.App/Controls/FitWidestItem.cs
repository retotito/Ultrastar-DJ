using System.Collections;
using System.Collections.Specialized;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace UltrastarDJ.App.Controls;

/// <summary>
/// <c>c:FitWidestItem.IsEnabled="True"</c> on a ComboBox: its width is that of its widest item, measured from the
/// items' text. Without it, the box follows the selected text and the virtualised drop-down follows whichever
/// items are scrolled into view — both jump while browsing a long list (genres).
/// With a <c>Suffix</c> (the open list's song counts) only the open list gets the extra room; the closed box, which
/// shows just the name, stays as narrow as the longest name.
/// </summary>
public static class FitWidestItem
{
    // Template chrome around the text: left/right padding plus the drop-down arrow column.
    private const double ChromeWidth = 56;
    // Around an item's text in the open list: item padding plus room for the scrollbar.
    private const double ListChromeWidth = 44;

    private static readonly AttachedProperty<double> DropDownWidthProperty =
        AvaloniaProperty.RegisterAttached<ComboBox, double>("DropDownWidth", typeof(FitWidestItem));

    private static readonly AttachedProperty<Border?> PopupBorderProperty =
        AvaloniaProperty.RegisterAttached<ComboBox, Border?>("PopupBorder", typeof(FitWidestItem));

    /// <summary>Text appended to every item when measuring — room for what the item template adds (" (88,888)").</summary>
    public static readonly AttachedProperty<string?> SuffixProperty =
        AvaloniaProperty.RegisterAttached<ComboBox, string?>("Suffix", typeof(FitWidestItem));

    public static string? GetSuffix(ComboBox c) => c.GetValue(SuffixProperty);
    public static void SetSuffix(ComboBox c, string? value) => c.SetValue(SuffixProperty, value);

    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<ComboBox, bool>("IsEnabled", typeof(FitWidestItem));

    private static readonly AttachedProperty<INotifyCollectionChanged?> WatchedProperty =
        AvaloniaProperty.RegisterAttached<ComboBox, INotifyCollectionChanged?>("Watched", typeof(FitWidestItem));

    private static readonly AttachedProperty<double> BaseMinWidthProperty =
        AvaloniaProperty.RegisterAttached<ComboBox, double>("BaseMinWidth", typeof(FitWidestItem), double.NaN);

    static FitWidestItem()
    {
        IsEnabledProperty.Changed.AddClassHandler<ComboBox>((c, _) => Watch(c));
        ItemsControl.ItemsSourceProperty.Changed.AddClassHandler<ComboBox>((c, _) => Watch(c));
    }

    public static bool GetIsEnabled(ComboBox c) => c.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(ComboBox c, bool value) => c.SetValue(IsEnabledProperty, value);

    private static void Watch(ComboBox combo)
    {
        if (combo.GetValue(WatchedProperty) is { } old)
        {
            old.CollectionChanged -= OnItemsChanged;
            combo.SetValue(WatchedProperty, null);
        }

        if (!GetIsEnabled(combo))
        {
            return;
        }

        if (double.IsNaN(combo.GetValue(BaseMinWidthProperty)))
        {
            combo.SetValue(BaseMinWidthProperty, combo.MinWidth);
            combo.TemplateApplied += (_, e) =>
            {
                combo.SetValue(PopupBorderProperty, e.NameScope.Find<Border>("PopupBorder"));
                ApplyDropDownWidth(combo);
            };
            combo.DropDownOpened += (_, _) => ApplyDropDownWidth(combo);
        }

        if (combo.ItemsSource is INotifyCollectionChanged incc)
        {
            // The handler needs the combo: keep the pair in a table instead of a closure per subscription.
            Owners.AddOrUpdate(incc, combo);
            incc.CollectionChanged += OnItemsChanged;
            combo.SetValue(WatchedProperty, incc);
        }

        Fit(combo);
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<INotifyCollectionChanged, ComboBox> Owners = new();

    private static void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (sender is INotifyCollectionChanged incc && Owners.TryGetValue(incc, out ComboBox? combo))
        {
            Fit(combo);
        }
    }

    private static void Fit(ComboBox combo)
    {
        if (combo.ItemsSource is not IEnumerable items)
        {
            return;
        }

        Typeface face = new(combo.FontFamily, combo.FontStyle, combo.FontWeight);
        string? suffix = GetSuffix(combo);
        double widestName = 0;
        double widestEntry = 0;
        foreach (object? item in items)
        {
            if (item?.ToString() is { Length: > 0 } label)
            {
                widestName = Math.Max(widestName, Width(label, face, combo.FontSize));
                widestEntry = Math.Max(widestEntry, suffix is null ? 0 : Width(label + suffix, face, combo.FontSize));
            }
        }

        double baseMin = combo.GetValue(BaseMinWidthProperty);
        combo.MinWidth = Math.Max(double.IsNaN(baseMin) ? 0 : baseMin, Math.Ceiling(widestName + ChromeWidth));
        combo.MaxWidth = combo.MinWidth;
        combo.SetValue(DropDownWidthProperty, suffix is null ? 0 : Math.Ceiling(widestEntry + ListChromeWidth));
        ApplyDropDownWidth(combo);
    }

    private static double Width(string text, Typeface face, double size)
        => new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, face, size, null).WidthIncludingTrailingWhitespace;

    // The open list may be wider than the box (room for the counts); never narrower. The border's margin is the
    // shadow room around the list.
    private static void ApplyDropDownWidth(ComboBox combo)
    {
        double list = combo.GetValue(DropDownWidthProperty);
        if (combo.GetValue(PopupBorderProperty) is not { } border || list <= 0)
        {
            return;
        }

        border.MinWidth = Math.Max(combo.Bounds.Width, list + border.Margin.Left + border.Margin.Right);
    }
}
