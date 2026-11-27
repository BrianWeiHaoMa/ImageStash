using Avalonia.Input;
using ImageStash.Store;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// The preview's steps through the grid's list as the window takes the arrows (G19, G7, G8): each key through the
/// window's routing to the step's act over the real listing, which reads the list as it is at the press, in All, a
/// search's results, Recent and Favorites; does nothing past either end; follows the place an item that left the list
/// last stood at; selects the item it shows alone, so the keys that act on the preview's item act on it; and neither
/// waits for nor takes along a rename still being written.
/// </summary>
[TestClass]
public sealed class PreviewSteppingTests
{
    [TestMethod]
    public async Task InAllRightAndDownShowTheNextItemAndLeftAndUpThePreviousEachSelectingItAlone()
    {
        using var listed = await ListedStash.Open();
        var all = await Listed(listed, Scope.All, "one", "two", "three", "four");
        // Newest added first: four, three, two, one; the grid's selection left elsewhere, on the first.
        OpenOn(listed, all[1]);
        listed.Listing.Focus(all[0].Id);

        await Step(listed, Key.Right, all[2]);
        await Step(listed, Key.Down, all[3]);
        await Step(listed, Key.Left, all[2]);
        await Step(listed, Key.Up, all[1]);
    }

    [TestMethod]
    public async Task AtTheListsFirstItemTheStepBackAndAtItsLastTheStepOnDoNothing()
    {
        using var listed = await ListedStash.Open();
        var all = await Listed(listed, Scope.All, "one", "two", "three");
        OpenOn(listed, all[0]);
        listed.Listing.Focus(all[0].Id);

        await Unmoved(listed, Key.Left, all[0]);
        await Unmoved(listed, Key.Up, all[0]);
        await Step(listed, Key.Right, all[1]);
        await Step(listed, Key.Right, all[2]);
        await Unmoved(listed, Key.Right, all[2]);
        await Unmoved(listed, Key.Down, all[2]);
        Assert.IsTrue(listed.PreviewOpen, "the preview stays open at either end");
    }

    [TestMethod]
    public async Task InASearchTheStepsFollowTheResultsOrderNotAllsNorTheItemsIdentities()
    {
        using var listed = await ListedStash.Open();
        foreach (var name in new[] { "a cat", "cat", "bobcat" })
        {
            await listed.Stash.Import(name);
        }

        await listed.Listing.SetQuery("cat");
        // G4: the matches where "cat" starts a word first, newest added first ("cat", "a cat"), then "bobcat".
        CollectionAssert.AreEqual(new[] { "cat", "a cat", "bobcat" }, listed.Listing.Items.Select(item => item.Name).ToList());
        var results = listed.Listing.Items;
        OpenOn(listed, results[0]);

        await Unmoved(listed, Key.Left, results[0]);
        await Step(listed, Key.Right, results[1]);
        await Step(listed, Key.Right, results[2]);
        await Unmoved(listed, Key.Right, results[2]);
    }

    [TestMethod]
    public async Task InRecentTheStepsFollowTheDisplayedOrderUntilTheNextSummon()
    {
        using var listed = await ListedStash.Open();
        // Real pictures: a copy puts a still's bitmap on the clipboard too.
        foreach (var fixture in new[] { "photo.jpg", "transparent.png", "still.webp" })
        {
            await listed.Stash.ImportFixture(Path.GetFileNameWithoutExtension(fixture), fixture);
        }

        await listed.Listing.ShowScope(Scope.All);
        var all = listed.Listing.Items;
        foreach (var used in new[] { all[1], all[2], all[0] })
        {
            listed.Stash.Clock.Now += TimeSpan.FromMinutes(1);
            Assert.IsTrue((await listed.Handoff.Copy(null!, [used])).Succeeded);
        }

        await listed.Listing.ShowScope(Scope.Recent);
        var recent = listed.Listing.Items;
        CollectionAssert.AreEqual(new[] { all[0].Id, all[2].Id, all[1].Id }, recent.Select(item => item.Id).ToList(), "latest use first (G4)");
        OpenOn(listed, recent[1]);
        await Step(listed, Key.Right, recent[2]);

        // A copy in the preview records use while stepping still follows the displayed list (G4, G10, G19).
        listed.Stash.Clock.Now += TimeSpan.FromMinutes(1);
        Assert.IsTrue((await listed.Handoff.Copy(null!, [listed.PreviewItem!])).Succeeded);
        await listed.Listing.Settle();
        Assert.AreEqual(recent[2].Id, (await listed.Stash.Store!.Recent()).Value![0].Id, "the copy recorded the latest use");
        CollectionAssert.AreEqual(recent.Select(item => item.Id).ToArray(), listed.Listing.Items.Select(item => item.Id).ToArray());
        await Unmoved(listed, Key.Right, recent[2]);
        await Step(listed, Key.Left, recent[1]);
        await Step(listed, Key.Right, recent[2]);

        await listed.Summon(pinned: true);
        Assert.AreEqual(recent[2].Id, listed.Listing.Items[0].Id, "the summon takes the latest order");
        await Unmoved(listed, Key.Left, recent[2]);
        await Step(listed, Key.Right, recent[0]);
    }

