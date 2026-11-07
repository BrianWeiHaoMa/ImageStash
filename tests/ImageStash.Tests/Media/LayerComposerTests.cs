using System.Reflection;
using ImageStash.Layers;
using ImageStash.Media;
using ImageStash.Tests.Support;
using SkiaSharp;
using static ImageStash.Tests.Support.Compositions;
using static ImageStash.Tests.Support.SyntheticFrames;

namespace ImageStash.Tests.Media;

/// <summary>
/// Media's one drawing routine (R4) in Composition's order: the crop cuts the original's rectangle without moving a
/// layer; hide, draw and text layers draw in list order inside it, each only in its frames; caption bars add white bands
/// above; Shrink scales the result, filtered premultiplied; the output size is the one the routine draws (G21, G22,
/// G23, G24, What crosses every boundary).
/// </summary>
[TestClass]
public sealed class LayerComposerTests
{
    private static readonly Rgba Red = new(200, 30, 30, 255);
    private static readonly uint Black = Color(0, 0, 0);
    private static readonly uint White = 0xFFFFFFFF;
    private static readonly MediaFrame Grey = Frame(64, 32, (_, _) => (128, 128, 128));

    [TestMethod]
    public void ACropCutsExactlyItsRectangle()
    {
        var original = Pattern(0, 32, 24);
        var layers = new LayerList([new CropLayer(new(8, 4, 16, 12))]);

        var composed = Composed(original, layers);

        Assert.AreEqual(new PixelSize(16, 12), LayerComposer.OutputSize(32, 24, layers));
        Assert.AreEqual(Pixel(original, 8, 4), Pixel(composed, 0, 0));
        Assert.AreEqual(Pixel(original, 23, 15), Pixel(composed, 15, 11));
        AssertEveryPixel(composed, (x, y) => Pixel(original, x + 8, y + 4), "the crop's rectangle of the original, pixel for pixel");
    }

    [TestMethod]
    public void ACropNeverMovesALayer()
    {
        var original = Pattern(0, 32, 24);
        var box = new DrawMarkLayer(MarkKind.Box, [], new(10, 6, 4, 4), Red, Thickness.Thick, null);

        var whole = Composed(original, new LayerList([box]));
        var cropped = Composed(original, new LayerList([box, new CropLayer(new(8, 4, 16, 12))]));

        Assert.IsTrue(Enumerable.Range(0, 32).Any(x => Pixel(whole, x, 6) != Pixel(original, x, 6)), "the box is drawn");
        AssertEveryPixel(cropped, (x, y) => Pixel(whole, x + 8, y + 4), "the box falls on the same pixels of the original with the crop and without it");
    }

    [TestMethod]
    public void ACropWiderThanThePictureKeepsWhatIsLeftAndOneThatMissesItDrawsNothing()
    {
        var original = Pattern(0, 32, 24);
        var past = new LayerList([new CropLayer(new(24, 20, 100, 100))]);
        var missing = new LayerList([new CropLayer(new(40, 0, 8, 8)), new CaptionBarLayer("x", "Impact", 16, Red, Outline.None)]);

        Assert.AreEqual(new PixelSize(8, 4), LayerComposer.OutputSize(32, 24, past));
        AssertEveryPixel(Composed(original, past), (x, y) => Pixel(original, x + 24, y + 20), "the part of the crop on the picture");
        Assert.AreEqual(new PixelSize(0, 0), LayerComposer.OutputSize(32, 24, missing));
        using var surface = Surface(4, 4);
        surface.Canvas.Clear(SKColors.White);
        var drawn = LayerComposer.Compose(surface.Canvas, original, missing, 0, "the audacity");
        Assert.IsTrue(drawn.Succeeded, drawn.Failure?.Sentence);
        AssertEveryPixel(ReadBack(surface), (_, _) => White, "nothing drawn");
    }

    [TestMethod]
    public void ABlackBoxIsOneOpaqueColourOverExactlyItsRectangle()
    {
        var original = Pattern(0, 32, 24);
        Assert.IsFalse(Enumerable.Range(0, 32 * 24).Any(i => Pixel(original, i % 32, i / 32) == Black), "the pattern holds no black");

        var composed = Composed(original, new LayerList([new HideLayer(new(4, 4, 8, 8), HideKind.Black, null)]));

        AssertEveryPixel(composed, (x, y) => Inside(x, y, 4, 4, 8, 8) ? Black : Pixel(original, x, y), "black inside, the original outside");
    }

