using System.Globalization;
using Avalonia.Data.Converters;

namespace UltrastarDJ.App.ViewModels;

/// <summary>Converters for the sidebar's active-state class bindings.</summary>
public static class SidebarPanelConverters
{
    /// <summary>True when the bound <see cref="SidebarPanel"/> equals the panel named in the converter parameter.</summary>
    public static readonly IValueConverter Is = new FuncValueConverter<SidebarPanel, string?, bool>(
        (value, parameter) => parameter is not null && Enum.TryParse(parameter, out SidebarPanel p) && p == value);
}
