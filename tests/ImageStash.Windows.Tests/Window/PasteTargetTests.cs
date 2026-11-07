using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ImageStash.Seams;
using ImageStash.Window;
using ImageStash.Windows.Tests.Support;
using static ImageStash.Windows.Tests.Support.ShownWindow;

namespace ImageStash.Windows.Tests.Window;

/// <summary>
/// The paste target through the real stash window (G8, G2, G9, G10): from the shortcut the footer and the item menu
/// name the app in front at the press, which the summon's own activation leaves standing; Enter, a double-click and the
/// menu's first entry paste into it after hiding the flyout, a pinned window staying, and a flyout whose dialog is up
/// when the hide comes, after a retried write, staying; a target that cannot take the
/// paste hides the flyout too, leaving the app in front, with the notification raised; from the tray, Enter copies; Ctrl+C copies; Enter
/// pressed before the matches arrive pastes where the footer said at that moment; the preview's handoff button is named
/// as the footer and pastes its item, with Copy beside it copying. The paste seam is the check's own;
/// the clipboard is the user's real one. These summons show the window on the primary display for a moment.
/// </summary>
[TestClass]
public sealed class PasteTargetTests
{
    private static readonly PasteTarget Relay = PresetTarget.App("Relay");
    private static readonly PasteTarget Other = PresetTarget.App("Other");

    [TestMethod]
    public async Task FromTheShortcutTheFooterAndTheMenuNameTheAppAndEnterPastesIntoItOnceTheFlyoutHasHidden()
    {
        var target = new PresetTarget { Before = Other };
        using var shown = ShownWindow.Open(target: target);
        var item = (await shown.OpenStash(shown.Picture("paste one.png"))).Single();
        var hiddenAtThePaste = false;
        target.DuringPaste = () => hiddenAtThePaste = !shown.Window.IsVisible;
        await SummonFromShortcut(shown, Relay);

        var hints = TrayDesktop.Get(shown.FooterHints);
        var menu = TrayDesktop.Get(() => shown.RightClick(item.Id));
        TrayDesktop.Run(shown.CloseMenu);
        TrayDesktop.Run(() => ShownWindow.Press(shown.Search, Key.Enter));
        await Eventually(() => target.Pasted.Count == 1 && shown.Uses() == 1);

        CollectionAssert.AreEqual(new[] { "Enter · Paste into Relay", "Ctrl+C · Copy" }, hints, "the footer names the app (G8)");
        CollectionAssert.AreEqual(new[] { "Paste into Relay", "Copy", "Preview", "Edit", "Add to Favorites", "Rename", "Add tag", "Save a copy…", "Open with…", "Reveal in File Explorer", "Delete" }, menu, "and so does the menu (G8), Delete last (G18)");
        CollectionAssert.AreEqual(new[] { Relay }, target.Pasted, "pasted into the app in front at the press, not the one the summon's activation came after");
        Assert.IsTrue(hiddenAtThePaste, "the flyout hid before the target was brought forward (G2, G8)");
        TrayDesktop.Run(() => Assert.IsFalse(shown.Window.IsVisible));
        CollectionAssert.AreEqual(new[] { item.FilePath }, TestClipboard.Read().Files.ToList(), "the copy's content (G8)");
        Assert.IsEmpty(shown.Notifications.Shown, "a paste says nothing (G10)");
    }

    [TestMethod]
    public async Task AnActivationAfterAPressSetsTheTargetThoughThatPressesSummonComesLater()
    {
        var target = new PresetTarget { Before = Other };
        using var shown = ShownWindow.Open(target: target);
        TrayDesktop.Run(shown.Window.SummonPinned);
        await Task.Delay(300);
        // A press over Relay: its reading reaches the window at once, its summon once Relay's caret has answered.
        TrayDesktop.Run(() => shown.Window.PasteTarget.FromPress(new ForegroundReading(false, Relay)));
        var atThePress = TrayDesktop.Get(() => shown.Window.PasteTarget.Current);
        // Meanwhile the pinned window is clicked from Other.
        TrayDesktop.Run(shown.Activate);
        await Task.Delay(300);

        TrayDesktop.Run(() => shown.Window.SummonFromShortcut(new SummonAnchor(null)));
        await Task.Delay(300);
        var afterTheSummon = TrayDesktop.Get(() => shown.Window.PasteTarget.Current);

        Assert.AreEqual(Relay, atThePress);
        Assert.AreEqual(Other, afterTheSummon, "the press's late summon leaves the later activation's target standing (G8: whenever ImageStash gains focus any other way)");
    }

