using System.Buffers.Binary;
using ImageStash.Windows.Seams;

namespace ImageStash.Windows.Tests.Seams;

/// <summary>
/// A clipboard bitmap of any depth into the boundary pixel contract (Composition, Clipboard and drops; What crosses
/// every boundary): 32-bit BGRA, straight alpha, top-down rows packed without padding. Each case is the bytes a
/// producer places, written out as they are stored, bottom-up unless the height is negative; the alpha rule tells a
/// DIB's alpha from bytes nobody uses and premultiplied from straight, as Edge, WinForms and ImageStash write them.
/// A bitmap that cannot be read is a failure naming why, never an exception (G35).
/// </summary>
[TestClass]
public sealed class WindowsDibTests
{
    private const uint Rgb = 0;
    private const uint Bitfields = 3;
    private static readonly uint[] Bgra = [0x00FF0000, 0x0000FF00, 0x000000FF];

    [TestMethod]
    public void ImageStashsOwnBitmapIsTurnedTopDownWithItsStraightAlphaAsItIs()
    {
        var dib = Concat(Header(124, 3, 2, 32, Bitfields, masks: [.. Bgra, 0xFF000000]),
            [0x00, 0xFF, 0x00, 0xFF, 0x01, 0x02, 0x03, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF],
            [0x10, 0x20, 0x30, 0x00, 0xFF, 0x40, 0x20, 0x80, 0x00, 0x00, 0xFF, 0xFF]);

        AssertFrame(dib, 3, 2,
            0x10, 0x20, 0x30, 0x00, 0xFF, 0x40, 0x20, 0x80, 0x00, 0x00, 0xFF, 0xFF,
            0x00, 0xFF, 0x00, 0xFF, 0x01, 0x02, 0x03, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF);
    }

    [TestMethod]
    [DataRow(Bitfields, DisplayName = "masks in the header")]
    [DataRow(Rgb, DisplayName = "BI_RGB with only the alpha mask, as Edge writes")]
    public void APremultipliedBitmapComesOutStraight(uint compression)
    {
        uint[] masks = compression == Bitfields ? [.. Bgra, 0xFF000000] : [0, 0, 0, 0xFF000000];
        var dib = Concat(Header(124, 3, 2, 32, compression, masks: masks),
            [0x00, 0xFF, 0x00, 0xFF, 0x01, 0x02, 0x03, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF],
            [0x00, 0x00, 0x00, 0x00, 0x80, 0x20, 0x10, 0x80, 0x00, 0x00, 0xFF, 0xFF]);

        AssertFrame(dib, 3, 2,
            0x00, 0x00, 0x00, 0x00, 0xFF, 0x40, 0x20, 0x80, 0x00, 0x00, 0xFF, 0xFF,
            0x00, 0xFF, 0x00, 0xFF, 0x01, 0x02, 0x03, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF);
    }

    [TestMethod]
    public void OnlyTransparentAndOpaquePixelsReadTheSameEitherWay()
    {
        var dib = Concat(Header(124, 2, 1, 32, Bitfields, masks: [.. Bgra, 0xFF000000]), [0x00, 0x00, 0x00, 0x00, 0x40, 0x50, 0x60, 0xFF]);

        AssertFrame(dib, 2, 1, 0x00, 0x00, 0x00, 0x00, 0x40, 0x50, 0x60, 0xFF);
    }

    [TestMethod]
    public void ADeclaredAlphaThatIsZeroEverywhereIsFullyTransparent()
    {
        var dib = Concat(Header(124, 2, 1, 32, Bitfields, masks: [.. Bgra, 0xFF000000]), [0x40, 0x50, 0x60, 0x00, 0x11, 0x22, 0x33, 0x00]);

        AssertFrame(dib, 2, 1, 0x40, 0x50, 0x60, 0x00, 0x11, 0x22, 0x33, 0x00);
    }

    [TestMethod]
    public void MasksInAnotherOrderArePermutedIntoBgra()
    {
        // Red in the low byte, blue in the third: the stored bytes are R G B A.
        var dib = Concat(Header(108, 2, 1, 32, Bitfields, masks: [0x000000FF, 0x0000FF00, 0x00FF0000, 0xFF000000]), [0x11, 0x22, 0x33, 0xFF, 0xAA, 0xBB, 0xCC, 0x80]);

        AssertFrame(dib, 2, 1, 0x33, 0x22, 0x11, 0xFF, 0xCC, 0xBB, 0xAA, 0x80);
    }

