using Avalonia.Controls;
using Avalonia.LogicalTree;
using ImageStash.Files;
using ImageStash.Seams;
using ImageStash.Store;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// G16's notice that names a problem, in the stash window, on the stash's clock through the window's wiring: a skipped
/// file, a refusal or a failure stays past its time until its × or the next notice of the same action, while a notice
/// naming none leaves at its time; the notices one act raises stand together, and a later act of the same action, by
/// when the person asked, takes the place of an earlier act's problem notices, one still offering its Undo leaving
/// when that Undo ends; an Undo acts for the notice's time only (G18), what the notice was given for leaving running
/// once at that end, the card dropping its Undo button with it; the notifications above the tray keep six seconds.
/// </summary>
[TestClass]
public sealed class ProblemNoticeTests
{
    private static readonly TimeSpan AMinute = TimeSpan.FromMinutes(1);

    [TestMethod]
    public async Task APastesFailureIsStillThereAfterAMinuteOfShownTimeAndItsCloseTakesItDownAtOnce()
    {
        using var listed = await ListedStash.Open();

        await listed.Paste.Paste();
        var refused = listed.Notices.Shown.Single();
        Pass(listed, AMinute);

        Assert.AreEqual(Failure.Refusal(ClipboardPaste.Action, "the clipboard", "it holds no picture or file").Sentence, refused.Text);
        Assert.IsTrue(refused.NamesAProblem);
        CollectionAssert.AreEqual(new[] { refused }, listed.Notices.Shown.ToList(), "a refusal stays past its time");
        listed.Notices.Close(refused);
        Assert.IsEmpty(listed.Notices.Shown, "its × takes it down");
    }

    [TestMethod]
    public async Task ANoticeNamingNoProblemLeavesAtItsTime()
    {
        using var listed = await ListedStash.Open();
        listed.Notices.Raise(Moving.MovedWords(@"C:\Elsewhere\stash"));

        Pass(listed, Notices.Lasting);

        Assert.IsEmpty(listed.Notices.Shown);
    }

    [TestMethod]
    public async Task ASecondRefusedPasteLeavesOneSuchNoticeTheLater()
    {
        using var listed = await ListedStash.Open();

        await listed.Paste.Paste();
        var first = listed.Notices.Shown.Single();
        await listed.Paste.Paste();
        Pass(listed, AMinute);

        Assert.HasCount(1, listed.Notices.Shown);
        Assert.AreNotSame(first, listed.Notices.Shown.Single(), "the later paste's notice took the earlier's place");
    }

    [TestMethod]
    public async Task APasteThatThenSucceedsLeavesNoEarlierPasteFailureStandingThoughTheImportRaisesItsResult()
    {
        using var listed = await ListedStash.Open();
        await listed.Paste.Paste();
        var refused = listed.Notices.Shown.Single();

        listed.Clipboard.Reading = Outcome<ClipboardReading>.Of(new ClipboardReading([], MediaFixtures.Bytes("transparent.png"), null, false));
        await listed.Paste.Paste();

        Assert.DoesNotContain(refused, listed.Notices.Shown);
        Assert.HasCount(1, listed.Notices.Shown);
        Assert.AreEqual("Added 1 to your stash", listed.Notices.Shown.Single().Heading);
    }

    [TestMethod]
    public async Task ADropsResultLeavesAPastesFailureStanding()
    {
        using var listed = await ListedStash.Open();
        await listed.Paste.Paste();
        var refused = listed.Notices.Shown.Single();

        await listed.Import.Drop(ListedStash.Contents([MediaFixtures.Path("still.png")]));
        Pass(listed, AMinute);

        CollectionAssert.AreEqual(new[] { refused }, listed.Notices.Shown.ToList(), "another action's result, gone at its time; the paste's failure stays");
    }

