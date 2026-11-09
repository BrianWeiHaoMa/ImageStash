using System.Collections.Concurrent;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using ImageStash.Editor;
using ImageStash.Seams;
using ImageStash.Store;
using ImageStash.Window;
using ImageStash.Windows.Seams;
using ImageStash.Windows.Tests.Support;
using static ImageStash.Windows.Tests.Support.CaptureDesk;
using static ImageStash.Windows.Tests.Support.ShownWindow;
using static ImageStash.Windows.Tests.Support.TestMouse;

namespace ImageStash.Windows.Tests.Window;

/// <summary>
/// The capture notification's activation as Windows shows it (G16): a click on a card's ×, or on a plain notification,
/// leaving ImageStash's own file picker in front with the keystrokes and a name typed there as
/// typed, and leaving the stash window active throughout, its paste target standing even right after an Alt+Tab to it,
/// and a click on the chat before ImageStash has handled one leaving the chat in front; the stash flyout kept for focus
/// on its own notification and hidden when the user leaves for another app (G2); the paste target read at the field's
/// click (G8); a card's Edit or Trim opening the stash window in front on its item in the editor, as a flyout above the
/// tray or pinned where it was, the window staying after Save with the item selected, and the card's Undo closing that
/// edit, the card staying above the flyout that stands where it does (G16, G1, G26). These checks show the notification and a chat window of their own on the primary display, open
/// the stash window's Import files… picker, switch windows with Alt+Tab, click them, type into them and move the pointer.
/// </summary>
[TestClass]
public sealed class CaptureNotificationTests
{
    private const ushort Esc = 0x1B;

    [TestInitialize]
    public void UseProductTheme() => TrayDesktop.UseProductTheme();

    [TestMethod]
    public async Task TheFlyoutStaysForFocusOnItsOwnNotificationAndHidesWhenTheUserLeavesTheFieldForAnotherApp()
    {
        using var desk = await CaptureDesk.Open(["stash flyout.png"]);
        var card = await desk.Raise(desk.Items().Single());
        var flyout = desk.Shown.Window;
        await desk.FlyoutInFront();

        TestMouse.Click(Centre(TrayDesktop.Get(() => NameButton(card))));
        await Eventually(() => TrayDesktop.Get(() => card.Field.IsEditing && NameBox(card).IsFocused));
        Thread.Sleep(500);
        TrayDesktop.Run(() => Assert.IsTrue(flyout.IsVisible, "focus went to ImageStash's own notification, not another app (G2)"));

        desk.Chat.ClickTitleBar();
        await Eventually(() => TrayDesktop.Get(() => !flyout.IsVisible));

        await desk.FlyoutInFront();
        TestMouse.Click(Centre(TrayDesktop.Get(() => NameButton(card))));
        await Eventually(() => TrayDesktop.Get(() => card.Field.IsEditing && NameBox(card).IsFocused));
        TestHotKeys.Tap(Esc);
        await Eventually(() => TrayDesktop.Get(() => flyout.IsActive));
        Thread.Sleep(500);
        TrayDesktop.Run(() => Assert.IsTrue(flyout.IsVisible && flyout.IsActive, "the activation back on the flyout, still showing"));
        Assert.AreEqual(TrayDesktop.Get(() => flyout.TryGetPlatformHandle()!.Handle), FrontWindow.GetForegroundWindow());
    }

