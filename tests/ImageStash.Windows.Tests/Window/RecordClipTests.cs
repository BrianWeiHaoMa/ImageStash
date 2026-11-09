using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using ImageStash.Capture;
using ImageStash.Files;
using ImageStash.Media;
using ImageStash.Seams;
using ImageStash.Store;
using ImageStash.Window;
using ImageStash.Windows.Seams;
using ImageStash.Windows.Tests.Support;
using static ImageStash.Windows.Tests.Support.ShownWindow;

namespace ImageStash.Windows.Tests.Window;

/// <summary>
/// Record a clip over the real screen, overlay, frame and bar, Media's GIF encoder, the clipboard and the notifications,
/// the stash window hidden as a person has it while typing in a chat (G15): a real drag's release over a counter of the
/// check's own leaves the region waiting, the frame without REC and the bar offering Record and Cancel, in no grab, the
/// chat keeping the keystrokes and Esc reaching it without ending the wait, the tray reading Start recording; a real
/// click on Record records it live from then, each frame lasting a whole number of 70 ms samples, how many measured
/// since a loaded PC keeps fewer, the clip lasting the time from Record, the counter's values from Record on in its
/// frames and the pointer in them, nothing of a notification raised inside the region, while the chat keeps the
/// keystrokes and Esc reaches the chat without ending it; Stop on the bar saves one GIF item (F3, F12), the clipboard
/// then holds its file alone, no use is recorded (G8,
/// G10), and the capture notification shows its picture, length and size (G16, G24); with the pointer off in the
/// settings none is drawn, and the cap ends a recording at its length counted from Record; over the chat a click inside
/// the region is the chat's while it waits and while it records, a second press starts a waiting recording and a third
/// stops it, and Cancel discards one waiting or recording; the frame and the bar are gone after each (F4); the stash
/// window summoned during a recording over its place is in the frames that follow, as any window inside the region is
/// (G15, G3). Its ways in (F5, G34): a real press of <c>Ctrl+Shift+PrtScn</c>, registered as ImageStash binds its four
/// shortcuts, opens the overlay in record mode, a real press again starts the waiting recording and another stops it;
/// the <c>+</c> menu's Record a clip opens the overlay once the menu has closed, and chosen while a region waits, starts
/// it. These checks show a counter below the primary display's middle, the overlay over the primary display, the frame and the bar
/// around a region over the counter or over the chat at (100, 100), the flyout at the primary display's top right or
/// above the tray with its menu; they drag across it, move the pointer into it, click inside it, on Record, Stop and
/// Cancel, on the <c>+</c> button and its entry, type a letter, Esc and the record shortcut, and read the screen's pixels
/// there.
/// </summary>
[TestClass]
public sealed class RecordClipTests
{
    private const ushort VkEscape = 0x1B;

    public TestContext TestContext { get; set; } = null!;

    private static readonly Shortcut RecordShortcut = Shortcut.Parse("Ctrl+Shift+PrtScn").Value!;

    /// <summary>Where the counter goes: clear of the chat at (100, 100), beneath a notification shown above the stand-in tray icon.</summary>
    private static readonly PixelRect CounterPlace = DisplayPlaces.Counter(DisplayPlaces.Primary());

