using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Input;
using ImageStash.Media;
using ImageStash.Window;
using ImageStash.Windows.Seams;
using ImageStash.Windows.Tests.Support;

namespace ImageStash.Windows.Tests.Seams;

/// <summary>
/// The real screen (G14, S4, E1): the displays as Avalonia gives them, in physical pixels; a grab of the desktop as it is
/// composed when called, opaque and top-down, read against windows of the check's own painting known colours at known
/// places; the pointer drawn in only when asked and only where it lies (G15); a window marked before its first showing
/// left out of the grab; a grab that cannot be made named, never thrown. These checks show topmost windows of their own
/// on the primary display, move the pointer over them, and read the screen's pixels there.
/// </summary>
[TestClass]
public sealed class WindowsScreenTests
{
    private const uint Backdrop = 0xFF6E5A46;
    private const uint Cover = 0xFF32A0A0;
    private const nint IdcCross = 32515;
    private const int LeftButton = 0x01;
    private const uint CursorShowing = 1;

    [TestMethod]
    public async Task AGrabOfThePrimaryDisplayHoldsThePatternWhereItLiesOpaqueAndTopDown()
    {
        var screen = new WindowsScreen();
        using var pattern = PatternWindow.Show();
        await pattern.OnScreen(screen);
        var primary = screen.Displays().Single(display => display.IsPrimary);

        var grab = await Task.Run(() => screen.Grab(primary.Bounds, withPointer: false));

        Assert.IsTrue(grab.Succeeded, grab.Failure?.Sentence);
        var frame = grab.Value!;
        Assert.AreEqual((primary.Bounds.Width, primary.Bounds.Height, 0), (frame.Width, frame.Height, frame.DurationMs), "the display's size in physical pixels, a still");
        Assert.HasCount(frame.Width * frame.Height * 4, frame.Pixels, "rows packed without padding");
        foreach (var (block, colour) in pattern.Blocks)
        {
            var centre = block.Center - primary.Bounds.Position;
            Assert.AreEqual(colour, PatternWindow.ColourAt(frame, centre.X, centre.Y), $"the block at {block}, where it lies on the desktop");
        }

        Assert.IsEmpty(pattern.Differences(frame, primary.Bounds), "every pixel of the pattern as painted");
        Assert.IsTrue(Enumerable.Range(0, frame.Width * frame.Height).All(i => frame.Pixels[i * 4 + 3] == 255), "every alpha byte 255, though GDI leaves it undefined");

        var inside = pattern.Blocks[1].Block;
        var part = screen.Grab(new PixelRect(inside.X + 10, inside.Y + 10, 20, 15), withPointer: false);
        Assert.IsTrue(part.Succeeded, part.Failure?.Sentence);
        Assert.AreEqual((20, 15), (part.Value!.Width, part.Value.Height));
        Assert.IsTrue(Enumerable.Range(0, 20 * 15).All(i => PatternWindow.ColourAt(part.Value, i % 20, i / 20) == PatternWindow.PatternColours[1]), "a rectangle inside one block is that block's colour throughout");

        var (top, bottom) = (pattern.Blocks[0].Block, pattern.Blocks[2].Block);
        var column = screen.Grab(new PixelRect(top.X + 10, top.Y + 10, 10, bottom.Bottom - top.Y - 20), withPointer: false);
        Assert.IsTrue(column.Succeeded, column.Failure?.Sentence);
        Assert.AreEqual(PatternWindow.PatternColours[0], PatternWindow.ColourAt(column.Value!, 5, 0), "row 0 is the top of the rectangle");
        Assert.AreEqual(PatternWindow.PatternColours[2], PatternWindow.ColourAt(column.Value!, 5, column.Value!.Height - 1), "the last row its bottom");
    }

