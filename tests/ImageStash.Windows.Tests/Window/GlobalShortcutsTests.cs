using System.Diagnostics;
using Avalonia.Threading;
using ImageStash.Seams;
using ImageStash.Store;
using ImageStash.Window;
using ImageStash.Windows.Seams;
using ImageStash.Windows.Tests.Support;

namespace ImageStash.Windows.Tests.Window;

/// <summary>
/// G33 and F5 for the four shortcuts over the real hot-key seam, registered as ImageStash binds them: a combination
/// another program holds at start is named and left unregistered for the run, through Pause and its toggle, while the
/// other three fire; with nothing holding any, Pause releases all four, so other programs can take them, and its toggle
/// brings all four back. A press of a combination not registered is always taken by another program of the check's
/// own, so it reaches no window.
/// </summary>
[TestClass]
public sealed class GlobalShortcutsTests
{
    private static readonly Shortcut Open = Shortcut.Parse("Ctrl+Alt+Space").Value!;
    private static readonly Shortcut TakeScreenshot = Shortcut.Parse("Ctrl+PrtScn").Value!;
    private static readonly Shortcut RecordClip = Shortcut.Parse("Ctrl+Shift+PrtScn").Value!;
    private static readonly Shortcut StashClipboardImage = Shortcut.Parse("Ctrl+Alt+V").Value!;

    [TestMethod]
    [DataRow("Ctrl+Alt+Space", "Register the open shortcut failed: Ctrl+Alt+Space, another program holds it", DisplayName = "the open shortcut held")]
    [DataRow("Ctrl+PrtScn", "Register the screenshot shortcut failed: Ctrl+PrtScn, another program holds it", DisplayName = "the screenshot shortcut held")]
    [DataRow("Ctrl+Shift+PrtScn", "Register the record shortcut failed: Ctrl+Shift+PrtScn, another program holds it", DisplayName = "the record shortcut held")]
    [DataRow("Ctrl+Alt+V", "Register the Stash clipboard image shortcut failed: Ctrl+Alt+V, another program holds it", DisplayName = "Stash clipboard image's held")]
    public void ACombinationAnotherProgramHoldsAtStartIsNamedAndStaysUnregisteredThroughPauseWhileTheOthersFire(string heldText, string sentence)
    {
        var heldShortcut = Shortcut.Parse(heldText).Value!;
        var held = OtherProgramHoldingAHotKey.Start(heldShortcut);
        Assert.IsNotNull(held, $"the check's holder took {heldText}");
        using var seam = new WindowsShortcuts();
        var policy = new Policy(seam);
        try
        {
            TrayDesktop.Run(policy.Shortcuts.Start);

            CollectionAssert.AreEqual(new[] { sentence }, policy.Named(), "which shortcut is unavailable and why, for the tooltip (G33)");
            policy.PressEachAndWait(heldShortcut);

            TrayDesktop.Run(policy.Shortcuts.TogglePause);
            TrayDesktop.Run(policy.Shortcuts.TogglePause);

            policy.PressEachAndWait(heldShortcut);
            Assert.HasCount(1, policy.Named(), "the toggle back registers the other three and does not try the held one again");
        }
        finally
        {
            held.Dispose();
        }

        // Let go by the other program, it is still not ImageStash's: Pause and its toggle do not revive it (G33: until
        // it is changed in Settings), so another program can take it again and a press goes there.
        TrayDesktop.Run(policy.Shortcuts.TogglePause);
        TrayDesktop.Run(policy.Shortcuts.TogglePause);
        var acted = policy.Acted();
        using (var again = OtherProgramHoldingAHotKey.Start(heldShortcut))
        {
            Assert.IsNotNull(again, $"nothing of ImageStash's holds {heldText}");
            TestHotKeys.Press(heldShortcut);
            Thread.Sleep(300);
        }

        Assert.AreEqual(acted, policy.Acted(), "the held one never acted");
        Assert.HasCount(1, policy.Named());
        policy.PressEachAndWait(heldShortcut);
    }

