using ImageStash.Layers;
using ImageStash.Media;
using SkiaSharp;
using static ImageStash.Tests.Support.Compositions;
using static ImageStash.Tests.Support.SyntheticFrames;

namespace ImageStash.Tests.Media;

/// <summary>
/// The letters of text and caption-bar layers as the one routine draws them (R4): a Meme in uppercase with its outline
/// under the fill, a Label as typed where its box is, Auto's colour from the fill, rotation clockwise about the box's
/// centre, a caption in its fill on its white band, a missing family drawn in a fallback and named once, an emoji the
/// font lacks drawn from an emoji font, a range honoured, and the laid-out size the editor reads (G21, G22, G24, E3, S11,
/// G35, What crosses every boundary).
/// </summary>
[TestClass]
public sealed class TextLetteringTests
{
    private static readonly Rgba White = new(255, 255, 255, 255);
    private static readonly Rgba Yellow = new(255, 212, 59, 255);
    private static readonly Rgba Blue = new(47, 107, 255, 255);
    private static readonly Rgba Red = new(230, 20, 20, 255);
    private static readonly MediaFrame Grey = Frame(64, 48, (_, _) => (128, 128, 128));

    [TestMethod]
    public void AMemeIsDrawnInUppercaseAndALabelAsTyped()
    {
        TextLayer Meme(string text) => new(TextPreset.Meme, text, "Impact", 12, White, Outline.Auto, new(4, 2, 56, 15), 0, null);
        TextLayer Label(string text) => new(TextPreset.Label, text, "Impact", 12, White, Outline.Auto, new(4, 2, 56, 15), 0, null);

        var lower = Drawn(Grey, Meme("ab"));

        Assert.IsNotEmpty(Changed(Grey, lower), "the letters are drawn");
        CollectionAssert.AreEqual(Drawn(Grey, Meme("AB")).Pixels, lower.Pixels, "a Meme's letters are uppercase whatever was typed");
        CollectionAssert.AreNotEqual(Drawn(Grey, Label("AB")).Pixels, Drawn(Grey, Label("ab")).Pixels, "a Label's are as typed");
        Assert.AreEqual(LayerComposer.TextSize(Meme("AB")), LayerComposer.TextSize(Meme("ab")));
    }

    [TestMethod]
    public void AMemesLetterWhoseCapitalIsSeveralLettersIsDrawnAsThem()
    {
        TextLayer Meme(string text) => new(TextPreset.Meme, text, "Segoe UI", 10, White, Outline.Auto, new(2, 2, 60, 40), 0, null);

        // ß, the ligature ﬃ and ᾳ have no one-letter capital: their full uppercase is SS, FFI and ΑΙ;
        // the dotless ı's is I, and the Garay 𐵰's 𐵐, which .NET's invariant uppercase does not give.
        var typed = Meme("straße ﬃ ᾳ ı 𐵰");
        var capitals = Meme("STRASSE FFI ΑΙ I 𐵐");

        Assert.AreEqual(LayerComposer.TextSize(capitals), LayerComposer.TextSize(typed));
        CollectionAssert.AreEqual(Drawn(Grey, capitals).Pixels, Drawn(Grey, typed).Pixels, "a Meme is uppercase (G21)");
    }

