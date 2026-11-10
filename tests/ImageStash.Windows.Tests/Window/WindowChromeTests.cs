using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ImageStash.Seams;
using ImageStash.Store;
using ImageStash.Window;
using ImageStash.Windows.Tests.Support;

namespace ImageStash.Windows.Tests.Window;

/// <summary>
/// The stash window's two frames through the real window (G2, G1, G8): the pin button turns the flyout into the pinned
/// window, with a title bar, resizing and a taskbar button, wholly at the pinned window's remembered place and size, and
/// back into a flyout where the window is, at the flyout's size; a resize and a move of the pinned window that are not
/// the window's own placements are remembered, while the flyout's showings change neither; a drag below the window's
/// minimum stops there and a size remembered below it shows at it, wholly on screen, the stored size kept; a pin and an
/// unpin leave the paste target as it was; Close unpins and hides. These show the window on the primary display for a
/// moment.
/// </summary>
[TestClass]
public sealed class WindowChromeTests
{
    private static readonly PasteTarget Relay = PresetTarget.App("Relay");
    private static readonly PasteTarget Other = PresetTarget.App("Other");

    [TestMethod]
    public async Task ThePinButtonTurnsTheFlyoutIntoThePinnedWindowAndBackEachStillShowingWithTheSearchFocused()
    {
        using var shown = ShownWindow.Open();
        TrayDesktop.Run(() =>
        {
            shown.Window.Hide();
            shown.Window.SummonFromShortcut(default, new SummonAnchor(null));
        });
        await Task.Delay(300);

        TrayDesktop.Run(() => shown.Click(Pin(shown)));
        await Task.Delay(300);

        var pinnedAt = TrayDesktop.Get(() =>
        {
            var window = shown.Window;
            var expected = Summon.Pinned(null, Summon.DisplaysOf(window.Screens), WindowSize.PinnedDefault);
            Assert.AreEqual(WindowDecorations.Full, window.WindowDecorations, "a title bar (G2)");
            Assert.IsTrue(window.ShowInTaskbar, "a taskbar button");
            Assert.IsTrue(window.CanResize, "resizing");
            Assert.IsFalse(window.Topmost, "a normal window");
            Assert.AreEqual("Unpin", ToolTip.GetTip(Pin(shown)));
            Assert.AreEqual(expected.Position, window.Position, "centred on the primary display, never placed by the user");
            AssertFrame(expected.Size, window, "the pinned default size, its title bar included");
            Assert.IsTrue(window.IsVisible, "still showing");
            Assert.IsTrue(shown.Search.IsFocused, "the search box keeps focus");
            Assert.IsNull(shown.Settings.Current.PinnedPosition, "the pin's own placement is not the user's move");
            Assert.AreEqual(WindowSize.PinnedDefault, shown.Settings.Current.PinnedSize, "nor the user's resize");
            return window.Position;
        });

        TrayDesktop.Run(() => shown.Click(Pin(shown)));
        await Task.Delay(300);

        TrayDesktop.Run(() =>
        {
            var window = shown.Window;
            var expected = Summon.Unpinned(pinnedAt, Summon.DisplaysOf(window.Screens), WindowSize.FlyoutDefault);
            Assert.AreEqual(WindowDecorations.BorderOnly, window.WindowDecorations, "no title bar");
            Assert.IsFalse(window.ShowInTaskbar, "no taskbar button");
            Assert.IsFalse(window.CanResize);
            Assert.IsTrue(window.Topmost, "above other windows, as the flyout");
            Assert.AreEqual("Keep open", ToolTip.GetTip(Pin(shown)));
            Assert.AreEqual(expected.Position, window.Position, "a flyout where the pinned window was");
            AssertFrame(expected.Size, window, "the flyout's size");
            Assert.IsTrue(window.IsVisible, "still showing");
            Assert.IsTrue(shown.Search.IsFocused, "with the search box focused");
            Assert.IsNull(shown.Settings.Current.PinnedPosition, "the unpin's placements are not the user's");
            Assert.AreEqual(WindowSize.PinnedDefault, shown.Settings.Current.PinnedSize);
        });
    }

