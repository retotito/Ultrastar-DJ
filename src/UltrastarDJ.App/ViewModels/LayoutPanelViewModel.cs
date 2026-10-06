using CommunityToolkit.Mvvm.ComponentModel;
using UltrastarDJ.App.Library;
using UltrastarDJ.App.Services;

namespace UltrastarDJ.App.ViewModels;

/// <summary>Layout panel: which library columns are shown (Title always is). Persisted in the app settings.</summary>
public sealed class LayoutPanelViewModel : ViewModelBase
{
    public LayoutPanelViewModel(AppSettingsService settings)
    {
        IReadOnlySet<LibraryColumn> visible = settings.VisibleColumns;
        Columns = [.. LibraryColumns.All.Select(c => new ColumnToggle(c, visible.Contains(c), settings))];
    }

    public IReadOnlyList<ColumnToggle> Columns { get; }
}

public sealed partial class ColumnToggle(LibraryColumn column, bool visible, AppSettingsService settings) : ObservableObject
{
    [ObservableProperty] private bool _isVisible = visible;

    public string Label => LibraryColumns.Label(column);

    partial void OnIsVisibleChanged(bool value) => settings.SetColumnVisible(column, value);
}
