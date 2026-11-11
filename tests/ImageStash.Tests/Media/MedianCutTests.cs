using System.Drawing;
using System.Runtime.InteropServices;
using ImageStash.Media;
using ImageStash.Tests.Support;
using static ImageStash.Tests.Support.SyntheticFrames;

namespace ImageStash.Tests.Media;

/// <summary>
/// Media's median cut, the palette of a GIF image too colourful for one (the G14, G15 entry: "a median-cut quantization of
/// those pixels, no dithering"): at most 256 entries, every one of them used when the colours fill them, each made from
/// the pixels given it; one index for every pixel of one colour; a colour many pixels carry kept exactly; a pixel below
/// half alpha the transparent entry (G21); the area's pixels alone; and, through the encoder, an image's palette made of
/// its own pixels with nothing kept from the image before.
/// </summary>
[TestClass]
// The encoder's checks here share the library's pool threads with GifEncoderTests' (see there).
[DoNotParallelize]
public sealed class MedianCutTests
{
    private const int Side = 64;

    [TestMethod]
    public void FourThousandColoursCarriedAlikeUseEveryEntry()
    {
        var pixels = EveryColourOnce();
        var indices = new byte[Side * Side];
        var palette = new uint[MedianCut.PaletteSize];

        var count = new MedianCut().Quantize(pixels, Side, new Rectangle(0, 0, Side, Side), indices, palette);

        Assert.AreEqual(256, count, "a palette's every entry, none transparent");
        Assert.AreEqual(256, indices.Distinct().Count(), "every entry given to some pixel");
        Assert.AreEqual(256, palette.Distinct().Count(), "no two entries alike");
        Assert.IsTrue(palette.All(entry => entry >> 24 == 255), "every entry opaque");
    }

    [TestMethod]
    public void EachEntryIsMadeFromThePixelsGivenItAndEveryPixelOfOneColourGetsOneIndex()
    {
        var pixels = MemoryMarshal.Cast<byte, uint>(Noise(seed: 3, colours: 3000, Side, Side).Pixels).ToArray();
        var indices = new byte[Side * Side];
        var palette = new uint[MedianCut.PaletteSize];

        var count = new MedianCut().Quantize(pixels, Side, new Rectangle(0, 0, Side, Side), indices, palette);

        Assert.IsLessThanOrEqualTo(256, count);
        var byColour = pixels.Select((pixel, i) => (pixel, index: indices[i])).GroupBy(p => p.pixel).ToList();
        Assert.IsTrue(byColour.All(colour => colour.Select(p => p.index).Distinct().Count() == 1), "no dithering: one colour, one index");
        foreach (var given in pixels.Select((pixel, i) => (pixel, index: indices[i])).GroupBy(p => p.index))
        {
            var entry = palette[given.Key];
            foreach (var shift in new[] { 0, 8, 16 })
            {
                var channel = given.Select(p => (int)(p.pixel >> shift & 0xFF)).ToList();
                var shown = (int)(entry >> shift & 0xFF);
                Assert.IsTrue(shown >= channel.Min() && shown <= channel.Max(), $"entry {given.Key} {entry:X8}: a channel inside its pixels' own range");
            }
        }
    }

    [TestMethod]
    public void AColourHalfThePixelsCarryIsKeptExactlyBesideThreeHundredOthers()
    {
        var flat = Color(40, 200, 90);
        var pixels = Enumerable.Range(0, Side * Side)
            .Select(i => i % Side >= Side / 2 ? flat : Color((byte)(i % 300 * 7), (byte)(255 - i % 300 * 3), (byte)(i % 300 * 13 % 251)))
            .ToArray();
        Assert.AreEqual(301, pixels.Distinct().Count());
        var indices = new byte[Side * Side];
        var palette = new uint[MedianCut.PaletteSize];

        new MedianCut().Quantize(pixels, Side, new Rectangle(0, 0, Side, Side), indices, palette);

        Assert.AreEqual(flat, palette[indices[Side - 1]], "the flat colour's entry is that colour");
    }

    [TestMethod]
    public void APixelBelowHalfAlphaTakesTheTransparentEntryAfterAtMost255OpaqueOnes()
    {
        var pixels = EveryColourOnce();
        for (var i = 0; i < pixels.Length; i += 5)
        {
            // Alphas either side of half: below it transparent, at or above it the straight colour counts.
            pixels[i] = pixels[i] & 0x00FFFFFF | (uint)(i % 2 == 0 ? 127 : 128) << 24;
        }

        var indices = new byte[Side * Side];
        var palette = new uint[MedianCut.PaletteSize];

        var count = new MedianCut().Quantize(pixels, Side, new Rectangle(0, 0, Side, Side), indices, palette);

        Assert.AreEqual(256, count, "255 opaque entries and the transparent one");
        Assert.AreEqual(0u, palette[count - 1], "the transparent entry last");
        for (var i = 0; i < pixels.Length; i++)
        {
            Assert.AreEqual(pixels[i] >> 24 < 128, indices[i] == count - 1, $"pixel {i} of alpha {pixels[i] >> 24}");
        }

        Assert.IsTrue(palette.Take(count - 1).All(entry => entry >> 24 == 255), "the rest opaque");
    }