    [TestMethod]
    public async Task AnEarlierActsProblemNoticeArrivingAfterALaterActsNoticeIsTakenOverAsItComes()
    {
        using var listed = await ListedStash.Open();
        var begun = new TaskCompletionSource();
        var answered = new TaskCompletionSource();
        var earlier = Task.Run(async () =>
        {
            using var acting = UserAct.Begin(ClipboardPaste.Action);
            begun.SetResult();
            await answered.Task;
            return listed.Notices.Raise(new Failure("Add to the stash", "dog.png", "it is unreadable"));
        });
        await begun.Task;
        Notice later;
        using (UserAct.Begin(ClipboardPaste.Action))
        {
            later = listed.Notices.Raise("Added 1 to your stash: cat.png.");
        }

        answered.SetResult();
        var late = await earlier;

        Assert.IsTrue(late.NamesAProblem);
        CollectionAssert.AreEqual(new[] { later }, listed.Notices.Shown.ToList(), "the later paste's notice, which came first, took its place");
    }

    [TestMethod]
    public async Task AnEarlierActsNoticeOfferingUndoArrivingLateKeepsItForItsTimeAndDoesNotStay()
    {
        using var listed = await ListedStash.Open();
        var begun = new TaskCompletionSource();
        var answered = new TaskCompletionSource();
        var earlier = Task.Run(async () =>
        {
            using var acting = UserAct.Begin(ClipboardPaste.Action);
            begun.SetResult();
            await answered.Task;
            return listed.Notices.Raise("Added 1 to your stash: cat.png. dog.png is unreadable.", "Undo", () => Task.FromResult(Outcome.Done), namesAProblem: true);
        });
        await begun.Task;
        using (UserAct.Begin(ClipboardPaste.Action))
        {
            listed.Notices.Raise(new Failure(ClipboardPaste.Action, "the clipboard", "it holds no picture or file"));
        }

        answered.SetResult();
        var late = await earlier;

        Assert.IsTrue(listed.Notices.OffersUndo(late), "not cut short");
        Pass(listed, Notices.LastingFor(late.Words));
        Assert.DoesNotContain(late, listed.Notices.Shown, "it leaves when its Undo ends");
    }

    [TestMethod]
    public async Task ASearchWhoseListingFailsTakesThePlaceOfTheSearchBeforeIt()
    {
        using var listed = await ListedStash.Open();
        // The window's wiring: the listing names its failures in the window's notices.
        var listing = new Listing(failure => listed.Notices.Raise(failure));
        await listing.StashOpened(listed.Stash.Store!);

        using (listed.Stash.HoldStore())
        {
            await listing.SetQuery("c");
            await listing.SetQuery("ca");
        }

        Assert.HasCount(1, listed.Notices.Shown, string.Join(" | ", listed.Notices.Shown.Select(notice => notice.Text)));
        Assert.IsTrue(listed.Notices.Shown.Single().NamesAProblem, "a failure, which stays until the next search's notice or its ×");
    }

    [TestMethod]
    public async Task OneActsNoticesStandTogether()
    {
        using var listed = await ListedStash.Open();
        using (UserAct.Begin(Deletion.Action))
        {
            listed.Notices.Raise("Deleted “cat”.", "Undo", () => Task.FromResult(Outcome.Done));
            listed.Notices.Raise(new Failure("Move to the Recycle Bin", "dog.png", HeldFileRetry.HeldReason));
            listed.Notices.Raise(new Failure("Copy", "owl.png", "it is locked"));
        }

        Assert.HasCount(3, listed.Notices.Shown);
    }