    [TestMethod]
    public async Task ARealDragOverTheCounterWaitsForRecordThenRecordsEachSampleWithThePointerAndNothingOfANotificationThenStopSavesItCopiedAndAnnounced()
    {
        using var desk = await CaptureDesk.Open(midScreen: true);
        using var counter = CounterWindow.Show(CounterPlace);
        var screen = new WindowsScreen();
        await counter.OnScreen(screen);

        var recording = desk.Record();
        await desk.OverlayInFront();
        var region = await CaptureDesk.Drag(new PixelPoint(CounterPlace.X + 10, CounterPlace.Y + 5), new PixelPoint(CounterPlace.Right - 11, CounterPlace.Bottom - 11));
        await desk.WhileWaiting();
        TrayDesktop.Run(() =>
        {
            Assert.AreEqual(RegionSelection.Label(region), desk.Frame.LabelWords, "the frame without REC while the region waits (G15)");
            Assert.IsNull(desk.Frame.Bar!.StopButton, "no Stop before Record");
            Assert.IsNotNull(desk.Frame.Bar.CancelButton, "Cancel offered while the region waits (G15)");
            CollectionAssert.AreEqual(new[] { "Up to 0:30" }, desk.Frame.Bar.Words.ToList(), "no time or size running before Record");
            Assert.IsTrue(desk.Session.RecordingWaiting && !desk.Session.RecordingSampling);
        });
        CollectionAssert.AreEqual(new[] { RecordingStage.Waiting }, desk.TrayRecording, "Start recording, no red dot, while the region waits (G34, G15)");
        Assert.IsEmpty(Directory.GetFiles(TrayDesktop.Get(() => desk.Shown.Listing.Store!.Folder.Work)), "nothing written while the region waits (G28)");
        desk.ChatKeepsTheKeystrokes("while the region waits, the frame and the bar up");
        var beforeEsc = desk.Chat.Messages().Count;
        TestHotKeys.Tap(VkEscape);
        await Eventually(() => desk.Chat.Messages().Skip(beforeEsc).Contains("WM_KEYDOWN:0x1B"));
        await Task.Delay(300);
        Assert.IsTrue(TrayDesktop.Get(() => desk.Session.RecordingWaiting), "Esc reached the chat and did not end the wait (G15)");
        // The desk's clock, which names captures, moved on a minute while the region waits: a name taken at the release
        // would read the minute before.
        var atTheRelease = AutoName.Recording(desk.Clock.GetLocalNow());
        TrayDesktop.Run(() => desk.Clock.Advance(TimeSpan.FromMinutes(1)));
        var name = AutoName.Recording(desk.Clock.GetLocalNow());
        Assert.AreNotEqual(atTheRelease, name);
        await Eventually(() => desk.Timed.Now - desk.Timed.WaitingAt >= TimeSpan.FromSeconds(2));
        var atRecord = screen.Grab(CounterPlace, withPointer: false);
        var countAtRecord = counter.CountIn(atRecord.Value!, CounterPlace)!.Value;
        desk.ClickRecord();
        await desk.WhileRecording();
        TrayDesktop.Run(() => Assert.AreEqual(RecordingFrame.Label(region), desk.Frame.LabelWords, "REC once it records (G15)"));
        CollectionAssert.AreEqual(new[] { RecordingStage.Waiting, RecordingStage.Sampling }, desk.TrayRecording, "the red dot and Stop recording while it samples (G15)");
        desk.ChatKeepsTheKeystrokes("while the recording samples, the frame and the bar up");
        TrayDesktop.Run(() => desk.Notifications.Raise("A notification of the check's own"));
        await Eventually(() => TrayDesktop.Get(() => desk.Notification.Shown is not null));
        // Placed again once shown, at the next idle (Notification.Show).
        await Task.Delay(500);
        var standing = TrayDesktop.Get(() => new PixelRect(desk.Notification.Shown!.Position, PixelSize.FromSize(desk.Notification.Shown.ClientSize, desk.Notification.Shown.RenderScaling)));
        Assert.IsTrue(region.Intersects(counter.FieldBounds.Intersect(standing)), $"the notification at {standing} stands over the counter's field inside the region {region}");
        var pointerAt = new PixelPoint(CounterPlace.X + 40, CounterPlace.Bottom - 50);
        var dragEnd = new PixelPoint(region.Right - 1, region.Bottom - 1);
        Assert.IsFalse(Around(pointerAt).Intersects(standing) || Around(dragEnd).Intersects(standing), "the pointer never stands where the notification does");
        TestMouse.MoveTo(pointerAt);
        var pressed = desk.Chat.Messages().Count;
        TestHotKeys.Tap(VkEscape);
        await Eventually(() => desk.Chat.Messages().Skip(pressed).Contains("WM_KEYDOWN:0x1B"));
        await Task.Delay(300);
        Assert.IsTrue(TrayDesktop.Get(() => desk.Session.RecordingSampling), "Esc reached the chat and did not end the recording (G15)");
        await Eventually(() => desk.Timed.Now - desk.Timed.StartedAt >= TimeSpan.FromSeconds(2));
        TestMouse.Click(TestMouse.Centre(TrayDesktop.Get(() => desk.Frame.Bar!.StopButton!)));
        await recording.WaitAsync(TimeSpan.FromSeconds(30));
        await CaptureDesk.NothingOfTheRecordingLeft();

        var item = desk.Items().Single();
        Assert.AreEqual(name, item.Name, "the auto name of Record's moment in local time, not the release's (F12)");
        Assert.AreEqual(name + ".gif", item.FileName);
        Assert.AreEqual((ImageFormat.Gif, region.Width, region.Height, true), (item.Facts.Format, item.Facts.Width, item.Facts.Height, item.Facts.Animated), "an animated GIF of the region (F3)");
        var recorded = (desk.Timed.StoppedAt - desk.Timed.StartedAt)!.Value.TotalMilliseconds;
        var frames = CaptureDesk.Frames(item.FilePath);
        Assert.HasCount(item.Facts.FrameCount, frames, "every image the facts count decodes");
        // A PC that cannot sample and encode at F3's rate skips samples and keeps each frame until the next, so the clip
        // lasts the recorded time at a lower rate (G15): how many frames, their average delay and the counter's steps
        // between them are measured here, not bounded; every kept frame still sits on F3's 70 ms timeline, so each delay
        // but the last is a whole number of samples however loaded the PC.
        var delays = frames.SkipLast(1).Select(frame => frame.DurationMs).ToList();
        TestContext.WriteLine($"{frames.Count} frames in {recorded:F0} ms; delays but the last {string.Join(", ", delays)}, average {(delays.Count > 0 ? delays.Average() : 0):F1} ms");
        Assert.IsTrue(delays.All(delay => delay > 0 && delay % 70 == 0), $"each frame lasts until the next kept 70 ms sample (F3, G15): {string.Join(", ", delays)}");
        Assert.IsLessThanOrEqualTo(70, Math.Abs(item.Facts.DurationMs - recorded), $"the clip lasts the time from Record within one sample, the wait not counted in: {item.Facts.DurationMs} ms against {recorded} ms (G15)");
        var counts = frames.Select(frame => counter.CountIn(frame, region)).ToList();
        Assert.IsTrue(counts.All(count => count is not null), $"every frame shows a whole count: {string.Join(", ", counts)}");
        TestContext.WriteLine($"the counter in each frame: {string.Join(", ", counts)}");
        Assert.IsGreaterThanOrEqualTo(countAtRecord, counts[0]!.Value, $"the first frame is the counter at Record or later, not at the release: {counts[0]} against {countAtRecord} (G15)");
        Assert.IsTrue(counts.Zip(counts.Skip(1)).All(pair => pair.Second >= pair.First) && counts[^1] > counts[0],
            $"the counter's successive values, live, never going back (G15): {string.Join(", ", counts)}");
        var withPointer = frames.Count(frame => counter.FieldDifferences(frame, region, Around(pointerAt)).Count >= 20);
        Assert.IsGreaterThanOrEqualTo(1, withPointer, "the pointer drawn where it stood, as Settings has it by default (G15)");
        var notificationSeen = frames.Select((frame, index) => (index, counter.FieldDifferences(frame, region, standing))).Where(found => found.Item2.Count > 0).ToList();
        Assert.IsEmpty(notificationSeen, $"nothing of the notification in any frame (G15, S4): {string.Join("; ", notificationSeen.Take(3).Select(found => $"frame {found.index}: {string.Join(", ", found.Item2.Take(3))}"))}");
        var read = TestClipboard.Read();
        CollectionAssert.AreEqual(new[] { item.FilePath }, read.Files.ToList(), "the file (G8)");
        Assert.IsNull(read.Png, "an animation goes as a file only (G8)");
        Assert.IsNull(read.DibV5);
        Assert.AreEqual(0L, desk.Shown.Uses(), "the capture records no use (G10)");
        var card = await desk.CardShown(name);
        var notice = TrayDesktop.Get(() => desk.Notifications.Shown.Single(each => each.Text.StartsWith("Saved")));
        Assert.AreEqual($"Saved “{name}” to your stash. It is on the clipboard too. It is {ClipLength.Words(item.Facts.DurationMs)} long.", notice.Text, "in the stash, on the clipboard, with its length (G16)");
        await Eventually(() => TrayDesktop.Get(() => card.Size) == FileSize.Words(new FileInfo(item.FilePath).Length));
        TrayDesktop.Run(() => Assert.IsNotNull(card.Picture, "the card shows the picture (G16)"));
        CollectionAssert.AreEqual(new[] { RecordingStage.Waiting, RecordingStage.Sampling, RecordingStage.None }, desk.TrayRecording, "the red dot gone at Stop (G15)");
        desk.ChatKeepsTheKeystrokes("after the recording");
    }

