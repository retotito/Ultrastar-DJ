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
/// </summary>
public static class FitWidestItem
{
    // Template chrome around the text: left/right padding plus the drop-down arrow column.
    private const double ChromeWidth = 56;

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
        double widest = 0;
        foreach (object? item in items)
        {
            if (item?.ToString() is { Length: > 0 } text)
            {
                FormattedText ft = new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, face, combo.FontSize, null);
                widest = Math.Max(widest, ft.WidthIncludingTrailingWhitespace);
            }
        }

        double baseMin = combo.GetValue(BaseMinWidthProperty);
        combo.MinWidth = Math.Max(double.IsNaN(baseMin) ? 0 : baseMin, Math.Ceiling(widest + ChromeWidth));
        combo.MaxWidth = combo.MinWidth;
    }
}
