using ImageStash.Media;
using ImageStash.Tests.Support;

namespace ImageStash.Tests.Media;

/// <summary>
/// Media's one PNG encoder (R4): a frame in the pixel contract written as a PNG that decodes to the same pixels, alpha
/// included; a frame that does not fill its size, or with a side past the encoder's bound, named, never thrown (G35,
/// E4); and no type but it and the one JPEG encoder that encodes a picture.
/// </summary>
[TestClass]
public sealed class PngEncoderTests
{
    [TestMethod]
    public void AFrameIsWrittenAsAPngThatDecodesToTheSamePixelsAlphaIncluded()
    {
        // BGRA, straight alpha: opaque red, half-transparent blue, fully transparent, opaque white, translucent green, opaque black.
        byte[] pixels = [0, 0, 255, 255, 255, 0, 0, 128, 0, 0, 0, 0, 255, 255, 255, 255, 0, 200, 0, 64, 0, 0, 0, 255];
        var encoded = PngEncoder.Encode(new MediaFrame(3, 2, 0, pixels), "pasted");

        Assert.IsTrue(encoded.Succeeded, encoded.Failure?.ToString());
        using var scratch = new ScratchFolder();
        var path = scratch.File("pasted.png");
        File.WriteAllBytes(path, encoded.Value!);
        var read = MediaDecoder.ReadFacts(path, "pasted", TestStash.ShortRetry);
        Assert.AreEqual(new MediaFacts(ImageFormat.Png, 3, 2, false, 1, 0), read.Value!.Facts);
        using var decoder = MediaDecoder.Open(path, "pasted", TestStash.ShortRetry).Value!;
        CollectionAssert.AreEqual(pixels, decoder.NextFrame(3).Value!.Pixels);
    }

    [TestMethod]
    public void AFrameWhosePixelsDoNotFillItsSizeIsNamedNotThrown()
    {
        var encoded = PngEncoder.Encode(new MediaFrame(3, 2, 0, new byte[20]), "pasted");

        Assert.AreEqual(new Failure("Write the picture as PNG", "pasted", "its pixels do not fill its size"), encoded.Failure);
    }

    [TestMethod]
    public void ASideTheEncoderCannotWriteIsRefusedByName()
    {
        var refused = new Failure("Write the picture as PNG", "tall", "a side over 1,000,000 pixels is more than the PNG encoder can write");

        Assert.AreEqual(refused, PngEncoder.Encode(new MediaFrame(1_000_001, 1, 0, new byte[4_000_004]), "tall").Failure);
        Assert.AreEqual(refused, PngEncoder.Encode(new MediaFrame(1, 1_000_001, 0, new byte[4_000_004]), "tall").Failure);

        var tallest = PngEncoder.Encode(new MediaFrame(1, 1_000_000, 0, new byte[4_000_000]), "tall");

        Assert.IsTrue(tallest.Succeeded, tallest.Failure?.ToString());
        using var scratch = new ScratchFolder();
        var path = scratch.File("tall.png");
        File.WriteAllBytes(path, tallest.Value!);
        Assert.AreEqual(new MediaFacts(ImageFormat.Png, 1, 1_000_000, false, 1, 0), MediaDecoder.ReadFacts(path, "tall", TestStash.ShortRetry).Value!.Facts);
    }

    [TestMethod]
    public void NoOtherTypeOfTheProductEncodesAPicture()
    {
        var encoders = ProductCalls.Methods()
            .Where(method => ProductCalls.CalledMethods(method).Any(called => called.DeclaringType?.Namespace == "SkiaSharp" && called.Name.StartsWith("Encode", StringComparison.Ordinal)))
            .Select(method => ProductCalls.Outermost(method.DeclaringType!).FullName)
            .Distinct()
            .ToList();

        CollectionAssert.AreEquivalent(new[] { typeof(PngEncoder).FullName, typeof(JpegEncoder).FullName }, encoders, string.Join(", ", encoders));
    }
}
