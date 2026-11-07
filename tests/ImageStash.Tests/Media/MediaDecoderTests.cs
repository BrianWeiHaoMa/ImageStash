using System.Runtime.Versioning;
using System.Security.Cryptography;
using ImageStash.Files;
using ImageStash.Media;
using ImageStash.Tests.Support;
using SkiaSharp;

namespace ImageStash.Tests.Media;

/// <summary>
/// Media's decode boundary: facts from a full decode (F3, E4), frames one at a time in the boundary pixel contract and
/// scaled to a bound (R7), unreadable and truncated input refused naming the file without a crash, a held file named as
/// held (G35), every open read-only with sharing (R3), and the hash of the bytes, every frame of a decoder opened hashing
/// decoded from the file it hashed (G12).
/// </summary>
[TestClass]
public sealed class MediaDecoderTests
{
    private static readonly TimeSpan Retry = TimeSpan.FromSeconds(5);

    private static readonly byte[] Red = [0, 0, 255, 255];
    private static readonly byte[] Green = [0, 255, 0, 255];
    private static readonly byte[] Blue = [255, 0, 0, 255];
    private static readonly byte[] MidGrey = [128, 128, 128, 255];

    [TestMethod]
    [DataRow("still.png", ImageFormat.Png)]
    [DataRow("transparent.png", ImageFormat.Png)]
    [DataRow("animated.png", ImageFormat.Png)]
    [DataRow("photo.jpg", ImageFormat.Jpeg)]
    [DataRow("photo.jpeg", ImageFormat.Jpeg)]
    [DataRow("still.gif", ImageFormat.Gif)]
    [DataRow("still.webp", ImageFormat.WebP)]
    [DataRow("transparent.webp", ImageFormat.WebP)]
    public void AStillReadsAsOneFrameWithoutDuration(string fixture, ImageFormat format)
    {
        Assert.AreEqual(new MediaFacts(format, 8, 6, false, 1, 0), Facts(fixture));
    }

    [TestMethod]
    [DataRow("animated.gif", ImageFormat.Gif, 3, 600)]
    [DataRow("animated.webp", ImageFormat.WebP, 3, 600)]
    [DataRow("long.gif", ImageFormat.Gif, 60, 1800)]
    [DataRow("restore-previous.gif", ImageFormat.Gif, 3, 300)]
    public void AnAnimationReadsWithItsFrameCountAndTotalDuration(string fixture, ImageFormat format, int frames, int durationMs)
    {
        Assert.AreEqual(new MediaFacts(format, 8, 6, true, frames, durationMs), Facts(fixture));
    }

    [TestMethod]
    public void AnAnimatedPngIsAStillWhoseOneFrameDecodes()
    {
        using var decoder = Open("animated.png");

        Assert.AreEqual(1, decoder.FrameCount);
        var frame = Next(decoder, int.MaxValue);
        CollectionAssert.AreEqual(Red, Pixel(frame, 0, 0));
        Assert.AreEqual(0, frame.DurationMs);
        AssertEnded(decoder);
    }

    [TestMethod]
    [DataRow("animated.gif")]
    [DataRow("animated.webp")]
    public void AnAnimationsFramesComeOneAtATimeWithTheirDurations(string fixture)
    {
        using var decoder = Open(fixture);

        foreach (var (color, durationMs) in new[] { (Red, 100), (Green, 200), (Blue, 300) })
        {
            var frame = Next(decoder, int.MaxValue);
            Assert.AreEqual((8, 6, durationMs, 8 * 6 * 4), (frame.Width, frame.Height, frame.DurationMs, frame.Pixels.Length));
            CollectionAssert.AreEqual(color, Pixel(frame, 0, 0));
            CollectionAssert.AreEqual(color, Pixel(frame, 7, 5));
        }

        AssertEnded(decoder);
    }

    [TestMethod]
    public void AFrameAfterOneThatRestoresWhatWasUnderItIsDrawnOverTheFrameBefore()
    {
        using var decoder = Open("restore-previous.gif");

        foreach (var (corner, opposite) in new[] { (Red, Red), (Blue, Red), (Red, Green) })
        {
            var frame = Next(decoder, int.MaxValue);
            CollectionAssert.AreEqual(corner, Pixel(frame, 0, 0));
            CollectionAssert.AreEqual(opposite, Pixel(frame, 6, 4));
        }
    }

    [TestMethod]
    [DataRow("animated.gif")]
    [DataRow("animated.webp")]
    [DataRow("restore-previous.gif")]
    [DataRow("long.gif")]
    public void ARewoundAnimationGivesItsFramesAgainAsAFreshOpenDoes(string fixture)
    {
        using var decoder = Open(fixture);
        var first = Frames(decoder, 5);

        decoder.Rewind();
        var again = Frames(decoder, 5);

        using var fresh = Open(fixture);
        var expected = Frames(fresh, 5);
        Assert.HasCount(decoder.FrameCount, again);
        for (var i = 0; i < expected.Count; i++)
        {
            Assert.AreEqual(expected[i].DurationMs, first[i].DurationMs);
            Assert.AreEqual(expected[i].DurationMs, again[i].DurationMs);
            CollectionAssert.AreEqual(expected[i].Pixels, first[i].Pixels, $"frame {i} before the rewind");
            CollectionAssert.AreEqual(expected[i].Pixels, again[i].Pixels, $"frame {i} after the rewind");
        }
    }

