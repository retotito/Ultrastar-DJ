#if DEBUG
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Microsoft.Extensions.Logging;

namespace UltrastarDJ.App.Diagnostics;

/// <summary>
/// Debug-only stand-in for browser devtools (Avalonia's own Developer Tools are paid).
/// F12 in a window outlines the control under the mouse and opens the <see cref="UiInspectorWindow"/>, which shows
/// its type, name, classes, XAML file, DataContext, layout and look. Alt/Option+click pins a control (the click does
/// not reach the app), so the mouse can travel to the inspector window without changing the target; the next
/// Alt+click anywhere, or the window's Unpin button, releases it.
/// Shift+F12 (or "Dump tree") writes the window's visual tree to the logs folder with the target marked.
/// Popups (ComboBox drop-downs, flyouts) are separate top levels and not covered.
/// </summary>
internal static class UiInspector
{
    private static readonly ConditionalWeakTable<Window, UiInspectorOverlay> Overlays = new();
    private static readonly List<WeakReference<Window>> Inspected = [];
    private static string _dumpDir = "";
    private static ILogger? _log;
    private static UiInspectorWindow? _window;
    private static Control? _target;
    private static bool _pinned;

    public static void Attach(string dumpDir, ILogger log)
    {
        _dumpDir = dumpDir;
        _log = log;
        InputElement.KeyDownEvent.AddClassHandler<Window>(OnKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
        InputElement.PointerMovedEvent.AddClassHandler<Window>(OnPointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        InputElement.PointerPressedEvent.AddClassHandler<Window>(OnPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    private static void OnKeyDown(Window window, KeyEventArgs e)
    {
        if (e.Key != Key.F12 || window is UiInspectorWindow)
        {
            return;
        }

        e.Handled = true;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            Dump(window);
            return;
        }

        if (Overlays.TryGetValue(window, out _))
        {
            Disable(window);
        }
        else
        {
            Enable(window);
        }
    }

    private static void Enable(Window window)
    {
        if (AdornerLayer.GetAdornerLayer(window) is not { } layer)
        {
            _log?.LogWarning("Inspector: {Window} has no adorner layer", window.GetType().Name);
            return;
        }

        UiInspectorOverlay overlay = new();
        layer.Children.Add(overlay);
        Overlays.Add(window, overlay);
        Inspected.Add(new WeakReference<Window>(window));
        window.Closed += (_, _) => Disable(window);

        if (_window is null)
        {
            _window = new UiInspectorWindow(DumpTarget, Unpin);
            _window.Closed += (_, _) =>
            {
                _window = null;
                foreach (Window w in InspectedWindows())
                {
                    Disable(w);
                }
            };
            _window.Show();
        }

        Refresh();
    }

    private static void Disable(Window window)
    {
        if (Overlays.TryGetValue(window, out UiInspectorOverlay? overlay))
        {
            AdornerLayer.GetAdornerLayer(window)?.Children.Remove(overlay);
            Overlays.Remove(window);
        }

        Inspected.RemoveAll(r => !r.TryGetTarget(out Window? w) || ReferenceEquals(w, window));
        if (_target is not null && ReferenceEquals(TopLevel.GetTopLevel(_target), window))
        {
            _target = null;
            _pinned = false;
        }

        if (Inspected.Count == 0)
        {
            _window?.Close();
        }

        Refresh();
    }

    private static List<Window> InspectedWindows()
        => Inspected.Select(r => r.TryGetTarget(out Window? w) ? w : null).OfType<Window>().ToList();

    private static void OnPointerMoved(Window window, PointerEventArgs e)
    {
        if (!_pinned && Overlays.TryGetValue(window, out UiInspectorOverlay? overlay))
        {
            _target = HitTest(window, overlay, e.GetPosition(window));
            Refresh();
        }
    }

    private static void OnPointerPressed(Window window, PointerPressedEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Alt) || !Overlays.TryGetValue(window, out UiInspectorOverlay? overlay))
        {
            return;
        }

        // Alt+click pins, the next Alt+click anywhere releases; it must not press the button underneath.
        e.Handled = true;
        if (_pinned)
        {
            Unpin();
            return;
        }

        _target = HitTest(window, overlay, e.GetPosition(window));
        _pinned = _target is not null;
        Refresh();
    }

    private static void Unpin()
    {
        _pinned = false;
        Refresh();
    }

    private static Control? HitTest(Window window, UiInspectorOverlay overlay, Point p)
        => window.GetVisualsAt(p, v => v.IsVisible && v is not AdornerLayer && v != overlay).OfType<Control>().FirstOrDefault();

    private static void Refresh()
    {
        foreach (Window w in InspectedWindows())
        {
            if (Overlays.TryGetValue(w, out UiInspectorOverlay? overlay))
            {
                overlay.Show(_target is not null && ReferenceEquals(TopLevel.GetTopLevel(_target), w) ? _target : null, _pinned);
            }
        }

        _window?.Show(_target is null ? null : DescribeDetailed(_target), _pinned);
    }

    private static string? DumpTarget()
    {
        Window? window = (_target is null ? null : TopLevel.GetTopLevel(_target) as Window) ?? InspectedWindows().FirstOrDefault();
        return window is null ? null : Dump(window);
    }

    private static string Dump(Window window)
    {
        Control? marked = _target is not null && ReferenceEquals(TopLevel.GetTopLevel(_target), window) ? _target : null;

        StringBuilder sb = new();
        if (marked is not null)
        {
            sb.AppendLine("Inspected:");
            foreach (string line in DescribeDetailed(marked).Split('\n'))
            {
                sb.Append("  ").AppendLine(line);
            }

            sb.AppendLine();
        }

        sb.AppendLine("Visual tree:");
        DumpNode(window, 0, marked, sb);

        Directory.CreateDirectory(_dumpDir);
        string path = Path.Combine(_dumpDir, $"visual-tree-{window.GetType().Name}-{DateTime.Now:HHmmss}.txt");
        File.WriteAllText(path, sb.ToString());
        _log?.LogInformation("Inspector: visual tree written to {Path}", path);
        return path;
    }

    private static void DumpNode(Visual v, int depth, Control? marked, StringBuilder sb)
    {
        if (v is AdornerLayer)
        {
            return;
        }

        sb.Append(' ', depth * 2).Append(Headline(v));
        if (v is Control c)
        {
            sb.Append("  ").Append(Size(c.Bounds));
            if (c.DataContext is { } dc && (v.GetVisualParent() is not Control parent || !ReferenceEquals(parent.DataContext, dc)))
            {
                sb.Append("  DC=").Append(dc.GetType().Name);
            }
        }

        if (v is TextBlock { Text: { Length: > 0 } text })
        {
            sb.Append("  \"").Append(Shorten(text)).Append('"');
        }

        if (!v.IsVisible)
        {
            sb.Append("  (hidden)");
        }

        if (ReferenceEquals(v, marked))
        {
            sb.Append("   ◀ INSPECTED");
        }

        sb.AppendLine();
        foreach (Visual child in v.GetVisualChildren())
        {
            DumpNode(child, depth + 1, marked, sb);
        }
    }

    /// <summary>Everything the inspector window shows about a control, as plain text (also the head of a dump).</summary>
    public static string DescribeDetailed(Control c)
    {
        StringBuilder sb = new();
        sb.AppendLine(Headline(c));
        string pseudo = string.Join(' ', c.Classes.Where(x => x.StartsWith(':')));
        if (pseudo.Length > 0)
        {
            sb.Append("state  ").AppendLine(pseudo);
        }

        sb.AppendLine().AppendLine("SOURCE");
        // The nearest UserControl/Window is the XAML file this control is written in (x:Class = file name).
        Control? view = c.GetSelfAndVisualAncestors().OfType<Control>().FirstOrDefault(a => a is UserControl or Window);
        sb.Append("  file         ").AppendLine(view is null ? "—" : $"{view.GetType().Name}.axaml");
        if (c.TemplatedParent is Control tp && !ReferenceEquals(tp, c))
        {
            sb.Append("  template of  ").AppendLine(Headline(tp));
        }

        sb.Append("  DataContext  ").AppendLine(c.DataContext?.GetType().Name ?? "—");

        sb.AppendLine().AppendLine("LAYOUT");
        Point origin = c.TranslatePoint(default, (Visual?)TopLevel.GetTopLevel(c) ?? c) ?? default;
        sb.Append("  size         ").AppendLine(Size(c.Bounds));
        sb.Append("  position     ").AppendLine($"{N(origin.X)},{N(origin.Y)} in window");
        sb.Append("  margin       ").AppendLine(Thick(c.Margin));
        Thickness? padding = c switch
        {
            TemplatedControl t => t.Padding,
            Decorator d => d.Padding,
            TextBlock t => t.Padding,
            _ => null,
        };
        if (padding is { } pad)
        {
            sb.Append("  padding      ").AppendLine(Thick(pad));
        }

        sb.Append("  align        ").AppendLine($"H {c.HorizontalAlignment}  V {c.VerticalAlignment}");
        if (!double.IsNaN(c.Width) || !double.IsNaN(c.Height))
        {
            sb.Append("  set size     ").AppendLine($"{N(c.Width)} × {N(c.Height)}");
        }

        sb.Append("  state        ").AppendLine($"visible {Yes(c.IsEffectivelyVisible)}  enabled {Yes(c.IsEffectivelyEnabled)}  opacity {N(c.Opacity)}");

        (string? Text, double? Size, FontFamily? Family, FontWeight? Weight, IBrush? Fg, IBrush? Bg) look = c switch
        {
            TextBlock t => (t.Text, t.FontSize, t.FontFamily, t.FontWeight, t.Foreground, t.Background),
            TemplatedControl t => (null, t.FontSize, t.FontFamily, t.FontWeight, t.Foreground, t.Background),
            Panel p => (null, null, null, null, null, p.Background),
            Border b => (null, null, null, null, null, b.Background),
            _ => (null, null, null, null, null, null),
        };
        if (look.Text is not null || look.Size is not null || look.Fg is not null || look.Bg is not null)
        {
            sb.AppendLine().AppendLine("LOOK");
            if (!string.IsNullOrEmpty(look.Text))
            {
                sb.Append("  text         ").AppendLine($"\"{Shorten(look.Text)}\"");
            }

            if (look.Size is { } fs)
            {
                sb.Append("  font         ").AppendLine($"{look.Family?.Name} {N(fs)} {look.Weight}");
            }

            if (look.Fg is not null)
            {
                sb.Append("  foreground   ").AppendLine(BrushText(look.Fg));
            }

            if (look.Bg is not null)
            {
                sb.Append("  background   ").AppendLine(BrushText(look.Bg));
            }

            if (c is Border { CornerRadius: var r } && r != default)
            {
                sb.Append("  corners      ").AppendLine($"{N(r.TopLeft)},{N(r.TopRight)},{N(r.BottomRight)},{N(r.BottomLeft)}");
            }
        }

        sb.AppendLine().AppendLine("PATH");
        foreach (Control a in c.GetSelfAndVisualAncestors().OfType<Control>().Reverse())
        {
            sb.Append("  ").AppendLine(Headline(a));
        }

        return sb.ToString().TrimEnd();
    }

    private static string Headline(Visual v)
    {
        StringBuilder sb = new(v.GetType().Name);
        if (v is StyledElement s)
        {
            if (!string.IsNullOrEmpty(s.Name))
            {
                sb.Append(" #").Append(s.Name);
            }

            foreach (string cls in s.Classes.Where(x => !x.StartsWith(':')))
            {
                sb.Append(" .").Append(cls);
            }
        }

        return sb.ToString();
    }

    private static string BrushText(IBrush brush) => brush is ISolidColorBrush s ? s.Color.ToString() : brush.GetType().Name;
    private static string Size(Rect r) => $"{N(r.Width)}×{N(r.Height)}";
    private static string Thick(Thickness t) => $"{N(t.Left)},{N(t.Top)},{N(t.Right)},{N(t.Bottom)}";
    private static string N(double v) => double.IsNaN(v) ? "auto" : v.ToString("0.#", CultureInfo.InvariantCulture);
    private static string Yes(bool b) => b ? "yes" : "no";
    private static string Shorten(string s) => s.Length > 60 ? s[..60] + "…" : s.ReplaceLineEndings(" ");
}

/// <summary>Outlines the inspected control (margin dashed). Never hit-testable, so the app keeps working underneath.</summary>
internal sealed class UiInspectorOverlay : Control
{
    // Fixed devtools colours: must stand out on every theme and on the beamer's video. Pinned = orange.
    private static readonly IBrush Fill = new SolidColorBrush(Color.FromArgb(0x33, 0x4F, 0x8E, 0xF7));
    private static readonly IBrush PinnedFill = new SolidColorBrush(Color.FromArgb(0x33, 0xF7, 0xA6, 0x4F));
    private static readonly Pen Outline = new(new SolidColorBrush(Color.FromRgb(0x4F, 0x8E, 0xF7)), 1.5);
    private static readonly Pen PinnedOutline = new(new SolidColorBrush(Color.FromRgb(0xF7, 0xA6, 0x4F)), 2);
    private static readonly Pen Frame = new(new SolidColorBrush(Color.FromRgb(0x4F, 0x8E, 0xF7)), 3);
    private static readonly Pen MarginPen = new(new SolidColorBrush(Color.FromArgb(0xAA, 0xF7, 0xA6, 0x4F)), 1, DashStyle.Dash);