    [TestMethod]
    public void ABlurChangesItsRectangleAndNothingElse()
    {
        var original = Pattern(0, 32, 24);

        var composed = Composed(original, new LayerList([new HideLayer(new(4, 4, 8, 8), HideKind.Blur, null)]));

        var changed = Enumerable.Range(0, 64).Count(i => Pixel(composed, 4 + i % 8, 4 + i / 8) != Pixel(original, 4 + i % 8, 4 + i / 8));
        Assert.IsGreaterThan(48, changed, "most of the rectangle is blurred");
        AssertEveryPixel(composed, (x, y) => Inside(x, y, 4, 4, 8, 8) ? Pixel(composed, x, y) : Pixel(original, x, y), "outside, the original");
        AssertOpaque(composed, "a blur of an opaque picture stays opaque");
    }

    [TestMethod]
    public void ABlurOverABlackBoxBlursTheBlackNotWhatTheBoxCovers()
    {
        var original = Pattern(0, 32, 24);

        var composed = Composed(original, new LayerList(
        [
            new HideLayer(new(4, 4, 16, 16), HideKind.Black, null),
            new HideLayer(new(8, 8, 8, 8), HideKind.Blur, null),
        ]));

        AssertEveryPixel(composed, (x, y) => Inside(x, y, 4, 4, 16, 16) ? Black : Pixel(original, x, y), "the box leaves nothing of what it covers");
    }

    [TestMethod]
    public void ABlurBelowABlackBoxSpreadsNothingTheBoxCovers()
    {
        var plain = Frame(64, 64, (_, _) => (255, 255, 255));
        var secret = Paint(plain, 24, 24, 16, 16, (0, 0, 255));
        var layers = new LayerList(
        [
            new HideLayer(new(8, 8, 48, 48), HideKind.Blur, null),
            new HideLayer(new(24, 24, 16, 16), HideKind.Black, null),
        ]);

        var overPlain = Composed(plain, layers);
        var overSecret = Composed(secret, layers);

        AssertEveryPixel(overSecret, (x, y) => Pixel(overPlain, x, y), "nothing of what the box covers survives anywhere");
        Assert.AreEqual(Black, Pixel(overSecret, 30, 30));
    }

    [TestMethod]
    public void ARectangleLandsOnItsPixelsBeyondFloatPrecision()
    {
        // Past 2^24 a float no longer holds every whole number.
        const int Width = 16_777_220, Secret = 16_777_217;
        var original = Frame(Width, 1, (x, _) => x == Secret ? ((byte)0, (byte)0, (byte)255) : ((byte)255, (byte)255, (byte)255));
        var box = new HideLayer(new(Secret, 0, 1, 1), HideKind.Black, null);

        var untouched = Composed(original, LayerList.Empty);
        var covered = Composed(original, new LayerList([box]));
        var cropped = Composed(original, new LayerList([box, new CropLayer(new(Secret - 1, 0, 3, 1))]));

        Assert.AreEqual(Color(0, 0, 255), Pixel(untouched, Secret, 0), "the picture is copied pixel for pixel");
        Assert.AreEqual(Color(255, 255, 255), Pixel(untouched, Secret - 1, 0));
        Assert.AreEqual(Black, Pixel(covered, Secret, 0), "the box covers its pixel");
        Assert.AreEqual(Color(255, 255, 255), Pixel(covered, Secret - 1, 0), "and only that pixel");
        Assert.AreEqual(Color(255, 255, 255), Pixel(covered, Secret + 1, 0));
        CollectionAssert.AreEqual(new[] { Color(255, 255, 255), Black, Color(255, 255, 255) }, Enumerable.Range(0, 3).Select(x => Pixel(cropped, x, 0)).ToArray(), "cropped there");
    }

    [TestMethod]
    public void ABlurPartlyOutsideThePictureChangesOnlyThePictureInsideIt()
    {
        var original = Pattern(0, 32, 24);

        var composed = Composed(original, new LayerList([new HideLayer(new(20, 12, 40, 40), HideKind.Blur, null)]));

        Assert.IsTrue(Enumerable.Range(20, 12).Any(x => Pixel(composed, x, 20) != Pixel(original, x, 20)), "the part on the picture is blurred");
        AssertEveryPixel(composed, (x, y) => Inside(x, y, 20, 12, 12, 12) ? Pixel(composed, x, y) : Pixel(original, x, y), "outside, the original");
        AssertOpaque(composed, "the edge of the picture is not blurred into transparency");
    }

    [TestMethod]
    public void LayersDrawInListOrderBottomToTop()
    {
        var red = new DrawMarkLayer(MarkKind.Pen, [new(4, 16), new(60, 16)], null, new(255, 0, 0, 255), Thickness.Medium, null);
        var blue = red with { Color = new(0, 0, 255, 255) };
        var box = new HideLayer(new(20, 8, 16, 16), HideKind.Black, null);

        var redThenBlue = Composed(Grey, new LayerList([red, blue]));
        var strokeThenBox = Composed(Grey, new LayerList([red, box]));
        var boxThenStroke = Composed(Grey, new LayerList([box, red]));

        Assert.AreEqual(Color(255, 0, 0), Pixel(redThenBlue, 10, 16), "blue, listed above red, covers it");
        Assert.AreEqual(Black, Pixel(strokeThenBox, 28, 16), "a box listed after the stroke covers it");
        Assert.AreEqual(Color(0, 0, 255), Pixel(boxThenStroke, 28, 16), "a stroke listed after the box shows over it");
        Assert.AreEqual(Black, Pixel(boxThenStroke, 28, 10), "and the box is there beside it");
    }

