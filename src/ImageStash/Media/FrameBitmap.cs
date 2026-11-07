using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace ImageStash.Media;

/// <summary>A frame in the boundary pixel contract as Avalonia draws it on screen, for the grid, the preview, a capture card and the capture overlay.</summary>
public static class FrameBitmap
{
    /// <summary>The frame's pixels copied row by row into a bitmap of its size, straight alpha kept (G6).</summary>
    public static Bitmap Of(MediaFrame frame)
    {
        var bitmap = new WriteableBitmap(new PixelSize(frame.Width, frame.Height), new Vector(96, 96), PixelFormats.Bgra8888, AlphaFormat.Unpremul);
        using var pixels = bitmap.Lock();
        var rowBytes = frame.Width * 4;
        for (var y = 0; y < frame.Height; y++)
        {
            Marshal.Copy(frame.Pixels, y * rowBytes, pixels.Address + y * pixels.RowBytes, rowBytes);
        }

        return bitmap;
    }
}
