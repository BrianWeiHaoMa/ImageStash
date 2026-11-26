using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ImageStash.Look;
using ImageStash.Store;
using ImageStash.Window;
using ImageStash.Windows.Tests.Support;

namespace ImageStash.Windows.Tests.Window;

/// <summary>
/// The flyout's frame through the real window (F10, G1, G2, G3): its edge drawn all the way round in either take, taken
/// off by the pin and put back by the unpin; no frame of its own, so the window Windows and DWM report is the flyout a
/// person sees, placed where the summon computes; its border dragged to a size that every later flyout shows, the pinned
/// size untouched, and the reverse; a drag of each edge and corner stopping at the minimum with the opposite edges
/// unmoved and the focus where it was; put back after a capture on the rectangle it had. These show the window on the
/// primary display for a moment, and the drags move the real pointer.
/// </summary>
[TestClass]
public sealed class FlyoutFrameTests
{
    [TestMethod]
    [DataRow("Light")]
    [DataRow("Dark")]
    public async Task TheFlyoutHoldsItsEdgeInEitherTakeThePinTakesItOffAndTheUnpinPutsItBack(string theme)
    {
        using var shown = ShownWindow.Open();
        try
        {
            TrayDesktop.Run(() =>
            {
                Application.Current!.RequestedThemeVariant = theme == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
                shown.Window.Hide();
                shown.Window.SummonFromShortcut(default, new SummonAnchor(null));
            });
            await Task.Delay(300);
            TrayDesktop.Run(() =>
            {
                var window = shown.Window;
                var take = Looks.All[0].Resources(theme == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light, Colors.Black);
                Assert.IsTrue(Edge(shown).IsEffectivelyVisible, $"{theme}: the flyout holds its edge");
                Assert.AreEqual(((ISolidColorBrush)take[LookKeys.WindowEdge]!).Color, ((ISolidColorBrush)Line(shown).BorderBrush!).Color, $"{theme}: in the take's own edge colour (F10)");
                Assert.AreEqual(take[LookKeys.WindowRadius], Line(shown).CornerRadius, "its corners rounded as the window's");
                Assert.AreEqual(new Rect(window.ClientSize), Line(shown).Bounds, "all the way round the window's own edge");
                Assert.AreEqual(window.ClientSize, window.FrameSize!.Value, "no frame of its own, so nothing unseen counts as the window (G1)");
                CollectionAssert.Contains(shown.QuietWindows.Rounded, window, "its corners asked of the platform, as Windows 11 rounds its own (F13)");
            });

            TrayDesktop.Run(() => shown.Click(Pin(shown)));
            await Task.Delay(300);
            TrayDesktop.Run(() =>
            {
                Assert.IsTrue(shown.Window.ShowInTaskbar, "pinned");
                Assert.IsFalse(Edge(shown).IsEffectivelyVisible, "the pinned window's frame is the system's, with no edge of the flyout's inside it");
            });

            TrayDesktop.Run(() => shown.Click(Pin(shown)));
            await Task.Delay(300);
            TrayDesktop.Run(() =>
            {
                Assert.IsFalse(shown.Window.ShowInTaskbar, "unpinned");
                Assert.IsTrue(Edge(shown).IsEffectivelyVisible, "the flyout's edge back");
            });
        }
        finally
        {
            TrayDesktop.Run(() => Application.Current!.RequestedThemeVariant = ThemeVariant.Default);
        }
    }