    [TestMethod]
    public void AnAreaWhollyTransparentIsTheTransparentEntryAlone()
    {
        var indices = new byte[16];
        var palette = new uint[MedianCut.PaletteSize];

        var count = new MedianCut().Quantize(new uint[16], 4, new Rectangle(0, 0, 4, 4), indices, palette);

        Assert.AreEqual(1, count);
        Assert.AreEqual(0u, palette[0]);
        Assert.IsTrue(indices.All(index => index == 0));
    }

    [TestMethod]
    public void OnlyTheAreasPixelsAreQuantizedRowByRow()
    {
        const int Width = 80;
        var red = Color(0, 0, 255);
        var pixels = Enumerable.Repeat(red, Width * 70).ToArray();
        var colours = EveryColourOnce();
        for (var y = 0; y < Side; y++)
        {
            colours.AsSpan(y * Side, Side).CopyTo(pixels.AsSpan((y + 3) * Width + 10, Side));
        }

        var indices = new byte[Side * Side];
        var palette = new uint[MedianCut.PaletteSize];

        var count = new MedianCut().Quantize(pixels, Width, new Rectangle(10, 3, Side, Side), indices, palette);

        Assert.AreEqual(256, count);
        CollectionAssert.DoesNotContain(palette, red, "the red around the area is none of its pixels");
        var again = new byte[Side * Side];
        new MedianCut().Quantize(colours, Side, new Rectangle(0, 0, Side, Side), again, new uint[MedianCut.PaletteSize]);
        CollectionAssert.AreEqual(again, indices, "the area's pixels, row by row, as if they were the whole");
    }

    [TestMethod]
    public void ThroughTheEncoderEveryEntryIsUsedAndAPixelBelowHalfAlphaIsTransparent()
    {
        using var scratch = new ScratchFolder();
        var opaque = Frame(EveryColourOnce());
        var translucent = Frame(EveryColourOnce());
        for (var i = 0; i < Side * Side; i += 7)
        {
            translucent.Pixels[i * 4 + 3] = 100;
        }

        var opaqueShown = Decoded(scratch.File("opaque.gif"), [opaque])[0];
        var translucentShown = Decoded(scratch.File("translucent.gif"), [translucent])[0];

        Assert.AreEqual(256, Seen(opaqueShown).Distinct().Count(), "256 colours shown: the palette's every entry");
        var seen = Seen(translucentShown);
        for (var i = 0; i < Side * Side; i++)
        {
            Assert.AreEqual(i % 7 == 0, seen[i] == 0, $"pixel {i} transparent only below half alpha");
        }

        Assert.IsLessThanOrEqualTo(255, seen.Where(pixel => pixel != 0).Distinct().Count(), "at most 255 opaque colours beside the transparent index");
    }

    [TestMethod]
    public void ThroughTheEncoderAnImagesPaletteIsMadeOfItsOwnPixelsWithNothingKeptFromTheImageBefore()
    {
        using var scratch = new ScratchFolder();
        var random = new Random(5);
        // Thousands of colours that all carry red, then, every pixel changed, thousands that carry none.
        var reddish = SyntheticFrames.Frame(Side, Side, (_, _) => ((byte)random.Next(256), (byte)random.Next(256), (byte)(128 + random.Next(128))));
        var redless = SyntheticFrames.Frame(Side, Side, (_, _) => ((byte)random.Next(256), (byte)random.Next(256), 0));

        var shown = Decoded(scratch.File("two.gif"), [reddish, redless]);

        Assert.IsTrue(Seen(shown[0]).All(pixel => (pixel >> 16 & 0xFF) >= 128), "the first image as red as its pixels");
        Assert.IsTrue(Seen(shown[1]).All(pixel => (pixel >> 16 & 0xFF) == 0), "no pixel of the second has any red");
    }

    /// <summary>The 4,096 colours of 16 steps a channel, each pixel one of them, in a <see cref="Side"/> × <see cref="Side"/> image.</summary>
    private static uint[] EveryColourOnce() =>
        Enumerable.Range(0, Side * Side).Select(i => Color((byte)(i % 16 * 17), (byte)(i / 16 % 16 * 17), (byte)(i / 256 * 17))).ToArray();

    private static MediaFrame Frame(uint[] pixels) => new(Side, Side, 0, MemoryMarshal.AsBytes(pixels.AsSpan()).ToArray());

    /// <summary>The frames written by the encoder at <paramref name="path"/>, 70 ms apart, and read back.</summary>
    private static List<MediaFrame> Decoded(string path, IReadOnlyList<MediaFrame> frames)
    {
        var encoder = GifEncoder.Open(path, Side, Side, "clip", TestStash.ShortRetry).Value!;
        for (var i = 0; i < frames.Count; i++)
        {
            Assert.IsTrue(encoder.Append(frames[i], i * 70L).Succeeded);
        }

        Assert.IsTrue(encoder.Stop(frames.Count * 70L).Succeeded);
        return MediaFixtures.Frames(path);
    }

    /// <summary>What a frame shows: each pixel as <see cref="SyntheticFrames.Pixel"/> reads it, a fully transparent one as 0.</summary>
    private static List<uint> Seen(MediaFrame frame) =>
        Enumerable.Range(0, frame.Width * frame.Height).Select(i => Pixel(frame, i % frame.Width, i / frame.Width)).Select(pixel => pixel >> 24 == 0 ? 0 : pixel).ToList();
}