    [TestMethod]
    public async Task WithThePointerOffInTheSettingsNoneIsDrawnAndTheCapEndsTheRecordingAtItsLengthFromRecord()
    {
        using var desk = await CaptureDesk.Open();
        TrayDesktop.Run(() => desk.Shown.Settings.Change(settings => settings with { RecordPointer = false, RecordingMaxSeconds = 2 }));
        using var counter = CounterWindow.Show(CounterPlace);
        await counter.OnScreen(new WindowsScreen());

        var recording = desk.Record();
        await desk.OverlayInFront();
        var region = await CaptureDesk.Drag(new PixelPoint(CounterPlace.X + 10, CounterPlace.Y + 5), new PixelPoint(CounterPlace.Right - 11, CounterPlace.Bottom - 11));
        await desk.WhileWaiting();
        TrayDesktop.Run(() => CollectionAssert.AreEqual(new[] { "Up to 0:02" }, desk.Frame.Bar!.Words.ToList(), "the cap the settings read at the press"));
        // The region waits three seconds, longer than the cap, before Record.
        await Task.Delay(3000);
        Assert.IsTrue(TrayDesktop.Get(() => desk.Session.RecordingWaiting), "the cap does not run while the region waits (G15, G32)");
        desk.ClickRecord();
        await desk.WhileRecording();
        TestMouse.MoveTo(new PixelPoint(CounterPlace.X + 40, CounterPlace.Bottom - 50));
        await recording.WaitAsync(TimeSpan.FromSeconds(30));
        await CaptureDesk.NothingOfTheRecordingLeft();

        var item = desk.Items().Single();
        Assert.AreEqual(2000, item.Facts.DurationMs, "the cap ends it at its length counted from Record (G15, G32)");
        var frames = CaptureDesk.Frames(item.FilePath);
        var drawn = frames.Select((frame, index) => (index, counter.FieldDifferences(frame, region, region))).Where(found => found.Item2.Count > 0).ToList();
        Assert.IsEmpty(drawn, $"no pointer drawn while Settings says so (G15): {string.Join("; ", drawn.Take(3).Select(found => $"frame {found.index}: {string.Join(", ", found.Item2.Take(3))}"))}");
        Assert.IsTrue(TrayDesktop.Get(() => desk.Notifications.Shown.Any(notice => notice.Text.EndsWith("It is 2.0 s long."))), "announced with its length");
        desk.ChatKeepsTheKeystrokes("after the cap");
    }

