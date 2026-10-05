#if DEBUG
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using Microsoft.Extensions.Logging;

namespace UltrastarDJ.App.Diagnostics;

/// <summary>
/// Debug-only stand-in for browser devtools (Avalonia's own Developer Tools are paid).
/// F12 toggles an overlay that outlines the control under the mouse and shows its type, name, classes,
/// XAML file, DataContext and layout. Shift+F12 writes the window's visual tree to the logs folder,
/// with the hovered control marked, so it can be read outside the app.
/// Works in every <see cref="Window"/>; popups (ComboBox drop-downs, flyouts) are separate top levels and not covered.
/// </summary>
internal static class UiInspector
{
    private static readonly ConditionalWeakTable<Window, UiInspectorOverlay> Overlays = new();
    private static string _dumpDir = "";
    private static ILogger? _log;

    public static void Attach(string dumpDir, ILogger log)
    {
        _dumpDir = dumpDir;
        _log = log;
        InputElement.KeyDownEvent.AddClassHandler<Window>(OnKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
        InputElement.PointerMovedEvent.AddClassHandler<Window>(OnPointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    private static void OnKeyDown(Window window, KeyEventArgs e)
    {
        if (e.Key != Key.F12)
        {
            return;
        }

        e.Handled = true;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            Dump(window);
            return;
        }

        if (Overlays.TryGetValue(window, out UiInspectorOverlay? overlay))
        {
            AdornerLayer.GetAdornerLayer(window)?.Children.Remove(overlay);
            Overlays.Remove(window);
            return;
        }

        AdornerLayer? layer = AdornerLayer.GetAdornerLayer(window);
        if (layer is null)
        {
            _log?.LogWarning("Inspector: {Window} has no adorner layer", window.GetType().Name);
            return;
        }

        overlay = new UiInspectorOverlay();
        layer.Children.Add(overlay);
        Overlays.Add(window, overlay);
    }

    private static void OnPointerMoved(Window window, PointerEventArgs e)
    {
        if (!Overlays.TryGetValue(window, out UiInspectorOverlay? overlay))
        {
            return;
        }

        Point p = e.GetPosition(window);
        Control? target = window.GetVisualsAt(p, v => v.IsVisible && v is not AdornerLayer && v != overlay)
            .OfType<Control>()
            .FirstOrDefault();
        overlay.Show(target, e.GetPosition(overlay));
    }

    private static void Dump(Window window)
    {
        Overlays.TryGetValue(window, out UiInspectorOverlay? overlay);
        Control? hovered = overlay?.Target;

        StringBuilder sb = new();
        if (hovered is not null)
        {
            sb.AppendLine("Hovered:");
            foreach (string line in Describe(hovered))
            {
                sb.Append("  ").AppendLine(line);
            }

            sb.AppendLine();
        }

        sb.AppendLine("Visual tree:");
        DumpNode(window, 0, hovered, sb);

        Directory.CreateDirectory(_dumpDir);
        string path = Path.Combine(_dumpDir, $"visual-tree-{window.GetType().Name}-{DateTime.Now:HHmmss}.txt");
        File.WriteAllText(path, sb.ToString());
        _log?.LogInformation("Inspector: visual tree written to {Path}", path);
    }

    private static void DumpNode(Visual v, int depth, Control? hovered, StringBuilder sb)
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

        if (ReferenceEquals(v, hovered))
        {
            sb.Append("   ◀ HOVERED");
        }

        sb.AppendLine();
        foreach (Visual child in v.GetVisualChildren())
        {
            DumpNode(child, depth + 1, hovered, sb);
        }
    }

    /// <summary>Lines shown in the overlay box and at the top of a dump.</summary>
    public static IReadOnlyList<string> Describe(Control c)
    {
        List<string> lines = [Headline(c)];

        // The nearest UserControl/Window is the XAML file this control is written in (x:Class = file name).
        Control? view = c.GetSelfAndVisualAncestors().OfType<Control>().FirstOrDefault(a => a is UserControl or Window);
        string where = view is null ? "" : $"in {view.GetType().Name}.axaml";
        if (c.TemplatedParent is Control tp && !ReferenceEquals(tp, c))
        {
            where += $"  (template part of {tp.GetType().Name})";
        }

        lines.Add(where.Trim());
        lines.Add("DataContext: " + (c.DataContext?.GetType().Name ?? "—"));

        Point origin = c.TranslatePoint(default, (Visual?)TopLevel.GetTopLevel(c) ?? c) ?? default;
        StringBuilder layout = new($"size {Size(c.Bounds)}  at {N(origin.X)},{N(origin.Y)}");
        if (c.Margin != default)
        {
            layout.Append("  margin ").Append(Thick(c.Margin));
        }

        Thickness? padding = c switch
        {
            TemplatedControl t => t.Padding,
            Decorator d => d.Padding,
            TextBlock t => t.Padding,
            _ => null,
        };
        if (padding is { } pad && pad != default)
        {
            layout.Append("  padding ").Append(Thick(pad));
        }

        lines.Add(layout.ToString());

        (double? fontSize, IBrush? fg, IBrush? bg) = c switch
        {
            TextBlock t => (t.FontSize, t.Foreground, t.Background),
            TemplatedControl t => (t.FontSize, t.Foreground, t.Background),
            Panel p => ((double?)null, (IBrush?)null, p.Background),
            Border b => (null, null, b.Background),
            _ => (null, null, null),
        };
        StringBuilder look = new();
        if (c is TextBlock { Text: { Length: > 0 } text })
        {
            look.Append('"').Append(Shorten(text)).Append("\"  ");
        }

        if (fontSize is { } fs)
        {
            look.Append("font ").Append(N(fs)).Append("  ");
        }

        if (fg is not null)
        {
            look.Append("fg ").Append(BrushText(fg)).Append("  ");
        }

        if (bg is not null)
        {
            look.Append("bg ").Append(BrushText(bg));
        }

        if (look.Length > 0)
        {
            lines.Add(look.ToString().Trim());
        }

        List<string> path = c.GetSelfAndVisualAncestors()
            .OfType<Control>()
            .Where(a => a is UserControl or Window || !string.IsNullOrEmpty(a.Name) || ReferenceEquals(a, c))
            .Select(a => string.IsNullOrEmpty(a.Name) ? a.GetType().Name : $"{a.GetType().Name}#{a.Name}")
            .Reverse()
            .ToList();
        lines.Add(string.Join(" › ", path));
        return lines;
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
    private static string N(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);
    private static string Shorten(string s) => s.Length > 40 ? s[..40] + "…" : s.ReplaceLineEndings(" ");
}

/// <summary>Draws the outline and info box. Never hit-testable, so the app keeps working underneath.</summary>
internal sealed class UiInspectorOverlay : Control
{
    // Fixed devtools colours: must stand out on every theme and on the beamer's video.
    private static readonly IBrush Fill = new SolidColorBrush(Color.FromArgb(0x33, 0x4F, 0x8E, 0xF7));
    private static readonly Pen Outline = new Pen(new SolidColorBrush(Color.FromRgb(0x4F, 0x8E, 0xF7)), 1.5);
    private static readonly Pen MarginPen = new Pen(new SolidColorBrush(Color.FromArgb(0xAA, 0xF7, 0xA6, 0x4F)), 1, DashStyle.Dash);
    private static readonly IBrush BoxBackground = new SolidColorBrush(Color.FromArgb(0xEE, 0x10, 0x10, 0x14));
    private static readonly IBrush BoxText = Brushes.White;
    private static readonly IBrush BoxHeadline = new SolidColorBrush(Color.FromRgb(0x9C, 0xC2, 0xFF));
    private static readonly Typeface Mono = new("Menlo, Consolas, monospace");

    private Point _pointer;

    public UiInspectorOverlay()
    {
        IsHitTestVisible = false;
        ClipToBounds = false;
    }

    public Control? Target { get; private set; }

    public void Show(Control? target, Point pointer)
    {
        Target = target;
        _pointer = pointer;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        Rect area = new(Bounds.Size);
        context.DrawRectangle(null, new Pen(Outline.Brush, 3), area.Deflate(1.5));

        if (Target is null || Target.TranslatePoint(default, this) is not { } origin)
        {
            DrawBox(context, ["Inspector on — hover a control.  F12 off · Shift+F12 dump tree"], new Point(8, 8), area);
            return;
        }

        Rect rect = new(origin, Target.Bounds.Size);
        Thickness m = Target.Margin;
        if (m != default)
        {
            context.DrawRectangle(null, MarginPen, new Rect(rect.X - m.Left, rect.Y - m.Top, rect.Width + m.Left + m.Right, rect.Height + m.Top + m.Bottom));
        }

        context.DrawRectangle(Fill, Outline, rect);
        DrawBox(context, UiInspector.Describe(Target), _pointer + new Point(16, 20), area);
    }

    private static void DrawBox(DrawingContext context, IReadOnlyList<string> lines, Point at, Rect area)
    {
        const double pad = 8;
        List<FormattedText> texts = lines
            .Where(l => l.Length > 0)
            .Select((l, i) => new FormattedText(l, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, 12, i == 0 ? BoxHeadline : BoxText))
            .ToList();
        double w = texts.Max(t => t.Width) + 2 * pad;
        double h = texts.Sum(t => t.Height) + 2 * pad;

        // Keep the box inside the window: flip to the other side of the pointer when it would overflow.
        double x = at.X + w > area.Right ? Math.Max(area.X, at.X - w - 32) : at.X;
        double y = at.Y + h > area.Bottom ? Math.Max(area.Y, at.Y - h - 40) : at.Y;

        context.DrawRectangle(BoxBackground, null, new RoundedRect(new Rect(x, y, w, h), 6));
        double ty = y + pad;
        foreach (FormattedText t in texts)
        {
            context.DrawText(t, new Point(x + pad, ty));
            ty += t.Height;
        }
    }
}
#endif
