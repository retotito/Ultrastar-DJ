namespace UltrastarDJ.Media;

/// <summary>
/// One decoded video frame, BGRA32 top-down. The buffer belongs to the source and is only valid
/// during the <see cref="IFrameSource.FrameReady"/> callback — subscribers must copy inside it.
/// </summary>
public readonly record struct FrameRef(nint Data, int Width, int Height, int Stride);

/// <summary>Anything that produces frames: a player's render output or the <see cref="FrameBus"/> fan-out.</summary>
public interface IFrameSource
{
    /// <summary>Raised on the source's render thread. Copy the pixels inside the handler; never store the pointer.</summary>
    event Action<FrameRef>? FrameReady;
}

/// <summary>
/// Fans one <see cref="IFrameSource"/> out to any number of surfaces (DJ monitor, beamer 1, beamer 2).
/// The upstream source can be swapped when a new song loads; subscribers stay attached.
/// </summary>
public sealed class FrameBus : IFrameSource, IDisposable
{
    private IFrameSource? _upstream;

    public event Action<FrameRef>? FrameReady;

    /// <summary>Raised on the UI-agnostic caller thread when the upstream is replaced or removed (surfaces clear).</summary>
    public event Action? SourceChanged;

    public bool HasSource => _upstream is not null;

    public void SetSource(IFrameSource? source)
    {
        if (ReferenceEquals(_upstream, source))
        {
            return;
        }

        if (_upstream is not null)
        {
            _upstream.FrameReady -= OnFrame;
        }

        _upstream = source;
        if (_upstream is not null)
        {
            _upstream.FrameReady += OnFrame;
        }

        SourceChanged?.Invoke();
    }

    private void OnFrame(FrameRef frame) => FrameReady?.Invoke(frame);

    public void Dispose() => SetSource(null);
}