    [TestMethod]
    public async Task ATakenOverNoticeStillOfferingItsUndoKeepsItUntilItsTimeAndThenLeaves()
    {
        using var listed = await ListedStash.Open();
        var undone = 0;
        Notice skipped;
        using (UserAct.Begin("Drop"))
        {
            skipped = listed.Notices.Raise("Added 1 to your stash: cat.png. Skipped clip.mp4 (a video): ImageStash keeps GIF, PNG, JPG and WebP.", "Undo",
                () =>
                {
                    undone++;
                    return Task.FromResult(Outcome.Done);
                }, namesAProblem: true);
        }

        using (UserAct.Begin("Drop"))
        {
            listed.Notices.Raise("Added 1 to your stash: dog.png.");
        }

        Assert.IsTrue(listed.Notices.OffersUndo(skipped), "not cut short by the later drop");
        Pass(listed, Notices.LastingFor(skipped.Words));
        Assert.DoesNotContain(skipped, listed.Notices.Shown, "taken over, it leaves when its Undo ends and does not stay");
        await listed.Notices.Undo(skipped);
        Assert.AreEqual(0, undone);
    }

    [TestMethod]
    public async Task AtItsTimeAProblemNoticesUndoEndsWhatItLeavesWithRunsOnceAndTheWordsAndCloseRemain()
    {
        using var listed = await ListedStash.Open();
        var ran = new List<string>();
        var notice = listed.Notices.Raise("Added 4 to your stash. Skipped clip.mp4 (a video).", "Undo", () =>
        {
            ran.Add("undo");
            return Task.FromResult(Outcome.Done);
        }, () =>
        {
            ran.Add("left");
            return Task.CompletedTask;
        }, namesAProblem: true);

        Pass(listed, Notices.LastingFor(notice.Words) - TimeSpan.FromMilliseconds(1));
        Assert.IsTrue(listed.Notices.OffersUndo(notice), "its Undo stands for the notice's time");
        Pass(listed, TimeSpan.FromMilliseconds(1));
        Assert.IsFalse(listed.Notices.OffersUndo(notice), "and ends at it");
        CollectionAssert.AreEqual(new[] { notice }, listed.Notices.Shown.ToList(), "the words stay");
        await listed.Notices.Undo(notice);
        Pass(listed, AMinute);
        listed.Notices.Close(notice);

        CollectionAssert.AreEqual(new[] { "left" }, ran, "what it was given for leaving, once, at the Undo's end; never the Undo, nor again at its ×");
        Assert.IsEmpty(listed.Notices.Shown);
    }

    [TestMethod]
    public async Task AnUndoEndedInAHiddenWindowTakesNotTheWordsOfANoticeNamingAProblem()
    {
        using var listed = await ListedStash.Open();
        var left = 0;
        var notice = listed.Notices.Raise("Added 4 to your stash. Skipped clip.mp4 (a video).", "Undo", () => Task.FromResult(Outcome.Done), () =>
        {
            left++;
            return Task.CompletedTask;
        }, namesAProblem: true);
        listed.WindowShowing.Seen(visible: false, minimised: false);

        listed.Notices.EndUndosWhileHidden();
        listed.WindowShowing.Seen(visible: true, minimised: false);
        Pass(listed, AMinute);

        CollectionAssert.AreEqual(new[] { notice }, listed.Notices.Shown.ToList());
        Assert.IsFalse(listed.Notices.OffersUndo(notice));
        Assert.AreEqual(1, left);
    }

    [TestMethod]
    public async Task WhatANoticeStartsOnLeavingRaisesAsTheActThatRaisedItThoughAMoveEndsIt()
    {
        using var listed = await ListedStash.Open();
        Notice deleted;
        using (UserAct.Begin(Deletion.Action))
        {
            deleted = listed.Notices.Raise("Deleted “cat”.", "Undo", () => Task.FromResult(Outcome.Done), () =>
            {
                listed.Notices.Raise(new Failure("Finish the delete", "cat.png", HeldFileRetry.HeldReason));
                return Task.CompletedTask;
            });
        }

        listed.WindowShowing.Seen(visible: false, minimised: false);
        using (UserAct.Begin(StashMove.Action))
        {
            listed.Notices.EndUndosWhileHidden();
        }

        var finishFailed = listed.Notices.Shown.Single();
        Assert.AreSame(deleted.Act, finishFailed.Act, "the Delete's, so the next Delete's notice takes its place");
    }

