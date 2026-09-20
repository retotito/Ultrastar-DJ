using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using UltrastarDJ.Media;

namespace UltrastarDJ.App.Controls;

/// <summary>
/// Draws frames from an <see cref="IFrameSource"/>, letterboxed. Frames arrive on the source's
/// render thread and are copied into a <see cref="WriteableBitmap"/> there; the UI thread only blits.
/// Several surfaces may share one source (DJ monitor + beamers).
/// </summary>
// The bitmap is released when the control leaves the visual tree; controls are not IDisposable in Avalonia.
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable")]
public sealed class VideoSurface : Control
{
    public static readonly StyledProperty<IFrameSource?> SourceProperty =
        AvaloniaProperty.Register<VideoSurface, IFrameSource?>(nameof(Source));

    private readonly object _bitmapLock = new();
    private WriteableBitmap? _bitmap;
    private int _pendingInvalidate;

    static VideoSurface()
    {
        SourceProperty.Changed.AddClassHandler<VideoSurface>((s, e) => s.OnSourceChanged(e));
        AffectsRender<VideoSurface>(SourceProperty);
    }

    public IFrameSource? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    private void OnSourceChanged(AvaloniaPropertyChangedEventArgs e)
    {
        if (e.OldValue is IFrameSource old)
        {
            old.FrameReady -= OnFrame;
        }

        if (e.NewValue is IFrameSource next)
        {
            next.FrameReady += OnFrame;
        }

        lock (_bitmapLock)
        {
            _bitmap?.Dispose();
            _bitmap = null;
        }

        InvalidateVisual();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (Source is { } src)
        {
            src.FrameReady -= OnFrame;
        }

        lock (_bitmapLock)
        {
            _bitmap?.Dispose();
            _bitmap = null;
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (Source is { } src)
        {
            src.FrameReady -= OnFrame;
            src.FrameReady += OnFrame;
        }
    }

    // Render thread of the media player.
    private unsafe void OnFrame(FrameRef frame)
    {
        lock (_bitmapLock)
        {
            if (_bitmap is null || _bitmap.PixelSize.Width != frame.Width || _bitmap.PixelSize.Height != frame.Height)
            {
                _bitmap?.Dispose();
                _bitmap = new WriteableBitmap(new PixelSize(frame.Width, frame.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
            }

            using ILockedFramebuffer fb = _bitmap.Lock();
            byte* src = (byte*)frame.Data;
            byte* dst = (byte*)fb.Address;
            int rowBytes = frame.Width * 4;
            if (fb.RowBytes == frame.Stride)
            {
                Buffer.MemoryCopy(src, dst, (long)fb.RowBytes * frame.Height, (long)frame.Stride * frame.Height);
            }
            else
            {
                for (int y = 0; y < frame.Height; y++)
                {
                    Buffer.MemoryCopy(src + (long)y * frame.Stride, dst + (long)y * fb.RowBytes, rowBytes, rowBytes);
                }
            }
        }

        // Coalesce: one invalidate per UI frame no matter how many video frames arrive.
        if (Interlocked.Exchange(ref _pendingInvalidate, 1) == 0)
        {
            Dispatcher.UIThread.Post(() =>
            {
                Interlocked.Exchange(ref _pendingInvalidate, 0);
                InvalidateVisual();
            }, DispatcherPriority.Render);
        }
    }

    public override void Render(DrawingContext context)
    {
        Rect bounds = Bounds.WithX(0).WithY(0);
        context.FillRectangle(Brushes.Black, bounds);

        lock (_bitmapLock)
        {
            if (_bitmap is null)
            {
                return;
            }

            Size size = _bitmap.Size;
            double scale = Math.Min(bounds.Width / size.Width, bounds.Height / size.Height);
            double w = size.Width * scale;
            double h = size.Height * scale;
            Rect dest = new((bounds.Width - w) / 2, (bounds.Height - h) / 2, w, h);
            context.DrawImage(_bitmap, new Rect(size), dest);
        }
    }
}
