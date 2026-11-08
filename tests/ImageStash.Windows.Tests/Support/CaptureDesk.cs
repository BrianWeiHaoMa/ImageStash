using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using ImageStash.Capture;
using ImageStash.Files;
using ImageStash.Media;
using ImageStash.Seams;
using ImageStash.Store;
using ImageStash.Window;
using ImageStash.Windows.Seams;

namespace ImageStash.Windows.Tests.Support;

/// <summary>
/// A stash window of the check's own, hidden as it is while a person types in a chat, its stash holding the given
/// pictures; the notifications above the tray on a clock the check moves, shown through the real Quiet-windows seam,
/// wired to the stash window as ImageStash wires them, with the capture notification over them; Take a screenshot and
/// Record a clip as ImageStash makes them, over one capture session on the real screen, the real overlay, the real
/// recording frame and bar and Media's GIF encoder, the stash window's own hiding and handoff, on the desk's clock, both
/// started by the stash window's own menu as ImageStash wires them; and a chat window of the check's own in front.
/// </summary>
public sealed class CaptureDesk : IDisposable
{
    private const uint WmClose = 0x0010;
    private readonly IDisposable pointer;
    private readonly WindowsTray tray;
    private readonly List<RecordingStage> trayRecording = [];

    private static readonly TimeSpan PersonsPause = TimeSpan.FromMilliseconds(300);

    private CaptureDesk(IDisposable pointer, ShownWindow shown, WindowsTray tray, StoppedClock clock, Notices notifications, Notification notification, CaptureNotification capture, TargetApp chat)
    {
        this.pointer = pointer;
        Shown = shown;
        this.tray = tray;
        Clock = clock;
        Notifications = notifications;
        Notification = notification;
        Capture = capture;
        Chat = chat;
        Frame = TrayDesktop.Get(() => new RecordingFrame(new WindowsQuietWindows(tray), new WindowsScreen(), failure => notifications.Raise(failure)));
        Timed = new TimedFrame(Frame);
        Session = TrayDesktop.Get(() => new CaptureSession(new WindowsScreen(), Overlay, Timed, GifEncoder.Opener(HeldFileRetry.Duration), clock, failure => notifications.Raise(failure)));
        Screenshot = TrayDesktop.Get(() => new Screenshot(shown.Listing, notifications, capture, shown.Handoff, Session, shown.Settings, shown.Window.CaptureHiding, shown.Window, HeldFileRetry.Duration));
        RecordClip = TrayDesktop.Get(() => new RecordClip(shown.Listing, notifications, capture, shown.Handoff, Session, shown.Settings, shown.Window.CaptureHiding, shown.Window,
            trayRecording.Add, HeldFileRetry.Duration));
        TrayDesktop.Run(() =>
        {
            shown.Window.ScreenshotRequested += () => _ = Screenshot.Take();
            shown.Window.RecordRequested += () => _ = RecordClip.StartOrStop();
        });
    }

    public ShownWindow Shown { get; }

    public StoppedClock Clock { get; }

    public Notices Notifications { get; }

    public Notification Notification { get; }

    public CaptureNotification Capture { get; }

    public TargetApp Chat { get; }

    /// <summary>The screenshot's and the recording's overlay, whose windows the checks look at and drag across.</summary>
    public RegionOverlay Overlay { get; } = new();

    /// <summary>The recording's frame and bar, whose windows and buttons the checks look at and click.</summary>
    public RecordingFrame Frame { get; }

    /// <summary>When the frame took its recording look, the recorder started, and when the bar's Stop was pressed.</summary>
    public TimedFrame Timed { get; }

    public CaptureSession Session { get; }

    public Screenshot Screenshot { get; }

    public RecordClip RecordClip { get; }

    /// <summary>What the record act told the tray at each change of the recording's stage, in order: Start recording while waiting, the red dot and Stop recording while sampling.</summary>
    public List<RecordingStage> TrayRecording => TrayDesktop.Get(() => trayRecording.ToList());

    public nint NotificationHandle => TrayDesktop.Get(() => Notification.Shown!.TryGetPlatformHandle()!.Handle);