    [TestMethod]
    public void AFirstFrameThatRestoresWhatWasUnderItDecodes()
    {
        using var scratch = new ScratchFolder();
        var bytes = MediaFixtures.Bytes("animated.gif");
        // The first frame's graphic control extension: its disposal, bits 2 to 4 of the byte after 21 F9 04, becomes 3.
        var control = bytes.AsSpan().IndexOf(new byte[] { 0x21, 0xF9, 0x04 });
        bytes[control + 3] = (byte)((bytes[control + 3] & ~0x1C) | (3 << 2));
        var path = scratch.File("restores-first.gif");
        File.WriteAllBytes(path, bytes);

        var read = MediaDecoder.ReadFacts(path, "restores-first.gif", Retry);
        Assert.IsTrue(read.Succeeded, read.Failure?.ToString());
        Assert.AreEqual(new MediaFacts(ImageFormat.Gif, 8, 6, true, 3, 600), read.Value!.Facts);
        using var decoder = Open(path, "restores-first.gif");
        foreach (var color in new[] { Red, Green, Blue })
        {
            CollectionAssert.AreEqual(color, Pixel(Next(decoder, int.MaxValue), 0, 0));
        }
    }

    [TestMethod]
    [DataRow("transparent.png")]
    [DataRow("transparent.webp")]
    public void TransparencyCrossesAsStraightAlpha(string fixture)
    {
        using var decoder = Open(fixture);

        var frame = Next(decoder, int.MaxValue);
        CollectionAssert.AreEqual(new byte[] { 0, 0, 255, 128 }, Pixel(frame, 1, 1));
        CollectionAssert.AreEqual(Blue, Pixel(frame, 0, 0));
    }

    [TestMethod]
    [DataRow(4, 4, 3)]
    [DataRow(6, 6, 5)]
    [DataRow(8, 8, 6)]
    [DataRow(100, 8, 6)]
    public void AFrameFitsTheBoundOnItsLongerEdgeAtTheSameAspectRatioAndIsNeverEnlarged(int bound, int width, int height)
    {
        using var decoder = Open("still.png");

        var frame = Next(decoder, bound);
        Assert.AreEqual((width, height, width * height * 4), (frame.Width, frame.Height, frame.Pixels.Length));
        CollectionAssert.AreEqual(Blue, Pixel(frame, width - 1, height - 1));
    }

    [TestMethod]
    public void AScaledFrameKeepsStraightAlpha()
    {
        using var decoder = Open("translucent.png");

        var frame = Next(decoder, 4);
        for (var pixel = 0; pixel < frame.Width * frame.Height; pixel++)
        {
            CollectionAssert.AreEqual(new byte[] { 0, 0, 255, 128 }, frame.Pixels[(pixel * 4)..(pixel * 4 + 4)]);
        }
    }

    [TestMethod]
    public void AScaledFrameTakesNoColourFromPixelsNobodySees()
    {
        using var decoder = Open("invisible-blue.png");

        var pixel = Pixel(Next(decoder, 1), 0, 0);
        Assert.AreEqual((0, 0, 255), (pixel[0], pixel[1], pixel[2]), "a transparent pixel's hidden blue showed");
        Assert.IsInRange(127, 128, pixel[3]);
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    [DataRow(4)]
    [DataRow(5)]
    [DataRow(6)]
    [DataRow(7)]
    [DataRow(8)]
    public void AnOrientedJpegIsUprightInItsSizeItsFramesAndItsFactsAndItsBytesStayAsTheyCame(int orientation)
    {
        // Composition, Media: a JPEG's EXIF orientation is applied as the file is decoded, while the file's bytes stay as
        // they came (F3, G29) and so does their hash (G12).
        var fixture = $"oriented-{orientation}.jpg";
        var bytes = MediaFixtures.Bytes(fixture);
        var (width, height) = orientation >= 5 ? (24, 40) : (40, 24);

        Assert.AreEqual(new MediaFacts(ImageFormat.Jpeg, width, height, false, 1, 0), Facts(fixture));
        using (var decoder = Open(MediaFixtures.Path(fixture), fixture, hash: true))
        {
            Assert.AreEqual((width, height), (decoder.Width, decoder.Height));
            CollectionAssert.AreEqual(SHA256.HashData(bytes), decoder.Sha256);
            var full = Next(decoder, int.MaxValue);
            Assert.AreEqual((width, height), (full.Width, full.Height));
            AssertQuadrants(full, MediaFixtures.UprightQuadrants[orientation], tolerance: 8, fixture);
            decoder.Rewind();
            // Scaled from the upright frame: the bound fits its longer edge, now the height for 5 to 8.
            var scaled = Next(decoder, 12);
            Assert.AreEqual(orientation >= 5 ? (7, 12) : (12, 7), (scaled.Width, scaled.Height));
            AssertQuadrants(scaled, MediaFixtures.UprightQuadrants[orientation], tolerance: 40, fixture + " at bound 12");
        }

        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(MediaFixtures.Path(fixture)), "the file's bytes after every read");
    }