    [TestMethod]
    public async Task TheFlyoutSeenIsWhereTheSummonPutsItBesideACaretAndAboveTheTray()
    {
        using var shown = ShownWindow.Open();
        var display = DisplayPlaces.Primary();
        var caret = new PixelRect(display.WorkingArea.X + DisplayPlaces.Scaled(display, 600), display.WorkingArea.Bottom - DisplayPlaces.Scaled(display, 60), 2, DisplayPlaces.Scaled(display, 18));
        TrayDesktop.Run(() =>
        {
            shown.Window.Hide();
            shown.Window.SummonFromShortcut(default, new SummonAnchor(null, Caret: caret));
        });
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        await Task.Delay(300);
        TrayDesktop.Run(() =>
        {
            var expected = Summon.AtAnchor(new SummonAnchor(null, Caret: caret), Summon.DisplaysOf(shown.Window.Screens), WindowSize.FlyoutDefault);
            AssertSeenAt(expected, shown.Window, "beside the caret, its bottom the caret's gap above the caret's top (G1)");
            Assert.AreEqual(caret.Y - (int)Math.Round(Summon.CaretGap * display.Scaling), Seen(shown.Window).Bottom, "the gap measured to the edge a person sees");
        });

        TrayDesktop.Run(() =>
        {
            shown.Window.Hide();
            shown.Window.SummonFromTray();
        });
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        await Task.Delay(300);
        TrayDesktop.Run(() =>
        {
            // The checks' stand-in tray gives no icon rectangle, so the flyout takes the primary display's centre.
            var expected = Summon.AboveTrayIcon(null, Summon.DisplaysOf(shown.Window.Screens), WindowSize.FlyoutDefault);
            AssertSeenAt(expected, shown.Window, "from the tray");
            Assert.IsTrue(display.WorkingArea.Contains(Seen(shown.Window)), "wholly inside the working area");
        });
    }

    [TestMethod]
    public async Task AFlyoutDragIsTheSizeOfEveryLaterFlyoutAndAPinnedDragThePinnedSizeEachLeavingTheOther()
    {
        using var shown = ShownWindow.Open();
        var display = DisplayPlaces.Primary();
        TrayDesktop.Run(shown.Window.SummonFromTray);
        await Task.Delay(300);
        var handle = TrayDesktop.Get(() => shown.Window.TryGetPlatformHandle()!.Handle);
        var dragged = DisplayPlaces.Rect(display, 200, 60, 620, 600);

        // As a drag of the flyout's border reaches the window: a resize that is not the window's own placement.
        TrayDesktop.Run(() => Assert.IsTrue(SetWindowPos(handle, 0, dragged.X, dragged.Y, dragged.Width, dragged.Height, NoZOrderNoActivate)));
        await Task.Delay(300);
        var flyout = TrayDesktop.Get(() =>
        {
            var settings = shown.Settings.Current;
            Assert.AreEqual(new WindowSize(shown.Window.FrameSize!.Value.Width, shown.Window.FrameSize.Value.Height), settings.FlyoutSize, "the flyout's size is remembered (G2)");
            Assert.AreEqual(WindowSize.PinnedDefault, settings.PinnedSize, "and the pinned size is not");
            Assert.IsNull(settings.PinnedPosition, "nor the pinned position");
            return settings.FlyoutSize;
        });

        TrayDesktop.Run(() =>
        {
            shown.Window.Hide();
            shown.Window.SummonFromShortcut(default, new SummonAnchor(null));
        });
        await Task.Delay(300);
        TrayDesktop.Run(() => AssertSize(dragged.Size, Seen(shown.Window).Size, "the shortcut's flyout at the size dragged"));

        TrayDesktop.Run(() => shown.Click(Pin(shown)));
        await Task.Delay(300);
        TrayDesktop.Run(() =>
        {
            // The pinned window's frame keeps borders unseen, so it is measured as the whole window Windows reports.
            AssertSize(Summon.Pinned(null, Summon.DisplaysOf(shown.Window.Screens), WindowSize.PinnedDefault).Size, CaptureDesk.Outer(shown.Window).Size, "pinned: the pinned size, not the flyout's");
            Assert.AreEqual(flyout, shown.Settings.Current.FlyoutSize, "the pin's own placement leaves the flyout's size");
        });

        var pinned = DisplayPlaces.Rect(display, 120, 50, 800, 620);
        TrayDesktop.Run(() => Assert.IsTrue(SetWindowPos(handle, 0, pinned.X, pinned.Y, pinned.Width, pinned.Height, NoZOrderNoActivate)));
        await Task.Delay(300);
        TrayDesktop.Run(() =>
        {
            Assert.AreEqual(new WindowSize(shown.Window.FrameSize!.Value.Width, shown.Window.FrameSize.Value.Height), shown.Settings.Current.PinnedSize, "a pinned drag is the pinned size");
            Assert.AreEqual(flyout, shown.Settings.Current.FlyoutSize, "and leaves the flyout's");
        });

        TrayDesktop.Run(() => shown.Click(Pin(shown)));
        await Task.Delay(300);
        TrayDesktop.Run(() =>
        {
            Assert.IsFalse(shown.Window.ShowInTaskbar, "unpinned");
            AssertSize(dragged.Size, Seen(shown.Window).Size, "the unpin's flyout at the flyout's size");
        });
    }

