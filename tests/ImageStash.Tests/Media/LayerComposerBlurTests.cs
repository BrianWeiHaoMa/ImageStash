using ImageStash.Layers;
using ImageStash.Media;
using ImageStash.Tests.Support;
using SkiaSharp;
using static ImageStash.Tests.Support.SyntheticFrames;

namespace ImageStash.Tests.Media;

[TestClass]
public sealed class LayerComposerBlurTests
{
    private const int FourKWidth = 3840, FourKHeight = 2160;
    private const string PictureName = "blur sample";
    private const double StrokeContrastLimit = 0.60;

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void BlurGrowsWithTheBoxAndTheOriginal()
    {
        var small = VerticalSplit(640);
        var large = VerticalSplit(3840);
        var narrow = new PixelRect(16, 20, 96, 12);
        var tall = narrow with { Height = 40 };
        var line = narrow with { Y = 25, Height = 1 };

        var narrowContrast = Contrast(Composed(small, Blur(narrow)), line);
        Assert.IsLessThan(narrowContrast, Contrast(Composed(small, Blur(tall)), line), "a taller box softens the same pixels further");
        Assert.IsLessThan(narrowContrast, Contrast(Composed(large, Blur(narrow)), line), "a larger original raises the floor for the same box");
    }

    [TestMethod]
    [DataRow(640, 40)]
    [DataRow(FourKWidth, 24)]
    public void CropAndShrinkUseTheOriginalGridAndSaveMatchesCompose(int width, int boxHeight)
    {
        using var files = new ScratchFolder();
        var sourcePath = files.File("original.png");
        WritePng(sourcePath, VerticalSplit(width));
        var original = MediaFixtures.FirstFrame(sourcePath);
        var box = new PixelRect(16, 20, 96, boxHeight);
        var hide = Blur(box);
        var crop = new PixelRect(8, 12, 112, 56);
        var clipped = new PixelRect(8, 24, 112, 16);
        var full = Saved(files, sourcePath, "full.png", hide);
        var cropped = Saved(files, sourcePath, "cropped.png", hide, new CropLayer(crop));
        var cutThrough = Saved(files, sourcePath, "clipped.png", hide, new CropLayer(clipped));
        var shrunk = Saved(files, sourcePath, "shrunk.png", hide, new ShrinkLayer(Shrink.ThreeQuarter));

        CollectionAssert.AreEqual(RectanglePixels(Composed(original, hide), box), RectanglePixels(full, box), "Compose and Save agree inside the hide");
        CollectionAssert.AreEqual(RectanglePixels(full, crop), cropped.Pixels, "a crop leaving the box whole gives the same PNG pixels");
        // The stripes are constant vertically, so removing some rows changes neither the clamp samples nor the expected strength.
        CollectionAssert.AreEqual(RectanglePixels(full, clipped), cutThrough.Pixels, "a crop cutting through the box still uses its original height");
        CollectionAssert.AreEqual(Scale(full, width * 3 / 4, 72).Pixels, shrunk.Pixels, "Shrink scales the already blurred pixels");
    }

    [TestMethod]
    [DataRow(160)]
    [DataRow(6400)]
    public void BlurLeavesNoMoreContrastThanThePreviousStrength(int width)
    {
        var original = VerticalSplit(width);
        PixelRect[] boxes = [new(63, 35, 1, 1), new(63, 35, 3, 3), new(48, 12, 32, 24), new(16, 12, 96, 60), new(0, 0, width, 96)];
        foreach (var box in boxes)
        {
            var previous = PreviousBlur(original, box);
            var current = Composed(original, Blur(box));
            Assert.IsLessThanOrEqualTo(Contrast(previous, box), Contrast(current, box), $"{width}-pixel original, {box.Width} x {box.Height} box");

            if (width == 6400)
            {
                CollectionAssert.AreEqual(previous.Pixels, current.Pixels, "the previous sigma of 48 exceeds the cap on box growth");
            }
        }
    }

