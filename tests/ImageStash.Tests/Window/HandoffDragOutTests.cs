using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// The drag-out over a real stash with real pictures: the files a drag carries (G9), in progress while it runs (G2); one
/// use per item only once the drop has landed, and none for a drag cancelled, taken by nothing or failed (G10); Recent
/// reordered while the window stays (G4); every failure named (G35).
/// </summary>
[TestClass]
public sealed class HandoffDragOutTests
{
    [TestMethod]
    public async Task ADropThatLandedRecordsOneUsePerItemOnlyOnceTheDragHasEndedAndRecentListsThemFirst()
    {
        using var handing = await Handing.Open("still.png", "photo.jpg", "animated.gif", "transparent.png");
        var three = handing.Items.Take(3).ToList();
        await handing.Listing.ShowScope(Scope.Recent);
        handing.Listing.Select(three.Select(item => item.Id), three[1].Id);
        var steps = new List<string>();
        var dragging = false;
        handing.Clipboard.DuringDrag = () => steps.Add($"drag, in progress {dragging}, uses {handing.Stash.Store!.Recent().Result.Value!.Count}");

        var dragged = await handing.Handoff.DragOut(three, handing.Drag, now => steps.Add($"in progress {dragging = now}"), () => steps.Add($"ended, in progress {dragging}"));

        Assert.IsTrue(dragged.Succeeded, dragged.Failure?.Sentence);
        CollectionAssert.AreEqual(three.Select(item => item.FilePath).ToList(), handing.Clipboard.Dragged.Single().ToList(), "the selection's files, in order (G9)");
        CollectionAssert.AreEqual(new[] { "in progress True", "drag, in progress True, uses 0", "in progress False", "ended, in progress False" }, steps,
            "in progress while the drag runs (G2); no use before the drop (G10); the hide judged again after it");
        Assert.IsEmpty(handing.Clipboard.Written, "a drag puts nothing on the clipboard (N9)");
        Assert.IsEmpty(handing.Notices.Shown);
        var at = handing.Stash.Clock.Now.ToUnixTimeMilliseconds();
        CollectionAssert.AreEquivalent(three.Select(item => (item.Id, at)).ToList(), handing.Uses(), "one use per item, one time");
    }

    [TestMethod]
    public async Task RecentReordersAfterALandedDropWithTheSelectionKept()
    {
        using var handing = await Handing.Open("photo.jpg", "animated.gif");
        var (first, second) = (handing.Items[0], handing.Items[1]);
        await handing.Copy([first]);
        handing.Stash.Clock.Now += TimeSpan.FromMinutes(1);
        await handing.Copy([second]);
        await handing.Listing.ShowScope(Scope.Recent);
        handing.Listing.Focus(first.Id);
        handing.Stash.Clock.Now += TimeSpan.FromMinutes(1);

        await handing.Handoff.DragOut(handing.Listing.SelectedItems, handing.Drag, _ => { }, () => { });

        CollectionAssert.AreEqual(new[] { first.Id, second.Id }, handing.Listing.Items.Select(item => item.Id).ToList(), "Recent while it shows (G4)");
        CollectionAssert.AreEqual(new[] { first.Id }, handing.Listing.Selection.ToList());
    }

    [TestMethod]
    public async Task ADragCancelledOrTakenByNothingRecordsNothingAndSaysNothing()
    {
        using var handing = await Handing.Open("photo.jpg");
        handing.Clipboard.DragEnds = Outcome<bool>.Of(false);
        var ended = 0;
        var dragging = false;

        var dragged = await handing.Handoff.DragOut(handing.Items, handing.Drag, now => dragging = now, () => ended++);

        Assert.IsTrue(dragged.Succeeded);
        Assert.HasCount(1, handing.Clipboard.Dragged);
        Assert.IsFalse(dragging, "the flyout's hide rule applies again (G2)");
        Assert.AreEqual(1, ended);
        Assert.IsEmpty(handing.Notices.Shown);
        Assert.IsEmpty(handing.Uses(), "a drag that did not land is not a use (G10)");
    }

