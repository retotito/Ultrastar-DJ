using Avalonia.Controls;
using UltrastarDJ.App.ViewModels;

namespace UltrastarDJ.App.Views;

public sealed partial class SettingsPanelView : UserControl
{
    public SettingsPanelView()
    {
        InitializeComponent();
        // The backup file pickers need the owning window; hand it over once we are in the tree.
        AttachedToVisualTree += (_, _) =>
        {
            if (DataContext is SettingsPanelViewModel vm)
            {
                vm.Owner = TopLevel.GetTopLevel(this);
            }
        };
    }

    // The Developer card is hidden in Release (BuildInfo.IsDeveloper) and the editor is not compiled in there.
    private void OnEditTranslations(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
#if DEBUG
        if (TopLevel.GetTopLevel(this) is Window owner)
        {
            Diagnostics.TranslationEditor.Open(owner);
        }
#endif
    }
}