    [TestMethod]
    public async Task TheActivationTheShortcutsSummonCausesKeepsThePressesTargetAndALaterActivationReadsTheWindowBefore()
    {
        var target = new PresetTarget { Before = Other };
        using var shown = ShownWindow.Open(target: target);

        TrayDesktop.Run(() =>
        {
            shown.Window.Hide();
            shown.Window.SummonFromShortcut(new ForegroundReading(false, Relay), new SummonAnchor(null));
        });
        await Task.Delay(300);
        var (activated, afterTheSummon) = TrayDesktop.Get(() => (shown.Window.IsActive, shown.Window.PasteTarget.Current));
        if (!activated)
        {
            Assert.Inconclusive("Windows did not activate the summoned window, so the activation the summon causes did not happen.");
        }

        TrayDesktop.Run(shown.Activate);
        await Task.Delay(300);
        var afterAClick = TrayDesktop.Get(() => shown.Window.PasteTarget.Current);

        Assert.AreEqual(Relay, afterTheSummon, "the press's target stands through the summon's own activation (G8)");
        Assert.AreEqual(Other, afterAClick, "a later activation reads the window that had focus just before (G8)");
    }

    [TestMethod]
    public async Task AnActivationRightAfterTheSummonsOwnReadsTheWindowBeforeIt()
    {
        var target = new PresetTarget { Before = Other };
        using var shown = ShownWindow.Open(target: target);

        TrayDesktop.Run(() =>
        {
            shown.Window.Hide();
            shown.Window.SummonFromShortcut(new ForegroundReading(false, Relay), new SummonAnchor(null));
            // The summon's own activation, then, before the next idle, another program in front and ImageStash again.
            shown.Activate();
            target.Before = PresetTarget.App("Brief");
            shown.Activate();
        });
        await Task.Delay(300);

        Assert.AreEqual(PresetTarget.App("Brief"), TrayDesktop.Get(() => shown.Window.PasteTarget.Current), "a gain of focus after the summon's own reads again (G8)");
    }

    [TestMethod]
    public async Task APressWithImageStashInFrontLeavesTheTargetAndOneWithTheDesktopInFrontClearsIt()
    {
        using var shown = ShownWindow.Open(target: new PresetTarget());
        await SummonFromShortcut(shown, Relay);

        TrayDesktop.Run(() =>
        {
            shown.Window.Hide();
            shown.Window.SummonFromShortcut(new ForegroundReading(true, null), new SummonAnchor(null));
        });
        var whileMine = TrayDesktop.Get(() => shown.Window.PasteTarget.Current);
        TrayDesktop.Run(() =>
        {
            shown.Window.Hide();
            shown.Window.SummonFromShortcut(new ForegroundReading(false, null), new SummonAnchor(null));
        });
        var fromTheDesktop = TrayDesktop.Get(() => shown.Window.PasteTarget.Current);

        Assert.AreEqual(Relay, whileMine);
        Assert.IsNull(fromTheDesktop);
    }

    [TestMethod]
    public async Task EnterPressedBeforeTheMatchesArrivePastesWhereTheFooterSaidWhenEnterWasPressed()
    {
        var target = new PresetTarget { Before = Other };
        using var shown = ShownWindow.Open(target: target);
        var items = await shown.OpenStash(shown.Picture("paste corner.png"), shown.Picture("paste stripe.png"));
        await SummonFromShortcut(shown, Relay);

        using (shown.HoldStore())
        {
            // While another connection holds the store, the query's matches cannot arrive before Enter acts.
            TrayDesktop.Run(() =>
            {
                shown.Search.Text = "stripe";
                ShownWindow.Press(shown.Search, Key.Enter);
                shown.Activate();
            });
            await Task.Delay(300);
            TrayDesktop.Run(() => Assert.AreEqual(Other, shown.Window.PasteTarget.Current, "the footer has moved on to another app meanwhile"));
        }

        await Eventually(() => target.Pasted.Count == 1);
        CollectionAssert.AreEqual(new[] { Relay }, target.Pasted, "the target the footer named when Enter was pressed (G8)");
        CollectionAssert.AreEqual(new[] { items[1].FilePath }, TestClipboard.Read().Files.ToList(), "the first match (G5)");
    }

    [TestMethod]
    public async Task APinnedWindowPastesAndStaysShowing()
    {
        var target = new PresetTarget();
        using var shown = ShownWindow.Open(target: target);
        await shown.OpenStash(shown.Picture("paste pinned.png"));
        // Pinned first, its own activation read, then the shortcut pressed over Relay.
        TrayDesktop.Run(shown.Window.SummonPinned);
        await Task.Delay(300);
        TrayDesktop.Run(() => shown.Window.SummonFromShortcut(new ForegroundReading(false, Relay), new SummonAnchor(null)));
        await TrayDesktop.Get(() => shown.Listing.Settle());

        TrayDesktop.Run(() => ShownWindow.Press(shown.Search, Key.Enter));
        await Eventually(() => target.Pasted.Count == 1);

        TrayDesktop.Run(() =>
        {
            Assert.IsTrue(shown.Window.IsVisible, "a pinned window is never hidden by a paste (G2)");
            Assert.IsTrue(shown.Window.ShowInTaskbar, "still pinned");
        });
    }

