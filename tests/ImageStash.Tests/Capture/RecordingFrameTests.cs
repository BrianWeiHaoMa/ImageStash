using Avalonia;
using ImageStash.Capture;

namespace ImageStash.Tests.Capture;

/// <summary>
/// The recording frame's layout (G15): a band around the region, just outside it where the display has room and over
/// its own edge where it has none, so it always shows; the REC label with the region's size on the band above the
/// region, or inside its top-left corner at the display's top, kept on the display; the window the band and the label
/// together, its shown parts given from its own top-left, so everything else of it is a hole the region shows through.
/// </summary>
[TestClass]
public sealed class RecordingFrameTests
{
    private static readonly PixelRect Display = new(0, 0, 1280, 800);

    private static readonly PixelSize Label = new(120, 24);

    [TestMethod]
    public void TheBandLiesJustOutsideTheRegionWithTheLabelOnItAbove()
    {
        var layout = RecordingFrame.Layout(new PixelRect(200, 100, 300, 200), Display, 2, Label);

        Assert.AreEqual(new PixelRect(198, 74, 304, 228), layout.Window);
        CollectionAssert.AreEqual(new[]
        {
            new PixelRect(0, 24, 304, 2),
            new PixelRect(0, 226, 304, 2),
            new PixelRect(0, 26, 2, 200),
            new PixelRect(302, 26, 2, 200),
            new PixelRect(0, 0, 120, 24),
        }, layout.Parts.ToList(), "four strips around the region, every pixel of the region a hole, and the label");
        Assert.AreEqual(new PixelRect(0, 0, 120, 24), layout.Label);
    }

    [TestMethod]
    public void AtTheDisplaysTopLeftTheBandIsOverTheRegionsEdgeAndTheLabelInsideItsCorner()
    {
        var layout = RecordingFrame.Layout(new PixelRect(0, 0, 300, 200), Display, 2, Label);

        Assert.AreEqual(new PixelRect(0, 0, 302, 202), layout.Window, "nothing off the display");
        CollectionAssert.AreEqual(new[]
        {
            new PixelRect(0, 0, 302, 2),
            new PixelRect(0, 200, 302, 2),
            new PixelRect(0, 2, 2, 198),
            new PixelRect(300, 2, 2, 198),
            new PixelRect(0, 2, 120, 24),
        }, layout.Parts.ToList());
        Assert.AreEqual(new PixelRect(0, 2, 120, 24), layout.Label, "inside the region's top-left corner, below the band");
    }

    [TestMethod]
    public void AWholeDisplayRegionKeepsItsBandOnTheDisplay()
    {
        var layout = RecordingFrame.Layout(Display, Display, 3, Label);

        Assert.AreEqual(Display, layout.Window);
        CollectionAssert.AreEqual(new[]
        {
            new PixelRect(0, 0, 1280, 3),
            new PixelRect(0, 797, 1280, 3),
            new PixelRect(0, 3, 3, 794),
            new PixelRect(1277, 3, 3, 794),
            new PixelRect(0, 3, 120, 24),
        }, layout.Parts.ToList(), "a frame that still shows, over the region's own edge");
    }

    [TestMethod]
    public void TheLabelIsKeptOnTheDisplayAcrossAndOnASecondDisplayTheLayoutIsItsOwn()
    {
        var atRight = RecordingFrame.Layout(new PixelRect(1250, 300, 20, 20), Display, 2, Label);
        Assert.AreEqual(new PixelRect(1160, 274, 120, 48), atRight.Window, "the label ends at the display's right edge");
        Assert.AreEqual(new PixelRect(0, 0, 120, 24), atRight.Label);

        var left = new PixelRect(-1920, -200, 1920, 1080);
        var onLeft = RecordingFrame.Layout(new PixelRect(-1900, -200, 100, 50), left, 2, Label);
        Assert.AreEqual(new PixelRect(-1902, -200, 120, 52), onLeft.Window, "at that display's top: the label inside the region");
        Assert.AreEqual(new PixelRect(0, 2, 120, 24), onLeft.Label);
    }

    [TestMethod]
    public void TheBandIsTwoPixelsAtScalingOneAndTheNearestWholeAtAnotherAndTheLabelGivesTheSize()
    {
        Assert.AreEqual(2, RecordingFrame.Band(1));
        Assert.AreEqual(3, RecordingFrame.Band(1.25), "2.5 to the nearest whole, away from zero");
        Assert.AreEqual(3, RecordingFrame.Band(1.5));
        Assert.AreEqual(4, RecordingFrame.Band(2));
        Assert.AreEqual(1, RecordingFrame.Band(0.25), "never no band");
        Assert.AreEqual("● REC · 1440 × 810", RecordingFrame.Label(new PixelRect(5, 5, 1440, 810)));
    }
}
