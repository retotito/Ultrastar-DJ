using Avalonia.Controls;

namespace UltrastarDJ.App.Controls;

/// <summary>
/// A radio button that turns off again when clicked while on (a plain one stays on) — the duet popup's "nobody sings
/// this voice". Looks like a radio button; exclusivity is the view model's job, not a group's.
/// </summary>
public sealed class ToggleRadioButton : RadioButton
{
    protected override Type StyleKeyOverride => typeof(RadioButton);

    protected override void Toggle() => IsChecked = IsChecked != true;
}
