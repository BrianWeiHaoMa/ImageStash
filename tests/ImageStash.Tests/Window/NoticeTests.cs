using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// G16's notices: above the tray, six seconds on its own clock, longer while hovered or while its field has focus, each
/// hold ending on its own and time held by both counted once; in the stash window, through the window's wiring over a
/// test stash, as long as its words need, up to twenty seconds, on time the window shows as its rule tells it, a hover
/// begun before a hide ending with it; Undo while it shows and once, and what a notice leaving without its Undo was
/// given run once, never after Undo; a notice a later one replaces taken down running neither; G35's failures named.
/// </summary>
[TestClass]
public sealed class NoticeTests
{
    private readonly SetClock clock = new();

    [TestMethod]
    public void ANoticeLeavesSixSecondsAfterItIsRaised()
    {
        var notices = new Notices(clock);
        notices.Raise("3 added");

        clock.Now += TimeSpan.FromMilliseconds(5999);
        notices.Sweep();
        Assert.HasCount(1, notices.Shown);

        clock.Now += TimeSpan.FromMilliseconds(1);
        notices.Sweep();
        Assert.IsEmpty(notices.Shown);
    }

    [TestMethod]
    public void WhileHoveredItsClockStopsAndItLeavesNoLaterThanSixSecondsAfterThePointerLeaves()
    {
        var notices = new Notices(clock);
        var notice = notices.Raise("3 added");
        clock.Now += TimeSpan.FromSeconds(2);
        notices.Hover(notice, over: true);

        clock.Now += TimeSpan.FromSeconds(18);
        notices.Sweep();
        Assert.HasCount(1, notices.Shown, "hovered past its six seconds");

        notices.Hover(notice, over: false);
        clock.Now += TimeSpan.FromMilliseconds(3999);
        notices.Sweep();
        Assert.HasCount(1, notices.Shown);
        clock.Now += TimeSpan.FromMilliseconds(1);
        notices.Sweep();
        Assert.IsEmpty(notices.Shown);
    }

    [TestMethod]
    public void WhileItsFieldHasFocusItsClockStopsAndItLeavesNoLaterThanSixSecondsAfterFocusLeaves()
    {
        var notices = new Notices(clock);
        var notice = notices.Raise("Saved");
        clock.Now += TimeSpan.FromSeconds(2);
        notices.FieldFocus(notice, focused: true);

        clock.Now += TimeSpan.FromSeconds(18);
        notices.Sweep();
        Assert.HasCount(1, notices.Shown, "its field focused past its six seconds (G16)");

        notices.FieldFocus(notice, focused: false);
        clock.Now += TimeSpan.FromMilliseconds(3999);
        notices.Sweep();
        Assert.HasCount(1, notices.Shown);
        clock.Now += TimeSpan.FromMilliseconds(1);
        notices.Sweep();
        Assert.IsEmpty(notices.Shown);
    }

    [TestMethod]
    public void TheHoversEndLeavesTheFieldsFocusHoldingItAndTimeHeldByBothCountsOnce()
    {
        var notices = new Notices(clock);
        var notice = notices.Raise("Saved");
        clock.Now += TimeSpan.FromSeconds(1);
        notices.Hover(notice, over: true);
        clock.Now += TimeSpan.FromSeconds(1);
        notices.FieldFocus(notice, focused: true);
        clock.Now += TimeSpan.FromSeconds(10);
        notices.Hover(notice, over: false);

        clock.Now += TimeSpan.FromSeconds(20);
        notices.Sweep();
        Assert.HasCount(1, notices.Shown, "the pointer gone, its field's focus still holds it");

        // Held from the hover's start, 1 s in, to the focus's end, 32 s in: it leaves 31 s after its own six.
        notices.FieldFocus(notice, focused: false);
        clock.Now += TimeSpan.FromMilliseconds(4999);
        notices.Sweep();
        Assert.HasCount(1, notices.Shown);
        clock.Now += TimeSpan.FromMilliseconds(1);
        notices.Sweep();
        Assert.IsEmpty(notices.Shown);
    }

