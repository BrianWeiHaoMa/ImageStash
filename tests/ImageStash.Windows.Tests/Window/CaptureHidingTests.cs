using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using ImageStash.Media;
using ImageStash.Seams;
using ImageStash.Store;
using ImageStash.Window;
using ImageStash.Windows.Seams;
using ImageStash.Windows.Tests.Support;
using static ImageStash.Windows.Tests.Support.ShownWindow;

namespace ImageStash.Windows.Tests.Window;

/// <summary>
/// The real stash window through a screenshot over the real overlay (G3): a showing flyout hidden while the overlay is
/// up, its frozen grab holding what lies beneath it, and back afterwards where it was, at its size, active, with its
/// query, selection, open preview and paste target as they were (G8); a pinned window minimised meanwhile, its picker
/// up or not, its frozen grab holding what lies beneath it, and restored, active only when it was, so a chat in front
/// keeps its keystrokes; a window already hidden left hidden; a summon during the capture showing the window as a summon
/// does, a minimised pinned one restored (G1), and the capture's end then showing nothing again; a pinned window closed
/// during the capture left closed, the next summon bringing it forward (G2); Take a screenshot chosen from the flyout's
/// own menu hiding it like any other start, the menu closed before the grab and its closing not taken for focus lost
/// to another app (G3, G2); a tooltip of the window, or its menu, open at the press gone before the grab (G3, G14); a
/// notice hovered and a tag list open at the start ending with the hide (G16, F4); a
/// summon while a recording's region waits showing the flyout, a screenshot then hiding it again, and neither Record
/// nor anything before the recording's end, once no capture is in progress, bringing it back (G3).
/// These checks show the stash window on the primary display, the overlay over it, and drag the mouse across it.
/// </summary>
[TestClass]
public sealed class CaptureHidingTests
{
    private const ushort VkEscape = 0x1B;

    /// <summary>Where the pinned window goes in these checks: clear of the chat window at (100, 100), larger than the window's minimum with its frame, inside the working area.</summary>
    // Its position in physical pixels, (560, 260) at 100 %; its size in device-independent units, as the settings keep it.
    private static PixelRect PinnedPlace => new(DisplayPlaces.At(DisplayPlaces.Primary(), 560, 260), new PixelSize(600, 480));