    [TestMethod]
    public void BlurClampsAtItsRimAndLeavesEveryPixelOutsideAlone()
    {
        var box = new PixelRect(16, 20, 72, 56);
        var original = Paint(Pattern(1, 104, 96), box.X, box.Y, box.Width, box.Height, (224, 224, 224));
        var current = Composed(original, Blur(box));

        // The uniform interior meets varied colours outside. Reading across the rim or sampling transparency changes it.
        CollectionAssert.AreEqual(original.Pixels, current.Pixels, "the rim keeps its colour and opacity, and every outside pixel is untouched");
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void TheBlackBoxRemovesThePatternInEitherOrder(bool blackFirst)
    {
        var covered = new PixelRect(60, 20, 32, 48);
        var black = new HideLayer(covered, HideKind.Black, null);
        var blur = Blur(new(12, 12, 66, 72));
        var blank = Frame(108, 96, (_, _) => Gray(0));
        var original = Frame(blank.Width, blank.Height, (x, y) =>
            x >= covered.X && x < covered.X + covered.Width && y >= covered.Y && y < covered.Y + covered.Height && (x + y) % 4 < 2
                ? Gray(255) : Gray(0));
        Layer[] layers = blackFirst ? [black, blur] : [blur, black];

        CollectionAssert.AreEqual(blank.Pixels, Composed(original, layers).Pixels, "the covered pixels are black and no bright pattern spreads outside them");
    }

    [TestMethod]
    [DataRow(640, 360)]
    [DataRow(FourKWidth, FourKHeight)]
    public void BlurErasesStrokeScaleContrastFromLetters(int width, int height)
    {
        var (original, box) = Lettered(width, height);
        var previous = StrokeContrast(PreviousBlur(original, box), box);
        var current = StrokeContrast(Composed(original, Blur(box)), box);
        TestContext.WriteLine($"{width} x {height}, {box.Width} x {box.Height} hide: previous RMS {previous:F3}, new RMS {current:F3}");

        if (width == 640)
        {
            Assert.IsGreaterThan(StrokeContrastLimit, previous, "the previous sigma leaves stroke-scale contrast on the small original");
        }

        Assert.IsLessThan(StrokeContrastLimit, current, $"stroke-scale luminance RMS must be below {StrokeContrastLimit:F2} levels out of 255");
    }

    [TestMethod]
    public void BlurComposesAWholeOpaqueFourKFrame()
    {
        var original = Pattern(2, FourKWidth, FourKHeight);
        var composed = Composed(original, Blur(new(0, 0, FourKWidth, FourKHeight)));
        Assert.AreEqual((FourKWidth, FourKHeight), (composed.Width, composed.Height));
        Assert.AreNotEqual(Pixel(original, 1920, 1080), Pixel(composed, 1920, 1080));
        Assert.AreEqual((byte)255, MediaFixtures.Alpha(composed, 0, 0));
    }

    private static MediaFrame VerticalSplit(int width) => Frame(width, 96, (x, _) => Gray(x < 64 ? (byte)20 : (byte)240));

    private static (byte B, byte G, byte R) Gray(byte luminance) => (luminance, luminance, luminance);

    private static HideLayer Blur(PixelRect box) => new(box, HideKind.Blur, null);

    private static MediaFrame Composed(MediaFrame original, params Layer[] layers) =>
        Compositions.Compose(original, new LayerList(layers)).Frame;

    private static MediaFrame Saved(ScratchFolder files, string source, string file, params Layer[] layers)
    {
        var path = files.File(file);
        var rendered = FinishedPicture.Render(source, PictureName, new LayerList(layers), ImageFormat.Png, path, TestStash.ShortRetry);
        Assert.IsTrue(rendered.Succeeded, rendered.Failure?.Sentence);
        return MediaFixtures.FirstFrame(path);
    }

    private static void WritePng(string path, MediaFrame frame)
    {
        var encoded = PngEncoder.Encode(frame, PictureName);
        Assert.IsTrue(encoded.Succeeded, encoded.Failure?.Sentence);
        File.WriteAllBytes(path, encoded.Value!);
    }

    private static MediaFrame Scale(MediaFrame full, int width, int height)
    {
        using var surface = Compositions.Surface(width, height);
        using var srgb = SKColorSpace.CreateSrgb();
        using var image = SKImage.FromPixelCopy(new SKImageInfo(full.Width, full.Height, SKColorType.Bgra8888, SKAlphaType.Premul, srgb), full.Pixels);
        surface.Canvas.Scale(width / (float)full.Width, height / (float)full.Height);
        surface.Canvas.DrawImage(image, 0, 0, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear), null);
        return Compositions.ReadBack(surface);
    }

