using System.Reflection;
using ImageStash.Media;
using ImageStash.Tests.Support;
using SkiaSharp;

namespace ImageStash.Tests.Media;

/// <summary>
/// Media's one JPEG encoder (R4): a frame in the pixel contract written as a JPEG that decodes close to it, at the one
/// quality Save and the estimate share (T2), the same bytes every time; a side the encoder cannot write refused before
/// SkiaSharp is reached (E4, S11) and a frame that does not fill its size named, never thrown (G35).
/// </summary>
[TestClass]
public sealed class JpegEncoderTests
{
    private const string Action = "Write the picture as JPEG";

    private const int W = 64, H = 48;

    [TestMethod]
    public void AFrameIsWrittenAsAJpegThatDecodesCloseToIt()
    {
        // Measured at quality 90: the pattern's square centres within 8 of their colour (1.3 on average), every channel of
        // the gradient within 6 (0.8 on average). The pattern's edges ring further, up to 132, because JPEG halves the
        // chroma and the squares' saturated colours change at every edge, which a photograph's colours do not.
        var pattern = SyntheticFrames.Pattern(3, W, H);
        var gradient = SyntheticFrames.Frame(W, H, (x, y) => ((byte)(x * 4), (byte)(y * 5), (byte)(255 - (x + y) * 2)));

        var squares = Errors(pattern, Written(pattern), (x, y) => x % 8 == 4 && y % 8 == 4);
        var smooth = Errors(gradient, Written(gradient), (_, _) => true);

        Assert.IsLessThanOrEqualTo(12, squares.Max(), "every square keeps its colour, channels in their order");
        Assert.IsLessThanOrEqualTo(2, squares.Average());
        Assert.IsLessThanOrEqualTo(10, smooth.Max());
        Assert.IsLessThanOrEqualTo(1.5, smooth.Average());
    }

    [TestMethod]
    public void TheBytesAreSkiaSharpsJpegAtTheOneQualityTheSameEveryTime()
    {
        var source = SyntheticFrames.Noise(seed: 4, colours: 500, W, H);

        var first = JpegEncoder.Encode(source, "noise").Value!;
        var second = JpegEncoder.Encode(source, "noise").Value!;

        CollectionAssert.AreEqual(first, second);
        var info = new SKImageInfo(W, H, SKColorType.Bgra8888, SKAlphaType.Unpremul, SKColorSpace.CreateSrgb());
        using var image = SKImage.FromPixelCopy(info, source.Pixels);
        using var atQuality = image.Encode(SKEncodedImageFormat.Jpeg, JpegEncoder.Quality);
        CollectionAssert.AreEqual(atQuality.ToArray(), first, "Save and the estimate read one quality (T2)");
        var quality = typeof(JpegEncoder).GetField(nameof(JpegEncoder.Quality), BindingFlags.Public | BindingFlags.Static)!;
        Assert.IsTrue(quality.IsLiteral && quality.FieldType == typeof(int), "one public const int");
    }

    [TestMethod]
    public void ASideTheEncoderCannotWriteIsRefusedByNameBeforeItIsReached()
    {
        var refused = new Failure(Action, "long", "a side over 65,500 pixels is more than the JPEG encoder can write");

        // Asked for such a side, SkiaSharp's encoder faults the process: these return only because it is never reached.
        Assert.AreEqual(refused, JpegEncoder.Encode(Solid(65_501, 1), "long").Failure);
        Assert.AreEqual(refused, JpegEncoder.Encode(Solid(1, 65_501), "long").Failure);

        var widest = JpegEncoder.Encode(Solid(65_500, 1), "long");

        Assert.IsTrue(widest.Succeeded, widest.Failure?.ToString());
        using var scratch = new ScratchFolder();
        var path = scratch.File("long.jpg");
        File.WriteAllBytes(path, widest.Value!);
        Assert.AreEqual(new MediaFacts(ImageFormat.Jpeg, 65_500, 1, false, 1, 0), MediaDecoder.ReadFacts(path, "long", TestStash.ShortRetry).Value!.Facts);
    }

    [TestMethod]
    public void AFrameWhosePixelsDoNotFillItsSizeIsNamedNotThrown()
    {
        var notFilled = new Failure(Action, "pasted", "its pixels do not fill its size");

        Assert.AreEqual(notFilled, JpegEncoder.Encode(new MediaFrame(3, 2, 0, new byte[20]), "pasted").Failure);
        Assert.AreEqual(notFilled, JpegEncoder.Encode(new MediaFrame(0, 2, 0, []), "pasted").Failure);
        Assert.AreEqual(notFilled, JpegEncoder.Encode(new MediaFrame(65_501, 1, 0, new byte[4]), "pasted").Failure);
    }

    private static MediaFrame Solid(int width, int height) => SyntheticFrames.Frame(width, height, (_, _) => (40, 120, 200));

    /// <summary>The frame <see cref="JpegEncoder"/> writes for <paramref name="source"/>, read back as a JPEG of its size, opaque.</summary>
    private static MediaFrame Written(MediaFrame source)
    {
        var encoded = JpegEncoder.Encode(source, "the audacity");
        Assert.IsTrue(encoded.Succeeded, encoded.Failure?.ToString());
        using var scratch = new ScratchFolder();
        var path = scratch.File("the audacity.jpg");
        File.WriteAllBytes(path, encoded.Value!);
        Assert.AreEqual(new MediaFacts(ImageFormat.Jpeg, source.Width, source.Height, false, 1, 0), MediaDecoder.ReadFacts(path, "the audacity", TestStash.ShortRetry).Value!.Facts);
        var decoded = MediaFixtures.FirstFrame(path);
        Assert.IsTrue(Enumerable.Range(0, W * H).All(i => decoded.Pixels[i * 4 + 3] == 255), "a JPEG is opaque");
        return decoded;
    }

    /// <summary>The colour channels' differences between two frames at the pixels <paramref name="where"/> admits.</summary>
    private static List<int> Errors(MediaFrame source, MediaFrame decoded, Func<int, int, bool> where) =>
        Enumerable.Range(0, W * H).Where(i => where(i % W, i / W)).SelectMany(i => Enumerable.Range(i * 4, 3)).Select(c => Math.Abs(decoded.Pixels[c] - source.Pixels[c])).ToList();
}
