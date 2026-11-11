using System.Runtime.Versioning;
using ImageStash.Files;
using ImageStash.Layers;
using ImageStash.Media;
using ImageStash.Tests.Support;
using SkiaSharp;
using static ImageStash.Tests.Support.SyntheticFrames;

namespace ImageStash.Tests.Media;

/// <summary>
/// Media's one GIF encoder: full frames with their times in, one image per frame holding only what changed, each with
/// its own palette and the boundary rule's delay, a frame at the previous frame's time included (What crosses every
/// boundary; the G14, G15 entry; F3, G15, T3), a pixel turned transparent cleared rather than left showing the frame
/// before (G6, G8, S12), the bytes written so far, Stop, Cancel and the clip with no frame, a clip written to a caller's
/// stream, every refusal and failure named and never thrown (G35), a fixed few frames held (R7), and no other type
/// calling the library (R4). Every result is read back through MediaDecoder, and its images through the file's own blocks.
/// </summary>
[TestClass]
// The library quantizes on the thread pool and waits for it: run side by side on pool threads, the first of these checks
// stalled 12 s each, while one at a time each takes milliseconds. Alone, the heap measures one clip (R7).
[DoNotParallelize]
public sealed class GifEncoderTests
{
    private const string Clip = "Recording 2026-10-20 12.00.00";

    private const string Action = "Write the clip as GIF";

    private const int W = 64, H = 48;

    private static readonly (byte B, byte G, byte R) Orange = (0, 140, 255), Teal = (128, 128, 0);

    [TestMethod]
    public async Task FramesAreWrittenAsAnAnimatedGifAtTheWorkPathStoreAllocated()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var path = store.AllocateWorkPath(".gif").Value!;
        var frames = Enumerable.Range(0, 4).Select(seed => Pattern(seed)).ToList();

        var clip = Encode(path, frames, [0, 70, 140, 210], stopMs: 280);

        Assert.AreEqual(new GifClip(path, 4, 280), clip);
        Assert.AreEqual(new MediaFacts(ImageFormat.Gif, W, H, true, 4, 280), Facts(path));
        var decoded = MediaFixtures.Frames(path);
        CollectionAssert.AreEqual(new[] { 70, 70, 70, 70 }, decoded.Select(f => f.DurationMs).ToList());
        for (var i = 0; i < 4; i++)
        {
            CollectionAssert.AreEqual(frames[i].Pixels, decoded[i].Pixels, $"frame {i}");
        }

