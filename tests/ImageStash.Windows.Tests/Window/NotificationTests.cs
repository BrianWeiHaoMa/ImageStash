using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using ImageStash.Media;
using ImageStash.Seams;
using ImageStash.Window;
using ImageStash.Windows.Seams;
using ImageStash.Windows.Tests.Support;

namespace ImageStash.Windows.Tests.Window;

/// <summary>
/// The notification above the tray as Windows shows it (G16, F4): while another program's window is in front, it
/// appears without taking the foreground, and a click on it leaves that program with the keystrokes; it is topmost,
/// without a taskbar button, its caption its words, wholly on the primary display above the tray icon or its corner;
/// each stays six seconds from its own raising, longer while the pointer is over it, and the window closes once the
/// last has gone; each window it shows, and the tooltip over it, is left out of screen captures from before its first
/// showing, and one the platform will not leave out is shown anyway with the refusal named (S4, G35). These checks show
/// it on the primary display, click it, type a letter into a window of their own, move the mouse pointer over it and
/// read the screen's pixels under it.
/// </summary>
[TestClass]
public sealed class NotificationTests
{
    private const string Copied = "Copied · press Ctrl+V";
    private const uint WsExNoActivate = 0x08000000;
    private const uint WsExTopmost = 0x00000008;

    /// <summary>Each check sees the notification as the product lays it out, whichever check ran first.</summary>
    [TestInitialize]
    public void UseProductTheme() => TrayDesktop.UseProductTheme();

    [TestMethod]
    public void ItAppearsAboveTheTrayWithoutTakingTheForegroundAndAClickLeavesTheAppInFrontWithTheKeystrokes()
    {
        using var pointer = TestMouse.PutBackAfterwards();
        using var tray = new WindowsTray();
        using var chat = TargetApp.Start("ImageStash check chat", 100, 100);
        var clock = new MovableClock();
        var notifications = new Notices(clock);
        var notification = TrayDesktop.Get(() => new Notification(notifications, new WindowsQuietWindows(tray), new WindowsScreen(), _ => { }));
        chat.ClickTitleBar();

        TrayDesktop.Run(() => notifications.Raise(Copied));
        Thread.Sleep(500);

        var (handle, active, inTaskbar, topmost, title, outer, area) = TrayDesktop.Get(() =>
        {
            var window = notification.Shown!;
            return (window.TryGetPlatformHandle()!.Handle, window.IsActive, window.ShowInTaskbar, window.Topmost, window.Title,
                new PixelRect(window.Position, PixelSize.FromSize(window.FrameSize ?? window.ClientSize, window.RenderScaling)), window.Screens.Primary!.WorkingArea);
        });
        Assert.AreEqual(chat.Window, FrontWindow.GetForegroundWindow(), "the app in front keeps the foreground (G16)");
        Assert.IsFalse(active);
        Assert.IsFalse(inTaskbar, "no taskbar button");
        Assert.IsTrue(topmost);
        Assert.AreEqual(Copied, title);
        Assert.AreEqual(Copied, FrontWindow.NativeText(handle), "another program finds it by its words");
        Assert.IsTrue(IsWindowVisible(handle));
        var extended = (uint)GetWindowLongPtrW(handle, -20);
        Assert.AreEqual(WsExTopmost, extended & WsExTopmost);
        Assert.AreEqual(WsExNoActivate, extended & WsExNoActivate);
        Assert.IsTrue(area.Contains(outer), $"{outer} lies wholly inside the primary working area {area}");
        Assert.IsGreaterThan(area.Center.Y, outer.Y, "in the lower half, above the tray");
        Assert.IsGreaterThan(area.Center.X, outer.X, "on the right, where the tray icon's corner is");

        var activations = 0;
        TrayDesktop.Run(() => notification.Shown!.Activated += (_, _) => activations++);
        TestMouse.Click(outer.Center);
        Thread.Sleep(300);
        TrayDesktop.Run(() => Assert.IsFalse(notification.Shown!.IsActive || activations > 0, "the click activates it not even within its own thread"));
        var typedBefore = chat.Messages().Count;
        TestHotKeys.Press(new ImageStash.Seams.Shortcut(new KeyGesture(Key.A), "A"));
        Thread.Sleep(300);

        Assert.AreEqual(chat.Window, FrontWindow.GetForegroundWindow(), "a click on it leaves the app in front");
        CollectionAssert.Contains(chat.Messages().Skip(typedBefore).ToList(), "WM_KEYDOWN:0x41", "with the keystrokes");
        TestMouse.MoveTo(TestMouse.Away);
        Thread.Sleep(300);
        clock.Now += TimeSpan.FromSeconds(7);
        Thread.Sleep(600);
        TrayDesktop.Run(() => Assert.IsNull(notification.Shown, "gone after its six seconds"));
        Assert.IsFalse(IsWindow(handle), "its window closed");
    }