    [TestMethod]
    public void A32BitBitmapWhoseFourthBytesAreAllZeroIsOpaque()
    {
        var dib = Concat(Header(40, 2, 1, 32, Rgb), [0x10, 0x20, 0x30, 0x00, 0x40, 0x50, 0x60, 0x00]);

        AssertFrame(dib, 2, 1, 0x10, 0x20, 0x30, 0xFF, 0x40, 0x50, 0x60, 0xFF);
    }

    [TestMethod]
    public void A32BitBitmapWithAnyFourthByteSetCarriesThemAsAlpha()
    {
        var dib = Concat(Header(40, 2, 1, 32, Rgb), [0x10, 0x20, 0x30, 0x00, 0x40, 0x50, 0x60, 0x80]);

        AssertFrame(dib, 2, 1, 0x10, 0x20, 0x30, 0x00, 0x40, 0x50, 0x60, 0x80);
    }

    [TestMethod]
    public void A40ByteHeaderWithMasksHasThemBeforeItsRows()
    {
        var dib = Concat(Header(40, 2, 1, 32, Bitfields), Dwords(Bgra), [0x10, 0x20, 0x30, 0x00, 0xE9, 0x69, 0x69, 0x80]);

        AssertFrame(dib, 2, 1, 0x10, 0x20, 0x30, 0x00, 0xE9, 0x69, 0x69, 0x80);
    }

    [TestMethod]
    public void TheV5WindowsMakesFromABitmapHasItsMasksAgainBeforeItsRows()
    {
        // WinForms' 2×2 as Windows hands its CF_BITMAP over as CF_DIBV5: 124 + 12 + 16 bytes, its alpha bytes straight.
        var dib = Concat(Header(124, 2, 2, 32, Bitfields, masks: [.. Bgra, 0]), Dwords(Bgra),
            [0x00, 0xFF, 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF],
            [0xD3, 0xD3, 0xD3, 0x00, 0xE9, 0x69, 0x69, 0x80]);
        Assert.HasCount(152, dib);

        AssertFrame(dib, 2, 2,
            0xD3, 0xD3, 0xD3, 0x00, 0xE9, 0x69, 0x69, 0x80,
            0x00, 0xFF, 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF);
    }

    [TestMethod]
    public void AProfileAfterTheRowsIsNotPartOfThem()
    {
        var header = Header(124, 2, 1, 32, Bitfields, masks: [.. Bgra, 0xFF000000]);
        BitConverter.GetBytes(0x4D424544u).CopyTo(header, 56); // PROFILE_EMBEDDED
        BitConverter.GetBytes(124u + 8).CopyTo(header, 112);
        BitConverter.GetBytes(16u).CopyTo(header, 116);
        var dib = Concat(header, [0x10, 0x20, 0x30, 0xFF, 0x40, 0x50, 0x60, 0xFF], new byte[16]);

        AssertFrame(dib, 2, 1, 0x10, 0x20, 0x30, 0xFF, 0x40, 0x50, 0x60, 0xFF);
    }

    [TestMethod]
    [DataRow(0x4C494E4Bu, DisplayName = "PROFILE_LINKED")]
    [DataRow(0x4D424544u, DisplayName = "PROFILE_EMBEDDED")]
    public void RowsWhoseFirstPixelsAreTheMasksAreRowsWhenTheProfileStartsRightAfterThem(uint profileType)
    {
        // Red, green and blue, then a profile where the rows end, as a packed DIB places it: no masks table between.
        var header = Header(124, 3, 1, 32, Bitfields, masks: [.. Bgra, 0]);
        BitConverter.GetBytes(profileType).CopyTo(header, 56);
        BitConverter.GetBytes(124u + 12).CopyTo(header, 112);
        BitConverter.GetBytes(24u).CopyTo(header, 116);
        var dib = Concat(header, Dwords(Bgra), "C:\\ImageStash-probe.icc\0"u8.ToArray());

        AssertFrame(dib, 3, 1, 0x00, 0x00, 0xFF, 0xFF, 0x00, 0xFF, 0x00, 0xFF, 0xFF, 0x00, 0x00, 0xFF);
    }

