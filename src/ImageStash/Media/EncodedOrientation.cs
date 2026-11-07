using SkiaSharp;

namespace ImageStash.Media;

/// <summary>
/// A file's encoded origin, its EXIF orientation as the codec reports it, and the turn or mirror that brings the pixels
/// the codec decodes upright as the camera meant them (Composition, Media). The codec reports it for a JPEG and a WebP
/// and never applies it, and reads no PNG's eXIf, so a PNG shows as its pixels are (S11).
/// </summary>
internal static class EncodedOrientation
{
    /// <summary>The origin <paramref name="codec"/> reports, a value outside EXIF's eight read as upright rather than thrown (G35).</summary>
    public static SKEncodedOrigin Of(SKCodec codec)
    {
        try
        {
            var origin = codec.EncodedOrigin;
            return Enum.IsDefined(origin) ? origin : SKEncodedOrigin.TopLeft;
        }
        catch (Exception)
        {
            return SKEncodedOrigin.TopLeft;
        }
    }

    /// <summary>Whether the upright picture is the decoded one turned a quarter, its width the decoded height.</summary>
    public static bool SwapsSides(SKEncodedOrigin origin) =>
        origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;

    /// <summary>
    /// Writes <paramref name="source"/>, <paramref name="width"/> × <paramref name="height"/> pixels as decoded, rows
    /// <paramref name="sourceStride"/> pixels apart, into <paramref name="target"/> upright, rows
    /// <paramref name="targetStride"/> pixels apart: each pixel moved whole, none blended.
    /// </summary>
    public static void Turn(SKEncodedOrigin origin, ReadOnlySpan<uint> source, int width, int height, int sourceStride, Span<uint> target, int targetStride)
    {
        // Where the upright picture's top-left pixel comes from, and how far one step right and one step down move in the
        // decoded pixels.
        long stride = sourceStride, last = (height - 1) * stride, end = width - 1;
        var (start, right, down) = origin switch
        {
            SKEncodedOrigin.TopRight => (end, -1, stride),
            SKEncodedOrigin.BottomRight => (last + end, -1, -stride),
            SKEncodedOrigin.BottomLeft => (last, 1, -stride),
            SKEncodedOrigin.LeftTop => (0, stride, 1),
            SKEncodedOrigin.RightTop => (last, -stride, 1),
            SKEncodedOrigin.RightBottom => (last + end, -stride, -1),
            SKEncodedOrigin.LeftBottom => (end, stride, -1),
            _ => (0L, 1L, stride),
        };
        var (uprightWidth, uprightHeight) = SwapsSides(origin) ? (height, width) : (width, height);
        for (var y = 0; y < uprightHeight; y++)
        {
            var from = start + y * down;
            var row = target.Slice(y * targetStride, uprightWidth);
            for (var x = 0; x < row.Length; x++, from += right)
            {
                row[x] = source[(int)from];
            }
        }
    }
}
