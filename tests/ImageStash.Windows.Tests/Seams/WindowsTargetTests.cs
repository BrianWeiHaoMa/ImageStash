using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia;
using ImageStash.Seams;
using ImageStash.Windows.Seams;
using ImageStash.Window;
using ImageStash.Windows.Tests.Support;

namespace ImageStash.Windows.Tests.Seams;

/// <summary>
/// The real paste target (G1, G8, S6, S15, N3, R6) against windows of another program and of the check's own: the window
/// in front and the one before ImageStash's own, named by their app; the desktop and the taskbar as none, a click on the
/// tray included; the shell's task switcher passed over, and a switch cancelled back to ImageStash's own leaving none,
/// also when the seam hears of the switcher late;
/// a window that is gone; integrity levels compared; one Ctrl+V landing in the other program's edit box and nothing
/// else; no keystroke when the target cannot be brought to the front, a minimised one restored, a minimised one whose
/// thread is busy refused within the bound and never pasted late; an activation read before the seam heard of the app
/// clicked from taking that app once its report comes; and the caret of the other program's edit box,
/// none for a window without one, a Chromium textarea's through its accessibility caret and its UI Automation selection
/// (S15), and a lookup that hangs abandoned at its bound. These checks click windows of their own on the primary
/// display, the tray icon of their own, press Alt+Tab, Win+Tab and Esc, replace the clipboard's contents, suspend the
/// window thread of a PowerShell of their own for a moment, and start Microsoft Edge with a profile of their own under
/// %TEMP%; three hold the seam's own thread, through a switch or two clicks.
/// </summary>
[TestClass]
public sealed class WindowsTargetTests
{
    private static readonly TimeSpan Short = TimeSpan.FromMilliseconds(300);

    [TestMethod]
    public void AnotherProgramsWindowInFrontIsTheTargetByItsAppsNameAndStaysTheOneBeforeImageStashsOwn()
    {
        using var pointer = TestMouse.PutBackAfterwards();
        using var seam = new WindowsTarget();
        using var other = TargetApp.Start("ImageStash check target", DisplayPlaces.At(DisplayPlaces.Primary(), 100, 100));
        using var own = FrontWindow.Show(DisplayPlaces.At(DisplayPlaces.Primary(), 600, 100));

        other.ClickTitleBar();
        var inFront = seam.Foreground();
        Eventually(() => seam.LastBeforeMine() is not null);
        var beforeWhileOtherInFront = seam.LastBeforeMine();
        own.ClickToFront();
        var mineInFront = seam.Foreground();
        var beforeMine = seam.LastBeforeMine();

        var expected = new PasteTarget(other.Window, "Windows PowerShell");
        Assert.AreEqual(new ForegroundReading(false, expected), inFront, "named by its executable's file description, not its title (G8)");
        Assert.AreEqual(expected, beforeWhileOtherInFront);
        Assert.AreEqual(new ForegroundReading(true, null), mineInFront, "ImageStash's own process is never a target (G8)");
        Assert.AreEqual(expected, beforeMine, "the window that had focus just before ImageStash's own (G8)");
        Assert.IsTrue(seam.Confirm(expected).Succeeded, "a window of the same rights may be sent input (S6)");
    }

    [TestMethod]
    public void AWindowOfImageStashsOwnJustBeforeLeavesNoTarget()
    {
        using var pointer = TestMouse.PutBackAfterwards();
        using var seam = new WindowsTarget();
        using var other = TargetApp.Start("ImageStash check before a dialog", DisplayPlaces.At(DisplayPlaces.Primary(), 100, 100));
        using var stash = FrontWindow.Show(DisplayPlaces.At(DisplayPlaces.Primary(), 600, 100));
        using var dialog = FrontWindow.Show(DisplayPlaces.At(DisplayPlaces.Primary(), 600, 350));

        other.ClickTitleBar();
        stash.ClickToFront();
        var fromTheApp = seam.LastBeforeMine();
        dialog.ClickToFront();
        stash.ClickToFront();
        Thread.Sleep(200);
        var fromTheDialog = seam.LastBeforeMine();

        Assert.AreEqual(new PasteTarget(other.Window, "Windows PowerShell"), fromTheApp, "the window that had focus just before (G8)");
        Assert.IsNull(fromTheDialog, "none when that window was ImageStash itself, as a dialog of its own is (G8)");
    }

    [TestMethod]
    public void AWindowOfHigherOrUnreadableRightsIsRefusedByTheConfirmation()
    {
        using var pointer = TestMouse.PutBackAfterwards();
        using var other = TargetApp.Start("ImageStash check elevated stand-in", DisplayPlaces.At(DisplayPlaces.Primary(), 100, 100));
        other.ClickTitleBar();
        var target = new PasteTarget(other.Window, "Windows PowerShell");
        var own = WindowsTarget.IntegrityOf((uint)Environment.ProcessId);
        using var elevated = new WindowsTarget(process => process == other.ProcessId ? 0x3000 : own);
        using var unreadable = new WindowsTarget(process => process == other.ProcessId ? null : own);

        Assert.AreEqual($"Paste failed: Windows PowerShell, {PasteTargetReasons.HigherRights}", elevated.Confirm(target).Failure?.Sentence, "an elevated app (E6, S6)");
        Assert.AreEqual($"Paste failed: Windows PowerShell, {PasteTargetReasons.RightsUnreadable}", unreadable.Confirm(target).Failure?.Sentence, "a level it cannot read counts as above (S6)");
    }