    [TestMethod]
    public async Task WithTheFlyoutInFrontAClickOnAPlainNotificationLeavesItsPasteTargetAndAClickOnTheNameReadsItAgain()
    {
        using var targets = new WindowsTarget();
        using var desk = await CaptureDesk.Open(["stash flyout target.png"], platformTarget: targets);
        var card = await desk.Raise(desk.Items().Single());
        var plain = await desk.RaisePlain();
        var flyout = desk.Shown.Window;
        var paste = flyout.PasteTarget;
        // Brought in front from the chat by a click, ImageStash gaining focus from it: the chat is the target (G8).
        await desk.FlyoutInFront();
        await Eventually(() => TrayDesktop.Get(() => paste.Current?.Handle) == desk.Chat.Window);
        var handle = TrayDesktop.Get(() => flyout.TryGetPlatformHandle()!.Handle);
        var deactivations = 0;
        TrayDesktop.Run(() => flyout.Deactivated += (_, _) => deactivations++);
        nint? TargetAfterASettle()
        {
            Thread.Sleep(500);
            return TrayDesktop.Get(() => paste.Current?.Handle);
        }

        // A click on the notification takes no focus (G16), not even for a moment: the flyout is never deactivated, so
        // ImageStash gains none and the footer's target stands (G8); and the flyout keeps the keystrokes, a letter typed
        // after the click landing in its search box.
        async Task ClickLeavesTheFlyoutActive(Control control, string what)
        {
            var at = Centre(control);
            Assert.AreEqual(0, FrontWindow.ElsewhereInFront(handle, () => TestMouse.Click(at), 500), $"the flyout in front throughout a click on {what} (G16)");
            TrayDesktop.Run(() => Assert.AreEqual(0, deactivations, $"a click on {what} never deactivates the flyout"));
            TrayDesktop.Run(() => Assert.IsTrue(flyout.IsVisible && flyout.IsActive, $"and it keeps the activation after a click on {what}"));
            var typed = TrayDesktop.Get(() => desk.Shown.Search.Text ?? "") + "k";
            TestHotKeys.Tap('K');
            var clock = System.Diagnostics.Stopwatch.StartNew();
            while (TrayDesktop.Get(() => desk.Shown.Search.Text) != typed && clock.ElapsedMilliseconds < 2000)
            {
                await Task.Delay(20);
            }

            Assert.AreEqual(typed, TrayDesktop.Get(() => desk.Shown.Search.Text), $"a letter typed after a click on {what} lands in the flyout's search box (G16)");
        }

        await ClickLeavesTheFlyoutActive(plain, "a plain notification's words");
        Assert.AreEqual(desk.Chat.Window, TargetAfterASettle(), "a click on the words takes no focus, so the target stands");

        // The name's click is the one that takes focus: the window before the notification was ImageStash's own, so none,
        // and the give-back to the flyout reads none as well (G8: "none when that window was ImageStash itself, one of its
        // notifications").
        TestMouse.Click(Centre(TrayDesktop.Get(() => NameButton(card))));
        await Eventually(() => TrayDesktop.Get(() => card.Field.IsEditing && NameBox(card).IsFocused));
        Assert.IsNull(TargetAfterASettle(), "the field took the focus from ImageStash's own window");
        TestHotKeys.Tap(Esc);
        await Eventually(() => TrayDesktop.Get(() => flyout.IsActive));
        Assert.IsNull(TargetAfterASettle(), "the flyout took it back from ImageStash's own notification");

        // × as well: the notice leaves, and the flyout never lost the activation to it.
        TrayDesktop.Run(() => deactivations = 0);
        await ClickLeavesTheFlyoutActive(TrayDesktop.Get(() => CloseButton(card)), "×");
        await Eventually(() => TrayDesktop.Get(() => desk.Notifications.Shown.Count == 1));
    }

