using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace UltrastarDJ.App.Services;

/// <summary>A transient notification shown in the DJ window's corner.</summary>
public sealed partial class Toast(string title, string? detail, string glyph, bool isWarning) : ObservableObject
{
    public string Title { get; } = title;
    public string? Detail { get; } = detail;
    public string Glyph { get; } = glyph;
    public bool IsWarning { get; } = isWarning;
    public bool HasDetail => !string.IsNullOrEmpty(Detail);
}

/// <summary>Blocking message shown as an overlay in the DJ window (validation errors, load failures).</summary>
public sealed record DialogMessage(string Title, string Message);

/// <summary>
/// UI notifications without any window reference: toasts auto-dismiss, the dialog waits for OK.
/// ViewModels call it; <c>DjWindowViewModel</c> renders it.
/// </summary>
public sealed class NotificationService
{
    private static readonly TimeSpan ToastLifetime = TimeSpan.FromSeconds(5);

    public ObservableCollection<Toast> Toasts { get; } = [];

    public DialogMessage? Dialog
    {
        get;
        private set
        {
            field = value;
            DialogChanged?.Invoke(value);
        }
    }

    public event Action<DialogMessage?>? DialogChanged;

    public void Info(string title, string? detail = null) => Show(new Toast(title, detail, "info", false));
    public void Warn(string title, string? detail = null) => Show(new Toast(title, detail, "warning", true));

    public void ShowDialog(string title, string message) => Dispatcher.UIThread.Post(() => Dialog = new DialogMessage(title, message));

    public void DismissDialog() => Dialog = null;

    public void Dismiss(Toast toast) => Toasts.Remove(toast);

    private void Show(Toast toast)
    {
        Dispatcher.UIThread.Post(() =>
        {
            Toasts.Add(toast);
            DispatcherTimer.RunOnce(() => Toasts.Remove(toast), ToastLifetime);
        });
    }
}