    [TestMethod]
    public async Task AShowingNarrowedBesideACaretAndHeldToTheDisplayLeavesTheRememberedSize()
    {
        using var shown = ShownWindow.Open();
        var display = DisplayPlaces.Primary();
        var area = display.WorkingArea;
        // Wider than either side of the caret has room for, and taller than the working area.
        var remembered = new WindowSize(area.Width / display.Scaling, area.Height / display.Scaling + 200);
        var caret = new PixelRect(area.X + area.Width / 2, area.Y + area.Height / 3, 2, DisplayPlaces.Scaled(display, 18));
        TrayDesktop.Run(() =>
        {
            shown.Settings.Change(s => s with { FlyoutSize = remembered });
            shown.Window.Hide();
            shown.Window.SummonFromShortcut(default, new SummonAnchor(null, Caret: caret));
        });
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        await Task.Delay(300);

        TrayDesktop.Run(() =>
        {
            var seen = Seen(shown.Window);
            Assert.IsLessThan(area.Width, seen.Width, $"narrowed beside the caret for this showing: {seen}");
            Assert.IsTrue(area.Contains(seen), $"held inside the working area: {seen}");
            Assert.AreEqual(remembered, shown.Settings.Current.FlyoutSize, "the size the window gave itself is not the person's: the remembered size stays (G2)");
        });
    }