    [TestMethod]
    public async Task OverTheChatAClickInsideTheRegionIsTheChatsASecondPressStartsAThirdStopsAndCancelDiscardsWaitingOrRecording()
    {
        using var desk = await CaptureDesk.Open();
        var chat = Rectangle(desk.Chat.Window);
        var from = new PixelPoint(chat.X + 40, chat.Y + 60);
        var to = new PixelPoint(chat.X + 300, chat.Y + 200);

        var recording = desk.Record();
        await desk.OverlayInFront();
        var region = await CaptureDesk.Drag(from, to);
        await desk.WhileWaiting();
        await ClickInsideIsTheChats(desk, region, "while the region waits");
        await Task.Delay(500);
        await desk.Record();
        await desk.WhileRecording();
        TrayDesktop.Run(() => Assert.IsEmpty(desk.Overlay.Shown, "the second press started the waiting recording, opening no second overlay (F5)"));
        await ClickInsideIsTheChats(desk, region, "while it records");
        await Task.Delay(500);
        await desk.Record();
        await recording.WaitAsync(TimeSpan.FromSeconds(30));
        await CaptureDesk.NothingOfTheRecordingLeft();

        var item = desk.Items().Single();
        Assert.IsTrue(item.Name.StartsWith("Recording ", StringComparison.Ordinal) && item.Facts.Format == ImageFormat.Gif, "the third press stopped it and it was saved (G15)");
        desk.ChatKeepsTheKeystrokes("after the third press");

        var shown = TrayDesktop.Get(() => desk.Notifications.Shown.Count);
        foreach (var started in new[] { false, true })
        {
            var cancelled = desk.Record();
            await desk.OverlayInFront();
            await CaptureDesk.Drag(from, to);
            if (started)
            {
                await desk.RecordFromTheWait();
            }
            else
            {
                await desk.WhileWaiting();
            }

            await Task.Delay(500);
            TestMouse.Click(TestMouse.Centre(TrayDesktop.Get(() => desk.Frame.Bar!.CancelButton!)));
            await cancelled.WaitAsync(TimeSpan.FromSeconds(30));
            await CaptureDesk.NothingOfTheRecordingLeft();

            var when = started ? "while it records" : "while the region waits";
            Assert.AreEqual(item.Id, desk.Items().Single().Id, $"Cancel {when} discards it (G15)");
            Assert.AreEqual(shown, TrayDesktop.Get(() => desk.Notifications.Shown.Count), "and says nothing (E3)");
            Assert.IsEmpty(Directory.GetFiles(TrayDesktop.Get(() => desk.Shown.Listing.Store!.Folder.Work)), "no clip left in .work");
            desk.ChatKeepsTheKeystrokes($"after Cancel {when}");
        }

        CollectionAssert.AreEqual(
            new[] { RecordingStage.Waiting, RecordingStage.Sampling, RecordingStage.None, RecordingStage.Waiting, RecordingStage.None, RecordingStage.Waiting, RecordingStage.Sampling, RecordingStage.None },
            desk.TrayRecording, "the tray back to Record a clip after each, a cancelled wait included (G34)");
    }

