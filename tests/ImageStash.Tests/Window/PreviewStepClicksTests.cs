using ImageStash.Store;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// The preview's ‹ and › as the window runs their clicks (G19, G17, G35, G2, G20, G7), over the real listing and store,
/// a field's draft handed in as the preview's field hands it: each click steps as the arrows do; what is typed is
/// committed to the item it was typed for before the move, through the same organising and rename entries; the next item
/// is the one decided before that commit changed the list; a refused commit leaves the preview where it is; clicks run in
/// order, each from the item shown at its turn; a move still waiting when the preview closes or is closing, or an edit
/// opens on its item, is dropped with the commit standing; and each button is available exactly where its step shows an item.
/// </summary>
[TestClass]
public sealed class PreviewStepClicksTests
{
    [TestMethod]
    public async Task NextAndPreviousStepAsTheArrowsDoEachSelectingTheItemShownAlone()
    {
        using var listed = await ListedStash.Open();
        var all = await Listed(listed, Scope.All, "one", "two", "three");
        OpenOn(listed, all[1]);
        listed.Listing.Focus(all[0].Id);

        await Clicked(listed, StepWay.On, all[2]);
        await Clicked(listed, StepWay.Back, all[1]);
        await Clicked(listed, StepWay.Back, all[0]);
    }

    [TestMethod]
    public async Task ANameTypedForTheItemShownIsCommittedToItOnceAndTheItemSteppedToShowsUnderItsOwnName()
    {
        using var listed = await ListedStash.Open();
        var all = await Listed(listed, Scope.All, "first", "second", "third");
        OpenOn(listed, all[0]);
        var commits = 0;

        await listed.PreviewStepClicks.Click(StepWay.On, Typed(() =>
        {
            commits++;
            return listed.Renaming.Rename(listed.PreviewItem!, "x");
        }));

        Assert.AreEqual(1, commits, "committed once");
        var stored = await listed.Stash.All();
        Assert.AreEqual("x", stored.Single(item => item.Id == all[0].Id).Name, "the name lands on the item it was typed for");
        Assert.AreEqual(all[1].Name, stored.Single(item => item.Id == all[1].Id).Name, "never on the item stepped to");
        Assert.AreEqual(all[1].Id, listed.PreviewItem?.Id);
        Assert.AreEqual(all[1].Name, listed.PreviewItem?.Name, "it shows under its own name");
        CollectionAssert.AreEqual(new[] { all[1].Id }, listed.Listing.Selection.ToList());
    }

    [TestMethod]
    public async Task ATagTypedForTheItemShownIsAddedToItAndNotToTheItemSteppedTo()
    {
        using var listed = await ListedStash.Open();
        var all = await Listed(listed, Scope.All, "first", "second");
        OpenOn(listed, all[0]);

        await listed.PreviewStepClicks.Click(StepWay.On, Typed(() => FieldLeaving.AddTags(listed.Organising, [listed.PreviewItem!], "cat")));

        var stored = await listed.Stash.All();
        CollectionAssert.Contains(stored.Single(item => item.Id == all[0].Id).Tags.ToList(), "cat");
        Assert.IsEmpty(stored.Single(item => item.Id == all[1].Id).Tags, "the item stepped to carries no tag it was not given");
        Assert.AreEqual(all[1].Id, listed.PreviewItem?.Id);
    }

    [TestMethod]
    public async Task ARenameThatReordersTheResultsStepsToTheItemThatFollowedItBeforeTheRename()
    {
        using var listed = await ListedStash.Open();
        foreach (var name in new[] { "cat a", "cat b", "bobcat", "wildcat" })
        {
            await listed.Stash.Import(name);
        }

        await listed.Listing.SetQuery("cat");
        // G4: the words starting "cat" first, newest added first, then the other matches.
        CollectionAssert.AreEqual(new[] { "cat b", "cat a", "wildcat", "bobcat" }, Names(listed));
        var results = listed.Listing.Items;
        OpenOn(listed, results[2]);

        // Renamed so "cat" starts a word, it moves to the results' head, before "cat b".
        await listed.PreviewStepClicks.Click(StepWay.On, Typed(() => listed.Renaming.Rename(listed.PreviewItem!, "cat wild")));

        CollectionAssert.AreEqual(new[] { "cat wild", "cat b", "cat a", "bobcat" }, Names(listed), "the rename reordered the results");
        Assert.AreEqual(results[3].Id, listed.PreviewItem?.Id, "the item that followed it when the click was made");
        CollectionAssert.AreEqual(new[] { results[3].Id }, listed.Listing.Selection.ToList());
    }