    [TestMethod]
    [DataRow(Thickness.Thin)]
    [DataRow(Thickness.Medium)]
    [DataRow(Thickness.Thick)]
    public void APenStrokeCarriesItsColourAlongItsPoints(Thickness thickness)
    {
        var composed = Composed(Grey, new LayerList([new DrawMarkLayer(MarkKind.Pen, [new(4, 16), new(30, 16), new(30, 4)], null, Red, thickness, null)]));

        foreach (var (x, y) in new[] { (4, 16), (17, 16), (30, 16), (30, 10), (30, 4) })
        {
            Assert.AreEqual(Color(30, 30, 200), Pixel(composed, x, y), $"({x}, {y}) on the stroke");
        }
    }

    [TestMethod]
    public void AThickerStepCoversMorePixels()
    {
        int Covered(Thickness thickness, MarkKind kind) =>
            Changed(Composed(Grey, new LayerList([new DrawMarkLayer(kind, [new(8, 16), new(56, 16)], null, Red, thickness, null)])));

        foreach (var kind in new[] { MarkKind.Pen, MarkKind.Marker, MarkKind.Arrow })
        {
            Assert.IsLessThan(Covered(Thickness.Medium, kind), Covered(Thickness.Thin, kind), $"{kind}: thin under medium");
            Assert.IsLessThan(Covered(Thickness.Thick, kind), Covered(Thickness.Medium, kind), $"{kind}: medium under thick");
        }

        Assert.IsLessThan(Covered(Thickness.Thin, MarkKind.Marker), Covered(Thickness.Thin, MarkKind.Pen), "a marker is wider than a pen");
    }

    [TestMethod]
    [DataRow(Thickness.Thin)]
    [DataRow(Thickness.Thick)]
    public void AMarkerIsTranslucent(Thickness thickness)
    {
        var composed = Composed(Grey, new LayerList([new DrawMarkLayer(MarkKind.Marker, [new(8, 16), new(56, 16)], null, Red, thickness, null)]));

        var pixel = Pixel(composed, 32, 16);
        Assert.AreNotEqual(Color(30, 30, 200), pixel, "not the colour");
        Assert.AreNotEqual(Pixel(Grey, 32, 16), pixel, "not the background");
        Assert.AreEqual(255u, pixel >> 24, "over an opaque picture, still opaque");
    }

    [TestMethod]
    [DataRow(Thickness.Thin, 2)]
    [DataRow(Thickness.Medium, 4)]
    [DataRow(Thickness.Thick, 8)]
    public void AnArrowHasAHeadAtItsSecondPoint(Thickness thickness, int width)
    {
        var composed = Composed(Grey, new LayerList([new DrawMarkLayer(MarkKind.Arrow, [new(4, 16), new(60, 16)], null, Red, thickness, null)]));

        // Three widths back from the tip the head is three widths across; the shaft is one.
        Assert.AreEqual(Color(30, 30, 200), Pixel(composed, 60 - 3 * width, 16 - width), "the head, beside the second point");
        Assert.AreEqual(Color(30, 30, 200), Pixel(composed, 60 - 3 * width, 16 + width), "the head, on its other side");
        Assert.AreEqual(Pixel(Grey, 0, 0), Pixel(composed, 4 + width, 16 - width), "no head at the first point");
        Assert.AreEqual(Color(30, 30, 200), Pixel(composed, 4 + width, 16), "the shaft");
    }

    [TestMethod]
    [DataRow(Thickness.Thin)]
    [DataRow(Thickness.Medium)]
    [DataRow(Thickness.Thick)]
    public void ABoxIsAnOutlineNotAFill(Thickness thickness)
    {
        var composed = Composed(Grey, new LayerList([new DrawMarkLayer(MarkKind.Box, [], new(8, 4, 40, 24), Red, thickness, null)]));

        foreach (var (x, y) in new[] { (8, 16), (47, 16), (28, 4), (28, 27) })
        {
            Assert.AreEqual(Color(30, 30, 200), Pixel(composed, x, y), $"({x}, {y}) on an edge");
        }

        Assert.AreEqual(Pixel(Grey, 28, 16), Pixel(composed, 28, 16), "its centre is the picture's");
    }

