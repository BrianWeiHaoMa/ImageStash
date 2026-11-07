using ImageStash.Media;
using ImageStash.Tests.Support;
using SkiaSharp;

namespace ImageStash.Tests.Media;

/// <summary>
/// G35's entry: a file that ends before its format's own end (PNG's IEND, GIF's trailer, JPEG's end-of-image marker,
/// the length WebP's RIFF header declares) is not whole, wherever it was cut; bytes after the end are tolerated.
/// </summary>
[TestClass]
public sealed class TruncationCheckTests
{
    [TestMethod]
    [DynamicData(nameof(MediaFixtures.Whole), typeof(MediaFixtures))]
    public void AWholeFileIsWholeAndNamesItsFormat(string fixture, ImageFormat format)
    {
        Assert.AreEqual(format, WholeFormat(MediaFixtures.Bytes(fixture)));
    }

    [TestMethod]
    [DynamicData(nameof(MediaFixtures.Whole), typeof(MediaFixtures))]
    public void AFileCutAnywhereIsNotWhole(string fixture, ImageFormat format)
    {
        var bytes = MediaFixtures.Bytes(fixture);
        for (var length = 0; length < bytes.Length; length++)
        {
            Assert.IsNull(WholeFormat(bytes[..length]), $"{fixture} cut to {length} of {bytes.Length} bytes reads as a whole {format}");
        }
    }

    [TestMethod]
    [DynamicData(nameof(MediaFixtures.Whole), typeof(MediaFixtures))]
    public void BytesAfterTheEndAreTolerated(string fixture, ImageFormat format)
    {
        var bytes = MediaFixtures.Bytes(fixture).Concat(Enumerable.Repeat((byte)0xFF, 100)).ToArray();

        Assert.AreEqual(format, WholeFormat(bytes));
    }

    [TestMethod]
    public void AJpegWhoseScanHoldsEveryByteValueIsWholeOnlyAtItsEnd()
    {
        // Noise makes scan data full of escaped 0xFF bytes and restart-like pairs; the walk must not stop inside it.
        using var noise = new SKBitmap(new SKImageInfo(48, 40, SKColorType.Bgra8888, SKAlphaType.Opaque));
        var random = new Random(3);
        for (var y = 0; y < noise.Height; y++)
        {
            for (var x = 0; x < noise.Width; x++)
            {
                noise.SetPixel(x, y, new SKColor((uint)random.Next() | 0xFF000000));
            }
        }

        var bytes = noise.Encode(SKEncodedImageFormat.Jpeg, 100).ToArray();

        Assert.AreEqual(ImageFormat.Jpeg, WholeFormat(bytes));
        for (var length = 0; length < bytes.Length; length++)
        {
            Assert.IsNull(WholeFormat(bytes[..length]), $"cut to {length} of {bytes.Length} bytes");
        }
    }

    [TestMethod]
    public void BytesOfNoFormatAreNotWhole()
    {
        var bytes = new byte[512];
        new Random(5).NextBytes(bytes);

        Assert.IsNull(WholeFormat(bytes));
        Assert.IsNull(WholeFormat([]));
    }

    private static ImageFormat? WholeFormat(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        return TruncationCheck.WholeFormat(stream);
    }
}