    [TestMethod]
    public async Task ARenameLeavingTheSearchStepsToTheItemThatFollowedItInTheResults()
    {
        using var listed = await ListedStash.Open();
        foreach (var name in new[] { "cat one", "cat two", "cat three" })
        {
            await listed.Stash.Import(name);
        }

        await listed.Listing.SetQuery("cat");
        var results = listed.Listing.Items;
        OpenOn(listed, results[0]);

        await listed.PreviewStepClicks.Click(StepWay.On, Typed(() => listed.Renaming.Rename(listed.PreviewItem!, "monday")));

        Assert.IsNull(listed.Listing.PlaceOf(results[0].Id), "the renamed item has left the results");
        Assert.AreEqual(results[1].Id, listed.PreviewItem?.Id);
        CollectionAssert.AreEqual(new[] { results[1].Id }, listed.Listing.Selection.ToList());
    }

    [TestMethod]
    public async Task ARenameTheStoreRefusesIsNamedForItsItemAndThePreviewStaysOnIt()
    {
        using var listed = await ListedStash.Open();
        var all = await Listed(listed, Scope.All, "first picture", "second picture");
        var typedFor = all[0];
        OpenOn(listed, typedFor);
        listed.Listing.Focus(typedFor.Id);

        using (TestStash.Hold(typedFor.FilePath))
        {
            // Held past the store's retries, the rename is refused (E3, G35).
            await listed.PreviewStepClicks.Click(StepWay.On, Typed(() => listed.Renaming.Rename(listed.PreviewItem!, "renamed")));
        }

        var named = listed.Notices.Shown.Single(notice => notice.NamesAProblem);
        StringAssert.Contains(named.Text, typedFor.Name, "the refusal names the item the name was typed for");
        Assert.AreEqual(typedFor.Id, listed.PreviewItem?.Id, "the preview stays on the item the refusal names");
        CollectionAssert.AreEqual(new[] { typedFor.Id }, listed.Listing.Selection.ToList());
    }

    [TestMethod]
    public async Task ANameTypedAgainWhileTheFirstCommitIsAnsweredIsCommittedTooBeforeTheMove()
    {
        using var listed = await ListedStash.Open();
        var all = await Listed(listed, Scope.All, "first", "second");
        OpenOn(listed, all[0]);

        // The name committed, then typed again in the name while the store answered.
        await listed.PreviewStepClicks.Click(StepWay.On, Typed(
            () => listed.Renaming.Rename(listed.PreviewItem!, "renamed"),
            () => listed.Renaming.Rename(listed.PreviewItem!, "corrected")));

        Assert.AreEqual("corrected", (await listed.Stash.All()).Single(item => item.Id == all[0].Id).Name, "nothing typed is dropped by the move");
        Assert.AreEqual(all[1].Id, listed.PreviewItem?.Id);
    }

    [TestMethod]
    public async Task TwoClicksInARowRunInOrderEachFromTheItemShownWhenItsTurnComes()
    {
        using var listed = await ListedStash.Open(storeRetry: TimeSpan.FromSeconds(30));
        var all = await Listed(listed, Scope.All, "one", "two", "three", "four");
        OpenOn(listed, all[0]);

        Task first, second;
        using (TestStash.Hold(all[0].FilePath))
        {
            first = listed.PreviewStepClicks.Click(StepWay.On, Typed(() => listed.Renaming.Rename(listed.PreviewItem!, "renamed")));
            second = listed.PreviewStepClicks.Click(StepWay.On, () => null);
            Assert.IsFalse(first.IsCompleted, "the rename is still held");
            Assert.IsFalse(second.IsCompleted, "the second click waits for the first's turn");
            Assert.AreEqual(all[0].Id, listed.PreviewItem?.Id, "nothing moves before the commit has answered");
        }

        await Task.WhenAll(first, second);
        Assert.AreEqual(all[2].Id, listed.PreviewItem?.Id, "two steps on, the second from the item the first showed");
        CollectionAssert.AreEqual(new[] { all[2].Id }, listed.Listing.Selection.ToList());
        Assert.AreEqual("renamed", (await listed.Stash.All()).Single(item => item.Id == all[0].Id).Name);
    }

