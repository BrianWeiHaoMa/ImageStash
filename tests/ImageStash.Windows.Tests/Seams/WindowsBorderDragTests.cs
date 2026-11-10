using System.Runtime.InteropServices;
using Avalonia;
using ImageStash.Windows.Seams;
using ImageStash.Windows.Tests.Support;

namespace ImageStash.Windows.Tests.Seams;

/// <summary>
/// A drag of the border stops at the minimum with the opposite edge where it is (G20, G21): the arithmetic at a scaling
/// other than 1, which this seam's own window on one display cannot show, and the answer to the message a real window
/// gets. The real drag is in WindowChromeTests.
/// </summary>
[TestClass]
public sealed class WindowsBorderDragTests
{
    private const int Left = 1, Right = 2, Top = 3, TopLeft = 4, TopRight = 5, Bottom = 6, BottomLeft = 7, BottomRight = 8;

    private static WindowsBorderDrag.Rectangle Rectangle(int left, int top, int right, int bottom) => new() { Left = left, Top = top, Right = right, Bottom = bottom };

    private static (int, int, int, int) Sides(WindowsBorderDrag.Rectangle rectangle) => (rectangle.Left, rectangle.Top, rectangle.Right, rectangle.Bottom);

    [TestMethod]
    public void TheLeastIsTheClientsMinimumRoundedUpPlusTheFrameItHasNowAtAnyScaling()
    {
        Assert.AreEqual(new PixelSize(556, 539), WindowsBorderDrag.Least(new Size(540, 508), 1, new PixelSize(16, 31)));
        Assert.AreEqual(new PixelSize(834, 809), WindowsBorderDrag.Least(new Size(540, 508), 1.5, new PixelSize(24, 47)), "the frame of a display at 150 %, not the system's");
        Assert.AreEqual(new PixelSize(675 + 16, 636 + 31), WindowsBorderDrag.Least(new Size(540, 508.4), 1.25, new PixelSize(16, 31)), "up, so the client is never short and the layout grows nothing back");
    }

    [TestMethod]
    [DataRow(Left, 544, 200, 1100, 700)]
    [DataRow(Right, 300, 200, 856, 700)]
    [DataRow(Top, 300, 161, 1100, 700)]
    [DataRow(Bottom, 300, 200, 1100, 739)]
    [DataRow(TopLeft, 544, 161, 1100, 700)]
    [DataRow(BottomRight, 300, 200, 856, 739)]
    public void AskedSmallerThanTheLeastTheDraggedEdgeStopsAndTheOppositeOneStays(int edge, int left, int top, int right, int bottom)
    {
        var asked = (edge is Left or TopLeft ? 900 : 300, edge is Top or TopLeft ? 600 : 200, edge is Right or BottomRight ? 400 : 1100, edge is Bottom or BottomRight ? 300 : 700);
        var stopped = WindowsBorderDrag.Stopped(Rectangle(asked.Item1, asked.Item2, asked.Item3, asked.Item4), edge, new PixelSize(556, 539));

        Assert.AreEqual((left, top, right, bottom), Sides(stopped));
    }

    [TestMethod]
    public void AskedNoSmallerThanTheLeastTheDragGoesAsAsked()
    {
        var asked = Rectangle(100, 100, 656, 639);

        Assert.AreEqual(Sides(asked), Sides(WindowsBorderDrag.Stopped(asked, TopLeft, new PixelSize(556, 539))));
    }

    [TestMethod]
    [DataRow(1.0)]
    [DataRow(1.25)]
    [DataRow(1.5)]
    [DataRow(1.75)]
    [DataRow(2.0)]
    [DataRow(2.25)]
    public void EveryClientLengthDraggedToIsOneAvaloniaKeepsTheDraggedSideGivingAPixelAndTheOppositeOneStaying(double scaling)
    {
        var frame = new PixelSize(24, 64);
        var given = 0;
        foreach (var length in Enumerable.Range(400, 1100))
        {
            foreach (var edge in new[] { Left, Right, Top, Bottom, TopLeft, TopRight, BottomLeft, BottomRight })
            {
                var (leftward, upward) = (edge is Left or TopLeft or BottomLeft, edge is Top or TopLeft or TopRight);
                var (across, down) = (edge is not (Top or Bottom), edge is not (Left or Right));
                var asked = Rectangle(100, 100, 100 + frame.Width + (across ? length : 500), 100 + frame.Height + (down ? length : 400));

                var kept = WindowsBorderDrag.Kept(asked, edge, frame, scaling);

                var (width, height) = (kept.Right - kept.Left - frame.Width, kept.Bottom - kept.Top - frame.Height);
                Assert.AreEqual(width, (int)(width / scaling * scaling), $"a client {width} wide, which Avalonia lays out at {width / scaling} and keeps");
                Assert.AreEqual(height, (int)(height / scaling * scaling), $"a client {height} high, kept likewise");
                var grown = kept.Right - asked.Right + asked.Left - kept.Left + kept.Bottom - asked.Bottom + asked.Top - kept.Top;
                Assert.IsLessThanOrEqualTo(across && down ? 2 : 1, grown, $"a pixel at most on each dragged side, asked {length} at {scaling}");
                Assert.AreEqual(leftward ? asked.Right : asked.Left, leftward ? kept.Right : kept.Left, $"edge {edge}: the side opposite across stays");
                Assert.AreEqual(upward ? asked.Bottom : asked.Top, upward ? kept.Bottom : kept.Top, $"edge {edge}: the side opposite down stays");
                given += grown;
            }
        }

        if (scaling == 1.75)
        {
            Assert.IsGreaterThan(0, given, "at 175 % the sweep meets lengths Avalonia lays a pixel short (976 among them)");
        }
    }

    [TestMethod]
    public void TheWindowsOwnAnswerToTheMessageHoldsItsMinimumPlusItsFrame() => TrayDesktop.Run(() =>
    {
        var window = new Avalonia.Controls.Window { Width = 500, Height = 400, MinWidth = 400, MinHeight = 300, ShowActivated = false, Position = new PixelPoint(-8000, 100) };
        new WindowsBorderDrag().StopAtMinimum(window);
        window.Show();
        try
        {
            var handle = window.TryGetPlatformHandle()!.Handle;
            Assert.IsTrue(GetWindowRect(handle, out var outer));
            Assert.IsTrue(GetClientRect(handle, out var client));
            var frame = new PixelSize(outer.Right - outer.Left - client.Right, outer.Bottom - outer.Top - client.Bottom);
            var least = WindowsBorderDrag.Least(new Size(400, 300), window.RenderScaling, frame);
            var asked = Rectangle(outer.Right - 10, outer.Top, outer.Right, outer.Bottom);

            var answered = SendMessage(handle, WindowsBorderDrag.WmSizing, Left, ref asked);

            Assert.AreEqual(1, answered, "handled, as WM_SIZING's answer says");
            Assert.AreEqual((outer.Right - least.Width, outer.Top, outer.Right, outer.Bottom), Sides(asked));
        }
        finally
        {
            window.Close();
        }
    });

    [DllImport("user32.dll", EntryPoint = "SendMessageW", ExactSpelling = true)]
    private static extern nint SendMessage(nint window, uint message, nint wParam, ref WindowsBorderDrag.Rectangle rectangle);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, out WindowsBorderDrag.Rectangle rectangle);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(nint window, out WindowsBorderDrag.Rectangle rectangle);
}
