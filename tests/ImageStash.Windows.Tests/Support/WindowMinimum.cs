using Avalonia;
using ImageStash.Store;

namespace ImageStash.Windows.Tests.Support;

/// <summary>The stash window's minimum size as a display lays it out.</summary>
public static class WindowMinimum
{
    /// <summary>
    /// <paramref name="size"/>, a window's in device-independent pixels, is <see cref="WindowSize.Minimum"/> within half
    /// of one: a window is a whole number of display pixels, so where the minimum times the display's scaling is not one
    /// (458 × 1.75 = 801.5) the window lies on a whole pixel beside it.
    /// </summary>
    public static void AssertAt(Size size, string because)
    {
        Assert.AreEqual(WindowSize.Minimum.Width, size.Width, 0.5, $"{because}: width");
        Assert.AreEqual(WindowSize.Minimum.Height, size.Height, 0.5, $"{because}: height");
    }
}