    /// <param name="activations">The seam the card takes the activation through, instead of the one that shows the notification.</param>
    /// <param name="midScreen">The notifications shown above <see cref="TrayIconMidScreen.Icon"/> instead of the tray.</param>
    public static async Task<CaptureDesk> Open(string[]? pictures = null, IQuietWindows? activations = null, TimeSpan? heldFileRetry = null, ITarget? platformTarget = null, bool midScreen = false)
    {
        var pointer = TestMouse.PutBackAfterwards();
        var shown = ShownWindow.Open(platformTarget: platformTarget);
        await shown.OpenStash((pictures ?? []).Select(shown.Picture).ToArray());
        var tray = new WindowsTray();
        var real = new WindowsQuietWindows(tray);
        IQuietWindows quiet = midScreen ? new TrayIconMidScreen(real) : real;
        var clock = new StoppedClock();
        var notifications = new Notices(clock);
        var (notification, capture) = TrayDesktop.Get(() =>
        {
            var notification = new Notification(notifications, quiet, new WindowsScreen(), _ => { });
            var capture = new CaptureNotification(notifications, shown.Listing, shown.Handoff, activations ?? quiet, heldFileRetry ?? HeldFileRetry.Duration);
            shown.Window.Follow(notification, capture);
            shown.Window.Hide();
            return (notification, capture);
        });
        var chat = TargetApp.Start("ImageStash check chat", 100, 100);
        chat.ClickTitleBar();
        return new CaptureDesk(pointer, shown, tray, clock, notifications, notification, capture, chat);
    }

    public List<StashItem> Items() => TrayDesktop.Get(() => Shown.Listing.Store!.All()).GetAwaiter().GetResult().Value!.OrderBy(item => item.Id).ToList();

    /// <summary>Imports the picture at <paramref name="path"/> as a drop would, its notice closed, and answers the item it added.</summary>
    public async Task<StashItem> Import(string path)
    {
        var added = await TrayDesktop.Get(() => Shown.Import.Drop(new DropContents([new DroppedItem(Path.GetFileName(path), path, null, IsFolder: false)], false, false, [])));
        TrayDesktop.Run(() =>
        {
            foreach (var notice in Shown.Notices.Shown.ToList())
            {
                Shown.Notices.Close(notice);
            }
        });
        return added.Single().Item!;
    }