    [TestMethod]
    public void AnOutlineIsDrawnUnderTheFillInAutosColourFromTheFill()
    {
        TextLayer Meme(Rgba fill, Outline outline) => new(TextPreset.Meme, "I", "Impact", 32, fill, outline, new(4, 4, 56, 40), 0, null);

        var yellow = Drawn(Grey, Meme(Yellow, Outline.Auto));
        var plainYellow = Drawn(Grey, Meme(Yellow, Outline.None));
        var blue = Drawn(Grey, Meme(Blue, Outline.Auto));

        Assert.AreEqual(new Rgba(0, 0, 0, 255), LayerComposer.AutoOutline(Yellow), "black on a light fill");
        Assert.AreEqual(new Rgba(255, 255, 255, 255), LayerComposer.AutoOutline(Blue), "white on a dark one");
        Assert.IsTrue(Any(yellow, IsExactly(Yellow)), "the yellow fill");
        Assert.IsTrue(Any(yellow, IsBlack), "a black outline round the yellow");
        Assert.IsFalse(Any(yellow, IsWhite), "no white outline round the yellow");
        Assert.IsTrue(Any(blue, IsExactly(Blue)), "the blue fill");
        Assert.IsTrue(Any(blue, IsWhite), "a white outline round the blue");
        Assert.IsFalse(Any(blue, IsBlack), "no black outline round the blue");
        Assert.IsFalse(Any(plainYellow, IsBlack) || Any(plainYellow, IsWhite), "None draws no outline");
        for (var i = 0; i < plainYellow.Width * plainYellow.Height; i++)
        {
            if (IsExactly(Yellow)(Pixel(plainYellow, i % 64, i / 64)))
            {
                Assert.IsTrue(IsExactly(Yellow)(Pixel(yellow, i % 64, i / 64)), $"({i % 64}, {i / 64}): the outline lies under the fill, so no letter is eaten");
            }
        }

        Assert.IsTrue(Any(Drawn(Grey, Meme(Yellow, Outline.Of(Red))), IsExactly(Red)), "a chosen outline colour");
    }

    [TestMethod]
    public void ALabelIsPlainTextWhereItsBoxIs()
    {
        TextLayer Label(int x, int y)
        {
            var label = new TextLayer(TextPreset.Label, "I", "Segoe UI", 16, White, Outline.None, new(x, y, 1, 1), 0, null);
            var size = LayerComposer.TextSize(label);
            return label with { Box = new(x, y, size.Width, size.Height) };
        }

        foreach (var label in new[] { Label(30, 20), Label(10, 5) })
        {
            var drawn = Changed(Grey, Drawn(Grey, label));

            Assert.IsNotEmpty(drawn, $"drawn at {label.Box}");
            Assert.IsTrue(drawn.All(p => p.X >= label.Box.X && p.X < label.Box.X + label.Box.Width && p.Y >= label.Box.Y && p.Y < label.Box.Y + label.Box.Height),
                $"every letter pixel inside {label.Box}");
        }
    }

    [TestMethod]
    public void AMemeWrapsAtItsBoxAndALabelNever()
    {
        var text = "ha ha ha ha ha ha ha ha";
        var meme = new TextLayer(TextPreset.Meme, text, "Impact", 10, White, Outline.Auto, new(0, 0, 30, 10), 0, null);
        var label = new TextLayer(TextPreset.Label, text, "Impact", 10, White, Outline.Auto, new(0, 0, 30, 10), 0, null);
        var oneLine = (10 * 12 + 9) / 10;

        Assert.AreEqual(oneLine, LayerComposer.TextSize(label).Height, "a Label is one line however narrow its box");
        Assert.IsGreaterThan(30, LayerComposer.TextSize(label).Width);
        Assert.IsGreaterThan(oneLine, LayerComposer.TextSize(meme).Height, "a Meme wraps at its box's width");
        Assert.IsLessThanOrEqualTo(30, LayerComposer.TextSize(meme).Width);
        Assert.AreEqual((10 * 24 + 9) / 10, LayerComposer.TextSize(label with { Text = "I\nII" }).Height, "a line break adds a line");
        Assert.AreEqual(new PixelSize(1, oneLine), LayerComposer.TextSize(label with { Text = "" }), "an empty text is one line");
        Assert.AreEqual((10 * 48 + 9) / 10, LayerComposer.TextSize(meme with { Text = "WWWW", Box = new(0, 0, 1, 10) }).Height, "a word wider than the box is broken, a letter a line");
    }