    [TestMethod]
    public async Task AMoveWaitingWhenThePreviewIsClosedIsDroppedAndTheCommitStands()
    {
        using var listed = await ListedStash.Open(storeRetry: TimeSpan.FromSeconds(30));
        var all = await Listed(listed, Scope.All, "one", "two");
        OpenOn(listed, all[0]);
        listed.Listing.Focus(all[0].Id);

        Task click;
        using (TestStash.Hold(all[0].FilePath))
        {
            click = listed.PreviewStepClicks.Click(StepWay.On, Typed(() => listed.Renaming.Rename(listed.PreviewItem!, "renamed")));
            listed.PreviewOpen = false;
        }

        await click;
        Assert.AreEqual(all[0].Id, listed.PreviewItem?.Id, "no move once closed");
        CollectionAssert.AreEqual(new[] { all[0].Id }, listed.Listing.Selection.ToList(), "the grid keeps the item the preview closed on");
        Assert.AreEqual("renamed", (await listed.Stash.All()).Single(item => item.Id == all[0].Id).Name, "the commit stands");
    }

    [TestMethod]
    public async Task AMoveWaitingWhenACloseIsAskedIsDroppedSoTheCloseFollowsTheCommitOnTheItem()
    {
        using var listed = await ListedStash.Open(storeRetry: TimeSpan.FromSeconds(30));
        var all = await Listed(listed, Scope.All, "one", "two");
        OpenOn(listed, all[0]);
        listed.Listing.Focus(all[0].Id);

        Task click;
        using (TestStash.Hold(all[0].FilePath))
        {
            click = listed.PreviewStepClicks.Click(StepWay.On, Typed(() => listed.Renaming.Rename(listed.PreviewItem!, "renamed")));
            // Esc while the commit is in flight: the preview closes once it answers, whichever resumes first.
            listed.PreviewClosing = true;
        }

        await click;
        Assert.AreEqual(all[0].Id, listed.PreviewItem?.Id, "the click moves nothing while the close waits");
        CollectionAssert.AreEqual(new[] { all[0].Id }, listed.Listing.Selection.ToList());
        Assert.AreEqual("renamed", (await listed.Stash.All()).Single(item => item.Id == all[0].Id).Name, "the commit stands");
    }

    [TestMethod]
    public async Task AMoveWaitingWhenAnEditOpensOnTheItemIsDroppedAndTheEditIsOfThatItem()
    {
        using var listed = await ListedStash.Open(storeRetry: TimeSpan.FromSeconds(30));
        // Real pictures, so the edit can open.
        foreach (var fixture in new[] { "photo.jpg", "transparent.png" })
        {
            await listed.Stash.ImportFixture(Path.GetFileNameWithoutExtension(fixture), fixture);
        }

        await listed.Listing.ShowScope(Scope.All);
        var all = listed.Listing.Items;
        OpenOn(listed, all[0]);

        Task click;
        Task<Outcome> editing;
        using (TestStash.Hold(all[0].FilePath))
        {
            click = listed.PreviewStepClicks.Click(StepWay.On, Typed(() => listed.Renaming.Rename(listed.PreviewItem!, "renamed")));
            // Ctrl+E in the preview meanwhile: the edit opens on the item shown.
            editing = listed.Editing.OpenOnSelection(listed.PreviewItem);
        }

        await click;
        Assert.IsTrue((await editing).Succeeded, (await editing).Failure?.Sentence);
        Assert.AreEqual(all[0].Id, listed.Editing.Session?.Item.Id, "the edit is of the item shown at Ctrl+E");
        Assert.AreEqual(all[0].Id, listed.PreviewItem?.Id, "the preview beneath stays on it");
        listed.Editing.Cancel();
    }

