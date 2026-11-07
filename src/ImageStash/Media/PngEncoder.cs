using System.Globalization;
using SkiaSharp;

namespace ImageStash.Media;

/// <summary>
/// Media's one PNG encoder (R4): a frame in the boundary pixel contract written losslessly as PNG, its straight alpha
/// and its sRGB kept, for a still's clipboard form and a pasted picture alike.
/// </summary>
public static class PngEncoder
{
    private const string Action = "Write the picture as PNG";

    /// <summary>
    /// The longest side the encoder writes, libpng's default user limit in SkiaSharp, far under the format's 2,147,483,647:
    /// asked for a longer side, SkiaSharp's encoder writes nothing, so such a frame is refused by name before it is
    /// reached (E4).
    /// </summary>
    public const int LargestSide = 1_000_000;

    private static readonly SKColorSpace Srgb = SKColorSpace.CreateSrgb();

    /// <summary>The PNG bytes of <paramref name="frame"/>; refusals and failures name <paramref name="name"/> and are never thrown (G35).</summary>
    public static Outcome<byte[]> Encode(MediaFrame frame, string name)
    {
        if (frame.Width < 1 || frame.Height < 1 || frame.Pixels.LongLength != 4L * frame.Width * frame.Height)
        {
            return new Failure(Action, name, "its pixels do not fill its size");
        }

        if (frame.Width > LargestSide || frame.Height > LargestSide)
        {
            return new Failure(Action, name, string.Create(CultureInfo.InvariantCulture, $"a side over {LargestSide:N0} pixels is more than the PNG encoder can write"));
        }

        var noMemory = new Failure(Action, name, "there is not enough memory to write it");
        try
        {
            var info = new SKImageInfo(frame.Width, frame.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul, Srgb);
            using var image = SKImage.FromPixelCopy(info, frame.Pixels);
            using var encoded = image?.Encode(SKEncodedImageFormat.Png, 100);
            return encoded is null ? noMemory : Outcome<byte[]>.Of(encoded.ToArray());
        }
        catch (OutOfMemoryException)
        {
            // A picture the PC holds in memory (E4) may still leave no room for its PNG beside it.
            return noMemory;
        }
    }
}