    [TestMethod]
    public async Task ThePasteBringsTheTargetToTheFrontAndSendsItOneCtrlVAndNothingElse()
    {
        using var pointer = TestMouse.PutBackAfterwards();
        using var seam = new WindowsTarget();
        using var other = TargetApp.Start("ImageStash check paste target", DisplayPlaces.At(DisplayPlaces.Primary(), 100, 100));
        using var own = FrontWindow.Show(DisplayPlaces.At(DisplayPlaces.Primary(), 600, 100));
        other.ClickTitleBar();
        var target = seam.Foreground().Target!;
        own.ClickToFront();
        TestClipboard.PutText("probe text");
        var before = other.Messages().Count;

        var clock = Stopwatch.StartNew();
        var pasted = await seam.ActivateAndPaste(target, TimeSpan.FromSeconds(1));
        var took = clock.ElapsedMilliseconds;

        Assert.IsTrue(pasted.Succeeded, pasted.Failure?.Sentence);
        Assert.AreEqual(other.Window, FrontWindow.GetForegroundWindow(), "the target is in front");
        Eventually(() => other.Text() == "probe text");
        var keys = other.Messages().Skip(before).Where(message => message.StartsWith("WM_KEY") || message.StartsWith("WM_SYSKEY")).ToList();
        CollectionAssert.AreEqual(new[] { "WM_KEYDOWN:0x11", "WM_KEYDOWN:0x56", "WM_KEYUP:0x56", "WM_KEYUP:0x11" }, keys, "one Ctrl+V and nothing else (N3, R6)");
        Assert.IsEmpty(own.Keys(), "nothing reached the window that was in front before");
        Assert.IsLessThan(1000, took);
    }

    /// <summary>
    /// G8, R6: a key the user still holds, Alt during a double-click, would join the Ctrl+V and make another chord of it,
    /// since SendInput leaves the keyboard's state as it is. The paste waits for it to be let go, and one still held at
    /// the bound sends nothing and fails, so the copy's notification follows. Alt is pressed while another program's
    /// window is in front, as after the flyout has hidden; its release then reaches the target, which is the user's key.
    /// </summary>
    [TestMethod]
    public async Task APasteWaitsForAKeyTheUserStillHoldsAndOneHeldPastTheBoundSendsNothing()
    {
        using var pointer = TestMouse.PutBackAfterwards();
        using var seam = new WindowsTarget();
        using var other = TargetApp.Start("ImageStash check held key target", DisplayPlaces.At(DisplayPlaces.Primary(), 100, 100));
        using var bystander = TargetApp.Start("ImageStash check held key bystander", DisplayPlaces.At(DisplayPlaces.Primary(), 100, 400), editBox: false);
        other.ClickTitleBar();
        var target = seam.Foreground().Target!;
        TestClipboard.PutText("probe text");

        bystander.ClickTitleBar();
        var before = other.Messages().Count;
        Outcome held;
        using (TestHotKeys.HoldAlt())
        {
            held = await seam.ActivateAndPaste(target, TimeSpan.FromMilliseconds(500));
        }

        Assert.AreEqual($"Paste failed: Windows PowerShell, {PasteTargetReasons.KeyHeld}", held.Failure?.Sentence);
        Assert.IsEmpty(SentKeys(other, before), "nothing was sent while Alt was held (R6)");

        bystander.ClickTitleBar();
        before = other.Messages().Count;
        Task<Outcome> pasting;
        using (TestHotKeys.HoldAlt())
        {
            pasting = seam.ActivateAndPaste(target, TimeSpan.FromSeconds(1));
            await Task.Delay(300);
        }

        var pasted = await pasting;
        Assert.IsTrue(pasted.Succeeded, pasted.Failure?.Sentence);
        Assert.AreEqual(other.Window, FrontWindow.GetForegroundWindow(), "the target is in front");
        Eventually(() => other.Text() == "probe text");
        CollectionAssert.AreEqual(new[] { "WM_KEYDOWN:0x11", "WM_KEYDOWN:0x56", "WM_KEYUP:0x56", "WM_KEYUP:0x11" }, SentKeys(other, before), "one Ctrl+V once Alt was let go, and nothing else (N3, R6)");
    }

    /// <summary>The key messages <paramref name="app"/> received after its first <paramref name="before"/> messages, but those of Alt, the user's own key here.</summary>
    private static List<string> SentKeys(TargetApp app, int before) =>
        app.Messages().Skip(before).Where(message => (message.StartsWith("WM_KEY") || message.StartsWith("WM_SYSKEY")) && !message.EndsWith(":0x12")).ToList();