    [TestMethod]
    public void A24BitRowSkipsItsPaddingAndIsOpaque()
    {
        // Three pixels are nine bytes, stored in twelve.
        var dib = Concat(Header(40, 3, 2, 24, Rgb),
            [0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0xEE, 0xEE, 0xEE],
            [0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17, 0x18, 0x19, 0xEE, 0xEE, 0xEE]);

        AssertFrame(dib, 3, 2,
            0x11, 0x12, 0x13, 0xFF, 0x14, 0x15, 0x16, 0xFF, 0x17, 0x18, 0x19, 0xFF,
            0x01, 0x02, 0x03, 0xFF, 0x04, 0x05, 0x06, 0xFF, 0x07, 0x08, 0x09, 0xFF);
    }

    [TestMethod]
    public void A16BitRgbBitmapIs555()
    {
        // Red, green, blue full, then 16 of 31 in each.
        var dib = Concat(Header(40, 4, 1, 16, Rgb), Words(0x7C00, 0x03E0, 0x001F, 0x4210));

        AssertFrame(dib, 4, 1, 0x00, 0x00, 0xFF, 0xFF, 0x00, 0xFF, 0x00, 0xFF, 0xFF, 0x00, 0x00, 0xFF, 132, 132, 132, 0xFF);
    }

    [TestMethod]
    public void A16BitBitmapWithMasksIs565()
    {
        // Red, green, blue full, then 32 of 63 in green; one pixel padded to the row's DWORD.
        var dib = Concat(Header(40, 3, 1, 16, Bitfields), Dwords(0xF800, 0x07E0, 0x001F), Words(0xF800, 0x07E0, 0x001F, 0xEEEE));
        var dibWithGreen = Concat(Header(40, 1, 1, 16, Bitfields), Dwords(0xF800, 0x07E0, 0x001F), Words(0x0400, 0));

        AssertFrame(dib, 3, 1, 0x00, 0x00, 0xFF, 0xFF, 0x00, 0xFF, 0x00, 0xFF, 0xFF, 0x00, 0x00, 0xFF);
        AssertFrame(dibWithGreen, 1, 1, 0x00, 130, 0x00, 0xFF);
    }

    [TestMethod]
    public void An8BitBitmapReadsTheColorsOfItsTableOfFour()
    {
        // biClrUsed 4: the rows follow four entries, not 256; an index past them is black.
        var dib = Concat(Header(40, 3, 1, 8, Rgb, colorsUsed: 4), Dwords(0x00000000, 0x00FF0000, 0x0000FF00, 0x00102030), [0x01, 0x03, 0x07, 0x00]);

        AssertFrame(dib, 3, 1, 0x00, 0x00, 0xFF, 0xFF, 0x30, 0x20, 0x10, 0xFF, 0x00, 0x00, 0x00, 0xFF);
    }

    [TestMethod]
    public void A4BitBitmapReadsItsFullTableHighNibbleFirst()
    {
        var table = Enumerable.Range(0, 16).Select(i => (uint)(i * 0x101010)).ToArray();
        var dib = Concat(Header(40, 3, 1, 4, Rgb), Dwords(table), [0x12, 0x30, 0x00, 0x00]);

        AssertFrame(dib, 3, 1, 0x10, 0x10, 0x10, 0xFF, 0x20, 0x20, 0x20, 0xFF, 0x30, 0x30, 0x30, 0xFF);
    }

    [TestMethod]
    public void A1BitBitmapReadsItsTwoColorsHighBitFirst()
    {
        // Ten pixels, 1010000011, in two bytes padded to four.
        var dib = Concat(Header(40, 10, 1, 1, Rgb), Dwords(0x00000000, 0x00FFFFFF), [0xA0, 0xC0, 0x00, 0x00]);

        var frame = Converted(dib, 10, 1);
        var white = frame.Pixels.Chunk(4).Select(pixel => pixel[0] == 0xFF).ToArray();
        CollectionAssert.AreEqual(new[] { true, false, true, false, false, false, false, false, true, true }, white);
        Assert.IsTrue(frame.Pixels.Chunk(4).All(pixel => pixel[3] == 0xFF), "opaque");
    }

