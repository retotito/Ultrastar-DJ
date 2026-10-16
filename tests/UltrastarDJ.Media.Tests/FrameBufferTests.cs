namespace UltrastarDJ.Media.Tests;

public class FrameBufferTests
{
    /// <summary>
    /// The first frame renders at 1280×720 before mpv knows the video size, then the buffer shrinks to it. On Windows
    /// an aligned block released with the plain free corrupts the heap (0xC0000374) and kills the process here.
    /// </summary>
    [Fact]
    public unsafe void EnsureSize_Reallocates_AndReleasesTheOldBlock()
    {
        using FrameBuffer buffer = new();
        buffer.EnsureSize(1280, 720);
        void* first = buffer.Pointer;
        new Span<byte>(buffer.Pointer, (int)(buffer.Stride * (nuint)buffer.Height)).Fill(0xFF);

        buffer.EnsureSize(1280, 720);
        Assert.True(buffer.Pointer == first);

        for (int i = 0; i < 50; i++)
        {
            buffer.EnsureSize(640 + i * 2, 336);
            new Span<byte>(buffer.Pointer, (int)(buffer.Stride * (nuint)buffer.Height)).Fill(0x7F);
        }

        Assert.Equal(738, buffer.Width);
        Assert.Equal((nuint)(738 * 4), buffer.Stride);
    }
}