    [TestMethod]
    public async Task ThePointerIsDrawnInAtItsHotSpotWhereItLiesInsideTheRectangleAndOnlyWhenAsked()
    {
        var screen = new WindowsScreen();
        using var putBack = TestMouse.PutBackAfterwards();
        // A crosshair's hot spot is its centre, so a pointer drawn without subtracting it lands visibly off.
        using var flat = PatternWindow.Flat(PatternWindow.Place, Backdrop, window => window.Cursor = new Cursor(StandardCursorType.Cross));
        await flat.OnScreen(screen);
        var place = PatternWindow.Place;
        var at = new PixelPoint(place.X + 120, place.Y + 80);
        await PointerShowingTheCrosshairAt(at);

        var drawn = await Task.Run(() => screen.Grab(place, withPointer: true));
        var plain = await Task.Run(() => screen.Grab(place, withPointer: false));

        Assert.IsTrue(drawn.Succeeded && plain.Succeeded, drawn.Failure?.Sentence ?? plain.Failure?.Sentence);
        var changed = Changed(flat, drawn.Value!, place);
        Assert.IsNotEmpty(changed, "the pointer drawn into the grab");
        var (left, top, right, bottom) = (changed.Min(p => p.X), changed.Min(p => p.Y), changed.Max(p => p.X), changed.Max(p => p.Y));
        var centre = new PixelPoint((left + right) / 2, (top + bottom) / 2);
        var expected = at - place.Position;
        Assert.IsTrue(Math.Abs(centre.X - expected.X) <= 2 && Math.Abs(centre.Y - expected.Y) <= 2, $"the crosshair centred where the pointer is in the rectangle, {expected}, not at {centre} (drawn {left}, {top} to {right}, {bottom})");
        Assert.IsTrue(Enumerable.Range(0, place.Width * place.Height).All(i => drawn.Value!.Pixels[i * 4 + 3] == 255), "every alpha byte 255");
        Assert.IsEmpty(flat.Differences(plain.Value!, place), "without the pointer, every pixel the flat colour");

        TestMouse.MoveTo(new PixelPoint(place.X - 400, place.Y + 80));
        var away = await Task.Run(() => screen.Grab(place, withPointer: true));

        Assert.IsTrue(away.Succeeded, away.Failure?.Sentence);
        Assert.IsEmpty(flat.Differences(away.Value!, place), "a pointer outside the rectangle draws nothing in it");
    }

    [TestMethod]
    public async Task TheDisplaysAreTheOnesAvaloniaGivesAShownWindowFromAnyThread()
    {
        var screen = new WindowsScreen();
        using var pattern = PatternWindow.Show();
        await pattern.OnScreen(screen);

        var shown = TrayDesktop.Get(() => Summon.DisplaysOf(pattern.Window.Screens)).ToList();
        var read = await Task.Run(() => screen.Displays());

        CollectionAssert.AreEqual(shown, read.ToList(), "bounds, working area, scaling and primary, in physical pixels (E1, S10)");
        Assert.ContainsSingle(read.Where(display => display.IsPrimary));
    }

    [TestMethod]
    public async Task AWindowHiddenJustBeforeTheGrabIsNotInIt()
    {
        var screen = new WindowsScreen();
        using var backdrop = PatternWindow.Flat(PatternWindow.Place, Backdrop);
        await backdrop.OnScreen(screen);
        // Three times, since a grab that only sometimes waits for the composition would pass once.
        for (var round = 1; round <= 3; round++)
        {
            using var pattern = PatternWindow.Show();
            await pattern.OnScreen(screen);

            TrayDesktop.Run(pattern.Window.Hide);
            var grab = screen.Grab(PatternWindow.Place, withPointer: false);

            Assert.IsTrue(grab.Succeeded, grab.Failure?.Sentence);
            Assert.IsEmpty(backdrop.Differences(grab.Value!, PatternWindow.Place), $"round {round}: what lies beneath the window hidden at once before the grab");
        }
    }

    [TestMethod]
    public async Task AWindowMarkedBeforeItsFirstShowingIsLeftOutOfTheGrabAndAnUnmarkedOneIsIn()
    {
        var screen = new WindowsScreen();
        using var pattern = PatternWindow.Show();
        await pattern.OnScreen(screen);
        var (topLeft, bottomRight) = (pattern.Blocks[0].Block, pattern.Blocks[3].Block);
        var markedAt = new PixelRect(topLeft.X + 10, topLeft.Y + 10, topLeft.Width - 20, topLeft.Height - 20);
        var unmarkedAt = new PixelRect(bottomRight.X + 10, bottomRight.Y + 10, bottomRight.Width - 20, bottomRight.Height - 20);
        Outcome? marked = null;
        var shownWhenMarked = true;

        using var markedCover = PatternWindow.Flat(markedAt, Cover, window =>
        {
            shownWhenMarked = window.IsVisible;
            marked = screen.ExcludeFromCapture(window);
        });
        using var unmarkedCover = PatternWindow.Flat(unmarkedAt, Cover);
        // Both shown in one go: once the unmarked one is composed, so is the marked one.
        await unmarkedCover.OnScreen(screen);
        var underMarked = screen.Grab(markedAt, withPointer: false);
        var underUnmarked = screen.Grab(unmarkedAt, withPointer: false);

        Assert.IsTrue(marked?.Succeeded, marked?.Failure?.Sentence);
        Assert.IsFalse(shownWhenMarked, "marked before its first showing");
        Assert.IsTrue(TrayDesktop.Get(() => markedCover.Window.IsVisible), "the marked window is showing");
        Assert.IsTrue(underMarked.Succeeded && underUnmarked.Succeeded);
        Assert.IsEmpty(pattern.Differences(underMarked.Value!, markedAt), "the pattern beneath the marked window shows through it (S4)");
        Assert.IsEmpty(unmarkedCover.Differences(underUnmarked.Value!, unmarkedAt), "the unmarked window is in the grab");
    }

