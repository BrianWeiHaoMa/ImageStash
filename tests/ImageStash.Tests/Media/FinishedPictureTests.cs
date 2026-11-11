using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using ImageStash.Files;
using ImageStash.Layers;
using ImageStash.Media;
using ImageStash.Tests.Support;
using SkiaSharp;
using static ImageStash.Tests.Support.SyntheticFrames;

namespace ImageStash.Tests.Media;

/// <summary>
/// An edited item's finished picture: the original's kept frames under its layers, written at the work path in the
/// format the caller names through the one encoder path, the trim's frames at their own timing, the facts read back from
/// the file beside the hash of the original decoded, the original untouched; WebP, a crop off the picture and an output past its format's or encoder's limit
/// refused before anything is created; every failure named, never thrown; the size estimate the same encoders' length;
/// a fixed few frames held (F3, N7, E4, G22, G23, G24, G29, G35, R2, R3, R4, R7, T2, the delay rule).
/// </summary>
[TestClass]
// Rendering a GIF calls the library, which quantizes on the thread pool and stalls when many checks call it at once
// (GifEncoderTests); alone, the heap measures one render (R7).
[DoNotParallelize]
public sealed class FinishedPictureTests
{
    private const string Item = "the audacity";

    private const string Action = "Save the picture";

    private static readonly Rgba Red = new(220, 20, 20, 255);

    private static readonly Rgba Lime = new(30, 220, 30, 255);

    private static readonly (byte B, byte G, byte R) Orange = (0, 140, 255);

    private static readonly uint Blue = Color(255, 0, 0), Green = Color(0, 255, 0), RedPixel = Color(0, 0, 255);

    [TestMethod]
    public async Task AStillIsWrittenAtTheWorkPathAsItsComposedPixelsAndTheOriginalIsUntouched()
    {
        using var stash = new TestStash();
        var work = (await stash.OpenCleanly()).AllocateWorkPath(".png").Value!;
        var original = MediaFixtures.Path("still.png");
        var before = SHA256.HashData(File.ReadAllBytes(original));
        var layers = List(BlackBox(1, 1, 2, 3), Box(4, 1, 3, 4));

        var rendered = Render(original, layers, ImageFormat.Png, work);

        Assert.AreEqual(new MediaFacts(ImageFormat.Png, 8, 6, false, 1, 0), rendered.Value, rendered.Failure?.Sentence);
        var file = MediaFixtures.FirstFrame(work);
        CollectionAssert.AreEqual(Composed("still.png", layers).Pixels, file.Pixels, "the file holds the composed pixels");
        Assert.AreEqual(Color(0, 0, 0), Pixel(file, 2, 3), "the black box");
        Assert.AreEqual(Color(20, 20, 220), Pixel(file, 4, 3), "the box's stroke");
        Assert.AreEqual(Blue, Pixel(file, 0, 5), "the original where no layer is");
        CollectionAssert.AreEqual(new[] { Path.GetFileName(work) }, stash.WorkFiles());
        Assert.IsEmpty(stash.RootFiles());
        CollectionAssert.AreEqual(before, SHA256.HashData(File.ReadAllBytes(original)), "the original's bytes");
    }

    [TestMethod]
    [DataRow("still.png", ImageFormat.Png, "finished.png")]
    [DataRow("animated.gif", ImageFormat.Gif, "finished.gif")]
    public void ARenderAnswersTheHashOfTheOriginalItDecodedBesideTheFactsOfTheFileItWrote(string fixture, ImageFormat format, string finished)
    {
        using var scratch = new ScratchFolder();
        var work = scratch.File(finished);

        var rendered = FinishedPicture.Render(MediaFixtures.Path(fixture), Item, List(new CropLayer(new(0, 0, 4, 3)), Box(1, 1, 2, 2)), format, work, TestStash.ShortRetry);

        Assert.IsTrue(rendered.Succeeded, rendered.Failure?.Sentence);
        CollectionAssert.AreEqual(SHA256.HashData(MediaFixtures.Bytes(fixture)), rendered.Value!.OriginalSha256, "the original's bytes, not the file written");
        Assert.AreEqual(MediaDecoder.ReadFacts(work, Item, TestStash.ShortRetry).Value!.Facts, rendered.Value.Facts, "the facts of the file written");
        Assert.AreEqual((4, 3), (rendered.Value.Facts.Width, rendered.Value.Facts.Height), "cropped, so not the original's facts");
    }

    [TestMethod]
    public void ATransparentPixelKeepsItsAlphaInThePng()
    {
        using var scratch = new ScratchFolder();
        var work = scratch.File("finished.png");

        Assert.IsTrue(Render(MediaFixtures.Path("transparent.png"), List(Box(5, 3, 2, 2)), ImageFormat.Png, work).Succeeded);

        var file = MediaFixtures.FirstFrame(work);
        Assert.AreEqual(128, MediaFixtures.Alpha(file, 1, 1), 1, "the half-transparent pixel");
        Assert.AreEqual(255, file.Pixels[(1 * 8 + 1) * 4 + 2], 1, "its red");
        Assert.AreEqual(Blue, Pixel(file, 7, 0), "an opaque pixel no layer touches");
    }