    [TestMethod]
    public async Task AClosedTargetIsGoneAndAHandleThatNeverComesToTheFrontFailsWithinTheBoundSendingNothing()
    {
        using var pointer = TestMouse.PutBackAfterwards();
        using var seam = new WindowsTarget();
        using var other = TargetApp.Start("ImageStash check closing target", DisplayPlaces.At(DisplayPlaces.Primary(), 100, 100));
        using var own = FrontWindow.Show(DisplayPlaces.At(DisplayPlaces.Primary(), 600, 100));
        other.ClickTitleBar();
        var target = seam.Foreground().Target!;

        other.Close();
        var confirmed = seam.Confirm(target);
        own.ClickToFront();
        var clock = Stopwatch.StartNew();
        var pasted = await seam.ActivateAndPaste(target, Short);
        var took = clock.ElapsedMilliseconds;

        Assert.AreEqual("Paste failed: Windows PowerShell, the window is gone", confirmed.Failure?.Sentence);
        Assert.AreEqual($"Paste failed: Windows PowerShell, {PasteTargetReasons.NotBroughtToFront}", pasted.Failure?.Sentence);
        Assert.IsGreaterThanOrEqualTo(300, took, "it waited the bound");
        Assert.IsLessThan(1300, took, "and no longer than the bound and a margin");
        Assert.IsEmpty(own.Keys(), "no keystroke went to the window in front (N3)");
    }

    [TestMethod]
    public void TheDesktopAndTheTaskbarAreNoTargetAndTheShellIsNamedByItsDescription()
    {
        using var seam = new WindowsTarget();
        using var own = FrontWindow.Show(DisplayPlaces.At(DisplayPlaces.Primary(), 600, 100));
        var taskbar = FindWindowW("Shell_TrayWnd", null);
        Assert.AreNotEqual(nint.Zero, taskbar, "this desktop has a taskbar");

        Assert.AreEqual(new ForegroundReading(false, null), seam.Read(taskbar));
        Assert.AreEqual(new ForegroundReading(false, null), seam.Read(GetShellWindow()), "the desktop");
        Assert.AreEqual("Windows Explorer", WindowsTarget.AppNameOf(taskbar), "the shell's file description");
        Assert.AreEqual(new ForegroundReading(true, null), seam.Read(own.Handle));
        Assert.AreEqual(new ForegroundReading(false, null), seam.Read(0), "no window in front");
    }

    [TestMethod]
    public async Task AClickOnTheTrayLeavesNoTargetBeforeImageStashsOwnWindow()
    {
        using var pointer = TestMouse.PutBackAfterwards();
        var tray = new WindowsTray();
        try
        {
            TrayDesktop.Run(() => tray.Show([], "ImageStash Windows check", () => { }));
            // Explorer registers the icon while the UI thread keeps pumping.
            await Task.Delay(1000);
            using var seam = new WindowsTarget();
            using var other = TargetApp.Start("ImageStash check before the tray", DisplayPlaces.At(DisplayPlaces.Primary(), 100, 100));
            using var own = FrontWindow.Show(DisplayPlaces.At(DisplayPlaces.Primary(), 600, 100));
            other.ClickTitleBar();
            Eventually(() => seam.LastBeforeMine() is not null);
            var icon = TrayDesktop.Get(() => new WindowsQuietWindows(tray).TrayIconRectangle());
            Assert.IsNotNull(icon, "the check's tray icon has a place");

            TestMouse.Click(icon.Value.Center);
            await Task.Delay(500);
            var afterClick = FrontWindow.GetForegroundWindow();
            Console.WriteLine($"in front after the click on the tray icon at {icon}: {ClassName(afterClick)}");
            var readAfterClick = seam.Read(afterClick);
            own.ClickToFront();
            var before = seam.LastBeforeMine();

            Assert.AreEqual(new ForegroundReading(false, null), readAfterClick, $"{ClassName(afterClick)} is the taskbar (G8)");
            Assert.IsNull(before, "from the tray, the target is none, so the footer reads Copy (G8, In use)");
        }
        finally
        {
            TrayDesktop.Run(tray.Dispose);
        }
    }

    [TestMethod]
    public void AnAltTabFromAnotherAppPassesOverTheTaskSwitcherToThatApp()
    {
        using var pointer = TestMouse.PutBackAfterwards();
        using var seam = new WindowsTarget();
        using var other = TargetApp.Start("ImageStash check before a switch", DisplayPlaces.At(DisplayPlaces.Primary(), 100, 100));
        using var own = FrontWindow.Show(DisplayPlaces.At(DisplayPlaces.Primary(), 600, 100));
        var expected = new PasteTarget(other.Window, "Windows PowerShell");
        other.ClickTitleBar();

        // Released at once, and held until the switcher shows, the window in front then read as a press reads it.
        foreach (var held in new[] { TimeSpan.Zero, TimeSpan.FromMilliseconds(400) })
        {
            own.ClickToFront();
            other.ClickTitleBar();
            Eventually(() => seam.LastBeforeMine() is { } app && app.Handle == other.Window);
            var whileHeld = (Front: nint.Zero, Reading: default(ForegroundReading));
            TestHotKeys.SwitchWindows(held, () => whileHeld = (FrontWindow.GetForegroundWindow(), seam.Foreground()));
            FrontWindow.WaitUntilInFront(own.Handle);
            Thread.Sleep(200);

            Assert.AreEqual(expected, seam.LastBeforeMine(), $"Alt held {held.TotalMilliseconds} ms: the shell's task switcher, which Windows puts in front between the two, is passed over for the app the user came from (G8)");
            if (held > TimeSpan.Zero)
            {
                Console.WriteLine($"in front while Alt was held: {ClassName(whileHeld.Front)}");
                Assert.IsFalse(whileHeld.Front == other.Window || whileHeld.Front == own.Handle, "the switcher is in front while Alt is held");
                Assert.AreEqual(new ForegroundReading(false, expected), whileHeld.Reading, "the switcher in front at a press is passed over too");
            }
        }
    }