    [TestMethod]
    public async Task ADeleteUndoneBehindThePreviewIsSteppedToFromTheItemBeforeIt()
    {
        using var listed = await ListedStash.Open();
        var all = await Listed(listed, Scope.All, "one", "two", "three", "four");
        Assert.IsTrue((await listed.Deletion.Delete([all[2]])).Succeeded);
        await listed.Listing.Settle();
        OpenOn(listed, all[1]);

        Assert.AreEqual(KeyAction.UndoNotice, await listed.Press(Key.Z, KeyModifiers.Control, KeyPlace.Preview));
        listed.Keys.Released(Key.Z);
        await listed.Listing.Settle();

        await Step(listed, Key.Right, all[2]);
    }

    [TestMethod]
    public async Task DelInThePreviewShowsTheItemThatTookItsPlaceAndASecondDelDeletesThatOne()
    {
        using var listed = await ListedStash.Open();
        var all = await Listed(listed, Scope.All, "e", "d", "c", "b", "a");
        // Newest added first: a, b, c, d, e.
        OpenOn(listed, all[2]);
        listed.Listing.FocusForPreview(all[2].Id);

        await Del(listed);
        Assert.IsTrue(listed.PreviewOpen);
        Assert.AreEqual(all[3].Id, listed.PreviewItem?.Id, "d, as › would have shown");
        CollectionAssert.AreEqual(new[] { all[3].Id }, listed.Listing.Selection.ToList(), "and the grid's one selected item, so the keys act on it");

        await Del(listed);
        Assert.AreEqual(all[4].Id, listed.PreviewItem?.Id, "the second Del took d, the item shown, and shows e");
        CollectionAssert.AreEqual(new[] { all[0].Id, all[1].Id, all[4].Id }, listed.Listing.Items.Select(item => item.Id).ToList());

        await Del(listed);
        Assert.AreEqual(all[1].Id, listed.PreviewItem?.Id, "e was the last: the new last");
        await Del(listed);
        await Del(listed);
        Assert.IsFalse(listed.PreviewOpen, "the only item deleted: the list is empty and the preview closes");
        Assert.IsEmpty(listed.Failures);
    }

    [TestMethod]
    public async Task ARenameEndingTheSearchsMatchLeavesTheStepOnToWhatWasTheSecondResult()
    {
        using var listed = await ListedStash.Open();
        foreach (var name in new[] { "screenshot one", "screenshot two", "screenshot three" })
        {
            await listed.Stash.Import(name);
        }

        await listed.Listing.SetQuery("screenshot");
        var results = listed.Listing.Items;
        OpenOn(listed, results[0]);

        Assert.IsTrue((await listed.Renaming.Rename(results[0], "monday")).Succeeded);
        await listed.Listing.Settle();
        Assert.IsNull(listed.Listing.PlaceOf(results[0].Id), "the renamed item has left the results");

        await Unmoved(listed, Key.Left, listed.PreviewItem!);
        await Step(listed, Key.Right, results[1]);
    }

    [TestMethod]
    public async Task AStarClickedOffWhileFavoritesShowsLeavesTheStepsAroundThePlaceItStoodAt()
    {
        using var listed = await ListedStash.Open();
        var favorites = await Favorites(listed, "one", "two", "three");
        var shown = favorites[1];
        OpenOn(listed, shown);

        Assert.IsTrue((await listed.Organising.ToggleFavorite([shown])).Succeeded);
        await listed.Listing.Settle();
        Assert.IsNull(listed.Listing.PlaceOf(shown.Id), "no longer a favorite, it has left Favorites");

        Assert.AreEqual(favorites[0].Id, listed.PreviewStepping.Next(StepWay.Back)?.Id, "back: the one before the place it stood at");
        await Step(listed, Key.Right, favorites[2]);
    }