    /// <summary>Raises the item's capture notification, as a stashed clipboard image's unless <paramref name="kind"/> says otherwise, and waits until its card is placed above the tray.</summary>
    public async Task<CaptureCard> Raise(StashItem item, CaptureKind kind = CaptureKind.StashedClipboardImage)
    {
        TrayDesktop.Run(() => Capture.Raise(item, kind, onClipboard: false, lengthMs: null));
        var card = await CardShown(item.Name);
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => card.Size is not null));
        return card;
    }

    /// <summary>Raises a plain notification and waits until it is placed above the tray; its words, a click on which takes no focus (G16).</summary>
    public async Task<TextBlock> RaisePlain()
    {
        const string said = "Copied · press Ctrl+V";
        TrayDesktop.Run(() => Notifications.Raise(said));
        TextBlock? words = null;
        await ShownWindow.Eventually(() => (words = TrayDesktop.Get(() => Notification.Shown?.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(text => text.Text == said))) is not null);
        // Placed again once shown, at the next idle (Notification.Show).
        Thread.Sleep(500);
        return words!;
    }

    /// <summary>The card showing <paramref name="name"/>, or the newest, once it is in the notification; then until it is placed.</summary>
    public async Task<CaptureCard> CardShown(string? name = null)
    {
        CaptureCard? card = null;
        await ShownWindow.Eventually(() => (card = TrayDesktop.Get(() => Notification.Shown?.GetVisualDescendants().OfType<CaptureCard>()
            .LastOrDefault(shown => name is null || shown.Field.Shown == name))) is not null);
        // Placed again once shown, at the next idle (Notification.Show).
        Thread.Sleep(500);
        return card!;
    }

    /// <summary>Starts the act on the UI thread, as each way in runs it; the task ends when the act has.</summary>
    public Task Take() => TrayDesktop.Get(Screenshot.Take);

    /// <summary>Presses Record a clip on the UI thread, as each way in runs it; the task ends when the act has.</summary>
    public Task Record() => TrayDesktop.Get(RecordClip.StartOrStop);

    /// <summary>
    /// Until the frame and the bar are on the screen with the released region waiting, the bar offering Record; then a
    /// moment more, as a person's first input comes.
    /// </summary>
    public async Task WhileWaiting()
    {
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => Session.RecordingWaiting && Frame.FrameWindow is { IsVisible: true } && Frame.BarWindow is { IsVisible: true } && Frame.Bar?.RecordButton is not null));
        await Task.Delay(PersonsPause);
    }

    /// <summary>Until the frame and the bar are on the screen and the recording samples; then a moment more, as a person's first input comes.</summary>
    public async Task WhileRecording()
    {
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => Session.RecordingSampling && Frame.FrameWindow is { IsVisible: true } && Frame.BarWindow is { IsVisible: true }));
        await Task.Delay(PersonsPause);
    }

    /// <summary>A real click on the waiting bar's Record.</summary>
    public void ClickRecord() => TestMouse.Click(TestMouse.Centre(TrayDesktop.Get(() => Frame.Bar!.RecordButton!)));

    /// <summary>Until the region waits, then a real click on the bar's Record, then until the recording samples.</summary>
    public async Task RecordFromTheWait()
    {
        await WhileWaiting();
        ClickRecord();
        await WhileRecording();
    }

    /// <summary>Until neither the frame nor the bar has a window any more (F4).</summary>
    public static Task NothingOfTheRecordingLeft() =>
        ShownWindow.Eventually(() => FindWindowW(null, RecordingFrame.Title) == 0 && FindWindowW(null, RecordingBar.Title) == 0);

    /// <summary>Every frame of the clip at <paramref name="path"/>, as Media decodes it, at full size, each with its duration.</summary>
    public static List<MediaFrame> Frames(string path)
    {
        using var decoder = MediaDecoder.Open(path, Path.GetFileName(path), HeldFileRetry.Duration).Value!;
        var frames = new List<MediaFrame>();
        while (decoder.NextFrame(Math.Max(decoder.Width, decoder.Height)).Value is { } frame)
        {
            frames.Add(frame);
        }

        return frames;
    }

    /// <summary>
    /// The overlay up over the primary display and one of its windows in front, within a few seconds; then a moment more,
    /// as a person's first input comes, since one sent the instant the overlay came to the front was seen to reach nothing.
    /// </summary>
    public async Task OverlayInFront()
    {
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => Overlay.Shown.Select(window => window.TryGetPlatformHandle()!.Handle).ToList()).Contains(FrontWindow.GetForegroundWindow()));
        await Task.Delay(PersonsPause);
    }

    /// <summary>A real drag across the overlay from <paramref name="from"/> to <paramref name="to"/>: the rectangle it selects, both corners in, as Windows placed the pointer.</summary>
    public static async Task<PixelRect> Drag(PixelPoint from, PixelPoint to)
    {
        TestMouse.Press(from);
        var pressed = TestMouse.Position();
        foreach (var step in Enumerable.Range(1, 8))
        {
            TestMouse.MoveTo(new PixelPoint(from.X + (to.X - from.X) * step / 8, from.Y + (to.Y - from.Y) * step / 8));
            await Task.Delay(15);
        }

        var at = TestMouse.Position();
        TestMouse.Release(at);
        return new PixelRect(Math.Min(pressed.X, at.X), Math.Min(pressed.Y, at.Y), Math.Abs(at.X - pressed.X) + 1, Math.Abs(at.Y - pressed.Y) + 1);
    }

    /// <summary>The chat is the window in front and a letter typed now reaches it.</summary>
    public void ChatKeepsTheKeystrokes(string when) => Chat.KeepsTheKeystrokes(when);

    /// <summary>The stash window's client as <see cref="FlyoutInFront"/> shows it on <paramref name="display"/>: its width, at its minimum height, in physical pixels.</summary>
    public PixelSize FlyoutClient(Display display) =>
        TrayDesktop.Get(() => PixelSize.FromSize(new Size(Shown.Window.Width, WindowSize.Minimum.Height), display.Scaling));

    /// <summary>The stash window shown at the top right of the primary display (<see cref="DisplayPlaces.Flyout"/>), at its minimum height, which leaves the notification clear, and clicked to the front.</summary>
    public async Task FlyoutInFront()
    {
        var window = Shown.Window;
        var display = DisplayPlaces.Primary();
        var flyout = DisplayPlaces.Flyout(display, FlyoutClient(display));
        TrayDesktop.Run(() =>
        {
            window.Height = WindowSize.Minimum.Height;
            window.Position = flyout.Position;
            // On a short display a stack of notifications reaches up to the top right.
            if (Notification.Shown is { IsVisible: true } standing && flyout.Intersects(Outer(standing)))
            {
                var margin = DisplayPlaces.Scaled(display, 20);
                window.Position = new PixelPoint(display.WorkingArea.X + margin, display.WorkingArea.Y + margin);
            }

            window.Show();
        });
        Thread.Sleep(300);
        // What the checks rest on: a notification already up is not under the flyout, whatever its minimum height. A pinned
        // window is put back at its own place once its frame settles, and a check that pins it places it itself.
        TrayDesktop.Run(() => Assert.IsFalse(window.Topmost && Notification.Shown is { IsVisible: true } note && Outer(window).Intersects(Outer(note)), $"the flyout at {Outer(window)} clear of the notification"));
        var handle = TrayDesktop.Get(() => window.TryGetPlatformHandle()!.Handle);
        TestMouse.Click(TestMouse.Centre(TrayDesktop.Get(() => Shown.Search)));
        await ShownWindow.Eventually(() => FrontWindow.GetForegroundWindow() == handle && TrayDesktop.Get(() => window.IsActive));
    }

    /// <summary>The window's whole rectangle on the desktop, frame included, in physical pixels.</summary>
    public static PixelRect Outer(Avalonia.Controls.Window window) =>
        new(window.Position, PixelSize.FromSize(window.FrameSize!.Value, window.RenderScaling));

    /// <summary>The stash window, in front, opens its Import files… picker with a real `Ctrl+O`: the picker's handle once it is in front.</summary>
    public async Task<nint> PickerInFront()
    {
        var window = Shown.Window;
        var flyout = TrayDesktop.Get(() => window.TryGetPlatformHandle()!.Handle);
        using var targets = new WindowsTarget();
        nint picker = 0;
        TestHotKeys.Press(new Shortcut(new Avalonia.Input.KeyGesture(Avalonia.Input.Key.O, Avalonia.Input.KeyModifiers.Control), "Ctrl+O"));
        var notification = TrayDesktop.Get(() => Notification.Shown?.TryGetPlatformHandle()?.Handle ?? 0);
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => window.DialogUp) && (picker = FrontWindow.GetForegroundWindow()) != flyout && picker != notification && targets.Foreground().IsMine);
        Thread.Sleep(500);
        return FrontWindow.GetForegroundWindow();
    }

    /// <summary>Closes the picker <see cref="PickerInFront"/> opened, if it is still up.</summary>
    public async Task ClosePicker(nint picker)
    {
        if (picker != 0 && TrayDesktop.Get(() => Shown.Window.DialogUp))
        {
            PostMessageW(picker, WmClose, 0, 0);
            await ShownWindow.Eventually(() => TrayDesktop.Get(() => !Shown.Window.DialogUp));
        }
    }

    public void Dispose()
    {
        TrayDesktop.Run(() =>
        {
            // A check that failed with a recording waiting or running cancels it, so its frame and bar cover no later check.
            Session.CancelRecording();
            // A check that failed with the overlay up closes it, which cancels, so it covers no later check.
            foreach (var window in Overlay.Shown)
            {
                window.Close();
            }

            foreach (var notice in Notifications.Shown.ToList())
            {
                Notifications.Close(notice);
            }
        });
        Chat.Dispose();
        Shown.Dispose();
        tray.Dispose();
        pointer.Dispose();
    }

    /// <summary>The first frame of the picture at <paramref name="path"/>, as Media decodes it, at full size.</summary>
    public static MediaFrame FirstFrame(string path)
    {
        using var decoder = MediaDecoder.Open(path, Path.GetFileName(path), HeldFileRetry.Duration).Value!;
        return decoder.NextFrame(Math.Max(decoder.Width, decoder.Height)).Value!;
    }

    /// <summary>The card's name, the button a click on it edits.</summary>
    public static Button NameButton(CaptureCard card) => card.Field.GetVisualDescendants().OfType<Button>().Single(button => Avalonia.Automation.AutomationProperties.GetName(button) == "Rename");

    /// <summary>The text box of the card's name field, shown or not.</summary>
    public static TextBox NameBox(CaptureCard card) => card.Field.GetVisualDescendants().OfType<TextBox>().Single();

    /// <summary>The card's button for the editor: Edit, or Trim on a recording's card.</summary>
    public static Button EditButton(CaptureCard card) => card.GetVisualDescendants().OfType<Button>().Single(button => button.Content is "Edit" or "Trim");

    /// <summary>The card's Undo.</summary>
    public static Button UndoButton(CaptureCard card) => card.GetVisualDescendants().OfType<Button>().Single(button => button.Content as string == "Undo");

    /// <summary>The ×, named Close, of a card above the tray or inside the window.</summary>
    public static Button CloseButton(Control card) => card.GetVisualDescendants().OfType<Button>().Single(button => Avalonia.Automation.AutomationProperties.GetName(button) == "Close");

    [System.Runtime.InteropServices.DllImport("user32.dll", ExactSpelling = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool PostMessageW(nint window, uint message, nint wParam, nint lParam);

    [System.Runtime.InteropServices.DllImport("user32.dll", ExactSpelling = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern nint FindWindowW(string? className, string windowName);
}

/// <summary>
/// The live frame and bar, as the session shows them, with the moment of each showing of the waiting region, of each
/// recording look, which follows the recorder's start at once, and of each press of the bar's Stop kept on a stopwatch,
/// so a check can tell how long a recording ran from Record.
/// </summary>
public sealed class TimedFrame(RecordingFrame frame) : IRecordingFrame
{
    private readonly Stopwatch clock = Stopwatch.StartNew();

    /// <summary>When the frame last showed a region waiting, on this frame's stopwatch.</summary>
    public TimeSpan? WaitingAt { get; private set; }

    /// <summary>When the frame last took its recording look, at the recorder's start, on this frame's stopwatch.</summary>
    public TimeSpan? StartedAt { get; private set; }

    /// <summary>Now, on this frame's stopwatch.</summary>
    public TimeSpan Now => clock.Elapsed;

    /// <summary>When the bar's Stop was last pressed, on this frame's stopwatch.</summary>
    public TimeSpan? StoppedAt { get; private set; }

    /// <summary>The readings the recording look was last handed: the recorder the session started.</summary>
    public IRecordingReadings? Readings { get; private set; }

    public Outcome Show(SelectedRegion region, TimeSpan cap, Action record, Action stop, Action cancel)
    {
        (WaitingAt, StartedAt, StoppedAt) = (clock.Elapsed, null, null);
        return frame.Show(region, cap, record, () =>
        {
            StoppedAt = clock.Elapsed;
            stop();
        }, cancel);
    }

    public Outcome Recording(IRecordingReadings readings)
    {
        (StartedAt, Readings) = (clock.Elapsed, readings);
        return frame.Recording(readings);
    }

    public void Saving() => frame.Saving();

    public void Close() => frame.Close();
}

/// <summary>The real seam, but its activation refused, as Windows may refuse a process the foreground.</summary>
public sealed class RefusedActivation : IQuietWindows
{
    public void ShowWithoutActivating(Avalonia.Controls.Window window) => throw new NotSupportedException("The notification is shown by the real seam.");

    public Outcome ShowOnly(Avalonia.Controls.Window window, IReadOnlyList<PixelRect> parts) => throw new NotSupportedException("The notification is shown whole by the real seam.");

    public Outcome<IDisposable> TakeActivation(Avalonia.Controls.Window window) => new Failure("Activate the notification", window.Title ?? "", "it could not be brought to the front");

    public void RoundCorners(Avalonia.Controls.Window window) => throw new NotSupportedException("The notification is rounded by the real seam.");

    public PixelRect? TrayIconRectangle() => null;

    public PixelPoint? PointerPosition() => null;

    public PixelRect? FrontWindowRectangle() => null;
}
