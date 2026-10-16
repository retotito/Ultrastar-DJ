using System.Runtime.InteropServices;

namespace UltrastarDJ.Media;

/// <summary>
/// The native BGR0 buffer mpv's software renderer draws into, reallocated when the target size changes.
/// Only the owning render thread touches it.
/// </summary>
internal sealed unsafe class FrameBuffer : IDisposable
{
    public void* Pointer { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }
    public nuint Stride { get; private set; }

    public void EnsureSize(int width, int height)
    {
        if (Pointer is not null && width == Width && height == Height)
        {
            return;
        }

        Release();
        Width = width;
        Height = height;
        Stride = (nuint)(width * 4);
        Pointer = NativeMemory.AlignedAlloc(Stride * (nuint)height, 64);
    }

    private void Release()
    {
        if (Pointer is not null)
        {
            // AlignedAlloc's block must go back through AlignedFree: on Windows they are _aligned_malloc/_aligned_free
            // and the plain free corrupts the heap (macOS / Linux happen to accept either).
            NativeMemory.AlignedFree(Pointer);
            Pointer = null;
        }
    }

    public void Dispose() => Release();
}