    [TestMethod]
    [DataRow(true, DisplayName = "a drag released")]
    [DataRow(false, DisplayName = "Esc")]
    public async Task AFlyoutIsHiddenWhileTheOverlayIsUpAndComesBackAsItWas(bool released)
    {
        using var desk = await CaptureDesk.Open(["hiding kept one.png", "hiding kept two.png"]);
        var shown = desk.Shown;
        // Inside the flyout's place at the primary display's top right (CaptureDesk.FlyoutInFront).
        var display = DisplayPlaces.Primary();
        var area = DisplayPlaces.UnderFlyout(display, desk.FlyoutClient(display));
        // A pattern of the check's own beneath the flyout, not topmost so the flyout comes in front of it: what lies beneath
        // is still, not a desktop the check does not own, and every pixel of it is compared.
        using var pattern = PatternWindow.Show(area, window => window.Topmost = false);
        await pattern.OnScreen(new WindowsScreen());
        var beneath = await Task.Run(() => new WindowsScreen().Grab(area, withPointer: false).Value!);
        shown.Target.Before = PresetTarget.App("Chat");
        await desk.FlyoutInFront();
        await Eventually(() => TrayDesktop.Get(shown.FooterHints).Contains("Enter · Paste into Chat"));
        // Read again at the restore, the target would be none: the restore is the window's own showing (G8).
        shown.Target.Before = null;
        TrayDesktop.Run(() => shown.Search.Text = "hiding");
        await Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled && shown.Listing.Items.Count == 2));
        var kept = TrayDesktop.Get(() => shown.Listing.Items[1]);
        TrayDesktop.Run(() => shown.OpenPreview(kept));
        var (place, handle) = TrayDesktop.Get(() => (CaptureDesk.Outer(shown.Window), shown.Window.TryGetPlatformHandle()!.Handle));
        Assert.IsTrue(place.Contains(area), $"the flyout at {place} covers {area}");

        var taking = desk.Take();
        await desk.OverlayInFront();
        TrayDesktop.Run(() => Assert.IsFalse(shown.Window.IsVisible, "hidden while the overlay is up (G3)"));
        var region = default(PixelRect);
        if (released)
        {
            region = await CaptureDesk.Drag(new PixelPoint(area.X + 20, area.Y + 20), new PixelPoint(area.Right - 20, area.Bottom - 20));
        }
        else
        {
            TestHotKeys.Tap(VkEscape);
        }

        await taking;

        await Eventually(() => FrontWindow.GetForegroundWindow() == handle && TrayDesktop.Get(() => shown.Window.IsActive));
        // An activation read as a gain of focus sets the target at the next idle.
        await Task.Delay(300);
        TrayDesktop.Run(() =>
        {
            Assert.IsTrue(shown.Window.IsVisible, "back once the capture ended");
            Assert.AreEqual(place, CaptureDesk.Outer(shown.Window), "where it was, at the size it had");
            Assert.AreEqual("hiding", shown.Search.Text, "the query as it was, not reset by a summon");
            CollectionAssert.AreEqual(new[] { kept.Id }, shown.Listing.Selection.ToList(), "the selection");
            Assert.AreEqual(kept.Id, shown.Preview.Item?.Id, "the preview open on its item");
            Assert.AreSame(shown.PreviewPicture, shown.Focused, "focus where it was");
            Assert.AreEqual(PresetTarget.App("Chat"), shown.Window.PasteTarget.Current, "the paste target it had, which the footer and Enter use (G8)");
        });
        Assert.HasCount(released ? 3 : 2, desk.Items(), released ? "the screenshot saved" : "nothing saved");
        if (released)
        {
            var saved = CaptureDesk.FirstFrame(desk.Items().Single(each => each.Name.StartsWith("Screenshot ")).FilePath);
            CollectionAssert.AreEqual(Cut(beneath, new PixelRect(region.X - area.X, region.Y - area.Y, region.Width, region.Height)).Pixels, saved.Pixels, "what lies beneath the flyout, nothing of it");
        }
    }

    [TestMethod]
    public async Task APinnedWindowInFrontIsMinimisedForTheGrabAndRestoredActive()
    {
        using var desk = await CaptureDesk.Open(["pinned kept.png"]);
        var shown = desk.Shown;
        var area = DisplayPlaces.Rect(DisplayPlaces.Primary(), 660, 320, 260, 200);
        // A pattern of the check's own beneath the pinned window, not topmost so the pinned window comes in front of it:
        // what lies beneath is known, not a grab of a desktop the check does not own, taken at another moment.
        using var beneath = PatternWindow.Show(area, window => window.Topmost = false);
        await beneath.OnScreen(new WindowsScreen());
        var handle = await PinnedInFront(desk);

        var taking = desk.Take();
        await desk.OverlayInFront();
        TrayDesktop.Run(() => Assert.AreEqual(WindowState.Minimized, shown.Window.WindowState, "minimised while the overlay is up (G3)"));
        var region = await CaptureDesk.Drag(new PixelPoint(area.X + 20, area.Y + 20), new PixelPoint(area.Right - 20, area.Bottom - 20));
        await taking;

        await Eventually(() => FrontWindow.GetForegroundWindow() == handle && TrayDesktop.Get(() => shown.Window.IsActive));
        TrayDesktop.Run(() =>
        {
            Assert.AreEqual(WindowState.Normal, shown.Window.WindowState, "restored once the capture ended");
            Assert.AreEqual(PinnedPlace.Position, shown.Window.Position, "where it was");
            Assert.IsTrue(shown.Search.IsFocused, "focus where it was");
        });
        var item = desk.Items().Single(each => each.Name.StartsWith("Screenshot "));
        var saved = CaptureDesk.FirstFrame(item.FilePath);
        var differences = beneath.Differences(saved, region);
        Assert.IsEmpty(differences, $"the pattern beneath the pinned window, nothing of it or of its minimising; found, expected: {string.Join("; ", differences.Take(5))}");
    }

    [TestMethod]
    public async Task APinnedWindowBehindTheChatIsRestoredWithoutTakingTheChatsKeystrokes()
    {
        using var desk = await CaptureDesk.Open(["pinned behind.png"]);
        var shown = desk.Shown;
        await PinnedInFront(desk);
        desk.Chat.ClickTitleBar();
        FrontWindow.WaitUntilInFront(desk.Chat.Window);

        var taking = desk.Take();
        await desk.OverlayInFront();
        TrayDesktop.Run(() => Assert.AreEqual(WindowState.Minimized, shown.Window.WindowState));
        TestHotKeys.Tap(VkEscape);
        await taking;

        await Eventually(() => TrayDesktop.Get(() => shown.Window.IsVisible && shown.Window.WindowState == WindowState.Normal));
        TrayDesktop.Run(() => Assert.IsFalse(shown.Window.IsActive, "not active, as it was not (G3)"));
        desk.ChatKeepsTheKeystrokes("after the capture");
    }

    [TestMethod]
    public async Task APinnedWindowWithItsPickerUpIsMinimisedForTheCaptureAndComesBackWithThePickerStillUp()
    {
        using var desk = await CaptureDesk.Open(["pinned picking.png"]);
        var window = desk.Shown.Window;
        await PinnedInFront(desk);
        var picker = await desk.PickerInFront();
        try
        {
            var taking = desk.Take();
            await desk.OverlayInFront();
            TrayDesktop.Run(() => Assert.AreEqual(WindowState.Minimized, window.WindowState, "minimised whatever it has open (G3)"));
            TestHotKeys.Tap(VkEscape);
            await taking;

            await Eventually(() => TrayDesktop.Get(() => window.IsVisible && window.WindowState == WindowState.Normal));
            TrayDesktop.Run(() => Assert.IsTrue(window.DialogUp, "its picker still open"));
            await Eventually(() => IsWindowVisible(picker));
        }
        finally
        {
            await desk.ClosePicker(picker);
        }
    }

    [TestMethod]
    public async Task APinnedWindowClosedDuringTheCaptureStaysClosedAndTheNextSummonBringsItForward()
    {
        using var desk = await CaptureDesk.Open(["pinned closed.png"]);
        var window = desk.Shown.Window;
        await PinnedInFront(desk);
        // Behind the chat, so the capture's end would show it afresh rather than restore it.
        desk.Chat.ClickTitleBar();
        FrontWindow.WaitUntilInFront(desk.Chat.Window);

        var taking = desk.Take();
        await desk.OverlayInFront();
        TrayDesktop.Run(window.Close);
        TestMouse.Click(DisplayPlaces.At(DisplayPlaces.Primary(), 300, 600));
        await taking;

        await Task.Delay(300);
        TrayDesktop.Run(() => Assert.IsFalse(window.IsVisible, "Close unpins and hides it, and the capture's end leaves it so (G2)"));
        TrayDesktop.Run(window.SummonFromTray);
        await Eventually(() => TrayDesktop.Get(() => window.IsVisible));
        TrayDesktop.Run(() => Assert.AreEqual(WindowState.Normal, window.WindowState, "the next summon brings it forward (G2)"));
    }

    [TestMethod]
    [DataRow(false, DisplayName = "minimised by its state")]
    [DataRow(true, DisplayName = "minimised by its minimise button's command")]
    public async Task APinnedWindowMinimisedBeforeAScreenshotStaysMinimisedThroughItAndAfterItsNotificationShows(bool byItsButton)
    {
        using var desk = await CaptureDesk.Open(["minimised kept.png"]);
        var window = desk.Shown.Window;
        var handle = await PinnedInFront(desk);
        await Minimise(desk, handle, byItsButton);
        desk.Chat.ClickTitleBar();
        FrontWindow.WaitUntilInFront(desk.Chat.Window);

        var taking = desk.Take();
        await desk.OverlayInFront();
        await CaptureDesk.Drag(DisplayPlaces.At(DisplayPlaces.Primary(), 150, 160), DisplayPlaces.At(DisplayPlaces.Primary(), 400, 320));
        await taking;
        await desk.CardShown();
        await Task.Delay(500);

        TrayDesktop.Run(() =>
        {
            Assert.AreEqual(WindowState.Minimized, window.WindowState, "a window minimised before the capture stays so (G3)");
            Assert.IsFalse(window.IsActive);
        });
        Assert.AreNotEqual(handle, FrontWindow.GetForegroundWindow());
        desk.ChatKeepsTheKeystrokes("after the capture");
    }

    [TestMethod]
    public async Task APinnedWindowMinimisedBeforeTakeAScreenshotChosenInTheRealTrayMenuStaysMinimised()
    {
        using var pointer = TestMouse.PutBackAfterwards();
        using var desk = await CaptureDesk.Open(["minimised tray.png"]);
        var window = desk.Shown.Window;
        var handle = await PinnedInFront(desk);
        await Minimise(desk, handle, byItsButton: true);
        desk.Chat.ClickTitleBar();
        FrontWindow.WaitUntilInFront(desk.Chat.Window);
        var tray = new WindowsTray();
        Task? taking = null;
        try
        {
            TrayDesktop.Run(() => tray.Show([new TrayMenuEntry("Take a screenshot", () => taking = desk.Screenshot.Take()), new TrayMenuEntry("Quit ImageStash", () => { })], "ImageStash minimised capture check", () => { }));
            await Task.Delay(500);
            TrayDesktop.ChooseFromRealMenuThroughTheIcon(tray, 0, 2);
            await desk.OverlayInFront();
            TrayDesktop.Run(() => Assert.AreEqual(WindowState.Minimized, window.WindowState, "while the overlay is up (G3)"));
            await CaptureDesk.Drag(DisplayPlaces.At(DisplayPlaces.Primary(), 150, 160), DisplayPlaces.At(DisplayPlaces.Primary(), 400, 320));
            await TrayDesktop.Get(() => taking!);
            await desk.CardShown();
            await Task.Delay(500);

            TrayDesktop.Run(() =>
            {
                Assert.AreEqual(WindowState.Minimized, window.WindowState, "a window minimised before the capture stays so (G3)");
                Assert.IsFalse(window.IsActive);
            });
        }
        finally
        {
            TrayDesktop.Run(tray.Dispose);
        }
    }

    [TestMethod]
    public async Task APinnedWindowMinimisedBeforeARecordingStaysMinimisedThroughTheWaitTheRecordingAndAfterItsNotificationShows()
    {
        using var desk = await CaptureDesk.Open(["minimised recorded.png"]);
        var window = desk.Shown.Window;
        var handle = await PinnedInFront(desk);
        await Minimise(desk, handle, byItsButton: true);
        desk.Chat.ClickTitleBar();
        FrontWindow.WaitUntilInFront(desk.Chat.Window);

        var recording = desk.Record();
        await desk.OverlayInFront();
        await CaptureDesk.Drag(DisplayPlaces.At(DisplayPlaces.Primary(), 150, 160), DisplayPlaces.At(DisplayPlaces.Primary(), 400, 320));
        await desk.WhileWaiting();
        TrayDesktop.Run(() => Assert.AreEqual(WindowState.Minimized, window.WindowState, "while the region waits (G3)"));
        desk.ClickRecord();
        await desk.WhileRecording();
        await Task.Delay(1000);
        TrayDesktop.Run(() => Assert.AreEqual(WindowState.Minimized, window.WindowState, "while it records (G3)"));
        await desk.Record();
        await recording.WaitAsync(TimeSpan.FromSeconds(30));
        await desk.CardShown();
        await Task.Delay(500);

        TrayDesktop.Run(() =>
        {
            Assert.AreEqual(WindowState.Minimized, window.WindowState, "after the clip's notification (G3)");
            Assert.IsFalse(window.IsActive);
        });
        Assert.AreNotEqual(handle, FrontWindow.GetForegroundWindow());
    }

    [TestMethod]
    public async Task APinnedWindowTheUserRestoresAndMinimisesAgainWhileTheRegionWaitsStaysMinimisedAfterTheRecording()
    {
        using var desk = await CaptureDesk.Open(["minimised again.png"]);
        var window = desk.Shown.Window;
        var handle = await PinnedInFront(desk);

        var recording = desk.Record();
        await desk.OverlayInFront();
        await CaptureDesk.Drag(DisplayPlaces.At(DisplayPlaces.Primary(), 150, 160), DisplayPlaces.At(DisplayPlaces.Primary(), 400, 320));
        await desk.WhileWaiting();
        TrayDesktop.Run(() => Assert.AreEqual(WindowState.Minimized, window.WindowState, "minimised for the recording (G3)"));
        // As a click on its taskbar button restores it, and the user then minimises it again by its button.
        PostMessageW(handle, WmSysCommand, ScRestore, 0);
        await Eventually(() => TrayDesktop.Get(() => window.WindowState == WindowState.Normal));
        await Minimise(desk, handle, byItsButton: true);
        desk.ClickRecord();
        await desk.WhileRecording();
        await Task.Delay(500);
        await desk.Record();
        await recording.WaitAsync(TimeSpan.FromSeconds(30));
        await desk.CardShown();
        await Task.Delay(500);

        TrayDesktop.Run(() => Assert.AreEqual(WindowState.Minimized, window.WindowState, "the user minimised it, not the capture: it stays so (G3)"));
    }

    [TestMethod]
    public async Task AFlyoutHiddenBeforeThePressStaysHidden()
    {
        using var desk = await CaptureDesk.Open(["hidden kept.png"]);

        var taking = desk.Take();
        await desk.OverlayInFront();
        TestHotKeys.Tap(VkEscape);
        await taking;

        await Task.Delay(300);
        TrayDesktop.Run(() => Assert.IsFalse(desk.Shown.Window.IsVisible, "nothing shown that was not hidden for the capture"));
        desk.ChatKeepsTheKeystrokes("after the capture");
    }

    [TestMethod]
    public async Task AHoveredNoticeAndAnOpenTagListEndWithTheFlyoutsHideForACapture()
    {
        // G16, F4: the flyout goes for a capture as it goes any other way, so no pointer is over its notices once it has
        // gone, and one hovered at the start expires six seconds on as an unhovered one does; no list of it stays up.
        using var shown = Open();
        await shown.OpenStash(shown.Picture("hidden while hovered.png"));
        var notice = TrayDesktop.Get(() => shown.Notices.Raise("Hovered when the capture started"));
        TrayDesktop.Run(() =>
        {
            shown.Notices.Hover(notice, over: true);
            shown.ChipRow.Picker.Open(shown.Search, [new TagPickerEntry("hovered", false)], _ => { });
        });
        TrayDesktop.Run(() => Assert.IsTrue(shown.ChipRow.Picker.IsOpen, "the list open when the capture starts"));

        TrayDesktop.Run(() => shown.Window.CaptureHiding.HideForCapture());

        TrayDesktop.Run(() =>
        {
            Assert.IsFalse(shown.Window.IsVisible, "the flyout hidden for the capture (G3)");
            Assert.IsFalse(shown.ChipRow.Picker.IsOpen, "no list left behind (F4)");
            ((StoppedClock)shown.Notices.Clock).Advance(Notices.Lasting);
            shown.Notices.Sweep();
            CollectionAssert.DoesNotContain(shown.Notices.Shown.ToList(), notice, "the hover ended with the hide, so its six seconds ran (G16)");
            shown.Window.CaptureHiding.ShowAfterCapture();
        });
    }

    [TestMethod]
    public async Task ASummonDuringTheCaptureShowsTheFlyoutAndTheEndShowsNothingAgain()
    {
        using var desk = await CaptureDesk.Open(["summoned during.png"]);
        var window = desk.Shown.Window;
        await desk.FlyoutInFront();

        var taking = desk.Take();
        await desk.OverlayInFront();
        TrayDesktop.Run(window.SummonFromTray);
        TrayDesktop.Run(() => Assert.IsTrue(window.IsVisible, "the summon shows it as a summon does (G1)"));
        // The user puts it away again before the capture ends.
        TrayDesktop.Run(window.Hide);
        TestMouse.Click(DisplayPlaces.At(DisplayPlaces.Primary(), 300, 600));
        await taking;

        await Task.Delay(300);
        TrayDesktop.Run(() => Assert.IsFalse(window.IsVisible, "the summon forgot what the capture hid"));
    }

    [TestMethod]
    public async Task ASummonDuringTheCaptureRestoresTheMinimisedPinnedWindowAndTheEndShowsNothingAgain()
    {
        using var desk = await CaptureDesk.Open(["pinned summoned.png"]);
        var window = desk.Shown.Window;
        await PinnedInFront(desk);

        var taking = desk.Take();
        await desk.OverlayInFront();
        TrayDesktop.Run(() => Assert.AreEqual(WindowState.Minimized, window.WindowState));
        TrayDesktop.Run(window.SummonFromTray);
        TrayDesktop.Run(() => Assert.AreEqual(WindowState.Normal, window.WindowState, "a summon restores a minimised pinned window (G1)"));
        TrayDesktop.Run(() => window.WindowState = WindowState.Minimized);
        TestMouse.Click(DisplayPlaces.At(DisplayPlaces.Primary(), 300, 600));
        await taking;

        await Task.Delay(300);
        TrayDesktop.Run(() => Assert.AreEqual(WindowState.Minimized, window.WindowState, "the summon forgot what the capture minimised"));
    }

    [TestMethod]
    public async Task ASummonWhileTheRegionWaitsShowsTheFlyoutAScreenshotThenHidesItAgainAndOnlyTheRecordingsEndBringsItBack()
    {
        using var desk = await CaptureDesk.Open(["recorded during.png"]);
        var window = desk.Shown.Window;
        await desk.FlyoutInFront();

        var recording = desk.Record();
        await desk.OverlayInFront();
        TrayDesktop.Run(() => Assert.IsFalse(window.IsVisible, "hidden for the recording (G3)"));
        await CaptureDesk.Drag(DisplayPlaces.At(DisplayPlaces.Primary(), 150, 160), DisplayPlaces.At(DisplayPlaces.Primary(), 400, 320));
        await desk.WhileWaiting();
        TrayDesktop.Run(() => Assert.IsFalse(window.IsVisible, "still hidden while the region waits (G3)"));
        TrayDesktop.Run(window.SummonFromTray);
        TrayDesktop.Run(() => Assert.IsTrue(window.IsVisible && desk.Session.RecordingWaiting, "summoned as usual while the region waits, which still waits (G3)"));
        var taking = desk.Take();
        await desk.OverlayInFront();
        TrayDesktop.Run(() => Assert.IsFalse(window.IsVisible, "a screenshot then hides it again (G3)"));
        TestHotKeys.Tap(VkEscape);
        await taking;
        await Task.Delay(300);
        TrayDesktop.Run(() => Assert.IsFalse(window.IsVisible || !desk.Session.RecordingWaiting, "not back while the region still waits (G3)"));
        desk.ClickRecord();
        await desk.WhileRecording();
        TrayDesktop.Run(() => Assert.IsFalse(window.IsVisible, "not back while the recording runs (G3)"));
        await desk.Record();
        await recording.WaitAsync(TimeSpan.FromSeconds(30));

        await Eventually(() => TrayDesktop.Get(() => window.IsVisible));
        Assert.HasCount(2, desk.Items(), "the recording saved beside the earlier item");
    }

    [TestMethod]
    public async Task ThePlusMenusTakeAScreenshotHidesTheFlyoutWithItsMenuFromTheGrabAndBringsTheFlyoutBackInFront()
    {
        using var desk = await CaptureDesk.Open(["menu kept.png"]);
        var shown = desk.Shown;
        // Where the flyout stands at the primary display's top right (CaptureDesk.FlyoutInFront), with its + button and menu.
        var display = DisplayPlaces.Primary();
        var area = DisplayPlaces.AroundPlusMenu(display, desk.FlyoutClient(display));
        // A pattern of the check's own beneath the flyout and its menu, not topmost so both come in front of it: what lies
        // beneath is known, not a grab of a desktop the check does not own, taken at another moment.
        using var beneath = PatternWindow.Show(area, window => window.Topmost = false);
        await beneath.OnScreen(new WindowsScreen());
        await desk.FlyoutInFront();
        var handle = TrayDesktop.Get(() => shown.Window.TryGetPlatformHandle()!.Handle);
        TestMouse.Click(TestMouse.Centre(TrayDesktop.Get(() => shown.AddButton)));
        var entry = TrayDesktop.Get(() => shown.AddMenu().Single(each => (string)each.Header! == Screenshot.Action));
        await Eventually(() => TrayDesktop.Get(() => TopLevel.GetTopLevel(entry) is PopupRoot { IsVisible: true }));
        await Task.Delay(300);
        var (entries, menu, button) = TrayDesktop.Get(() => (
            shown.AddMenu().Select(each => $"{each.Header} {each.InputGesture}").ToList(),
            OnScreen(TopLevel.GetTopLevel(entry)!, TopLevel.GetTopLevel(entry)!),
            OnScreen(shown.AddButton, shown.Window)));
        var over = menu.Union(button);

        CollectionAssert.AreEqual(new[] { "Import files… Ctrl+O", "Paste image Ctrl+V", $"Take a screenshot {new KeyGesture(Key.PrintScreen, KeyModifiers.Control)}", $"Record a clip {new KeyGesture(Key.PrintScreen, KeyModifiers.Control | KeyModifiers.Shift)}" }, entries,
            "the ways in, the captures' with their shortcuts (G2, F5)");
        Assert.IsTrue(area.Contains(over), $"the menu at {menu} and the + button at {button} lie inside {area}");
        TrayDesktop.Run(() => Assert.IsTrue(shown.Window.IsActive, "the flyout keeps the focus while its menu is open"));

        TestMouse.Click(TestMouse.Centre(entry));
        await desk.OverlayInFront();
        TrayDesktop.Run(() => Assert.IsFalse(shown.Window.IsVisible, "hidden like any other start (G3)"));
        var region = await CaptureDesk.Drag(over.TopLeft, new PixelPoint(over.Right - 1, over.Bottom - 1));

        await Eventually(() => desk.Items().Count == 2 && TrayDesktop.Get(() => desk.Overlay.Shown.Count == 0 && shown.Window.IsActive) && FrontWindow.GetForegroundWindow() == handle);
        TrayDesktop.Run(() => Assert.IsTrue(shown.Window.IsVisible, "the flyout back in front, its menu's closing not taken for focus lost to another app (G2, G3)"));
        var saved = CaptureDesk.FirstFrame(desk.Items().Single(each => each.Name.StartsWith("Screenshot ")).FilePath);
        var differences = beneath.Differences(saved, region);
        Assert.IsEmpty(differences, $"the pattern beneath the menu and the flyout, nothing of either (G14); found, expected: {string.Join("; ", differences.Take(5))}");
    }

    [TestMethod]
    [DataRow(false, DisplayName = "the flyout")]
    [DataRow(true, DisplayName = "the pinned window")]
    public async Task AToolTipShowingAtThePressLeavesWithTheWindowBeforeTheGrab(bool pinned)
    {
        using var desk = await CaptureDesk.Open(["tip kept.png"]);
        var shown = desk.Shown;
        // Around the + button and its tooltip, with the window at the primary display's top right or pinned at PinnedPlace.
        var display = DisplayPlaces.Primary();
        var area = pinned
            ? DisplayPlaces.AroundPinnedPlusButton(display, new PixelRect(PinnedPlace.Position, PixelSize.FromSize(new Size(PinnedPlace.Width, PinnedPlace.Height), display.Scaling)))
            : DisplayPlaces.AroundPlusMenu(display, desk.FlyoutClient(display));
        var beneath = await Task.Run(() => new WindowsScreen().Grab(area, withPointer: false).Value!);
        var popups = new List<PopupRoot>();
        using var opened = TrayDesktop.Get(() => TemplatedControl.TemplateAppliedEvent.AddClassHandler<PopupRoot>((popup, _) => popups.Add(popup)));
        if (pinned)
        {
            await PinnedInFront(desk);
        }
        else
        {
            await desk.FlyoutInFront();
        }

        var add = TrayDesktop.Get(() => shown.AddButton);
        var centre = TestMouse.Centre(add);
        // The pointer coming to rest on the button, as a person's does, opens its tooltip.
        TestMouse.MoveTo(new PixelPoint(centre.X - 3, centre.Y));
        await Task.Delay(50);
        TestMouse.MoveTo(centre);
        await Eventually(() => TrayDesktop.Get(() => ToolTip.GetIsOpen(add)));
        await Task.Delay(300);
        var tip = TrayDesktop.Get(() =>
        {
            var root = popups.Last(popup => popup.IsVisible && popup.ParentTopLevel == shown.Window);
            return OnScreen(root, root);
        });
        Assert.IsTrue(area.Contains(tip), $"the tooltip at {tip} lies inside {area}");

        var taking = desk.Take();
        await desk.OverlayInFront();
        var region = await CaptureDesk.Drag(tip.TopLeft, new PixelPoint(tip.Right - 1, tip.Bottom - 1));
        await taking;

        var saved = CaptureDesk.FirstFrame(desk.Items().Single(each => each.Name.StartsWith("Screenshot ")).FilePath);
        CollectionAssert.AreEqual(Cut(beneath, new PixelRect(region.X - area.X, region.Y - area.Y, region.Width, region.Height)).Pixels, saved.Pixels, "what lies beneath the window's tooltip, nothing of it (G3, G14)");
    }

    [TestMethod]
    public async Task ThePlusMenuOpenAtThePressLeavesWithTheFlyoutBeforeTheGrab()
    {
        using var desk = await CaptureDesk.Open(["menu open.png"]);
        var shown = desk.Shown;
        // Where the flyout stands at the primary display's top right (CaptureDesk.FlyoutInFront), with its + menu.
        var display = DisplayPlaces.Primary();
        var area = DisplayPlaces.AroundPlusMenu(display, desk.FlyoutClient(display));
        var beneath = await Task.Run(() => new WindowsScreen().Grab(area, withPointer: false).Value!);
        await desk.FlyoutInFront();
        TestMouse.Click(TestMouse.Centre(TrayDesktop.Get(() => shown.AddButton)));
        var entry = TrayDesktop.Get(() => shown.AddMenu().Single(each => (string)each.Header! == Screenshot.Action));
        await Eventually(() => TrayDesktop.Get(() => TopLevel.GetTopLevel(entry) is PopupRoot { IsVisible: true }));
        await Task.Delay(300);
        var menu = TrayDesktop.Get(() => OnScreen(TopLevel.GetTopLevel(entry)!, TopLevel.GetTopLevel(entry)!));
        Assert.IsTrue(area.Contains(menu), $"the menu at {menu} lies inside {area}");

        // The shortcut's act, run on the UI thread as a press runs it, with the menu still open.
        var taking = desk.Take();
        await desk.OverlayInFront();
        var region = await CaptureDesk.Drag(menu.TopLeft, new PixelPoint(menu.Right - 1, menu.Bottom - 1));
        await taking;

        var saved = CaptureDesk.FirstFrame(desk.Items().Single(each => each.Name.StartsWith("Screenshot ")).FilePath);
        CollectionAssert.AreEqual(Cut(beneath, new PixelRect(region.X - area.X, region.Y - area.Y, region.Width, region.Height)).Pixels, saved.Pixels, "what lies beneath the menu, nothing of it (G3, G14)");
    }

    /// <summary>
    /// The pinned window minimised as the user does it: by its state, or by the minimise command its title bar's button
    /// sends Windows, posted to its window as the button posts it.
    /// </summary>
    private static async Task Minimise(CaptureDesk desk, nint handle, bool byItsButton)
    {
        var window = desk.Shown.Window;
        // Every change of the window's state, kept in memory and told only on a timeout, so that a minimise that never
        // comes shows whether it was ignored or undone; writing anything out before the post was seen to hide the stall.
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var changes = new System.Collections.Concurrent.ConcurrentQueue<string>();
        void Changed(object? sender, AvaloniaPropertyChangedEventArgs change)
        {
            if (change.Property == Avalonia.Controls.Window.WindowStateProperty)
            {
                changes.Enqueue($"{clock.ElapsedMilliseconds} ms: {change.OldValue} → {change.NewValue}, iconic {IsIconic(handle)}, in front {FrontWindow.GetForegroundWindow():X}");
            }
        }

        TrayDesktop.Run(() => window.PropertyChanged += Changed);
        try
        {
            if (byItsButton)
            {
                // Posted within milliseconds of the window's activation, the command was seen never acted on (no change of
                // state at all) once in a whole run; a person's click on the button never follows an activation that soon.
                FrontWindow.WaitUntilTheFrontSettles(300);
                PostMessageW(handle, WmSysCommand, ScMinimize, 0);
            }
            else
            {
                TrayDesktop.Run(() => window.WindowState = WindowState.Minimized);
            }

            changes.Enqueue($"{clock.ElapsedMilliseconds} ms: asked {(byItsButton ? "by its button's command" : "by its state")}");
            string Told() => $"{string.Join("; ", changes)}; now {TrayDesktop.Get(() => window.WindowState)}, iconic {IsIconic(handle)}, in front {FrontWindow.GetForegroundWindow():X} (the window {handle:X})";
            var deadline = DateTime.UtcNow.AddSeconds(10);
            bool Minimised() => TrayDesktop.Get(() => window.WindowState == WindowState.Minimized);
            await Within(deadline, Minimised, () => $"the pinned window did not minimise: {Told()}");
            await Within(deadline, () => Minimised() && FrontWindow.GetForegroundWindow() != handle, () => $"the minimised window stayed in front, or did not stay minimised: {Told()}");
        }
        finally
        {
            TrayDesktop.Run(() => window.PropertyChanged -= Changed);
        }

        await Task.Delay(300);
    }

    /// <summary>Waits until <paramref name="deadline"/> at most for <paramref name="condition"/>, failing with what <paramref name="told"/> says then.</summary>
    private static async Task Within(DateTime deadline, Func<bool> condition, Func<string> told)
    {
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                Assert.Fail(told());
            }

            await Task.Delay(20);
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", ExactSpelling = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool IsIconic(nint window);

    private const uint WmSysCommand = 0x0112;
    private const nint ScMinimize = 0xF020;
    private const nint ScRestore = 0xF120;

    [System.Runtime.InteropServices.DllImport("user32.dll", ExactSpelling = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool PostMessageW(nint window, uint message, nint wParam, nint lParam);

    /// <summary>The stash window pinned at <see cref="PinnedPlace"/>, clicked in front with its search box focused: its handle.</summary>
    private static async Task<nint> PinnedInFront(CaptureDesk desk)
    {
        var shown = desk.Shown;
        shown.Settings.Change(settings => settings with { PinnedPosition = new WindowPosition(PinnedPlace.X, PinnedPlace.Y), PinnedSize = new WindowSize(PinnedPlace.Width, PinnedPlace.Height) });
        TrayDesktop.Run(shown.Window.SummonPinned);
        await Task.Delay(300);
        var handle = TrayDesktop.Get(() => shown.Window.TryGetPlatformHandle()!.Handle);
        TestMouse.Click(TestMouse.Centre(TrayDesktop.Get(() => shown.Search)));
        await Eventually(() => FrontWindow.GetForegroundWindow() == handle && TrayDesktop.Get(() => shown.Window.IsActive && shown.Search.IsFocused));
        return handle;
    }

    /// <summary>A control's rectangle on the desktop in physical pixels, at the scaling of <paramref name="level"/>, its window.</summary>
    private static PixelRect OnScreen(Visual control, TopLevel level) =>
        new(control.PointToScreen(default), PixelSize.FromSize(control.Bounds.Size, level.RenderScaling));

    [System.Runtime.InteropServices.DllImport("user32.dll", ExactSpelling = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);

    private static MediaFrame Cut(MediaFrame frame, PixelRect area)
    {
        var pixels = new byte[4 * area.Width * area.Height];
        for (var y = 0; y < area.Height; y++)
        {
            Array.Copy(frame.Pixels, ((area.Y + y) * frame.Width + area.X) * 4, pixels, y * area.Width * 4, area.Width * 4);
        }

        return new MediaFrame(area.Width, area.Height, 0, pixels);
    }
}