    [TestMethod]
    public void AMarkReachingFarOutsideThePictureIsClipped()
    {
        var layers = new LayerList(
        [
            new DrawMarkLayer(MarkKind.Pen, [new(-100_000, -100_000), new(100_000, 100_000)], null, Red, Thickness.Thick, null),
            new DrawMarkLayer(MarkKind.Box, [], new(int.MaxValue - 10, 0, 10, int.MaxValue), Red, Thickness.Thick, null),
            new HideLayer(new(int.MaxValue - 1, int.MaxValue - 1, int.MaxValue, int.MaxValue), HideKind.Blur, null),
        ]);

        var composed = Composed(Grey, layers);

        Assert.AreEqual(new PixelSize(64, 32), new PixelSize(composed.Width, composed.Height));
        Assert.AreEqual(Color(30, 30, 200), Pixel(composed, 16, 16), "the diagonal crosses the picture");
    }

    [TestMethod]
    public void ACaptionBarAddsAWhiteBandOfItsHeightAboveThePicture()
    {
        var original = Pattern(0, 160, 24);
        var oneLine = new LayerList([new CaptionBarLayer("me, later", "Segoe UI", 16, new(0, 0, 0, 255), Outline.None)]);
        var twoLines = new LayerList([new CaptionBarLayer("me,\nlater", "Segoe UI", 16, new(0, 0, 0, 255), Outline.None)]);
        var both = new LayerList([.. oneLine.Layers, .. twoLines.Layers]);

        var band = LayerComposer.OutputSize(160, 24, oneLine).Height - 24;
        var taller = LayerComposer.OutputSize(160, 24, twoLines).Height - 24;
        var composed = Composed(original, oneLine);

        Assert.AreEqual(160, LayerComposer.OutputSize(160, 24, oneLine).Width);
        Assert.AreEqual((16 * 20 + 9) / 10, band, "one line: 1.2 font sizes, and 0.8 of padding");
        Assert.AreEqual((16 * 32 + 9) / 10, taller, "a line break adds a line");
        Assert.AreEqual(new PixelSize(160, 24 + band + taller), LayerComposer.OutputSize(160, 24, both), "each bar adds its own band");
        AssertEveryPixel(composed, (x, y) => y < band ? Lettered(composed, x, y) : Pixel(original, x, y - band), "the white band and its black letters, then the picture directly below");
        Assert.IsTrue(Enumerable.Range(0, 160 * band).Any(i => Pixel(composed, i % 160, i / 160) != White), "the band holds the caption");
        var two = Composed(original, both);
        AssertEveryPixel(two, (x, y) => y < band + taller ? Lettered(two, x, y) : Pixel(original, x, y - band - taller), "two bands above the picture");
    }

    [TestMethod]
    public void ACaptionWrapsAtThePicturesWidthAndItsBandCountsTheLaidOutLines()
    {
        var bar = new CaptionBarLayer("me, later", "Segoe UI", 16, new(0, 0, 0, 255), Outline.None);
        var wrapped = new CaptionBarLayer("me, later me, later me, later", "Segoe UI", 16, new(0, 0, 0, 255), Outline.None);

        var band = LayerComposer.OutputSize(160, 24, new LayerList([bar])).Height - 24;
        var longer = LayerComposer.OutputSize(160, 24, new LayerList([wrapped])).Height - 24;
        var narrow = LayerComposer.OutputSize(32, 24, new LayerList([bar])).Height - 24;

        Assert.AreEqual((16 * 20 + 9) / 10, band, "one line at 160 pixels");
        Assert.AreEqual((16 * 32 + 9) / 10, longer, "the long text wraps onto a second line, one line pitch taller");
        Assert.AreEqual((16 * 32 + 9) / 10, narrow, "at 32 pixels \"me,\" and \"later\" take a line each");
        Assert.AreEqual(longer, LayerComposer.Placement(160, 24, new LayerList([wrapped]))!.Value.Top, "the placement reads the same rule");
        var composed = Composed(Pattern(0, 160, 24), new LayerList([wrapped]));
        Assert.IsTrue(Enumerable.Range(0, 160).Any(x => Pixel(composed, x, band) != White), "the second line is drawn below the first line's band");
    }

