using Avalonia.Controls;
using Avalonia.Controls.Templates;
using UltrastarDJ.App.ViewModels;

namespace UltrastarDJ.App;

/// <summary>Maps <c>FooViewModel</c> to <c>Views.FooView</c> by naming convention.</summary>
public sealed class ViewLocator : IDataTemplate
{
    public Control Build(object? param)
    {
        if (param is null)
        {
            return new TextBlock { Text = "(null)" };
        }

        string name = param.GetType().FullName!
            .Replace(".ViewModels.", ".Views.", StringComparison.Ordinal)
            .Replace("ViewModel", "View", StringComparison.Ordinal);
        Type? type = Type.GetType(name);

        return type is null
            ? new TextBlock { Text = "View not found: " + name }
            : (Control)Activator.CreateInstance(type)!;
    }

    public bool Match(object? data) => data is ViewModelBase;
}