    [TestMethod]
    public void ANegativeHeightIsTopDownAlready()
    {
        var dib = Concat(Header(40, 1, -2, 32, Rgb), [0x01, 0x02, 0x03, 0x00], [0x04, 0x05, 0x06, 0x00]);

        AssertFrame(dib, 1, 2, 0x01, 0x02, 0x03, 0xFF, 0x04, 0x05, 0x06, 0xFF);
    }

    [TestMethod]
    [DataRow(12, DisplayName = "twelve bytes")]
    public void ABitmapTooShortForAHeaderIsNamed(int length) =>
        AssertUnreadable(Header(40, 1, 1, 32, Rgb)[..length], "it is too short to hold a bitmap header");

    [TestMethod]
    public void AHeaderOfAnotherKindIsNamed()
    {
        var dib = Concat(Header(40, 1, 1, 32, Rgb), new byte[4]);
        BitConverter.GetBytes(12u).CopyTo(dib, 0);

        AssertUnreadable(dib, "its header is of a kind not supported (12 bytes)");
    }

    [TestMethod]
    public void RowsCutShortAreNamed() =>
        AssertUnreadable(Concat(Header(40, 2, 2, 32, Rgb), new byte[12]), "it ends before its last row");

    [TestMethod]
    public void AStatedSizeTooSmallForTheRowsIsNamed() =>
        AssertUnreadable(Concat(Header(40, 2, 2, 32, Rgb, sizeImage: 8), new byte[16]), "its stated size cannot hold its rows");

    [TestMethod]
    [DataRow(4u, DisplayName = "BI_JPEG")]
    [DataRow(5u, DisplayName = "BI_PNG")]
    [DataRow(6u, DisplayName = "BI_ALPHABITFIELDS")]
    public void ACompressionNoScreenBitmapUsesIsNamed(uint compression) =>
        AssertUnreadable(Concat(Header(40, 2, 1, 32, compression), new byte[8]), "it is compressed in a way that is not supported");

    [TestMethod]
    public void AnRle8BitmapReadsItsRunsAndItsIndexesAsTheyAre()
    {
        // Bottom row: two of index 1, one of index 2, end of line; top row: indexes 3, 0, 1 as they are (padded to a
        // word), end of bitmap.
        var dib = Concat(Header(40, 3, 2, 8, 1, colorsUsed: 4), Dwords(0x00000000, 0x00FF0000, 0x0000FF00, 0x000000FF),
            [0x02, 0x01, 0x01, 0x02, 0x00, 0x00, 0x00, 0x03, 0x03, 0x00, 0x01, 0x00, 0x00, 0x01]);

        AssertFrame(dib, 3, 2,
            0xFF, 0x00, 0x00, 0xFF, 0x00, 0x00, 0x00, 0xFF, 0x00, 0x00, 0xFF, 0xFF,
            0x00, 0x00, 0xFF, 0xFF, 0x00, 0x00, 0xFF, 0xFF, 0x00, 0xFF, 0x00, 0xFF);
    }

    [TestMethod]
    public void AnRle8MoveLeavesThePixelsItSkipsTransparent()
    {
        // A move right 1 and up 1, then one pixel of index 1, then the end of the bitmap.
        var dib = Concat(Header(40, 3, 2, 8, 1, colorsUsed: 2), Dwords(0x00000000, 0x00FF0000), [0x00, 0x02, 0x01, 0x01, 0x01, 0x01, 0x00, 0x01]);

        AssertFrame(dib, 3, 2,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xFF, 0xFF, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00);
    }

    [TestMethod]
    public void AnRle4BitmapAlternatesTheTwoIndexesOfARun()
    {
        // Bottom row: five pixels alternating 1 and 2; top row: 3, 1, 2 as they are, then two of 4.
        var table = Enumerable.Range(0, 16).Select(i => (uint)(i * 0x101010)).ToArray();
        var dib = Concat(Header(40, 5, 2, 4, 2), Dwords(table), [0x05, 0x12, 0x00, 0x00, 0x00, 0x03, 0x31, 0x20, 0x02, 0x44, 0x00, 0x01]);

        var gray = Converted(dib, 5, 2).Pixels.Chunk(4).Select(pixel => pixel[0] / 0x10).ToArray();
        CollectionAssert.AreEqual(new[] { 3, 1, 2, 4, 4, 1, 2, 1, 2, 1 }, gray);
    }