    [TestMethod]
    public void BandsAreWhereTheCaptionsAreDrawnTheFirstDirectlyAboveThePicture()
    {
        var original = Pattern(0, 160, 24);
        Rgba blue = new(20, 20, 200, 255), red = new(200, 20, 20, 255);
        var layers = new LayerList(
        [
            new CaptionBarLayer("lower", "Segoe UI", 16, blue, Outline.None),
            new HideLayer(new(0, 0, 8, 8), HideKind.Black, null),
            new CaptionBarLayer("upper\nhigher", "Segoe UI", 12, red, Outline.None),
        ]);

        var bands = LayerComposer.Bands(160, 24, layers);
        var composed = Composed(original, layers);

        int lower = (16 * 20 + 9) / 10, upper = (12 * 32 + 9) / 10;
        CollectionAssert.AreEqual(new[] { (0, new PixelRect(0, -lower, 160, lower)), (2, new PixelRect(0, -lower - upper, 160, upper)) }, bands.ToList());
        Assert.AreEqual(lower + upper, LayerComposer.Placement(160, 24, layers)!.Value.Top);
        Assert.AreEqual(new PixelSize(160, 24 + lower + upper), LayerComposer.OutputSize(160, 24, layers));
        for (var y = 0; y < upper + lower; y++)
        {
            for (var x = 0; x < 160; x++)
            {
                var (b, _, r) = Channels(Pixel(composed, x, y));
                Assert.IsFalse(y < upper ? b > r : r > b, $"({x}, {y}): each caption's letters in its own band");
            }
        }

        Assert.IsTrue(Enumerable.Range(0, 160 * upper).Any(i => Channels(Pixel(composed, i % 160, i / 160)).R > 150 && Channels(Pixel(composed, i % 160, i / 160)).B < 100), "the upper band holds red letters");
        Assert.IsTrue(Enumerable.Range(160 * upper, 160 * lower).Any(i => Channels(Pixel(composed, i % 160, i / 160)).B > 150 && Channels(Pixel(composed, i % 160, i / 160)).R < 100), "the lower band holds blue letters");
    }

    [TestMethod]
    public void BandsFollowTheCropAndAreEmptyWithoutABarOrAPicture()
    {
        var bar = new CaptionBarLayer("x", "Impact", 10, new(0, 0, 0, 255), Outline.None);
        var height = (10 * 20 + 9) / 10;

        CollectionAssert.AreEqual(new[] { (1, new PixelRect(8, 4 - height, 16, height)) }, LayerComposer.Bands(32, 24, new LayerList([new CropLayer(new(8, 4, 16, 12)), bar])).ToList());
        Assert.IsEmpty(LayerComposer.Bands(32, 24, LayerList.Empty));
        Assert.IsEmpty(LayerComposer.Bands(32, 24, new LayerList([new CropLayer(new(40, 0, 8, 8)), bar])));
    }

    [TestMethod]
    public void AHugeCaptionIsLaidOutAtOnceWithACharacterOnEachLine()
    {
        var bar = new CaptionBarLayer("abcdefghijklmnopqrst", "Segoe UI", 100_000, new(0, 0, 0, 255), Outline.None);
        var clock = System.Diagnostics.Stopwatch.StartNew();

        var size = LayerComposer.OutputSize(64, 48, new LayerList([bar]));

        Assert.IsLessThan(1000, clock.ElapsedMilliseconds, "answered within a second");
        Assert.AreEqual(new PixelSize(64, 48 + (100_000 * (12 * 20 + 8) + 9) / 10), size, "twenty lines, one character each");
    }

    [TestMethod]
    public void NoLayerReachesIntoTheBand()
    {
        var original = Pattern(0, 32, 24);
        var layers = new LayerList(
        [
            new CaptionBarLayer("x", "Impact", 16, new(0, 0, 0, 255), Outline.None),
            new HideLayer(new(0, 0, 32, 8), HideKind.Black, null),
            new HideLayer(new(0, 8, 32, 8), HideKind.Blur, null),
            new DrawMarkLayer(MarkKind.Pen, [new(0, 0), new(31, 0)], null, Red, Thickness.Thick, null),
        ]);

        var band = LayerComposer.OutputSize(32, 24, layers).Height - 24;
        var composed = Composed(original, layers);
        var captionAlone = Composed(original, new LayerList([layers.Layers[0]]));

        AssertEveryPixel(composed, (x, y) => y < band ? Pixel(captionAlone, x, y) : Pixel(composed, x, y), "the band holds its caption and nothing of another layer");
        Assert.AreEqual(Black, Pixel(composed, 16, band + 6), "the box is on the picture, below the band");
    }