    [TestMethod]
    public async Task AResizeAndAMoveOfThePinnedWindowAreRememberedAndTheFlyoutsShowingChangesNeither()
    {
        using var shown = ShownWindow.Open();
        TrayDesktop.Run(shown.Window.SummonPinned);
        await Task.Delay(300);
        var area = TrayDesktop.Get(() => shown.Window.Screens.Primary!.WorkingArea);
        var primary = DisplayPlaces.Primary();
        var moved = new PixelRect(area.X + DisplayPlaces.Scaled(primary, 50), area.Y + DisplayPlaces.Scaled(primary, 40),
            Math.Min(DisplayPlaces.Scaled(primary, 760), area.Width - DisplayPlaces.Scaled(primary, 100)), Math.Min(DisplayPlaces.Scaled(primary, 600), area.Height - DisplayPlaces.Scaled(primary, 80)));

        // As a drag of the border or a snap reaches the window: a placement that is not the window's own.
        TrayDesktop.Run(() => Assert.IsTrue(SetWindowPos(shown.Window.TryGetPlatformHandle()!.Handle, 0, moved.X, moved.Y, moved.Width, moved.Height, NoZOrderNoActivate)));
        await Task.Delay(300);

        var remembered = TrayDesktop.Get(() =>
        {
            var window = shown.Window;
            var settings = shown.Settings.Current;
            Assert.AreEqual(moved.Position, window.Position, "the window moved");
            Assert.AreEqual(new WindowPosition(moved.X, moved.Y), settings.PinnedPosition, "the move is remembered (G2)");
            Assert.AreEqual(new WindowSize(window.FrameSize!.Value.Width, window.FrameSize.Value.Height), settings.PinnedSize, "the resize is remembered as the whole window, its frame included");
            Assert.AreNotEqual(WindowSize.PinnedDefault, settings.PinnedSize, "the window was resized");
            return settings;
        });

        TrayDesktop.Run(() =>
        {
            shown.Window.Hide();
            shown.Window.SummonFromTray();
        });
        await Task.Delay(300);
        TrayDesktop.Run(() =>
        {
            Assert.IsFalse(shown.Window.ShowInTaskbar, "the flyout");
            Assert.AreEqual(remembered.PinnedSize, shown.Settings.Current.PinnedSize, "the flyout's showing is not the pinned size (G2: remembered separately)");
            Assert.AreEqual(remembered.PinnedPosition, shown.Settings.Current.PinnedPosition, "nor the pinned position");
        });

        TrayDesktop.Run(() => shown.Click(Pin(shown)));
        await Task.Delay(300);

        TrayDesktop.Run(() =>
        {
            Assert.IsTrue(shown.Window.ShowInTaskbar, "pinned again");
            Assert.AreEqual(moved.Position, shown.Window.Position, "where the user left it");
            AssertFrame(new PixelSize(moved.Width, moved.Height), shown.Window, "at the size the user gave it");
            Assert.AreEqual(remembered.PinnedSize, shown.Settings.Current.PinnedSize, "the window's own placement is not the user's");
            Assert.AreEqual(remembered.PinnedPosition, shown.Settings.Current.PinnedPosition);
        });
    }

    [TestMethod]
    public async Task ThePinButtonShowsThePinnedWindowWhollyAtTheRememberedSizeAndPlace()
    {
        using var shown = ShownWindow.Open();
        var area = TrayDesktop.Get(() => shown.Window.Screens.Primary!.WorkingArea);
        var scaling = TrayDesktop.Get(() => shown.Window.Screens.Primary!.Scaling);
        // Larger than the minimum by more than the title bar and border, so the remembered size is the one shown.
        var size = new WindowSize(640, 600);
        TrayDesktop.Run(() =>
        {
            shown.Settings.Change(s => s with { PinnedSize = size, PinnedPosition = new WindowPosition(area.X + 30, area.Y + 20) });
            shown.Window.Hide();
            shown.Window.SummonFromShortcut(default, new SummonAnchor(null));
        });
        await Task.Delay(300);

        TrayDesktop.Run(() => shown.Click(Pin(shown)));
        await Task.Delay(300);

        TrayDesktop.Run(() =>
        {
            Assert.AreEqual(new PixelPoint(area.X + 30, area.Y + 20), shown.Window.Position, "at the remembered place");
            AssertFrame(new PixelSize((int)Math.Round(size.Width * scaling), (int)Math.Round(size.Height * scaling)), shown.Window,
                "the whole window, its title bar included, at the size remembered for it, not the flyout's (G1, G2)");
        });
    }