    [TestMethod]
    public void AnOrientedWebpIsUprightAndAPngShowsAsItsPixelsThoughItCarriesAnOrientation()
    {
        // The codec reports a WebP's origin as it does a JPEG's, and reads no PNG's eXIf (S11): a PNG is not turned.
        using (var webp = Open("oriented-6.webp"))
        {
            Assert.AreEqual((24, 40), (webp.Width, webp.Height));
            AssertQuadrants(Next(webp, int.MaxValue), MediaFixtures.UprightQuadrants[6], tolerance: 0, "oriented-6.webp");
        }

        using var png = Open("oriented-6.png");
        Assert.AreEqual((40, 24), (png.Width, png.Height));
        AssertQuadrants(Next(png, int.MaxValue), "ABCD", tolerance: 0, "oriented-6.png");
        Assert.AreEqual(new MediaFacts(ImageFormat.WebP, 24, 40, false, 1, 0), Facts("oriented-6.webp"));
        Assert.AreEqual(new MediaFacts(ImageFormat.Png, 40, 24, false, 1, 0), Facts("oriented-6.png"));
    }

    [TestMethod]
    public void AnOrientedJpegsPixelsAreTheStoredPixelsMovedWholeNoneBlended()
    {
        // At full size the turn moves pixels, so every pixel of the upright frame is one of the stored frame's, and the
        // multiset of pixels is the same.
        var stored = Frames(Open("oriented-1.jpg"), int.MaxValue)[0];
        for (var orientation = 2; orientation <= 8; orientation++)
        {
            var upright = Frames(Open($"oriented-{orientation}.jpg"), int.MaxValue)[0];
            CollectionAssert.AreEquivalent(PixelValues(stored), PixelValues(upright), $"oriented-{orientation}.jpg");
        }

        static List<uint> PixelValues(MediaFrame frame) => Enumerable.Range(0, frame.Width * frame.Height).Select(i => BitConverter.ToUInt32(frame.Pixels, i * 4)).ToList();
    }

    [TestMethod]
    public void ALongAnimationComesFrameByFrameAtTheBoundWithItsDurations()
    {
        using var decoder = Open("long.gif");

        for (var index = 0; index < 60; index++)
        {
            var frame = Next(decoder, 4);
            Assert.AreEqual((4, 3, 10 * (index % 5 + 1)), (frame.Width, frame.Height, frame.DurationMs), $"frame {index}");
            CollectionAssert.AreEqual(new byte[] { (byte)(index * 37 % 256), (byte)(255 - index * 4), (byte)(index * 4), 255 }, Pixel(frame, 0, 0), $"frame {index}");
        }

        AssertEnded(decoder);
    }

    [TestMethod]
    [DataRow(".png")]
    [DataRow(".jpg")]
    [DataRow(".jpeg")]
    [DataRow(".gif")]
    [DataRow(".webp")]
    public void RandomBytesAreUnreadableNamedAsTheUserKnowsTheFile(string extension)
    {
        using var scratch = new ScratchFolder();
        var bytes = new byte[4096];
        new Random(7).NextBytes(bytes);
        var path = scratch.File(Guid.NewGuid().ToString("N") + extension);
        File.WriteAllBytes(path, bytes);

        AssertUnreadable(MediaDecoder.ReadFacts(path, "noise" + extension, Retry).Failure, "noise" + extension);
        AssertUnreadable(MediaDecoder.Open(path, "noise" + extension, Retry).Failure, "noise" + extension);
    }

    [TestMethod]
    [DataRow("still.png")]
    [DataRow("photo.jpg")]
    [DataRow("animated.gif")]
    [DataRow("animated.webp")]
    public void AWholeHeaderFollowedByGarbageIsUnreadable(string fixture)
    {
        using var scratch = new ScratchFolder();
        var bytes = MediaFixtures.Bytes(fixture);
        var garbage = new byte[bytes.Length - 32];
        new Random(11).NextBytes(garbage);
        garbage.CopyTo(bytes, 32);
        var path = scratch.File(fixture);
        File.WriteAllBytes(path, bytes);

        AssertUnreadable(MediaDecoder.ReadFacts(path, fixture, Retry).Failure, fixture);
    }

    [TestMethod]
    [DataRow("still.png", "the IEND chunk", 12)]
    [DataRow("photo.jpg", "the end-of-image marker", 2)]
    [DataRow("animated.webp", "part of its last frame", 10)]
    [DataRow("still.webp", "part of its picture", 4)]
    public void AFileCutShortIsUnreadable(string fixture, string cutOff, int cut)
    {
        var bytes = MediaFixtures.Bytes(fixture);

        AssertUnreadableAfterCut(fixture, bytes[..^cut], cutOff);
    }

