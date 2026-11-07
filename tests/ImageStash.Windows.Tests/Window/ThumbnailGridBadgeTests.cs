using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using ImageStash.Window;
using ImageStash.Windows.Tests.Support;

namespace ImageStash.Windows.Tests.Window;

/// <summary>
/// The badge's words (G6, F10): the face the grid draws them in lays out in Segoe UI on this PC, at the weight the badge
/// asks, read from the glyph typeface Avalonia resolves, not from the family the face names.
/// </summary>
[TestClass]
public sealed class ThumbnailGridBadgeTests
{
    [TestMethod]
    public void TheBadgesWordsAreDrawnInSegoeUiAtTheWeightTheyAsk()
    {
        TrayDesktop.Run(() =>
        {
            // The grid draws the badge at 11 px in this face. Its drawing is not recorded instead: Avalonia's DrawingGroup
            // recorder throws on the rounded clip the grid pushes once a tile's thumbnail has loaded.
            foreach (var badge in new[] { "GIF", "WEBP" })
            {
                using var words = new TextLayout(badge, TileBadge.Face, 11, null);
                var faces = words.TextLines.SelectMany(line => line.TextRuns).OfType<ShapedTextRun>().Select(run => run.GlyphRun.GlyphTypeface).ToList();
                CollectionAssert.AreEqual(new[] { RenderedFamilies.SegoeUi }, RenderedFamilies.Of(words).ToList(), $"{badge} drawn in {string.Join(", ", faces.Select(face => face.FamilyName))}, not F10's Segoe UI");
                Assert.IsTrue(faces.All(face => face.Weight == FontWeight.SemiBold), $"{badge} at the weight the badge asks, not one its face fell to");
            }
        });
    }
}