    [TestMethod]
    public async Task ACancelClickedAsTheCapEndsTheSamplingStillDiscardsTheClip()
    {
        using var desk = await CaptureDesk.Open();
        TrayDesktop.Run(() => desk.Shown.Settings.Change(settings => settings with { RecordingMaxSeconds = 1 }));
        var chat = Rectangle(desk.Chat.Window);

        var recording = desk.Record();
        await desk.OverlayInFront();
        await CaptureDesk.Drag(new PixelPoint(chat.X + 40, chat.Y + 60), new PixelPoint(chat.X + 300, chat.Y + 200));
        await desk.RecordFromTheWait();
        // The UI thread held until the cap has ended the sampling, so the session's answer to the cap waits in the
        // queue; then a click on Cancel, which the bar still offers, comes first, as a person's click can.
        var offered = TrayDesktop.Get(() =>
        {
            var sampling = ((Recorder)desk.Timed.Readings!).SamplingEnded;
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (!sampling.IsCompleted && DateTime.UtcNow < deadline)
            {
                Thread.Sleep(5);
            }

            Thread.Sleep(100);
            var cancel = desk.Frame.Bar?.CancelButton;
            cancel?.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            return sampling.IsCompleted && cancel is not null;
        });
        await recording.WaitAsync(TimeSpan.FromSeconds(30));
        await CaptureDesk.NothingOfTheRecordingLeft();

        Assert.IsTrue(offered, "the cap ended the sampling while the bar still offered Cancel, and Cancel was clicked");
        Assert.IsEmpty(desk.Items(), "Cancel, clicked while the bar offered it as the cap ended the recording, discards it (G15)");
        Assert.IsEmpty(TrayDesktop.Get(() => desk.Notifications.Shown.ToList()), "and says nothing (E3)");
        Assert.IsEmpty(Directory.GetFiles(TrayDesktop.Get(() => desk.Shown.Listing.Store!.Folder.Work)), "no clip left in .work");
    }

