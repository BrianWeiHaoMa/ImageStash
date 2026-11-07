using Avalonia;
using ImageStash.Capture;

namespace ImageStash.Tests.Capture;

/// <summary>
/// Where the recording's bar goes (G15): beneath the region, centred on it, while the display's working area has room
/// below; above it when there is room above and none below; over its bottom edge, inside it, when there is room on
/// neither side; always within the working area, so never under the taskbar. And the bar's words: the time against the
/// cap and the size so far.
/// </summary>
[TestClass]
public sealed class RecordingBarTests
{
    /// <summary>The primary display's working area above a 48-pixel taskbar.</summary>
    private static readonly PixelRect WorkingArea = new(0, 0, 1280, 752);

    private static readonly PixelSize Bar = new(400, 44);

    private const int Gap = 14;

    [TestMethod]
    public void WithRoomBelowTheBarIsBeneathTheRegionCentredOnIt()
    {
        Assert.AreEqual(new PixelRect(300, 414, 400, 44), RecordingBar.Place(new PixelRect(200, 100, 600, 300), WorkingArea, Bar, Gap));
        Assert.AreEqual(new PixelRect(300, 708, 400, 44), RecordingBar.Place(new PixelRect(200, 100, 600, 594), WorkingArea, Bar, Gap), "just room below: the bar's bottom on the working area's");
    }

    [TestMethod]
    public void WithNoRoomBelowTheBarIsAboveTheRegion()
    {
        Assert.AreEqual(new PixelRect(300, 342, 400, 44), RecordingBar.Place(new PixelRect(200, 400, 600, 320), WorkingArea, Bar, Gap), "the region's bottom within the gap and the bar's height of the working area's");
        Assert.AreEqual(new PixelRect(300, 242, 400, 44), RecordingBar.Place(new PixelRect(200, 300, 600, 500), WorkingArea, Bar, Gap), "a region reaching over the taskbar: above it, never under the taskbar");
    }

    [TestMethod]
    public void WithRoomOnNeitherSideTheBarIsOverTheRegionsBottomEdgeInsideIt()
    {
        Assert.AreEqual(new PixelRect(300, 694, 400, 44), RecordingBar.Place(new PixelRect(200, 0, 600, 752), WorkingArea, Bar, Gap), "a region as tall as the working area: inside it, bottom-aligned");
        Assert.AreEqual(new PixelRect(440, 694, 400, 44), RecordingBar.Place(new PixelRect(0, 0, 1280, 800), WorkingArea, Bar, Gap), "the whole display: inside it, above the taskbar");
        Assert.AreEqual(new PixelRect(300, 0, 400, 44), RecordingBar.Place(new PixelRect(200, 10, 600, 40), new PixelRect(0, 0, 1280, 80), Bar, Gap), "never above the working area's top");
    }

    [TestMethod]
    public void TheBarStaysWithinTheWorkingAreaAcross()
    {
        Assert.AreEqual(new PixelRect(0, 194, 400, 44), RecordingBar.Place(new PixelRect(0, 100, 120, 80), WorkingArea, Bar, Gap), "a region at the left edge narrower than the bar: the bar's left is the working area's");
        Assert.AreEqual(new PixelRect(880, 194, 400, 44), RecordingBar.Place(new PixelRect(1200, 100, 80, 80), WorkingArea, Bar, Gap), "at the right edge: the bar's right is the working area's");
        var second = new PixelRect(1280, 0, 1920, 1152);
        Assert.AreEqual(new PixelRect(1280, 314, 400, 44), RecordingBar.Place(new PixelRect(1290, 200, 100, 100), second, Bar, Gap), "on a second display, within its own working area");
    }

    [TestMethod]
    public void TheBarReadsTheTimeAgainstTheCapAndTheSizeSoFar()
    {
        Assert.AreEqual("0:00.0", RecordingBar.Time(0));
        Assert.AreEqual("0:01.6", RecordingBar.Time(1_650), "tenths, never rounded up past the time recorded");
        Assert.AreEqual("1:01.2", RecordingBar.Time(61_250));
        Assert.AreEqual("0:30", RecordingBar.Time(30_000, tenths: false), "the cap without tenths");
        Assert.AreEqual("~0 KB", RecordingBar.Size(0), "nothing written yet");
        Assert.AreEqual("~614 KB", RecordingBar.Size(629_146));
        Assert.AreEqual("~1.1 MB", RecordingBar.Size(1_153_434));
    }
}
