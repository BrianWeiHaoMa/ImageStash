using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using ImageStash.Store;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// What the name and tag fields do as focus moves, one rule for all three (G17, G19, G7, G1): focus going to another
/// part of the window, whoever moves it, commits what Enter would commit and ends the edit; focus landing inside the
/// field, in its box or its own lists, changes nothing; Esc ends the edit before its own focus move, which then meets no
/// edit. The commit is Enter's own, over a real store, on the items given at the move: refusals named as Enter's are,
/// and an act asked after it, a Delete or an Edit, follows it (G18, G20, G35).
/// </summary>
[TestClass]
public sealed class FieldLeavingTests
{
    [TestMethod]
    public void FocusLandingElsewhereInTheWindowCommitsWhoeverMovedIt() =>
        Assert.IsTrue(FieldLeaving.Commits(holdsEdit: true, intoField: false), "a click, Tab, a click on the preview's name, a summon putting focus in the search box (G17, G19)");

    [TestMethod]
    public void FocusLandingInsideTheFieldOrOnceTheEditEndedCommitsNothing()
    {
        Assert.IsFalse(FieldLeaving.Commits(holdsEdit: true, intoField: true), "its box, its suggestions or its list of every tag: the field stays open on what was typed (G17)");
        Assert.IsFalse(FieldLeaving.Commits(holdsEdit: false, intoField: false), "Esc, or the window closing the field, ends the edit before focus moves, so that move commits nothing (G19)");
    }

    [TestMethod]
    public async Task LeavingCommitsWhatEnterWouldOntoEveryItemGivenTheLitSuggestionIncluded()
    {
        using var listed = await ListedStash.Open();
        var (cat, dog, emu) = (await listed.Stash.Import("cat", TestStash.Bytes(1)), await listed.Stash.Import("dog", TestStash.Bytes(2)), await listed.Stash.Import("emu", TestStash.Bytes(3)));
        await listed.Stash.Store!.AddTag([emu], "reaction");
        string? lit = null, typed = null;
        ControlThread.Run(() =>
        {
            lit = TagFieldTests.EnterCommitsAfter("r", Key.Down, "reaction");
            typed = TagFieldTests.EnterCommitsAfter("  funny   #cat ", null, "reaction");
        });

        Assert.IsTrue(FieldLeaving.Commits(holdsEdit: true, intoField: false));
        var leftWithSuggestion = await FieldLeaving.AddTags(listed.Organising, [cat, dog], lit!);
        var leftWithWords = await FieldLeaving.AddTags(listed.Organising, [cat], typed!);

        Assert.IsTrue(leftWithSuggestion.Succeeded && leftWithWords.Succeeded);
        CollectionAssert.AreEqual(new[] { "cat", "funny", "reaction" }, await Tags(listed, cat), "the lit suggestion, not the letter typed, and the words split as Enter splits them");
        CollectionAssert.AreEqual(new[] { "reaction" }, await Tags(listed, dog), "every item the field was opened on");
        CollectionAssert.AreEqual(new[] { "reaction" }, await Tags(listed, emu), "and no other");
        var notice = listed.Notices.Shown.Single();
        Assert.AreEqual("Tagged 2 items #reaction", notice.Text, "the single-picture edit answers in place");
        Assert.IsTrue(listed.Notices.OffersUndo(notice));
    }

    [TestMethod]
    public async Task EnterWithMixedTagGainsReportsEachTagAndUndoKeepsTheEarlierMembership()
    {
        using var listed = await ListedStash.Open();
        var cat = await listed.Stash.Import("cat");
        var dog = await listed.Stash.Import("dog");
        await listed.Stash.Store!.AddTag([cat], "reaction");
        await listed.Listing.ShowScope(Scope.All);

        ControlThread.Run(() => ControlThread.FinishOnDispatcher(async () =>
        {
            var overlay = new TagFieldOverlay(listed.Listing, listed.Organising, listed.Notices);
            overlay.Open([cat, dog], new Button());
            var box = overlay.Field.GetVisualDescendants().OfType<TextBox>().Single();
            await listed.WaitForOrganising(() =>
            {
                box.Text = "reaction #funny";
                box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            });

            var notice = listed.Notices.Shown.Single();
            Assert.IsTrue(listed.Notices.OffersUndo(notice));
            StringAssert.StartsWith(notice.Text, "Updated tags on 2 items");
            StringAssert.Contains(notice.Text, "#reaction added to 1 item");
            StringAssert.Contains(notice.Text, "#funny added to 2 items");
            CollectionAssert.AreEqual(new[] { "funny", "reaction" }, await Tags(listed, cat));
            CollectionAssert.AreEqual(new[] { "funny", "reaction" }, await Tags(listed, dog));
            Assert.IsTrue(overlay.IsVisible);

            await listed.Notices.Undo(notice);
            CollectionAssert.AreEqual(new[] { "reaction" }, await Tags(listed, cat));
            Assert.IsEmpty(await Tags(listed, dog));
            box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
        }));
    }