    [TestMethod]
    public void AJpegIsWrittenAtTheOneQualityAndItsBlackBoxLeavesNothingInside()
    {
        using var scratch = new ScratchFolder();
        var work = scratch.File("finished.jpg");
        var layers = List(BlackBox(0, 0, 24, 24));

        var rendered = Render(MediaFixtures.Path("progressive.jpg"), layers, ImageFormat.Jpeg, work);

        Assert.AreEqual(new MediaFacts(ImageFormat.Jpeg, 32, 32, false, 1, 0), rendered.Value, rendered.Failure?.Sentence);
        var composed = Composed("progressive.jpg", layers);
        CollectionAssert.AreEqual(JpegEncoder.Encode(composed, Item).Value, File.ReadAllBytes(work), "JpegEncoder's bytes at its one quality");
        var file = MediaFixtures.FirstFrame(work);
        var error = Enumerable.Range(0, composed.Pixels.Length).Where(i => i % 4 != 3).Select(i => Math.Abs(composed.Pixels[i] - file.Pixels[i])).ToList();
        Assert.IsLessThan(6.0, error.Average(), $"the mean error over the noise (measured 3.8); max {error.Max()}");

        // The box's corner block of 16 × 16 (JPEG's colour block) borders no uncovered pixel, so nothing rings into it.
        for (var y = 0; y < 16; y++)
        {
            for (var x = 0; x < 16; x++)
            {
                AssertNear(Color(0, 0, 0), Pixel(file, x, y), 4, $"({x}, {y})");
            }
        }
    }

    [TestMethod]
    public void AStillGifIsWrittenAsOneImageThatReadsAsAStill()
    {
        using var scratch = new ScratchFolder();
        var work = scratch.File("finished.gif");

        var rendered = Render(MediaFixtures.Path("still.gif"), List(Box(4, 1, 3, 4)), ImageFormat.Gif, work);

        Assert.AreEqual(new MediaFacts(ImageFormat.Gif, 8, 6, false, 1, 0), rendered.Value, rendered.Failure?.Sentence);
        Assert.HasCount(1, GifImages.Read(work).Images);
        var file = MediaFixtures.FirstFrame(work);
        AssertReddish(Pixel(file, 4, 3), "the box's stroke");
        Assert.AreEqual(Blue, Pixel(file, 0, 5), "the original where no layer is");
    }

    [TestMethod]
    public void AnEditedWebPIsWrittenAsPngForAStillAndAsGifForAnAnimation()
    {
        using var scratch = new ScratchFolder();
        var still = scratch.File("still.png");
        var animation = scratch.File("animation.gif");

        var stillFacts = Render(MediaFixtures.Path("still.webp"), List(Box(4, 1, 3, 4)), ImageFormat.Png, still);
        var animationFacts = Render(MediaFixtures.Path("animated.webp"), List(Box(4, 1, 3, 4), BlackBox(0, 2, 3, 3)), ImageFormat.Gif, animation);

        Assert.AreEqual(new MediaFacts(ImageFormat.Png, 8, 6, false, 1, 0), stillFacts.Value, stillFacts.Failure?.Sentence);
        Assert.AreEqual(new MediaFacts(ImageFormat.Gif, 8, 6, true, 3, 600), animationFacts.Value, animationFacts.Failure?.Sentence);
        CollectionAssert.AreEqual(new[] { 10, 20, 30 }, GifImages.Read(animation).Images.Select(image => image.DelayCs).ToList());
        foreach (var (frame, i) in MediaFixtures.Frames(animation).Select((frame, i) => (frame, i)))
        {
            var hidden = Enumerable.Range(0, 9).Select(n => Pixel(frame, n % 3, 2 + n / 3)).Distinct().ToList();
            Assert.HasCount(1, hidden, $"frame {i}: the black box is one colour");
            AssertNear(Color(0, 0, 0), hidden[0], 16, $"frame {i}: opaque and near black, nothing of red, green or blue");
        }
    }

    [TestMethod]
    public void AnAnimationWhoseFramesFitAPaletteKeepsEveryLayersColourInTheGif()
    {
        using var scratch = new ScratchFolder();
        // White with a red square that moves: so few colours that each composed frame fits one GIF palette.
        var original = Clip(
            scratch.File("few.gif"), 2, i => Frame(64, 64, (x, y) => x >= 5 + 4 * i && x < 25 + 4 * i && y is >= 5 and < 25 ? ((byte)0, (byte)0, (byte)255) : ((byte)255, (byte)255, (byte)255)),
            i => 100 * i, stopMs: 200);
        var pen = new DrawMarkLayer(MarkKind.Pen, [new PixelPoint(40, 40), new PixelPoint(41, 40)], null, new Rgba(0, 160, 0, 255), Thickness.Thin, null);
        var layers = List(pen, BlackBox(50, 10, 2, 2));
        var work = scratch.File("finished.gif");

        Assert.IsTrue(Render(original, layers, ImageFormat.Gif, work).Succeeded);

        var originals = MediaFixtures.Frames(original);
        var finished = MediaFixtures.Frames(work);
        for (var i = 0; i < 2; i++)
        {
            Assert.AreEqual(Color(0, 0, 0), Pixel(finished[i], 50, 10), $"frame {i}: the black box");
            Assert.AreEqual(Color(0, 160, 0), Pixel(finished[i], 40, 40), $"frame {i}: the pen");
            CollectionAssert.AreEqual(Composed(originals[i], layers, i).Pixels, finished[i].Pixels, $"frame {i}: the composed pixels, each colour kept");
        }
    }