    [TestMethod]
    public async Task AnAltTabToThePinnedWindowThenAClickOnAPlainNotificationAtOnceLeavesTheAppTheTarget()
    {
        using var targets = new WindowsTarget();
        using var desk = await CaptureDesk.Open(["stash alt tab.png"], platformTarget: targets);
        var plain = await desk.RaisePlain();
        var window = desk.Shown.Window;
        TrayDesktop.Run(window.SummonPinned);
        await desk.FlyoutInFront();
        // At its smallest and at the working area's top right, its frame included, clear of the chat's title bar and of the
        // notification below it; the height set again, since the pin's own placement, settling after the flyout's, put the
        // pinned height back.
        TrayDesktop.Run(() =>
        {
            var area = window.Screens.Primary!.WorkingArea;
            window.Width = WindowSize.Minimum.Width;
            window.Height = WindowSize.Minimum.Height;
            window.Position = new PixelPoint(area.Right - 500, area.Y);
        });
        Thread.Sleep(300);
        TrayDesktop.Run(() =>
        {
            var outer = CaptureDesk.Outer(window);
            Assert.IsFalse(outer.Intersects(desk.Chat.Bounds) || outer.Intersects(CaptureDesk.Outer(desk.Notification.Shown!)), $"the pinned window at {outer} clear of the chat at {desk.Chat.Bounds} and of the notification at {CaptureDesk.Outer(desk.Notification.Shown!)}");
        });
        var words = Centre(plain);
        var handle = TrayDesktop.Get(() => window.TryGetPlatformHandle()!.Handle);
        var targetsSet = new ConcurrentQueue<nint?>();
        TrayDesktop.Run(() => window.PasteTarget.Changed += () => targetsSet.Enqueue(window.PasteTarget.Current?.Handle));
        var unfinished = 0;
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            desk.Chat.ClickTitleBar();
            TestMouse.MoveTo(words);
            // Alt+Tab back to the pinned window, ImageStash gaining focus from the chat, then the click before the next idle.
            TestHotKeys.SwitchWindows(TimeSpan.Zero, () => { });
            TestMouse.Click(words);
            Thread.Sleep(400);
            // A click the instant Alt is released can leave the shell's switcher up with no switch made (3 in 500 such
            // switches on build 26300, none in 250 without the click): the user never reached the pinned window, so the
            // attempt is closed with Esc and made again, three times at most.
            if (targets.Read(FrontWindow.GetForegroundWindow()) is null && unfinished++ < 3)
            {
                TestHotKeys.Tap(Esc);
                Thread.Sleep(300);
                attempt--;
                continue;
            }

            Assert.AreEqual(handle, FrontWindow.GetForegroundWindow(), $"attempt {attempt}: the pinned window in front");
            // G8: while ImageStash has not yet heard which window had focus just before it, the target is none until it
            // hears, and then that window; a foreground hook still behind at the activation's read is heard after it.
            await Eventually(() => TrayDesktop.Get(() => window.PasteTarget.Current?.Handle) == desk.Chat.Window);
            Assert.AreEqual(desk.Chat.Window, TrayDesktop.Get(() => window.PasteTarget.Current?.Handle), $"attempt {attempt}: the app the user came from stays the target (G8)");
        }