    [TestMethod]
    public async Task LeavingWithNothingTypedChangesNothing()
    {
        using var listed = await ListedStash.Open();
        var cat = await listed.Stash.Import("cat");

        var left = await FieldLeaving.AddTags(listed.Organising, [cat], " # ");

        Assert.IsTrue(left.Succeeded);
        Assert.IsEmpty(await Tags(listed, cat), "nothing requires a tag (G17)");
        Assert.IsEmpty(listed.Notices.Shown);
    }

    [TestMethod]
    public async Task ADeleteClickedAsFocusLeftFollowsTheCommitAndNamesNoFailure()
    {
        using var listed = await ListedStash.Open();
        var cat = await listed.Stash.Import("cat");
        await listed.Listing.Refresh();

        // Focus left the field for Delete: its commit is started, not awaited, then the click's act.
        var committing = FieldLeaving.AddTags(listed.Organising, [cat], "funny");
        var deleted = await listed.Deletion.Delete([cat]);
        var committed = await committing;

        Assert.IsTrue(committed.Succeeded, committed.Failure?.Sentence);
        Assert.IsTrue(deleted.Succeeded, deleted.Failure?.Sentence);
        Assert.IsNull((await listed.Stash.Store!.Item(cat.Id)).Value, "the item is deleted (G18)");
        Assert.IsFalse(listed.Notices.Shown.Any(notice => notice.NamesAProblem), "no failure about an item that is gone: the tag was added first (G35)");
        await listed.Notices.Undo(listed.Notices.Shown.Single());
        CollectionAssert.AreEqual(new[] { "funny" }, await Tags(listed, cat), "Enter, then the click: the Delete took the item with its tag, and its Undo brings both back");
    }

    [TestMethod]
    public async Task AnEditClickedAsFocusLeftTheNameOpensOnTheRenamedItem()
    {
        using var listed = await ListedStash.Open();
        var cat = await listed.Stash.ImportFixture("cat", "still.png");
        await listed.Listing.Refresh();

        // The preview's rename on leaving and its Edit go through the same paths as Enter's rename and the Edit button.
        var renaming = listed.Renaming.Rename(cat, "dog");
        var opened = await listed.Editing.Open(cat, EditOrigin.Preview);
        await renaming;

        Assert.IsTrue(opened.Succeeded, opened.Failure?.Sentence);
        Assert.AreEqual("dog", listed.Editing.Session?.Item.Name, "Edit opens once that change has run (G20)");
        listed.Editing.Cancel();
    }

    [TestMethod]
    public async Task ACommitOnLeavingTheStoreRefusesIsNamedAsEnters()
    {
        using var listed = await ListedStash.Open();
        var cat = await listed.Stash.Import("cat");
        await listed.Stash.Store!.Delete([cat.Id]);
        await listed.Listing.Refresh();

        var left = await FieldLeaving.AddTags(listed.Organising, [cat], "funny");

        Assert.IsFalse(left.Succeeded, "an item gone from the stash is refused");
        var named = listed.Notices.Shown.Single();
        Assert.AreEqual(left.Failure!.Sentence, named.Text, "named in a notice, though no one is looking at the field (G35)");
        Assert.IsTrue(named.NamesAProblem);
    }

    private static async Task<List<string>> Tags(ListedStash listed, StashItem item) =>
        (await listed.Stash.Store!.Item(item.Id)).Value!.Tags.Order(StringComparer.OrdinalIgnoreCase).ToList();
}
