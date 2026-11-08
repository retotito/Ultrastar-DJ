using CommunityToolkit.Mvvm.ComponentModel;
using UltrastarDJ.App.Library;
using UltrastarDJ.App.Services;

namespace UltrastarDJ.App.ViewModels;

/// <summary>Layout panel: which library columns are shown (Title always is) and whether broken / blacklisted songs are. Persisted in the app settings.</summary>
public sealed partial class LayoutPanelViewModel : ViewModelBase
{
    private readonly AppSettingsService _settings;

    [ObservableProperty] private bool _showBrokenSongs;
    [ObservableProperty] private bool _showBlacklistedSongs;

    public LayoutPanelViewModel(AppSettingsService settings)
    {
        _settings = settings;
        _showBrokenSongs = settings.ShowBrokenSongs;
        _showBlacklistedSongs = settings.ShowBlacklistedSongs;
        IReadOnlySet<LibraryColumn> visible = settings.VisibleColumns;
        Columns = [.. LibraryColumns.All.Select(c => new ColumnToggle(c, visible.Contains(c), settings))];
    }

    public IReadOnlyList<ColumnToggle> Columns { get; }

    partial void OnShowBrokenSongsChanged(bool value) => _settings.SetShowBrokenSongs(value);
    partial void OnShowBlacklistedSongsChanged(bool value) => _settings.SetShowBlacklistedSongs(value);
}

public sealed partial class ColumnToggle(LibraryColumn column, bool visible, AppSettingsService settings) : ObservableObject
{
    [ObservableProperty] private bool _isVisible = visible;

    public string Label => LibraryColumns.Label(column);

    partial void OnIsVisibleChanged(bool value) => settings.SetColumnVisible(column, value);
}
