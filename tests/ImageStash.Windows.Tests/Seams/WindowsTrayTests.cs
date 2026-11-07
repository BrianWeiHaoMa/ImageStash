using Avalonia.Controls;
using Avalonia.Input;
using ImageStash.Seams;
using ImageStash.Windows.Seams;
using ImageStash.Windows.Tests.Support;
using Avalonia.Threading;

namespace ImageStash.Windows.Tests.Seams;

/// <summary>
/// The tooltip respects NOTIFYICONDATA's limit; native tray messages reach the click action only for the left button;
/// the menu's entries carry their shortcut and their toggle, whose check mark follows (G34); an entry's words change
/// while it is still found by its header, and the icon carries a red dot while recording (G15, G34); and Open ImageStash
/// chosen from the real menu, opened and clicked with real input, brings the stash window to the front (G1, G34).
/// </summary>
[TestClass]
public sealed class WindowsTrayTests
{
    [TestMethod]
    public void ALeftButtonMessageRunsTheClickActionExactlyOnceOnTheUiThread() => TrayDesktop.Run(() =>
    {
        using var tray = new WindowsTray();
        var clicks = 0;
        var onUiThread = false;
        tray.Show([], "ImageStash left click check", () =>
        {
            clicks++;
            onUiThread = Dispatcher.UIThread.CheckAccess();
        });

        TrayDesktop.SendTrayMouse(tray, 0x0202); // WM_LBUTTONUP

        Assert.AreEqual(1, clicks);
        Assert.IsTrue(onUiThread);
    });

    [TestMethod]
    public void ARightButtonMessageDoesNotRunTheClickAction() => TrayDesktop.Run(() =>
    {
        using var tray = new WindowsTray();
        var clicks = 0;
        // An empty menu exercises Avalonia's right-button branch without leaving a popup on the user's desktop.
        tray.Show([], "ImageStash right click check", () => clicks++);

        TrayDesktop.SendTrayMouse(tray, 0x0205); // WM_RBUTTONUP

        Assert.AreEqual(0, clicks);
    });

    [TestMethod]
    public void TheMenusEntriesCarryTheirShortcutAndTheToggleItsCheckMark() => TrayDesktop.Run(() =>
    {
        using var tray = new WindowsTray();
        var open = new KeyGesture(Key.Space, KeyModifiers.Control | KeyModifiers.Alt);
        tray.Show([new TrayMenuEntry("Open", () => { }, open), new TrayMenuEntry("Pause", () => { }, Checked: false), new TrayMenuEntry("Quit", () => { })], "ImageStash menu check", () => { });
        var items = tray.Icon!.Menu!.Items.OfType<NativeMenuItem>().ToList();

        Assert.AreEqual(open, items[0].Gesture);
        Assert.AreEqual(MenuItemToggleType.None, items[0].ToggleType);
        Assert.AreEqual(MenuItemToggleType.CheckBox, items[1].ToggleType);
        Assert.IsFalse(items[1].IsChecked);
        Assert.IsNull(items[2].Gesture);

        tray.SetChecked("Pause", true);
        Assert.IsTrue(items[1].IsChecked);
        tray.SetChecked("Pause", false);
        Assert.IsFalse(items[1].IsChecked);
    });

    [TestMethod]
    public void AnEntrysWordsChangeUnderItsOwnHeaderAndTheIconCarriesTheRedDotWhileRecording() => TrayDesktop.Run(() =>
    {
        using var tray = new WindowsTray();
        tray.Show([new TrayMenuEntry("Open", () => { }), new TrayMenuEntry("Record a clip", () => { }), new TrayMenuEntry("Quit", () => { })], "ImageStash recording check", () => { });
        var items = tray.Icon!.Menu!.Items.OfType<NativeMenuItem>().ToList();
        var plain = tray.Icon.Icon;

        tray.SetHeader("Record a clip", "Stop recording");
        tray.SetRecording(true);
        Assert.AreEqual("Stop recording", items[1].Header, "the menu reads it at its next opening (G34)");
        Assert.AreEqual("Open", items[0].Header, "no other entry changed");
        Assert.IsNotNull(tray.Icon.Icon);
        Assert.AreNotSame(plain, tray.Icon.Icon, "the icon with its red dot (G15)");

        tray.SetHeader("Record a clip", "Record a clip");
        tray.SetRecording(false);
        Assert.AreEqual("Record a clip", items[1].Header, "still found by the header it was shown with");
        Assert.AreSame(plain, tray.Icon.Icon, "the plain icon again");
    });