    [TestMethod]
    public async Task ADragOfThePinnedBorderBelowTheMinimumStopsThereAndIsRememberedAtTheSizeItGot()
    {
        using var shown = ShownWindow.Open();
        TrayDesktop.Run(shown.Window.SummonPinned);
        await Task.Delay(300);
        var area = TrayDesktop.Get(() => shown.Window.Screens.Primary!.WorkingArea);

        // As a drag of the border or a snap reaches the window, asking it 300 × 300.
        TrayDesktop.Run(() => Assert.IsTrue(SetWindowPos(shown.Window.TryGetPlatformHandle()!.Handle, 0, area.X + 100, area.Y + 100, 300, 300, NoZOrderNoActivate)));
        await Task.Delay(300);

        TrayDesktop.Run(() =>
        {
            var window = shown.Window;
            WindowMinimum.AssertAt(window.ClientSize, "the drag stops at the minimum (G20, G21)");
            Assert.AreEqual(new PixelPoint(area.X + 100, area.Y + 100), window.Position, "where the drag put it");
            // Within a display pixel: where the minimum is no whole number of them (458 × 1.75 = 801.5), Windows stops the
            // drag on the pixel below it, which is remembered, and the layout then lays the window on the pixel above.
            var (got, remembered, pixel) = (window.FrameSize!.Value, shown.Settings.Current.PinnedSize, 1 / window.RenderScaling + 1e-9);
            Assert.AreEqual(got.Width, remembered.Width, pixel, "remembered at the width it got, frame included, not the width asked (G2)");
            Assert.AreEqual(got.Height, remembered.Height, pixel, "and at the height it got");
            Assert.IsTrue(window.CanResize, "still resizable");
        });
    }

