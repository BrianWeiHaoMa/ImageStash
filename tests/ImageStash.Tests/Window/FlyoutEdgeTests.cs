using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using ImageStash.Look;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// The flyout's edge as its controls hold it (F10, L2, G2): a hairline in the look's own colour in either take, seen
/// beside a white or a dark app and on the window's own ground, its corners rounded, taking no presses and no room; a
/// grip at every side and corner, each starting a drag of the border at its own edge. Where it shows on the real
/// window, and the drag itself, are the desktop suite's (`FlyoutFrameTests`).
/// </summary>
[TestClass]
public sealed class FlyoutEdgeTests
{
    [TestMethod]
    [DataRow("Light")]
    [DataRow("Dark")]
    public void TheLineIsTheLooksOwnEdgeSeenBesideTheAppAndOnTheWindowsGround(string theme) => ControlThread.Run(() =>
    {
        var dark = theme == "Dark";
        var look = Looks.All[0].Resources(dark ? ThemeVariant.Dark : ThemeVariant.Light, Color.FromRgb(0x00, 0x67, 0xC0));
        var edge = new FlyoutEdge((_, _) => { });
        // No Avalonia platform runs here, so the bound brush itself is the desktop suite's; here, what the line binds.
        var line = ((ISolidColorBrush)look[LookKeys.WindowEdge]!).Color;
        Assert.AreEqual(new CornerRadius(8), look[LookKeys.WindowRadius], $"{theme}: Windows 11's own window radius");
        Assert.AreEqual(new Thickness(1), edge.Line.BorderThickness, "a hairline");
        Assert.IsFalse(edge.Line.IsHitTestVisible, "presses reach the content beneath it");
        var ground = ((ISolidColorBrush)look[LookKeys.Surface]!).Color;
        var beside = dark ? Colors.Black : Colors.White;
        Assert.IsGreaterThanOrEqualTo(1.5, Take.Contrast(line, ground), $"{theme}: seen on the window's own ground");
        Assert.IsGreaterThanOrEqualTo(1.5, Take.Contrast(line, beside), $"{theme}: seen beside an app of the same lightness, where the ground alone shows no edge");
    });

    [TestMethod]
    public void TheEdgeTakesNoRoomFromTheContentItLiesOver() => ControlThread.Run(() =>
    {
        var edge = new FlyoutEdge((_, _) => { });
        var content = new Border { HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
        var window = new Grid();
        window.Children.Add(content);
        window.Children.Add(edge.Layer);

        window.Measure(new Size(458, 508));
        window.Arrange(new Rect(0, 0, 458, 508));

        Assert.AreEqual(new Rect(0, 0, 458, 508), content.Bounds, "the content keeps the whole window, the minimum's every unit (L2, G20)");
        Assert.AreEqual(new Rect(0, 0, 458, 508), edge.Line.Bounds, "the line lies over the window's own edge");
    });

    [TestMethod]
    public void EverySideAndCornerHasAGripAlongItsOwnEdge() => ControlThread.Run(() =>
    {
        var edge = new FlyoutEdge((_, _) => { });
        var window = new Grid();
        window.Children.Add(edge.Layer);
        window.Measure(new Size(460, 640));
        window.Arrange(new Rect(0, 0, 460, 640));

        var grips = edge.Grips.ToList();
        CollectionAssert.AreEquivalent(Enum.GetValues<WindowEdge>(), grips.Select(g => g.Edge).ToList(), "one grip for each side and corner");
        foreach (var (at, grip) in grips)
        {
            var expected = at switch
            {
                WindowEdge.West => new Rect(0, 0, FlyoutEdge.Grip, 640),
                WindowEdge.East => new Rect(460 - FlyoutEdge.Grip, 0, FlyoutEdge.Grip, 640),
                WindowEdge.North => new Rect(0, 0, 460, FlyoutEdge.Grip),
                WindowEdge.South => new Rect(0, 640 - FlyoutEdge.Grip, 460, FlyoutEdge.Grip),
                WindowEdge.NorthWest => new Rect(0, 0, FlyoutEdge.CornerGrip, FlyoutEdge.CornerGrip),
                WindowEdge.NorthEast => new Rect(460 - FlyoutEdge.CornerGrip, 0, FlyoutEdge.CornerGrip, FlyoutEdge.CornerGrip),
                WindowEdge.SouthWest => new Rect(0, 640 - FlyoutEdge.CornerGrip, FlyoutEdge.CornerGrip, FlyoutEdge.CornerGrip),
                _ => new Rect(460 - FlyoutEdge.CornerGrip, 640 - FlyoutEdge.CornerGrip, FlyoutEdge.CornerGrip, FlyoutEdge.CornerGrip),
            };
            Assert.AreEqual(expected, grip.Bounds, $"{at}: along its own edge");
            Assert.IsNotNull(grip.Background, $"{at}: hit where nothing is drawn");
            Assert.IsFalse(grip.Focusable, $"{at}: a press leaves focus where it was (G2)");
        }

        // The corners lie over the sides, so a press where they meet resizes both ways.
        var order = edge.Layer.Children.ToList();
        var corners = grips.Where(g => g.Edge is WindowEdge.NorthWest or WindowEdge.NorthEast or WindowEdge.SouthWest or WindowEdge.SouthEast).Select(g => order.IndexOf(g.Grip));
        var sides = grips.Where(g => g.Edge is WindowEdge.West or WindowEdge.East or WindowEdge.North or WindowEdge.South).Select(g => order.IndexOf(g.Grip));
        Assert.IsGreaterThan(sides.Max(), corners.Min());

        edge.Shown = false;
        Assert.IsFalse(edge.Layer.IsVisible, "off the pinned window, grips and all");
    });
}
