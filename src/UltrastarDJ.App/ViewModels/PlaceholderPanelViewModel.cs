namespace UltrastarDJ.App.ViewModels;

/// <summary>Stand-in for panels that later sprints implement (sources, mics, outputs, settings).</summary>
public sealed class PlaceholderPanelViewModel : ViewModelBase
{
    public PlaceholderPanelViewModel(string title)
    {
        Title = title;
    }

    public string Title { get; }
}
