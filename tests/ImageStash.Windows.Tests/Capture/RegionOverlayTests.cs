using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using ImageStash.Capture;
using ImageStash.Media;
using ImageStash.Windows.Seams;
using ImageStash.Windows.Tests.Support;

namespace ImageStash.Windows.Tests.Capture;

/// <summary>
/// The region selection on the real desktop (G14, F4): over real grabs, within a second an overlay covers the primary
/// display, topmost and in front, showing the frozen picture dimmed; a real drag shows the rectangle undimmed with its
/// size in physical pixels and the release returns it; Esc and a click without a drag cancel; no overlay is left after
/// any of them. These checks show a pattern window of their own and a window of their own at (100, 400), drag the mouse
/// over the primary display and read the screen's pixels there.
/// </summary>
[TestClass]
public sealed class RegionOverlayTests
{
    private const ushort VkEscape = 0x1B;
    private static readonly TimeSpan PersonsPause = TimeSpan.FromMilliseconds(300);

    [TestMethod]
    public async Task OverTheFrozenScreenADragShowsItsRectangleUndimmedWithItsSizeAndTheReleaseReturnsIt()
    {
        using var desk = await OverlayDesk.Open();
        var primary = desk.Primary;
        var (press, release) = (desk.Pattern.Blocks[0].Block.Position + new PixelVector(20, 20), desk.Pattern.Blocks[3].Block.Position + new PixelVector(80, 50));

        var selecting = desk.Select();
        var window = await desk.InFrontWithinASecond();
        var undragged = await desk.ScreenWhen(grab => Darker(grab, primary.Frame, desk.Pattern.Bounds).Count == 0);
        Assert.IsEmpty(Darker(undragged, primary.Frame, desk.Pattern.Bounds), "the whole frozen display dimmed before a drag");

        TestMouse.Press(press);
        var pressed = TestMouse.Position();
        foreach (var step in Enumerable.Range(1, 8))
        {
            TestMouse.MoveTo(new PixelPoint(press.X + (release.X - press.X) * step / 8, press.Y + (release.Y - press.Y) * step / 8));
            await Task.Delay(15);
        }

        // The pixels the pointer is on, as Windows placed it, both corners taken in.
        var at = TestMouse.Position();
        var expected = new PixelRect(pressed.X, pressed.Y, at.X - pressed.X + 1, at.Y - pressed.Y + 1);
        Assert.IsTrue(desk.Pattern.Blocks[0].Block.Contains(pressed) && desk.Pattern.Blocks[3].Block.Contains(at), $"from {pressed} to {at}");
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => desk.Overlay.Label) == $"{expected.Width} × {expected.Height}");
        var outside = new[]
        {
            new PixelRect(desk.Pattern.Bounds.X + 3, expected.Y, expected.X - 3 - (desk.Pattern.Bounds.X + 3), desk.Pattern.Bounds.Bottom - 3 - expected.Y),
            new PixelRect(desk.Pattern.Bounds.X + 3, expected.Bottom + 3, desk.Pattern.Bounds.Width - 6, desk.Pattern.Bounds.Bottom - 3 - (expected.Bottom + 3)),
        };
        var dragged = await desk.ScreenWhen(grab => Unequal(grab, primary.Frame, expected).Count == 0 && outside.All(area => Darker(grab, primary.Frame, area).Count == 0));
        Assert.IsEmpty(Unequal(dragged, primary.Frame, expected), "inside the rectangle, the frozen picture as grabbed");
        foreach (var area in outside)
        {
            Assert.IsEmpty(Darker(dragged, primary.Frame, area), $"outside it, dimmed: {area}");
        }

        TestMouse.Release(at);
        var region = await selecting.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.AreEqual(new SelectedRegion(primary.Display, expected), region, "the rectangle in physical pixels on its display");
        await desk.NoOverlayLeft(window);
    }

    [TestMethod]
    public async Task EscCancelsAndLeavesNoOverlay()
    {
        using var desk = await OverlayDesk.Open();

        var selecting = desk.Select();
        var window = await desk.InFrontWithinASecond();
        TestHotKeys.Tap(VkEscape);

        Assert.IsNull(await selecting.WaitAsync(TimeSpan.FromSeconds(10)));
        await desk.NoOverlayLeft(window);
    }

    [TestMethod]
    public async Task TheRecordShortcutsOverlayIsTheSameUnderItsOwnTitleAndEscCancelsIt()
    {
        using var desk = await OverlayDesk.Open();

        var selecting = desk.Select(CaptureMode.Recording);
        var window = await desk.InFrontWithinASecond();

        Assert.AreEqual(RegionOverlay.RecordingTitle, FrontWindow.NativeText(window), "the record shortcut opens the same overlay, named for what its release starts (G15)");
        TestHotKeys.Tap(VkEscape);
        Assert.IsNull(await selecting.WaitAsync(TimeSpan.FromSeconds(10)));
        await desk.NoOverlayLeft(window);
        Assert.AreEqual(nint.Zero, FindWindowW(null, RegionOverlay.RecordingTitle), "no record overlay left (F4)");
    }

    [TestMethod]
    public async Task AClickWithoutADragCancelsAndLeavesNoOverlay()
    {
        using var desk = await OverlayDesk.Open();

        var selecting = desk.Select();
        var window = await desk.InFrontWithinASecond();
        TestMouse.Click(desk.Pattern.Blocks[1].Block.Center);

        Assert.IsNull(await selecting.WaitAsync(TimeSpan.FromSeconds(10)), "a click selects nothing");
        await desk.NoOverlayLeft(window);
    }

    /// <summary>Every pixel of <paramref name="area"/> on the screen not darker, by its channels' sum, than the frozen grab's; "x, y" each.</summary>
    private static List<string> Darker(MediaFrame screen, MediaFrame frozen, PixelRect area) =>
        Pixels(area).Where(p => Sum(screen, p) >= Sum(frozen, p)).Select(p => $"{p.X}, {p.Y}").ToList();

    /// <summary>Every pixel of <paramref name="area"/> on the screen that is not the frozen grab's; "x, y" each.</summary>
    private static List<string> Unequal(MediaFrame screen, MediaFrame frozen, PixelRect area) =>
        Pixels(area).Where(p => PatternWindow.ColourAt(screen, p.X, p.Y) != PatternWindow.ColourAt(frozen, p.X, p.Y)).Select(p => $"{p.X}, {p.Y}").ToList();

    private static IEnumerable<PixelPoint> Pixels(PixelRect area) =>
        Enumerable.Range(0, area.Width * area.Height).Select(i => new PixelPoint(area.X + i % area.Width, area.Y + i / area.Width));

    private static int Sum(MediaFrame frame, PixelPoint at) =>
        frame.Pixels[(at.Y * frame.Width + at.X) * 4] + frame.Pixels[(at.Y * frame.Width + at.X) * 4 + 1] + frame.Pixels[(at.Y * frame.Width + at.X) * 4 + 2];

    /// <summary>
    /// The pattern over the primary display, a window of the check's own in front, as the shortcut's press or a menu's
    /// click leaves ImageStash, and every display grabbed as the capture grabs them.
    /// </summary>
    private sealed class OverlayDesk : IDisposable
    {
        private readonly IDisposable pointer;
        private readonly FrontWindow front;
        private readonly Stopwatch sinceSelect = new();

        private OverlayDesk(IDisposable pointer, PatternWindow pattern, FrontWindow front, IReadOnlyList<DisplayGrab> grabs)
        {
            this.pointer = pointer;
            Pattern = pattern;
            this.front = front;
            Grabs = grabs;
        }

        public WindowsScreen Screen { get; } = new();

        public PatternWindow Pattern { get; }

        public IReadOnlyList<DisplayGrab> Grabs { get; }

        public DisplayGrab Primary => Grabs.Single(grab => grab.Display.IsPrimary);

        public RegionOverlay Overlay { get; } = new();

        public static async Task<OverlayDesk> Open()
        {
            TrayDesktop.UseProductTheme();
            var pointer = TestMouse.PutBackAfterwards();
            var screen = new WindowsScreen();
            var pattern = PatternWindow.Show();
            await pattern.OnScreen(screen);
            var front = FrontWindow.Show(new PixelPoint(100, 400));
            // Shown activated, it is in front at once unless Windows refused it the activation; only then is it clicked,
            // since a click in the moment after its own activation was seen to leave no window in front at all.
            if (!await InFrontSoon(front.Handle))
            {
                front.ClickToFront();
            }
            var grabs = await Task.Run(() => screen.Displays().Select(display => new DisplayGrab(display, screen.Grab(display.Bounds, withPointer: false).Value!)).ToList());
            return new OverlayDesk(pointer, pattern, front, grabs);
        }

        private static async Task<bool> InFrontSoon(nint window)
        {
            var clock = Stopwatch.StartNew();
            while (FrontWindow.GetForegroundWindow() != window && clock.ElapsedMilliseconds < 500)
            {
                await Task.Delay(20);
            }

            return FrontWindow.GetForegroundWindow() == window;
        }

        /// <summary>Starts the selection on the UI thread, as the capture does.</summary>
        public Task<SelectedRegion?> Select(CaptureMode mode = CaptureMode.Screenshot)
        {
            sinceSelect.Restart();
            return TrayDesktop.Get(() => Overlay.Select(Grabs, mode));
        }

        /// <summary>
        /// The primary display's overlay once it covers that display's bounds, topmost, and one of the overlays, whichever
        /// display's, is in front: within a second of the call.
        /// </summary>
        public async Task<nint> InFrontWithinASecond()
        {
            var bounds = Primary.Display.Bounds;
            while (true)
            {
                var handles = TrayDesktop.Get(() => Overlay.Shown.Select(window => window.TryGetPlatformHandle()!.Handle).ToList());
                var front = FrontWindow.GetForegroundWindow();
                var primary = handles.FirstOrDefault(handle => GetWindowRect(handle, out var rect) && new PixelRect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top) == bounds);
                if (handles.Contains(front) && primary != 0 && (GetWindowLongPtrW(primary, ExtendedStyle) & Topmost) != 0)
                {
                    Assert.IsLessThan(1000, sinceSelect.ElapsedMilliseconds, "shown and in front within a second (G14)");
                    Assert.HasCount(Grabs.Count, handles, "one overlay per display");
                    // A person's first click or key comes a moment after the overlay shows; a click sent the instant it
                    // came to the front was seen to reach nothing (once in ten runs).
                    await Task.Delay(PersonsPause);
                    return primary;
                }

                Assert.IsLessThan(1000, sinceSelect.ElapsedMilliseconds, $"no overlay covering {bounds}, topmost, with an overlay in front, within a second; {front:X} is in front, the overlays are {string.Join("; ", handles.Select(h => GetWindowRect(h, out var r) ? $"{h:X} at {r.Left}, {r.Top} to {r.Right}, {r.Bottom}" : h.ToString("X")))}");
                await Task.Delay(10);
            }
        }

        /// <summary>The primary display as the screen shows it now, once <paramref name="holds"/> of it, or after a few seconds whatever it is.</summary>
        public async Task<MediaFrame> ScreenWhen(Func<MediaFrame, bool> holds)
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (true)
            {
                var grab = await Task.Run(() => Screen.Grab(Primary.Display.Bounds, withPointer: false));
                Assert.IsTrue(grab.Succeeded, grab.Failure?.Sentence);
                if (holds(grab.Value!) || DateTime.UtcNow > deadline)
                {
                    return grab.Value!;
                }

                await Task.Delay(30);
            }
        }

        /// <summary>The overlay's windows all closed: none showing, the one that was in front gone from the desktop.</summary>
        public async Task NoOverlayLeft(nint window)
        {
            Assert.IsEmpty(TrayDesktop.Get(() => Overlay.Shown));
            await ShownWindow.Eventually(() => !IsWindow(window));
            Assert.AreEqual(nint.Zero, FindWindowW(null, RegionOverlay.Title), "no overlay window of any display left (F4)");
        }

        public void Dispose()
        {
            // A check that failed with the overlay up closes it, which cancels, so it covers no later check.
            TrayDesktop.Run(() =>
            {
                foreach (var window in Overlay.Shown)
                {
                    window.Close();
                }
            });
            // The pattern first: closed after the window in front, it would take the activation for a moment as it went,
            // and the next check's click could find no window in front.
            Pattern.Dispose();
            front.Dispose();
            pointer.Dispose();
        }
    }

    private const int ExtendedStyle = -20;
    private const nint Topmost = 0x8;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, out NativeRect rect);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint GetWindowLongPtrW(nint window, int index);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint window);

    [DllImport("user32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern nint FindWindowW(string? className, string windowName);
}