    [TestMethod]
    public async Task AResizedFlyoutIsPutBackAfterACaptureOnTheRectangleItHad()
    {
        using var shown = ShownWindow.Open();
        var display = DisplayPlaces.Primary();
        TrayDesktop.Run(() =>
        {
            shown.Window.ShowActivated = true;
            shown.Window.Hide();
            shown.Window.SummonFromShortcut(default, new SummonAnchor(display.WorkingArea.Position + new PixelPoint(DisplayPlaces.Scaled(display, 80), DisplayPlaces.Scaled(display, 40))));
        });
        // In front, so the capture's hide is one it puts back, not a loss of focus (G3).
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Window.IsActive));
        var handle = TrayDesktop.Get(() => shown.Window.TryGetPlatformHandle()!.Handle);
        var dragged = DisplayPlaces.Rect(display, 300, 70, 640, 610);
        TrayDesktop.Run(() => Assert.IsTrue(SetWindowPos(handle, 0, dragged.X, dragged.Y, dragged.Width, dragged.Height, NoZOrderNoActivate)));
        await Task.Delay(300);
        var before = TrayDesktop.Get(() => Seen(shown.Window));

        TrayDesktop.Run(() => shown.Window.CaptureHiding.HideForCapture());
        TrayDesktop.Run(() => Assert.IsFalse(shown.Window.IsVisible, "hidden for the capture (G3)"));
        TrayDesktop.Run(() => shown.Window.CaptureHiding.ShowAfterCapture());
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        await Task.Delay(300);

        TrayDesktop.Run(() =>
        {
            Assert.IsTrue(shown.Window.IsVisible, "put back");
            Assert.AreEqual(before, Seen(shown.Window), "on the rectangle it had, neither shifted nor grown by a frame");
        });
    }

    [TestMethod]
    [DataRow("left")]
    [DataRow("top")]
    [DataRow("right")]
    [DataRow("bottom")]
    [DataRow("top-left")]
    [DataRow("top-right")]
    [DataRow("bottom-left")]
    [DataRow("bottom-right")]
    public async Task AFlyoutEdgeDraggedWithRealInputPastTheMinimumStopsThereKeepsTheFocusAndIsRemembered(string edge)
    {
        using var pointer = TestMouse.PutBackAfterwards();
        using var shown = ShownWindow.Open();
        var window = shown.Window;
        TrayDesktop.Run(() =>
        {
            window.ShowActivated = true;
            window.Hide();
            window.SummonFromShortcut(default, new SummonAnchor(null));
        });
        await Task.Delay(300);
        var handle = TrayDesktop.Get(() => window.TryGetPlatformHandle()!.Handle);
        var place = DisplayPlaces.Rect(DisplayPlaces.Primary(), 100, 40, 700, 640);
        TrayDesktop.Run(() => Assert.IsTrue(SetWindowPos(handle, 0, place.X, place.Y, place.Width, place.Height, NoZOrderNoActivate)));
        await Task.Delay(300);
        TrayDesktop.Run(() => Assert.IsTrue(shown.Search.IsFocused, "the search box focused before the drag"));
        var start = TrayDesktop.Get(() => CaptureDesk.Outer(window));
        var scaling = TrayDesktop.Get(() => window.RenderScaling);
        var (dx, dy) = edge switch
        {
            "left" => (1, 0),
            "right" => (-1, 0),
            "top" => (0, 1),
            "bottom" => (0, -1),
            "top-left" => (1, 1),
            "top-right" => (-1, 1),
            "bottom-left" => (1, -1),
            _ => (-1, -1),
        };
        // Inside the edge's grip, which lies over the client's outermost units.
        var from = new PixelPoint(
            dx > 0 ? start.X + 2 : dx < 0 ? start.Right - 3 : start.Center.X,
            dy > 0 ? start.Y + 2 : dy < 0 ? start.Bottom - 3 : start.Center.Y);
        var past = (int)(Math.Max(dx == 0 ? 0 : start.Width - WindowSize.Minimum.Width * scaling, dy == 0 ? 0 : start.Height - WindowSize.Minimum.Height * scaling) + 200);
        PixelPoint Moved(int by) => new(from.X + dx * by, from.Y + dy * by);
        void OppositeEdgesStay(string when)
        {
            var now = TrayDesktop.Get(() => CaptureDesk.Outer(window));
            if (dx > 0) Assert.AreEqual(start.Right, now.Right, $"{when}: the right edge");
            if (dx < 0) Assert.AreEqual(start.X, now.X, $"{when}: the left edge");
            if (dy > 0) Assert.AreEqual(start.Bottom, now.Bottom, $"{when}: the bottom edge");
            if (dy < 0) Assert.AreEqual(start.Y, now.Y, $"{when}: the top edge");
        }

        TestMouse.MoveTo(from);
        await Task.Delay(200);
        Assert.AreEqual(handle, FrontWindow.Under(from), $"the edge at {from} is the flyout's own: \"{FrontWindow.NativeText(FrontWindow.Under(from))}\" stands there");
        TestMouse.Press(from);
        for (var by = 20; by <= past; by += 20)
        {
            TestMouse.MoveTo(Moved(by));
            await Task.Delay(30);
            OppositeEdgesStay($"{by} px into the drag");
        }

        TestMouse.Release(Moved(past));
        await Task.Delay(300);

        OppositeEdgesStay("after the release");
        TrayDesktop.Run(() =>
        {
            if (dx != 0) Assert.AreEqual(WindowSize.Minimum.Width, window.ClientSize.Width, 1, "it stops at the minimum width (G20, G21)");
            if (dy != 0) Assert.AreEqual(WindowSize.Minimum.Height, window.ClientSize.Height, 1, "it stops at the minimum height");
            Assert.AreEqual(new WindowSize(window.FrameSize!.Value.Width, window.FrameSize.Value.Height), shown.Settings.Current.FlyoutSize, "remembered as the flyout's size (G2)");
            Assert.AreEqual(WindowSize.PinnedDefault, shown.Settings.Current.PinnedSize, "the pinned size untouched");
            Assert.IsTrue(window.IsVisible, "a drag of the border is no leaving: the flyout still shows (G2)");
            Assert.IsTrue(window.IsActive, "still the window in front");
            Assert.IsTrue(shown.Search.IsFocused, "with the search box focused, so typing still reaches it");
            Assert.IsTrue(window.Topmost, "above other windows");
            Assert.IsFalse(window.ShowInTaskbar, "without a taskbar button");
            Assert.IsTrue(window.Screens.Primary!.WorkingArea.Contains(CaptureDesk.Outer(window)), "wholly on screen (G1)");
        });
    }

    /// <summary>The flyout's edge layer, by its name among the window's controls.</summary>
    private static Panel Edge(ShownWindow shown) => shown.Window.GetVisualDescendants().OfType<Panel>().Single(panel => panel.Name == FlyoutEdge.LayerName);

    private static Border Line(ShownWindow shown) => Edge(shown).Children.OfType<Border>().First(border => !border.IsHitTestVisible);

    /// <summary>The pin button, by the words it is named with in either state.</summary>
    private static ToggleButton Pin(ShownWindow shown) =>
        shown.Window.GetVisualDescendants().OfType<ToggleButton>().Single(button => AutomationProperties.GetName(button) is "Keep open" or "Unpin");

    /// <summary>The window as DWM draws it, without any border Windows keeps unseen (DWMWA_EXTENDED_FRAME_BOUNDS).</summary>
    private static PixelRect Seen(Avalonia.Controls.Window window)
    {
        var handle = window.TryGetPlatformHandle()!.Handle;
        Assert.AreEqual(0, DwmGetWindowAttribute(handle, 9, out var seen, Marshal.SizeOf<NativeRect>()), "DWM reports the frame it draws");
        return new PixelRect(seen.Left, seen.Top, seen.Right - seen.Left, seen.Bottom - seen.Top);
    }

    /// <summary>The window Windows reports and the window DWM draws are both <paramref name="expected"/>.</summary>
    private static void AssertSeenAt(PixelRect expected, Avalonia.Controls.Window window, string because)
    {
        Assert.IsTrue(GetWindowRect(window.TryGetPlatformHandle()!.Handle, out var reported));
        Assert.AreEqual(expected, new PixelRect(reported.Left, reported.Top, reported.Right - reported.Left, reported.Bottom - reported.Top), $"{because}: the window Windows reports");
        Assert.AreEqual(expected, Seen(window), $"{because}: the flyout a person sees");
    }

    /// <summary><paramref name="seen"/> is <paramref name="expected"/>, within a physical pixel.</summary>
    private static void AssertSize(PixelSize expected, PixelSize seen, string because)
    {
        Assert.IsLessThanOrEqualTo(1, Math.Abs(seen.Width - expected.Width), $"{because}: width {seen.Width}, not {expected.Width}");
        Assert.IsLessThanOrEqualTo(1, Math.Abs(seen.Height - expected.Height), $"{because}: height {seen.Height}, not {expected.Height}");
    }

    private const uint NoZOrderNoActivate = 0x0004 | 0x0010;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmGetWindowAttribute(nint window, int attribute, out NativeRect value, int size);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, out NativeRect rectangle);

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint window, nint insertAfter, int x, int y, int width, int height, uint flags);
}