    [TestMethod]
    public void AGifCutBetweenItsSecondAndThirdFrameIsUnreadable()
    {
        var bytes = MediaFixtures.Bytes("animated.gif");
        // Each frame starts with its graphic control extension.
        var starts = Enumerable.Range(0, bytes.Length - 2).Where(i => bytes[i] == 0x21 && bytes[i + 1] == 0xF9 && bytes[i + 2] == 0x04).ToList();
        Assert.HasCount(3, starts);

        AssertUnreadableAfterCut("animated.gif", bytes[..starts[2]], "its third frame");
    }

    [TestMethod]
    public void AJpegWhoseScanDataStopsAtAMarkerIsReadAsThePictureItStillDecodesTo()
    {
        // G35's one exception (S11): SkiaSharp reports success, and libjpeg leaves the blocks after the one where the data
        // stops mid-grey.
        using var scratch = new ScratchFolder();
        using var red = new SKBitmap(new SKImageInfo(16, 256, SKColorType.Bgra8888, SKAlphaType.Opaque));
        red.Erase(SKColors.Red);
        var half = scratch.File("half.jpg");
        File.WriteAllBytes(half, StopLastScan(red.Encode(SKEncodedImageFormat.Jpeg, 100).ToArray(), length => length / 2));
        var none = scratch.File("none.jpg");
        File.WriteAllBytes(none, StopLastScan(MediaFixtures.Bytes("photo.jpg"), _ => 0));

        var read = MediaDecoder.ReadFacts(half, "half.jpg", Retry);

        Assert.IsTrue(read.Succeeded, read.Failure?.ToString());
        Assert.AreEqual(new MediaFacts(ImageFormat.Jpeg, 16, 256, false, 1, 0), read.Value!.Facts);
        using var halfDecoder = Open(half, "half.jpg");
        var frame = Next(halfDecoder, int.MaxValue);
        var top = Pixel(frame, 0, 0);
        Assert.IsTrue(top[2] > 240 && top[1] < 16 && top[0] < 16, $"top left is {string.Join(", ", top)}, not red");
        CollectionAssert.AreEqual(MidGrey, Pixel(frame, 15, 255));
        var noneRead = MediaDecoder.ReadFacts(none, "none.jpg", Retry);
        Assert.IsTrue(noneRead.Succeeded, noneRead.Failure?.ToString());
        Assert.AreEqual(new MediaFacts(ImageFormat.Jpeg, 8, 6, false, 1, 0), noneRead.Value!.Facts);
    }

    [TestMethod]
    public void AProgressiveJpegWhoseLastScanStopsAtAMarkerIsRead()
    {
        using var scratch = new ScratchFolder();
        var path = scratch.File("progressive.jpg");
        File.WriteAllBytes(path, StopLastScan(MediaFixtures.Bytes("progressive.jpg"), length => length / 2));

        var read = MediaDecoder.ReadFacts(path, "progressive.jpg", Retry);

        Assert.IsTrue(read.Succeeded, read.Failure?.ToString());
        Assert.AreEqual(new MediaFacts(ImageFormat.Jpeg, 32, 32, false, 1, 0), read.Value!.Facts);
        using var decoder = Open(path, "progressive.jpg");
        Assert.HasCount(32 * 32 * 4, Next(decoder, int.MaxValue).Pixels);
    }

    [TestMethod]
    public void APngDeclaringMorePixelsThanCanBeHeldIsUnreadableAndNothingElseFails()
    {
        AssertUnreadable(MediaDecoder.ReadFacts(MediaFixtures.Path("huge.png"), "huge.png", Retry).Failure, "huge.png");

        Assert.AreEqual(new MediaFacts(ImageFormat.Png, 8, 6, false, 1, 0), Facts("still.png"));
    }

    [TestMethod]
    [DataRow(1_000_000, true, DisplayName = "a side of 1,000,000 is read")]
    [DataRow(1_000_001, false, DisplayName = "a side over 1,000,000 is unreadable")]
    public void APngWiderThanItsDecoderReadsIsUnreadable(int width, bool read)
    {
        using var scratch = new ScratchFolder();
        var path = scratch.File("wide.png");
        File.WriteAllBytes(path, OneRowPng(width));

        var facts = MediaDecoder.ReadFacts(path, "wide.png", Retry);

        if (read)
        {
            Assert.AreEqual(new MediaFacts(ImageFormat.Png, width, 1, false, 1, 0), facts.Value?.Facts, facts.Failure?.ToString());
        }
        else
        {
            AssertUnreadable(facts.Failure, "wide.png");
        }
    }