    [TestMethod]
    [DataRow("left")]
    [DataRow("top")]
    [DataRow("right")]
    [DataRow("bottom")]
    public async Task AnEdgeDraggedWithRealInputPastTheMinimumStopsThereAndTheOppositeEdgeNeverMoves(string edge)
    {
        using var pointer = TestMouse.PutBackAfterwards();
        using var shown = ShownWindow.Open();
        var window = shown.Window;
        TrayDesktop.Run(() =>
        {
            window.ShowActivated = true;
            window.SummonPinned();
        });
        await Task.Delay(300);
        var handle = TrayDesktop.Get(() => window.TryGetPlatformHandle()!.Handle);
        var place = DisplayPlaces.Rect(DisplayPlaces.Primary(), 100, 40, 700, 640);
        TrayDesktop.Run(() => Assert.IsTrue(SetWindowPos(handle, 0, place.X, place.Y, place.Width, place.Height, NoZOrderNoActivate)));
        await Task.Delay(300);
        var start = TrayDesktop.Get(() => CaptureDesk.Outer(window));
        var scaling = TrayDesktop.Get(() => window.RenderScaling);
        var across = edge is "left" or "right";
        var from = edge switch
        {
            "left" => new PixelPoint(start.X + 3, start.Center.Y),
            "right" => new PixelPoint(start.Right - 3, start.Center.Y),
            "top" => new PixelPoint(start.Center.X, start.Y + 3),
            _ => new PixelPoint(start.Center.X, start.Bottom - 3),
        };
        var past = (int)((across ? start.Width - WindowSize.Minimum.Width * scaling : start.Height - WindowSize.Minimum.Height * scaling) + 200);
        PixelPoint Moved(int by) => edge switch
        {
            "left" => new PixelPoint(from.X + by, from.Y),
            "right" => new PixelPoint(from.X - by, from.Y),
            "top" => new PixelPoint(from.X, from.Y + by),
            _ => new PixelPoint(from.X, from.Y - by),
        };
        void OppositeEdgeStays(string when)
        {
            var now = TrayDesktop.Get(() => CaptureDesk.Outer(window));
            switch (edge)
            {
                case "left": Assert.AreEqual(start.Right, now.Right, when); break;
                case "right": Assert.AreEqual(start.X, now.X, when); break;
                case "top": Assert.AreEqual(start.Bottom, now.Bottom, when); break;
                default: Assert.AreEqual(start.Y, now.Y, when); break;
            }
        }

        // Over the edge first: Windows reads what a press is on from where the pointer has come to rest.
        TestMouse.MoveTo(from);
        await Task.Delay(200);
        Assert.AreEqual(handle, FrontWindow.Under(from), $"the edge at {from} is the window's own: \"{FrontWindow.NativeText(FrontWindow.Under(from))}\" stands there");
        TestMouse.Press(from);
        for (var by = 20; by <= past; by += 20)
        {
            TestMouse.MoveTo(Moved(by));
            await Task.Delay(30);
            OppositeEdgeStays($"{by} px into the drag");
        }

        TestMouse.Release(Moved(past));
        await Task.Delay(300);

        OppositeEdgeStays("after the release");
        TrayDesktop.Run(() =>
        {
            var size = across ? window.ClientSize.Width : window.ClientSize.Height;
            var least = across ? WindowSize.Minimum.Width : WindowSize.Minimum.Height;
            Assert.AreEqual(least, size, 1, "it stops at the minimum (G20, G21)");
            Assert.AreEqual(new WindowSize(window.FrameSize!.Value.Width, window.FrameSize.Value.Height), shown.Settings.Current.PinnedSize, "remembered at the size it got, frame included (G2)");
            Assert.IsTrue(window.Screens.Primary!.WorkingArea.Contains(CaptureDesk.Outer(window)), "wholly on screen (G1)");
        });
    }

    [TestMethod]
    public async Task MovedByTheUserOntoADisplayThatHoldsTheMinimumTheWindowTakesItThereAgain()
    {
        using var shown = ShownWindow.Open();
        TrayDesktop.Run(shown.Window.SummonPinned);
        await Task.Delay(300);
        var area = TrayDesktop.Get(() => shown.Window.Screens.Primary!.WorkingArea);
        var handle = TrayDesktop.Get(() => shown.Window.TryGetPlatformHandle()!.Handle);
        // Lowered as a placement on a display too small for the minimum lowers it; this PC has one display, which holds it.
        TrayDesktop.Run(() =>
        {
            shown.Window.MinWidth = 200;
            shown.Window.MinHeight = 150;
        });

        // A drag of the title bar onto this display, then of the border, asking 300 × 300.
        TrayDesktop.Run(() => Assert.IsTrue(SetWindowPos(handle, 0, area.X + DisplayPlaces.Scaled(DisplayPlaces.Primary(), 120), area.Y + DisplayPlaces.Scaled(DisplayPlaces.Primary(), 90), 0, 0, NoSizeNoZOrderNoActivate)));
        await Task.Delay(300);
        TrayDesktop.Run(() => Assert.IsTrue(SetWindowPos(handle, 0, area.X + DisplayPlaces.Scaled(DisplayPlaces.Primary(), 120), area.Y + DisplayPlaces.Scaled(DisplayPlaces.Primary(), 90), DisplayPlaces.Scaled(DisplayPlaces.Primary(), 300), DisplayPlaces.Scaled(DisplayPlaces.Primary(), 300), NoZOrderNoActivate)));
        await Task.Delay(300);

        TrayDesktop.Run(() => WindowMinimum.AssertAt(shown.Window.ClientSize, "the move put this display's minimum back in force, so the drag stops at it (G20, G21)"));
    }

