using Avalonia;
using ImageStash.Capture;
using ImageStash.Seams;

namespace ImageStash.Tests.Capture;

/// <summary>
/// G14's drag as arithmetic on physical pixels across displays of different scaling (E1): the rectangle from the press
/// to the pointer, both corner pixels in, kept to the display the press was on and never crossing onto another; a
/// release at the press point, or within a click's tremble of it, a click without a drag, and any other release a drag,
/// however thin; the pointer brought back to its own physical pixel; the size label in physical pixels.
/// </summary>
[TestClass]
public sealed class RegionSelectionTests
{
    private static readonly Display Primary = new(new PixelRect(0, 0, 1280, 800), new PixelRect(0, 0, 1280, 752), 1, IsPrimary: true);
    private static readonly Display Right150 = new(new PixelRect(1280, 0, 1920, 1200), new PixelRect(1280, 0, 1920, 1152), 1.5, IsPrimary: false);
    private static readonly Display LeftAbove = new(new PixelRect(-1920, -300, 1920, 1080), new PixelRect(-1920, -300, 1920, 1032), 1.25, IsPrimary: false);

    [TestMethod]
    public void APressOnTheSecondDisplayDraggedOntoTheFirstStopsAtItsEdge()
    {
        var dragged = RegionSelection.Dragged(Right150, new PixelPoint(1300, 100), new PixelPoint(100, 900));
        var released = RegionSelection.Released(Right150, new PixelPoint(1300, 100), new PixelPoint(100, 900));

        Assert.AreEqual(new PixelRect(1280, 100, 21, 801), dragged, "from the press to the display's left edge, never onto the first display");
        Assert.IsTrue(Right150.Bounds.Contains(dragged));
        Assert.IsFalse(Primary.Bounds.Intersects(dragged));
        Assert.AreEqual(new SelectedRegion(Right150, dragged), released, "the release selects what the drag showed, on the press's display");
    }

    [TestMethod]
    public void ADragPastTheDisplaysLastRowAndColumnTakesThemIn()
    {
        var dragged = RegionSelection.Released(Primary, new PixelPoint(100, 100), new PixelPoint(5000, 5000))!.Rectangle;
        var toTheCorner = RegionSelection.Released(Primary, new PixelPoint(100, 100), new PixelPoint(1279, 799))!.Rectangle;

        Assert.AreEqual(new PixelRect(100, 100, 1180, 700), dragged, "to the right and bottom edges inclusive");
        Assert.AreEqual(dragged, toTheCorner, "the pointer on the last pixel takes that pixel in");
        Assert.AreEqual((Primary.Bounds.Right, Primary.Bounds.Bottom), (dragged.Right, dragged.Bottom));
    }

    [TestMethod]
    public void OnADisplayLeftOfAndAboveThePrimaryTheRectangleStaysOnIt()
    {
        var released = RegionSelection.Released(LeftAbove, new PixelPoint(-100, -250), new PixelPoint(300, 2000));

        Assert.AreEqual(new SelectedRegion(LeftAbove, new PixelRect(-100, -250, 100, 1030)), released, "negative coordinates, the edges at x = -1 and y = 779");
    }

    [TestMethod]
    public void AReleaseAtThePressPointIsAClickWithoutADrag()
    {
        Assert.IsNull(RegionSelection.Released(Primary, new PixelPoint(400, 300), new PixelPoint(400, 300)));
        Assert.IsNull(RegionSelection.Released(Right150, new PixelPoint(1280, 0), new PixelPoint(0, 0)), "a drag only off the display's edge has not moved on it");
    }

    [TestMethod]
    public void AClicksTrembleIsStillAClickWithoutADrag()
    {
        Assert.IsNull(RegionSelection.Released(Primary, new PixelPoint(400, 300), new PixelPoint(402, 298)), "two pixels along each axis");
        Assert.IsNull(RegionSelection.Released(Primary, new PixelPoint(400, 300), new PixelPoint(398, 301)));
        Assert.AreEqual(new PixelRect(400, 300, 4, 1), RegionSelection.Released(Primary, new PixelPoint(400, 300), new PixelPoint(403, 300))?.Rectangle, "three pixels across is a drag");
    }

    [TestMethod]
    public void ADragAlongOneAxisSelectsAStripOnePixelThick()
    {
        Assert.AreEqual(new PixelRect(400, 300, 301, 1), RegionSelection.Released(Primary, new PixelPoint(400, 300), new PixelPoint(700, 300))?.Rectangle, "across only: a drag, not a click (G14)");
        Assert.AreEqual(new PixelRect(400, 20, 1, 281), RegionSelection.Released(Primary, new PixelPoint(400, 300), new PixelPoint(400, 20))?.Rectangle, "up only");
    }

    [TestMethod]
    public void APointerPositionInDeviceIndependentUnitsComesBackToItsOwnPhysicalPixelAtAnyScaling()
    {
        foreach (var scaling in new[] { 1, 1.25, 1.5, 1.75, 2, 2.25, 2.5, 2.75, 3, 3.5 })
        {
            foreach (var pixel in Enumerable.Range(0, 4000))
            {
                // As Avalonia hands it over: the pointer's client pixel divided by the window's scaling.
                var at = RegionSelection.OnDesktop(new PixelPoint(1280, -300), new Point(pixel / scaling, (pixel + 7) / scaling), scaling);
                Assert.AreEqual(new PixelPoint(1280 + pixel, -300 + pixel + 7), at, $"pixel {pixel} at {scaling}");
            }
        }

        Assert.AreEqual(new PixelPoint(61, 61), RegionSelection.OnDesktop(default, new Point(61 / 1.75, 61 / 1.75), 1.75), "where truncation would give 60");
    }

    [TestMethod]
    public void TheRectangleIsTheSameWhicheverCornerTheDragStartedFrom()
    {
        var (left, top, right, bottom) = (1400, 200, 2257, 915);
        PixelPoint[] corners = [new(left, top), new(right, top), new(left, bottom), new(right, bottom)];

        var rectangles = corners.Select(press => RegionSelection.Released(Right150, press, new PixelPoint(left + right - press.X, top + bottom - press.Y))!.Rectangle).ToList();

        Assert.AreEqual(1, rectangles.Distinct().Count(), string.Join("; ", rectangles));
        Assert.AreEqual(new PixelRect(1400, 200, 858, 716), rectangles[0]);
    }

    [TestMethod]
    public void TheLabelReadsTheSizeInPhysicalPixels()
    {
        Assert.AreEqual("858 × 716", RegionSelection.Label(new PixelRect(1400, 200, 858, 716)));
        Assert.AreEqual("1920 × 1200", RegionSelection.Label(Right150.Bounds), "whatever the display's scaling");
    }
}