    [TestMethod]
    public void TheFieldsFocusEndingLeavesTheHoverHoldingIt()
    {
        var notices = new Notices(clock);
        var notice = notices.Raise("Saved");
        notices.FieldFocus(notice, focused: true);
        clock.Now += TimeSpan.FromSeconds(1);
        notices.Hover(notice, over: true);
        clock.Now += TimeSpan.FromSeconds(1);
        notices.FieldFocus(notice, focused: false);

        clock.Now += TimeSpan.FromSeconds(20);
        notices.Sweep();
        Assert.HasCount(1, notices.Shown, "still hovered");

        notices.Hover(notice, over: false);
        clock.Now += TimeSpan.FromMilliseconds(5999);
        notices.Sweep();
        Assert.HasCount(1, notices.Shown);
        clock.Now += TimeSpan.FromMilliseconds(1);
        notices.Sweep();
        Assert.IsEmpty(notices.Shown);
    }

    [TestMethod]
    public async Task UndoActsWhileTheNoticeShowsAndOnlyOnce()
    {
        var notices = new Notices(clock);
        var undone = 0;
        var notice = notices.Raise("2 added", "Undo", () =>
        {
            undone++;
            return Task.FromResult(Outcome.Done);
        });

        await notices.Undo(notice);
        await notices.Undo(notice);

        Assert.AreEqual(1, undone);
        Assert.IsEmpty(notices.Shown);
    }

    [TestMethod]
    public async Task UndoDoesNothingOnceTheNoticeHasGone()
    {
        var notices = new Notices(clock);
        var undone = false;
        var notice = notices.Raise("2 added", "Undo", () =>
        {
            undone = true;
            return Task.FromResult(Outcome.Done);
        });
        clock.Now += Notices.Lasting;
        notices.Sweep();

        await notices.Undo(notice);

        Assert.IsFalse(undone);
    }

    [TestMethod]
    public async Task AnUndoThatFailsIsNamedInANoticeOfItsOwn()
    {
        var notices = new Notices(clock);
        var notice = notices.Raise("2 added", "Undo", () =>
            Task.FromResult<Outcome>(new Failure("Undo the import", "cat.gif", "it is held open by another program")));

        await notices.Undo(notice);

        Assert.AreEqual("Couldn't undo the import: cat.gif is held open by another program. Try again in a moment.", notices.Shown.Single().Text);
        Assert.IsNull(notices.Shown.Single().UndoLabel);
    }

    [TestMethod]
    public async Task ANoticeLeavingByItsTimeOrItsCloseRunsItsLeaveOnceAndOneUndoneNever()
    {
        var notices = new Notices(clock);
        var left = new List<string>();
        Func<Task> Leave(string name) => () =>
        {
            left.Add(name);
            return Task.CompletedTask;
        };
        var expiring = notices.Raise("expiring", "Undo", () => Task.FromResult(Outcome.Done), Leave("expiring"));
        var closed = notices.Raise("closed", "Undo", () => Task.FromResult(Outcome.Done), Leave("closed"));
        var undone = notices.Raise("undone", "Undo", () => Task.FromResult(Outcome.Done), Leave("undone"));

        notices.Close(closed);
        notices.Close(closed);
        await notices.Undo(undone);
        clock.Now += Notices.Lasting;
        notices.Sweep();
        notices.Sweep();
        notices.Close(expiring);
        notices.Close(undone);

        CollectionAssert.AreEqual(new[] { "closed", "expiring" }, left, "each once, by its × or its time; never after Undo");
        Assert.IsEmpty(notices.Shown);
    }

    [TestMethod]
    public async Task ADismissedNoticeLeavesAtOnceRunningNeitherItsUndoNorItsLeave()
    {
        var notices = new Notices(clock);
        var ran = new List<string>();
        var dismissed = notices.Raise("replaced", "Undo", () =>
        {
            ran.Add("undo");
            return Task.FromResult(Outcome.Done);
        }, () =>
        {
            ran.Add("left");
            return Task.CompletedTask;
        });
        var changes = 0;
        notices.Changed += () => changes++;

        notices.Dismiss(dismissed);
        notices.Dismiss(dismissed);
        await notices.Undo(dismissed);
        notices.Close(dismissed);
        clock.Now += Notices.Lasting;
        notices.Sweep();

        Assert.IsEmpty(ran, "neither its Undo nor its leave, then or later");
        Assert.IsEmpty(notices.Shown);
        Assert.AreEqual(1, changes, "taken down once");
    }

