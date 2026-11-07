using ImageStash.Seams;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// G8's paste target as the window holds it: set at the open shortcut's press to the window in front unless that is
/// ImageStash itself, and at any other activation to the window that had focus just before, none for the desktop or the
/// taskbar; and the footer's words for it.
/// </summary>
[TestClass]
public sealed class PasteTargetTests
{
    [TestMethod]
    public void APressWithAnotherAppInFrontMakesThatAppTheTarget()
    {
        var holder = new PasteTargetHolder();
        var changes = 0;
        holder.Changed += () => changes++;

        holder.FromPress(new ForegroundReading(false, RecordingTarget.Relay));

        Assert.AreEqual(RecordingTarget.Relay, holder.Current);
        Assert.AreEqual(1, changes);
    }

    [TestMethod]
    public void APressWithImageStashInFrontLeavesTheTargetAsItWas()
    {
        var holder = new PasteTargetHolder();
        holder.FromPress(new ForegroundReading(false, RecordingTarget.Relay));
        var changes = 0;
        holder.Changed += () => changes++;

        holder.FromPress(new ForegroundReading(true, null));

        Assert.AreEqual(RecordingTarget.Relay, holder.Current, "ImageStash itself is never the target, and leaves the one it has (G8)");
        Assert.AreEqual(0, changes);
    }

    [TestMethod]
    public void APressWithTheDesktopOrTheTaskbarInFrontLeavesNoTarget()
    {
        var holder = new PasteTargetHolder();
        holder.FromPress(new ForegroundReading(false, RecordingTarget.Relay));

        holder.FromPress(new ForegroundReading(false, null));

        Assert.IsNull(holder.Current);
    }

    [TestMethod]
    public void AnActivationMakesTheWindowBeforeImageStashTheTargetOrNone()
    {
        var holder = new PasteTargetHolder();
        var seam = new RecordingTarget { Before = RecordingTarget.Relay };

        holder.FromActivation(seam);
        var afterApp = holder.Current;
        seam.Before = null;
        holder.FromActivation(seam);

        Assert.AreEqual(RecordingTarget.Relay, afterApp, "a pinned window clicked from another app gets that app (G8)");
        Assert.IsNull(holder.Current, "from the taskbar, as a tray click comes, none (G8, In use)");
    }

    [TestMethod]
    public void AChangeTheSeamHearsOfLateAfterAnActivationIsReadAgainWhileImageStashIsInFront()
    {
        // G8: the window that had focus just before the activation, though the seam heard of it only after the read.
        var holder = new PasteTargetHolder();
        var seam = new RecordingTarget { Before = RecordingTarget.App("Earlier"), InFront = new ForegroundReading(true, null) };
        holder.FromActivation(seam);
        var changes = 0;
        holder.Changed += () => changes++;

        seam.Before = RecordingTarget.Relay;
        seam.ReportLate();

        Assert.AreEqual(RecordingTarget.Relay, holder.Current);
        Assert.AreEqual(1, changes, "the footer is told");
    }

    [TestMethod]
    public void ALateReportOnceAnotherAppIsInFrontOrAfterAPressChangesNothing()
    {
        var holder = new PasteTargetHolder();
        var seam = new RecordingTarget { Before = RecordingTarget.Relay, InFront = new ForegroundReading(false, RecordingTarget.App("Elsewhere")) };
        holder.FromActivation(seam);
        seam.Before = RecordingTarget.App("Later");

        seam.ReportLate();

        Assert.AreEqual(RecordingTarget.Relay, holder.Current, "another app in front: the user's next change, read at the next press or activation");

        seam.InFront = new ForegroundReading(true, null);
        holder.FromPress(new ForegroundReading(false, RecordingTarget.App("Pressed over")));
        seam.ReportLate();

        Assert.AreEqual(RecordingTarget.App("Pressed over"), holder.Current, "set at the press, unchanged by any later report (G8)");

        holder.FromActivation(seam);
        var activated = holder.Current;
        holder.FromPress(new ForegroundReading(true, null));
        seam.Before = RecordingTarget.App("Later still");
        seam.ReportLate();

        Assert.AreEqual(activated, holder.Current, "a press with ImageStash in front leaves the target as it was, and no later report reads it again (G8)");
    }

    [TestMethod]
    public void AfterTheWindowsOwnShowingALateReportChangesNothing()
    {
        // G8, G3: once the window shows itself (a pin, its showing again after a capture), what is in front since is
        // ImageStash's own doing, and the target stands.
        var holder = new PasteTargetHolder();
        var seam = new RecordingTarget { Before = RecordingTarget.Relay, InFront = new ForegroundReading(true, null) };
        holder.FromActivation(seam);

        holder.Keep();
        seam.Before = null;
        seam.ReportLate();

        Assert.AreEqual(RecordingTarget.Relay, holder.Current);
    }

    [TestMethod]
    public void AReadAgainIsMadeWhereTheActivationWasReadNotOnTheSeamsThread()
    {
        var holder = new PasteTargetHolder();
        var seam = new RecordingTarget { Before = RecordingTarget.App("Earlier"), InFront = new ForegroundReading(true, null) };
        var window = new QueuedContext();
        var before = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(window);
        try
        {
            holder.FromActivation(seam);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(before);
        }

        seam.Before = RecordingTarget.Relay;
        Task.Run(seam.ReportLate).Wait();

        Assert.AreEqual(RecordingTarget.App("Earlier"), holder.Current, "nothing is set on the seam's thread");
        window.RunQueued();
        Assert.AreEqual(RecordingTarget.Relay, holder.Current);
    }

    [TestMethod]
    public void TheHandoffIsNamedPasteIntoTheTargetsAppOrCopy()
    {
        Assert.AreEqual("Copy", PasteTargetHolder.HandoffName(null), "with no target the handoff is the copy (G8)");
        Assert.AreEqual("Paste into Relay", PasteTargetHolder.HandoffName(RecordingTarget.Relay), "as the footer's Enter and the preview's button name it (G19)");
    }

    [TestMethod]
    public void TheFooterNamesTheTargetsAppWithCopyBesideItOrReadsCopy()
    {
        CollectionAssert.AreEqual(new[] { "Enter · Copy" }, PasteTargetHolder.Hints(null).ToList());
        CollectionAssert.AreEqual(new[] { "Enter · Paste into Relay", "Ctrl+C · Copy" }, PasteTargetHolder.Hints(RecordingTarget.Relay).ToList());
        CollectionAssert.AreEqual(new[] { "Enter · Paste into Microsoft Teams (work or school)", "Ctrl+C · Copy" },
            PasteTargetHolder.Hints(RecordingTarget.App("Microsoft Teams (work or school)")).ToList(), "the name as the seam gave it, never shortened");
    }

    /// <summary>A UI thread's context standing still: what is posted to it runs only when the check says.</summary>
    private sealed class QueuedContext : SynchronizationContext
    {
        private readonly List<(SendOrPostCallback Callback, object? State)> queued = [];

        public override void Post(SendOrPostCallback d, object? state)
        {
            lock (queued)
            {
                queued.Add((d, state));
            }
        }

        public void RunQueued()
        {
            lock (queued)
            {
                foreach (var (callback, state) in queued)
                {
                    callback(state);
                }

                queued.Clear();
            }
        }
    }
}