    [TestMethod]
    public async Task AProblemNoticesUndoKeepsD01sClockHiddenAndHovered()
    {
        using var listed = await ListedStash.Open();
        listed.WindowShowing.Seen(visible: false, minimised: false);
        var notice = listed.Notices.Raise("Added 4 to your stash. Skipped clip.mp4 (a video).", "Undo", () => Task.FromResult(Outcome.Done), namesAProblem: true);

        listed.Stash.Clock.Now += TimeSpan.FromHours(1);
        listed.Notices.Sweep();
        Assert.IsTrue(listed.Notices.OffersUndo(notice), "an hour hidden takes nothing of its time");
        listed.WindowShowing.Seen(visible: true, minimised: false);
        listed.Notices.Hover(notice, over: true);
        Pass(listed, AMinute);
        Assert.IsTrue(listed.Notices.OffersUndo(notice), "hovered, its time is held");
        listed.Notices.Hover(notice, over: false);
        Pass(listed, Notices.LastingFor(notice.Words));

        Assert.IsFalse(listed.Notices.OffersUndo(notice));
        CollectionAssert.AreEqual(new[] { notice }, listed.Notices.Shown.ToList());
    }

    [TestMethod]
    public async Task AFailureAnUndoRaisesStaysAsAnyFailureDoes()
    {
        using var listed = await ListedStash.Open();
        var notice = listed.Notices.Raise("Deleted “cat”.", "Undo", () => Task.FromResult<Outcome>(new Failure("Undo the delete", "cat.png", "its held copy is gone")));

        await listed.Notices.Undo(notice);
        Pass(listed, AMinute);

        Assert.AreEqual("Couldn't undo the delete: cat.png's held copy is gone.", listed.Notices.Shown.Single().Text);
    }

    [TestMethod]
    public async Task AboveTheTrayAFailureAndCopiedPressCtrlVLeaveAtSixSeconds()
    {
        using var listed = await ListedStash.Open();
        listed.Notifications.Raise(new Failure("Stash clipboard image", "the clipboard", "it holds no image"));
        using (UserAct.Begin("Hand over"))
        {
            listed.Notifications.Raise(Handoff.CopiedNotification);
        }

        listed.Stash.Clock.Now += Notices.Lasting;
        listed.Notifications.Sweep();

        Assert.IsEmpty(listed.Notifications.Shown);
    }

    [TestMethod]
    public async Task TheCardOfANoticeWhoseUndoHasEndedShowsItsWordsAndCloseAndNoUndo()
    {
        using var listed = await ListedStash.Open();
        var notice = listed.Notices.Raise("Added 4 to your stash. Skipped clip.mp4 (a video).", "Undo", () => Task.FromResult(Outcome.Done), namesAProblem: true);

        ControlThread.Run(() =>
        {
            var card = NoticeCard.For(notice, listed.Notices);
            Assert.IsTrue(Undo(card).IsVisible, "offered while its time runs");

            Pass(listed, Notices.LastingFor(notice.Words));

            Assert.IsFalse(Undo(card).IsVisible, "gone with the Undo's end");
            Assert.Contains(notice.Text, card.GetSelfAndLogicalDescendants().OfType<TextBlock>().Select(text => text.Text).ToList());
            Assert.HasCount(1, card.GetSelfAndLogicalDescendants().OfType<Button>().Where(button => button.IsVisible && !ReferenceEquals(button, Undo(card))).ToList(), "the ×");
        });
    }

    private static Button Undo(Control card) => card.GetSelfAndLogicalDescendants().OfType<Button>().Single(button => Equals(button.Content, "Undo"));

    /// <summary>Time the window shows passing, the sweep run as the window's timer runs it.</summary>
    private static void Pass(ListedStash listed, TimeSpan shown)
    {
        listed.Stash.Clock.Now += shown;
        listed.Notices.Sweep();
    }
}
