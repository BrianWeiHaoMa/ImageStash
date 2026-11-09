using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// The tag list's width (G4, G17): as wide as its widest name needs from the moment it opens, at every scaling Windows
/// offers the display, so that name shows whole; a whole number of physical pixels holding the entry's fixed measures as
/// the layout rounds each to the pixel grid and the words rounded up to a pixel; between the list's narrowest and widest.
/// The desktop checks show the name untrimmed at the display's real scale.
/// </summary>
[TestClass]
public sealed class TagPickerTests
{
    /// <summary>
    /// Words 231.4 units wide. The entry's fixed measures (the card's edges, the words' left padding, the mark's column
    /// and the words' right margin: 1, 8, 20, 35 and 1 units) take, in physical pixels as the layout rounds each, halves
    /// to even: 65 at 1; 1 + 10 + 25 + 44 + 1 = 81 at 1.25; 2 + 12 + 30 + 52 + 2 = 98 at 1.5; 2 + 14 + 35 + 61 + 2 = 114
    /// at 1.75; 130 at 2; 2 + 18 + 45 + 79 + 2 = 146 at 2.25. The words take 231.4 times the scaling, rounded up.
    /// </summary>
    [TestMethod]
    [DataRow(1.0, 65 + 232)]
    [DataRow(1.25, 81 + 290)]
    [DataRow(1.5, 98 + 348)]
    [DataRow(1.75, 114 + 405)]
    [DataRow(2.0, 130 + 463)]
    [DataRow(2.25, 146 + 521)]
    public void TheListIsAWholeNumberOfPixelsHoldingItsMeasuresAsTheLayoutRoundsThemAndItsWidestNameWhole(double scaling, int pixels)
    {
        var width = TagPicker.CardWidth(231.4, scaling);

        Assert.AreEqual(pixels, width * scaling, 1e-6, $"at {scaling}: {width} units");
    }

    [TestMethod]
    public void AtScalingOneTheWidthIsTheWordsBesideTheMeasuresRoundedUp()
    {
        Assert.AreEqual(297, TagPicker.CardWidth(231.4, 1));
        Assert.AreEqual(297, TagPicker.CardWidth(232, 1), "words of a whole width need no more");
    }

    [TestMethod]
    [DataRow(1.0)]
    [DataRow(1.25)]
    [DataRow(1.5)]
    [DataRow(1.75)]
    [DataRow(2.0)]
    [DataRow(2.25)]
    public void AShortNameGivesTheNarrowestListAndANameWiderThanItsWidestTheWidest(double scaling)
    {
        Assert.AreEqual(180, TagPicker.CardWidth(12, scaling), 1e-9, "never narrower than its narrowest");
        Assert.AreEqual(320, TagPicker.CardWidth(400, scaling), 1e-9, "never wider than its widest: the name is trimmed");
    }
}
