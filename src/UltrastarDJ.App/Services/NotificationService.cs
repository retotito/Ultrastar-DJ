using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace UltrastarDJ.App.Services;

public enum ToastKind
{
    Info,
    Success,
    Warning,
    /// <summary>A guest asked for a song on the songbook (light green, like the REQUESTS list).</summary>
    Request,
}

/// <summary>A transient notification shown in the DJ window's corner.</summary>
public sealed partial class Toast(string title, string? detail, ToastKind kind) : ObservableObject
{
    public string Title { get; } = title;
    public string? Detail { get; } = detail;
    public ToastKind Kind { get; } = kind;
    public bool IsWarning => Kind == ToastKind.Warning;
    public bool IsSuccess => Kind == ToastKind.Success;
    public bool IsRequest => Kind == ToastKind.Request;
    public string Glyph => Kind switch
    {
        ToastKind.Success => "check_circle",
        ToastKind.Warning => "warning",
        ToastKind.Request => "mic",
        _ => "info",
    };

    public bool HasDetail => !string.IsNullOrEmpty(Detail);
}

/// <summary>
/// Blocking message in the DJ window. <see cref="IsBug"/>: an unexpected exception — the dialog offers copying the
/// details and opening the log folder.
/// </summary>
public sealed record DialogMessage(string Title, IReadOnlyList<string> Reasons, string? Details = null, bool IsBug = false)
{
    public bool HasDetails => !string.IsNullOrEmpty(Details);
}

/// <summary>
/// The one place for user-facing messages (docs/04-ui.md "Errors and notifications"):
/// toasts for things that happened (auto-dismiss), an error dialog for actions the DJ started that failed,
/// a bug dialog for unexpected exceptions. ViewModels and services call it from any thread;
/// <c>DjWindowViewModel</c> renders it. Dialogs queue: a second one waits for OK on the first.
/// </summary>
public sealed class NotificationService
{
    private static readonly TimeSpan ToastLifetime = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan WarningLifetime = TimeSpan.FromSeconds(8);

    private readonly Queue<DialogMessage> _pending = [];

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

    public void Info(string title, string? detail = null) => Show(new Toast(title, detail, ToastKind.Info));
    public void Success(string title, string? detail = null) => Show(new Toast(title, detail, ToastKind.Success));
    public void Warn(string title, string? detail = null) => Show(new Toast(title, detail, ToastKind.Warning));
    public void Request(string title, string? detail = null) => Show(new Toast(title, detail, ToastKind.Request));

    /// <summary>An action the DJ started failed. <paramref name="reasons"/>: one plain sentence each.</summary>
    public void ShowError(string title, IEnumerable<string> reasons, string? details = null)
        => Enqueue(new DialogMessage(title, reasons.Where(r => !string.IsNullOrWhiteSpace(r)).ToList(), details));

    public void ShowError(string title, string reason, string? details = null) => ShowError(title, [reason], details);

    /// <summary>An exception nobody expected. The same message is not shown twice in a row.</summary>
    public void ShowBug(Exception ex, string context)
    {
        DialogMessage msg = new(
            "Something went wrong",
            [$"{context}: {ex.Message}", "The app keeps running. If this happens again, copy the details and report it."],
            ex.ToString(),
            IsBug: true);
        Dispatcher.UIThread.Post(() =>
        {
            if (Dialog?.Details == msg.Details || _pending.Any(p => p.Details == msg.Details))
            {
                return;
            }

            EnqueueOnUiThread(msg);
        });
    }

    public void DismissDialog() => Dialog = _pending.Count > 0 ? _pending.Dequeue() : null;

    public void Dismiss(Toast toast) => Toasts.Remove(toast);

    private void Enqueue(DialogMessage msg) => Dispatcher.UIThread.Post(() => EnqueueOnUiThread(msg));

    private void EnqueueOnUiThread(DialogMessage msg)
    {
        if (Dialog is null)
        {
            Dialog = msg;
        }
        else
        {
            _pending.Enqueue(msg);
        }
    }

    private void Show(Toast toast)
    {
        Dispatcher.UIThread.Post(() =>
        {
            Toasts.Add(toast);
            DispatcherTimer.RunOnce(() => Toasts.Remove(toast), toast.IsWarning ? WarningLifetime : ToastLifetime);
        });
    }
}
