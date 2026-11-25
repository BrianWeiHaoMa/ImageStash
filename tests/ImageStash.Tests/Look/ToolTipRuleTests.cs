using Avalonia;
using ImageStash.Look;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Look;

/// <summary>
/// The tooltip rule (F10, the look's one home): a tip that repeats the words a control shows is dropped, whatever the
/// case and the spacing; a control shown as a glyph alone keeps a tip naming its action; a tip that adds a key or a
/// role keeps; and no code sets a tip but the rule.
/// </summary>
[TestClass]
public sealed class ToolTipRuleTests
{
    [TestMethod]
    [DataRow(1.0, -200.0, 620.0)]
    [DataRow(1.5, -300.0, 600.0)]
    public void ATileTipNearACornerFitsAboveTheTileAtTheTipsScale(double displayScaleRatio, double x, double y)
    {
        // Positions are in the owner's units. The destination display is to its left and may use a larger scale.
        var workingArea = new Rect(-900, 80, 900, 620);
        var tile = new Rect(-100, 660, 80, 60);
        var at = ToolTips.DrawnTipPosition(tile, workingArea, new Size(200, 36), displayScaleRatio);
        Assert.AreEqual(new Point(x, y), at);
    }

    [TestMethod]
    public void ATileSpanningDisplaysAnchorsToThePartOnThePointersDisplay()
    {
        var tile = new Rect(-40, 10, 120, 60);
        Assert.AreEqual(new Rect(-40, 10, 40, 60), ToolTips.VisiblePart(tile, new Rect(-900, 0, 900, 700)));
        Assert.AreEqual(new Rect(0, 10, 80, 60), ToolTips.VisiblePart(tile, new Rect(0, 0, 1200, 900)));
    }

    [TestMethod]
    [DataRow("Meme", "Meme")]
    [DataRow("Black box", "black box")]
    [DataRow("1 : 1", "1:1")]
    [DataRow(" Paste into Slack ", "Paste into Slack")]
    public void ATipRepeatingTheWordsAControlShowsIsDropped(string shown, string tip)
    {
        Assert.IsNull(ToolTipRule.Decide([shown], tip));
    }

    [TestMethod]
    public void ATipRepeatingAnyOfTheWordsAControlShowsIsDroppedThoughItShowsAGlyphBesideThem()
    {
        // The tool bar's buttons: a glyph, then the tool's name.
        Assert.IsNull(ToolTipRule.Decide(["", "Text"], "Text"));
    }

    [TestMethod]
    [DataRow("", "Settings")]
    [DataRow("", "Add to your stash")]
    [DataRow("", "Every tag")]
    public void AControlShownAsAGlyphAloneKeepsATipNamingItsAction(string glyph, string tip)
    {
        Assert.AreEqual(tip, ToolTipRule.Decide([glyph], tip));
        Assert.AreEqual(tip, ToolTipRule.Decide([], tip), "a dot or a square shows no words at all");
    }

    [TestMethod]
    [DataRow("Compare", "Hold to see the original (C)")]
    [DataRow("the audacity", "Rename (F2)")]
    [DataRow("Impact", "Font")]
    [DataRow("Back", "Back (Esc)")]
    public void ATipThatAddsWhatTheWordsDoNotSayStays(string shown, string tip)
    {
        Assert.AreEqual(tip, ToolTipRule.Decide([shown], tip));
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("  ")]
    public void NoTipIsNone(string? tip)
    {
        Assert.IsNull(ToolTipRule.Decide([], tip));
    }

    [TestMethod]
    public void NoCodeSetsATipButTheRule()
    {
        var root = RepositoryRoot.Above(AppContext.BaseDirectory) ?? throw new AssertFailedException("no repository root above the tests");
        var source = Path.Combine(root, "src");
        var home = Path.Combine(source, "ImageStash", "Look", "ToolTipRule.cs");

        var found = Directory.EnumerateFiles(source, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj") && path != home)
            .SelectMany(path => File.ReadLines(path).Select((line, i) => (path, line, i)))
            .Where(each => each.line.Contains("ToolTip.SetTip", StringComparison.Ordinal) || each.line.Contains("ToolTip.TipProperty, ", StringComparison.Ordinal))
            .Select(each => $"{Path.GetRelativePath(root, each.path)}:{each.i + 1}: {each.line.Trim()}")
            .ToList();

        Assert.IsTrue(File.Exists(home), home);
        Assert.IsEmpty(found, string.Join(Environment.NewLine, found));
    }
}