    [TestMethod]
    public void ASwitchCancelledBackToImageStashsOwnWindowLeavesNoTarget()
    {
        using var pointer = TestMouse.PutBackAfterwards();
        using var seam = new WindowsTarget();
        using var other = TargetApp.Start("ImageStash check before a cancelled switch", DisplayPlaces.At(DisplayPlaces.Primary(), 100, 100));
        using var own = FrontWindow.Show(DisplayPlaces.At(DisplayPlaces.Primary(), 600, 100));
        var escape = Shortcut.Parse("Esc").Value!;

        // Alt+Tab held then cancelled with Esc, and Task View opened then closed with Esc, each from the window of
        // ImageStash's own activated from the other app.
        var switches = new (string Name, Action CancelledSwitch)[]
        {
            ("Alt+Tab", () => TestHotKeys.SwitchWindows(TimeSpan.FromMilliseconds(400), () => TestHotKeys.Press(escape))),
            ("Task View", () =>
            {
                TestHotKeys.Press(Shortcut.Parse("Win+Tab").Value!);
                Thread.Sleep(800);
                TestHotKeys.Press(escape);
            }),
        };
        foreach (var (name, cancelledSwitch) in switches)
        {
            other.ClickTitleBar();
            own.ClickToFront();
            Eventually(() => seam.LastBeforeMine() is { } app && app.Handle == other.Window);
            cancelledSwitch();
            FrontWindow.WaitUntilInFront(own.Handle);
            Thread.Sleep(200);

            Assert.IsNull(seam.LastBeforeMine(), $"{name} cancelled: passing over the shell's task switcher, the window before is ImageStash's own (G8)");
        }
    }

    [TestMethod]
    public void ASwitchCancelledThatTheSeamHearsOfLateStillLeavesNoTarget()
    {
        using var pointer = TestMouse.PutBackAfterwards();
        using var seam = new WindowsTarget();
        using var other = TargetApp.Start("ImageStash check before a switch heard late", DisplayPlaces.At(DisplayPlaces.Primary(), 100, 100));
        using var own = FrontWindow.Show(DisplayPlaces.At(DisplayPlaces.Primary(), 600, 100));
        var escape = Shortcut.Parse("Esc").Value!;
        other.ClickTitleBar();
        own.ClickToFront();
        Eventually(() => seam.LastBeforeMine() is { } app && app.Handle == other.Window);
        var hook = OpenThread(ThreadSuspendResume, false, seam.HookThread);
        Assert.AreNotEqual(nint.Zero, hook);
        var inFrontWhileHeld = "";
        try
        {
            // The hook's reports wait, as on a busy PC, until the switcher itself has replaced its staging window in front.
            Assert.AreNotEqual(uint.MaxValue, SuspendThread(hook), "the seam's thread is held");
            TestHotKeys.SwitchWindows(TimeSpan.FromMilliseconds(400), () =>
            {
                Eventually(() => ClassName(FrontWindow.GetForegroundWindow()) == "XamlExplorerHostIslandWindow");
                inFrontWhileHeld = ClassName(FrontWindow.GetForegroundWindow());
                ResumeThread(hook);
                Thread.Sleep(200);
                TestHotKeys.Press(escape);
            });
        }
        finally
        {
            ResumeThread(hook);
            CloseHandle(hook);
        }

        FrontWindow.WaitUntilInFront(own.Handle);
        Thread.Sleep(200);

        Assert.AreEqual("XamlExplorerHostIslandWindow", inFrontWhileHeld, "the switcher was in front, so ImageStash's window lost the focus and gained it back (G8)");
        Assert.IsNull(seam.LastBeforeMine(), "the switcher's staging window, reported once the switcher had replaced it, is still passed over to ImageStash's own (G8)");
    }