    [TestMethod]
    public async Task ADragThatFailedIsNamedRecordsNothingAndTheNextDragWorks()
    {
        using var handing = await Handing.Open("photo.jpg");
        var failure = new Failure("Drag out", "photo.jpg", "the file could not be handed over");
        handing.Clipboard.DragEnds = failure;
        var dragging = false;
        var ended = 0;

        var dragged = await handing.Handoff.DragOut(handing.Items, handing.Drag, now => dragging = now, () => ended++);

        Assert.AreEqual(failure, dragged.Failure);
        Assert.AreEqual("Drag out failed: photo.jpg, the file could not be handed over", handing.Notices.Shown.Single().Text, "named (G35)");
        Assert.IsFalse(dragging);
        Assert.AreEqual(1, ended);
        Assert.IsEmpty(handing.Stash.Store!.Recent().Result.Value!);

        handing.Clipboard.DragEnds = Outcome<bool>.Of(true);
        var again = await handing.Handoff.DragOut(handing.Items, handing.Drag, now => dragging = now, () => ended++);

        Assert.IsTrue(again.Succeeded);
        Assert.HasCount(1, handing.Uses());
    }

    [TestMethod]
    public async Task AUseTheStoreRefusesAfterALandedDropIsNamedAndTheDragStaysDone()
    {
        using var handing = await Handing.Open("animated.gif");
        Outcome dragged;
        using (handing.Stash.HoldStore())
        {
            dragged = await handing.Handoff.DragOut(handing.Items, handing.Drag, _ => { }, () => { });
        }

        Assert.IsTrue(dragged.Succeeded, "the drop landed, so the drag-out is done (G10)");
        Assert.StartsWith("Record a use failed: ", handing.Notices.Shown.Single().Text);
        Assert.IsEmpty(handing.Uses());
    }

    [TestMethod]
    public async Task WhileAnotherProgramHoldsTheStoreADragOfAnItemUnchangedSinceItWasListedDropsItsFinishedPicture()
    {
        using var handing = await Handing.Open("transparent.png", "photo.jpg");
        var (item, other) = (handing.Items[0], handing.Items[1]);
        await handing.Listing.Refresh();
        var tile = handing.Listing.Items.Single(listed => listed.Id == item.Id);
        var finished = File.ReadAllBytes(tile.FilePath);
        Assert.IsTrue((await handing.Stash.Store!.AddTag([other], "mood")).Single().Succeeded, "another item changed since the listing");
        bool? droppedFinished = null;
        using var holder = handing.Stash.HoldStore();
        handing.Clipboard.DuringDrag = () =>
        {
            droppedFinished = handing.Clipboard.Dragged.Single() is [var path] && File.Exists(path) && File.ReadAllBytes(path).SequenceEqual(finished);
            holder.Dispose();
        };

        var dragged = await handing.Handoff.DragOut([tile], handing.Drag, _ => { }, () => { });

        Assert.IsTrue(dragged.Succeeded, dragged.Failure?.Sentence);
        Assert.IsTrue(droppedFinished, "no change of the item since its listing, so its finished picture is dropped though the store is held (G9); the held-store refusal is the handoff's and Ctrl+C's (G8)");
        Assert.IsEmpty(handing.Notices.Shown);
        Assert.HasCount(1, handing.Uses(), "the store let go before the drop landed, so its use is recorded (G10)");
    }

    [TestMethod]
    public async Task NothingSelectedDragsNothing()
    {
        using var handing = await Handing.Open("photo.jpg");
        var dragging = false;

        var dragged = await handing.Handoff.DragOut([], handing.Drag, now => dragging = true, () => dragging = true);

        Assert.IsTrue(dragged.Succeeded);
        Assert.IsEmpty(handing.Clipboard.Dragged);
        Assert.IsFalse(dragging);
    }
}
