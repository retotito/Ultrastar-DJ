using System.Globalization;
using Avalonia.Data.Converters;

namespace UltrastarDJ.App.ViewModels;

public static class BoolConverters
{
    public static readonly IValueConverter OpenClosed =
        new FuncValueConverter<bool, string>(open => open ? "Open" : "Closed");

    /// <summary>ConverterParameter <c>"whenTrue|whenFalse"</c>.</summary>
    public static readonly IValueConverter Text = new PickConverter();
    public static readonly IValueConverter Glyph = Text;

    private sealed class PickConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            string[] parts = (parameter as string ?? "|").Split('|', 2);
            return value is true ? parts[0] : parts.Length > 1 ? parts[1] : "";
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}
