using Avalonia;
using ImageStash.Media;

namespace ImageStash.Tests.Media;

/// <summary>
/// Where a picture shows in the preview and the editor (G19): one picture pixel to one display pixel when it fits, scaled down whole
/// when it does not, never enlarged, its shape kept, centred on a whole display pixel.
/// </summary>
[TestClass]
public sealed class ShownPictureTests
{
    [TestMethod]
    public void APictureThatFitsShowsAtItsOwnSizeCentredAndIsNeverEnlarged()
    {
        var shown = ShownPicture.Fit(new PixelSize(8, 6), new Size(440, 400), scaling: 1);

        Assert.AreEqual(new Rect(216, 197, 8, 6), shown, "eight by six pixels stay eight by six, though the area could hold far more");
    }

    [TestMethod]
    public void APictureLargerThanTheAreaIsScaledDownWholeKeepingItsShape()
    {
        var wide = ShownPicture.Fit(new PixelSize(2000, 1000), new Size(440, 400), scaling: 1);
        var tall = ShownPicture.Fit(new PixelSize(300, 1200), new Size(440, 400), scaling: 1);

        Assert.AreEqual(440, wide.Width, 1e-9, "as wide as the area");
        Assert.AreEqual(220, wide.Height, 1e-9, "at its own ratio");
        Assert.AreEqual(90, wide.Y, 1e-9);
        Assert.AreEqual(400, tall.Height, 1e-9, "as tall as the area");
        Assert.AreEqual(100, tall.Width, 1e-9);
        Assert.AreEqual(170, tall.X, 1e-9);
    }

    [TestMethod]
    public void OnAScaledDisplayAPicturePixelIsOneDisplayPixelNotOneUnit()
    {
        var shown = ShownPicture.Fit(new PixelSize(300, 150), new Size(440, 400), scaling: 1.5);

        Assert.AreEqual(200, shown.Width, 1e-9, "300 pixels at 150 % are 200 units: full size, not enlarged by the scaling");
        Assert.AreEqual(100, shown.Height, 1e-9);
        Assert.AreEqual(Math.Round(shown.X * 1.5), shown.X * 1.5, 1e-9, "its corner on a whole display pixel");
        Assert.AreEqual(Math.Round(shown.Y * 1.5), shown.Y * 1.5, 1e-9);
    }
}
