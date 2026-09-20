using Avalonia.Data.Converters;

namespace UltrastarDJ.App.ViewModels;

public static class BoolConverters
{
    public static readonly IValueConverter OpenClosed =
        new FuncValueConverter<bool, string>(open => open ? "Open" : "Closed");
}