        CollectionAssert.AreEqual(new[] { Path.GetFileName(path) }, stash.WorkFiles());
        Assert.IsEmpty(stash.RootFiles());
        var blocks = GifImages.Read(path);
        Assert.AreEqual((W, H), (blocks.ScreenWidth, blocks.ScreenHeight));
        Assert.IsTrue(blocks.Images.All(image => image.Disposal == GifImages.Keep && image.PaletteSize is > 0 and <= 256), "every image is kept under the next and carries its own palette");
    }

    [TestMethod]
    public void EachTimeIsRoundedToItsCentisecondBeforeTheDelaysAreTaken()
    {
        using var scratch = new ScratchFolder();
        var path = scratch.File("clip.gif");

        // 0, 66, 134 and 200 ms round to 0, 7, 13 and 20 cs: rounding each difference instead would give 7, 7 and 7.
        var clip = Encode(path, [Pattern(0), Pattern(1), Pattern(2)], [0, 66, 134], stopMs: 200);

        CollectionAssert.AreEqual(new[] { 7, 6, 7 }, Delays(path));
        CollectionAssert.AreEqual(new[] { 70, 60, 70 }, MediaFixtures.Frames(path).Select(f => f.DurationMs).ToList());
        Assert.AreEqual(200, clip.DurationMs);
    }

    [TestMethod]
    public void ASkippedSampleLengthensTheFrameBeforeIt()
    {
        using var scratch = new ScratchFolder();
        var path = scratch.File("clip.gif");

        var clip = Encode(path, [Pattern(0), Pattern(1), Pattern(2)], [0, 70, 210], stopMs: 280);

        CollectionAssert.AreEqual(new[] { 70, 140, 70 }, MediaFixtures.Frames(path).Select(f => f.DurationMs).ToList());
        Assert.AreEqual(280, clip.DurationMs);
    }

    [TestMethod]
    public void AStopInTheLastFramesOwnCentisecondGivesItNoDelay()
    {
        using var scratch = new ScratchFolder();
        var path = scratch.File("clip.gif");

        var clip = Encode(path, [Pattern(0), Pattern(1)], [0, 70], stopMs: 72);

        CollectionAssert.AreEqual(new[] { 7, 0 }, Delays(path));
        CollectionAssert.AreEqual(new[] { 70, 0 }, MediaFixtures.Frames(path).Select(f => f.DurationMs).ToList());
        Assert.AreEqual(70, clip.DurationMs);
    }

    [TestMethod]
    public void AHalfCentisecondRoundsUpAndLessRoundsDown()
    {
        using var scratch = new ScratchFolder();
        var halfUp = scratch.File("half.gif");
        var below = scratch.File("below.gif");

        // 65 ms is 6.5 cs, up to 7; rounding halves to even would write 6 and 8 cs, the same total on the wrong frames.
        Encode(halfUp, [Pattern(0), Pattern(1)], [0, 65], stopMs: 140);
        Encode(below, [Pattern(0), Pattern(1)], [0, 64], stopMs: 140);

        CollectionAssert.AreEqual(new[] { 7, 7 }, Delays(halfUp));
        CollectionAssert.AreEqual(new[] { 6, 8 }, Delays(below));
    }

    [TestMethod]
    public void IdenticalFramesAreEachAnImageOfOnePixelSoTheClipLastsTheRecordedTime()
    {
        using var scratch = new ScratchFolder();
        var path = scratch.File("still.gif");
        // Few enough colours that median cut keeps each exactly (200 at random do not all survive it).
        var noise = Noise(seed: 3, colours: 64);

        var clip = Encode(path, Enumerable.Repeat(noise, 5).ToList(), [0, 70, 140, 210, 280], stopMs: 350);

        CollectionAssert.AreEqual(
            new[] { (0, 0, W, H), (0, 0, 1, 1), (0, 0, 1, 1), (0, 0, 1, 1), (0, 0, 1, 1) },
            GifImages.Read(path).Images.Select(image => (image.Left, image.Top, image.Width, image.Height)).ToList());
        Assert.AreEqual(new GifClip(path, 5, 350), clip);
        Assert.AreEqual(new MediaFacts(ImageFormat.Gif, W, H, true, 5, 350), Facts(path));
        foreach (var frame in MediaFixtures.Frames(path))
        {
            CollectionAssert.AreEqual(noise.Pixels, frame.Pixels);
        }
    }

    [TestMethod]
    public void OnlyTheChangedRectangleIsWrittenAtItsPositionOverTheKeptFrame()
    {
        using var scratch = new ScratchFolder();
        var path = scratch.File("block.gif");
        // More colours than one palette holds, so the block's exact colour shows its image was quantized alone.
        var first = Noise(seed: 5, colours: 4000);
        var block = Paint(first, 10, 4, 16, 16, (17, 230, 99));
        var corner = Paint(block, W - 1, H - 1, 1, 1, (250, 3, 128));

        Encode(path, [first, block, corner], [0, 70, 140], stopMs: 210);

        CollectionAssert.AreEqual(
            new[] { (0, 0, W, H), (10, 4, 16, 16), (W - 1, H - 1, 1, 1) },
            GifImages.Read(path).Images.Select(image => (image.Left, image.Top, image.Width, image.Height)).ToList());
        var decoded = MediaFixtures.Frames(path);
        for (var y = 0; y < H; y++)
        {
            for (var x = 0; x < W; x++)
            {
                var inBlock = x is >= 10 and < 26 && y is >= 4 and < 20;
                Assert.AreEqual(inBlock ? Pixel(block, x, y) : Pixel(decoded[0], x, y), Pixel(decoded[1], x, y), $"frame 1 at ({x}, {y})");
                var inCorner = x == W - 1 && y == H - 1;
                Assert.AreEqual(inCorner ? Pixel(corner, x, y) : Pixel(decoded[1], x, y), Pixel(decoded[2], x, y), $"frame 2 at ({x}, {y})");
            }
        }
    }

    [TestMethod]
    public void AFrameOfManyColoursGetsAMedianCutPaletteWithoutDithering()
    {
        using var scratch = new ScratchFolder();
        var path = scratch.File("colours.gif");
        var source = Frame((x, y) =>
        {
            var n = (y * (W / 2) + x) % 300;
            return x < W / 2 ? ((byte)(n * 7), (byte)(255 - n * 3), (byte)(n * 13 % 251)) : ((byte)40, (byte)200, (byte)90);
        });
        Assert.AreEqual(301, Enumerable.Range(0, W * H).Select(i => Pixel(source, i % W, i / W)).Distinct().Count());

        Encode(path, [source], [0], stopMs: 70);

        var decoded = MediaFixtures.Frames(path)[0];
        var all = Enumerable.Range(0, W * H).Select(i => Pixel(decoded, i % W, i / W)).Distinct().ToList();
        var flat = Enumerable.Range(0, W * H).Where(i => i % W >= W / 2).Select(i => Pixel(decoded, i % W, i / W)).Distinct().ToList();
        Assert.IsLessThanOrEqualTo(256, all.Count);
        CollectionAssert.AreEqual(new[] { Pixel(source, W - 1, 0) }, flat, "the flat half is one colour, the source's");
        var shownFor = Enumerable.Range(0, W * H).GroupBy(i => Pixel(source, i % W, i / W), i => Pixel(decoded, i % W, i / W));
        Assert.IsTrue(shownFor.All(colour => colour.Distinct().Count() == 1), "without dithering: every pixel of one colour shows alike");
    }

    [TestMethod]
    public void AFrameWhoseColoursOnePaletteHoldsKeepsEachExactlyThoughAFewPixelsCarryIt()
    {
        using var scratch = new ScratchFolder();
        var path = scratch.File("few.gif");
        var source = FifteenColours();

        Encode(path, [source], [0], stopMs: 70);

        CollectionAssert.AreEqual(source.Pixels, MediaFixtures.Frames(path)[0].Pixels, "every pixel as it was, the black box's and each green's");
    }

    [TestMethod]
    public void APixelAtHalfAlphaOrMoreShowsItsStraightColourOpaqueAndOneBelowIsTransparent()
    {
        // G21: colours render into the finished picture as shown; a GIF holds no partial alpha, so a pixel it keeps shows
        // its own colour, never that colour blended over black, and the rest is its one transparent index.
        using var scratch = new ScratchFolder();
        var path = scratch.File("translucent.gif");
        var source = FifteenColours();
        var row = 28;
        foreach (var (b, g, r) in new (byte, byte, byte)[] { (255, 255, 255), (20, 20, 220), (0, 200, 0) })
        {
            for (var x = 0; x < W; x++)
            {
                // Alphas from 1 to 254 across the row, either side of half.
                var alpha = (byte)(1 + x * 253 / (W - 1));
                (source.Pixels[(row * W + x) * 4], source.Pixels[(row * W + x) * 4 + 1], source.Pixels[(row * W + x) * 4 + 2], source.Pixels[(row * W + x) * 4 + 3]) = (b, g, r, alpha);
            }

            row++;
        }

        Encode(path, [source], [0], stopMs: 70);

        var decoded = MediaFixtures.Frames(path)[0];
        for (var i = 0; i < W * H; i++)
        {
            var pixel = Pixel(source, i % W, i / W);
            var shown = pixel >> 24 >= 128 ? pixel | 0xFF000000 : 0;
            Assert.AreEqual(shown, Seen(decoded)[i], $"({i % W}, {i / W}) from {pixel:X8}");
        }
    }

    [TestMethod]
    public void WhiteTextOverTransparencyKeepsWhiteEdgesWithoutADarkFringe()
    {
        using var scratch = new ScratchFolder();
        var transparent = new MediaFrame(240, 100, 70, new byte[240 * 100 * 4]);
        var text = new TextLayer(TextPreset.Label, "HELLO", "Segoe UI", 40, new(255, 255, 255, 255), Outline.None, new(8, 8, 220, 80), 0, null);
        var composed = Compositions.Compose(transparent, new LayerList([text])).Frame;
        var path = scratch.File("text.gif");

        Encode(path, [composed], [0], stopMs: 70);

        var decoded = MediaFixtures.FirstFrame(path);
        var kept = 0;
        for (var p = 0; p < decoded.Pixels.Length; p += 4)
        {
            if (composed.Pixels[p + 3] >= 128)
            {
                kept++;
                Assert.AreEqual(0xFFFFFFFFu, BitConverter.ToUInt32(decoded.Pixels, p), $"pixel {p / 4} of alpha {composed.Pixels[p + 3]}: white, not darkened");
            }
            else
            {
                Assert.AreEqual(0, decoded.Pixels[p + 3], $"pixel {p / 4} of alpha {composed.Pixels[p + 3]}: transparent");
            }
        }

        Assert.IsTrue(composed.Pixels.Where((_, i) => i % 4 == 3).Any(alpha => alpha is >= 128 and < 255), "the text has antialiased edges kept by the threshold");
        Assert.IsGreaterThan(100, kept, "the text was drawn");
    }

    [TestMethod]
    public void AFrameTooColourfulForOnePaletteIsQuantizedFromItsStraightColoursAlone()
    {
        // The median-cut path: what a pixel at or above half alpha shows depends on its colour alone, so the frame as it is
        // and the frame with those pixels made opaque write the same picture.
        using var scratch = new ScratchFolder();
        var gradient = Frame((x, y) => ((byte)(x * 4), (byte)(y * 5), (byte)(255 - x * 2)));
        var opaque = (byte[])gradient.Pixels.Clone();
        for (var i = 0; i < W * H; i++)
        {
            // Alpha steps through 0 to 255 by the pixel's place, so most rows hold pixels either side of half.
            var alpha = (byte)(i * 37 % 256);
            gradient.Pixels[i * 4 + 3] = alpha;
            opaque[i * 4 + 3] = alpha >= 128 ? (byte)255 : alpha;
        }

        Encode(scratch.File("translucent.gif"), [gradient], [0], stopMs: 70);
        Encode(scratch.File("opaque.gif"), [gradient with { Pixels = opaque }], [0], stopMs: 70);

        var translucent = Seen(MediaFixtures.Frames(scratch.File("translucent.gif"))[0]);
        CollectionAssert.AreEqual(Seen(MediaFixtures.Frames(scratch.File("opaque.gif"))[0]), translucent);
        Assert.IsGreaterThan(256, Enumerable.Range(0, W * H).Where(i => gradient.Pixels[i * 4 + 3] >= 128).Select(i => Pixel(gradient, i % W, i / W) | 0xFF000000).Distinct().Count(), "more colours than one palette holds");
        for (var i = 0; i < W * H; i++)
        {
            Assert.AreEqual(gradient.Pixels[i * 4 + 3] >= 128, translucent[i] != 0, $"pixel {i} kept or transparent by its alpha");
        }
    }

    [TestMethod]
    public void AOneFrameClipLastsUntilStopThoughItDecodesAsAStill()
    {
        using var scratch = new ScratchFolder();
        var path = scratch.File("one.gif");

        var clip = Encode(path, [Pattern(0)], [0], stopMs: 2500);

        Assert.AreEqual(new GifClip(path, 1, 2500), clip);
        CollectionAssert.AreEqual(new[] { 250 }, Delays(path));
        Assert.AreEqual(new MediaFacts(ImageFormat.Gif, W, H, false, 1, 0), Facts(path));
    }

    [TestMethod]
    public async Task AClipWithNoFrameIsCancelledAndLeavesNoFile()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var path = store.AllocateWorkPath(".gif").Value!;
        var encoder = Opened(path);

        var stopped = encoder.Stop(500);

        Assert.IsTrue(stopped.Succeeded, stopped.Failure?.ToString());
        Assert.IsNull(stopped.Value);
        Assert.IsFalse(File.Exists(path));
        Assert.IsEmpty(stash.WorkFiles());
    }

    [TestMethod]
    public async Task CancelClosesTheFileAsItIsForStoreToDiscard()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var path = store.AllocateWorkPath(".gif").Value!;
        var encoder = Opened(path);
        Assert.IsTrue(encoder.Append(Pattern(0), 0).Succeeded);
        Assert.IsTrue(encoder.Append(Pattern(1), 70).Succeeded);

        encoder.Cancel();

        var blocks = GifImages.Read(path);
        Assert.HasCount(1, blocks.Images, "the pending frame is dropped");
        Assert.IsFalse(blocks.Trailer, "nothing more is written");
        var finished = new Failure(Action, Clip, "the clip is already finished");
        Assert.AreEqual(finished, encoder.Append(Pattern(2), 140).Failure);
        Assert.AreEqual(finished, encoder.Stop(210).Failure);
        encoder.Cancel();
        var discarded = await store.Discard(path);
        Assert.IsTrue(discarded.Succeeded, discarded.Failure?.ToString());
        Assert.IsEmpty(stash.WorkFiles());
    }

    [TestMethod]
    public void AFrameAtThePreviousFramesTimeGivesThatFrameNoDelay()
    {
        using var scratch = new ScratchFolder();
        var path = scratch.File("zero.gif");
        var frames = Enumerable.Range(0, 3).Select(seed => Pattern(seed)).ToList();

        // An original's frame of 0 ms, kept at its own time: neither dropped nor lengthened.
        var clip = Encode(path, frames, [0, 0, 100], stopMs: 200);

        CollectionAssert.AreEqual(new[] { 0, 10, 10 }, Delays(path));
        var decoded = MediaFixtures.Frames(path);
        CollectionAssert.AreEqual(new[] { 0, 100, 100 }, decoded.Select(f => f.DurationMs).ToList());
        for (var i = 0; i < 3; i++)
        {
            CollectionAssert.AreEqual(frames[i].Pixels, decoded[i].Pixels, $"frame {i}");
        }

        Assert.AreEqual(new GifClip(path, 3, 200), clip);
        Assert.AreEqual(new MediaFacts(ImageFormat.Gif, W, H, true, 3, 200), Facts(path));
    }

    [TestMethod]
    public void AFrameEarlierThanThePreviousIsRefusedByNameAndTheClipGoesOn()
    {
        using var scratch = new ScratchFolder();
        var path = scratch.File("times.gif");
        var encoder = Opened(path);

        Assert.AreEqual(new Failure(Action, Clip, "a frame's time is before the clip's start"), encoder.Append(Pattern(0), -1).Failure);
        Assert.IsTrue(encoder.Append(Pattern(0), 70).Succeeded);
        Assert.IsTrue(encoder.Append(Pattern(1), 140).Succeeded);
        var written = encoder.BytesWritten;
        var earlier = new Failure(Action, Clip, "a frame's time is earlier than the previous frame's");
        Assert.AreEqual(earlier, encoder.Append(Pattern(2), 139).Failure);
        Assert.AreEqual(earlier, encoder.Append(Pattern(2), 100).Failure);
        Assert.AreEqual(new Failure(Action, Clip, "Stop's time is earlier than the last frame's"), encoder.Stop(139).Failure);
        Assert.AreEqual(written, encoder.BytesWritten);

        Assert.IsTrue(encoder.Append(Pattern(2), 210).Succeeded);
        var clip = encoder.Stop(280);

        Assert.AreEqual(new GifClip(path, 3, 210), clip.Value);
        CollectionAssert.AreEqual(new[] { 70, 70, 70 }, MediaFixtures.Frames(path).Select(f => f.DurationMs).ToList());
    }

    [TestMethod]
    public void APixelTurnedTransparentIsClearedNotLeftShowingTheFrameBefore()
    {
        using var scratch = new ScratchFolder();
        var path = scratch.File("cleared.gif");
        // BGRA: [opaque red, opaque blue], then [transparent, opaque blue].
        byte[] redBlue = [0, 0, 255, 255, 255, 0, 0, 255];
        byte[] clearBlue = [0, 0, 0, 0, 255, 0, 0, 255];

        Encode(path, [new MediaFrame(2, 1, 0, redBlue), new MediaFrame(2, 1, 0, clearBlue)], [0, 70], stopMs: 140);

        var decoded = MediaFixtures.Frames(path);
        Assert.AreEqual(Color(0, 0, 255), Pixel(decoded[0], 0, 0));
        Assert.AreEqual(0, MediaFixtures.Alpha(decoded[1], 0, 0), "the red pixel is gone");
        Assert.AreEqual(Color(255, 0, 0), Pixel(decoded[1], 1, 0));
        var first = GifImages.Read(path).Images[0];
        Assert.AreEqual((0, 0, 2, 1, GifImages.RestoreToBackground), (first.Left, first.Top, first.Width, first.Height, first.Disposal));
    }

    [TestMethod]
    public void ABlockMovingOverTransparencyLeavesNoTrail()
    {
        using var scratch = new ScratchFolder();
        var path = scratch.File("moving.gif");
        var frames = Enumerable.Range(0, 4).Select(i => Transparent(32, (8 * i, 12, 8, 8, Orange))).ToList();

        Encode(path, frames, [0, 70, 140, 210], stopMs: 280);

        var decoded = MediaFixtures.Frames(path);
        for (var i = 0; i < 4; i++)
        {
            CollectionAssert.AreEqual(Seen(frames[i]), Seen(decoded[i]), $"frame {i}");
            Assert.AreEqual(64, Enumerable.Range(0, 32 * 32).Count(p => decoded[i].Pixels[p * 4 + 3] == 255), $"frame {i}'s opaque pixels");
        }

        // Restoring only a changed rectangle would leave an earlier image showing outside it, so each cleared image is whole.
        var images = GifImages.Read(path).Images;
        Assert.IsTrue(images.Take(3).All(image => image.Disposal == GifImages.RestoreToBackground && (image.Left, image.Top, image.Width, image.Height) == (0, 0, 32, 32)), string.Join(", ", images));
    }

    [TestMethod]
    public void AnOpaqueRegionThatOnlyGrowsIsWrittenAsChangedRectanglesKeptUnderTheNext()
    {
        using var scratch = new ScratchFolder();
        var path = scratch.File("growing.gif");
        var frames = new[] { 8, 12, 16 }.Select(width => Transparent(32, (4, 12, width, 8, Orange))).ToList();

        Encode(path, frames, [0, 70, 140], stopMs: 210);

        var decoded = MediaFixtures.Frames(path);
        for (var i = 0; i < 3; i++)
        {
            CollectionAssert.AreEqual(Seen(frames[i]), Seen(decoded[i]), $"frame {i}");
        }

        CollectionAssert.AreEqual(
            new[] { (0, 0, 32, 32, GifImages.Keep), (12, 12, 4, 8, GifImages.Keep), (16, 12, 4, 8, GifImages.Keep) },
            GifImages.Read(path).Images.Select(image => (image.Left, image.Top, image.Width, image.Height, image.Disposal)).ToList());
    }

    [TestMethod]
    public void AfterAClearingTheNextImageHoldsEveryOpaquePixelOfItsFrame()
    {
        using var scratch = new ScratchFolder();
        var path = scratch.File("still and moving.gif");
        // A still corner throughout; a block that grows, then gives up its left half, then moves. Drawing only what changed
        // after a clearing loses the corner and the block's kept half; restoring the previous canvas brings the half back.
        var corner = (28, 28, 4, 4, Teal);
        var frames = new List<MediaFrame>
        {
            Transparent(32, (0, 0, 8, 8, Orange), corner),
            Transparent(32, (0, 0, 16, 8, Orange), corner),
            Transparent(32, (8, 0, 8, 8, Orange), corner),
            Transparent(32, (16, 8, 8, 8, Orange), corner),
        };

        Encode(path, frames, [0, 70, 140, 210], stopMs: 280);

        var decoded = MediaFixtures.Frames(path);
        for (var i = 0; i < 4; i++)
        {
            CollectionAssert.AreEqual(Seen(frames[i]), Seen(decoded[i]), $"frame {i}");
        }
    }

    [TestMethod]
    public void AClipWrittenToACallersStreamIsTheGifAFileGetsAndTheStreamStaysTheCallers()
    {
        using var scratch = new ScratchFolder();
        var path = scratch.File("file.gif");
        var frames = Enumerable.Range(0, 3).Select(seed => Pattern(seed)).ToList();
        Encode(path, frames, [0, 70, 140], stopMs: 210);
        using var stream = new MemoryStream();
        var encoder = GifEncoder.Open(stream, W, H, Clip).Value!;

        for (var i = 0; i < 3; i++)
        {
            Assert.IsTrue(encoder.Append(frames[i], i * 70L).Succeeded, $"frame {i}");
            Assert.AreEqual(stream.Position, encoder.BytesWritten, $"after frame {i}");
        }

        var clip = encoder.Stop(210);

        Assert.AreEqual(new GifClip(null, 3, 210), clip.Value, clip.Failure?.ToString());
        Assert.AreEqual(stream.Length, encoder.BytesWritten);
        Assert.IsTrue(stream.CanWrite, "the stream is the caller's and stays open");
        CollectionAssert.AreEqual(File.ReadAllBytes(path), stream.ToArray(), "one encoder path: the bytes the file got");
        CollectionAssert.AreEqual(new[] { path }, Directory.GetFiles(scratch.Path), "the stream's clip made no file of its own");
        var copy = scratch.File("stream.gif");
        File.WriteAllBytes(copy, stream.ToArray());
        Assert.AreEqual(new MediaFacts(ImageFormat.Gif, W, H, true, 3, 210), Facts(copy));
    }

    [TestMethod]
    public void CancelOrAnEmptyStopLeavesACallersStreamAsItIs()
    {
        using var cancelled = new MemoryStream();
        var encoder = GifEncoder.Open(cancelled, W, H, Clip).Value!;
        Assert.IsTrue(encoder.Append(Pattern(0), 0).Succeeded);
        Assert.IsTrue(encoder.Append(Pattern(1), 70).Succeeded);
        var written = cancelled.Position;

        encoder.Cancel();

        Assert.AreEqual(written, cancelled.Position);
        Assert.IsTrue(cancelled.CanWrite, "not closed");
        var blocks = GifImages.Read(cancelled.ToArray());
        Assert.HasCount(1, blocks.Images, "the pending frame is dropped");
        Assert.IsFalse(blocks.Trailer);
        using var empty = new MemoryStream();
        var stopped = GifEncoder.Open(empty, W, H, Clip).Value!.Stop(500);
        Assert.IsTrue(stopped.Succeeded && stopped.Value is null, stopped.Failure?.ToString());
        Assert.AreEqual(0, empty.Length);
        Assert.IsTrue(empty.CanWrite);
    }

    [TestMethod]
    public void AStreamThatCannotBeWrittenOrASizeAGifCannotHoldIsRefusedByName()
    {
        using var readOnly = new MemoryStream(new byte[16], writable: false);
        using var stream = new MemoryStream();

        Assert.AreEqual(new Failure(Action, Clip, "its stream cannot be written and measured"), GifEncoder.Open(readOnly, W, H, Clip).Failure);
        Assert.AreEqual(new Failure(Action, Clip, "its size is more than a GIF can hold"), GifEncoder.Open(stream, 65536, 10, Clip).Failure);
        Assert.AreEqual(0, stream.Length);
    }

    [TestMethod]
    public void AFrameOfAnotherSizeOrShortOfPixelsIsRefusedByNameAndTheClipGoesOn()
    {
        using var scratch = new ScratchFolder();
        var path = scratch.File("sizes.gif");
        var encoder = Opened(path);
        Assert.IsTrue(encoder.Append(Pattern(0), 0).Succeeded);
        Assert.IsTrue(encoder.Append(Pattern(1), 70).Succeeded);
        var written = encoder.BytesWritten;

        var narrower = new MediaFrame(W - 1, H, 0, new byte[(W - 1) * H * 4]);
        Assert.AreEqual(new Failure(Action, Clip, $"a frame of {W - 1} × {H} is not the clip's {W} × {H}"), encoder.Append(narrower, 140).Failure);
        var short4 = new MediaFrame(W, H, 0, new byte[W * H * 4 - 4]);
        Assert.AreEqual(new Failure(Action, Clip, "a frame's pixels do not fill its size"), encoder.Append(short4, 140).Failure);
        Assert.AreEqual(written, encoder.BytesWritten);

        var clip = encoder.Stop(140);

        Assert.AreEqual(new GifClip(path, 2, 140), clip.Value);
        var decoded = MediaFixtures.Frames(path);
        CollectionAssert.AreEqual(Pattern(1).Pixels, decoded[1].Pixels);
    }

    [TestMethod]
    public void APathThatCannotBeCreatedIsAFailureNamingTheClipAndCreatesNothing()
    {
        using var scratch = new ScratchFolder();
        var noFolder = scratch.File(Path.Combine("absent", "clip.gif"));
        var aFolder = scratch.File("a folder.gif");
        Directory.CreateDirectory(aFolder);

        var inNoFolder = GifEncoder.Open(noFolder, W, H, Clip, TestStash.ShortRetry);
        var atAFolder = GifEncoder.Open(aFolder, W, H, Clip, TestStash.ShortRetry);

        Assert.AreEqual(new Failure(Action, Clip, "its folder does not exist"), inNoFolder.Failure);
        Assert.AreEqual(new Failure(Action, Clip, "Windows refused access to it"), atAFolder.Failure);
        Assert.IsFalse(Directory.Exists(Path.GetDirectoryName(noFolder)));
        Assert.IsEmpty(Directory.GetFileSystemEntries(aFolder));
    }

    [TestMethod]
    public void ASizeAGifCannotHoldIsRefusedByName()
    {
        using var scratch = new ScratchFolder();
        var path = scratch.File("huge.gif");

        Assert.AreEqual(new Failure(Action, Clip, "its size is more than a GIF can hold"), GifEncoder.Open(path, 65536, 10, Clip, TestStash.ShortRetry).Failure);
        Assert.AreEqual(new Failure(Action, Clip, "its size is more than a GIF can hold"), GifEncoder.Open(path, 10, 0, Clip, TestStash.ShortRetry).Failure);
        Assert.IsFalse(File.Exists(path));
    }

    [TestMethod]
    [SupportedOSPlatform("windows")]
    public async Task AFileHeldBrieflyIsWaitedForAndTheClipGoesOn()
    {
        using var scratch = new ScratchFolder();
        var path = scratch.File("held.gif");
        var encoder = GifEncoder.Open(path, 256, 256, Clip, TestStash.ShortRetry).Value!;
        Assert.IsTrue(encoder.Append(Noise(seed: 1, colours: 4000, side: 256), 0).Succeeded);

        var released = HeldFiles.HeldFor(path, TimeSpan.FromMilliseconds(150));
        var appended = encoder.Append(Noise(seed: 2, colours: 4000, side: 256), 70);
        await released;
        released = HeldFiles.HeldFor(path, TimeSpan.FromMilliseconds(150));
        var stopped = encoder.Stop(140);
        await released;

        Assert.IsTrue(appended.Succeeded, appended.Failure?.ToString());
        Assert.AreEqual(new GifClip(path, 2, 140), stopped.Value, stopped.Failure?.ToString());
        Assert.AreEqual(new MediaFacts(ImageFormat.Gif, 256, 256, true, 2, 140), Facts(path));
    }

    [TestMethod]
    [SupportedOSPlatform("windows")]
    public void AFileHeldPastTheRetryMidClipIsAFailureThatSaysSoAndFinishesTheClip()
    {
        using var scratch = new ScratchFolder();
        var path = scratch.File("refused.gif");
        var encoder = GifEncoder.Open(path, 256, 256, Clip, TestStash.ShortRetry).Value!;
        Assert.IsTrue(encoder.Append(Noise(seed: 1, colours: 4000, side: 256), 0).Succeeded);
        var failed = HeldFiles.WhileLocked(path, () => encoder.Append(Noise(seed: 2, colours: 4000, side: 256), 70));

        Assert.AreEqual(new Failure(Action, Clip, HeldFileRetry.HeldReason), failed.Failure);
        var finished = new Failure(Action, Clip, "the clip is already finished");
        Assert.AreEqual(finished, encoder.Append(Noise(seed: 3, colours: 4000, side: 256), 140).Failure);
        Assert.AreEqual(finished, encoder.Stop(210).Failure);
        encoder.Cancel();
        File.Delete(path);
    }

    [TestMethod]
    [SupportedOSPlatform("windows")]
    public void AStopWhoseLastWriteIsHeldPastTheRetryIsAFailureThatSaysSo()
    {
        using var scratch = new ScratchFolder();
        var path = scratch.File("refused at stop.gif");
        var encoder = Opened(path);
        Assert.IsTrue(encoder.Append(Pattern(0), 0).Succeeded);
        Assert.IsTrue(encoder.Append(Pattern(1), 70).Succeeded);

        var stopped = HeldFiles.WhileLocked(path, () => encoder.Stop(140));

        Assert.AreEqual(new Failure(Action, Clip, HeldFileRetry.HeldReason), stopped.Failure);
        Assert.AreEqual(new Failure(Action, Clip, "the clip is already finished"), encoder.Stop(140).Failure);
        File.Delete(path);
    }

    [TestMethod]
    public void TheBytesWrittenSoFarGrowWithEachImageAndEndAtTheFilesLength()
    {
        using var scratch = new ScratchFolder();
        var path = scratch.File("bytes.gif");
        var encoder = Opened(path);
        var seen = new List<long> { encoder.BytesWritten };

        for (var i = 0; i < 4; i++)
        {
            Assert.IsTrue(encoder.Append(Pattern(i), i * 70L).Succeeded);
            seen.Add(encoder.BytesWritten);
        }

        Assert.IsTrue(encoder.Stop(280).Succeeded);
        seen.Add(encoder.BytesWritten);

        Assert.AreEqual(0, seen[0], "nothing is written at the opening");
        Assert.AreEqual(0, seen[1], "the first frame is pending");
        for (var i = 2; i < seen.Count; i++)
        {
            Assert.IsGreaterThan(seen[i - 1], seen[i], $"after call {i}");
        }

        Assert.AreEqual(new FileInfo(path).Length, seen[^1]);
    }

    [TestMethod]
    public void AfterEachCallTheFileHoldsTheBytesWrittenSoFarAndCancelWritesNothingMore()
    {
        // G15's size so far is on disk when each call returns, nothing of an image held back; a clip finished without
        // Stop is left as it was for Store to discard (State: Files).
        using var scratch = new ScratchFolder();
        var path = scratch.File("on disk.gif");
        var encoder = GifEncoder.Open(path, 256, 256, Clip, TestStash.ShortRetry).Value!;

        for (var i = 0; i < 4; i++)
        {
            Assert.IsTrue(encoder.Append(i % 2 == 0 ? Noise(seed: i, colours: 4000, side: 256) : SyntheticFrames.Pattern(i, 256, 256), i * 70L).Succeeded);
            var onDisk = WhileOpen(path);
            Assert.AreEqual(encoder.BytesWritten, onDisk.LongLength, $"after append {i}");
            if (i > 0)
            {
                // Every image of the frames before this one, whole, and nothing more: the reader fails on a cut image.
                var blocks = GifImages.Read(onDisk);
                Assert.HasCount(i, blocks.Images, $"after append {i}, the images written so far");
                Assert.IsFalse(blocks.Trailer, "no trailer before Stop");
            }
        }

        var before = WhileOpen(path);
        encoder.Cancel();

        Assert.HasCount(3, GifImages.Read(before).Images);
        CollectionAssert.AreEqual(before, File.ReadAllBytes(path), "Cancel's close writes nothing");
        Assert.AreEqual(before.LongLength, encoder.BytesWritten);

        // The clip's file as it is on disk while the encoder still holds it open for writing.
        static byte[] WhileOpen(string path)
        {
            using var reading = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var bytes = new byte[reading.Length];
            reading.ReadExactly(bytes);
            return bytes;
        }
    }

    [TestMethod]
    public void TheEncoderHoldsAFixedFewFramesHoweverLongTheClip()
    {
        const int Width = 1920, Height = 1080, Count = 150, FrameBytes = Width * Height * 4;
        using var scratch = new ScratchFolder();
        var path = scratch.File("long.gif");
        var encoder = GifEncoder.Open(path, Width, Height, Clip, TestStash.ShortRetry).Value!;
        var source = new byte[FrameBytes];
        var heap = new List<long>();

        for (var i = 0; i < Count; i++)
        {
            // One buffer the caller rewrites for every frame: the encoder must have copied the last one.
            Fill(source, Width, 0, 0, Width, Height, (30, 60, 90));
            Fill(source, Width, BlockX(i), 0, 8, 8, (250, 250, 250));
            Assert.IsTrue(encoder.Append(new MediaFrame(Width, Height, 0, source), i * 70L).Succeeded, $"frame {i}");
            if (i >= 2 && i % 25 == 0)
            {
                heap.Add(GC.GetTotalMemory(forceFullCollection: true));
            }
        }

        Assert.IsTrue(encoder.Stop(Count * 70L).Succeeded);
        Assert.IsLessThan(3L * FrameBytes, heap.Max() - heap.Min(), $"the heap after every 25 frames: {string.Join(", ", heap)}");
        using var decoder = MediaDecoder.Open(path, "long", TestStash.ShortRetry).Value!;
        Assert.AreEqual(Count, decoder.FrameCount);
        for (var i = 0; i < Count; i++)
        {
            var frame = decoder.NextFrame(Width).Value!;
            Assert.AreEqual(Color(250, 250, 250), Pixel(frame, BlockX(i) + 4, 4), $"frame {i}'s block");
            if (i > 0)
            {
                Assert.AreEqual(Color(30, 60, 90), Pixel(frame, BlockX(i - 1) + 4, 4), $"frame {i} where the block was");
            }
        }

        static int BlockX(int i) => 8 * i;
    }

    [TestMethod]
    public void ARecordingsOpaqueFramesAreWrittenToThePinnedBytes()
    {
        using var scratch = new ScratchFolder();
        var path = scratch.File("recording.gif");
        // Opaque frames as the recorder hands them over: patterns that one palette holds, noise that needs median cut,
        // a painted change and a repeat (G15). The hash is of what the encoder writes for them with Media's own median
        // cut: a change to it is a change to every recording's file.
        var frames = new[] { Pattern(0), Pattern(1), Noise(7, 400), Paint(Noise(7, 400), 10, 10, 12, 8, Orange), Pattern(1), Noise(8, 300) };

        Encode(path, frames, [0, 70, 140, 210, 280, 350], stopMs: 420);

        Assert.AreEqual("25306ECFC6439826D427BE84B0551020DF5B4742AD29050EC4CEFA03E9B918E5", Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))));
    }

    [TestMethod]
    public void TheClipLoopsForever()
    {
        using var scratch = new ScratchFolder();
        var path = scratch.File("loop.gif");

        Encode(path, [Pattern(0), Pattern(1)], [0, 70], stopMs: 140);

        Assert.AreEqual(0, GifImages.Read(path).LoopCount);
        using var codec = SKCodec.Create(path);
        Assert.AreEqual(-1, codec.RepetitionCount);
    }

    [TestMethod]
    public void NoOtherTypeOfTheProductCallsTheGifLibrary()
    {
        var callers = ProductCalls.Methods()
            .Where(method => ProductCalls.CalledMethods(method).Any(called => called.DeclaringType?.Namespace?.StartsWith("KGySoft", StringComparison.Ordinal) == true))
            .Select(method => ProductCalls.Outermost(method.DeclaringType!).FullName)
            .Distinct()
            .ToList();

        CollectionAssert.AreEqual(new[] { typeof(GifEncoder).FullName }, callers, string.Join(", ", callers));
    }

    private static GifEncoder Opened(string path, int width = W, int height = H)
    {
        var opened = GifEncoder.Open(path, width, height, Clip, TestStash.ShortRetry);
        Assert.IsTrue(opened.Succeeded, opened.Failure?.ToString());
        return opened.Value!;
    }

    /// <summary>Opens a clip of the frames' size at <paramref name="path"/>, appends every frame at its time, stops, and requires each step to succeed.</summary>
    private static GifClip Encode(string path, IReadOnlyList<MediaFrame> frames, long[] timesMs, long stopMs)
    {
        var encoder = Opened(path, frames[0].Width, frames[0].Height);
        for (var i = 0; i < frames.Count; i++)
        {
            var appended = encoder.Append(frames[i], timesMs[i]);
            Assert.IsTrue(appended.Succeeded, appended.Failure?.ToString());
        }

        var stopped = encoder.Stop(stopMs);
        Assert.IsTrue(stopped.Succeeded, stopped.Failure?.ToString());
        return stopped.Value!;
    }

    private static MediaFacts Facts(string path) => MediaDecoder.ReadFacts(path, "clip", TestStash.ShortRetry).Value!.Facts;

    private static List<int> Delays(string path) => GifImages.Read(path).Images.Select(image => image.DelayCs).ToList();

    private static MediaFrame Pattern(int seed) => SyntheticFrames.Pattern(seed, W, H);

    private static MediaFrame Noise(int seed, int colours, int side = 0) => SyntheticFrames.Noise(seed, colours, side > 0 ? side : W, side > 0 ? side : H);

    private static MediaFrame Frame(Func<int, int, (byte B, byte G, byte R)> colour) => SyntheticFrames.Frame(W, H, colour);

    /// <summary>
    /// Fifteen opaque colours: white, a red 20 × 20 square, a 2 × 2 black box and twelve greens of one pixel each, as at a
    /// mark's edge; the library's median cut gives the black box another colour.
    /// </summary>
    private static MediaFrame FifteenColours()
    {
        var marked = Frame((x, y) => y == 40 && x is >= 20 and < 32 ? ((byte)0, (byte)(100 + 10 * (x - 20)), (byte)0) : ((byte)255, (byte)255, (byte)255));
        return Paint(Paint(marked, 5, 5, 20, 20, (0, 0, 255)), 50, 10, 2, 2, (0, 0, 0));
    }

    /// <summary>A <paramref name="side"/> × <paramref name="side"/> frame, fully transparent but for the opaque rectangles given.</summary>
    private static MediaFrame Transparent(int side, params (int Left, int Top, int Width, int Height, (byte B, byte G, byte R) Colour)[] opaque)
    {
        var pixels = new byte[side * side * 4];
        foreach (var (left, top, width, height, colour) in opaque)
        {
            Fill(pixels, side, left, top, width, height, colour);
        }

        return new MediaFrame(side, side, 0, pixels);
    }

    /// <summary>What a frame shows: each pixel as <see cref="Pixel"/> reads it, a fully transparent one as 0 whatever colour it hides.</summary>
    private static List<uint> Seen(MediaFrame frame) =>
        Enumerable.Range(0, frame.Width * frame.Height).Select(i => Pixel(frame, i % frame.Width, i / frame.Width)).Select(pixel => pixel >> 24 == 0 ? 0 : pixel).ToList();
}