    [TestMethod]
    public void ATrimKeepsExactlyItsFramesAtTheirOwnTimingAndARangedLayerShowsOnlyInItsFrames()
    {
        using var scratch = new ScratchFolder();
        var work = scratch.File("finished.gif");
        var original = MediaFixtures.Path("animated.gif");
        var before = File.ReadAllBytes(original);

        var rendered = Render(original, List(new TrimLayer(new(1, 2)), Box(4, 1, 3, 4, new FrameRange(2, 2))), ImageFormat.Gif, work);

        Assert.AreEqual(new MediaFacts(ImageFormat.Gif, 8, 6, true, 2, 500), rendered.Value, rendered.Failure?.Sentence);
        CollectionAssert.AreEqual(new[] { 20, 30 }, GifImages.Read(work).Images.Select(image => image.DelayCs).ToList());
        var frames = MediaFixtures.Frames(work);
        CollectionAssert.AreEqual(new[] { 200, 300 }, frames.Select(frame => frame.DurationMs).ToList());
        Assert.AreEqual(Green, Pixel(frames[0], 4, 3), "the first kept frame, green, without the box ranged on the next");
        AssertReddish(Pixel(frames[1], 4, 3), "the second, with the box");
        Assert.AreEqual(Blue, Pixel(frames[1], 0, 5), "the second kept frame, blue");
        CollectionAssert.AreEqual(before, File.ReadAllBytes(original), "the original keeps every frame");
    }

    [TestMethod]
    public void AFrameOfNoDurationKeepsItsZeroDelay()
    {
        using var scratch = new ScratchFolder();
        var original = Clip(scratch.File("original.gif"), 3, i => Pattern(i, 32, 24), i => i == 2 ? 100 : 0, stopMs: 200);
        var work = scratch.File("finished.gif");
        CollectionAssert.AreEqual(new[] { 0, 100, 100 }, MediaFixtures.Frames(original).Select(frame => frame.DurationMs).ToList());

        var rendered = Render(original, List(Box(4, 4, 6, 6)), ImageFormat.Gif, work);

        Assert.AreEqual(new MediaFacts(ImageFormat.Gif, 32, 24, true, 3, 200), rendered.Value, rendered.Failure?.Sentence);
        CollectionAssert.AreEqual(new[] { 0, 10, 10 }, GifImages.Read(work).Images.Select(image => image.DelayCs).ToList());
    }

    [TestMethod]
    public void ATransparentAnimationShowsNoEarlierFrameThroughIt()
    {
        using var scratch = new ScratchFolder();
        var original = Clip(scratch.File("original.gif"), 4, Block, i => 100L * i, stopMs: 400);
        var work = scratch.File("finished.gif");

        Assert.IsTrue(Render(original, List(Box(24, 24, 6, 6)), ImageFormat.Gif, work).Succeeded);

        var frames = MediaFixtures.Frames(work);
        Assert.HasCount(4, frames);
        for (var i = 0; i < 4; i++)
        {
            Assert.AreEqual(255, MediaFixtures.Alpha(frames[i], 24, 27), $"frame {i}: the box");
            for (var y = 0; y < 20; y++)
            {
                for (var x = 0; x < 32; x++)
                {
                    var inBlock = x >= 8 * i && x < 8 * i + 8 && y < 8;
                    Assert.AreEqual(inBlock ? 255 : 0, MediaFixtures.Alpha(frames[i], x, y), $"frame {i} at ({x}, {y})");
                }
            }
        }

        static MediaFrame Block(int i)
        {
            var pixels = new byte[32 * 32 * 4];
            Fill(pixels, 32, 8 * i, 0, 8, 8, Orange);
            return new MediaFrame(32, 32, 0, pixels);
        }
    }

    [TestMethod]
    public void ALongClipKeepsEveryFrameWithTheOriginalsDelays()
    {
        using var scratch = new ScratchFolder();
        var work = scratch.File("finished.gif");
        var original = MediaFixtures.Path("long.gif");

        var rendered = Render(original, List(Box(4, 1, 3, 4)), ImageFormat.Gif, work);

        Assert.IsTrue(rendered.Succeeded, rendered.Failure?.Sentence);
        Assert.AreEqual(60, rendered.Value!.FrameCount);
        CollectionAssert.AreEqual(GifImages.Read(original).Images.Select(image => image.DelayCs).ToList(), GifImages.Read(work).Images.Select(image => image.DelayCs).ToList());
        var originals = MediaFixtures.Frames(original);
        foreach (var (frame, i) in MediaFixtures.Frames(work).Select((frame, i) => (frame, i)))
        {
            Assert.AreEqual(Pixel(originals[i], 0, 5), Pixel(frame, 0, 5), $"frame {i} is its own frame of the original");
        }
    }

    [TestMethod]
    public void AFailedClipLeavesItsWorkFileClosedForTheCallerToDiscard()
    {
        using var scratch = new ScratchFolder();
        var original = scratch.File("long-last-frame.webp");
        var bytes = MediaFixtures.Bytes("animated.webp");

        // The last ANMF chunk's 24-bit duration, 655,360 ms: a frame longer than GIF's 16-bit delay holds.
        var last = bytes.AsSpan().LastIndexOf("ANMF"u8);
        (bytes[last + 20], bytes[last + 21], bytes[last + 22]) = (0x00, 0x00, 0x0A);
        File.WriteAllBytes(original, bytes);
        var work = scratch.File("finished.gif");

        var rendered = Render(original, List(BlackBox(1, 1, 2, 2)), ImageFormat.Gif, work);

        Assert.AreEqual(new Failure("Write the clip as GIF", Item, "a frame would show for longer than a GIF can hold"), rendered.Failure);
        File.Delete(work);
        Assert.IsFalse(File.Exists(work), "the render let go of the file it created");
    }