    [TestMethod]
    public async Task APasteWhoseFlyoutHasADialogUpLeavesTheFlyoutShowing()
    {
        var target = new PresetTarget();
        using var shown = ShownWindow.Open(target: target);
        await shown.OpenStash(shown.Picture("paste dialog.png"));
        await SummonFromShortcut(shown, Relay);

        TrayDesktop.Run(() =>
        {
            // As when Ctrl+O opened the picker while the paste's write was retried for a clipboard another program held.
            shown.Window.DialogUp = true;
            ShownWindow.Press(shown.Search, Key.Enter);
        });
        await Eventually(() => target.Pasted.Count == 1);

        TrayDesktop.Run(() =>
        {
            Assert.IsTrue(shown.Window.IsVisible, "the flyout stays while a dialog it opened is up (G2)");
            shown.Window.DialogUp = false;
        });
    }

    [TestMethod]
    public async Task ADialogOpenedWhileThePastesWriteIsRetriedLeavesTheFlyoutShowing()
    {
        // G2: whether a dialog of the flyout's is up is judged as the paste hides it, after a write retried for a clipboard
        // another program held, not as Enter was pressed.
        var target = new PresetTarget();
        using var shown = ShownWindow.Open(target: target);
        await shown.OpenStash(shown.Picture("paste dialog while retried.png"));
        await SummonFromShortcut(shown, Relay);

        using (var holder = OtherProgramHoldingTheClipboard.Start())
        {
            TrayDesktop.Run(() => ShownWindow.Press(shown.Search, Key.Enter));
            // As when Ctrl+O opened the picker while the write waited.
            TrayDesktop.Run(() => shown.Window.DialogUp = true);
            Assert.IsEmpty(target.Pasted, "the write waits for the clipboard");
            holder.Release();
            await Eventually(() => target.Pasted.Count == 1);
        }

        TrayDesktop.Run(() =>
        {
            Assert.IsTrue(shown.Window.IsVisible, "the flyout stays while a dialog it opened is up (G2)");
            shown.Window.DialogUp = false;
        });
    }

    [TestMethod]
    public async Task APinnedWindowActivatedFromAnotherAppNamesThatApp()
    {
        var target = new PresetTarget();
        using var shown = ShownWindow.Open(target: target);
        await shown.OpenStash(shown.Picture("paste clicked.png"));
        TrayDesktop.Run(shown.Window.SummonPinned);
        await Task.Delay(300);
        target.Before = Relay;

        TrayDesktop.Run(shown.Activate);
        await Task.Delay(300);

        CollectionAssert.AreEqual(new[] { "Enter · Paste into Relay", "Ctrl+C · Copy" }, TrayDesktop.Get(shown.FooterHints), "G8: the window that had focus just before");
    }

    [TestMethod]
    public async Task AChangeTheSeamHearsOfLateAfterAnActivationRenamesTheFooterOnTheWindowsThread()
    {
        // G8: the seam heard of the app clicked from only after the activation was read; its report comes on the seam's thread.
        var target = new PresetTarget { Before = Other, InFront = new ForegroundReading(true, null) };
        using var shown = ShownWindow.Open(target: target);
        await shown.OpenStash(shown.Picture("paste heard late.png"));
        TrayDesktop.Run(shown.Window.SummonPinned);
        await Task.Delay(300);
        TrayDesktop.Run(shown.Activate);
        await Task.Delay(300);
        CollectionAssert.AreEqual(new[] { "Enter · Paste into Other", "Ctrl+C · Copy" }, TrayDesktop.Get(shown.FooterHints));

        target.Before = Relay;
        await Task.Run(target.ReportLate);

        await Eventually(() => TrayDesktop.Get(shown.FooterHints).SequenceEqual(new[] { "Enter · Paste into Relay", "Ctrl+C · Copy" }));
        Assert.AreEqual(Relay, TrayDesktop.Get(() => shown.Window.PasteTarget.Current));
    }