    private static byte[] RectanglePixels(MediaFrame frame, PixelRect rect)
    {
        var rowBytes = rect.Width * 4;
        var pixels = new byte[rowBytes * rect.Height];
        for (var y = 0; y < rect.Height; y++)
        {
            frame.Pixels.AsSpan(((rect.Y + y) * frame.Width + rect.X) * 4, rowBytes).CopyTo(pixels.AsSpan(y * rowBytes));
        }

        return pixels;
    }

    // The former compositor used just this sigma, with the same isolated, clamped input and source blend.
    private static MediaFrame PreviousBlur(MediaFrame original, PixelRect rect)
    {
        var sigma = 6f * Math.Max(1f, Math.Max(original.Width, original.Height) / 800f);
        using var srgb = SKColorSpace.CreateSrgb();
        using var image = SKImage.FromPixelCopy(new SKImageInfo(rect.Width, rect.Height, SKColorType.Bgra8888, SKAlphaType.Premul, srgb), RectanglePixels(original, rect));
        using var surface = Compositions.Surface(rect.Width, rect.Height);
        using var filter = SKImageFilter.CreateBlur(sigma, sigma, SKShaderTileMode.Clamp);
        using var paint = new SKPaint { ImageFilter = filter, BlendMode = SKBlendMode.Src };
        surface.Canvas.Clear(SKColors.Transparent);
        surface.Canvas.DrawImage(image, 0, 0, paint);

        var blurred = Compositions.ReadBack(surface);
        var pixels = (byte[])original.Pixels.Clone();
        var rowBytes = rect.Width * 4;
        for (var y = 0; y < rect.Height; y++)
        {
            blurred.Pixels.AsSpan(y * rowBytes, rowBytes).CopyTo(pixels.AsSpan(((rect.Y + y) * original.Width + rect.X) * 4));
        }

        return original with { Pixels = pixels };
    }

    // Standard deviation of grayscale luminance inside the same rectangle.
    private static double Contrast(MediaFrame frame, PixelRect rect)
    {
        var pixels = RectanglePixels(frame, rect);
        double sum = 0, squares = 0;
        for (var i = 0; i < pixels.Length; i += 4)
        {
            sum += pixels[i];
            squares += pixels[i] * pixels[i];
        }

        var count = rect.Width * rect.Height;
        return Math.Sqrt(squares / count - Math.Pow(sum / count, 2));
    }

    private static (MediaFrame Frame, PixelRect Box) Lettered(int width, int height)
    {
        const string word = "HIDDEN";
        using var typeface = SKFontManager.Default.MatchFamily("Arial", SKFontStyle.Bold);
        Assert.IsNotNull(typeface, "the Windows test runner has Arial");
        using var font = new SKFont(typeface, 46);
        using var ink = new SKPaint { Color = new SKColor(18, 18, 18), IsAntialias = true };
        font.MeasureText(word, out var bounds, ink);
        Assert.IsTrue(bounds.Height is >= 25 and <= 40, $"ink height is {bounds.Height} pixels");
        var inkWidth = (int)Math.Ceiling(bounds.Width);
        var inkHeight = (int)Math.Ceiling(bounds.Height);
        var left = (width - inkWidth) / 2;
        var top = (height - inkHeight) / 2;

        using var surface = Compositions.Surface(width, height);
        surface.Canvas.Clear(new SKColor(246, 246, 246));
        surface.Canvas.DrawText(word, left - bounds.Left, top - bounds.Top, SKTextAlign.Left, font, ink);
        return (Compositions.ReadBack(surface), new PixelRect(left - 2, top - 2, inkWidth + 4, inkHeight + 4));
    }

    // RMS of the grayscale pixel minus its 5 x 5 local mean, near the stroke width of these letters.
    private static double StrokeContrast(MediaFrame frame, PixelRect box)
    {
        double squares = 0;
        for (var y = box.Y + 2; y < box.Y + box.Height - 2; y++)
        {
            for (var x = box.X + 2; x < box.X + box.Width - 2; x++)
            {
                var residual = frame.Pixels[(y * frame.Width + x) * 4] - LocalMean(frame, x, y);
                squares += residual * residual;
            }
        }

        return Math.Sqrt(squares / ((box.Width - 4) * (box.Height - 4)));
    }

    private static double LocalMean(MediaFrame frame, int x, int y)
    {
        var sum = 0;
        for (var row = y - 2; row <= y + 2; row++)
        {
            for (var column = x - 2; column <= x + 2; column++)
            {
                sum += frame.Pixels[(row * frame.Width + column) * 4];
            }
        }

        return sum / 25d;
    }
}
