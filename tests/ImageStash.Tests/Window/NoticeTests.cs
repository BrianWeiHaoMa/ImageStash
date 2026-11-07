using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// G16's in-window notice: six seconds on its own clock, longer while hovered or while its field has focus, each hold
/// ending on its own and time held by both counted once; Undo while it shows and once, and what a
/// notice leaving without its Undo was given run once, never after Undo; a notice a later one replaces taken down
/// running neither; G35's failures named.
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
        notices.EndHovers();

        clock.Now += TimeSpan.FromSeconds(20);
        notices.Sweep();
        Assert.HasCount(1, notices.Shown, "the pointer gone and the window's hovers ended, its field's focus still holds it");

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
    public void HidingTheWindowEndsTheHoverAndTheClockRunsOn()
    {
        var notices = new Notices(clock);
        var notice = notices.Raise("3 added");
        notices.Hover(notice, over: true);
        clock.Now += TimeSpan.FromSeconds(1);

        notices.EndHovers();
        clock.Now += TimeSpan.FromSeconds(6);
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
            Task.FromResult<Outcome>(new Failure("Undo the import", "cat.gif", "another program is holding it open")));

        await notices.Undo(notice);

        Assert.AreEqual("Undo the import failed: cat.gif, another program is holding it open", notices.Shown.Single().Text);
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
        notices.Raise(new Failure("Save settings", "settings.json", "another program is holding it open"));

        CollectionAssert.AreEqual(new[] { "first", "Save settings failed: settings.json, another program is holding it open" }, notices.Shown.Select(n => n.Text).ToList());
        notices.Close(first);

        Assert.HasCount(1, notices.Shown);
    }
}
