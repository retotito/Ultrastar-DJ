using CommunityToolkit.Mvvm.ComponentModel;
using UltrastarDJ.App.Localization;

namespace UltrastarDJ.App.ViewModels;

/// <summary>
/// Marker base for the <see cref="ViewLocator"/>. ViewModels must not reference Avalonia types.
/// A language switch re-reads every property (texts built in code follow at once, like the bound ones). The
/// subscription is weak: transient view models are not kept alive by the language.
/// </summary>
public abstract class ViewModelBase : ObservableObject
{
    protected ViewModelBase()
    {
        WeakReference<ViewModelBase> self = new(this);
        void OnLanguage()
        {
            if (self.TryGetTarget(out ViewModelBase? vm))
            {
                vm.OnPropertyChanged(string.Empty);
            }
            else
            {
                Translations.Instance.Changed -= OnLanguage;
            }
        }

        Translations.Instance.Changed += OnLanguage;
    }
}