    [TestMethod]
    public async Task SizesRememberedBelowTheMinimumShowAtItWhollyOnScreenAndStayAsStored()
    {
        using var shown = ShownWindow.Open();
        var area = TrayDesktop.Get(() => shown.Window.Screens.Primary!.WorkingArea);
        // As a settings file from an earlier build or a hand edit can hold them (G31).
        var flyout = new WindowSize(200, 200);
        var pinned = new WindowSize(300, 300);
        var pinnedAt = new WindowPosition(area.Right - 40, area.Bottom - 40);
        TrayDesktop.Run(() =>
        {
            shown.Settings.Change(s => s with { FlyoutSize = flyout, PinnedSize = pinned, PinnedPosition = pinnedAt });
            shown.Window.Hide();
            shown.Window.SummonFromTray();
        });
        await Task.Delay(300);
        TrayDesktop.Run(() => AssertWhollyOnScreenAtTheMinimum(shown.Window, area, "from the tray (G1)"));

        // The pointer at the working area's bottom-right corner: the place's corner there, so a window grown by its
        // frame alone would hang past the edge.
        TrayDesktop.Run(() =>
        {
            shown.Window.Hide();
            shown.Window.SummonFromShortcut(default, new SummonAnchor(new PixelPoint(area.Right - 1, area.Bottom - 1)));
        });
        await Task.Delay(300);
        TrayDesktop.Run(() => AssertWhollyOnScreenAtTheMinimum(shown.Window, area, "from the shortcut at the pointer (G1)"));

        TrayDesktop.Run(() => shown.Click(Pin(shown)));
        await Task.Delay(300);
        TrayDesktop.Run(() =>
        {
            WindowMinimum.AssertAt(shown.Window.ClientSize, "pinned by its button");
            Assert.AreEqual(new PixelPoint(pinnedAt.X, pinnedAt.Y), shown.Window.Position, "where the user left it (G2)");
            var settings = shown.Settings.Current;
            Assert.AreEqual(flyout, settings.FlyoutSize, "the flyout's size stays as stored: raised for the showing only (G2, G31)");
            Assert.AreEqual(pinned, settings.PinnedSize, "and the pinned window's");
            Assert.AreEqual(pinnedAt, settings.PinnedPosition);
        });
    }

    /// <summary>The window is at the minimum with its whole rectangle, frame included, inside <paramref name="area"/>.</summary>
    private static void AssertWhollyOnScreenAtTheMinimum(Avalonia.Controls.Window window, PixelRect area, string because)
    {
        WindowMinimum.AssertAt(window.ClientSize, because);
        var outer = new PixelRect(window.Position, PixelSize.FromSize(window.FrameSize!.Value, window.RenderScaling));
        Assert.IsTrue(area.Contains(outer), $"{because}: the window at {outer} wholly inside the working area {area}");
    }

    [TestMethod]
    public async Task APinAndAnUnpinLeaveThePasteTargetTheFooterNames()
    {
        var target = new PresetTarget { Before = Other };
        using var shown = ShownWindow.Open(target: target);
        await shown.OpenStash(shown.Picture("chrome target.png"));
        TrayDesktop.Run(() =>
        {
            shown.Window.Hide();
            shown.Window.SummonFromShortcut(new ForegroundReading(false, Relay), new SummonAnchor(null));
        });
        await TrayDesktop.Get(() => shown.Listing.Settle());
        await Task.Delay(300);
        var summoned = TrayDesktop.Get(shown.FooterHints);

        TrayDesktop.Run(() => shown.Click(Pin(shown)));
        await Task.Delay(300);
        var pinned = TrayDesktop.Get(() => (shown.Window.ShowInTaskbar, shown.FooterHints()));
        TrayDesktop.Run(() => shown.Click(Pin(shown)));
        await Task.Delay(300);
        var unpinned = TrayDesktop.Get(() => (shown.Window.ShowInTaskbar, shown.FooterHints()));

        var relay = new[] { "Enter · Paste into Relay", "Ctrl+C · Copy" };
        CollectionAssert.AreEqual(relay, summoned, "the press's target");
        Assert.IsTrue(pinned.ShowInTaskbar, "pinned");
        CollectionAssert.AreEqual(relay, pinned.Item2, "the pin's own activation reads no target (G8)");
        Assert.IsFalse(unpinned.ShowInTaskbar, "unpinned");
        CollectionAssert.AreEqual(relay, unpinned.Item2, "nor does the unpin's");
    }