    /// <summary>
    /// G8: the activation is read while the seam has not yet heard that the app clicked from changed; once its report
    /// comes, the target is that app, not the one before it.
    /// </summary>
    [TestMethod]
    public void AnActivationReadBeforeTheSeamHearsOfTheAppClickedFromTakesThatAppOnceItsReportComes()
    {
        using var pointer = TestMouse.PutBackAfterwards();
        using var seam = new WindowsTarget();
        using var earlier = TargetApp.Start("ImageStash check earlier app", DisplayPlaces.At(DisplayPlaces.Primary(), 100, 100));
        using var current = TargetApp.Start("ImageStash check current app", DisplayPlaces.At(DisplayPlaces.Primary(), 100, 420));
        using var own = FrontWindow.Show(DisplayPlaces.At(DisplayPlaces.Primary(), 650, 100));
        var holder = new PasteTargetHolder();
        earlier.ClickTitleBar();
        own.ClickToFront();
        Eventually(() => seam.LastBeforeMine()?.Handle == earlier.Window);
        holder.FromActivation(seam);

        PasteTarget? whileBehind;
        using (Suspended(seam.HookThread))
        {
            current.ClickTitleBar();
            own.ClickToFront();
            holder.FromActivation(seam);
            whileBehind = holder.Current;
        }

        Assert.IsNull(whileBehind, "while the seam is behind, Enter copies rather than pasting into the app left (G8)");
        Eventually(() => seam.LastBeforeMine()?.Handle == current.Window);
        Eventually(() => holder.Current?.Handle == current.Window);
    }

    /// <summary>
    /// G8: Enter pressed right after a click from another app reads the window before ImageStash's while the seam's
    /// thread is still busy taking that click's report: the read waits for it, briefly, and answers the app clicked from.
    /// </summary>
    [TestMethod]
    public void AReadAskedWhileTheSeamIsBrieflyBehindWaitsForTheReportOfTheAppClickedFrom()
    {
        using var pointer = TestMouse.PutBackAfterwards();
        using var seam = new WindowsTarget();
        using var earlier = TargetApp.Start("ImageStash check earlier app read", DisplayPlaces.At(DisplayPlaces.Primary(), 100, 100));
        using var current = TargetApp.Start("ImageStash check current app read", DisplayPlaces.At(DisplayPlaces.Primary(), 100, 420));
        using var own = FrontWindow.Show(DisplayPlaces.At(DisplayPlaces.Primary(), 650, 100));
        earlier.ClickTitleBar();
        own.ClickToFront();
        Eventually(() => seam.LastBeforeMine()?.Handle == earlier.Window);

        PasteTarget? read;
        var held = Suspended(seam.HookThread);
        try
        {
            current.ClickTitleBar();
            own.ClickToFront();
            _ = Task.Delay(100).ContinueWith(_ => held.Dispose());
            read = seam.LastBeforeMine();
        }
        finally
        {
            held.Dispose();
        }

        Assert.AreEqual(current.Window, read?.Handle, "the read waited for the report the seam's thread was about to take");
    }

    /// <summary>
    /// G8, N3, R6: a minimised target whose window thread is busy cannot hold the paste past its bound, sends nothing
    /// once its thread answers again, and holds up no later paste.
    /// </summary>
    [TestMethod]
    public async Task AMinimisedTargetWhoseThreadIsBusyFailsWithinTheBoundAndNeverPastesLate()
    {
        using var pointer = TestMouse.PutBackAfterwards();
        using var seam = new WindowsTarget();
        using var busy = TargetApp.Start("ImageStash check busy minimised target", DisplayPlaces.At(DisplayPlaces.Primary(), 100, 100));
        using var responsive = TargetApp.Start("ImageStash check responsive target", DisplayPlaces.At(DisplayPlaces.Primary(), 100, 420));
        using var own = FrontWindow.Show(DisplayPlaces.At(DisplayPlaces.Primary(), 650, 100));
        responsive.ClickTitleBar();
        var next = seam.Foreground().Target!;
        busy.ClickTitleBar();
        var target = seam.Foreground().Target!;
        ShowWindowAsync(busy.Window, SwMinimize);
        Eventually(() => IsIconic(busy.Window));
        own.ClickToFront();
        TestClipboard.PutText("probe text");
        var before = busy.Messages().Count;
        Task<Outcome> pasting;
        bool inTime;
        using (Suspended(GetWindowThreadProcessId(busy.Window, out _)))
        {
            pasting = seam.ActivateAndPaste(target, Short);
            inTime = await Task.WhenAny(pasting, Task.Delay(1500)) == pasting;
        }

        var pasted = await pasting.WaitAsync(TimeSpan.FromSeconds(10));
        Thread.Sleep(500);

        Assert.IsTrue(inTime, "the paste answered within the bound and a margin, not once the busy thread answered");
        Assert.AreEqual($"Paste failed: Windows PowerShell, {PasteTargetReasons.NotBroughtToFront}", pasted.Failure?.Sentence);
        Assert.IsEmpty(SentKeys(busy, before), "no keystroke reached the target once its thread answered again (N3)");

        own.ClickToFront();
        var clock = Stopwatch.StartNew();
        var later = await seam.ActivateAndPaste(next, TimeSpan.FromSeconds(1));

        Assert.IsTrue(later.Succeeded, later.Failure?.Sentence);
        Assert.IsLessThan(1000, clock.ElapsedMilliseconds, "a later paste is not held behind the one that ran out of time");
        Eventually(() => responsive.Text() == "probe text");
    }