    [TestMethod]
    public void AClickOnItWhileImageStashsOwnWindowIsInFrontLeavesThatWindowInFront()
    {
        using var pointer = TestMouse.PutBackAfterwards();
        using var tray = new WindowsTray();
        using var own = FrontWindow.Show(new PixelPoint(100, 100));
        var notifications = new Notices(new MovableClock());
        var notification = TrayDesktop.Get(() => new Notification(notifications, new WindowsQuietWindows(tray), new WindowsScreen(), _ => { }));
        own.ClickToFront();
        var deactivations = 0;
        TrayDesktop.Run(() => own.Window.Deactivated += (_, _) => deactivations++);
        Avalonia.Controls.Window? first = null;
        // Twice: the window closes with its last notice, and the next notice's new window is as quiet as the first.
        for (var round = 1; round <= 2; round++)
        {
            TrayDesktop.Run(() => notifications.Raise(Copied));
            Thread.Sleep(500);
            var (shown, outer) = TrayDesktop.Get(() => (notification.Shown!, new PixelRect(notification.Shown!.Position, PixelSize.FromSize(notification.Shown.ClientSize, notification.Shown.RenderScaling))));
            Assert.IsTrue(first is null || !ReferenceEquals(first, shown), "a new window for the next notice");
            first = shown;

            var elsewhere = FrontWindow.ElsewhereInFront(own.Handle, () => TestMouse.Click(outer.Center), 500);

            Assert.AreEqual(0, elsewhere, $"round {round}: the stash window would keep the focus throughout, so its hide rule has nothing to judge (G2)");
            TrayDesktop.Run(() => Assert.AreEqual(0, deactivations, $"round {round}: never deactivated, so it reads no paste target again (G8)"));
            TrayDesktop.Run(() => Assert.IsTrue(own.Window.IsActive && !shown.IsActive, $"round {round}: the notification took no activation (G16)"));
            TrayDesktop.Run(() => notifications.Close(notifications.Shown.Single()));
            Thread.Sleep(300);
            TrayDesktop.Run(() => Assert.IsNull(notification.Shown, "closed with its last notice"));
        }
    }

    [TestMethod]
    public async Task WithTheTrayIconShownItAppearsAboveTheIcon()
    {
        var tray = new WindowsTray();
        try
        {
            TrayDesktop.Run(() => tray.Show([], "ImageStash Windows check", () => { }));
            // Explorer registers the icon while the UI thread keeps pumping.
            await Task.Delay(1000);
            var notifications = new Notices(new MovableClock());
            var notification = TrayDesktop.Get(() => new Notification(notifications, new WindowsQuietWindows(tray), new WindowsScreen(), _ => { }));
            var icon = TrayDesktop.Get(() => new WindowsQuietWindows(tray).TrayIconRectangle());
            Assert.IsNotNull(icon, "the check's tray icon has a place");

            TrayDesktop.Run(() => notifications.Raise(Copied));
            await Task.Delay(500);

            var (outer, area) = TrayDesktop.Get(() => (new PixelRect(notification.Shown!.Position, PixelSize.FromSize(notification.Shown.ClientSize, notification.Shown.RenderScaling)),
                notification.Shown.Screens.Primary!.WorkingArea));
            Assert.IsLessThanOrEqualTo(icon.Value.Y, outer.Bottom, $"{outer} is above the icon {icon}");
            Assert.IsTrue(outer.X <= icon.Value.Center.X && icon.Value.Center.X <= outer.Right || outer.Right == area.Right, $"{outer} is over the icon {icon}, or at the display's edge");
            Assert.IsTrue(area.Contains(outer));
            TrayDesktop.Run(() => notifications.Close(notifications.Shown.Single()));
        }
        finally
        {
            TrayDesktop.Run(tray.Dispose);
        }
    }