    [TestMethod]
    public async Task AWindowWithoutItsPlatformWindowIsNotMarkedAndTheFailureNamesIt()
    {
        var screen = new WindowsScreen();
        var closed = PatternWindow.Flat(PatternWindow.Place, Cover);
        closed.Dispose();
        await Task.Delay(100);

        var marked = TrayDesktop.Get(() => screen.ExcludeFromCapture(closed.Window));

        Assert.IsFalse(marked.Succeeded);
        Assert.AreEqual(("Hide a window from captures", "ImageStash check pattern"), (marked.Failure.Action, marked.Failure.Subject));
        Assert.IsFalse(string.IsNullOrWhiteSpace(marked.Failure.Reason));
    }

    [TestMethod]
    public void AGrabOfAnEmptyRectangleOrOneOffEveryDisplayIsANamedFailureNeverAnException()
    {
        var screen = new WindowsScreen();
        var primary = screen.Displays().Single(display => display.IsPrimary).Bounds;

        foreach (var rectangle in new[]
        {
            new PixelRect(10, 10, 0, 5),
            new PixelRect(10, 10, 5, -1),
            new PixelRect(-100000, -100000, 50, 50),
            new PixelRect(primary.Right - 10, primary.Y + 10, 20 + 100000, 20),
        })
        {
            var grab = screen.Grab(rectangle, withPointer: false);

            Assert.IsFalse(grab.Succeeded, $"{rectangle} is refused");
            Assert.AreEqual("Grab the screen", grab.Failure.Action);
            Assert.Contains($"{rectangle.X}, {rectangle.Y}", grab.Failure.Subject, "the subject says which area");
            Assert.IsFalse(string.IsNullOrWhiteSpace(grab.Failure.Reason));
            Assert.IsNull(grab.Value);
        }
    }

    /// <summary>The grab's points, in its own coordinates, where the flat window's colour is known and the grab differs from it.</summary>
    private static List<PixelPoint> Changed(PatternWindow flat, MediaFrame grab, PixelRect grabbed) =>
        Enumerable.Range(0, grabbed.Width * grabbed.Height)
            .Select(i => new PixelPoint(i % grabbed.Width, i / grabbed.Width))
            .Where(p => flat.Expected(grabbed.Position + p) is { } colour && PatternWindow.ColourAt(grab, p.X, p.Y) != colour)
            .ToList();

    /// <summary>
    /// Moves the pointer to <paramref name="at"/> and waits until the shell shows the system's crosshair there, as the
    /// window under it asks; each try comes in from a pixel beside it, since a move to where the pointer already is
    /// sends the window nothing to answer.
    /// </summary>
    private static async Task PointerShowingTheCrosshairAt(PixelPoint at)
    {
        var crosshair = LoadCursorW(0, IdcCross);
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (true)
        {
            TestMouse.MoveTo(new PixelPoint(at.X + 3, at.Y + 3));
            TestMouse.MoveTo(at);
            await Task.Delay(50);
            var info = new CursorInfo { Size = (uint)Marshal.SizeOf<CursorInfo>() };
            if (GetCursorInfo(ref info) && info.Flags == CursorShowing && info.Cursor == crosshair && (info.X, info.Y) == (at.X, at.Y))
            {
                return;
            }

            var under = WindowFromPoint(new ScreenPoint { X = info.X, Y = info.Y });
            var title = new System.Text.StringBuilder(256);
            GetWindowTextW(under, title, title.Capacity);
            Assert.IsLessThan(deadline, DateTime.UtcNow, $"the crosshair never showed: flags {info.Flags}, cursor {info.Cursor}, crosshair {crosshair}, pointer at {info.X}, {info.Y} over \"{title}\" ({under}), left button {(GetAsyncKeyState(LeftButton) < 0 ? "down" : "up")}");
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ScreenPoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CursorInfo
    {
        public uint Size;
        public uint Flags;
        public nint Cursor;
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorInfo(ref CursorInfo info);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint LoadCursorW(nint instance, nint name);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint WindowFromPoint(ScreenPoint point);

    [DllImport("user32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextW(nint window, System.Text.StringBuilder text, int length);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern short GetAsyncKeyState(int key);
}