    [TestMethod]
    public async Task AMinimisedTargetThatAnswersIsRestoredBroughtToTheFrontAndPastedWithinTheBound()
    {
        using var pointer = TestMouse.PutBackAfterwards();
        using var seam = new WindowsTarget();
        using var other = TargetApp.Start("ImageStash check minimised target", DisplayPlaces.At(DisplayPlaces.Primary(), 100, 100));
        using var own = FrontWindow.Show(DisplayPlaces.At(DisplayPlaces.Primary(), 650, 100));
        other.ClickTitleBar();
        var target = seam.Foreground().Target!;
        ShowWindowAsync(other.Window, SwMinimize);
        Eventually(() => IsIconic(other.Window));
        own.ClickToFront();
        TestClipboard.PutText("probe text");
        var before = other.Messages().Count;

        var pasted = await seam.ActivateAndPaste(target, TimeSpan.FromSeconds(1));

        Assert.IsTrue(pasted.Succeeded, pasted.Failure?.Sentence);
        Assert.IsFalse(IsIconic(other.Window), "restored (G8)");
        Assert.AreEqual(other.Window, FrontWindow.GetForegroundWindow());
        Eventually(() => other.Text() == "probe text");
        CollectionAssert.AreEqual(new[] { "WM_KEYDOWN:0x11", "WM_KEYDOWN:0x56", "WM_KEYUP:0x56", "WM_KEYUP:0x11" }, SentKeys(other, before), "one Ctrl+V and nothing else (N3, R6)");
    }

    [TestMethod]
    public void ASwitchCancelledWhileTheSeamIsHeldThroughoutStillLeavesNoTarget()
    {
        using var pointer = TestMouse.PutBackAfterwards();
        using var seam = new WindowsTarget();
        using var other = TargetApp.Start("ImageStash check before a switch heard after it", DisplayPlaces.At(DisplayPlaces.Primary(), 100, 100));
        using var own = FrontWindow.Show(DisplayPlaces.At(DisplayPlaces.Primary(), 650, 100));
        other.ClickTitleBar();
        own.ClickToFront();
        Eventually(() => seam.LastBeforeMine()?.Handle == other.Window);
        using (Suspended(seam.HookThread))
        {
            TestHotKeys.SwitchWindows(TimeSpan.FromMilliseconds(400), () => TestHotKeys.Press(Shortcut.Parse("Esc").Value!));
            FrontWindow.WaitUntilInFront(own.Handle);
        }

        Eventually(() => seam.LastBeforeMine() is null);
    }

    /// <summary>Holds <paramref name="thread"/> until disposed, so what it would answer or report comes late.</summary>
    private static IDisposable Suspended(uint thread)
    {
        var handle = OpenThread(ThreadSuspendResume, false, thread);
        Assert.AreNotEqual(nint.Zero, handle);
        if (SuspendThread(handle) == uint.MaxValue)
        {
            CloseHandle(handle);
            Assert.Fail("the thread could not be held");
        }

        return new Resumes(handle);
    }