    [TestMethod]
    [DataRow(65_500, true, DisplayName = "a side of 65,500 is read")]
    [DataRow(65_501, false, DisplayName = "a side over 65,500 is unreadable")]
    public void AJpegWiderThanItsDecoderReadsIsUnreadable(int width, bool read)
    {
        using var scratch = new ScratchFolder();
        var path = scratch.File("wide.jpg");
        // photo.jpg with its frame header declaring this width: the scan data then stops at the end-of-image marker long
        // before the picture is complete, which a JPEG within the decoder's bound reads as decoded (G35's exception).
        var bytes = MediaFixtures.Bytes("photo.jpg");
        var frame = Enumerable.Range(2, bytes.Length - 2).First(i => bytes[i] == 0xFF && bytes[i + 1] is 0xC0 or 0xC1 or 0xC2);
        bytes[frame + 7] = (byte)(width >> 8);
        bytes[frame + 8] = (byte)width;
        File.WriteAllBytes(path, bytes);

        var facts = MediaDecoder.ReadFacts(path, "wide.jpg", Retry);

        if (read)
        {
            Assert.AreEqual((ImageFormat.Jpeg, width), (facts.Value?.Facts.Format, facts.Value?.Facts.Width), facts.Failure?.ToString());
        }
        else
        {
            AssertUnreadable(facts.Failure, "wide.jpg");
        }
    }

    [TestMethod]
    public void APngWhoseImageDataIsDamagedIsRefusedByItsFactsNotByALaterFrame()
    {
        using var scratch = new ScratchFolder();
        var bytes = MediaFixtures.Bytes("still.png");
        var data = bytes.AsSpan().IndexOf("IDAT"u8) + 4;
        var length = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(data - 8));
        bytes.AsSpan(data + 2, length - 2).Fill(0xA5);
        var path = scratch.File("damaged.png");
        File.WriteAllBytes(path, bytes);