    [TestMethod]
    public void AnRle8LineRunningPastTheEdgeFurtherThanAColumnCanCountIsReadAndNeverThrows()
    {
        // Runs of 255 with no end of line carry the column on past 2^31 pixels; the pixel before them stays.
        var runs = new byte[2 * (int.MaxValue / 255 + 2)];
        for (var i = 0; i < runs.Length; i += 2)
        {
            (runs[i], runs[i + 1]) = (0xFF, 0x00);
        }

        var dib = Concat(Header(40, 2, 1, 8, 1, colorsUsed: 2), Dwords(0x00000000, 0x00FF0000), [0x01, 0x01], runs, [0x00, 0x01]);

        AssertFrame(dib, 2, 1, 0x00, 0x00, 0xFF, 0xFF, 0x00, 0x00, 0x00, 0xFF);
    }

    [TestMethod]
    public void ARunLengthEncodedBitmapStoredTopDownIsNamed() =>
        AssertUnreadable(Concat(Header(40, 1, -1, 8, 1), new byte[1024], [0x00, 0x01]), "its run-length encoded rows are stored top-down");

    [TestMethod]
    public void ACompressionThatDoesNotFitTheDepthIsNamed() =>
        AssertUnreadable(Concat(Header(40, 1, 1, 4, 1), new byte[64], [0x00, 0x01]), "its compression does not fit 4 bits per pixel");

    [TestMethod]
    public void ACalibratedColorSpaceIsConvertedToSrgb()
    {
        // sRGB's own primaries and white, but linear light (gamma 1): a mid gray of 0x80 is sRGB's 188.
        var header = Header(108, 1, 1, 24, Rgb);
        BitConverter.GetBytes(0u).CopyTo(header, 56); // LCS_CALIBRATED_RGB
        double[] primaries = [0.4124, 0.2126, 0.0193, 0.3576, 0.7152, 0.1192, 0.1805, 0.0722, 0.9505];
        for (var i = 0; i < 9; i++)
        {
            BitConverter.GetBytes((int)Math.Round(primaries[i] * (1 << 30))).CopyTo(header, 60 + i * 4);
        }

        Dwords(0x10000, 0x10000, 0x10000).CopyTo(header, 96);
        var gray = Converted(Concat(header, [0x80, 0x80, 0x80, 0x00]), 1, 1).Pixels;

        Assert.IsTrue(gray[..3].All(channel => Math.Abs(channel - 188) <= 1) && gray[3] == 0xFF, string.Join(" ", gray));
    }

    [TestMethod]
    public void AnEmbeddedProfileIsConvertedToSrgb()
    {
        var icc = LinearSrgbProfile();
        var header = Header(124, 1, 1, 24, Rgb);
        BitConverter.GetBytes(0x4D424544u).CopyTo(header, 56); // PROFILE_EMBEDDED
        BitConverter.GetBytes(124u + 4).CopyTo(header, 112);
        BitConverter.GetBytes((uint)icc.Length).CopyTo(header, 116);

        var gray = Converted(Concat(header, [0x80, 0x80, 0x80, 0x00], icc), 1, 1).Pixels;

        Assert.IsTrue(gray[..3].All(channel => Math.Abs(channel - 188) <= 1) && gray[3] == 0xFF, string.Join(" ", gray));
    }

    [TestMethod]
    public void AnEmbeddedProfileThatCannotBeParsedLeavesTheColorsAsTheyAre()
    {
        var header = Header(124, 1, 1, 24, Rgb);
        BitConverter.GetBytes(0x4D424544u).CopyTo(header, 56);
        BitConverter.GetBytes(124u + 4).CopyTo(header, 112);
        BitConverter.GetBytes(16u).CopyTo(header, 116);

        AssertFrame(Concat(header, [0x80, 0x40, 0x20, 0x00], new byte[16]), 1, 1, 0x80, 0x40, 0x20, 0xFF);
    }

    [TestMethod]
    [DataRow(0x73524742u, DisplayName = "LCS_sRGB")]
    [DataRow(0x57696E20u, DisplayName = "LCS_WINDOWS_COLOR_SPACE")]
    [DataRow(0x4C494E4Bu, DisplayName = "a linked profile, not opened")]
    [DataRow(0u, DisplayName = "calibrated without endpoints or gammas")]
    public void AColorSpaceThatIsSrgbOrSaysNothingLeavesTheColorsAsTheyAre(uint type)
    {
        var header = Header(124, 1, 1, 24, Rgb);
        BitConverter.GetBytes(type).CopyTo(header, 56);

        AssertFrame(Concat(header, [0x80, 0x40, 0x20, 0x00]), 1, 1, 0x80, 0x40, 0x20, 0xFF);
    }

