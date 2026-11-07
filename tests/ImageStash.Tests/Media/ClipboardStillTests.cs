using System.Runtime.InteropServices;
using ImageStash.Files;
using ImageStash.Media;
using ImageStash.Tests.Support;
using SkiaSharp;

namespace ImageStash.Tests.Media;

/// <summary>
/// A still's clipboard forms (G8): a PNG's own bytes, or any other still's picture encoded as PNG without loss; and its
/// frame at full size with its transparency; every failure named, never thrown (G35).
/// </summary>
[TestClass]
public sealed class ClipboardStillTests
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    [TestMethod]
    public void APngGoesAsItsOwnBytesWithItsFrameAtFullSizeAndItsTransparency()
    {
        var still = Read("transparent.png");

        CollectionAssert.AreEqual(MediaFixtures.Bytes("transparent.png"), still.Png, "a PNG's clipboard bytes are its file's");
        Assert.AreEqual((8, 6), (still.Frame.Width, still.Frame.Height));
        Assert.AreEqual(128, MediaFixtures.Alpha(still.Frame, 1, 1), "the half-transparent pixel keeps its alpha");
        Assert.AreEqual(255, MediaFixtures.Alpha(still.Frame, 0, 0));
    }

    [TestMethod]
    public void AFullyTransparentPixelStaysTransparent()
    {
        var still = Read("invisible-blue.png");

        Assert.AreEqual(255, MediaFixtures.Alpha(still.Frame, 0, 0));
        Assert.AreEqual(0, MediaFixtures.Alpha(still.Frame, 1, 0));
    }

    [TestMethod]
    [DataRow("photo.jpg")]
    [DataRow("still.gif")]
    [DataRow("still.webp")]
    [DataRow("transparent.webp")]
    public void EveryOtherStillGoesAsItsPictureEncodedAsPngWithoutLoss(string name)
    {
        var still = Read(name);

        CollectionAssert.AreEqual(PngSignature, still.Png.Take(8).ToArray());
        Assert.AreEqual((8, 6), (still.Frame.Width, still.Frame.Height), "at full size");
        CollectionAssert.AreEqual(still.Frame.Pixels, Decoded(still.Png), "the PNG holds the frame's pixels exactly, alpha included");
    }

    [TestMethod]
    public void AStillWebPKeepsItsTransparencyInBothForms()
    {
        var still = Read("transparent.webp");

        Assert.AreEqual(128, MediaFixtures.Alpha(still.Frame, 1, 1));
        using var codec = SKCodec.Create(new SKMemoryStream(still.Png));
        Assert.AreEqual(SKAlphaType.Unpremul, codec.Info.AlphaType, "the PNG carries an alpha channel");
    }

    [TestMethod]
    public void AFileThatDoesNotDecodeIsUnreadableByItsName()
    {
        using var scratch = new ScratchFolder();
        var path = Path.Combine(scratch.Path, "broken.png");
        File.WriteAllBytes(path, MediaFixtures.Bytes("transparent.png")[..40]);

        var read = ClipboardStill.Read(path, "broken", TestStash.ShortRetry);

        Assert.IsFalse(read.Succeeded);
        Assert.AreEqual(("Read the picture", "broken"), (read.Failure.Action, read.Failure.Subject));
        Assert.IsTrue(MediaDecoder.IsUnreadable(read.Failure));
    }

    [TestMethod]
    public void AMissingFileIsAFailureByItsNameNotAnException()
    {
        using var scratch = new ScratchFolder();

        var read = ClipboardStill.Read(Path.Combine(scratch.Path, "gone.png"), "gone", TestStash.ShortRetry);

        Assert.IsFalse(read.Succeeded);
        Assert.AreEqual("Read the picture failed: gone, the file does not exist", read.Failure.Sentence);
    }

    [TestMethod]
    public void AFileHeldOpenIsRetriedThenNamedAsHeld()
    {
        using var scratch = new ScratchFolder();
        var path = Path.Combine(scratch.Path, "held.png");
        File.WriteAllBytes(path, MediaFixtures.Bytes("transparent.png"));
        using var holder = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var read = ClipboardStill.Read(path, "held", TestStash.ShortRetry);

        Assert.IsFalse(read.Succeeded);
        Assert.AreEqual(("held", HeldFileRetry.HeldReason), (read.Failure.Subject, read.Failure.Reason));
    }

    [TestMethod]
    public void AnOrientedPhotoGoesUprightAsItsBitmapAndItsPngBytes()
    {
        // G8 and Composition, Media: a JPEG's clipboard forms are upright as the camera meant it, its PNG encoded from the
        // upright frame without loss.
        var still = Read("oriented-6.jpg");

        Assert.AreEqual((24, 40), (still.Frame.Width, still.Frame.Height));
        using var codec = SKCodec.Create(new SKMemoryStream(still.Png));
        Assert.AreEqual((24, 40), (codec.Info.Width, codec.Info.Height));
        CollectionAssert.AreEqual(still.Frame.Pixels, Decoded(still.Png), "the PNG holds the upright frame");
        CollectionAssert.AreEqual(MediaFixtures.FirstFrame(MediaFixtures.Path("oriented-6.jpg")).Pixels, still.Frame.Pixels, "the bitmap is the decoder's upright frame");
    }

    private static ClipboardStill Read(string fixture)
    {
        var read = ClipboardStill.Read(MediaFixtures.Path(fixture), fixture, TestStash.ShortRetry);
        Assert.IsTrue(read.Succeeded, read.Failure?.Sentence);
        return read.Value!;
    }

    /// <summary>The PNG decoded into the boundary pixel contract: BGRA, straight alpha, sRGB, rows packed.</summary>
    private static byte[] Decoded(byte[] png)
    {
        using var codec = SKCodec.Create(new SKMemoryStream(png));
        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul, SKColorSpace.CreateSrgb());
        using var bitmap = new SKBitmap(info);
        Assert.AreEqual(SKCodecResult.Success, codec.GetPixels(info, bitmap.GetPixels()));
        var pixels = new byte[info.BytesSize];
        Marshal.Copy(bitmap.GetPixels(), pixels, 0, pixels.Length);
        return pixels;
    }
}