    [TestMethod]
    public async Task EachButtonIsAvailableExactlyWhereItsStepShowsAnItem()
    {
        using var listed = await ListedStash.Open();
        var all = await Listed(listed, Scope.All, "one", "two", "three");
        var clicks = listed.PreviewStepClicks;
        Assert.IsFalse(clicks.Available(StepWay.Back) || clicks.Available(StepWay.On), "neither while the preview is closed");

        OpenOn(listed, all[0]);
        Assert.IsFalse(clicks.Available(StepWay.Back), "‹ at the list's first item");
        Assert.IsTrue(clicks.Available(StepWay.On));
        OpenOn(listed, all[1]);
        Assert.IsTrue(clicks.Available(StepWay.Back) && clicks.Available(StepWay.On), "both in the middle");
        OpenOn(listed, all[2]);
        Assert.IsTrue(clicks.Available(StepWay.Back));
        Assert.IsFalse(clicks.Available(StepWay.On), "› at its last");

        await listed.Listing.SetQuery("two");
        OpenOn(listed, listed.Listing.Items.Single());
        Assert.IsFalse(clicks.Available(StepWay.Back) || clicks.Available(StepWay.On), "neither at the list's only item");
    }

    [TestMethod]
    public async Task ForAFavoriteWhoseStarWasClickedOffInFavoritesTheStepsRuleDecidesWhichButtonIsAvailable()
    {
        using var listed = await ListedStash.Open();
        foreach (var name in new[] { "one", "two" })
        {
            var item = await listed.Stash.Import(name);
            Assert.IsTrue((await listed.Stash.Store!.SetFavorite([item], true)).All(outcome => outcome.Succeeded));
        }

        await listed.Listing.ShowScope(Scope.Favorites);
        var favorites = listed.Listing.Items;
        OpenOn(listed, favorites[0]);
        Assert.IsTrue((await listed.Organising.ToggleFavorite([favorites[0]])).Succeeded);
        await listed.Listing.Settle();
        Assert.IsNull(listed.Listing.PlaceOf(favorites[0].Id), "no longer a favorite, it has left Favorites");

        Assert.IsTrue(listed.PreviewStepClicks.Available(StepWay.On), "› stays live, to the favorite now first");
        Assert.IsFalse(listed.PreviewStepClicks.Available(StepWay.Back));
        await Clicked(listed, StepWay.On, favorites[1]);
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

    /// <summary>
    /// What the preview's fields hold typed, as the click's act asks for it: each commit in turn, started on the ask as the
    /// field's Enter starts it, the field holding nothing more once they are all asked.
    /// </summary>
    private static Func<Task<Outcome>?> Typed(params Func<Task<Outcome>>[] commits)
    {
        var asked = 0;
        return () => asked < commits.Length ? commits[asked++]() : null;
    }

    private static List<string> Names(ListedStash listed) => listed.Listing.Items.Select(item => item.Name).ToList();

    /// <summary>The preview told open on <paramref name="item"/>, as the check's starting point.</summary>
    private static void OpenOn(ListedStash listed, StashItem item)
    {
        listed.PreviewOpen = true;
        listed.PreviewItem = item;
    }

    /// <summary>A click stepping <paramref name="way"/> with nothing typed shows <paramref name="expected"/>, selected alone.</summary>
    private static async Task Clicked(ListedStash listed, StepWay way, StashItem expected)
    {
        await listed.PreviewStepClicks.Click(way, () => null);
        Assert.AreEqual(expected.Id, listed.PreviewItem?.Id, $"{way} shows {expected.Name}");
        CollectionAssert.AreEqual(new[] { expected.Id }, listed.Listing.Selection.ToList(), $"{way}: the item shown is the grid's one selected item (G8)");
        Assert.AreEqual(expected.Id, listed.Listing.Focused);
    }
}
