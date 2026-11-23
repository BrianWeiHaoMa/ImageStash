using Avalonia.Controls;
using ImageStash.Seams;
using ImageStash.Store;
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
    public void TheHandoffIsNamedPasteIntoTheTargetsAppOrCopyCountingSeveralItems()
    {
        Assert.AreEqual("Copy", PasteTargetHolder.HandoffName(null, 1), "with no target the handoff is the copy (G8)");
        Assert.AreEqual("Paste into Relay", PasteTargetHolder.HandoffName(RecordingTarget.Relay, 1), "as the footer's Enter and the preview's button name it (G19)");
        Assert.AreEqual("Paste 3 items into Relay", PasteTargetHolder.HandoffName(RecordingTarget.Relay, 3), "several are counted, the target's app still named (G8)");
        Assert.AreEqual("Copy 2 items", PasteTargetHolder.HandoffName(null, 2));
    }

    [TestMethod]
    public void TheFooterNamesTheTargetsAppWithCopyBesideItOrReadsCopy()
    {
        CollectionAssert.AreEqual(new[] { "Enter · Copy" }, PasteTargetHolder.Hints(null, 1).ToList());
        CollectionAssert.AreEqual(new[] { "Enter · Paste into Relay", "Ctrl+C · Copy" }, PasteTargetHolder.Hints(RecordingTarget.Relay, 1).ToList());
        CollectionAssert.AreEqual(new[] { "Enter · Paste into Microsoft Teams (work or school)", "Ctrl+C · Copy" },
            PasteTargetHolder.Hints(RecordingTarget.App("Microsoft Teams (work or school)"), 1).ToList(), "the name as the seam gave it, never shortened");
    }

    [TestMethod]
    public async Task TheFootersEnterHintCountsSeveralSelectedItemsAsTheSelectionChangesWhileTheTargetStays()
    {
        using var listed = await ListedStash.Open();
        var items = await Listed(listed, "one", "two", "three");
        listed.PasteTarget.FromPress(new ForegroundReading(false, RecordingTarget.Relay));

        ControlThread.Run(() =>
        {
            var footer = Footer(listed);
            listed.Listing.Focus(items[0].Id);
            var one = Hints(footer);
            listed.Listing.SelectAll();
            var three = Hints(footer);
            listed.Listing.Focus(items[1].Id);
            var backToOne = Hints(footer);
            listed.PasteTarget.None();
            listed.Listing.SelectAll();
            var copying = Hints(footer);

            CollectionAssert.AreEqual(new[] { "Enter · Paste into Relay", "Ctrl+C · Copy" }, one);
            CollectionAssert.AreEqual(new[] { "Enter · Paste 3 items into Relay", "Ctrl+C · Copy" }, three, "the footer counts the selection as it changes, the target unchanged (G8)");
            CollectionAssert.AreEqual(new[] { "Enter · Paste into Relay", "Ctrl+C · Copy" }, backToOne, "one item is not counted");
            CollectionAssert.AreEqual(new[] { "Enter · Copy 3 items" }, copying, "with no target, Enter copies them all");
        });
    }

    [TestMethod]
    public async Task ACopysConfirmationStandsInForTheHintsForTwoSecondsThenTheHintsReturnAsTheyAreBy()
    {
        using var listed = await ListedStash.Open();
        var reaction = await listed.Stash.ImportFixture("the audacity", "still.png");
        await Listed(listed, "two", "three");
        listed.PasteTarget.FromPress(new ForegroundReading(false, RecordingTarget.Relay));
        listed.Listing.Focus(reaction.Id);

        var copied = await listed.Handoff.Copy(null!, [reaction]);
        var confirmed = listed.FooterHints.Texts.ToList();
        var changes = 0;
        listed.FooterHints.Changed += () => changes++;
        listed.Stash.Clock.Now += TimeSpan.FromSeconds(1.9);
        listed.FooterHints.Sweep();
        var stillConfirmed = listed.FooterHints.Texts.ToList();
        var changesBefore = changes;
        listed.Listing.SelectAll();
        listed.PasteTarget.None();
        listed.Stash.Clock.Now += TimeSpan.FromSeconds(0.1);
        listed.FooterHints.Sweep();

        Assert.IsTrue(copied.Succeeded, copied.Failure?.Sentence);
        CollectionAssert.AreEqual(new[] { "✓ Copied “the audacity”" }, confirmed, "what went on the clipboard, in the hints' place (G10)");
        CollectionAssert.AreEqual(confirmed, stillConfirmed, "for two seconds");
        Assert.AreEqual(0, changesBefore);
        CollectionAssert.AreEqual(new[] { "Enter · Copy 3 items" }, listed.FooterHints.Texts.ToList(), "then the hints, for the target and the selection as they are now");
        Assert.AreEqual(1, changes, "the window is told to draw them");
        Assert.IsNull(listed.FooterHints.Confirmation);
    }

    [TestMethod]
    public async Task ASecondCopyWithinTheTwoSecondsNamesTheLatestAndLastsTwoSecondsFromIt()
    {
        using var listed = await ListedStash.Open();
        var reaction = await listed.Stash.ImportFixture("the audacity", "still.png");
        var others = await Listed(listed, "two", "three");
        listed.Listing.Focus(reaction.Id);

        await listed.Handoff.Copy(null!, [reaction]);
        listed.Stash.Clock.Now += TimeSpan.FromSeconds(1.5);
        await listed.Handoff.Copy(null!, others);
        var latest = listed.FooterHints.Texts.ToList();
        listed.Stash.Clock.Now += TimeSpan.FromSeconds(1.9);
        listed.FooterHints.Sweep();
        var pastTheFirstsTwoSeconds = listed.FooterHints.Texts.ToList();
        listed.Stash.Clock.Now += TimeSpan.FromSeconds(0.1);
        listed.FooterHints.Sweep();

        CollectionAssert.AreEqual(new[] { "✓ Copied 2 items" }, latest, "several by count (G10)");
        CollectionAssert.AreEqual(latest, pastTheFirstsTwoSeconds, "two seconds from the latest copy");
        CollectionAssert.AreEqual(new[] { "Enter · Copy" }, listed.FooterHints.Texts.ToList());
    }

    /// <summary>Items named <paramref name="names"/> added to the stash, the listing showing All with them.</summary>
    private static async Task<List<StashItem>> Listed(ListedStash listed, params string[] names)
    {
        var items = new List<StashItem>();
        foreach (var name in names)
        {
            items.Add(await listed.Stash.Import(name));
        }

        await listed.Listing.ShowScope(Scope.All);
        return items;
    }

    /// <summary>The footer as the window wires it: drawn again when the listing, the target or the copy's confirmation changes.</summary>
    private static WindowStatus Footer(ListedStash listed)
    {
        var status = new WindowStatus(listed.Listing, listed.Notices, listed.Settings, listed.FooterHints, new ScrollViewer(), () => false, () => { });
        listed.Listing.Changed += status.ShowListing;
        listed.PasteTarget.Changed += status.ShowHints;
        listed.FooterHints.Changed += status.ShowHints;
        status.ShowHints();
        status.ShowListing();
        return status;
    }

    /// <summary>The footer's hints as drawn, or none while they are out of sight.</summary>
    private static List<string> Hints(WindowStatus footer)
    {
        var left = (Panel)((Panel)footer.Footer.Child!).Children[0];
        var hints = left.Children.OfType<DockPanel>().Single();
        return hints.IsVisible ? hints.Children.Cast<TextBlock>().Select(text => text.Text!).ToList() : [];
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