    [TestMethod]
    public void EachOfTwoStaysSixSecondsFromItsOwnRaisingAndTheWindowClosesWithTheLast()
    {
        using var pointer = TestMouse.PutBackAfterwards();
        using var tray = new WindowsTray();
        var clock = new MovableClock();
        var notifications = new Notices(clock);
        var notification = TrayDesktop.Get(() => new Notification(notifications, new WindowsQuietWindows(tray), new WindowsScreen(), _ => { }));
        TestMouse.MoveTo(TestMouse.Away);

        TrayDesktop.Run(() => notifications.Raise(Copied));
        clock.Now += TimeSpan.FromSeconds(0.8);
        TrayDesktop.Run(() => notifications.Raise(Copied));
        Thread.Sleep(400);
        var cardsAtFirst = Cards(notification);
        clock.Now += TimeSpan.FromSeconds(5.1);
        Thread.Sleep(600);
        var cardsBeforeSix = Cards(notification);
        clock.Now += TimeSpan.FromSeconds(0.2);
        Thread.Sleep(600);
        var cardsAfterTheFirstsSix = Cards(notification);
        clock.Now += TimeSpan.FromSeconds(0.8);
        Thread.Sleep(600);

        Assert.AreEqual(2, cardsAtFirst, "both show, each with its words");
        Assert.AreEqual(2, cardsBeforeSix, "5.9 seconds after the first, both still show");
        Assert.AreEqual(1, cardsAfterTheFirstsSix, "the first leaves at its six seconds, the second stays (G16)");
        TrayDesktop.Run(() => Assert.IsNull(notification.Shown, "the window closes with the last"));
    }

    [TestMethod]
    public void WhileThePointerIsOverItItStays()
    {
        using var pointer = TestMouse.PutBackAfterwards();
        using var tray = new WindowsTray();
        var clock = new MovableClock();
        var notifications = new Notices(clock);
        var notification = TrayDesktop.Get(() => new Notification(notifications, new WindowsQuietWindows(tray), new WindowsScreen(), _ => { }));
        TrayDesktop.Run(() => notifications.Raise(Copied));
        Thread.Sleep(400);
        var outer = TrayDesktop.Get(() => new PixelRect(notification.Shown!.Position, PixelSize.FromSize(notification.Shown.ClientSize, notification.Shown.RenderScaling)));

        TestMouse.MoveTo(outer.Center);
        Thread.Sleep(300);
        clock.Now += TimeSpan.FromSeconds(10);
        Thread.Sleep(600);
        var stillThere = TrayDesktop.Get(() => notification.Shown is not null);
        TestMouse.MoveTo(TestMouse.Away);
        Thread.Sleep(300);
        clock.Now += TimeSpan.FromSeconds(6.5);
        Thread.Sleep(600);

        Assert.IsTrue(stillThere, "longer while hovered (G16)");
        TrayDesktop.Run(() => Assert.IsNull(notification.Shown, "and gone once its remaining time has run after the pointer left"));
    }

    [TestMethod]
    public void MoreThanFitAboveTheTrayKeepTheNewestInSight()
    {
        using var pointer = TestMouse.PutBackAfterwards();
        using var tray = new WindowsTray();
        var notifications = new Notices(new MovableClock());
        var notification = TrayDesktop.Get(() => new Notification(notifications, new WindowsQuietWindows(tray), new WindowsScreen(), _ => { }));
        TestMouse.MoveTo(TestMouse.Away);

        // As many as Enter held down raises, one per key repeat, while the target is gone.
        for (var raised = 0; raised < 40; raised++)
        {
            TrayDesktop.Run(() => notifications.Raise(Copied));
        }

        Thread.Sleep(600);
        var (top, bottom, height) = TrayDesktop.Get(() =>
        {
            var window = notification.Shown!;
            var newest = window.GetVisualDescendants().OfType<TextBlock>().Last(text => text.Text == Copied);
            var at = newest.TranslatePoint(default, window)!.Value.Y;
            return (at, at + newest.Bounds.Height, window.ClientSize.Height);
        });

        Assert.IsTrue(top >= 0 && bottom <= height, $"the newest notification shows inside its window: {top:0}–{bottom:0} of {height:0} (G16)");
    }

    [TestMethod]
    public async Task EachNewWindowOfItAsksWindowsForItsOwnRoundedCorners()
    {
        using var tray = new WindowsTray();
        var notifications = new Notices(new MovableClock());
        var notification = TrayDesktop.Get(() => new Notification(notifications, new TrayIconMidScreen(new WindowsQuietWindows(tray)), new WindowsScreen(), _ => { }));
        var handles = new List<nint>();
        for (var shown = 0; shown < 2; shown++)
        {
            TrayDesktop.Run(() => notifications.Raise(Copied));
            await Task.Delay(300);
            var handle = TrayDesktop.Get(() => notification.Shown!.TryGetPlatformHandle()!.Handle);
            handles.Add(handle);
            Assert.AreEqual(CornerPreference.Round, CornerPreference.Of(handle), "rounded as Windows 11 rounds its own windows (F13)");
            TrayDesktop.Run(() => notifications.Close(notifications.Shown.Single()));
            await Task.Delay(300);
        }

        Assert.AreNotEqual(handles[0], handles[1], "the second notice shows a new window, rounded too");
    }