    [TestMethod]
    public void AHoveredNoticeDoesNotLeaveAndSoRunsNothingUntilItsTimeIsUp()
    {
        var notices = new Notices(clock);
        var left = 0;
        var notice = notices.Raise("3 deleted", "Undo", () => Task.FromResult(Outcome.Done), () =>
        {
            left++;
            return Task.CompletedTask;
        });
        notices.Hover(notice, over: true);

        clock.Now += TimeSpan.FromSeconds(20);
        notices.Sweep();
        Assert.AreEqual(0, left, "hovered past its six seconds, it still shows");

        notices.Hover(notice, over: false);
        clock.Now += Notices.Lasting;
        notices.Sweep();
        Assert.AreEqual(1, left);
    }

    [TestMethod]
    public void SweepAndCloseStartTheLeaveWithoutWaitingForIt()
    {
        var notices = new Notices(clock);
        var never = new TaskCompletionSource();
        var started = 0;
        Func<Task> leave = () =>
        {
            started++;
            return never.Task;
        };
        notices.Raise("swept", "Undo", () => Task.FromResult(Outcome.Done), leave);
        var closing = notices.Raise("closed", "Undo", () => Task.FromResult(Outcome.Done), leave);

        notices.Close(closing);
        clock.Now += Notices.Lasting;
        notices.Sweep();

        Assert.AreEqual(2, started, "both started, and neither call waited for what they started");
        Assert.IsEmpty(notices.Shown);
    }

    [TestMethod]
    public void CloseLeavesAtOnceAndNoticesStackOldestFirst()
    {
        var notices = new Notices(clock);
        var first = notices.Raise("first");
        notices.Raise(new Failure("Save settings", "settings.json", "it is held open by another program"));

        CollectionAssert.AreEqual(new[] { "first", "Couldn't save settings: settings.json is held open by another program. Try again in a moment." }, notices.Shown.Select(n => n.Text).ToList());
        notices.Close(first);

        Assert.HasCount(1, notices.Shown);
    }

    [TestMethod]
    [DataRow(10, 5_999, 6_000, DisplayName = "ten long words: six seconds, whatever their letters")]
    [DataRow(14, 5_999, 6_000, DisplayName = "fourteen words: two and four, six seconds")]
    [DataRow(22, 8_285, 8_286, DisplayName = "twenty-two words: 8.29 seconds, not whole seconds")]
    [DataRow(28, 9_999, 10_000, DisplayName = "twenty-eight words: ten seconds")]
    [DataRow(200, 19_999, 20_000, DisplayName = "two hundred words: at most twenty seconds")]
    public async Task InTheWindowANoticeLastsAsLongAsItsWordsNeed(int words, int thereAtMs, int goneAtMs)
    {
        using var listed = await ListedStash.Open();
        var raisedAt = listed.Stash.Clock.Now;
        // Long words, so a count of letters would give a longer time; an Undo, whose label is no word of the notice.
        listed.Notices.Raise(Words(words, "abcdefghijkl"), "Undo", () => Task.FromResult(Outcome.Done));

        listed.Stash.Clock.Now = raisedAt + TimeSpan.FromMilliseconds(thereAtMs);
        listed.Notices.Sweep();
        Assert.HasCount(1, listed.Notices.Shown, $"{words} words, still there at {thereAtMs} ms");

        listed.Stash.Clock.Now = raisedAt + TimeSpan.FromMilliseconds(goneAtMs);
        listed.Notices.Sweep();
        Assert.IsEmpty(listed.Notices.Shown, $"{words} words, gone at {goneAtMs} ms");
    }

