using Avalonia;
using Avalonia.Controls;

namespace UltrastarDJ.App.Controls;

/// <summary>
/// A Material Symbols glyph. <see cref="Glyph"/> is the snake_case icon name (e.g. <c>mic_external_on</c>);
/// the font's ligature table turns it into the symbol. Names: https://fonts.google.com/icons
/// </summary>
public sealed class Icon : TextBlock
{
    public static readonly StyledProperty<string?> GlyphProperty =
        AvaloniaProperty.Register<Icon, string?>(nameof(Glyph));

    static Icon()
    {
        GlyphProperty.Changed.AddClassHandler<Icon>((icon, _) => icon.Text = icon.Glyph);
    }

    /// <summary>Material Symbols icon name.</summary>
    public string? Glyph
    {
        get => GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }
}
