using Avalonia.Controls;
using UltrastarDJ.App.ViewModels;

namespace UltrastarDJ.App.Views;

public sealed partial class SourcesPanelView : UserControl
{
    public SourcesPanelView()
    {
        InitializeComponent();
        // The folder picker needs the owning window; hand it over once we are in the tree.
        AttachedToVisualTree += (_, _) =>
        {
            if (DataContext is SourcesPanelViewModel vm)
            {
                vm.Owner = TopLevel.GetTopLevel(this);
            }
        };
    }
}