    [TestMethod]
    public async Task ARealPressOfTheRecordShortcutOverTheChatOpensTheOverlayInRecordModeAndARealPressAgainStopsAndSavesIt()
    {
        using var desk = await CaptureDesk.Open();
        using var pressed = CaptureShortcuts.Register(desk);
        Assert.IsEmpty(pressed.Named(), "all four registered; another program, or a running ImageStash, holds none");
        var chat = Rectangle(desk.Chat.Window);
        Assert.AreEqual(desk.Chat.Window, FrontWindow.GetForegroundWindow(), "the chat in front at the press");
        var displays = new WindowsScreen().Displays().Count;

        TestHotKeys.Press(RecordShortcut);
        await Eventually(() => TrayDesktop.Get(() => desk.Overlay.Shown.Count == displays));
        TrayDesktop.Run(() => Assert.IsTrue(desk.Overlay.Shown.All(window => window.Title == RegionOverlay.RecordingTitle), "the overlay in record mode over every display (G15)"));
        await desk.OverlayInFront();
        await CaptureDesk.Drag(new PixelPoint(chat.X + 40, chat.Y + 60), new PixelPoint(chat.X + 300, chat.Y + 200));
        await desk.WhileWaiting();
        await Task.Delay(1000);
        Assert.IsTrue(TrayDesktop.Get(() => desk.Session.RecordingWaiting), "the release left it waiting");
        TestHotKeys.Press(RecordShortcut);
        await desk.WhileRecording();
        TrayDesktop.Run(() => Assert.IsEmpty(desk.Overlay.Shown, "the press during the wait started it, opening no second overlay (F5, G15)"));
        await Task.Delay(1000);
        TestHotKeys.Press(RecordShortcut);
        await pressed.RecordEnded(1);
        await CaptureDesk.NothingOfTheRecordingLeft();

        Assert.AreEqual(3, pressed.Recorded, "each press reached the act");
        var item = desk.Items().Single();
        Assert.IsTrue(item.Name.StartsWith("Recording ", StringComparison.Ordinal) && item.Facts is { Format: ImageFormat.Gif, Animated: true },
            $"the shortcut again started it and once more stopped it, and the clip was saved (G15, F5): {item.Name}, {item.Facts}");
        CollectionAssert.AreEqual(new[] { RecordingStage.Waiting, RecordingStage.Sampling, RecordingStage.None }, desk.TrayRecording, "Start recording while it waited, the red dot while it sampled, gone at the press");
        desk.ChatKeepsTheKeystrokes("after the shortcut stopped the recording");
    }