    [TestMethod]
    public async Task ALateReportOnceAnotherAppIsInFrontLeavesTheTargetAndTheNextActivationReadsTheWindowBefore()
    {
        // G8, Target: the late report is read again only while ImageStash's window is still in front; once another app
        // is, the change is the user's next one, which the next activation reads.
        var target = new PresetTarget { Before = null, InFront = new ForegroundReading(true, null) };
        using var shown = ShownWindow.Open(target: target);
        await shown.OpenStash(shown.Picture("paste heard late elsewhere.png"));
        TrayDesktop.Run(shown.Window.SummonPinned);
        // The summon's own activation has come, so the next one is an activation the window did not cause.
        await Eventually(() => TrayDesktop.Get(() => shown.Window.IsActive));
        CollectionAssert.AreEqual(new[] { "Enter · Copy" }, TrayDesktop.Get(shown.FooterHints));
        target.Before = Other;
        TrayDesktop.Run(shown.Activate);
        // Named only once that activation has been read, which follows the seam's late reports from then on.
        string[] other = ["Enter · Paste into Other", "Ctrl+C · Copy"];
        await Eventually(() => TrayDesktop.Get(shown.FooterHints).SequenceEqual(other));

        var elsewhere = PresetTarget.App("Elsewhere");
        target.Before = Relay;
        target.InFront = new ForegroundReading(false, elsewhere);
        await Task.Run(target.ReportLate);
        // Behind everything the report posted to the window's thread, whatever its priority: the read again has run.
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.SystemIdle);
        CollectionAssert.AreEqual(other, TrayDesktop.Get(shown.FooterHints), "a report arriving with another app in front changes nothing");

