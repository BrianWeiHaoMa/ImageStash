using ImageStash.Layers;
using ImageStash.Media;
using SkiaSharp;

namespace ImageStash.Tests.Support;

/// <summary>
/// Media's one drawing routine run as the render runs it: onto a fresh surface of the output size (times a scale the
/// check sets on the canvas first, as the editor scales it), read back in the boundary pixel contract, with the font
/// families the routine said it drew in a fallback.
/// </summary>
public static class Compositions
{
    public static (MediaFrame Frame, IReadOnlyList<string> Replaced) Compose(MediaFrame original, LayerList layers, int frameIndex = 0, int scale = 1)
    {
        var size = LayerComposer.OutputSize(original.Width, original.Height, layers);
        using var surface = Surface(size.Width * scale, size.Height * scale);
        surface.Canvas.Clear(SKColors.Transparent);
        surface.Canvas.Scale(scale);

        var drawn = LayerComposer.Compose(surface.Canvas, original, layers, frameIndex, "the audacity");

        Assert.IsTrue(drawn.Succeeded, drawn.Failure?.Sentence);
        return (ReadBack(surface), drawn.Value!);
    }

    public static SKSurface Surface(int width, int height) =>
        SKSurface.Create(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul, SKColorSpace.CreateSrgb()));

    public static MediaFrame ReadBack(SKSurface surface)
    {
        var info = surface.Canvas.DeviceClipBounds;
        using var bitmap = new SKBitmap(new SKImageInfo(info.Width, info.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul, SKColorSpace.CreateSrgb()));
        Assert.IsTrue(surface.ReadPixels(bitmap.Info, bitmap.GetPixels(), bitmap.RowBytes, 0, 0));
        return new MediaFrame(info.Width, info.Height, 0, bitmap.Bytes);
    }
}