    [TestMethod]
    public void AnEntrysShortcutChangesUnderItsOwnHeader() => TrayDesktop.Run(() =>
    {
        using var tray = new WindowsTray();
        var open = new KeyGesture(Key.Space, KeyModifiers.Control | KeyModifiers.Alt);
        var remapped = new KeyGesture(Key.F8, KeyModifiers.Control | KeyModifiers.Shift);
        tray.Show([new TrayMenuEntry("Open", () => { }, open), new TrayMenuEntry("Take a screenshot", () => { }, new KeyGesture(Key.PrintScreen, KeyModifiers.Control)), new TrayMenuEntry("Quit", () => { })],
            "ImageStash shortcut check", () => { });
        var items = tray.Icon!.Menu!.Items.OfType<NativeMenuItem>().ToList();

        tray.SetGesture("Take a screenshot", remapped);
        Assert.AreEqual(remapped, items[1].Gesture, "the menu shows it from its next opening (G34)");
        Assert.AreEqual(open, items[0].Gesture, "no other entry changed");
        Assert.IsNull(items[2].Gesture);

        tray.SetGesture("Take a screenshot", null);
        Assert.IsNull(items[1].Gesture, "a setting that is not a shortcut shows none");
    });

    [TestMethod]
    public async Task OpenChosenInTheRealMenuBringsThePinnedWindowBehindAnotherForwardTenTimesInARowAndAHiddenFlyoutToo()
    {
        using var pointer = TestMouse.PutBackAfterwards();
        using var shown = ShownWindow.Open();
        await shown.OpenStash();
        using var other = FrontWindow.Show(new Avalonia.PixelPoint(100, 400));
        var window = shown.Window;
        var tray = new WindowsTray();
        try
        {
            TrayDesktop.Run(() =>
            {
                tray.Show([new TrayMenuEntry("Open ImageStash", window.SummonFromTray, MenuClosed: window.KeepForward), new TrayMenuEntry("Quit ImageStash", () => { })], "ImageStash menu's Open check", () => { });
                window.ShowActivated = true;
                window.SummonPinned();
            });
            var handle = TrayDesktop.Get(() => window.TryGetPlatformHandle()!.Handle);
            FrontWindow.WaitUntilInFront(handle);
            await Task.Delay(300);
            // Overlapping the other window, whose middle stays clear for the click that puts it in front.
            TrayDesktop.Run(() => window.Position = new Avalonia.PixelPoint(320, 40));
            await Task.Delay(300);
            var at = TrayDesktop.Get(() => window.Position);

            for (var attempt = 1; attempt <= 10; attempt++)
            {
                other.ClickToFront();
                var clock = System.Diagnostics.Stopwatch.StartNew();
                TrayDesktop.ChooseFromRealMenu(tray, 0, 2);
                var chosen = clock.ElapsedMilliseconds;
                FrontWindow.WaitUntilInFront(handle);
                Assert.IsLessThan(1000, clock.ElapsedMilliseconds - chosen, $"attempt {attempt}: in front within a second (G1)");
                await Task.Delay(300);

                Assert.AreEqual(handle, FrontWindow.GetForegroundWindow(), $"attempt {attempt}: still in front once the menu has gone (G1, G34)");
                TrayDesktop.Run(() =>
                {
                    Assert.AreEqual(at, window.Position, $"attempt {attempt}: where it was (G1)");
                    Assert.AreSame(shown.Search, shown.Focused, $"attempt {attempt}: the box focused (G1)");
                });
            }

            TrayDesktop.Run(() =>
            {
                window.Close();
                window.ShowActivated = true;
            });
            // Closing hands the window's activation on for some tens of milliseconds, and a tray menu opened inside that
            // hand-over was seen to close at once; no person's right-click follows a Close that soon.
            FrontWindow.WaitUntilTheFrontSettles(300);
            other.ClickToFront();
            TrayDesktop.ChooseFromRealMenu(tray, 0, 2);
            await ShownWindow.Eventually(() => FrontWindow.GetForegroundWindow() == TrayDesktop.Get(() => window.TryGetPlatformHandle()!.Handle));
            TrayDesktop.Run(() => Assert.IsTrue(window.IsVisible && !window.ShowInTaskbar, "the hidden window comes as the flyout (G1, G2)"));
        }
        finally
        {
            TrayDesktop.Run(tray.Dispose);
        }
    }

