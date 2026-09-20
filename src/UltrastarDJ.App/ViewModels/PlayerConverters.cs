using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Styling;

namespace UltrastarDJ.App.ViewModels;

public static class PlayerConverters
{
    /// <summary>Resolves a brush resource key (e.g. <c>BrushPlayer1</c>) from the application resources.</summary>
    public static readonly IValueConverter BrushByKey = new FuncValueConverter<string?, IBrush?>(key =>
    {
        if (key is null || Application.Current is not { } app)
        {
            return null;
        }

        return app.TryGetResource(key, ThemeVariant.Default, out object? value) ? value as IBrush : null;
    });

    public static readonly IValueConverter TestGlyph = new FuncValueConverter<bool, string>(on => on ? "stop" : "mic");
    public static readonly IValueConverter TestLabel = new FuncValueConverter<bool, string>(on => on ? "Stop test" : "Test mics");
    public static readonly IValueConverter MicTooltip = new FuncValueConverter<bool, string>(has => has ? "Sing on this screen" : "No microphone assigned");
}
