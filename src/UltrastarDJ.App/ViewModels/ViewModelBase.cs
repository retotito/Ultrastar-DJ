using CommunityToolkit.Mvvm.ComponentModel;

namespace UltrastarDJ.App.ViewModels;

/// <summary>Marker base for the <see cref="ViewLocator"/>. ViewModels must not reference Avalonia types.</summary>
public abstract class ViewModelBase : ObservableObject
{
}