        AssertUnreadable(MediaDecoder.ReadFacts(path, "damaged.png", Retry).Failure, "damaged.png");
        // Its header reads: only decoding the body tells it is damaged.
        var opened = MediaDecoder.Open(path, "damaged.png", Retry);
        Assert.IsTrue(opened.Succeeded, opened.Failure?.ToString());
        using var decoder = opened.Value!;
        AssertUnreadable(decoder.NextFrame(int.MaxValue).Failure, "damaged.png");
    }

    [TestMethod]
    public void AFileHeldOpenPastTheRetryIsAFailureThatSaysSoNotUnreadable()
    {
        using var scratch = new ScratchFolder();
        var path = Copy(scratch, "still.png");
        using var holder = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var failure = MediaDecoder.ReadFacts(path, "cat.png", TimeSpan.FromMilliseconds(300)).Failure;

        Assert.AreEqual(new Failure("Read the picture", "cat.png", HeldFileRetry.HeldReason), failure);
        Assert.IsFalse(MediaDecoder.IsUnreadable(failure!));
    }

    [TestMethod]
    public async Task AFileReleasedWithinTheRetryIsRead()
    {
        using var scratch = new ScratchFolder();
        var path = Copy(scratch, "animated.gif");
        var holder = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var release = Task.Run(async () =>
        {
            await Task.Delay(300);
            await holder.DisposeAsync();
        });

        var read = MediaDecoder.ReadFacts(path, "dance.gif", Retry);
        await release;

        Assert.IsTrue(read.Succeeded, read.Failure?.ToString());
        Assert.AreEqual(new MediaFacts(ImageFormat.Gif, 8, 6, true, 3, 600), read.Value!.Facts);
    }

    [TestMethod]
    public void AFileAnotherProgramWritesButLetsOthersReadIsRead()
    {
        using var scratch = new ScratchFolder();
        var path = Copy(scratch, "photo.jpg");
        using var holder = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);

        var read = MediaDecoder.ReadFacts(path, "photo.jpg", Retry);

        Assert.IsTrue(read.Succeeded, read.Failure?.ToString());
        Assert.AreEqual(new MediaFacts(ImageFormat.Jpeg, 8, 6, false, 1, 0), read.Value!.Facts);
    }

    [TestMethod]
    public void AFileCanBeRenamedWhileItsFramesAreRead()
    {
        using var scratch = new ScratchFolder();
        var path = Copy(scratch, "animated.gif");
        using var decoder = Open(path, "dance.gif");
        CollectionAssert.AreEqual(Red, Pixel(Next(decoder, int.MaxValue), 0, 0));

        File.Move(path, scratch.File("renamed.gif"));

        CollectionAssert.AreEqual(Green, Pixel(Next(decoder, int.MaxValue), 0, 0));
        CollectionAssert.AreEqual(Blue, Pixel(Next(decoder, int.MaxValue), 0, 0));
    }

    [TestMethod]
    [SupportedOSPlatform("windows")]
    [DataRow(false)]
    [DataRow(true)]
    public async Task AReadRefusedPartWayIsRetriedFromAFreshOpen(bool hash)
    {
        using var scratch = new ScratchFolder();
        var (path, pixels) = WriteNoisePng(scratch);
        var hashed = SHA256.HashData(File.ReadAllBytes(path));
        using var decoder = Open(path, "noise.png", hash);
        var locker = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        locker.Lock(0, locker.Length);
        var release = Task.Run(async () =>
        {
            await Task.Delay(300);
            locker.Unlock(0, locker.Length);
            await locker.DisposeAsync();
        });

        var frame = decoder.NextFrame(int.MaxValue);
        await release;

        Assert.IsTrue(frame.Succeeded, frame.Failure?.ToString());
        CollectionAssert.AreEqual(pixels, frame.Value!.Pixels);
        CollectionAssert.AreEqual(hash ? hashed : null, decoder.Sha256, "the same bytes, so the same hash");
    }

    [TestMethod]
    public void ADecoderOpenedHashingDecodesAndHashesTheFileItOpenedWhateverIsRenamedOntoItsPath()
    {
        using var scratch = new ScratchFolder();
        var (path, pixels) = WriteNoisePng(scratch);
        var hashed = SHA256.HashData(File.ReadAllBytes(path));
        using var decoder = Open(path, "noise.png", hash: true);

        PutAnotherFileAt(scratch, path, MediaFixtures.Bytes("still.png"));

        CollectionAssert.AreEqual(pixels, Next(decoder, int.MaxValue).Pixels, "the file the handle opened");
        CollectionAssert.AreEqual(hashed, decoder.Sha256);
        AssertEnded(decoder);
    }

    [TestMethod]
    public void AnOriginalOpenedByItsHashOpensThoseBytesAndRefusesAnotherFileAtItsPathByName()
    {
        using var scratch = new ScratchFolder();
        var (path, pixels) = WriteNoisePng(scratch);
        var hashed = SHA256.HashData(File.ReadAllBytes(path));
        using (var decoder = MediaDecoder.OpenOriginal(path, "noise.png", Retry, hashed).Value!)
        {
            CollectionAssert.AreEqual(pixels, Next(decoder, int.MaxValue).Pixels, "its own bytes");
        }

        PutAnotherFileAt(scratch, path, MediaFixtures.Bytes("still.png"));

        Assert.AreEqual(new Failure("Read the picture", "noise.png", MediaDecoder.ChangedReason), MediaDecoder.OpenOriginal(path, "noise.png", Retry, hashed).Failure, "a readable file, neither unreadable nor held");
    }

    [TestMethod]
    [SupportedOSPlatform("windows")]
    public void AReadRefusedPartWayThatFindsAnotherFileAtThePathFailsWithoutReadingIt()
    {
        using var scratch = new ScratchFolder();
        var (path, _) = WriteNoisePng(scratch);
        using var decoder = Open(path, "noise.png", hash: true);
        using var locker = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        locker.Lock(0, locker.Length);
        PutAnotherFileAt(scratch, path, MediaFixtures.Bytes("still.png"));

        var frame = decoder.NextFrame(int.MaxValue);

        Assert.AreEqual(new Failure("Read the picture", "noise.png", MediaDecoder.ChangedReason), frame.Failure, "neither unreadable nor held, and no frame of the other file");
    }

    [TestMethod]
    [SupportedOSPlatform("windows")]
    public void AReadRefusedPartWayPastTheFirstFrameThatFindsAnotherFileFailsWithoutReadingIt()
    {
        using var scratch = new ScratchFolder();
        var path = WriteNoiseGif(scratch, "noise.gif", seed: 5);
        using var decoder = Open(path, "noise.gif", hash: true);
        Next(decoder, int.MaxValue);
        using var locker = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        locker.Lock(0, locker.Length);
        PutAnotherFileAt(scratch, path, File.ReadAllBytes(WriteNoiseGif(scratch, "other.gif", seed: 6)));

        var frame = decoder.NextFrame(int.MaxValue);

        Assert.AreEqual(new Failure("Read the picture", "noise.gif", MediaDecoder.ChangedReason), frame.Failure, "a clip never goes on with another file's frames");
    }

    [TestMethod]
    [SupportedOSPlatform("windows")]
    public void AReadRefusedPartWayPastTheRetryIsAFailureThatSaysSo()
    {
        using var scratch = new ScratchFolder();
        var (path, _) = WriteNoisePng(scratch);
        var opened = MediaDecoder.Open(path, "noise.png", TimeSpan.FromMilliseconds(300));
        Assert.IsTrue(opened.Succeeded, opened.Failure?.ToString());
        using var decoder = opened.Value!;
        using var locker = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        locker.Lock(0, locker.Length);

        var failure = decoder.NextFrame(int.MaxValue).Failure;

        Assert.AreEqual(new Failure("Read the picture", "noise.png", HeldFileRetry.HeldReason), failure);
    }

    [TestMethod]
    public void TheHashIsTheSha256OfTheFilesBytes()
    {
        var read = MediaDecoder.ReadFacts(MediaFixtures.Path("still.png"), "still.png", Retry);

        Assert.IsTrue(read.Succeeded, read.Failure?.ToString());
        CollectionAssert.AreEqual(SHA256.HashData(MediaFixtures.Bytes("still.png")), read.Value!.Sha256);
    }

    [TestMethod]
    [DataRow("still.png")]
    [DataRow("animated.gif")]
    [DataRow("still.webp")]
    public void ThePictureReaderAnswersTheFactsAndTheHashReadFactsAnswers(string fixture)
    {
        var expected = MediaDecoder.ReadFacts(MediaFixtures.Path(fixture), fixture, Retry).Value!;

        var read = MediaDecoder.ReadPicture(MediaFixtures.Path(fixture), fixture, Retry);

        Assert.IsTrue(read.Succeeded, read.Failure?.ToString());
        Assert.AreEqual(expected.Facts, read.Value!.Facts);
        CollectionAssert.AreEqual(expected.Sha256, read.Value.Sha256);
    }

    [TestMethod]
    public void ThePictureReaderAnswersTheHashOfBytesThatDoNotDecodeWithNoFacts()
    {
        using var scratch = new ScratchFolder();
        var noise = new byte[200];
        new Random(7).NextBytes(noise);
        var cut = MediaFixtures.Bytes("still.png")[..^12];

        foreach (var (name, bytes) in new[] { ("noise.png", noise), ("cut.png", cut) })
        {
            var path = scratch.File(name);
            File.WriteAllBytes(path, bytes);

            var read = MediaDecoder.ReadPicture(path, name, Retry);

            Assert.IsTrue(read.Succeeded, read.Failure?.ToString());
            Assert.IsNull(read.Value!.Facts, $"{name} has no facts");
            CollectionAssert.AreEqual(SHA256.HashData(bytes), read.Value.Sha256, $"{name}'s own bytes");
            AssertUnreadable(MediaDecoder.ReadFacts(path, name, Retry).Failure, name);
        }
    }

    [TestMethod]
    [SupportedOSPlatform("windows")]
    public void ThePictureReaderNamesAMissingFileAndOneHeldPastTheRetryWithoutAHash()
    {
        using var scratch = new ScratchFolder();
        var path = Copy(scratch, "still.png");

        var held = HeldFiles.WhileLocked(path, () => MediaDecoder.ReadPicture(path, "cat.png", TimeSpan.FromMilliseconds(300)));
        var missing = MediaDecoder.ReadPicture(scratch.File("gone.png"), "gone.png", Retry);

        Assert.AreEqual(new Failure("Read the picture", "cat.png", HeldFileRetry.HeldReason), held.Failure);
        Assert.AreEqual(new Failure("Read the picture", "gone.png", HeldFileRetry.MissingReason), missing.Failure);
    }

    private static MediaFacts Facts(string fixture)
    {
        var read = MediaDecoder.ReadFacts(MediaFixtures.Path(fixture), fixture, Retry);
        Assert.IsTrue(read.Succeeded, read.Failure?.ToString());
        return read.Value!.Facts;
    }

    private static MediaDecoder Open(string fixture) => Open(MediaFixtures.Path(fixture), fixture);

    private static MediaDecoder Open(string path, string name, bool hash = false)
    {
        var opened = MediaDecoder.Open(path, name, Retry, hash);
        Assert.IsTrue(opened.Succeeded, opened.Failure?.ToString());
        return opened.Value!;
    }

    /// <summary>Renames the file at <paramref name="path"/> away, as a rename of its item does, and renames a file of <paramref name="bytes"/> onto the path.</summary>
    private static void PutAnotherFileAt(ScratchFolder scratch, string path, byte[] bytes)
    {
        File.Move(path, scratch.File("renamed away" + Path.GetExtension(path)));
        var other = scratch.File("other bytes" + Path.GetExtension(path));
        File.WriteAllBytes(other, bytes);
        File.Move(other, path);
    }

    private static MediaFrame Next(MediaDecoder decoder, int bound)
    {
        var frame = decoder.NextFrame(bound);
        Assert.IsTrue(frame.Succeeded, frame.Failure?.ToString());
        Assert.IsNotNull(frame.Value);
        return frame.Value;
    }

    /// <summary>Every frame left, at the bound, until the decoder says there is none.</summary>
    private static List<MediaFrame> Frames(MediaDecoder decoder, int bound)
    {
        var frames = new List<MediaFrame>();
        while (true)
        {
            var frame = decoder.NextFrame(bound);
            Assert.IsTrue(frame.Succeeded, frame.Failure?.ToString());
            if (frame.Value is null)
            {
                return frames;
            }

            frames.Add(frame.Value);
        }
    }

    private static void AssertEnded(MediaDecoder decoder)
    {
        var after = decoder.NextFrame(int.MaxValue);
        Assert.IsTrue(after.Succeeded, after.Failure?.ToString());
        Assert.IsNull(after.Value);
    }

    /// <summary>
    /// Asserts each quadrant's centre of <paramref name="frame"/>, top-left, top-right, bottom-left, bottom-right, shows
    /// the stored quadrant <paramref name="quadrants"/> names, within <paramref name="tolerance"/> on each channel.
    /// </summary>
    private static void AssertQuadrants(MediaFrame frame, string quadrants, int tolerance, string what)
    {
        var centres = new[] { (frame.Width / 4, frame.Height / 4), (3 * frame.Width / 4, frame.Height / 4), (frame.Width / 4, 3 * frame.Height / 4), (3 * frame.Width / 4, 3 * frame.Height / 4) };
        for (var i = 0; i < 4; i++)
        {
            var (x, y) = centres[i];
            var shown = Pixel(frame, x, y);
            var (r, g, b) = MediaFixtures.StoredQuadrants[quadrants[i]];
            var near = Math.Abs(shown[2] - r) <= tolerance && Math.Abs(shown[1] - g) <= tolerance && Math.Abs(shown[0] - b) <= tolerance && shown[3] == 255;
            Assert.IsTrue(near, $"{what}: at ({x}, {y}) BGRA {string.Join(", ", shown)}, where stored quadrant {quadrants[i]} (RGB {r}, {g}, {b}) belongs");
        }
    }

    private static byte[] Pixel(MediaFrame frame, int x, int y) => frame.Pixels[((y * frame.Width + x) * 4)..((y * frame.Width + x) * 4 + 4)];

    // An opaque black RGBA PNG one row high, its chunks' CRCs computed as PNG defines them.
    private static byte[] OneRowPng(int width)
    {
        static byte[] Chunk(string type, byte[] data)
        {
            var typed = System.Text.Encoding.ASCII.GetBytes(type).Concat(data).ToArray();
            var crc = 0xFFFFFFFFu;
            foreach (var b in typed)
            {
                crc ^= b;
                for (var k = 0; k < 8; k++)
                {
                    crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
                }
            }

            return [.. BigEndian((uint)data.Length), .. typed, .. BigEndian(~crc)];
        }

        static byte[] BigEndian(uint value) => [(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value];

        var row = new byte[1 + 4 * width];
        for (var x = 0; x < width; x++)
        {
            row[1 + 4 * x + 3] = 255;
        }

        using var compressed = new MemoryStream();
        using (var zlib = new System.IO.Compression.ZLibStream(compressed, System.IO.Compression.CompressionLevel.Fastest))
        {
            zlib.Write(row);
        }

        return [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A,
            .. Chunk("IHDR", [.. BigEndian((uint)width), .. BigEndian(1), 8, 6, 0, 0, 0]),
            .. Chunk("IDAT", compressed.ToArray()),
            .. Chunk("IEND", [])];
    }

    private static void AssertUnreadable(Failure? failure, string name)
    {
        Assert.AreEqual(new Failure("Read the picture", name, MediaDecoder.UnreadableReason), failure);
        Assert.IsTrue(MediaDecoder.IsUnreadable(failure!));
    }

    private static void AssertUnreadableAfterCut(string fixture, byte[] cut, string cutOff)
    {
        using var scratch = new ScratchFolder();
        var path = scratch.File(fixture);
        File.WriteAllBytes(path, cut);

        Assert.IsNotNull(MediaDecoder.ReadFacts(path, fixture, Retry).Failure, $"{fixture} without {cutOff} was read");
        AssertUnreadable(MediaDecoder.ReadFacts(path, fixture, Retry).Failure, fixture);
    }

    /// <summary>
    /// <paramref name="jpeg"/> with its last scan's entropy-coded data cut to the length <paramref name="kept"/> gives,
    /// then its end-of-image marker.
    /// </summary>
    private static byte[] StopLastScan(byte[] jpeg, Func<int, int> kept)
    {
        var scan = jpeg.AsSpan().LastIndexOf([(byte)0xFF, (byte)0xDA]);
        var data = scan + 2 + (jpeg[scan + 2] << 8 | jpeg[scan + 3]);
        return [.. jpeg[..(data + kept(jpeg.Length - 2 - data))], 0xFF, 0xD9];
    }

    private static string Copy(ScratchFolder scratch, string fixture)
    {
        var path = scratch.File(Guid.NewGuid().ToString("N") + Path.GetExtension(fixture));
        File.Copy(MediaFixtures.Path(fixture), path);
        return path;
    }

    /// <summary>A PNG of opaque noise, much larger than one read, so its image data is read after it is opened.</summary>
    private static (string Path, byte[] Pixels) WriteNoisePng(ScratchFolder scratch)
    {
        var info = new SKImageInfo(1200, 1000, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        var pixels = new byte[info.BytesSize];
        new Random(13).NextBytes(pixels);
        for (var alpha = 3; alpha < pixels.Length; alpha += 4)
        {
            pixels[alpha] = 255;
        }

        using var bitmap = new SKBitmap(info);
        System.Runtime.InteropServices.Marshal.Copy(pixels, 0, bitmap.GetPixels(), pixels.Length);
        var path = scratch.File("noise.png");
        File.WriteAllBytes(path, bitmap.Encode(SKEncodedImageFormat.Png, 100).ToArray());
        return (path, pixels);
    }

    /// <summary>
    /// A GIF of three frames of 256-colour noise, each image far larger than one read, so a frame past the first is read
    /// from the file after the first is decoded, not from what that read buffered.
    /// </summary>
    private static string WriteNoiseGif(ScratchFolder scratch, string name, int seed)
    {
        using var stream = new MemoryStream();
        var clip = GifEncoder.Open(stream, 400, 300, name).Value!;
        for (var i = 0; i < 3; i++)
        {
            Assert.IsTrue(clip.Append(SyntheticFrames.Noise(seed * 10 + i, 256, 400, 300), i * 100L).Succeeded, $"frame {i}");
        }

        Assert.IsNotNull(clip.Stop(300).Value);
        var path = scratch.File(name);
        File.WriteAllBytes(path, stream.ToArray());
        return path;
    }
}