    [TestMethod]
    public void WithNothingHoldingAnyPauseReleasesAllFourAndItsToggleBringsAllFourBack()
    {
        using var seam = new WindowsShortcuts();
        var policy = new Policy(seam);
        TrayDesktop.Run(policy.Shortcuts.Start);
        Assert.IsEmpty(policy.Named(), "all four registered; another program, or a running ImageStash, holds none");
        policy.PressAndWait(Open, () => policy.Summoned == 1);
        policy.PressAndWait(TakeScreenshot, () => policy.Screenshots == 1);
        policy.PressAndWait(RecordClip, () => policy.Recorded == 1);
        policy.PressAndWait(StashClipboardImage, () => policy.Stashed == 1);

        TrayDesktop.Run(policy.Shortcuts.TogglePause);

        using (var open = OtherProgramHoldingAHotKey.Start(Open))
        using (var screenshot = OtherProgramHoldingAHotKey.Start(TakeScreenshot))
        using (var record = OtherProgramHoldingAHotKey.Start(RecordClip))
        using (var stash = OtherProgramHoldingAHotKey.Start(StashClipboardImage))
        {
            Assert.IsNotNull(open, "Pause released the open shortcut");
            Assert.IsNotNull(screenshot, "Pause released the screenshot shortcut");
            Assert.IsNotNull(record, "Pause released the record shortcut");
            Assert.IsNotNull(stash, "Pause released Stash clipboard image's shortcut");
            TestHotKeys.Press(Open);
            TestHotKeys.Press(TakeScreenshot);
            TestHotKeys.Press(RecordClip);
            TestHotKeys.Press(StashClipboardImage);
            Thread.Sleep(300);
        }

        Assert.AreEqual((1, 1, 1, 1), policy.Acted(), "none fires while paused");

        TrayDesktop.Run(policy.Shortcuts.TogglePause);

        Assert.IsEmpty(policy.Named(), "all four registered again");
        policy.PressAndWait(Open, () => policy.Summoned == 2);
        policy.PressAndWait(TakeScreenshot, () => policy.Screenshots == 2);
        policy.PressAndWait(RecordClip, () => policy.Recorded == 2);
        policy.PressAndWait(StashClipboardImage, () => policy.Stashed == 2);
        Assert.IsTrue(policy.OnUiThread, "the screenshot's, the record shortcut's and Stash clipboard image's acts run on the UI thread");
    }

    /// <summary>
    /// The policy as ImageStash binds F5's four shortcuts, from the default settings, over the real seam: each act
    /// counted where it runs, the open shortcut's on the seam's thread and the other three on the UI thread.
    /// </summary>
    private sealed class Policy
    {
        private readonly List<string> named = [];
        private int summoned;
        private int screenshots;
        private int recorded;
        private int stashed;
        private volatile bool onUiThread = true;

        public Policy(IShortcuts seam) =>
            Shortcuts = new GlobalShortcuts(
                SettingsInMemory.Defaults(),
                GlobalShortcuts.Bindings(() => Interlocked.Increment(ref summoned), () =>
                {
                    onUiThread &= Dispatcher.UIThread.CheckAccess();
                    Interlocked.Increment(ref screenshots);
                }, () =>
                {
                    onUiThread &= Dispatcher.UIThread.CheckAccess();
                    Interlocked.Increment(ref recorded);
                }, () =>
                {
                    onUiThread &= Dispatcher.UIThread.CheckAccess();
                    Interlocked.Increment(ref stashed);
                }),
                seam,
                failure =>
                {
                    lock (named)
                    {
                        named.Add(failure.Sentence);
                    }
                },
                action => Dispatcher.UIThread.Post(action));

        public GlobalShortcuts Shortcuts { get; }

        public int Summoned => Volatile.Read(ref summoned);

        public int Screenshots => Volatile.Read(ref screenshots);

        public int Recorded => Volatile.Read(ref recorded);

        public int Stashed => Volatile.Read(ref stashed);

        public bool OnUiThread => onUiThread;

        /// <summary>How many times each act has run, the open shortcut's first.</summary>
        public (int, int, int, int) Acted() => (Summoned, Screenshots, Recorded, Stashed);

        public List<string> Named()
        {
            lock (named)
            {
                return [.. named];
            }
        }

        /// <summary>A real press of each shortcut but <paramref name="held"/>, each until its own act has run once more.</summary>
        public void PressEachAndWait(Shortcut held)
        {
            foreach (var (shortcut, count) in new (Shortcut, Func<int>)[] { (Open, () => Summoned), (TakeScreenshot, () => Screenshots), (RecordClip, () => Recorded), (StashClipboardImage, () => Stashed) })
            {
                if (shortcut != held)
                {
                    var before = count();
                    PressAndWait(shortcut, () => count() == before + 1);
                }
            }
        }

        /// <summary>A real press of <paramref name="shortcut"/>, then until its act has run, for a few seconds at most.</summary>
        public void PressAndWait(Shortcut shortcut, Func<bool> acted)
        {
            TestHotKeys.Press(shortcut);
            var clock = Stopwatch.StartNew();
            while (!acted())
            {
                Assert.IsLessThan(5000, clock.ElapsedMilliseconds, $"{shortcut.Text} did not act");
                Thread.Sleep(20);
            }
        }
    }
}