    [TestMethod]
    public void TheRenderAndTheEstimateHoldAFixedFewFramesHoweverLongTheClip()
    {
        const int Width = 512, Height = 512, Count = 96, FrameBytes = Width * Height * 4;
        using var scratch = new ScratchFolder();
        var pixels = new byte[FrameBytes];
        var original = Clip(scratch.File("original.gif"), Count, i =>
        {
            Fill(pixels, Width, 0, 0, Width, Height, (30, 60, 90));
            Fill(pixels, Width, 8 * i, 0, 8, 8, (250, 250, 250));
            return new MediaFrame(Width, Height, 0, pixels);
        }, i => 70L * i, stopMs: 70L * Count);
        var layers = List(Box(100, 100, 50, 50));

        var renders = 0;
        var (rendered, renderPeak, renderSamples) = HeapWhile(() => Render(original, layers, ImageFormat.Gif, scratch.File($"finished {++renders}.gif")));
        // Hashed before the heap is watched: the estimate itself hashes through a fixed buffer.
        var originalSha256 = SHA256.HashData(File.ReadAllBytes(original));
        var (estimate, estimatePeak, estimateSamples) = HeapWhile(() => FinishedPicture.Estimate(original, originalSha256, Item, layers, ImageFormat.Gif, TestStash.ShortRetry));

        Assert.AreEqual(Count, rendered.Value?.FrameCount, rendered.Failure?.Sentence);
        Assert.IsTrue(estimate.Succeeded, estimate.Failure?.Sentence);
        Assert.IsLessThan(12L * FrameBytes, renderPeak, $"the render's live heap above where it started over {renderSamples} samples, against {Count} frames of {FrameBytes:N0} bytes");
        Assert.IsLessThan(12L * FrameBytes, estimatePeak, $"the estimate's over {estimateSamples} samples");
        Assert.IsGreaterThan(1, Math.Min(renderSamples, estimateSamples), "the heap was measured while each ran");
    }

    [TestMethod]
    public void AnOrientedPhotoRendersUprightItsLayersOnTheUprightGridAndAHalfShrinkHalvesItsUprightSize()
    {
        // Composition, Media: the original's pixel grid every layer position names is the upright one; G21, G26.
        using var scratch = new ScratchFolder();
        var original = MediaFixtures.Path("oriented-6.jpg");
        var before = MediaFixtures.Bytes("oriented-6.jpg");

        var half = Render(original, List(new ShrinkLayer(Shrink.Half)), ImageFormat.Jpeg, scratch.File("half.jpg"));
        var boxed = Render(original, List(BlackBox(0, 0, 12, 20)), ImageFormat.Jpeg, scratch.File("boxed.jpg"));

        Assert.AreEqual(new MediaFacts(ImageFormat.Jpeg, 12, 20, false, 1, 0), half.Value, half.Failure?.Sentence);
        Assert.AreEqual(new MediaFacts(ImageFormat.Jpeg, 24, 40, false, 1, 0), boxed.Value, boxed.Failure?.Sentence);
        var file = MediaFixtures.FirstFrame(scratch.File("boxed.jpg"));
        AssertNear(Color(0, 0, 0), Pixel(file, 6, 10), 12, "the black box over the upright top-left quadrant");
        var (r, g, b) = MediaFixtures.StoredQuadrants[MediaFixtures.UprightQuadrants[6][1]];
        AssertNear(Color(b, g, r), Pixel(file, 18, 10), 12, "the upright top-right quadrant, uncovered");
        CollectionAssert.AreEqual(before, File.ReadAllBytes(original), "the original's bytes");
    }

    [TestMethod]
    public void ALongClipsEstimateCopiesOutOnlyTheFramesItSamples()
    {
        const int Width = 1280, Height = 720, Count = 900, FrameBytes = Width * Height * 4;
        using var scratch = new ScratchFolder();
        // A nearly static recording: one picture appended at every 70 ms step, so the file is small and its frames many.
        var original = scratch.File("recording.gif");
        var frame = SyntheticFrames.Frame(Width, Height, (_, _) => (180, 180, 180));
        Clip(original, Count, _ => frame, i => 70L * i, stopMs: 70L * Count);
        var originalSha256 = SHA256.HashData(File.ReadAllBytes(original));
        var layers = List(BlackBox(0, 0, 20, 20));

        // Counted on this thread alone, so checks running beside it do not move the figure (R7).
        var before = GC.GetAllocatedBytesForCurrentThread();
        var estimate = FinishedPicture.Estimate(original, originalSha256, Item, layers, ImageFormat.Gif, TestStash.ShortRetry);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.AreEqual(27_984L, estimate.Value, estimate.Failure?.Sentence ?? "the value the walk that copied every frame gave for the same sampled frames (G24)");
        Assert.IsLessThan(20L * FrameBytes, allocated, $"the estimate allocated {allocated:N0} bytes on its thread against {Count} frames of {FrameBytes:N0} bytes");
    }

    [TestMethod]
    public void ACancelledEstimateAnswersNoSize()
    {
        var original = MediaFixtures.Path("long.gif");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        var estimate = FinishedPicture.Estimate(original, SHA256.HashData(File.ReadAllBytes(original)), Item, List(BlackBox(0, 0, 2, 2)), ImageFormat.Gif, TestStash.ShortRetry, cancelled.Token);

        Assert.AreEqual(new Failure(Action, Item, "the estimate was cancelled"), estimate.Failure, "never a size for layers it did not measure (G24)");
    }

    [TestMethod]
    public void ATrimBeyondTheFramesKeepsTheLastAndATrimAtTheStartTheFirst()
    {
        using var scratch = new ScratchFolder();
        var original = MediaFixtures.Path("animated.gif");

        var beyond = Render(original, List(new TrimLayer(new(5, 9))), ImageFormat.Gif, scratch.File("beyond.gif"));
        var start = Render(original, List(new TrimLayer(new(0, 0))), ImageFormat.Gif, scratch.File("start.gif"));

        Assert.AreEqual(new MediaFacts(ImageFormat.Gif, 8, 6, false, 1, 0), beyond.Value, beyond.Failure?.Sentence);
        Assert.AreEqual(Blue, Pixel(MediaFixtures.FirstFrame(scratch.File("beyond.gif")), 0, 0), "the last frame");
        Assert.AreEqual(new MediaFacts(ImageFormat.Gif, 8, 6, false, 1, 0), start.Value, start.Failure?.Sentence);
        Assert.AreEqual(RedPixel, Pixel(MediaFixtures.FirstFrame(scratch.File("start.gif")), 0, 0), "the first frame");
    }