    [TestMethod]
    public void ShrinkScalesTheSizeAndThePictureByOneRule()
    {
        var halves = Frame(32, 24, (x, _) => x < 16 ? ((byte)0, (byte)0, (byte)255) : ((byte)255, (byte)0, (byte)0));
        var threeQuarter = new LayerList([new ShrinkLayer(Shrink.ThreeQuarter)]);
        var half = new LayerList([new ShrinkLayer(Shrink.Half)]);

        Assert.AreEqual(new PixelSize(24, 18), LayerComposer.OutputSize(32, 24, threeQuarter));
        Assert.AreEqual(new PixelSize(16, 12), LayerComposer.OutputSize(32, 24, half));
        Assert.AreEqual(new PixelSize(25, 19), LayerComposer.OutputSize(33, 25, threeQuarter), "33 × 3/4 = 24.75 and 25 × 3/4 = 18.75 round to the nearest pixel");
        Assert.AreEqual(new PixelSize(17, 13), LayerComposer.OutputSize(33, 25, half), "halves round up");
        Assert.AreEqual(new PixelSize(1, 1), LayerComposer.OutputSize(1, 1, half), "never below one pixel");
        Assert.AreEqual(
            new PixelSize(2, 1_100_000_002),
            LayerComposer.OutputSize(4, 4, new LayerList([new CaptionBarLayer("x", "Impact", 1_100_000_000, Red, Outline.None), .. half.Layers])),
            "a side past int.MaxValue before Shrink is counted whole, then halved");
        var composed = Composed(halves, half);
        AssertEveryPixel(composed, (x, _) => x < 8 ? Color(0, 0, 255) : Color(255, 0, 0), "the split stays at the middle");
        var quarter = Composed(halves, threeQuarter);
        Assert.AreEqual(Color(0, 0, 255), Pixel(quarter, 5, 9));
        Assert.AreEqual(Color(255, 0, 0), Pixel(quarter, 18, 9));
    }

    [TestMethod]
    public void ShrinkScalesTheBandWithThePicture()
    {
        var original = Pattern(0, 32, 24);
        var bar = new CaptionBarLayer("me, later", "Segoe UI", 16, new(0, 0, 0, 255), Outline.None);
        var band = LayerComposer.OutputSize(32, 24, new LayerList([bar])).Height - 24;

        var layers = new LayerList([bar, new ShrinkLayer(Shrink.Half)]);
        var composed = Composed(original, layers);

        Assert.AreEqual(new PixelSize(16, (24 + band + 1) / 2), LayerComposer.OutputSize(32, 24, layers));
        AssertEveryPixel(composed, (x, y) => y < band / 2 ? Lettered(composed, x, y) : Pixel(composed, x, y), "the band and its black letters, scaled");
        Assert.AreEqual(Pixel(original, 4, 4), Pixel(composed, 2, band / 2 + 2), "then the scaled picture");
    }

    [TestMethod]
    public void ShrinkFiltersOnPremultipliedPixels()
    {
        var composed = Composed(MediaFixtures.FirstFrame(MediaFixtures.Path("invisible-blue.png")), new LayerList([new ShrinkLayer(Shrink.Half)]));

        Assert.AreEqual(1, composed.Width);
        Assert.IsLessThanOrEqualTo(3, (int)composed.Pixels[0], "no blue from the transparent pixel's hidden colour");
        Assert.IsGreaterThanOrEqualTo(250, (int)composed.Pixels[2], "the red pixel stays red");
    }

    [TestMethod]
    public void TransparencyIsKept()
    {
        var original = MediaFixtures.FirstFrame(MediaFixtures.Path("transparent.png"));

        var composed = Composed(original, new LayerList([new HideLayer(new(5, 3, 2, 2), HideKind.Black, null)]));

        Assert.IsLessThanOrEqualTo(1, Math.Abs(MediaFixtures.Alpha(composed, 1, 1) - 128), "the half-transparent pixel");
        AssertEveryPixel(composed, (x, y) => x == 1 && y == 1 ? Pixel(composed, x, y) : Inside(x, y, 5, 3, 2, 2) ? Black : Pixel(original, x, y), "the rest as it was");
        var invisible = Composed(MediaFixtures.FirstFrame(MediaFixtures.Path("invisible-blue.png")), LayerList.Empty);
        Assert.AreEqual(Color(0, 0, 255), Pixel(invisible, 0, 0), "an opaque pixel exactly");
        Assert.AreEqual(0, MediaFixtures.Alpha(invisible, 1, 0), "a transparent pixel stays transparent");
    }

    [TestMethod]
    public void ALayerAppearsOnlyInTheFramesItsRangeAdmits()
    {
        var frames = MediaFixtures.Frames(MediaFixtures.Path("animated.gif"));
        var box = new DrawMarkLayer(MarkKind.Box, [], new(1, 1, 5, 4), new(255, 255, 255, 255), Thickness.Thin, new(1, 2));
        var hide = new HideLayer(new(3, 2, 1, 1), HideKind.Black, new(2, 2));
        var ranged = new LayerList([box, hide]);
        var whole = new LayerList([box with { Range = null }, hide with { Range = null }]);

        bool Boxed(MediaFrame frame) => Pixel(frame, 1, 2) == White;
        bool Hidden(MediaFrame frame) => Pixel(frame, 3, 2) == Black;

        var drawn = frames.Select((frame, i) => Composed(frame, ranged, i)).ToList();
        Assert.IsFalse(Boxed(drawn[0]) || Hidden(drawn[0]), "frame 0: neither");
        Assert.IsTrue(Boxed(drawn[1]) && !Hidden(drawn[1]), "frame 1: the box alone");
        Assert.IsTrue(Boxed(drawn[2]) && Hidden(drawn[2]), "frame 2: both");
        Assert.IsTrue(frames.Select((frame, i) => Composed(frame, whole, i)).All(frame => Boxed(frame) && Hidden(frame)), "no range: every frame");
    }