    [TestMethod]
    public async Task AboveTheTrayANoticeKeepsItsSixSecondsWhateverItsWordsAndWhateverTheWindowDoes()
    {
        using var listed = await ListedStash.Open();
        var raisedAt = listed.Stash.Clock.Now;
        var held = listed.Notifications.Raise(Words(40, "word"));
        listed.WindowShowing.Seen(visible: false, minimised: false);
        listed.Notifications.Raise(Words(40, "word"));

        listed.Stash.Clock.Now = raisedAt + TimeSpan.FromMilliseconds(5_999);
        listed.Notifications.Sweep();
        Assert.HasCount(2, listed.Notifications.Shown);
        listed.Notifications.Hover(held, over: true);
        listed.Stash.Clock.Now = raisedAt + Notices.Lasting;
        listed.Notifications.Sweep();

        Assert.AreEqual(held, listed.Notifications.Shown.Single(), "the hovered one held, the other gone at six seconds with the window hidden");
    }

    [TestMethod]
    public async Task ANoticeRaisedWhileTheWindowIsHiddenIsThereAtTheNextShowWithItsWholeTimeAhead()
    {
        using var listed = await ListedStash.Open();
        listed.WindowShowing.Seen(visible: false, minimised: false);
        listed.Notices.Raise("Added 4 to your stash.");

        listed.Stash.Clock.Now += TimeSpan.FromHours(1);
        listed.Notices.Sweep();
        Assert.HasCount(1, listed.Notices.Shown, "an hour hidden");
        listed.WindowShowing.Seen(visible: true, minimised: false);
        listed.Notices.Sweep();
        Assert.HasCount(1, listed.Notices.Shown, "there at the show, nothing charged for the hour");

        listed.Stash.Clock.Now += TimeSpan.FromMilliseconds(5_999);
        listed.Notices.Sweep();
        Assert.HasCount(1, listed.Notices.Shown);
        listed.Stash.Clock.Now += TimeSpan.FromMilliseconds(1);
        listed.Notices.Sweep();
        Assert.IsEmpty(listed.Notices.Shown, "six seconds of the window showing");
    }

    [TestMethod]
    [DataRow(false, false, DisplayName = "hidden")]
    [DataRow(true, true, DisplayName = "minimised")]
    public async Task ShownThreeSecondsThenNotShowingAMinuteItLeavesThreeSecondsAfterItShowsAgain(bool visible, bool minimised)
    {
        using var listed = await ListedStash.Open();
        listed.Notices.Raise("Deleted “cat”.");
        listed.Stash.Clock.Now += TimeSpan.FromSeconds(3);

        listed.WindowShowing.Seen(visible, minimised);
        listed.Stash.Clock.Now += TimeSpan.FromMinutes(1);
        listed.Notices.Sweep();
        Assert.HasCount(1, listed.Notices.Shown);
        listed.WindowShowing.Seen(visible: true, minimised: false);

        listed.Stash.Clock.Now += TimeSpan.FromMilliseconds(2_999);
        listed.Notices.Sweep();
        Assert.HasCount(1, listed.Notices.Shown);
        listed.Stash.Clock.Now += TimeSpan.FromMilliseconds(1);
        listed.Notices.Sweep();
        Assert.IsEmpty(listed.Notices.Shown);
    }

    [TestMethod]
    public async Task AHideAndShowWithinOneShowingAsUnpinningMakesNeitherLosesTheNoticeNorRestartsItsTime()
    {
        using var listed = await ListedStash.Open();
        listed.Notices.Raise("Deleted “cat”.");
        listed.Stash.Clock.Now += TimeSpan.FromSeconds(4);

        listed.WindowShowing.Seen(visible: false, minimised: false);
        listed.WindowShowing.Seen(visible: true, minimised: false);
        listed.Notices.Sweep();
        Assert.HasCount(1, listed.Notices.Shown);

        listed.Stash.Clock.Now += TimeSpan.FromSeconds(2);
        listed.Notices.Sweep();
        Assert.IsEmpty(listed.Notices.Shown, "six seconds in all, the instant's hide costing nothing and restarting nothing");
    }