    [TestMethod]
    public void AnEstimateOverAFileThatIsNotTheOriginalIsRefusedByName()
    {
        var original = SHA256.HashData(MediaFixtures.Bytes("photo.jpg"));

        var estimate = FinishedPicture.Estimate(MediaFixtures.Path("still.png"), original, Item, List(Box(1, 1, 2, 2)), ImageFormat.Png, TestStash.ShortRetry);

        Assert.AreEqual(new Failure("Read the picture", Item, MediaDecoder.ChangedReason), estimate.Failure, "the footer never states the size of another file (G24)");
    }

    [TestMethod]
    public void WebPAsAFinishedFormatIsRefusedBeforeTheOriginalIsOpened()
    {
        using var scratch = new ScratchFolder();
        var work = scratch.File("finished.webp");
        var refused = new Failure(Action, Item, "ImageStash writes no WebP");

        Assert.AreEqual(refused, Render(scratch.File("absent.webp"), List(Box(1, 1, 2, 2)), ImageFormat.WebP, work).Failure);
        Assert.AreEqual(refused, Estimate(scratch.File("absent.webp"), List(Box(1, 1, 2, 2)), ImageFormat.WebP).Failure);
        Assert.IsFalse(File.Exists(work));
    }

    [TestMethod]
    public void AnOutputLargerThanItsFormatHoldsOrItsEncoderWritesIsRefusedBeforeAnythingIsCreated()
    {
        using var scratch = new ScratchFolder();
        var gif = Clip(scratch.File("tall.gif"), 1, _ => Frame(1, 65_535, (_, _) => (90, 60, 30)), _ => 0, stopMs: 0);
        var jpeg = scratch.File("tall.jpg");
        var png = scratch.File("tall.png");
        var tallestPng = scratch.File("tallest.png");
        using (var bitmap = new SKBitmap(1, 65_500))
        {
            bitmap.Erase(SKColors.Gray);
            File.WriteAllBytes(jpeg, bitmap.Encode(SKEncodedImageFormat.Jpeg, 90).ToArray());
        }

        using (var bitmap = new SKBitmap(1, 65_536))
        {
            bitmap.Erase(SKColors.Gray);
            File.WriteAllBytes(png, bitmap.Encode(SKEncodedImageFormat.Png, 100).ToArray());
        }

        // The tallest PNG the encoder writes, and SkiaSharp reads.
        using (var bitmap = new SKBitmap(1, 1_000_000))
        {
            bitmap.Erase(SKColors.Gray);
            File.WriteAllBytes(tallestPng, bitmap.Encode(SKEncodedImageFormat.Png, 100).ToArray());
        }

        // A caption bar of one line at 10 pixels adds a band 20 pixels high.
        var banded = List(new CaptionBarLayer("a", "Impact", 10, new Rgba(0, 0, 0, 255), Outline.None));
        var gifRefused = new Failure(Action, Item, "it would be 1 × 65,555 pixels, and a GIF holds at most 65,535 pixels a side");

        Assert.AreEqual(gifRefused, Render(gif, banded, ImageFormat.Gif, scratch.File("gif.gif")).Failure);
        Assert.AreEqual(gifRefused, Estimate(gif, banded, ImageFormat.Gif).Failure);
        Assert.AreEqual(
            new Failure(Action, Item, "it would be 1 × 65,520 pixels, and ImageStash's JPEG encoder writes at most 65,500 pixels a side"),
            Render(jpeg, banded, ImageFormat.Jpeg, scratch.File("jpeg.jpg")).Failure);
        var pngRefused = new Failure(Action, Item, "it would be 1 × 1,000,020 pixels, and ImageStash's PNG encoder writes at most 1,000,000 pixels a side");
        Assert.AreEqual(pngRefused, Render(tallestPng, banded, ImageFormat.Png, scratch.File("png-over.png")).Failure);
        Assert.AreEqual(pngRefused, Estimate(tallestPng, banded, ImageFormat.Png).Failure);
        // A band too high to count answers int.MaxValue: refused, never handed to the surface.
        Assert.AreEqual(
            new Failure(Action, Item, "it would be 8 × 2,147,483,647 pixels, and ImageStash's PNG encoder writes at most 1,000,000 pixels a side"),
            Render(MediaFixtures.Path("still.png"), List(new CaptionBarLayer("a", "Impact", int.MaxValue, new Rgba(0, 0, 0, 255), Outline.None)), ImageFormat.Png, scratch.File("overflow.png")).Failure);
        Assert.IsFalse(new[] { "gif.gif", "jpeg.jpg", "png-over.png", "overflow.png" }.Any(name => File.Exists(scratch.File(name))), "nothing created");
        Assert.AreEqual(new MediaFacts(ImageFormat.Gif, 1, 65_535, false, 1, 0), Render(gif, LayerList.Empty, ImageFormat.Gif, scratch.File("at-the-limit.gif")).Value, "a GIF at the limit");
        Assert.AreEqual(new MediaFacts(ImageFormat.Png, 1, 65_556, false, 1, 0), Render(png, banded, ImageFormat.Png, scratch.File("png.png")).Value, "a PNG holds it");
    }

