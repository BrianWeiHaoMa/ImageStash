using System.Globalization;
using SkiaSharp;

namespace ImageStash.Media;

/// <summary>
/// Media's one JPEG encoder (R4): a frame in the boundary pixel contract written as JPEG at <see cref="Quality"/>, the
/// one quality an edited JPEG's finished picture and its size estimate share (T2). JPEG holds no alpha: each pixel's
/// colour is written as it is, which matters only for a frame that is not opaque, and F3 writes JPEG only for an edited
/// JPEG, whose frames are opaque.
/// </summary>
public static class JpegEncoder
{
    private const string Action = "Write the picture as JPEG";

    /// <summary>
    /// T2's one quality. Windows 11's detailed forest-and-lake wallpaper <c>C:\Windows\Web\Wallpaper\ThemeC\img29.jpg</c>,
    /// re-encoded once at 90, measured 42.1 dB PSNR, a mean error under 1 of 255, at about the size it shipped at.
    /// </summary>
    public const int Quality = 90;

    /// <summary>
    /// The longest side the encoder writes, libjpeg-turbo's <c>JPEG_MAX_DIMENSION</c>, under the format's 65,535: asked
    /// for a longer side, SkiaSharp's encoder faults the process (S11), so such a frame is refused before it is reached (E4).
    /// </summary>
    public const int LargestSide = 65_500;

    private static readonly SKColorSpace Srgb = SKColorSpace.CreateSrgb();

    /// <summary>The JPEG bytes of <paramref name="frame"/>; refusals and failures name <paramref name="name"/> and are never thrown (G35).</summary>
    public static Outcome<byte[]> Encode(MediaFrame frame, string name)
    {
        if (frame.Width < 1 || frame.Height < 1 || frame.Pixels?.LongLength != 4L * frame.Width * frame.Height)
        {
            return new Failure(Action, name, "its pixels do not fill its size");
        }

        if (frame.Width > LargestSide || frame.Height > LargestSide)
        {
            return new Failure(Action, name, string.Create(CultureInfo.InvariantCulture, $"a side over {LargestSide:N0} pixels is more than the JPEG encoder can write"));
        }

        var noMemory = new Failure(Action, name, "there is not enough memory to write it");
        try
        {
            var info = new SKImageInfo(frame.Width, frame.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul, Srgb);
            using var image = SKImage.FromPixelCopy(info, frame.Pixels);
            using var encoded = image?.Encode(SKEncodedImageFormat.Jpeg, Quality);
            return encoded is null ? noMemory : Outcome<byte[]>.Of(encoded.ToArray());
        }
        catch (OutOfMemoryException)
        {
            // A picture the PC holds in memory (E4) may still leave no room for its JPEG beside it.
            return noMemory;
        }
    }
}