    [TestMethod]
    public async Task WhereFavoritesHasShrunkPastThePlaceTheStepOnDoesNothingAndTheStepBackShowsTheLastFavorite()
    {
        using var listed = await ListedStash.Open();
        var favorites = await Favorites(listed, "one", "two", "three");
        var shown = favorites[2];
        OpenOn(listed, shown);

        Assert.IsTrue((await listed.Organising.ToggleFavorite([shown])).Succeeded);
        await listed.Listing.Settle();

        await Unmoved(listed, Key.Right, shown);
        await Step(listed, Key.Left, favorites[1]);
    }

    [TestMethod]
    public async Task AnItemThatNeverStoodInFavoritesStepsOnToTheFirstFavoriteAndBackToNothing()
    {
        using var listed = await ListedStash.Open();
        var plain = await listed.Stash.Import("no favorite");
        var favorites = await Favorites(listed, "one", "two");
        // As Save as new item leaves the preview on a new item, which is no favorite, while Favorites shows.
        OpenOn(listed, plain);
        listed.Listing.Focus(favorites[1].Id);

        await Unmoved(listed, Key.Left, plain);
        await Step(listed, Key.Right, favorites[0]);
    }

    [TestMethod]
    public async Task WithFavoritesEmptiedByTheStarBothStepsDoNothing()
    {
        using var listed = await ListedStash.Open();
        var favorites = await Favorites(listed, "only");
        OpenOn(listed, favorites[0]);

        Assert.IsTrue((await listed.Organising.ToggleFavorite([favorites[0]])).Succeeded);
        await listed.Listing.Settle();
        Assert.IsEmpty(listed.Listing.Items);

        await Unmoved(listed, Key.Right, favorites[0]);
        await Unmoved(listed, Key.Left, favorites[0]);
    }

    [TestMethod]
    public async Task FivePressesWithoutWaitingLeaveTheFifthItemShownAndSelected()
    {
        using var listed = await ListedStash.Open();
        var all = await Listed(listed, Scope.All, "one", "two", "three", "four", "five", "six", "seven");
        OpenOn(listed, all[0]);

        var presses = Enumerable.Range(0, 5).Select(_ => listed.Press(Key.Right, KeyModifiers.None, KeyPlace.Preview)).ToList();
        await Task.WhenAll(presses);

        Assert.AreEqual(all[5].Id, listed.PreviewItem?.Id, "a held or quickly pressed Right steps on every press");
        CollectionAssert.AreEqual(new[] { all[5].Id }, listed.Listing.Selection.ToList());
        Assert.AreEqual(all[5].Id, listed.Listing.Focused);
    }

    [TestMethod]
    public async Task ARenameHeldByAScannerWhenRightIsPressedLandsOnTheItemItWasTypedForAndTheStepDoesNotWait()
    {
        using var listed = await ListedStash.Open(storeRetry: TimeSpan.FromSeconds(30));
        var all = await Listed(listed, Scope.All, "one", "two", "three");
        var typedFor = all[0];
        OpenOn(listed, typedFor);

        Task<Outcome> renaming;
        using (TestStash.Hold(typedFor.FilePath))
        {
            // Focus leaving the name starts its rename on the item the preview shows (G19), held here (E3); then Right.
            renaming = listed.Renaming.Rename(typedFor, "renamed");
            var pressed = listed.Press(Key.Right, KeyModifiers.None, KeyPlace.PreviewButton);

            Assert.IsTrue(pressed.IsCompleted, "the step does not wait for the rename");
            Assert.AreEqual(KeyAction.StepOn, await pressed);
            Assert.AreEqual(all[1].Id, listed.PreviewItem?.Id);
            CollectionAssert.AreEqual(new[] { all[1].Id }, listed.Listing.Selection.ToList());
            Assert.IsFalse(renaming.IsCompleted, "the rename is still held");
        }

        Assert.IsTrue((await renaming).Succeeded, (await renaming).Failure?.Sentence);
        var stored = await listed.Stash.All();
        Assert.AreEqual("renamed", stored.Single(item => item.Id == typedFor.Id).Name, "the rename lands on the item it was typed for");
        Assert.AreEqual(all[1].Name, stored.Single(item => item.Id == all[1].Id).Name, "the item stepped to keeps its name");
        Assert.AreEqual(all[1].Id, listed.PreviewItem?.Id, "and stays shown");
    }