        target.Before = elsewhere;
        target.InFront = new ForegroundReading(true, null);
        TrayDesktop.Run(shown.Activate);
        await Eventually(() => TrayDesktop.Get(shown.FooterHints).SequenceEqual(new[] { "Enter · Paste into Elsewhere", "Ctrl+C · Copy" }));
    }

    [TestMethod]
    public async Task ALateReportWhileACapturesWindowsAreInFrontLeavesTheTargetTheWindowHad()
    {
        // G8, G3: a report heard late while the capture's windows, ImageStash's own, are in front reads nothing again.
        var target = new PresetTarget { Before = Other, InFront = new ForegroundReading(true, null) };
        using var shown = ShownWindow.Open(target: target);
        await shown.OpenStash(shown.Picture("paste across a capture.png"));
        TrayDesktop.Run(shown.Window.SummonPinned);
        await Task.Delay(300);
        TrayDesktop.Run(shown.Activate);
        await Task.Delay(300);
        string[] other = ["Enter · Paste into Other", "Ctrl+C · Copy"];
        CollectionAssert.AreEqual(other, TrayDesktop.Get(shown.FooterHints));

        TrayDesktop.Run(() => shown.Window.CaptureHiding.HideForCapture());
        await Task.Delay(300);
        target.Before = null;
        await Task.Run(target.ReportLate);
        await Task.Delay(300);
        var whileHidden = TrayDesktop.Get(shown.FooterHints);
        TrayDesktop.Run(() => shown.Window.CaptureHiding.ShowAfterCapture());

        CollectionAssert.AreEqual(other, whileHidden);
    }

    [TestMethod]
    public async Task APinnedWindowActiveWhenACaptureStartedIsRestoredWithThePasteTargetItHad()
    {
        // G8: "The stash window shown again at the end of a capture (G3) keeps the paste target it had"; the window before
        // ImageStash's own is by then a capture window of its own, so reading it again would leave none.
        var target = new PresetTarget { Before = Other, InFront = new ForegroundReading(true, null) };
        using var shown = ShownWindow.Open(target: target);
        await shown.OpenStash(shown.Picture("paste restored after a capture.png"));
        TrayDesktop.Run(shown.Window.SummonPinned);
        await Task.Delay(300);
        TrayDesktop.Run(shown.Activate);
        await Task.Delay(300);
        string[] other = ["Enter · Paste into Other", "Ctrl+C · Copy"];
        CollectionAssert.AreEqual(other, TrayDesktop.Get(shown.FooterHints));
        Assert.IsTrue(TrayDesktop.Get(() => shown.Window.IsActive), "the pinned window is active when the capture starts");

        TrayDesktop.Run(() => shown.Window.CaptureHiding.HideForCapture());
        await Task.Delay(300);
        target.Before = null;
        TrayDesktop.Run(() => shown.Window.CaptureHiding.ShowAfterCapture());
        await Task.Delay(600);

        Assert.AreEqual(WindowState.Normal, TrayDesktop.Get(() => shown.Window.WindowState), "the pinned window is restored");
        CollectionAssert.AreEqual(other, TrayDesktop.Get(shown.FooterHints), "the restore keeps the target the window had");
        Assert.AreEqual(Other, TrayDesktop.Get(() => shown.Window.PasteTarget.Current));
    }

    [TestMethod]
    public async Task AClickIntoThePinnedWindowRightAfterACaptureShowedItInactiveTakesTheAppClickedFrom()
    {
        // G8: a pinned window shown again without activation after a capture expects no activation of its own, so the
        // user's click from another app straight after it is ImageStash gaining focus, and sets the target to that app.
        var target = new PresetTarget { Before = Other, InFront = new ForegroundReading(true, null) };
        using var shown = ShownWindow.Open(target: target);
        await shown.OpenStash(shown.Picture("paste clicked after a capture.png"));
        TrayDesktop.Run(shown.Window.SummonPinned);
        await Task.Delay(300);
        TrayDesktop.Run(shown.Activate);
        await Task.Delay(300);
        TrayDesktop.Run(shown.Deactivate);
        await Task.Delay(300);
        Assert.IsFalse(TrayDesktop.Get(() => shown.Window.IsActive), "another app is in front when the capture starts");

        TrayDesktop.Run(() => shown.Window.CaptureHiding.HideForCapture());
        await Task.Delay(300);
        target.Before = Relay;
        TrayDesktop.Run(() =>
        {
            shown.Window.CaptureHiding.ShowAfterCapture();
            shown.Activate();
        });
        await Task.Delay(600);

        CollectionAssert.AreEqual(new[] { "Enter · Paste into Relay", "Ctrl+C · Copy" }, TrayDesktop.Get(shown.FooterHints), "the app clicked from");
    }

    [TestMethod]
    public async Task AClickIntoTheFlyoutRightAfterACaptureShowedItInactiveTakesTheAppClickedFrom()
    {
        // G3, G8: a flyout not active when the capture started is shown again where it was without activation, expecting
        // no activation of its own, so the user's click from another app straight after it sets the target to that app.
        var target = new PresetTarget();
        using var shown = ShownWindow.Open(target: target);
        await shown.OpenStash(shown.Picture("paste flyout clicked after a capture.png"));
        var place = TrayDesktop.Get(() => shown.Window.Position);
        Assert.IsFalse(TrayDesktop.Get(() => shown.Window.IsActive), "another app is in front when the capture starts");

        TrayDesktop.Run(() => shown.Window.CaptureHiding.HideForCapture());
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.SystemIdle);
        target.Before = Relay;
        TrayDesktop.Run(() =>
        {
            shown.Window.CaptureHiding.ShowAfterCapture();
            Assert.IsTrue(shown.Window.IsVisible, "shown again once the capture ended (G3)");
            Assert.IsFalse(shown.Window.IsActive, "not activated, as it was not (G3)");
            Assert.AreEqual(place, shown.Window.Position, "where it was");
            shown.Activate();
        });
        // Behind the activation's read, which is posted for the next idle.
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.SystemIdle);

        CollectionAssert.AreEqual(new[] { "Enter · Paste into Relay", "Ctrl+C · Copy" }, TrayDesktop.Get(shown.FooterHints), "the app clicked from");
    }

    [TestMethod]
    public async Task EnterADoubleClickOrTheMenuHandledRightAfterAnActivationUseTheAppActivatedFrom()
    {
        var target = new PresetTarget { Before = Other };
        using var shown = ShownWindow.Open(target: target);
        var item = (await shown.OpenStash(shown.Picture("paste right after.png"))).Single();
        TrayDesktop.Run(shown.Window.SummonPinned);
        await TrayDesktop.Get(() => shown.Listing.Settle());
        await Task.Delay(300);

        // Each time the pinned window is clicked from another app with the next input queued right behind the click:
        // both are handled before any Background-priority work the activation left runs.
        target.Before = Relay;
        TrayDesktop.Run(() =>
        {
            shown.Activate();
            ShownWindow.Press(shown.Search, Key.Enter);
        });
        await Eventually(() => target.Pasted.Count == 1);
        target.Before = Other;
        TrayDesktop.Run(() =>
        {
            shown.Activate();
            shown.DoubleClick(item.Id);
        });
        await Eventually(() => target.Pasted.Count == 2);
        target.Before = Relay;
        var menu = TrayDesktop.Get(() =>
        {
            shown.Activate();
            return shown.RightClick(item.Id);
        });
        TrayDesktop.Run(shown.CloseMenu);

        CollectionAssert.AreEqual(new[] { Relay, Other }, target.Pasted, "G8: whenever ImageStash gains focus any other way, the window that had focus just before");
        Assert.AreEqual("Paste into Relay", menu[0], "the menu names the app activated from (G8)");
    }

    [TestMethod]
    public async Task ATargetThatCannotTakeThePasteHidesTheFlyoutWithTheNotificationRaised()
    {
        var target = new PresetTarget { Confirms = new Failure(PasteTargetReasons.Action, "Relay", PasteTargetReasons.HigherRights) };
        using var shown = ShownWindow.Open(target: target);
        await shown.OpenStash(shown.Picture("paste elevated.png"));
        await SummonFromShortcut(shown, Relay);

        TrayDesktop.Run(() => ShownWindow.Press(shown.Search, Key.Enter));
        await Eventually(() => shown.Notifications.Shown.Count == 1);

        Assert.AreEqual("Copied · press Ctrl+V", shown.Notifications.Shown.Single().Text, "G8's words");
        Assert.IsEmpty(target.Pasted, "no keystroke");
        TrayDesktop.Run(() => Assert.IsFalse(shown.Window.IsVisible, "the flyout hides as for a paste, so the app is in front for its Ctrl+V (G2, G8)"));
        Assert.IsFalse(shown.Notices.Shown.Any(notice => notice.Text.StartsWith("Copied") || notice.Text.StartsWith("Paste")), "the notification, not a notice inside the window");
        Assert.AreEqual(1, shown.Uses(), "the copy it made is a use (G10)");
    }

    [TestMethod]
    public async Task ATargetThatCannotTakeThePasteLeavesThePinnedWindowShowingWithTheNotificationRaised()
    {
        var target = new PresetTarget { Before = Relay, Confirms = new Failure(PasteTargetReasons.Action, "Relay", PasteTargetReasons.HigherRights) };
        using var shown = ShownWindow.Open(target: target);
        await shown.OpenStash(shown.Picture("paste elevated pinned.png"));
        TrayDesktop.Run(shown.Window.SummonPinned);
        await Task.Delay(300);

        TrayDesktop.Run(() => ShownWindow.Press(shown.Search, Key.Enter));
        await Eventually(() => shown.Notifications.Shown.Count == 1);

        Assert.AreEqual("Copied · press Ctrl+V", shown.Notifications.Shown.Single().Text);
        TrayDesktop.Run(() => Assert.IsTrue(shown.Window.IsVisible, "the pinned window stays (G2)"));
    }

    [TestMethod]
    public async Task OverAnAppThatCannotBeSentInputTheHiddenFlyoutLeavesThatAppInFront()
    {
        var target = new PresetTarget { Before = Relay, Confirms = new Failure(PasteTargetReasons.Action, "Relay", PasteTargetReasons.HigherRights) };
        using var pointer = TestMouse.PutBackAfterwards();
        using var shown = ShownWindow.Open(target: target);
        await shown.OpenStash(shown.Picture("paste front.png"));
        // Another process, as an app the user came from is, clear of the stash window at the display's left edge.
        using var app = TargetApp.Start("ImageStash check behind the flyout", 700, 400);
        app.ClickTitleBar();
        FrontWindow.WaitUntilInFront(app.Window);
        TrayDesktop.Run(() => shown.Window.ShowActivated = true);
        await SummonFromShortcut(shown, Relay);
        var flyout = TrayDesktop.Get(() => shown.Window.TryGetPlatformHandle()!.Handle);
        FrontWindow.WaitUntilInFront(flyout);

        TrayDesktop.Run(() => ShownWindow.Press(shown.Search, Key.Enter));

        FrontWindow.WaitUntilInFront(app.Window);
        await Eventually(() => shown.Notifications.Shown.Count == 1);
        Assert.AreEqual(app.Window, FrontWindow.GetForegroundWindow(), "the app the user came from is in front for its Ctrl+V (G8, 20b)");
    }

    [TestMethod]
    public async Task TheTrayMenuLeavesNoPasteTargetWhicheverAppWasInFrontWhenItsEntryWasClicked()
    {
        // Windows may put the app back in front under a menu still showing, so the seam answers with that app.
        var target = new PresetTarget { Before = Relay };
        using var shown = ShownWindow.Open(target: target);
        await shown.OpenStash(shown.Picture("paste menu.png"));
        TrayDesktop.Run(() =>
        {
            shown.Window.Hide();
            shown.Window.SummonFromTray();
        });
        await Task.Delay(300);
        Assert.AreEqual(Relay, TrayDesktop.Get(() => shown.Window.PasteTarget.Current), "what the summon read inside the click");

        TrayDesktop.Run(shown.Window.KeepForward);
        await Task.Delay(300);

        TrayDesktop.Run(() =>
        {
            Assert.IsNull(shown.Window.PasteTarget.Current, "the window before it was the menu, ImageStash's own (G8)");
            CollectionAssert.AreEqual(new[] { "Enter · Copy" }, shown.FooterHints(), "opened from the tray, Enter copies (In use)");
        });
    }

    [TestMethod]
    public async Task FromTheTrayTheFooterReadsCopyAndEnterCopiesWithTheWindowStaying()
    {
        var target = new PresetTarget { Before = null };
        using var shown = ShownWindow.Open(target: target);
        var item = (await shown.OpenStash(shown.Picture("paste tray.png"))).Single();
        TrayDesktop.Run(() =>
        {
            shown.Window.Hide();
            shown.Window.SummonFromTray();
        });
        await TrayDesktop.Get(() => shown.Listing.Settle());
        await Task.Delay(300);

        var hints = TrayDesktop.Get(shown.FooterHints);
        TrayDesktop.Run(() => ShownWindow.Press(shown.Search, Key.Enter));
        await Eventually(() => shown.Uses() == 1);

        CollectionAssert.AreEqual(new[] { "Enter · Copy" }, hints, "In use: opened from the tray, the footer reads Enter · Copy");
        Assert.IsEmpty(target.Pasted);
        TrayDesktop.Run(() => Assert.IsTrue(shown.Window.IsVisible, "a copy is not a hide (G2)"));
        CollectionAssert.AreEqual(new[] { item.FilePath }, TestClipboard.Read().Files.ToList());
    }

    [TestMethod]
    public async Task CtrlCWithATargetCopiesWithoutHidingOrPastingAndADoubleClickPastes()
    {
        var target = new PresetTarget();
        using var shown = ShownWindow.Open(target: target);
        var item = (await shown.OpenStash(shown.Picture("paste keys.png"))).Single();
        await SummonFromShortcut(shown, Relay);

        TrayDesktop.Run(() => ShownWindow.Press(shown.Search, Key.C, KeyModifiers.Control));
        await Eventually(() => shown.Uses() == 1);
        var visibleAfterCopy = TrayDesktop.Get(() => shown.Window.IsVisible);
        var pastedAfterCopy = target.Pasted.Count;
        TrayDesktop.Run(() => shown.DoubleClick(item.Id));
        await Eventually(() => target.Pasted.Count == 1);

        Assert.IsTrue(visibleAfterCopy, "Ctrl+C copies and the window stays (G9)");
        Assert.AreEqual(0, pastedAfterCopy, "with no keystroke");
        CollectionAssert.AreEqual(new[] { Relay }, target.Pasted, "a double-click is the handoff (G8)");
        await Eventually(() => shown.Uses() == 2);
    }

    [TestMethod]
    public async Task ThePreviewsHandoffButtonIsNamedAsTheFooterPastesItsItemAndCopyBesideItCopiesByPointerAndByKeyboard()
    {
        var target = new PresetTarget();
        using var shown = ShownWindow.Open(target: target);
        var items = await shown.OpenStash(shown.Picture("button one.png"), shown.Picture("button two.png"));
        var previewed = items[1];
        TrayDesktop.Run(() => shown.OpenPreview(previewed));

        TrayDesktop.Run(() =>
        {
            CollectionAssert.AreEqual(new[] { "Copy", "Enter" }, ShownWindow.Words(shown.HandoffButton), "with no target the handoff button reads Copy, as the footer's Enter does (G19, G8)");
            Assert.IsFalse(shown.CopyButton.IsVisible, "it is the copy, with no second Copy beside it");
            shown.Window.PasteTarget.FromPress(new ForegroundReading(false, Relay));
        });
        TrayDesktop.Run(() =>
        {
            CollectionAssert.AreEqual(new[] { "Paste into Relay", "Enter" }, ShownWindow.Words(shown.HandoffButton), "renamed as the target changes");
            Assert.AreEqual("Paste into Relay", AutomationProperties.GetName(shown.HandoffButton), "and read so");
            Assert.IsTrue(shown.CopyButton.IsVisible, "Copy beside the paste (G19)");
            ShownWindow.Press(shown.PreviewPicture, Key.Escape);
        });
        TrayDesktop.Run(() => CollectionAssert.AreEqual(new[] { "Enter · Paste into Relay", "Ctrl+C · Copy" }, shown.FooterHints(), "the footer, with the preview closed, names it the same"));

        TrayDesktop.Run(() =>
        {
            shown.OpenPreview(previewed);
            shown.Listing.Select(items.Select(item => item.Id), items[0].Id);
            shown.HandoffButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        });
        await Eventually(() => target.Pasted.Count == 1 && shown.Uses() == 1);
        TrayDesktop.Run(() =>
        {
            Assert.IsFalse(shown.Window.IsVisible, "a click pastes as Enter does, the flyout hidden first (G8, G2)");
            Assert.AreEqual(previewed.Id, shown.Preview.Item?.Id, "with the preview still open on its item");
        });
        CollectionAssert.AreEqual(new[] { previewed.FilePath }, TestClipboard.Read().Files.ToList(), "the previewed item, not the two the grid selects");
        Assert.AreEqual(1, (await shown.Listing.Store!.UseHistory(previewed.Id)).Value!.Count, "one use on its item (G10)");

        TrayDesktop.Run(shown.Window.Show);
        TrayDesktop.Run(() => shown.PreviewPicture.Focus(NavigationMethod.Tab));
        TrayDesktop.Run(() => ShownWindow.Press((Control)shown.Focused!, Key.Tab));
        TrayDesktop.Run(() => ShownWindow.Press((Control)shown.Focused!, Key.Tab));
        TrayDesktop.Run(() => ShownWindow.Press((Control)shown.Focused!, Key.Tab));
        TrayDesktop.Run(() => Assert.AreSame(shown.CopyButton, shown.Focused, "Tab reaches Copy after the tag field and the handoff button (G7)"));
        TrayDesktop.Run(() =>
        {
            ShownWindow.Press(shown.CopyButton, Key.Space);
            ShownWindow.Release(shown.CopyButton, Key.Space);
        });
        await Eventually(() => shown.Uses() == 2);
        TrayDesktop.Run(() => Assert.IsTrue(shown.Window.IsVisible && shown.Preview.IsOpen, "Space on Copy copies and the window stays (G9)"));
        Assert.HasCount(1, target.Pasted, "with no keystroke");

        TrayDesktop.Run(() => ShownWindow.Press(shown.CopyButton, Key.Enter));
        await Eventually(() => target.Pasted.Count == 2 && shown.Uses() == 3);
        TrayDesktop.Run(() => Assert.IsFalse(shown.Window.IsVisible, "Enter there is the handoff, as everywhere outside a field (G7, G8)"));

        TrayDesktop.Run(shown.Window.Show);
        TrayDesktop.Run(() => shown.Window.PasteTarget.FromActivation(new PresetTarget { Before = null }));
        TrayDesktop.Run(() =>
        {
            CollectionAssert.AreEqual(new[] { "Copy", "Enter" }, ShownWindow.Words(shown.HandoffButton), "the target gone to none, it reads Copy again");
            Assert.IsFalse(shown.CopyButton.IsVisible);
            shown.HandoffButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        });
        await Eventually(() => shown.Uses() == 4);
        TrayDesktop.Run(() => Assert.IsTrue(shown.Window.IsVisible, "with the target gone to none, the button copies and the window stays (G8)"));
        Assert.HasCount(2, target.Pasted);
        Assert.AreEqual(4, (await shown.Listing.Store!.UseHistory(previewed.Id)).Value!.Count, "every one on the previewed item");
    }

    [TestMethod]
    public async Task WithALongAppNameThePreviewsHandoffButtonTrimsItsWordsAndCopyStaysInsideTheWindowWithinThePointersReach()
    {
        using var shown = ShownWindow.Open();
        var item = (await shown.OpenStash(shown.Picture("long name.png"))).Single();
        var longName = new string('W', 100);
        TrayDesktop.Run(() => shown.Window.PasteTarget.FromPress(new ForegroundReading(false, PresetTarget.App(longName))));
        TrayDesktop.Run(() => shown.OpenPreview(item));
        // The import's notice, which the check's stopped clock never lets go, closed by its × as a person would: it shows
        // above the preview, over the row.
        TrayDesktop.Run(() => shown.Notices.Shown.ToList().ForEach(shown.Notices.Close));

        // The hit test reads the rendered scene, so it waits until the window has rendered the row.
        await Eventually(() => TrayDesktop.Get(() => Clickable(shown, shown.CopyButton) && Clickable(shown, shown.HandoffButton)));
        TrayDesktop.Run(() =>
        {
            var content = (Control)shown.Window.Content!;
            var copyLeft = shown.CopyButton.TranslatePoint(default, content)!.Value.X;
            var copyRight = shown.CopyButton.TranslatePoint(new Point(shown.CopyButton.Bounds.Width, 0), content)!.Value.X;
            var handoffRight = shown.HandoffButton.TranslatePoint(new Point(shown.HandoffButton.Bounds.Width, 0), content)!.Value.X;
            Assert.IsLessThanOrEqualTo(content.Bounds.Width, copyRight, "Copy wholly inside the window, a click away (G19, G7)");
            Assert.IsLessThanOrEqualTo(copyLeft, handoffRight, "the handoff button beside it, not under it");
            Assert.AreEqual("Paste into " + longName, AutomationProperties.GetName(shown.HandoffButton), "its whole name still read out");
            Assert.AreEqual("Paste into " + longName, ToolTip.GetTip(shown.HandoffButton), "and shown on hover");
        });
    }

    /// <summary>Whether a click at the centre of <paramref name="button"/>, as the window has rendered it, lands on it.</summary>
    private static bool Clickable(ShownWindow shown, Button button)
    {
        var content = (Control)shown.Window.Content!;
        var centre = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), content)!.Value;
        return content.InputHitTest(centre) is Visual hit && (ReferenceEquals(hit, button) || hit.GetVisualAncestors().Contains(button));
    }

    /// <summary>The flyout shown by the open shortcut pressed over <paramref name="inFront"/>, its listing settled and its own activation handled.</summary>
    private static async Task SummonFromShortcut(ShownWindow shown, PasteTarget inFront)
    {
        TrayDesktop.Run(() =>
        {
            shown.Window.Hide();
            shown.Window.SummonFromShortcut(new ForegroundReading(false, inFront), new SummonAnchor(null));
        });
        await TrayDesktop.Get(() => shown.Listing.Settle());
        await Task.Delay(300);
    }
}