    [TestMethod]
    public void ARangeIsJudgedOnTheOriginalsTimelineAsWritten()
    {
        var frames = MediaFixtures.Frames(MediaFixtures.Path("animated.gif"));
        var onLast = new HideLayer(new(0, 0, 8, 6), HideKind.Black, new(2, 2));
        var beyond = onLast with { Range = new(5, 9) };
        var trimmed = new LayerList([onLast, new TrimLayer(new(1, 2))]);

        // The kept frames of a trim to [1, 2] are the original's frames 1 and 2, asked for by their own indices.
        Assert.AreNotEqual(Black, Pixel(Composed(frames[1], trimmed, 1), 0, 0), "the first kept frame");
        Assert.AreEqual(Black, Pixel(Composed(frames[2], trimmed, 2), 0, 0), "the second kept frame");
        Assert.IsFalse(frames.Select((frame, i) => Pixel(Composed(frame, new LayerList([beyond]), i), 0, 0)).Contains(Black), "a range beyond the frames: no frame");
    }

    [TestMethod]
    public void TheOutputSizeIsExactlyWhatIsDrawn()
    {
        var original = Pattern(0, 33, 25);
        var layers = new LayerList([new CaptionBarLayer("x", "Impact", 7, Red, Outline.None), new CropLayer(new(1, 2, 31, 21)), new ShrinkLayer(Shrink.ThreeQuarter)]);
        var size = LayerComposer.OutputSize(33, 25, layers);
        using var surface = Surface(size.Width + 3, size.Height + 3);
        surface.Canvas.Clear(SKColors.Transparent);

        Assert.IsTrue(LayerComposer.Compose(surface.Canvas, original, layers, 0, "the audacity").Succeeded);

        var drawn = ReadBack(surface);
        AssertEveryPixel(drawn, (x, y) => x < size.Width && y < size.Height ? Pixel(drawn, x, y) | 0xFF000000 : 0, "opaque inside the output size, untouched outside");
    }

    [TestMethod]
    public void TheCallerScalesTheCanvas()
    {
        var original = Pattern(0, 32, 24);
        var layers = new LayerList([new HideLayer(new(8, 8, 8, 8), HideKind.Black, null)]);
        var unscaled = Composed(original, layers);

        var doubled = Composed(original, layers, scale: 2);

        Assert.AreEqual(new PixelSize(64, 48), new PixelSize(doubled.Width, doubled.Height));
        for (var y = 0; y < 24; y++)
        {
            for (var x = 0; x < 32; x++)
            {
                // Away from the squares' edges, where filtering mixes two colours.
                if (x % 8 != 0 && y % 8 != 0)
                {
                    Assert.AreEqual(Pixel(unscaled, x, y), Pixel(doubled, 2 * x, 2 * y), $"({x}, {y})");
                }
            }
        }
    }

    [TestMethod]
    public void NothingIsKeptBetweenCalls()
    {
        var original = Pattern(0, 32, 24);
        Composed(original, new LayerList([new HideLayer(new(0, 0, 32, 24), HideKind.Black, null), new ShrinkLayer(Shrink.Half)]));

        var second = Composed(original, LayerList.Empty);

        AssertEveryPixel(second, (x, y) => Pixel(original, x, y), "the second holds nothing of the first");
        var mutable = typeof(LayerComposer).GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
            .Where(field => !field.IsInitOnly && !field.IsLiteral)
            .Select(field => field.Name)
            .ToList();
        Assert.IsEmpty(mutable, "no static state to share between threads: " + string.Join(", ", mutable));
    }

    [TestMethod]
    public void ThreadsComposingAtOnceShareNothing()
    {
        var originals = Enumerable.Range(0, 8).Select(seed => Pattern(seed, 48, 32)).ToList();
        var layers = Enumerable.Range(0, 8).Select(seed => new LayerList(
        [
            new HideLayer(new(seed, seed, 16, 12), seed % 2 == 0 ? HideKind.Blur : HideKind.Black, null),
            new DrawMarkLayer(MarkKind.Arrow, [new(0, seed), new(40, 20)], null, Red, Thickness.Medium, null),
        ])).ToList();
        var alone = Enumerable.Range(0, 8).Select(i => Composed(originals[i], layers[i])).ToList();

        var together = new MediaFrame[8];
        Parallel.For(0, 8, i => together[i] = Composed(originals[i], layers[i]));

        for (var i = 0; i < 8; i++)
        {
            CollectionAssert.AreEqual(alone[i].Pixels, together[i].Pixels, $"composition {i}");
        }
    }