    [TestMethod]
    public async Task CloseUnpinsAndHidesThePinnedWindow()
    {
        using var shown = ShownWindow.Open();
        TrayDesktop.Run(shown.Window.SummonPinned);
        await Task.Delay(300);

        TrayDesktop.Run(shown.Window.Close);

        TrayDesktop.Run(() =>
        {
            Assert.IsFalse(shown.Window.IsVisible, "hidden, not closed (G3)");
            Assert.IsFalse(shown.Window.ShowInTaskbar, "unpinned (G2)");
            Assert.IsTrue(shown.Window.Topmost);
            Assert.AreEqual(WindowDecorations.BorderOnly, shown.Window.WindowDecorations);
            Assert.AreEqual("Keep open", ToolTip.GetTip(Pin(shown)));
        });
    }

    [TestMethod]
    [DataRow(false, DisplayName = "minimised by its state")]
    [DataRow(true, DisplayName = "minimised by its minimise button's command")]
    public async Task APinnedWindowsPlaceOutlastsAMinimiseAndACloseWhileMinimisedAndThePinBringsItBackThere(bool byItsButton)
    {
        using var shown = ShownWindow.Open();
        TrayDesktop.Run(shown.Window.SummonPinned);
        await Task.Delay(300);
        var handle = TrayDesktop.Get(() => shown.Window.TryGetPlatformHandle()!.Handle);
        var place = DisplayPlaces.Rect(DisplayPlaces.Primary(), 100, 40, 700, 640);
        TrayDesktop.Run(() => Assert.IsTrue(SetWindowPos(handle, 0, place.X, place.Y, place.Width, place.Height, NoZOrderNoActivate)));
        await Task.Delay(600);
        TrayDesktop.Run(() => Assert.AreEqual(new WindowPosition(place.X, place.Y), shown.Settings.Current.PinnedPosition, "a move the user makes is remembered (G2)"));

        await Minimise(shown, handle, byItsButton);
        await Task.Delay(600);
        TrayDesktop.Run(() => Assert.AreEqual(new WindowPosition(place.X, place.Y), shown.Settings.Current.PinnedPosition, "the minimise's own move is not the user's (G2)"));

        TrayDesktop.Run(() => shown.Window.WindowState = WindowState.Normal);
        await Task.Delay(300);
        TrayDesktop.Run(() => shown.Window.Position = DisplayPlaces.At(DisplayPlaces.Primary(), 160, 80));
        await Task.Delay(600);
        TrayDesktop.Run(() => Assert.AreEqual(new WindowPosition(DisplayPlaces.Scaled(DisplayPlaces.Primary(), 160), DisplayPlaces.Scaled(DisplayPlaces.Primary(), 80)), shown.Settings.Current.PinnedPosition, "restored and moved, the move is remembered at once"));

        await Minimise(shown, handle, byItsButton);
        TrayDesktop.Run(() => shown.Window.Close());
        await Task.Delay(600);
        TrayDesktop.Run(() =>
        {
            Assert.IsFalse(shown.Window.IsVisible, "Close while minimised unpins and hides it (G2)");
            Assert.AreEqual(new WindowPosition(DisplayPlaces.Scaled(DisplayPlaces.Primary(), 160), DisplayPlaces.Scaled(DisplayPlaces.Primary(), 80)), shown.Settings.Current.PinnedPosition, "and its place stays where the user left it");
            shown.Window.SummonPinned();
        });
        await Task.Delay(600);
        TrayDesktop.Run(() =>
        {
            Assert.AreEqual(WindowState.Normal, shown.Window.WindowState);
            Assert.AreEqual(DisplayPlaces.At(DisplayPlaces.Primary(), 160, 80), shown.Window.Position, "pinned again, where it was before the minimise (G2)");
        });
    }