    [TestMethod]
    public void ATextLayerTurnsClockwiseAboutItsBoxsCentre()
    {
        var unturned = new TextLayer(TextPreset.Label, "IIIIIIIIIIII\nI", "Segoe UI", 10, White, Outline.None, new(0, 0, 1, 1), 0, null);
        var size = LayerComposer.TextSize(unturned);
        unturned = unturned with { Box = new(32 - size.Width / 2, 24 - size.Height / 2, size.Width, size.Height) };
        float centreX = unturned.Box.X + unturned.Box.Width / 2f, centreY = unturned.Box.Y + unturned.Box.Height / 2f;

        var flat = Changed(Grey, Drawn(Grey, unturned));
        var clockwise = Changed(Grey, Drawn(Grey, unturned with { Rotation = 90 }));
        var anticlockwise = Changed(Grey, Drawn(Grey, unturned with { Rotation = -90 }));

        Assert.IsGreaterThan(Height(flat), Width(flat), "unturned, the long first line makes it wide");
        Assert.IsGreaterThan(flat.Count(p => p.Y + 0.5f > centreY), flat.Count(p => p.Y + 0.5f < centreY), "and most of it above the centre");
        Assert.IsGreaterThan(Width(clockwise), Height(clockwise), "turned a quarter, it is tall");
        Assert.IsTrue(clockwise.Min(p => p.X) <= centreX && clockwise.Max(p => p.X) >= centreX && clockwise.Min(p => p.Y) <= centreY && clockwise.Max(p => p.Y) >= centreY,
            "about the box's centre");
        Assert.IsGreaterThan(clockwise.Count(p => p.X + 0.5f < centreX), clockwise.Count(p => p.X + 0.5f > centreX), "clockwise, y pointing down, the first line goes to the right");
        Assert.IsGreaterThan(anticlockwise.Count(p => p.X + 0.5f > centreX), anticlockwise.Count(p => p.X + 0.5f < centreX), "anticlockwise, to the left");
    }

    [TestMethod]
    public void ACaptionIsInItsFillWithTheSameOutlineRuleOnABandThatStaysWhite()
    {
        var picture = Frame(160, 24, (_, _) => (128, 128, 128));
        var blue = Drawn(picture, new CaptionBarLayer("me, later", "Segoe UI", 16, new(20, 20, 200, 255), Outline.None));
        var outlined = Drawn(picture, new CaptionBarLayer("me, later", "Segoe UI", 16, White, Outline.Auto));
        var band = blue.Height - 24;

        Assert.AreEqual(0xFFFFFFFF, Pixel(blue, 0, 0), "the band is white, whatever the fill");
        Assert.AreEqual(0xFFFFFFFF, Pixel(blue, 159, band - 1));
        Assert.IsTrue(Any(blue, pixel => Channels(pixel)[0] - Channels(pixel)[2] > 100), "the letters are in the fill");
        Assert.IsTrue(Any(outlined, pixel => Channels(pixel).All(channel => channel < 100)), "white letters on the white band carry Auto's black outline");
    }

    [TestMethod]
    public void ACaptionsLettersStayInsideItsBand()
    {
        // Javanese Text's bar reaches 0.95 font sizes below its baseline, where a one-line band leaves 0.35.
        Assert.IsNotNull(Typefaces.Match("Javanese Text"), "a font Windows 11 installs");
        var picture = Pattern(0, 160, 24);
        var deep = new CaptionBarLayer("|", "Javanese Text", 40, new(0, 0, 0, 255), Outline.None);
        var lower = new CaptionBarLayer("x", "Segoe UI", 20, new(0, 0, 0, 255), Outline.None);

        var alone = Drawn(picture, deep);
        var stacked = Compose(picture, new LayerList([lower, deep])).Frame;
        var lowerAlone = Drawn(picture, lower);
        var lowerBand = lowerAlone.Height - 24;

        Assert.IsEmpty(Changed(picture, alone), "nothing of the caption reaches the picture");
        for (var y = 0; y < lowerBand + 24; y++)
        {
            for (var x = 0; x < 160; x++)
            {
                Assert.AreEqual(Pixel(lowerAlone, x, y), Pixel(stacked, x, stacked.Height - lowerBand - 24 + y), $"({x}, {y}): nothing of the upper caption reaches the lower band");
            }
        }
    }