    [TestMethod]
    public async Task OpenChosenThroughARealRightClickOnTheIconBringsThePinnedWindowFromBehindAnotherAppToTheFrontEveryTime()
    {
        using var pointer = TestMouse.PutBackAfterwards();
        using var targets = new WindowsTarget();
        using var shown = ShownWindow.Open(platformTarget: targets);
        await shown.OpenStash();
        var window = shown.Window;
        var tray = new WindowsTray();
        try
        {
            TrayDesktop.Run(() =>
            {
                tray.Show([new TrayMenuEntry("Open ImageStash", window.SummonFromTray, MenuClosed: window.KeepForward), new TrayMenuEntry("Quit ImageStash", () => { })], "ImageStash real click check", () => { });
                window.ShowActivated = true;
                window.SummonPinned();
            });
            await Task.Delay(1000);
            var handle = TrayDesktop.Get(() => window.TryGetPlatformHandle()!.Handle);
            FrontWindow.WaitUntilInFront(handle);
            // Overlapping the app, whose title bar stays clear for the click that puts it in front.
            TrayDesktop.Run(() => window.Position = new Avalonia.PixelPoint(320, 180));
            await Task.Delay(300);
            using var app = TargetApp.Start("ImageStash check over the pinned window", 100, 100);
            var outcomes = new List<string>();
            for (var attempt = 1; attempt <= 10; attempt++)
            {
                app.ClickTitleBar();
                FrontWindow.WaitUntilInFront(app.Window);
                await Task.Delay(200);
                TrayDesktop.ChooseFromRealMenuThroughTheIcon(tray, 0, 2);
                var clock = System.Diagnostics.Stopwatch.StartNew();
                while (FrontWindow.GetForegroundWindow() != handle && clock.ElapsedMilliseconds < 1500)
                {
                    await Task.Delay(20);
                }

                await Task.Delay(400);
                var front = FrontWindow.GetForegroundWindow();
                outcomes.Add((front == handle ? "front" : $"{front:X} \"{FrontWindow.NativeText(front)}\"") + " target=" + TrayDesktop.Get(() => window.PasteTarget.Current?.AppName ?? "none"));
            }

            Assert.IsTrue(outcomes.All(outcome => outcome == "front target=none"), $"in front, and the paste target read, at each attempt: {string.Join("; ", outcomes)}");
        }
        finally
        {
            TrayDesktop.Run(tray.Dispose);
        }
    }

    [TestMethod]
    public void AnEntrysMenuClosedRunsOnTheUiThreadAfterItsChosenAndNotInsideTheClick() => TrayDesktop.Run(() =>
    {
        using var tray = new WindowsTray();
        var steps = new List<string>();
        tray.Show([new TrayMenuEntry("Open", () => steps.Add("chosen"), MenuClosed: () => steps.Add("closed"))], "ImageStash menu closed check", () => { });

        ((INativeMenuItemExporterEventsImplBridge)tray.Icon!.Menu!.Items.OfType<NativeMenuItem>().Single()).RaiseClicked();
        CollectionAssert.AreEqual(new[] { "chosen" }, steps, "the menu is still there during the click");

        Dispatcher.UIThread.RunJobs();
        CollectionAssert.AreEqual(new[] { "chosen", "closed" }, steps);
    });

    [TestMethod]
    public void ATooltipWithinTheLimitIsKeptWhole()
    {
        var text = new string('a', WindowsTray.ToolTipLimit);

        Assert.AreEqual(text, WindowsTray.Cut(text));
    }

    [TestMethod]
    public void ALongerTooltipIsCutAtTheLimitWithoutSplittingACharacter()
    {
        Assert.AreEqual(new string('a', 127), WindowsTray.Cut(new string('a', 300)));

        var emojiAcrossTheLimit = new string('a', 126) + "\U0001F600" + "tail";
        Assert.AreEqual(new string('a', 126), WindowsTray.Cut(emojiAcrossTheLimit));
    }
}
