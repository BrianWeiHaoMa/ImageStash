using Avalonia;
using ImageStash.Editor;
using ImageStash.Layers;
using ImageStash.Media;

namespace ImageStash.Tests.Editor;

/// <summary>
/// Where the editor's canvas shows the picture and how a point of the canvas maps back to the original's grid (G23,
/// G24, What crosses every boundary): the output fitted downscale-only, never enlarged; Shrink's scale and the fit
/// inverted on each axis, so a drag lands on the pixels it covers whatever the Shrink choice; a screen distance as a
/// distance in the original's pixels.
/// </summary>
[TestClass]
public sealed class CanvasMappingTests
{
    private static readonly LayerList Half = new([new ShrinkLayer(Shrink.Half)]);

    [TestMethod]
    public void AtHalfSizeTheQuarterPointsOfTheShownPictureAreTheOriginalsQuarterPointsNotTheOutputs()
    {
        var mapping = CanvasMapping.Of(64, 48, Half, new Size(400, 300), scaling: 1)!;

        Assert.AreEqual(new Rect(184, 138, 32, 24), mapping.Box, "the 32 × 24 output at its own size, centred, never enlarged");
        Assert.AreEqual(new System.Drawing.PointF(16, 12), mapping.ToOriginal(new Point(184 + 8, 138 + 6)), "not (8, 6): Shrink is inverted too");
        Assert.AreEqual(new System.Drawing.PointF(48, 36), mapping.ToOriginal(new Point(184 + 24, 138 + 18)));
        Assert.AreEqual(new Point(184 + 8, 138 + 6), mapping.ToCanvas(16, 12), "and back");
        Assert.AreEqual(16f, mapping.ToOriginal(8), "eight units of the screen are sixteen of the original's pixels");
    }

    [TestMethod]
    public void AtFullSizeTheSameDragLandsOnTheSamePixels()
    {
        var mapping = CanvasMapping.Of(64, 48, LayerList.Empty, new Size(400, 300), scaling: 1)!;

        Assert.AreEqual(new Rect(168, 126, 64, 48), mapping.Box);
        Assert.AreEqual(new System.Drawing.PointF(16, 12), mapping.ToOriginal(new Point(168 + 16, 126 + 12)));
        Assert.AreEqual(new System.Drawing.PointF(48, 36), mapping.ToOriginal(new Point(168 + 48, 126 + 36)));
    }

    [TestMethod]
    public void EachAxisIsScaledByItsOwnRoundedSideSoTheShownPicturesCornerIsTheOriginalsCorner()
    {
        var mapping = CanvasMapping.Of(7, 5, Half, new Size(400, 300), scaling: 1)!;
        var output = LayerComposerSides(7, 5, Half);

        Assert.AreEqual(output, (mapping.Box.Width, mapping.Box.Height));
        var corner = mapping.ToOriginal(mapping.Box.BottomRight);
        Assert.AreEqual(7f, corner.X, 1e-4f, "the right edge is the original's right edge");
        Assert.AreEqual(5f, corner.Y, 1e-4f, "and the bottom edge its bottom edge, though one scale for both axes would miss one");
    }

    [TestMethod]
    public void APictureLargerThanTheCanvasIsScaledDownAndPointsScaleUpToTheOriginal()
    {
        var mapping = CanvasMapping.Of(2000, 1000, LayerList.Empty, new Size(400, 300), scaling: 1)!;

        Assert.AreEqual(new Rect(0, 50, 400, 200), mapping.Box);
        Assert.AreEqual(new System.Drawing.PointF(1000, 500), mapping.ToOriginal(new Point(200, 150)));
        Assert.AreEqual(40f, mapping.ToOriginal(8), "eight units cover forty of the original's pixels");
    }

    [TestMethod]
    public void OnAScaledDisplayAPicturePixelIsADisplayPixelNotAUnit()
    {
        var mapping = CanvasMapping.Of(300, 150, LayerList.Empty, new Size(440, 400), scaling: 1.5)!;

        Assert.AreEqual(200, mapping.Box.Width, 1e-9, "300 pixels at 150 % are 200 units");
        Assert.AreEqual(1.5f, mapping.ToOriginal(1), 1e-4f);
    }

    [TestMethod]
    public void NothingIsShownInACanvasWithNoRoom()
    {
        Assert.IsNull(CanvasMapping.Of(8, 6, LayerList.Empty, new Size(0, 0), scaling: 1));
    }

    private static (double, double) LayerComposerSides(int width, int height, LayerList layers)
    {
        var output = LayerComposer.OutputSize(width, height, layers);
        return (output.Width, output.Height);
    }
}