    [TestMethod]
    [DataRow(0, 1)]
    [DataRow(1, 0)]
    [DataRow(-1, 1)]
    public void ABitmapWithoutASizeIsNamed(int width, int height) =>
        AssertUnreadable(Concat(Header(40, width, height, 32, Rgb), new byte[16]), "its width or height is not a size");

    [TestMethod]
    public void AnUnknownDepthIsNamed() =>
        AssertUnreadable(Concat(Header(40, 1, 1, 2, Rgb), new byte[32]), "its depth of 2 bits per pixel is not supported");

    [TestMethod]
    public void ASizeWhoseRowsOverflowIsNamedAndNeverThrows() =>
        AssertUnreadable(Concat(Header(40, int.MaxValue, int.MaxValue, 32, Rgb), new byte[4]), "it is too large to read");

    [TestMethod]
    public void A16BitBitmapWithADeclaredAlphaMaskCarriesItsAlpha()
    {
        // 1-5-5-5: a transparent red and an opaque red.
        var dib = Concat(Header(108, 2, 1, 16, Bitfields, masks: [0x7C00, 0x03E0, 0x001F, 0x8000]), Words(0x7C00, 0xFC00));

        AssertFrame(dib, 2, 1, 0x00, 0x00, 0xFF, 0x00, 0x00, 0x00, 0xFF, 0xFF);
    }

    [TestMethod]
    public void GarbageIsNamedAndNeverThrows()
    {
        var random = new Random(7);
        for (var i = 0; i < 2000; i++)
        {
            var dib = new byte[random.Next(0, 200)];
            random.NextBytes(dib);
            if (dib.Length >= 4 && i % 2 == 0)
            {
                BitConverter.GetBytes(new uint[] { 40, 108, 124 }[i % 3]).CopyTo(dib, 0);
            }

            var converted = WindowsDib.Convert(dib);
            Assert.IsTrue(converted.Succeeded || converted.Failure is { Action: "Paste", Subject: "the clipboard" }, converted.Failure?.Sentence);
        }
    }

    [TestMethod]
    public void PlausibleHeadersOverGarbageAreNamedOrReadAndNeverThrow()
    {
        var random = new Random(11);
        ushort[] depths = [1, 4, 8, 16, 24, 32];
        uint[] spaces = [0, 0x73524742, 0x4D424544];
        for (var i = 0; i < 5000; i++)
        {
            var size = new[] { 40, 108, 124 }[random.Next(3)];
            var header = Header(size, random.Next(-1, 9), random.Next(-8, 9), depths[random.Next(depths.Length)], (uint)random.Next(4), sizeImage: (uint)random.Next(0, 3) * (uint)random.Next(0, 300), colorsUsed: (uint)random.Next(0, 20));
            if (size >= 108)
            {
                random.NextBytes(header.AsSpan(40, size - 40));
                BitConverter.GetBytes(spaces[random.Next(spaces.Length)]).CopyTo(header, 56);
                if (size == 124)
                {
                    BitConverter.GetBytes((uint)random.Next(0, 700)).CopyTo(header, 112);
                    BitConverter.GetBytes((uint)random.Next(0, 300)).CopyTo(header, 116);
                }
            }

            var body = new byte[random.Next(0, 600)];
            random.NextBytes(body);
            var converted = WindowsDib.Convert(Concat(header, body));
            Assert.IsTrue(converted.Succeeded || converted.Failure is { Action: "Paste", Subject: "the clipboard" }, converted.Failure?.Sentence);
        }
    }

    private static void AssertFrame(byte[] dib, int width, int height, params byte[] pixels) =>
        CollectionAssert.AreEqual(pixels, Converted(dib, width, height).Pixels);

    private static Media.MediaFrame Converted(byte[] dib, int width, int height)
    {
        var converted = WindowsDib.Convert(dib);
        Assert.IsTrue(converted.Succeeded, converted.Failure?.Sentence);
        var frame = converted.Value!;
        Assert.AreEqual((width, height, 0), (frame.Width, frame.Height, frame.DurationMs));
        return frame;
    }