    [TestMethod]
    public async Task EachNewWindowOfItIsLeftOutOfEveryGrabFromItsShowingOn()
    {
        using var pointer = TestMouse.PutBackAfterwards();
        using var tray = new WindowsTray();
        var screen = new WindowsScreen();
        var named = new List<Failure>();
        var notifications = new Notices(new MovableClock());
        var notification = TrayDesktop.Get(() => new Notification(notifications, new TrayIconMidScreen(new WindowsQuietWindows(tray)), screen, named.Add));
        TestMouse.MoveTo(TestMouse.Away);
        TrayDesktop.Run(() => notifications.Raise(Copied));
        await Task.Delay(500);
        var where = TrayDesktop.Get(() => Outer(notification.Shown!));
        TrayDesktop.Run(() => notifications.Close(notifications.Shown.Single()));
        using var pattern = PatternWindow.Show(where);
        await pattern.OnScreen(screen);

        // The window closed with its notice, so the next notice shows a new window, over the pattern; grabs follow at once,
        // one per composition, until it has long been drawn.
        var shown = TrayDesktop.Get(() =>
        {
            notifications.Raise(Copied);
            return notification.Shown!;
        });
        var grabs = new List<Outcome<MediaFrame>>();
        for (var since = Stopwatch.StartNew(); since.ElapsedMilliseconds < 800;)
        {
            grabs.Add(screen.Grab(where, withPointer: false));
        }

        Assert.IsTrue(IsWindowVisible(TrayDesktop.Get(() => shown.TryGetPlatformHandle()!.Handle)), "the notification shows over the pattern");
        Assert.IsTrue(grabs.All(grab => grab.Succeeded));
        Assert.IsEmpty(grabs.SelectMany(grab => pattern.Differences(grab.Value!, where)).ToList(), $"none of {grabs.Count} grabs from its showing on holds any of it (S4)");
        Assert.IsEmpty(named, "the platform marked it");
        TrayDesktop.Run(() => notifications.Close(notifications.Shown.Single()));
    }

    [TestMethod]
    public async Task AWindowThePlatformWillNotMarkStillShowsWithItsWordsAndUndoAndTheRefusalIsNamed()
    {
        using var pointer = TestMouse.PutBackAfterwards();
        using var tray = new WindowsTray();
        var screen = new RecordedMarks(null);
        var named = new List<Failure>();
        var notifications = new Notices(new MovableClock());
        var notification = TrayDesktop.Get(() => new Notification(notifications, new WindowsQuietWindows(tray), screen, named.Add));
        var undone = 0;
        TestMouse.MoveTo(TestMouse.Away);
        // Twice: the window closes with its last notice, and the next notice's new window is asked to be marked too.
        for (var round = 1; round <= 2; round++)
        {
            TrayDesktop.Run(() => notifications.Raise(Copied, "Undo", () =>
            {
                undone++;
                return Task.FromResult(Outcome.Done);
            }));
            await Task.Delay(500);
            var (shown, handle, undo) = TrayDesktop.Get(() => (notification.Shown!, notification.Shown!.TryGetPlatformHandle()!.Handle,
                notification.Shown!.GetVisualDescendants().OfType<Button>().Single(button => button.Content as string == "Undo")));

            Assert.IsTrue(IsWindowVisible(handle), $"round {round}: shown anyway (G35)");
            Assert.AreEqual(Copied, FrontWindow.NativeText(handle), "with its words");
            Assert.HasCount(round, screen.Asked);
            Assert.AreSame(shown, screen.Asked[^1].Window, "the window shown is the one asked to be marked");
            Assert.IsFalse(screen.Asked[^1].Showing, "asked before its first showing");
            Assert.HasCount(round, named, "each refusal named");
            Assert.AreEqual(("Hide a window from captures", Copied, RecordedMarks.Refusal), (named[^1].Action, named[^1].Subject, named[^1].Reason));
            TestMouse.Click(TestMouse.Centre(undo));
            await ShownWindow.Eventually(() => TrayDesktop.Get(() => undone == round && notification.Shown is null));
        }

        Assert.AreNotSame(screen.Asked[0].Window, screen.Asked[1].Window, "each showing is a new window");
    }