        Assert.IsTrue(targetsSet.All(set => set is null || set == desk.Chat.Window), "no target but none and the app the user came from (G8)");
    }

    [TestMethod]
    public async Task ThePasteTargetIsTheAppTheFieldWasClickedFromSoTheShortcutsFlyoutPastesThereAndAClickOnAPlainNotificationChangesNothing()
    {
        using var targets = new WindowsTarget();
        using var desk = await CaptureDesk.Open(["stash target.png"], platformTarget: targets);
        using var other = TargetApp.Start("ImageStash check other chat", DisplayPlaces.At(DisplayPlaces.Primary(), 100, 420));
        var paste = desk.Shown.Window.PasteTarget;
        TrayDesktop.Run(() => paste.FromPress(new ForegroundReading(false, new PasteTarget(other.Window, "other"))));
        desk.Chat.ClickTitleBar();
        var card = await desk.Raise(desk.Items().Single());
        var plain = await desk.RaisePlain();

        TestMouse.Click(Centre(plain));
        Thread.Sleep(300);
        Assert.AreEqual(other.Window, TrayDesktop.Get(() => paste.Current?.Handle), "a click on the words activates nothing, so the target stands (G8)");

        TestMouse.Click(Centre(TrayDesktop.Get(() => NameButton(card))));
        await Eventually(() => TrayDesktop.Get(() => card.Field.IsEditing && NameBox(card).IsFocused));
        Assert.AreEqual(desk.Chat.Window, TrayDesktop.Get(() => paste.Current?.Handle), "ImageStash gained focus from the chat: the chat is the target (G8)");
        var atThePress = targets.Foreground();
        Assert.IsTrue(atThePress.IsMine, "the open shortcut pressed now finds ImageStash in front");

        // The open shortcut's summon with what was in front at its press, activating the flyout as the product's does,
        // then Enter in the flyout.
        TrayDesktop.Run(() =>
        {
            desk.Shown.Window.ShowActivated = true;
            desk.Shown.Window.SummonFromShortcut(atThePress, new SummonAnchor(null));
        });
        await Eventually(() => TrayDesktop.Get(() => desk.Shown.Window.IsActive && !card.Field.IsEditing));
        Assert.AreEqual(desk.Chat.Window, TrayDesktop.Get(() => paste.Current?.Handle), "the press leaves the chat the flyout's target");
        var keysBefore = desk.Chat.Messages().Count;
        TrayDesktop.Run(() => ShownWindow.Press(desk.Shown.Search, Avalonia.Input.Key.Enter));
        await Eventually(() => desk.Chat.Messages().Skip(keysBefore).Contains("WM_KEYDOWN:0x56"));
        Assert.AreEqual(desk.Chat.Window, FrontWindow.GetForegroundWindow(), "Enter pasted into the chat the field was clicked from (G8)");
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task AClickOnANotificationWhileImageStashsOwnPickerIsInFrontLeavesThePickerItsKeystrokes(bool captureCard)
    {
        using var targets = new WindowsTarget();
        using var desk = await CaptureDesk.Open(["stash picker.png"], platformTarget: targets);
        CaptureCard? card = null;
        if (captureCard)
        {
            card = await desk.Raise(desk.Items().Single());
        }

        var plain = await desk.RaisePlain();
        await desk.FlyoutInFront();
        var picker = await desk.PickerInFront();
        try
        {
            var keys = new List<Avalonia.Input.Key>();
            TrayDesktop.Run(() => desk.Notification.Shown!.KeyDown += (_, e) => keys.Add(e.Key));

            TestMouse.Click(Centre(plain));
            Thread.Sleep(300);
            TestHotKeys.Tap('A');
            Thread.Sleep(200);

            Assert.AreEqual(picker, FrontWindow.GetForegroundWindow(), "the click takes no focus from ImageStash's own picker (G16)");
            Assert.IsEmpty(TrayDesktop.Get(() => keys.ToList()), "and the notification gets none of the keystrokes");

            if (card is not null)
            {
                // The name's click still takes the activation from the picker, the name selected, and Esc gives it back.
                TestMouse.Click(Centre(TrayDesktop.Get(() => NameButton(card))));
                await Eventually(() => TrayDesktop.Get(() => card.Field.IsEditing && NameBox(card).IsFocused));
                Assert.AreEqual(desk.NotificationHandle, FrontWindow.GetForegroundWindow(), "the name's click brings the notification in front (G16)");
                TrayDesktop.Run(() => Assert.AreEqual(card.Field.Shown, NameBox(card).SelectedText, "the name selected, so typing replaces it"));
                TestHotKeys.Tap(Esc);
                await Eventually(() => FrontWindow.GetForegroundWindow() == picker);
                TrayDesktop.Run(() => Assert.IsFalse(card.Field.IsEditing, "Esc ended the edit"));
            }
        }
        finally
        {
            await desk.ClosePicker(picker);
        }
    }

    [TestMethod]
    public async Task AClickOnANotificationWhileANameIsTypedInImageStashsOwnPickerLeavesTheNameAsTyped()
    {
        using var targets = new WindowsTarget();
        using var desk = await CaptureDesk.Open(["stash picker name.png"], platformTarget: targets);
        var plain = await desk.RaisePlain();
        await desk.FlyoutInFront();
        var picker = await desk.PickerInFront();
        try
        {
            TestHotKeys.Press(new Shortcut(new Avalonia.Input.KeyGesture(Avalonia.Input.Key.N, Avalonia.Input.KeyModifiers.Alt), "Alt+N"));
            Thread.Sleep(150);
            var box = FrontWindow.FocusedHandle();
            TestHotKeys.Press(new Shortcut(new Avalonia.Input.KeyGesture(Avalonia.Input.Key.A, Avalonia.Input.KeyModifiers.Control), "Ctrl+A"));
            TestHotKeys.Tap('Z');
            // End: the caret after the z, nothing selected, as a person typing a name leaves it.
            TestHotKeys.Tap(0x23);
            await Eventually(() => FrontWindow.NativeText(box) == "z");

            TestMouse.Click(Centre(plain));
            Thread.Sleep(500);
            TestHotKeys.Tap('A');
            Thread.Sleep(200);

            Assert.AreEqual(picker, FrontWindow.GetForegroundWindow(), "the click takes no focus from ImageStash's own picker (G16)");
            Assert.AreEqual("za", FrontWindow.NativeText(box), "the letter typed after the click lands where the user left the caret (G16)");
        }
        finally
        {
            await desk.ClosePicker(picker);
        }
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task AClickOnAnotherAppBeforeAClickOnANotificationIsHandledLeavesThatAppInFrontAndTheFlyoutHidesUnlessItsPickerIsUp(bool pickerUp)
    {
        using var targets = new WindowsTarget();
        using var desk = await CaptureDesk.Open(["stash picker later.png"], platformTarget: targets);
        var plain = await desk.RaisePlain();
        var flyout = desk.Shown.Window;
        await desk.FlyoutInFront();
        var picker = pickerUp ? await desk.PickerInFront() : 0;
        using var clicked = new ManualResetEventSlim();
        using var resume = new ManualResetEventSlim();
        // The UI thread held inside the click on the notification, so the user's click on the chat lands before whatever that
        // click left queued for the UI thread runs.
        EventHandler<Avalonia.Input.PointerPressedEventArgs> stall = (_, _) =>
        {
            clicked.Set();
            resume.Wait(TimeSpan.FromSeconds(5));
        };
        try
        {
            TrayDesktop.Run(() => plain.AddHandler(Avalonia.Input.InputElement.PointerPressedEvent, stall, Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true));
            TestMouse.Click(Centre(plain));
            Assert.IsTrue(clicked.Wait(TimeSpan.FromSeconds(5)), "the click on the notification arrived");
            desk.Chat.ClickTitleBar();
            Assert.AreEqual(desk.Chat.Window, FrontWindow.GetForegroundWindow(), "the user's click put the chat in front");
            resume.Set();
            Thread.Sleep(700);
            desk.ChatKeepsTheKeystrokes("after the click on the notification was handled (G16)");
            // Focus went to another app: the flyout hides, but not while a dialog it opened is up (G2).
            Assert.AreEqual(pickerUp, TrayDesktop.Get(() => flyout.IsVisible), "the flyout shows only while its picker is up (G2)");
        }
        finally
        {
            resume.Set();
            TrayDesktop.Run(() => plain.RemoveHandler(Avalonia.Input.InputElement.PointerPressedEvent, stall));
            await desk.ClosePicker(picker);
        }
    }

    [TestMethod]
    public async Task EditOnACardOpensTheFlyoutAboveTheTrayInFrontOnItsItemWithNoPasteTargetAndAfterSaveItStaysWithTheItemSelected()
    {
        using var targets = new WindowsTarget();
        using var desk = await CaptureDesk.Open(["stash other.png"], platformTarget: targets);
        // Large enough for a Hide box dragged across it, so the Save renders.
        var edited = await desk.Import(desk.Shown.Picture("stash edited.png", 160, 120, (x, y) => new SkiaSharp.SKColor((byte)x, (byte)y, 90)));
        var card = await desk.Raise(edited, CaptureKind.Screenshot);
        var flyout = desk.Shown.Window;
        var handle = TrayDesktop.Get(() => flyout.TryGetPlatformHandle()!.Handle);
        using var editor = new ShownEditor(desk.Shown);
        var keys = 0;
        TrayDesktop.Run(() => flyout.AddHandler(Avalonia.Input.InputElement.KeyDownEvent, (_, _) => keys++, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true));

        TestMouse.Click(Centre(TrayDesktop.Get(() => EditButton(card))));
        await Eventually(() => TrayDesktop.Get(() => desk.Shown.Editing.Session?.Item.Id == edited.Id) && FrontWindow.GetForegroundWindow() == handle);
        Thread.Sleep(500);

        Assert.AreEqual(handle, FrontWindow.GetForegroundWindow(), "the stash window in front, not the notification (G16)");
        TrayDesktop.Run(() =>
        {
            var expected = Summon.AboveTrayIcon(null, Summon.DisplaysOf(flyout.Screens), WindowSize.FlyoutDefault);
            Assert.IsTrue(flyout.IsVisible && flyout.IsActive);
            Assert.IsFalse(flyout.ShowInTaskbar, "a flyout, the window not having been pinned (G16)");
            Assert.AreEqual(expected.Position, flyout.Position, "placed as from the tray, here with no icon to stand above (G1)");
            Assert.AreEqual(EditOrigin.Notification, desk.Shown.Editing.Origin);
            Assert.IsNull(flyout.PasteTarget.Current, "the window before it was ImageStash's own notification, so no paste target and Enter copies (G8)");
            Assert.IsTrue(desk.Shown.Focused is Visual focused && focused.GetSelfAndVisualAncestors().Contains(editor.Overlay), "keyboard focus in the editor");
            Assert.AreSame(card, desk.Notification.Shown?.GetVisualDescendants().OfType<CaptureCard>().SingleOrDefault(), "the card stays, with its Undo");
        });
        var chatBefore = desk.Chat.Messages().Count;
        TestHotKeys.Tap('K');
        await Eventually(() => TrayDesktop.Get(() => keys) > 0);
        Assert.IsFalse(desk.Chat.Messages().Skip(chatBefore).Any(message => message.StartsWith("WM_KEYDOWN")), "a letter typed reaches the editor, not the chat");

        // Focus taken to the chat while the edit is open keeps the flyout (G2); brought back by a click, Save then leaves it up.
        desk.Chat.ClickTitleBar();
        Thread.Sleep(500);
        TrayDesktop.Run(() => Assert.IsTrue(flyout.IsVisible, "an open edit keeps the flyout up (G2)"));
        TestMouse.Click(Centre(TrayDesktop.Get(() => editor.Overlay.GetVisualDescendants().OfType<TextBlock>().First(text => text.IsEffectivelyVisible && text.Text == edited.Name))));
        await Eventually(() => FrontWindow.GetForegroundWindow() == handle);
        TrayDesktop.Run(() =>
        {
            editor.Session!.Tool = editor.Session.Tools.OfType<HideTool>().Single();
            editor.Drag((0.2, 0.2), (0.7, 0.7));
        });
        TestMouse.Click(Centre(TrayDesktop.Get(() => editor.Button("Save"))));
        await Eventually(() => TrayDesktop.Get(() => !desk.Shown.Editing.IsOpen));
        Thread.Sleep(500);

        TrayDesktop.Run(() =>
        {
            Assert.IsTrue(flyout.IsVisible, "G26: a window a notification opened stays open after Save");
            CollectionAssert.AreEqual(new[] { edited.Id }, desk.Shown.Listing.Selection.ToList(), "with the item selected");
            Assert.AreEqual(1, desk.Shown.Listing.Items.Single(item => item.Id == edited.Id).RenderedRevision, "saved");
        });
    }

    [TestMethod]
    public async Task ACardStaysAboveTheFlyoutAboveTheTrayBeforeAndAfterItsEditOpensItAndAfterACaptureSoItsUndoClosesThatEdit()
    {
        // The card above the stand-in icon by the display's middle, under which the flyout, summoned with no tray icon to
        // stand above, is centred on any display; the working area's corner, where the card stands without an icon, lies
        // under a centred flyout only on a small display.
        using var desk = await CaptureDesk.Open(["stash captured.png"], midScreen: true);
        var item = desk.Items().Single();
        var flyout = desk.Shown.Window;
        var handle = TrayDesktop.Get(() => flyout.TryGetPlatformHandle()!.Handle);
        var card = await desk.Raise(item, CaptureKind.Screenshot);

        // The flyout summoned from the tray over the card's place, coming forward after the card was raised (G1, G16).
        TrayDesktop.Run(flyout.SummonFromTray);
        await Eventually(() => FrontWindow.GetForegroundWindow() == handle && TrayDesktop.Get(() => flyout.IsActive));
        Thread.Sleep(500);
        TrayDesktop.Run(() => Assert.IsTrue(Outer(flyout).Intersects(Outer(desk.Notification.Shown!)), $"both above the tray: the flyout at {Outer(flyout)}, the card's window at {Outer(desk.Notification.Shown!)}"));
        Assert.AreEqual(desk.NotificationHandle, FrontWindow.Under(Centre(TrayDesktop.Get(() => EditButton(card)))), "the card's Edit above the flyout (G16)");
        Assert.AreEqual(desk.NotificationHandle, FrontWindow.Under(Centre(TrayDesktop.Get(() => UndoButton(card)))), "and its Undo");

        // The flyout hidden again, so the card's Edit shows it afresh above the tray, as after a capture.
        TrayDesktop.Run(flyout.Hide);
        TestMouse.Click(Centre(TrayDesktop.Get(() => EditButton(card))));
        await Eventually(() => TrayDesktop.Get(() => desk.Shown.Editing.Session?.Item.Id == item.Id) && FrontWindow.GetForegroundWindow() == handle);
        Thread.Sleep(500);
        TrayDesktop.Run(() => Assert.IsTrue(Outer(flyout).Intersects(Outer(desk.Notification.Shown!)), $"both above the tray: the flyout at {Outer(flyout)}, the card's window at {Outer(desk.Notification.Shown!)}"));
        Assert.AreEqual(desk.NotificationHandle, FrontWindow.Under(Centre(TrayDesktop.Get(() => UndoButton(card)))), "the card's Undo above the flyout its Edit opened (G16)");
        Assert.AreEqual(handle, FrontWindow.GetForegroundWindow(), "the flyout in front with the edit");

        // A capture started during the edit brings the flyout back activated, the card still up (G3).
        var taking = desk.Take();
        await desk.OverlayInFront();
        TrayDesktop.Run(() => Assert.IsFalse(flyout.IsVisible, "hidden while the overlay is up (G3)"));
        TestHotKeys.Tap(Esc);
        await taking;
        await Eventually(() => FrontWindow.GetForegroundWindow() == handle && TrayDesktop.Get(() => flyout.IsVisible && flyout.IsActive));
        Thread.Sleep(500);
        Assert.AreEqual(desk.NotificationHandle, FrontWindow.Under(Centre(TrayDesktop.Get(() => UndoButton(card)))), "the card's Undo above the flyout shown again");

        TestMouse.Click(Centre(TrayDesktop.Get(() => UndoButton(card))));
        await Eventually(() => TrayDesktop.Get(() => !desk.Shown.Editing.IsOpen) && desk.Items().All(listed => listed.Id != item.Id));
        TrayDesktop.Run(() => Assert.IsTrue(flyout.IsVisible, "the edit closed without saving, the item gone, the window up (G16)"));
        foreach (var entry in RecycleBinEntries.Of(item.FilePath))
        {
            File.Delete(entry.Recycled);
            File.Delete(entry.Record);
        }
    }

    [TestMethod]
    public async Task TrimBringsThePinnedWindowBackWhereItWasOnTheTrimToolAnotherCardsEditOpensItsOwnItemAndThatCardsUndoClosesTheEdit()
    {
        using var desk = await CaptureDesk.Open(["stash still edited.png"]);
        var shown = desk.Shown;
        var still = desk.Items().Single();
        var clip = await desk.Import(shown.Fixture("animated.gif", "stash clip.gif"));
        var clipCard = await desk.Raise(clip, CaptureKind.Recording);
        var stillCard = await desk.Raise(still, CaptureKind.Screenshot);
        using var editor = new ShownEditor(shown);
        // Pinned clear of the notification above the tray, then minimised.
        shown.Settings.Change(settings => settings with { PinnedPosition = new WindowPosition(DisplayPlaces.Scaled(DisplayPlaces.Primary(), 40), DisplayPlaces.Scaled(DisplayPlaces.Primary(), 40)), PinnedSize = new WindowSize(560, 560) });
        TrayDesktop.Run(shown.Window.SummonPinned);
        await Task.Delay(300);
        var where = TrayDesktop.Get(() => shown.Window.Position);
        var handle = TrayDesktop.Get(() => shown.Window.TryGetPlatformHandle()!.Handle);
        TrayDesktop.Run(() => shown.Window.WindowState = WindowState.Minimized);
        await Task.Delay(500);

        TrayDesktop.Run(() => Assert.AreEqual("Trim", EditButton(clipCard).Content, "a recording's card offers Trim (G16)"));
        TestMouse.Click(Centre(TrayDesktop.Get(() => EditButton(clipCard))));
        await Eventually(() => TrayDesktop.Get(() => shown.Editing.Session?.Item.Id == clip.Id) && FrontWindow.GetForegroundWindow() == handle);
        TrayDesktop.Run(() =>
        {
            Assert.AreEqual(WindowState.Normal, shown.Window.WindowState, "restored");
            Assert.IsTrue(shown.Window.ShowInTaskbar, "still pinned (G16)");
            Assert.AreEqual(where, shown.Window.Position, "brought forward where it was (G1)");
            Assert.IsInstanceOfType<TrimTool>(shown.Editing.Session!.Tool, "with the Trim tool selected (G16)");
            Assert.IsTrue(editor.Button("Trim") is Avalonia.Controls.Primitives.ToggleButton { IsChecked: true }, "shown as the current tool");
        });

        // The other card's Edit, the first card still showing: the clip's edit left as Cancel would, the still opened (G20).
        TestMouse.Click(Centre(TrayDesktop.Get(() => EditButton(stillCard))));
        await Eventually(() => TrayDesktop.Get(() => shown.Editing.Session?.Item.Id == still.Id));
        TrayDesktop.Run(() =>
        {
            Assert.IsInstanceOfType<TextTool>(shown.Editing.Session!.Tool, "Edit opens on Text");
            CollectionAssert.AreEqual(new[] { "Discarded the edit of “stash clip”." }, shown.Notices.Shown.Select(notice => notice.Text).ToList());
        });

        // That card's Undo while its edit is open: the editor closes without saving or a notice, and the item leaves (G16).
        TestMouse.Click(Centre(TrayDesktop.Get(() => UndoButton(stillCard))));
        await Eventually(() => TrayDesktop.Get(() => !shown.Editing.IsOpen) && desk.Items().All(item => item.Id != still.Id));
        Thread.Sleep(300);
        TrayDesktop.Run(() =>
        {
            Assert.IsTrue(shown.Window.IsVisible);
            Assert.IsFalse(shown.Listing.Items.Any(item => item.Id == still.Id), "the grid without it");
            Assert.HasCount(1, shown.Notices.Shown, "no notice of its own");
        });
        Assert.AreEqual(handle, FrontWindow.GetForegroundWindow(), "the window keeps the activation through Undo");
        var entries = RecycleBinEntries.Of(still.FilePath);
        Assert.HasCount(1, entries, "its one file in the Recycle Bin (G29)");
        foreach (var entry in entries)
        {
            File.Delete(entry.Recycled);
            File.Delete(entry.Record);
        }
    }
}