    [TestMethod]
    public void LinesStartTheirAscentBelowTheTopAMemesAndACaptionsCentred()
    {
        using var segoe = new SKFont(Typefaces.Match("Segoe UI"), 20);
        var label = new TextLayer(TextPreset.Label, "HH", "Segoe UI", 20, White, Outline.None, new(6, 10, 1, 1), 0, null);
        var meme = new TextLayer(TextPreset.Meme, "HH", "Impact", 20, White, Outline.None, new(0, 2, 64, 30), 0, null);
        var picture = Frame(160, 24, (_, _) => (128, 128, 128));

        var labelInk = Changed(Grey, Drawn(Grey, label with { Box = label.Box with { Width = 40, Height = 30 } }));
        var memeInk = Changed(Grey, Drawn(Grey, meme));
        var caption = Drawn(picture, new CaptionBarLayer("HH", "Segoe UI", 20, new(0, 0, 0, 255), Outline.None));
        var captionInk = Enumerable.Range(0, 160 * (caption.Height - 24)).Select(i => (X: i % 160, Y: i / 160)).Where(p => Pixel(caption, p.X, p.Y) != 0xFFFFFFFF).ToList();

        var baseline = 10 - segoe.Metrics.Ascent;
        Assert.AreEqual(baseline, labelInk.Max(p => p.Y) + 1, 1, "a Label's first baseline is its ascent below the box's top");
        Assert.AreEqual(baseline - segoe.Metrics.CapHeight, labelInk.Min(p => p.Y), 1.5, "its capitals reach up from there");
        Assert.AreEqual(32, (memeInk.Min(p => p.X) + memeInk.Max(p => p.X) + 1) / 2f, 1.5, "a Meme's line is centred in its box");
        Assert.AreEqual(0.4f * 20 - segoe.Metrics.Ascent, captionInk.Max(p => p.Y) + 1, 1, "a caption's first line starts 0.4 font sizes below its band's top");
        Assert.AreEqual(80, (captionInk.Min(p => p.X) + captionInk.Max(p => p.X) + 1) / 2f, 1.5, "and is centred in its band");
    }

    [TestMethod]
    public void AMissingFamilyIsDrawnInTheDefaultTypefaceAndNamedOnce()
    {
        var meme = new TextLayer(TextPreset.Meme, "hello", "No Such Font", 12, White, Outline.Auto, new(4, 2, 56, 15), 0, null);
        var label = new TextLayer(TextPreset.Label, "there", "no such font", 12, White, Outline.Auto, new(4, 20, 40, 15), 0, null);
        var bar = new CaptionBarLayer("over", "No Such Font", 12, new(0, 0, 0, 255), Outline.None);

        var (alone, replaced) = Compose(Grey, new LayerList([meme]));

        Assert.IsNotEmpty(Changed(Grey, alone), "drawn in the fallback");
        CollectionAssert.AreEqual(new[] { "No Such Font" }, replaced.ToList());
        CollectionAssert.AreEqual(new[] { "No Such Font" }, Compose(Grey, new LayerList([meme, label, bar])).Replaced.ToList(), "each family once");
        CollectionAssert.AreEqual(new[] { "Ghost", "No Such Font" },
            Compose(Grey, new LayerList([label with { Font = "Ghost" }, meme])).Replaced.ToList(), "in list order");
        Assert.IsEmpty(Compose(Grey, new LayerList([meme with { Font = "Impact" }])).Replaced, "an installed family answers nothing");
    }

    [TestMethod]
    public void ArialBlackDrawsInItsOwnFace()
    {
        var black = new TextLayer(TextPreset.Meme, "AB", "Arial Black", 16, White, Outline.None, new(4, 2, 56, 20), 0, null);

        var (drawn, replaced) = Compose(Grey, new LayerList([black]));

        Assert.IsEmpty(replaced, "Arial Black is installed here");
        CollectionAssert.AreNotEqual(Drawn(Grey, black with { Font = "Arial" }).Pixels, drawn.Pixels, "the Black style's letters differ from Arial's");
    }