    [TestMethod]
    public async Task ThePlusMenusRecordAClipOpensTheOverlayInRecordModeOnceTheMenuHasClosedAndEscBringsTheFlyoutBack()
    {
        using var desk = await CaptureDesk.Open(["menu record.png"]);
        var shown = desk.Shown;
        await desk.FlyoutInFront();
        var handle = TrayDesktop.Get(() => shown.Window.TryGetPlatformHandle()!.Handle);
        TestMouse.Click(TestMouse.Centre(TrayDesktop.Get(() => shown.AddButton)));
        var entry = TrayDesktop.Get(() => shown.AddMenu().Single(each => (string)each.Header! == RecordClip.Action));
        await Eventually(() => TrayDesktop.Get(() => TopLevel.GetTopLevel(entry) is PopupRoot { IsVisible: true }));
        await Task.Delay(300);
        TrayDesktop.Run(() => Assert.AreEqual(new KeyGesture(Key.PrintScreen, KeyModifiers.Control | KeyModifiers.Shift), entry.InputGesture, "with its shortcut as the settings read it (F5)"));
        bool? menuUpAtTheRequest = null;
        TrayDesktop.Run(() => shown.Window.RecordRequested += () => menuUpAtTheRequest = TopLevel.GetTopLevel(entry) is PopupRoot { IsVisible: true });

        TestMouse.Click(TestMouse.Centre(entry));
        await desk.OverlayInFront();

        TrayDesktop.Run(() =>
        {
            Assert.IsFalse(menuUpAtTheRequest!.Value, "the act asked for once the menu had closed, so the frozen screen holds nothing of it");
            Assert.IsTrue(desk.Overlay.Shown.All(window => window.Title == RegionOverlay.RecordingTitle), "the overlay in record mode (G15)");
            Assert.IsFalse(shown.Window.IsVisible, "the flyout hidden like any other start (G3)");
        });
        TestHotKeys.Tap(VkEscape);
        await Eventually(() => TrayDesktop.Get(() => desk.Overlay.Shown.Count == 0 && shown.Window.IsActive) && FrontWindow.GetForegroundWindow() == handle);

        Assert.HasCount(1, desk.Items(), "Esc at the overlay adds nothing (E3)");
        TrayDesktop.Run(() => Assert.IsFalse(desk.Session.AnyInProgress));
    }

    [TestMethod]
    public async Task ThePlusMenusRecordAClipChosenWhileARegionWaitsStartsTheRecording()
    {
        using var desk = await CaptureDesk.Open(["menu starts.png"]);
        var shown = desk.Shown;
        var window = shown.Window;
        var chat = Rectangle(desk.Chat.Window);

        var recording = desk.Record();
        await desk.OverlayInFront();
        await CaptureDesk.Drag(new PixelPoint(chat.X + 40, chat.Y + 60), new PixelPoint(chat.X + 300, chat.Y + 200));
        await desk.WhileWaiting();
        // Summoned during the wait as usual (G3), above the tray, clear of the region over the chat.
        TrayDesktop.Run(window.SummonFromTray);
        await Eventually(() => TrayDesktop.Get(() => window.IsVisible));
        await Task.Delay(300);
        TestMouse.Click(TestMouse.Centre(TrayDesktop.Get(() => shown.AddButton)));
        var entry = TrayDesktop.Get(() => shown.AddMenu().Single(each => (string)each.Header! == RecordClip.Action));
        await Eventually(() => TrayDesktop.Get(() => TopLevel.GetTopLevel(entry) is PopupRoot { IsVisible: true }));
        await Task.Delay(300);
        Assert.IsTrue(TrayDesktop.Get(() => desk.Session.RecordingWaiting), "still waiting with the menu open");
        TestMouse.Click(TestMouse.Centre(entry));
        await desk.WhileRecording();

        TrayDesktop.Run(() => Assert.IsEmpty(desk.Overlay.Shown, "the entry started the waiting recording, opening no second overlay (G15)"));
        await Task.Delay(700);
        TestMouse.Click(TestMouse.Centre(TrayDesktop.Get(() => desk.Frame.Bar!.StopButton!)));
        await recording.WaitAsync(TimeSpan.FromSeconds(30));
        await CaptureDesk.NothingOfTheRecordingLeft();

        Assert.HasCount(2, desk.Items(), "the clip saved beside the earlier item");
        CollectionAssert.AreEqual(new[] { RecordingStage.Waiting, RecordingStage.Sampling, RecordingStage.None }, desk.TrayRecording);
    }