    [TestMethod]
    public void ACropOffThePictureIsRefusedAndOnePartlyOnItKeepsWhatIsOnIt()
    {
        using var scratch = new ScratchFolder();
        var original = MediaFixtures.Path("still.png");

        var missed = Render(original, List(new CropLayer(new(100, 100, 4, 4))), ImageFormat.Png, scratch.File("missed.png"));
        var partly = Render(original, List(new CropLayer(new(6, 4, 10, 10))), ImageFormat.Png, scratch.File("partly.png"));

        Assert.AreEqual(new Failure(Action, Item, "its crop lies wholly outside the picture"), missed.Failure);
        Assert.IsFalse(File.Exists(scratch.File("missed.png")));
        Assert.AreEqual(new MediaFacts(ImageFormat.Png, 2, 2, false, 1, 0), partly.Value, partly.Failure?.Sentence);
    }

    [TestMethod]
    [SupportedOSPlatform("windows")]
    public void AnUnreadableOrHeldOriginalAndARefusedWorkPathAreNamedNotThrown()
    {
        using var scratch = new ScratchFolder();
        var held = scratch.File("held.png");
        File.Copy(MediaFixtures.Path("still.png"), held);
        var taken = scratch.File("taken.png");
        File.WriteAllBytes(taken, [1, 2, 3]);
        var layers = List(Box(1, 1, 2, 2));

        var unreadable = Render(MediaFixtures.Path("huge.png"), layers, ImageFormat.Png, scratch.File("huge-finished.png"));
        var heldSha256 = SHA256.HashData(File.ReadAllBytes(held));
        var (heldRender, heldEstimate) = HeldFiles.WhileLocked(held, () =>
            (Render(held, layers, ImageFormat.Png, scratch.File("held-finished.png")), FinishedPicture.Estimate(held, heldSha256, Item, layers, ImageFormat.Png, TestStash.ShortRetry)));
        var noFolder = Render(held, layers, ImageFormat.Png, scratch.File(Path.Combine("gone", "finished.png")));
        var noFolderGif = Render(held, layers, ImageFormat.Gif, scratch.File(Path.Combine("gone", "finished.gif")));
        var existing = Render(held, layers, ImageFormat.Png, taken);

        Assert.AreEqual(new Failure("Read the picture", Item, MediaDecoder.UnreadableReason), unreadable.Failure);
        Assert.AreEqual(new Failure("Read the picture", Item, HeldFileRetry.HeldReason), heldRender.Failure);
        Assert.AreEqual(heldRender.Failure, heldEstimate.Failure);
        Assert.IsFalse(File.Exists(scratch.File("huge-finished.png")) || File.Exists(scratch.File("held-finished.png")), "nothing created");
        Assert.AreEqual(new Failure(Action, Item, "its folder does not exist"), noFolder.Failure);
        Assert.AreEqual(new Failure("Write the clip as GIF", Item, "its folder does not exist"), noFolderGif.Failure);
        Assert.AreEqual((Action, Item), (existing.Failure?.Action, existing.Failure?.Subject), existing.Failure?.Sentence);
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, File.ReadAllBytes(taken), "the file already there is left as it was");
    }

    [TestMethod]
    public void TheEstimateIsTheLengthTheSameEncodersWrite()
    {
        using var scratch = new ScratchFolder();
        var hidden = List(BlackBox(1, 1, 2, 3), Box(4, 1, 3, 4));
        foreach (var (fixture, layers, format) in new[]
        {
            ("still.png", hidden, ImageFormat.Png),
            ("progressive.jpg", List(BlackBox(0, 0, 24, 24)), ImageFormat.Jpeg),
            ("animated.gif", hidden, ImageFormat.Gif),
            ("still.gif", hidden, ImageFormat.Gif),
        })
        {
            var work = scratch.File($"{fixture}.{format}");
            Assert.IsTrue(Render(MediaFixtures.Path(fixture), layers, format, work).Succeeded, fixture);

            var estimate = Estimate(MediaFixtures.Path(fixture), layers, format);

            Assert.AreEqual(new FileInfo(work).Length, estimate.Value, $"{fixture}: {estimate.Failure?.Sentence}");
        }
    }

    [TestMethod]
    public void TheEstimateOfALongerClipGrowsWithItsKeptFramesAndScalesToTheFile()
    {
        using var scratch = new ScratchFolder();
        var original = MediaFixtures.Path("long.gif");
        var work = scratch.File("finished.gif");
        Assert.IsTrue(Render(original, List(Box(4, 1, 3, 4)), ImageFormat.Gif, work).Succeeded);

        var twenty = Estimate(original, List(Box(4, 1, 3, 4), new TrimLayer(new(0, 19))), ImageFormat.Gif);
        var sixty = Estimate(original, List(Box(4, 1, 3, 4)), ImageFormat.Gif);

        Assert.IsGreaterThan(twenty.Value, sixty.Value, $"{twenty.Failure?.Sentence} {sixty.Failure?.Sentence}");
        Assert.AreEqual(new FileInfo(work).Length, sixty.Value, new FileInfo(work).Length / 10.0, "sampled and scaled, within a tenth of the file");
    }

    [TestMethod]
    public void TheRenderDrawsAndEncodesOnlyThroughMediasOneRoutineAndEncodersAndReachesNoOtherPart()
    {
        string[] forbidden = ["Avalonia", "ImageStash.Store", "ImageStash.Window", "ImageStash.Capture"];
        var called = ProductCalls.Methods()
            .Where(method => ProductCalls.Outermost(method.DeclaringType!) == typeof(FinishedPicture))
            .SelectMany(ProductCalls.CalledMethods)
            .Select(method => method.DeclaringType)
            .ToHashSet();
        var reached = ProductCalls.ReferencedTypes(typeof(FinishedPicture))
            .Where(used => forbidden.Any(space => used.Namespace is { } name && (name == space || name.StartsWith(space + ".", StringComparison.Ordinal))))
            .Select(used => used.FullName)
            .ToList();

        foreach (var type in new[] { typeof(LayerComposer), typeof(PngEncoder), typeof(JpegEncoder), typeof(GifEncoder), typeof(MediaDecoder) })
        {
            Assert.Contains(type, called);
        }

        Assert.IsEmpty(reached, string.Join(", ", reached));
    }

    [TestMethod]
    public void TextIsRenderedIntoAPngAJpegAndAStillWebPsPngKeepingItsTransparency()
    {
        using var scratch = new ScratchFolder();
        var label = new TextLayer(TextPreset.Label, "M", "Impact", 30, Lime, Outline.None, new(30, 4, 30, 36), 0, null);
        var png = Written(scratch.File("original.png"), SKEncodedImageFormat.Png, _ => { });
        var jpeg = Written(scratch.File("original.jpg"), SKEncodedImageFormat.Jpeg, _ => { });
        var webp = Written(scratch.File("original.webp"), SKEncodedImageFormat.Webp, bitmap => bitmap.Erase(SKColors.Transparent, SKRectI.Create(0, 0, 16, 48)));
        Assert.AreEqual(0, MediaFixtures.Alpha(MediaFixtures.FirstFrame(webp), 2, 2), "the check's WebP original is transparent at its left");

        Assert.IsTrue(Render(png, List(label), ImageFormat.Png, scratch.File("finished.png")).Succeeded);
        Assert.IsTrue(Render(jpeg, List(label), ImageFormat.Jpeg, scratch.File("finished.jpg")).Succeeded);
        Assert.IsTrue(Render(webp, List(label), ImageFormat.Png, scratch.File("finished-webp.png")).Succeeded);

        Assert.IsTrue(Pixels(MediaFixtures.FirstFrame(scratch.File("finished.png"))).Contains(Color(Lime.B, Lime.G, Lime.R)), "the PNG holds the letters' colour");
        Assert.IsTrue(Pixels(MediaFixtures.FirstFrame(scratch.File("finished.jpg"))).Any(pixel => Near(pixel, Lime, 40)), "the JPEG holds it within JPEG's loss");
        var fromWebP = MediaFixtures.FirstFrame(scratch.File("finished-webp.png"));
        Assert.IsTrue(Pixels(fromWebP).Contains(Color(Lime.B, Lime.G, Lime.R)), "the WebP's PNG holds the letters' colour");
        Assert.AreEqual(0, MediaFixtures.Alpha(fromWebP, 2, 2), "and stays transparent where the original is and no letter lies");
        Assert.AreEqual(0, MediaFixtures.Alpha(fromWebP, 10, 40));
    }

    [TestMethod]
    public void TextRangedToTheSecondFrameIsInTheGifsSecondFrameOnly()
    {
        using var scratch = new ScratchFolder();
        var original = Clip(scratch.File("original.gif"), 2, _ => Frame(64, 48, (_, _) => (128, 128, 128)), i => i * 100L, 200);
        var label = new TextLayer(TextPreset.Label, "M", "Impact", 30, Lime, Outline.None, new(20, 4, 30, 36), 0, new FrameRange(1, 1));

        Assert.IsTrue(Render(original, List(label), ImageFormat.Gif, scratch.File("finished.gif")).Succeeded);

        var frames = MediaFixtures.Frames(scratch.File("finished.gif"));
        Assert.HasCount(2, frames);
        Assert.IsFalse(Pixels(frames[0]).Any(pixel => Near(pixel, Lime, 40)), "not in the first frame");
        Assert.IsTrue(Pixels(frames[1]).Any(pixel => Near(pixel, Lime, 40)), "in the second");
    }

    private static Outcome<MediaFacts> Render(string original, LayerList layers, ImageFormat format, string work)
    {
        var rendered = FinishedPicture.Render(original, Item, layers, format, work, TestStash.ShortRetry);
        return rendered.Succeeded ? Outcome<MediaFacts>.Of(rendered.Value!.Facts) : rendered.Failure;
    }

    /// <summary>The estimate of <paramref name="layers"/> over <paramref name="original"/>, given the hash its bytes have now (none when it is absent).</summary>
    private static Outcome<long> Estimate(string original, LayerList layers, ImageFormat format) =>
        FinishedPicture.Estimate(original, File.Exists(original) ? SHA256.HashData(File.ReadAllBytes(original)) : [], Item, layers, format, TestStash.ShortRetry);

    private static LayerList List(params Layer[] layers) => new(layers);

    private static HideLayer BlackBox(int x, int y, int width, int height) => new(new(x, y, width, height), HideKind.Black, null);

    private static DrawMarkLayer Box(int x, int y, int width, int height, FrameRange? range = null) =>
        new(MarkKind.Box, [], new PixelRect(x, y, width, height), Red, Thickness.Thin, range);

    /// <summary>A 64 × 48 opaque grey original written by SkiaSharp in <paramref name="format"/>, after <paramref name="paint"/> changes it.</summary>
    private static string Written(string path, SKEncodedImageFormat format, Action<SKBitmap> paint)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(64, 48, SKColorType.Bgra8888, SKAlphaType.Unpremul));
        bitmap.Erase(new SKColor(128, 128, 128));
        paint(bitmap);
        using var data = bitmap.Encode(format, 100);
        File.WriteAllBytes(path, data.ToArray());
        return path;
    }

    private static IEnumerable<uint> Pixels(MediaFrame frame) => Enumerable.Range(0, frame.Width * frame.Height).Select(i => Pixel(frame, i % frame.Width, i / frame.Width));

    private static bool Near(uint pixel, Rgba color, int tolerance) =>
        Math.Abs((int)(pixel >> 16 & 0xFF) - color.R) <= tolerance && Math.Abs((int)(pixel >> 8 & 0xFF) - color.G) <= tolerance && Math.Abs((int)(pixel & 0xFF) - color.B) <= tolerance;

    private static void AssertReddish(uint pixel, string because)
    {
        Assert.IsGreaterThanOrEqualTo(200u, (pixel >> 16) & 0xFFu, $"{because}: red");
        Assert.IsLessThanOrEqualTo(60u, (pixel >> 8) & 0xFFu, $"{because}: green");
        Assert.IsLessThanOrEqualTo(60u, pixel & 0xFFu, $"{because}: blue");
    }

    /// <summary>Each channel of <paramref name="pixel"/>, alpha included, within <paramref name="tolerance"/> of <paramref name="expected"/>'s.</summary>
    private static void AssertNear(uint expected, uint pixel, int tolerance, string because)
    {
        for (var shift = 0; shift < 32; shift += 8)
        {
            Assert.AreEqual((int)(expected >> shift) & 0xFF, (int)(pixel >> shift) & 0xFF, tolerance, $"{because}: {pixel:X8} against {expected:X8}");
        }
    }

    /// <summary>The fixture's first frame under <paramref name="layers"/>, composed by the one routine onto a surface of the output size and read back.</summary>
    private static MediaFrame Composed(string fixture, LayerList layers) => Composed(MediaFixtures.FirstFrame(MediaFixtures.Path(fixture)), layers, 0);

    /// <summary><paramref name="original"/>, the frame at <paramref name="frameIndex"/>, under <paramref name="layers"/>, composed by the one routine and read back.</summary>
    private static MediaFrame Composed(MediaFrame original, LayerList layers, int frameIndex)
    {
        var size = LayerComposer.OutputSize(original.Width, original.Height, layers);
        var info = new SKImageInfo(size.Width, size.Height, SKColorType.Bgra8888, SKAlphaType.Premul, SKColorSpace.CreateSrgb());
        using var surface = SKSurface.Create(info);
        surface.Canvas.Clear(SKColors.Transparent);
        Assert.IsTrue(LayerComposer.Compose(surface.Canvas, original, layers, frameIndex, Item).Succeeded);
        using var bitmap = new SKBitmap(info.WithAlphaType(SKAlphaType.Unpremul));
        Assert.IsTrue(surface.ReadPixels(bitmap.Info, bitmap.GetPixels(), bitmap.RowBytes, 0, 0));
        return new MediaFrame(size.Width, size.Height, 0, bitmap.Bytes);
    }

    /// <summary>A GIF original written by the one GIF encoder: <paramref name="count"/> frames, each made when it is appended.</summary>
    private static string Clip(string path, int count, Func<int, MediaFrame> frame, Func<int, long> atMs, long stopMs)
    {
        var clip = GifEncoder.Open(path, frame(0).Width, frame(0).Height, "original", TestStash.ShortRetry).Value!;
        for (var i = 0; i < count; i++)
        {
            Assert.IsTrue(clip.Append(frame(i), atMs(i)).Succeeded, $"frame {i}");
        }

        Assert.IsNotNull(clip.Stop(stopMs).Value);
        return path;
    }

    /// <summary>
    /// <paramref name="call"/>'s result, and the most the live heap held above where it started at a full collection made
    /// from another thread while a run of the call was in progress, with how many such collections there were. Late in a
    /// whole run a collection, waiting for the finalizers then pending, takes as long as the estimate, so collections made
    /// on a timer may miss a run altogether: one counts only when it was asked for before the run returned and the call's
    /// own thread saw it happen between the run's start and its return, and the call runs again until four have counted,
    /// one of them asked for in the later half of a run, where a call that kept every frame it read would hold most of
    /// them (R7).
    /// </summary>
    private static (T Result, long Peak, int Samples) HeapWhile<T>(Func<T> call)
    {
        const int Wanted = 4, MostRuns = 1_000;
        var start = Collected().Live;
        var collections = new ConcurrentQueue<(long Index, long At, long Live)>();
        using var done = new ManualResetEventSlim();
        var sampler = new Thread(() =>
        {
            while (!done.IsSet)
            {
                collections.Enqueue(Collected());
                Thread.Sleep(5);
            }
        });
        sampler.Start();
        var runs = new List<(long Before, long After, long From, long To)>();
        List<(long Live, bool Late)> inRuns;
        T result;
        try
        {
            do
            {
                Assert.IsLessThan(MostRuns, runs.Count, $"{Wanted} collections, one late in a run, did not land inside {MostRuns} runs");
                var before = GC.GetGCMemoryInfo(GCKind.Any).Index;
                var from = Stopwatch.GetTimestamp();
                result = call();
                var to = Stopwatch.GetTimestamp();
                runs.Add((before, GC.GetGCMemoryInfo(GCKind.Any).Index, from, to));
                inRuns = [.. from c in collections from r in runs where r.Before < c.Index && c.Index <= r.After && c.At < r.To select (c.Live, 2 * (c.At - r.From) >= r.To - r.From)];
            }
            while (inRuns.Count < Wanted || !inRuns.Any(c => c.Late));
        }
        finally
        {
            done.Set();
            sampler.Join();
        }

        return (result, inRuns.Max(c => c.Live) - start, inRuns.Count);
    }

    /// <summary>A full blocking collection made now: its index among the process's collections, when it was asked for, and the live heap it left.</summary>
    private static (long Index, long At, long Live) Collected()
    {
        var at = Stopwatch.GetTimestamp();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true);
        var info = GC.GetGCMemoryInfo(GCKind.FullBlocking);
        return (info.Index, at, info.HeapSizeBytes - info.FragmentedBytes);
    }
}