    private Control? _target;
    private bool _pinned;

    public UiInspectorOverlay()
    {
        IsHitTestVisible = false;
        ClipToBounds = false;
    }

    public void Show(Control? target, bool pinned)
    {
        _target = target;
        _pinned = pinned;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        context.DrawRectangle(null, Frame, new Rect(Bounds.Size).Deflate(1.5));
        if (_target is null || _target.TranslatePoint(default, this) is not { } origin)
        {
            return;
        }

        Rect rect = new(origin, _target.Bounds.Size);
        Thickness m = _target.Margin;
        if (m != default)
        {
            context.DrawRectangle(null, MarginPen, new Rect(rect.X - m.Left, rect.Y - m.Top, rect.Width + m.Left + m.Right, rect.Height + m.Top + m.Bottom));
        }

        context.DrawRectangle(_pinned ? PinnedFill : Fill, _pinned ? PinnedOutline : Outline, rect);
    }
}

/// <summary>
/// Separate, always-on-top window with the details of the inspected control. Text is selectable; Copy puts it on the
/// clipboard, Dump tree writes the tree file and shows its path.
/// </summary>
internal sealed class UiInspectorWindow : Window
{
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly SelectableTextBlock _details = new() { FontFamily = new FontFamily("Menlo, Consolas, monospace"), FontSize = 13 };
    private readonly Button _unpin = new() { Content = "Unpin", Margin = new Thickness(0, 0, 8, 0) };
    private string _text = "";