    [TestMethod]
    public async Task TheStashWindowSummonedDuringARecordingOverItsPlaceIsInTheFramesThatFollow()
    {
        using var desk = await CaptureDesk.Open(["summoned while recorded.png"]);
        var window = desk.Shown.Window;
        // The open shortcut's summon with no caret found puts the flyout at the pointer (G1); its place read first.
        var pointer = new SummonAnchor(DisplayPlaces.At(DisplayPlaces.Primary(), 300, 200));
        TrayDesktop.Run(() => window.SummonFromShortcut(default, pointer));
        await Eventually(() => TrayDesktop.Get(() => window.IsVisible));
        await Task.Delay(300);
        var flyout = TrayDesktop.Get(() => new PixelRect(window.Position, PixelSize.FromSize(window.FrameSize!.Value, window.RenderScaling)));
        var area = TrayDesktop.Get(() => window.Screens.Primary!.WorkingArea);
        TrayDesktop.Run(window.Hide);
        desk.Chat.ClickTitleBar();
        var from = new PixelPoint(Math.Max(area.X + 4, flyout.X - 20), Math.Max(area.Y + 4, flyout.Y - 20));
        var to = new PixelPoint(Math.Min(area.Right - 5, flyout.Right + 20), Math.Min(area.Bottom - 5, flyout.Bottom + 20));

        var recording = desk.Record();
        await desk.OverlayInFront();
        var region = await CaptureDesk.Drag(from, to);
        await desk.RecordFromTheWait();
        await Task.Delay(700);
        TrayDesktop.Run(() => window.SummonFromShortcut(default, pointer));
        await Eventually(() => TrayDesktop.Get(() => window.IsVisible));
        await Task.Delay(700);
        var summoned = TrayDesktop.Get(() => new PixelRect(window.Position, PixelSize.FromSize(window.FrameSize!.Value, window.RenderScaling)));
        await desk.Record();
        await recording.WaitAsync(TimeSpan.FromSeconds(30));
        await CaptureDesk.NothingOfTheRecordingLeft();

        Assert.AreEqual(flyout, summoned, "summoned during the recording where it stood before (G1, G3)");
        TrayDesktop.Run(() => Assert.IsTrue(window.IsVisible, "and left showing at the recording's end, the summon having taken it back (G3)"));
        var frames = CaptureDesk.Frames(desk.Items().Single(item => item.Name.StartsWith("Recording ", StringComparison.Ordinal)).FilePath);
        var inside = summoned.Intersect(region);
        var changed = 0;
        for (var y = inside.Y; y < inside.Bottom; y++)
        {
            for (var x = inside.X; x < inside.Right; x++)
            {
                var at = ((y - region.Y) * region.Width + (x - region.X)) * 4;
                if (Enumerable.Range(0, 3).Any(channel => Math.Abs(frames[0].Pixels[at + channel] - frames[^1].Pixels[at + channel]) > 24))
                {
                    changed++;
                }
            }
        }

        // What lies there may be as light as the window, so its controls alone differ; a window left out of the grab
        // would leave the last frame what lay beneath, the first.
        Assert.IsGreaterThan(inside.Width * inside.Height / 20, changed,
            $"the last frame shows the stash window where the first showed what lay there before, as any window inside the region is recorded (G15): {changed} of {inside.Width * inside.Height} pixels changed in {inside}");
    }

    /// <summary>A real click inside the region is the chat's own, the chat in front with its keystrokes after it.</summary>
    private static async Task ClickInsideIsTheChats(CaptureDesk desk, PixelRect region, string when)
    {
        var pressed = desk.Chat.Messages().Count;
        TestMouse.Click(region.Center);
        await Eventually(() => desk.Chat.Messages().Skip(pressed).Contains("WM_LBUTTONDOWN:0x1"));
        Assert.AreEqual(desk.Chat.Window, FrontWindow.GetForegroundWindow(), $"a click inside the region {when} is the chat's own: the screen stays live and clickable (G15)");
        await Eventually(() => desk.Chat.Messages().Skip(pressed).Contains("WM_LBUTTONUP:0x0"));
        desk.ChatKeepsTheKeystrokes($"after a click inside the region {when}");
    }

    /// <summary>A box around a point where the pointer may stand, its arrow drawn down and right of it.</summary>
    private static PixelRect Around(PixelPoint point) => new(point.X - 4, point.Y - 4, 40, 44);

    private static PixelRect Rectangle(nint window)
    {
        Assert.IsTrue(GetWindowRect(window, out var rectangle));
        return new PixelRect(rectangle.Left, rectangle.Top, rectangle.Right - rectangle.Left, rectangle.Bottom - rectangle.Top);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRectangle
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, out NativeRectangle rectangle);
}