    [TestMethod]
    public void AnEmojiTheFontLacksIsDrawnFromAnEmojiFont()
    {
        var picture = Frame(96, 48, (_, _) => (128, 128, 128));
        var label = new TextLayer(TextPreset.Label, "Hi 😀", "Impact", 24, White, Outline.None, new(2, 2, 90, 40), 0, null);
        var end = 2 + LayerComposer.TextSize(label with { Text = "Hi " }).Width;

        var (drawn, replaced) = Compose(picture, new LayerList([label]));

        Assert.IsEmpty(replaced, "a missing glyph is not a missing family");
        Assert.IsTrue(Changed(picture, drawn).Any(p => p.X >= end && IsColoured(Pixel(drawn, p.X, p.Y))), "the emoji's colours right of \"Hi \", where white letters alone draw none");
        Assert.IsNotEmpty(Changed(picture, Drawn(picture, label with { Text = "😀" })), "an emoji alone");
        var (unmatched, unreported) = Compose(picture, new LayerList([label with { Text = "a\U000E0000b\U0010FFFD" }]));
        Assert.IsNotEmpty(Changed(picture, unmatched), "a character no font has draws without failing, and the letters round it are drawn");
        Assert.IsEmpty(unreported);
    }

    [TestMethod]
    public void ARangedTextLayerShowsOnlyInItsFrames()
    {
        var label = new TextLayer(TextPreset.Label, "range", "Segoe UI", 12, White, Outline.Auto, new(4, 4, 50, 20), 0, new FrameRange(1, 1));

        Assert.IsEmpty(Changed(Grey, Compose(Grey, new LayerList([label]), frameIndex: 0).Frame), "not on frame 0");
        Assert.IsNotEmpty(Changed(Grey, Compose(Grey, new LayerList([label]), frameIndex: 1).Frame), "on frame 1");
    }

    [TestMethod]
    public void TheLargestSizeTheModelAdmitsIsDrawnAndMeasuredAtOnce()
    {
        var meme = new TextLayer(TextPreset.Meme, "huge words here", "Impact", int.MaxValue, White, Outline.Auto, new(0, 0, 64, 48), 1e300, null);
        var label = new TextLayer(TextPreset.Label, "huge\nwords", "Segoe UI", int.MaxValue, White, Outline.None, new(3, 3, 9, 9), -1e300, null);
        var clock = System.Diagnostics.Stopwatch.StartNew();

        Compose(Grey, new LayerList([meme, label]));
        var size = LayerComposer.TextSize(label);

        Assert.IsLessThan(2000, clock.ElapsedMilliseconds, "answered at once");
        Assert.AreEqual(new PixelSize(int.MaxValue, int.MaxValue), size, "a size past counting answers the largest");
    }

    /// <summary><paramref name="original"/> under one layer, composed, with no family replaced.</summary>
    private static MediaFrame Drawn(MediaFrame original, Layer layer)
    {
        var (frame, replaced) = Compose(original, new LayerList([layer]));
        Assert.IsEmpty(replaced, "no font replaced");
        return frame;
    }

    /// <summary>Where <paramref name="drawn"/>'s picture differs from <paramref name="original"/>, on the original's rows (below any band).</summary>
    private static List<(int X, int Y)> Changed(MediaFrame original, MediaFrame drawn)
    {
        var band = drawn.Height - original.Height;
        return Enumerable.Range(0, original.Width * original.Height)
            .Select(i => (X: i % original.Width, Y: i / original.Width))
            .Where(p => Pixel(drawn, p.X, p.Y + band) != Pixel(original, p.X, p.Y))
            .ToList();
    }

    private static int Width(List<(int X, int Y)> pixels) => pixels.Max(p => p.X) - pixels.Min(p => p.X) + 1;

    private static int Height(List<(int X, int Y)> pixels) => pixels.Max(p => p.Y) - pixels.Min(p => p.Y) + 1;

    private static bool Any(MediaFrame frame, Func<uint, bool> test) =>
        Enumerable.Range(0, frame.Width * frame.Height).Any(i => test(Pixel(frame, i % frame.Width, i / frame.Width)));

    private static Func<uint, bool> IsExactly(Rgba color) => pixel => pixel == Color(color.B, color.G, color.R);

    private static bool IsBlack(uint pixel) => Channels(pixel).All(channel => channel <= 8);

    private static bool IsWhite(uint pixel) => Channels(pixel).All(channel => channel >= 247);

    private static bool IsColoured(uint pixel) => Channels(pixel).Max() - Channels(pixel).Min() > 64;

    private static int[] Channels(uint pixel) => [(int)(pixel & 0xFF), (int)(pixel >> 8 & 0xFF), (int)(pixel >> 16 & 0xFF)];
}