    public UiInspectorWindow(Func<string?> dump, Action unpin)
    {
        Title = "UI Inspector";
        Width = 560;
        Height = 680;
        Topmost = true;
        this[!BackgroundProperty] = this.GetResourceObservable("BrushSurfaceContainerLow").ToBinding();

        Button copy = new() { Content = "Copy", Margin = new Thickness(0, 0, 8, 0) };
        copy.Click += async (_, _) =>
        {
            if (Clipboard is { } clipboard && _text.Length > 0)
            {
                await clipboard.SetTextAsync(_text);
            }
        };
        Button dumpTree = new() { Content = "Dump tree" };
        dumpTree.Click += (_, _) =>
        {
            if (dump() is { } path)
            {
                _status.Text = "Tree written to " + path;
            }
        };

        DockPanel root = new() { Margin = new Thickness(12) };
        StackPanel header = new() { Spacing = 8, Margin = new Thickness(0, 0, 0, 12) };
        header.Children.Add(_status);
        _unpin.Click += (_, _) => unpin();
        StackPanel buttons = new() { Orientation = Orientation.Horizontal };
        buttons.Children.Add(_unpin);
        buttons.Children.Add(copy);
        buttons.Children.Add(dumpTree);
        header.Children.Add(buttons);
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        root.Children.Add(new ScrollViewer { Content = _details, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = root;
        Show(null, false);
    }

    public void Show(string? details, bool pinned)
    {
        _text = details ?? "";
        _details.Text = details ?? "Hover a control in a window with F12 on.";
        _unpin.IsVisible = pinned;
        _status.Text = pinned
            ? "📌 Pinned — Unpin, or Alt/Option+click anywhere, to follow the mouse again."
            : "Live — follows the mouse. Alt/Option+click pins a control. F12 in a window toggles it, Shift+F12 dumps its tree.";
    }
}
#endif