    [TestMethod]
    public async Task ATooltipOverItIsLeftOutOfCapturesFromBeforeItsFirstShowing()
    {
        using var pointer = TestMouse.PutBackAfterwards();
        using var tray = new WindowsTray();
        var screen = new RecordedMarks(new WindowsScreen());
        var named = new List<Failure>();
        var notifications = new Notices(new MovableClock());
        var notification = TrayDesktop.Get(() => new Notification(notifications, new TrayIconMidScreen(new WindowsQuietWindows(tray)), screen, named.Add));
        // Under the notification and wherever its tooltip shows.
        using var pattern = PatternWindow.Show(new PixelRect(400, 320, 560, 320));
        await pattern.OnScreen(screen);
        TestMouse.MoveTo(TestMouse.Away);
        Border? card = null;
        TextBlock? tip = null;
        TrayDesktop.Run(() => notifications.Raise(Copied, card: (notice, _) =>
        {
            card = new Border { Child = new TextBlock { Text = notice.Text }, Padding = new Thickness(14), Background = Brushes.Transparent };
            tip = new TextBlock { Text = "A tooltip of the check's own" };
            ToolTip.SetTip(card, tip);
            return card;
        }));
        await Task.Delay(500);

        TestMouse.MoveTo(TestMouse.Centre(card!));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => ToolTip.GetIsOpen(card!)));
        await Task.Delay(300);
        var tooltip = TrayDesktop.Get(() => (PopupRoot)TopLevel.GetTopLevel(tip)!);
        var handle = TrayDesktop.Get(() => tooltip.TryGetPlatformHandle()!.Handle);
        Assert.IsTrue(GetWindowRect(handle, out var edges));
        var bounds = new PixelRect(edges.Left, edges.Top, edges.Right - edges.Left, edges.Bottom - edges.Top);
        var grab = screen.Grab(bounds, withPointer: false);
        var asked = TrayDesktop.Get(() => screen.Asked.Where(mark => ReferenceEquals(mark.Window, tooltip)).ToList());

        Assert.IsTrue(TrayDesktop.Get(() => tooltip.ParentTopLevel == notification.Shown), "the tooltip is the notification's own window");
        Assert.IsTrue(IsWindowVisible(handle), "the tooltip shows");
        Assert.IsTrue(pattern.Bounds.Contains(bounds), $"over the pattern: {bounds}");
        Assert.IsTrue(grab.Succeeded, grab.Failure?.Sentence);
        Assert.IsEmpty(pattern.Differences(grab.Value!, bounds), "the tooltip is not in a grab (S4)");
        Assert.HasCount(1, asked, "marked once");
        Assert.IsFalse(asked[0].Showing, "before its first showing");
        Assert.IsEmpty(named, "the platform marked both");
        TestMouse.MoveTo(TestMouse.Away);
        TrayDesktop.Run(() => notifications.Close(notifications.Shown.Single()));
    }

    private static PixelRect Outer(Avalonia.Controls.Window window) =>
        new(window.Position, PixelSize.FromSize(window.ClientSize, window.RenderScaling));

    private static int Cards(Notification notification) => TrayDesktop.Get(() =>
        notification.Shown?.GetVisualDescendants().OfType<TextBlock>().Count(text => text.Text == Copied) ?? 0);

    /// <summary>A clock a check moves by hand, so a notice's six seconds pass when the check says.</summary>
    private sealed class MovableClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    /// <summary>
    /// The screen, recording every window it was asked to mark and whether Windows showed it then; the mark is the real
    /// seam's, or, with none, refused. UI thread only.
    /// </summary>
    private sealed class RecordedMarks(WindowsScreen? real) : IScreen
    {
        public const string Refusal = "the platform refused it";

        public List<(TopLevel Window, bool Showing)> Asked { get; } = [];

        public IReadOnlyList<Display> Displays() => real?.Displays() ?? throw new NotSupportedException("A notification reads no display through the screen.");

        public Outcome<MediaFrame> Grab(PixelRect rectangle, bool withPointer) => real?.Grab(rectangle, withPointer) ?? throw new NotSupportedException("A notification grabs nothing.");

        public Outcome ExcludeFromCapture(TopLevel window)
        {
            Asked.Add((window, IsWindowVisible(window.TryGetPlatformHandle()!.Handle)));
            return real?.ExcludeFromCapture(window) ?? new Failure("Hide a window from captures", (window as Avalonia.Controls.Window)?.Title ?? "", Refusal);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Edges
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, out Edges edges);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint GetWindowLongPtrW(nint window, int index);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint window);
}
