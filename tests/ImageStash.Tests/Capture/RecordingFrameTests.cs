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

    /// <summary>
    /// A window's size in layout units never runs past its place in physical pixels, at every scaling, so the frame and
    /// the bar at a place touching the display's edge stay on the display (G15), and lands on it to the pixel wherever
    /// Avalonia's conversion can: everywhere but where the place over the scaling, times the scaling, falls a hair short.
    /// </summary>
    [TestMethod]
    [DataRow(1.0)]
    [DataRow(1.25)]
    [DataRow(1.5)]
    [DataRow(1.75)]
    [DataRow(2.0)]
    [DataRow(2.25)]
    public void AWindowsSizeCoversItsPlaceToThePixelAndNoFurtherAtEveryScaling(double scaling)
    {
        var display = new PixelRect(0, 0, 2560, 1440);
        var workingArea = new PixelRect(0, 0, 2560, 1368);
        var places = new[]
        {
            new PixelRect(display.Right - 1200, workingArea.Bottom - 700, 1200, 700),
            new PixelRect(0, 0, 1001, 333),
            new PixelRect(display.Right - 317, workingArea.Bottom - 68, 317, 68),
        }.Concat(Enumerable.Range(1, 400).Select(side => new PixelRect(display.Right - side, 0, side, side + 1)));
        foreach (var place in places)
        {
            var size = RecordingFrame.WindowSize(place.Size, scaling);
            var shown = new PixelSize(OnScreen(size.Width, scaling), OnScreen(size.Height, scaling));

            Assert.IsTrue(shown.Width <= place.Width && shown.Height <= place.Height, $"{place} at {scaling}: {size} units shown {shown}, past the place");
            Assert.AreEqual(Reachable(place.Width, scaling), shown.Width, $"{place} at {scaling}: on the place's right edge wherever Avalonia can land there");
            Assert.AreEqual(Reachable(place.Height, scaling), shown.Height, $"{place} at {scaling}: on the place's bottom edge wherever Avalonia can land there");
        }

        Assert.AreEqual(scaling == 1.75 ? 229 : 230, OnScreen(RecordingFrame.WindowSize(new PixelSize(230, 230), scaling).Width, scaling), "the one kind of size that lands short, as the probe found it");
    }

    /// <summary>
    /// The physical pixels Avalonia gives a window <paramref name="length"/> units long at <paramref name="scaling"/>: the
    /// length snapped up to whole pixels, then that over the scaling, times the scaling, truncated (probed on this PC's
    /// display at 100 % to 225 %, every size from 80 to 1,500 pixels: 175 units at 1.75 are 307 pixels, 230 / 1.75 units 229).
    /// </summary>
    private static int OnScreen(double length, double scaling) => (int)(Math.Ceiling(length * scaling - 1e-6) / scaling * scaling);

    /// <summary>The nearest to <paramref name="pixels"/> Avalonia can land a window without passing it: the pixels, or one short where their round trip through the scaling falls short.</summary>
    private static int Reachable(int pixels, double scaling) => pixels / scaling * scaling < pixels ? pixels - 1 : pixels;
}