    [TestMethod]
    public void APictureTooLargeToDrawIsNamedNotThrown()
    {
        var huge = new LayerList([new CaptionBarLayer("x", "Impact", 100_000_000, Red, Outline.None)]);
        using var surface = Surface(4, 4);

        var drawn = LayerComposer.Compose(surface.Canvas, Pattern(0, 64, 64), huge, 0, "the audacity");

        Assert.AreEqual(new Failure("Draw the picture with its layers", "the audacity", "there is not enough memory to draw it"), drawn.Failure);
        Assert.AreEqual(int.MaxValue, LayerComposer.OutputSize(64, 64, new LayerList([new CaptionBarLayer("x", "Impact", int.MaxValue, Red, Outline.None)])).Height, "a side past counting");
    }

    [TestMethod]
    public void AFrameWhosePixelsDoNotFillItsSizeIsNamedNotThrown()
    {
        using var surface = Surface(4, 4);

        var drawn = LayerComposer.Compose(surface.Canvas, new MediaFrame(4, 4, 0, new byte[60]), LayerList.Empty, 0, "the audacity");

        Assert.AreEqual(new Failure("Draw the picture with its layers", "the audacity", "its pixels do not fill its size"), drawn.Failure);
    }

    [TestMethod]
    public void WhatDrawsLayersReachesNothingOfAvaloniaOrTheOtherParts()
    {
        string[] forbidden = ["Avalonia", "ImageStash.Store", "ImageStash.Window", "ImageStash.Capture"];
        var drawers = typeof(LayerComposer).Assembly.GetTypes()
            .Where(type => type.Namespace == typeof(LayerComposer).Namespace && type.DeclaringType is null)
            .Where(type => ProductCalls.ReferencedTypes(type).Any(used => used.Namespace == typeof(LayerList).Namespace))
            .ToList();

        var reached = drawers
            .SelectMany(type => ProductCalls.ReferencedTypes(type).Select(used => (Drawer: type.Name, Used: used)))
            .Where(pair => forbidden.Any(space => pair.Used.Namespace is { } name && (name == space || name.StartsWith(space + ".", StringComparison.Ordinal))))
            .Select(pair => $"{pair.Drawer} → {pair.Used.FullName}")
            .Distinct()
            .ToList();

        Assert.Contains(typeof(LayerComposer), drawers);
        Assert.IsEmpty(reached, string.Join(", ", reached));
    }

    /// <summary>
    /// <paramref name="original"/> under <paramref name="layers"/>, composed onto a fresh surface of the output size times
    /// <paramref name="scale"/> whose canvas the check scales first, read back in the boundary pixel contract.
    /// </summary>
    private static MediaFrame Composed(MediaFrame original, LayerList layers, int frameIndex = 0, int scale = 1)
    {
        var (composed, replaced) = Compose(original, layers, frameIndex, scale);
        Assert.IsEmpty(replaced, "no font replaced");
        return composed;
    }

    /// <summary>
    /// The pixel itself when it is white or a grey of black letters on white, which nothing but a band's black caption
    /// draws there; otherwise white, so the assertion names it.
    /// </summary>
    private static uint Lettered(MediaFrame frame, int x, int y)
    {
        var pixel = Pixel(frame, x, y);
        var (b, g, r) = Channels(pixel);
        return pixel >> 24 == 255 && b == g && g == r ? pixel : White;
    }

    private static (byte B, byte G, byte R) Channels(uint pixel) => ((byte)pixel, (byte)(pixel >> 8), (byte)(pixel >> 16));

    private static bool Inside(int x, int y, int left, int top, int width, int height) =>
        x >= left && x < left + width && y >= top && y < top + height;

    private static int Changed(MediaFrame composed) =>
        Enumerable.Range(0, composed.Width * composed.Height).Count(i => Pixel(composed, i % composed.Width, i / composed.Width) != Pixel(Grey, 0, 0));

    private static void AssertOpaque(MediaFrame frame, string because) =>
        AssertEveryPixel(frame, (x, y) => Pixel(frame, x, y) | 0xFF000000, because);

    private static void AssertEveryPixel(MediaFrame frame, Func<int, int, uint> expected, string because)
    {
        for (var y = 0; y < frame.Height; y++)
        {
            for (var x = 0; x < frame.Width; x++)
            {
                Assert.AreEqual(expected(x, y), Pixel(frame, x, y), $"({x}, {y}): {because}");
            }
        }
    }
}