    [TestMethod]
    public async Task ARenameRefusedAfterTheStepNamesTheItemItWasTypedFor()
    {
        using var listed = await ListedStash.Open();
        var all = await Listed(listed, Scope.All, "first picture", "second picture");
        var typedFor = all[0];
        OpenOn(listed, typedFor);

        Outcome renamed;
        using (TestStash.Hold(typedFor.FilePath))
        {
            var renaming = listed.Renaming.Rename(typedFor, "renamed");
            await Step(listed, Key.Right, all[1]);
            renamed = await renaming;
        }

        Assert.IsFalse(renamed.Succeeded, "held past its retries, the rename is refused (G35)");
        var named = listed.Notices.Shown.Single(notice => notice.NamesAProblem);
        StringAssert.Contains(named.Text, typedFor.Name, "the refusal names the item the name was typed for");
        Assert.DoesNotContain(all[1].Name, named.Text, "never the item the preview now shows");
        Assert.AreEqual(all[1].Id, listed.PreviewItem?.Id);
    }

    /// <summary>Imports <paramref name="names"/> in order and shows <paramref name="scope"/>; the items as listed.</summary>
    private static async Task<IReadOnlyList<StashItem>> Listed(ListedStash listed, Scope scope, params string[] names)
    {
        foreach (var name in names)
        {
            await listed.Stash.Import(name);
        }

        await listed.Listing.ShowScope(scope);
        Assert.HasCount(names.Length, listed.Listing.Items);
        return listed.Listing.Items;
    }

    /// <summary>Imports <paramref name="names"/> as favorites and shows Favorites; the favorites as listed.</summary>
    private static async Task<IReadOnlyList<StashItem>> Favorites(ListedStash listed, params string[] names)
    {
        foreach (var name in names)
        {
            var item = await listed.Stash.Import(name);
            Assert.IsTrue((await listed.Stash.Store!.SetFavorite([item], true)).All(outcome => outcome.Succeeded));
        }

        await listed.Listing.ShowScope(Scope.Favorites);
        Assert.HasCount(names.Length, listed.Listing.Items);
        return listed.Listing.Items;
    }

    /// <summary>The preview told open on <paramref name="item"/>, as the check's starting point.</summary>
    /// <summary>Del in the preview, its key let go first so the press acts.</summary>
    private static async Task Del(ListedStash listed)
    {
        listed.Keys.Released(Key.Delete);
        Assert.AreEqual(KeyAction.Delete, await listed.Press(Key.Delete, KeyModifiers.None, KeyPlace.Preview));
    }

    private static void OpenOn(ListedStash listed, StashItem item)
    {
        listed.PreviewOpen = true;
        listed.PreviewItem = item;
    }

    /// <summary>A press of <paramref name="key"/> in the preview steps to <paramref name="expected"/>, selecting it alone.</summary>
    private static async Task Step(ListedStash listed, Key key, StashItem expected)
    {
        var action = await listed.Press(key, KeyModifiers.None, KeyPlace.Preview);
        Assert.AreEqual(key is Key.Left or Key.Up ? KeyAction.StepBack : KeyAction.StepOn, action);
        Assert.AreEqual(expected.Id, listed.PreviewItem?.Id, $"{key} shows {expected.Name}");
        CollectionAssert.AreEqual(new[] { expected.Id }, listed.Listing.Selection.ToList(), $"{key}: the item shown is the grid's one selected item (G8)");
        Assert.AreEqual(expected.Id, listed.Listing.Focused);
    }

    /// <summary>A press of <paramref name="key"/> in the preview changes neither the item shown nor the selection.</summary>
    private static async Task Unmoved(ListedStash listed, Key key, StashItem shown)
    {
        var selected = listed.Listing.Selection.ToList();
        await listed.Press(key, KeyModifiers.None, KeyPlace.Preview);
        Assert.AreEqual(shown.Id, listed.PreviewItem?.Id, $"{key} does nothing here");
        CollectionAssert.AreEquivalent(selected, listed.Listing.Selection.ToList(), $"{key} leaves the selection");
    }
}