    [TestMethod]
    public async Task AHoverBegunBeforeAHideEndsWithItAndTheTimeLeftIsWhatItWasWhenTheHoverBegan()
    {
        using var listed = await ListedStash.Open();
        var notice = listed.Notices.Raise("Deleted “cat”.");
        listed.Stash.Clock.Now += TimeSpan.FromSeconds(1);
        listed.Notices.Hover(notice, over: true);
        listed.Stash.Clock.Now += TimeSpan.FromSeconds(2);

        listed.WindowShowing.Seen(visible: false, minimised: false);
        listed.Stash.Clock.Now += TimeSpan.FromMinutes(1);
        listed.WindowShowing.Seen(visible: true, minimised: false);

        listed.Stash.Clock.Now += TimeSpan.FromMilliseconds(4_999);
        listed.Notices.Sweep();
        Assert.HasCount(1, listed.Notices.Shown, "five seconds were left when the pointer came over it");
        listed.Stash.Clock.Now += TimeSpan.FromMilliseconds(1);
        listed.Notices.Sweep();
        Assert.IsEmpty(listed.Notices.Shown, "the hover ended with the hide, so nothing holds it now");
    }

    [TestMethod]
    [DataRow(true, DisplayName = "the window hid")]
    [DataRow(false, DisplayName = "the pointer came over it")]
    public async Task ANoticeWhoseTimeRanOutBeforeTheSweepLeavesAtTheNextSweepThoughItsClockHasStopped(bool hid)
    {
        using var listed = await ListedStash.Open();
        var undone = false;
        var notice = listed.Notices.Raise("Deleted “cat”.", "Undo", () =>
        {
            undone = true;
            return Task.FromResult(Outcome.Done);
        });
        listed.Stash.Clock.Now += Notices.Lasting + TimeSpan.FromMilliseconds(100);

        if (hid)
        {
            listed.WindowShowing.Seen(visible: false, minimised: false);
        }
        else
        {
            listed.Notices.Hover(notice, over: true);
        }

        listed.Notices.Sweep();
        await listed.Notices.Undo(notice);

        Assert.IsEmpty(listed.Notices.Shown, "its time was up before its clock stopped (G16)");
        Assert.IsFalse(undone, "an Undo acts for the notice's time, not past it (G16, G18)");
    }

    [TestMethod]
    public async Task AnUndoPressedOnceTheNoticesTimeHasRunOutDoesNothingThoughNoSweepHasRunYet()
    {
        using var listed = await ListedStash.Open();
        var undone = false;
        var notice = listed.Notices.Raise("Deleted “cat”.", "Undo", () =>
        {
            undone = true;
            return Task.FromResult(Outcome.Done);
        });
        listed.Stash.Clock.Now += Notices.Lasting + TimeSpan.FromMilliseconds(50);
        listed.Notices.Hover(notice, over: true);

        await listed.Notices.Undo(notice);

        Assert.IsFalse(undone, "an Undo acts for the notice's time, not past it, whenever the sweep comes (G16, G18)");
        Assert.IsEmpty(listed.Notices.Shown, "its time was up, and it left as at its time");
    }

    [TestMethod]
    public void AHideStopsEveryNoticesClockAtOneMoment()
    {
        var reading = new ReadingClock();
        var notices = Notices.InWindow(reading);
        new WindowShowing(notices).Seen(visible: true, minimised: false);
        notices.Raise("first");
        notices.Raise("second");
        reading.Now += TimeSpan.FromSeconds(2);

        // Each reading of the clock a moment later than the last, as on a busy UI thread.
        reading.Step = TimeSpan.FromSeconds(1);
        new WindowShowing(notices).Seen(visible: false, minimised: false);
        reading.Step = TimeSpan.Zero;
        new WindowShowing(notices).Seen(visible: true, minimised: false);

        reading.Now += TimeSpan.FromMilliseconds(2_999);
        notices.Sweep();
        Assert.HasCount(2, notices.Shown, "both stopped at the hide's one moment, three seconds spent each");
        reading.Now += TimeSpan.FromMilliseconds(1);
        notices.Sweep();
        Assert.IsEmpty(notices.Shown);
    }

    /// <summary>A clock that moves on by <see cref="Step"/> at every reading.</summary>
    private sealed class ReadingClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);

        public TimeSpan Step { get; set; }

        public override DateTimeOffset GetUtcNow() => Now += Step;
    }

    /// <summary><paramref name="count"/> words, each <paramref name="word"/>.</summary>
    private static string Words(int count, string word) => string.Join(" ", Enumerable.Repeat(word, count));
}