    private static void AssertUnreadable(byte[] dib, string why)
    {
        var converted = WindowsDib.Convert(dib);
        Assert.AreEqual($"Paste failed: the clipboard, its picture could not be read: {why}", converted.Failure?.Sentence);
    }

    /// <summary>
    /// A BITMAPINFOHEADER (40), BITMAPV4HEADER (108) or BITMAPV5HEADER (124); a V4 or V5 one carries
    /// <paramref name="masks"/> (red, green, blue, alpha) and, for a V5, LCS_sRGB.
    /// </summary>
    private static byte[] Header(int size, int width, int height, ushort bits, uint compression, uint sizeImage = 0, uint colorsUsed = 0, uint[]? masks = null)
    {
        var header = new byte[size];
        BitConverter.GetBytes(size).CopyTo(header, 0);
        BitConverter.GetBytes(width).CopyTo(header, 4);
        BitConverter.GetBytes(height).CopyTo(header, 8);
        BitConverter.GetBytes((ushort)1).CopyTo(header, 12);
        BitConverter.GetBytes(bits).CopyTo(header, 14);
        BitConverter.GetBytes(compression).CopyTo(header, 16);
        BitConverter.GetBytes(sizeImage).CopyTo(header, 20);
        BitConverter.GetBytes(colorsUsed).CopyTo(header, 32);
        if (size >= 108)
        {
            Dwords(masks ?? [0, 0, 0, 0]).CopyTo(header, 40);
            BitConverter.GetBytes(0x73524742u).CopyTo(header, 56);
        }

        return header;
    }

    /// <summary>
    /// A minimal ICC v2 display profile: sRGB's primaries adapted to D50, as the sRGB profile carries them, and a gamma
    /// of 1 for each channel, so linear light. Big-endian throughout.
    /// </summary>
    private static byte[] LinearSrgbProfile()
    {
        var icc = new byte[312];
        void Put(int offset, uint value) => BinaryPrimitives.WriteUInt32BigEndian(icc.AsSpan(offset), value);
        void Text(int offset, string four) => System.Text.Encoding.ASCII.GetBytes(four).CopyTo(icc, offset);
        void Xyz(int offset, double x, double y, double z)
        {
            Text(offset, "XYZ ");
            Put(offset + 8, (uint)(int)Math.Round(x * 65536));
            Put(offset + 12, (uint)(int)Math.Round(y * 65536));
            Put(offset + 16, (uint)(int)Math.Round(z * 65536));
        }

        Put(0, (uint)icc.Length);
        Put(8, 0x02100000);
        Text(12, "mntr");
        Text(16, "RGB ");
        Text(20, "XYZ ");
        Text(36, "acsp");
        Put(68, 63190); // D50: 0.9642, 1, 0.8249
        Put(72, 65536);
        Put(76, 54061);
        (string Tag, int Offset, int Size)[] tags = [("rXYZ", 216, 20), ("gXYZ", 236, 20), ("bXYZ", 256, 20), ("wtpt", 276, 20), ("rTRC", 296, 14), ("gTRC", 296, 14), ("bTRC", 296, 14)];
        Put(128, (uint)tags.Length);
        for (var i = 0; i < tags.Length; i++)
        {
            Text(132 + i * 12, tags[i].Tag);
            Put(136 + i * 12, (uint)tags[i].Offset);
            Put(140 + i * 12, (uint)tags[i].Size);
        }

        Xyz(216, 0.4361, 0.2225, 0.0139);
        Xyz(236, 0.3851, 0.7169, 0.0971);
        Xyz(256, 0.1431, 0.0606, 0.7141);
        Xyz(276, 0.9642, 1.0, 0.8249);
        Text(296, "curv");
        Put(304, 1);
        BinaryPrimitives.WriteUInt16BigEndian(icc.AsSpan(308), 0x0100);
        return icc;
    }

    private static byte[] Dwords(params uint[] values) => [.. values.SelectMany(BitConverter.GetBytes)];

    private static byte[] Words(params ushort[] values) => [.. values.SelectMany(BitConverter.GetBytes)];

    private static byte[] Concat(params byte[][] parts) => [.. parts.SelectMany(part => part)];
}