    /// <summary>The pinned window minimised by its state, or by the minimise command its title bar's button posts.</summary>
    private static async Task Minimise(ShownWindow shown, nint handle, bool byItsButton)
    {
        if (byItsButton)
        {
            Assert.IsTrue(PostMessageW(handle, 0x0112, 0xF020, 0));
        }
        else
        {
            TrayDesktop.Run(() => shown.Window.WindowState = WindowState.Minimized);
        }

        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Window.WindowState == WindowState.Minimized));
    }

    [TestMethod]
    public async Task APinnedPlaceWhoseTitleBarWasOnADisplayNowGoneShowsWithItsTitleBarOnADisplayThatRemains()
    {
        using var shown = ShownWindow.Open();
        await shown.OpenStash(shown.Picture("placement.png"));
        TrayDesktop.Run(() =>
        {
            var display = Summon.DisplaysOf(shown.Window.Screens).Single(d => d.IsPrimary);
            var area = display.WorkingArea;
            if (area.Width / display.Scaling < 900 || area.Height / display.Scaling < 700)
            {
                Assert.Inconclusive("The primary display's working area does not hold 900 × 700 device-independent pixels.");
            }

            // Its bottom 100 pixels on the primary display, its title bar where a display above it stood.
            var place = new WindowPosition(area.X + DisplayPlaces.Scaled(display, 100), area.Y - DisplayPlaces.Scaled(display, 700) + DisplayPlaces.Scaled(display, 100));
            if (Summon.DisplaysOf(shown.Window.Screens).Any(d => d.WorkingArea.Intersects(new PixelRect(place.X, place.Y, 1, 1))))
            {
                Assert.Inconclusive("A display stands above the primary one, so the remembered title bar is still on a display.");
            }

            shown.Settings.Change(settings => settings with { PinnedSize = new WindowSize(900, 700), PinnedPosition = place });
            shown.Window.SummonPinned();
        });
        // The placement WindowChrome posts once the frame is known.
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        TrayDesktop.Run(() =>
        {
            var window = shown.Window;
            var outer = PixelSize.FromSize(window.FrameSize ?? window.ClientSize, window.RenderScaling);
            var titleBar = new PixelRect(window.Position.X, window.Position.Y, outer.Width, (int)Math.Ceiling(Summon.TitleStrip * window.RenderScaling));
            Assert.IsTrue(Summon.DisplaysOf(window.Screens).Any(display => display.WorkingArea.Intersects(titleBar)),
                $"the title bar the user grabs the window by is on a display (G1, G2): {titleBar}");
        });
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessageW(nint window, uint message, nint wParam, nint lParam);

    private const uint NoZOrderNoActivate = 0x0004 | 0x0010;

    private const uint NoSizeNoZOrderNoActivate = 0x0001 | NoZOrderNoActivate;

    /// <summary>The pin button, by the words it is named with in either state.</summary>
    private static ToggleButton Pin(ShownWindow shown) =>
        shown.Window.GetVisualDescendants().OfType<ToggleButton>().Single(button => AutomationProperties.GetName(button) is "Keep open" or "Unpin");

    /// <summary>The whole window, frame included, is <paramref name="expected"/> in physical pixels, within one.</summary>
    private static void AssertFrame(PixelSize expected, Avalonia.Controls.Window window, string because)
    {
        var outer = PixelSize.FromSize(window.FrameSize!.Value, window.RenderScaling);
        Assert.IsLessThanOrEqualTo(1, Math.Abs(outer.Width - expected.Width), $"{because}: width {outer.Width}, not {expected.Width}");
        Assert.IsLessThanOrEqualTo(1, Math.Abs(outer.Height - expected.Height), $"{because}: height {outer.Height}, not {expected.Height}");
    }

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint window, nint insertAfter, int x, int y, int width, int height, uint flags);
}