    private sealed class Resumes(nint handle) : IDisposable
    {
        private int done;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref done, 1) == 0)
            {
                ResumeThread(handle);
                CloseHandle(handle);
            }
        }
    }

    [TestMethod]
    public async Task TheCaretOfAnotherProgramsEditBoxIsItsThreadsWindowsCaretOnTheScreen()
    {
        using var pointer = TestMouse.PutBackAfterwards();
        using var seam = new WindowsTarget();
        // DPI-unaware, as some real chat apps are: its caret comes in its own 96-DPI pixels (E6).
        using var other = TargetApp.Start("ImageStash check caret target", DisplayPlaces.At(DisplayPlaces.Primary(), 100, 100), awareness: TargetAwareness.Unaware);
        other.ClickTitleBar();
        var target = seam.Foreground().Target!;
        Eventually(() => ScreenCaret.Of(other.Window) is not null);
        var expected = ScreenCaret.Of(other.Window);
        Assert.IsNotNull(expected, "the edit box in front shows a caret");
        var editBox = ScreenCaret.FocusOf(other.Window);

        var clock = Stopwatch.StartNew();
        var found = await WindowsCaret.Find(other.Window, ShortcutSummon.CaretBound);
        var took = clock.ElapsedMilliseconds;
        var throughTheSeam = await seam.CaretOf(target, ShortcutSummon.CaretBound);
        var accessible = WindowsCaret.AccessibleCaretOf(editBox);

        Assert.AreEqual(new CaretFound(expected.Value, CaretStep.WindowsCaret), found, "the first step of G1's chain, its client rectangle on the screen");
        Assert.IsLessThan(50, took, "found at once");
        Assert.AreEqual(expected, throughTheSeam);
        // For a DPI-unaware window on a scaled display, Windows' own caret object places its corner by its rounding of the
        // window's points and keeps the window's own size (1 × 16 at 150 % for a caret 2 × 24 on the screen); the chain
        // asks it only for a thread with no Windows caret, so its place is what is compared here. An aware window's is
        // compared whole in WindowsCaretScalingTests.
        Assert.IsNotNull(accessible, "the accessibility caret object, the chain's second step");
        Assert.IsTrue(Math.Abs(accessible.Rectangle.X - expected.Value.X) <= 1 && Math.Abs(accessible.Rectangle.Y - expected.Value.Y) <= 1,
            $"locates the same caret's place on the screen, within the pixel Windows' scaling of the window's points rounds: {accessible.Rectangle} for {expected}");
    }

    [TestMethod]
    public void AWindowsCaretIsScaledFromItsWindowsLogicalPixelsToItsDisplays()
    {
        var caret = new WindowsCaret.Rectangle { Left = 100, Top = 40, Right = 101, Bottom = 56 };
        var origin = new PixelPoint(1000, 500);

        Assert.AreEqual(new PixelRect(1100, 540, 1, 16), WindowsCaret.OnScreen(origin, caret, 1), "a per-monitor aware window: its pixels are its display's");
        Assert.AreEqual(new PixelRect(1200, 580, 2, 32), WindowsCaret.OnScreen(origin, caret, 2), "a DPI-unaware window on a 200 % display: rcCaret is in its own 96-DPI pixels (GetGUIThreadInfo, DPI Virtualization; E1)");
        Assert.AreEqual(new PixelRect(1150, 560, 2, 24), WindowsCaret.OnScreen(origin, caret, 1.5), "and on a 150 % display, rounded");
    }

    [TestMethod]
    public async Task AWindowWithNoCaretAnswersNoneWithinTheBound()
    {
        using var pointer = TestMouse.PutBackAfterwards();
        using var plain = TargetApp.Start("ImageStash check window without a caret", DisplayPlaces.At(DisplayPlaces.Primary(), 100, 100), editBox: false);
        plain.ClickTitleBar();
        Assert.IsNull(ScreenCaret.Of(plain.Window), "its thread shows no Windows caret");

        var clock = Stopwatch.StartNew();
        var found = await WindowsCaret.Find(plain.Window, ShortcutSummon.CaretBound);
        var took = clock.ElapsedMilliseconds;

        Assert.IsNull(found, "no Windows caret, no accessibility caret, no text pattern: the pointer stands in (G1)");
        Assert.IsLessThan(750, took, "within the bound and a margin");
    }

    [TestMethod]
    public async Task AChromiumTextareasCaretIsItsAccessibilityCaretWhereItsCollapsedSelectionIs()
    {
        using var pointer = TestMouse.PutBackAfterwards();
        using var edge = EdgeApp.Textarea();
        if (edge is null)
        {
            Assert.Inconclusive("Microsoft Edge is not installed on this PC, so no Chromium window can be asked");
            return;
        }

        TargetApp.ClickTitleBar(edge.Window);
        Assert.IsTrue(GetWindowRect(edge.Window, out var bounds));
        var clock = Stopwatch.StartNew();
        CaretFound? found = null;
        // Until the page has loaded and its textarea taken the focus; Chromium places its accessibility caret object a
        // moment after that, and meanwhile the chain rightly answers from the next step, its UI Automation selection.
        while (found?.Step != CaretStep.AccessibleCaret && clock.ElapsedMilliseconds < 10_000)
        {
            found = await WindowsCaret.Find(edge.Window, ShortcutSummon.CaretBound);
        }

        // Chromium builds its UI Automation tree once first asked, answering with a pane until it has, and for a moment its
        // collapsed selection has no rectangle yet, when the chain rightly answers from the one character at it.
        CaretFound? selection = null;
        for (clock.Restart(); selection?.Step != CaretStep.Selection && clock.ElapsedMilliseconds < 10_000; Thread.Sleep(100))
        {
            selection = WindowsCaret.SelectionCaretOf(edge.Window, (uint)edge.ProcessId, (uint)edge.ProcessId);
        }

        var otherProcess = WindowsCaret.SelectionCaretOf(edge.Window, (uint)Environment.ProcessId, (uint)Environment.ProcessId);
        var otherWindow = WindowsCaret.SelectionCaretOf(GetShellWindow(), (uint)edge.ProcessId, (uint)edge.ProcessId);
        TestHotKeys.Press(Shortcut.Parse("Ctrl+A").Value!);
        Thread.Sleep(300);
        var selectedText = WindowsCaret.SelectionCaretOf(edge.Window, (uint)edge.ProcessId, (uint)edge.ProcessId);

        Assert.AreEqual(CaretStep.AccessibleCaret, found?.Step, "Chromium shows no Windows caret and answers the accessibility caret object (S15)");
        var caret = found!.Rectangle;
        Assert.IsTrue(caret.X >= bounds.Left && caret.Y >= bounds.Top && caret.Right <= bounds.Right && caret.Bottom <= bounds.Bottom, $"{caret} lies inside Edge's window");
        Assert.AreEqual(CaretStep.Selection, selection?.Step, "its collapsed selection has a rectangle (S15)");
        Assert.IsLessThanOrEqualTo(2, Math.Abs(selection!.Rectangle.X - caret.X), $"the selection {selection.Rectangle} is where the caret {caret} is");
        Assert.IsTrue(selection.Rectangle.Intersects(new PixelRect(caret.X - 2, caret.Y - 2, caret.Width + 4, caret.Height + 4)), $"the selection {selection.Rectangle} is where the caret {caret} is");
        Assert.IsNull(otherProcess, "the focused element of another process lends no caret");
        Assert.IsNull(otherWindow, "nor one in a window that is not in front");
        Assert.IsNull(selectedText, "selected text is no caret: only a collapsed selection is (How the guarantees hold, G1)");
    }

    [TestMethod]
    public async Task AnotherTopLevelWindowOfTheSameThreadLendsNoCaret()
    {
        using var pointer = TestMouse.PutBackAfterwards();
        using var other = TargetApp.Start("ImageStash check caret target", DisplayPlaces.At(DisplayPlaces.Primary(), 100, 100));
        other.ClickTitleBar();
        Assert.IsNotNull(ScreenCaret.Of(other.Window), "the edit box shows a caret");
        // The thread's default IME window: a top-level window of the edit box's own thread.
        var sameThread = ImmGetDefaultIMEWnd(other.Window);
        Assert.AreNotEqual(nint.Zero, sameThread);
        Assert.AreEqual(GetWindowThreadProcessId(other.Window, out _), GetWindowThreadProcessId(sameThread, out _));

        var found = await WindowsCaret.Find(sameThread, ShortcutSummon.CaretBound);

        Assert.IsNull(found, "the thread's caret lies in another window, so this one has none (G1: the focused control in the app the user came from)");
    }

    [TestMethod]
    public async Task ALookupThatHangsIsAbandonedAtTheBoundAndTheNextLookupStillAnswers()
    {
        using var pointer = TestMouse.PutBackAfterwards();
        using var hung = TargetApp.Start("ImageStash check hung caret target", DisplayPlaces.At(DisplayPlaces.Primary(), 100, 100), editBox: false);
        using var other = TargetApp.Start("ImageStash check caret target", DisplayPlaces.At(DisplayPlaces.Primary(), 600, 100));
        hung.ClickTitleBar();
        Assert.IsNull(ScreenCaret.Of(hung.Window), "no Windows caret, so the lookup asks the window itself, which cannot answer");
        var thread = OpenThread(ThreadSuspendResume, false, GetWindowThreadProcessId(hung.Window, out _));
        Assert.AreNotEqual(nint.Zero, thread);
        try
        {
            Assert.AreNotEqual(uint.MaxValue, SuspendThread(thread), "the window's thread is held");
            var clock = Stopwatch.StartNew();
            var abandoned = await WindowsCaret.Find(hung.Window, ShortcutSummon.CaretBound);
            var took = clock.ElapsedMilliseconds;
            other.ClickTitleBar();
            // Its edit box makes its caret as it takes the focus, a moment after the window comes to the front.
            Eventually(() => ScreenCaret.Of(other.Window) is not null);
            clock.Restart();
            var next = await WindowsCaret.Find(other.Window, ShortcutSummon.CaretBound);
            var nextTook = clock.ElapsedMilliseconds;

            Assert.IsNull(abandoned, "the lookup's answer comes too late and is dropped (G1)");
            Assert.IsGreaterThanOrEqualTo(240, took, "it waited the bound");
            Assert.IsLessThan(750, took, "and no longer than the bound and a margin");
            Assert.AreEqual(CaretStep.WindowsCaret, next?.Step, "the next lookup answers while the first is still held");
            Assert.IsLessThan(50, nextTook);
        }
        finally
        {
            ResumeThread(thread);
            CloseHandle(thread);
        }
    }

    [TestMethod]
    public void RightsAboveImageStashsOrUnreadableCountAsAbove()
    {
        Assert.AreEqual(0x2000u, WindowsTarget.IntegrityOf((uint)Environment.ProcessId), "this check runs at medium integrity, as ImageStash does (E1)");
        Assert.IsNull(WindowsTarget.IntegrityOf(0xFFFFFFF0), "a process that cannot be opened has no level");
        Assert.IsNull(WindowsTarget.Refusal(0x2000, 0x2000));
        Assert.IsNull(WindowsTarget.Refusal(0x1000, 0x2000));
        Assert.AreEqual(PasteTargetReasons.HigherRights, WindowsTarget.Refusal(0x3000, 0x2000), "an elevated app (E6)");
        Assert.AreEqual(PasteTargetReasons.RightsUnreadable, WindowsTarget.Refusal(null, 0x2000), "a level it cannot read counts as above (S6)");
        Assert.AreEqual(PasteTargetReasons.RightsUnreadable, WindowsTarget.Refusal(0x2000, null));
    }

    private static void Eventually(Func<bool> condition)
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.IsLessThan(5000, clock.ElapsedMilliseconds, "the condition did not come true");
            Thread.Sleep(20);
        }
    }

    private static string ClassName(nint window)
    {
        var name = new StringBuilder(256);
        GetClassNameW(window, name, name.Capacity);
        return name.ToString();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rectangle
    {
        public int Left, Top, Right, Bottom;
    }

    private const int SwMinimize = 6;

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindowAsync(nint window, int command);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint window);

    private const uint ThreadSuspendResume = 0x0002;

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, out Rectangle rectangle);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern nint OpenThread(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint threadId);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern uint SuspendThread(nint thread);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern uint ResumeThread(nint thread);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);

    [DllImport("user32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern nint FindWindowW(string? className, string? title);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint GetShellWindow();

    [DllImport("imm32.dll", ExactSpelling = true)]
    private static extern nint ImmGetDefaultIMEWnd(nint window);

    [DllImport("user32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(nint window, StringBuilder name, int capacity);
}
