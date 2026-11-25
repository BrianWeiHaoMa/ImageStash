using Avalonia.Input;
using ImageStash.Store;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// What an import's end shows (G12), through the window's import and listing as the window wires them, each batch held
/// back by a dropped picture whose bytes the check releases: a person who, since their act began the import, changed the
/// search, the scope or the selection, or opened the preview, finds them kept over what the stash now holds (G5) and the
/// notice offering Show; anyone else finds the search cleared and All with the new items first and selected, or, when
/// the import added none, the items its files already were in the stash as. Only the person's acts count, and from the
/// act on: an earlier import's end, a summon and a paste's return to Recent are the window's own.
/// </summary>
[TestClass]
public sealed class ImportEndTests
{
    [TestMethod]
    public async Task TwoDropsInARowWithNothingTouchedEachEndOnAllWithTheirOwnItemsSelectedASummonBetweenCountingForNothing()
    {
        using var listed = await ListedStash.Open();
        await listed.Stash.ImportFixture("older", "still.png");
        await listed.Listing.SetQuery("older");
        var first = new HeldBackFile(MediaFixtures.Path("transparent.png"));
        var second = new HeldBackFile(MediaFixtures.Path("photo.jpg"));

        var firstRunning = listed.Import.Drop(first.Drop);
        var secondRunning = listed.Import.Drop(second.Drop);
        await listed.Summon();
        first.Release();
        var firstAdded = (await firstRunning).Single().Item!;

        AssertShown(listed, [firstAdded.Id], "the first ends as if nothing was touched: its earlier search was typed before the drop");
        Assert.IsNull(listed.Notices.Shown.Single().Button, "and its notice offers no Show");

        second.Release();
        var secondAdded = (await secondRunning).Single().Item!;

        AssertShown(listed, [secondAdded.Id], "the first's end is the window's, not the person's: the second shows its own (G12)");
        Assert.IsTrue(listed.Notices.Shown.All(notice => notice.Button is null));
        Assert.IsEmpty(listed.Failures);
    }

    [TestMethod]
    public async Task ASearchTypedAfterTheSecondDropIsKeptAtBothEndsOneTypedBeforeItIsClearedAtTheSecondsEnd()
    {
        using var listed = await ListedStash.Open();
        await listed.Stash.ImportFixture("older", "still.png");
        var first = new HeldBackFile(MediaFixtures.Path("transparent.png"));
        var second = new HeldBackFile(MediaFixtures.Path("photo.jpg"));
        var firstRunning = listed.Import.Drop(first.Drop);
        var secondRunning = listed.Import.Drop(second.Drop);

        await listed.Listing.SetQuery("old");
        first.Release();
        await firstRunning;
        Assert.AreEqual("old", listed.Listing.Query, "kept at the first's end");
        second.Release();
        await secondRunning;

        Assert.AreEqual("old", listed.Listing.Query, "the second's count began at its drop, not at its turn: the search typed after it stays (G12)");
        Assert.IsTrue(listed.Notices.Shown.All(notice => notice.Button?.Label == ImportEnd.Show), "both notices offer Show");

        var third = new HeldBackFile(MediaFixtures.Path("animated.gif"));
        var fourth = new HeldBackFile(MediaFixtures.Path("animated.webp"));
        var thirdRunning = listed.Import.Drop(third.Drop);
        await listed.Listing.SetQuery("ol");
        var fourthRunning = listed.Import.Drop(fourth.Drop);
        third.Release();
        await thirdRunning;
        fourth.Release();
        var fourthAdded = (await fourthRunning).Single().Item!;

        AssertShown(listed, [fourthAdded.Id], "a search typed before the fourth drop is not a change since it began");
    }

    [TestMethod]
    [DataRow("an arrow")]
    [DataRow("a page key")]
    [DataRow("a click")]
    [DataRow("a scope chip")]
    [DataRow("a scope key")]
    [DataRow("a tag chip")]
    [DataRow("Space")]
    public async Task EachActAloneLeavesTheWindowAsThePersonLeftItAndTheNoticeOffersShow(string act)
    {
        using var listed = await ListedStash.Open();
        var older = await listed.Stash.ImportFixture("older", "still.png");
        var newer = await listed.Stash.ImportFixture("newer", "animated.gif");
        listed.Stash.Tag(older.Id, "cat");
        await listed.Organising.ToggleFavorite([older]);
        await listed.Listing.ShowScope(Scope.All);
        Assert.AreEqual(newer.Id, listed.Listing.Focused);
        var held = new HeldBackFile(MediaFixtures.Path("transparent.png"));
        var running = listed.Import.Drop(held.Drop);

        switch (act)
        {
            case "an arrow":
                // Right, as the grid's keys move the selection (PendingQueryKeys).
                listed.Listing.MoveFocus(Direction.Right, []);
                break;
            case "a page key":
                await listed.Press(Key.PageDown, KeyModifiers.None, KeyPlace.Grid);
                Assert.AreEqual(older.Id, listed.Listing.Focused);
                break;
            case "a click":
                listed.Listing.Focus(older.Id);
                break;
            case "a scope chip":
                await listed.Listing.ShowScope(Scope.Favorites);
                break;
            case "a scope key":
                await listed.Press(Key.D2, KeyModifiers.Control, KeyPlace.SearchBox);
                break;
            case "a tag chip":
                await listed.Listing.ChooseTag("cat");
                break;
            case "Space":
                Assert.AreEqual(KeyAction.TogglePreview, await listed.Press(Key.Space, KeyModifiers.None, KeyPlace.Grid));
                break;
        }

        var left = (listed.Listing.Query, listed.Listing.LitChip, listed.Listing.Focused, listed.PreviewOpen);
        var selected = listed.Listing.Selection.ToList();
        held.Release();
        var added = (await running).Single().Item!;

        Assert.AreEqual(left, (listed.Listing.Query, listed.Listing.LitChip, listed.Listing.Focused, listed.PreviewOpen), $"{act} alone keeps search, scope, focus and preview (G12)");
        CollectionAssert.AreEquivalent(selected, listed.Listing.Selection.ToList(), "and the selection");
        Assert.DoesNotContain(added.Id, listed.Listing.Selection.ToList());
        var notice = listed.Notices.Shown.Single();
        Assert.AreEqual(ImportEnd.Show, notice.Button?.Label, "its notice offers Show");
        Assert.AreEqual("Undo", notice.UndoLabel, "beside its Undo");
        Assert.IsEmpty(listed.Failures);
    }

    [TestMethod]
    public async Task TheKeptWindowListsWhatTheStashNowHoldsTheNewItemAtAllsHeadUnselected()
    {
        using var listed = await ListedStash.Open();
        var older = await listed.Stash.ImportFixture("older", "still.png");
        var newer = await listed.Stash.ImportFixture("newer", "animated.gif");
        await listed.Listing.ShowScope(Scope.All);
        var held = new HeldBackFile(MediaFixtures.Path("transparent.png"));
        var running = listed.Import.Drop(held.Drop);
        listed.Listing.Focus(older.Id);

        held.Release();
        var added = (await running).Single().Item!;

        CollectionAssert.AreEqual(new[] { added.Id, newer.Id, older.Id }, listed.Listing.Items.Select(item => item.Id).ToList(), "All lists the new item at its head (G4)");
        CollectionAssert.AreEqual(new[] { older.Id }, listed.Listing.Selection.ToList(), "the selection where the person put it");
        Assert.AreEqual(older.Id, listed.Listing.Focused);
    }

    [TestMethod]
    public async Task AnImportThatLeavesAnEmptyQueryAndAllInPlaceKeepsTheClickedRow()
    {
        using var listed = await ListedStash.Open();
        var cat = await listed.Stash.ImportFixture("one", "still.png");
        var dog = await listed.Stash.ImportFixture("two", "photo.jpg");
        listed.Stash.Tag(cat.Id, "cat");
        listed.Stash.Tag(dog.Id, "dog");
        await listed.Listing.ShowScope(Scope.All);
        await listed.Listing.SetQuery("cat");
        await listed.Listing.ChooseTag("cat");
        Assert.AreEqual("", listed.Listing.Query);
        CollectionAssert.AreEqual(new[] { "cat" }, listed.Listing.Chips.Select(chip => chip.Tag).ToArray());

        var imported = await listed.Import.Drop(ListedStash.Contents([MediaFixtures.Path("transparent.png")]));

        Assert.AreEqual(Scope.All, listed.Listing.Scope);
        Assert.AreEqual("", listed.Listing.Query);
        CollectionAssert.AreEqual(new[] { "cat" }, listed.Listing.Chips.Select(chip => chip.Tag).ToArray());
        Assert.AreEqual(imported.Single().Item!.Id, listed.Listing.Focused);
    }

    [TestMethod]
    public async Task AChipClickedBeforeTheDropDoesNotCountAsMovingOnWhileTheImportRuns()
    {
        using var listed = await ListedStash.Open();
        var carrier = await listed.Stash.ImportFixture("carrier", "still.png");
        Assert.IsTrue((await listed.Stash.Store!.AddTag([carrier], "cat dog")).Single().Succeeded);
        var listing = listed.Listing;
        await listing.ShowScope(Scope.All);
        var file = new HeldBackFile(MediaFixtures.Path("photo.jpg"));
        Task cat, dog;
        Task<IReadOnlyList<ImportedFile>> importing;
        using (listed.Stash.HoldStore())
        {
            cat = listing.ChooseTag("cat");
            dog = listing.ChooseTag("dog");
            CollectionAssert.AreEqual(new[] { "cat" }, listing.LitTags.ToArray(), "only cat is lit when the file is dropped");
            importing = listed.Import.Drop(file.Drop);
        }

        try
        {
            await Task.WhenAll(cat, dog);
            Assert.AreEqual("cat dog", listing.Query);
        }
        finally
        {
            file.Release();
        }

        var item = (await importing).Single().Item!;
        Assert.AreEqual("", listing.Query, "the import selects its result when the person did not move on after dropping");
        Assert.AreEqual(item.Id, listing.Focused);
        CollectionAssert.AreEqual(new[] { "cat" }, item.Tags.ToArray(), "the drop uses the tags lit at the drop");
    }

    [TestMethod]
    public async Task AKeptSearchListsANewItemAndAnItemJustTaggedThatMatchItTheSelectionStaying()
    {
        using var listed = await ListedStash.Open();
        var held = await listed.Stash.ImportFixture("held", "still.png");
        var cat = await listed.Stash.ImportFixture("cat one", "animated.gif");
        var other = await listed.Stash.ImportFixture("cat three", "animated.webp");
        listed.Stash.Tag(cat.Id, "cat");
        await listed.Listing.Refresh();
        await listed.Listing.ChooseTag("cat");
        Assert.AreEqual(other.Id, listed.Listing.Focused, "the newest match first");
        var arriving = new HeldBackFile(MediaFixtures.Path("transparent.png"), "cat two.png");
        var again = new HeldBackFile(MediaFixtures.Path("still.png"));
        var running = listed.Import.Drop(new([arriving.Item, again.Item], false, false, []));
        listed.Listing.Focus(cat.Id);

        arriving.Release();
        again.Release();
        var results = await running;

        var added = results.Single(file => file.Result == ImportResultKind.Added).Item!;
        Assert.AreEqual("cat", listed.Listing.Query.Trim(), "the search kept");
        CollectionAssert.AreEquivalent(new[] { added.Id, held.Id, cat.Id, other.Id }, listed.Listing.Items.Select(item => item.Id).ToList(),
            "its matches as the stash now holds them: the new picture, and the one the lit chip just tagged (G5, G11)");
        CollectionAssert.AreEqual(new[] { cat.Id }, listed.Listing.Selection.ToList(), "the selection stays");
    }

    [TestMethod]
    public async Task AnImportThatAddedNoneSelectsEachItemItsFilesAlreadyWereInTheStashAsOnceEach()
    {
        using var listed = await ListedStash.Open();
        var still = await listed.Stash.ImportFixture("still", "still.png");
        await listed.Stash.ImportFixture("between", "transparent.png");
        var gif = await listed.Stash.ImportFixture("gif", "animated.gif");
        await listed.Stash.ImportFixture("newest", "photo.jpg");
        await listed.Listing.SetQuery("newest");

        await listed.Import.Drop(ListedStash.Contents([MediaFixtures.Path("still.png"), MediaFixtures.Path("animated.gif"), MediaFixtures.Path("still.png")]));

        Assert.AreEqual("", listed.Listing.Query);
        Assert.AreEqual(Scope.All, listed.Listing.LitChip);
        CollectionAssert.AreEquivalent(new[] { still.Id, gif.Id }, listed.Listing.Selection.ToList(), "the items its files already were (G12)");
        Assert.AreEqual(gif.Id, listed.Listing.Focused, "the first of them in All's order focused, so it is brought into view");

        var some = await listed.Import.Drop(ListedStash.Contents([MediaFixtures.Path("still.png"), MediaFixtures.Path("animated.webp")]));
        var added = some.Single(file => file.Result == ImportResultKind.Added).Item!;
        CollectionAssert.AreEqual(new[] { added.Id }, listed.Listing.Selection.ToList(), "one that added any selects only what it added");

        await listed.Listing.SetQuery("newest");
        await listed.Import.Drop(ListedStash.Contents([listed.Stash.Source("clip.mp4", TestStash.Bytes(1))]));
        Assert.AreEqual(("", Scope.All), (listed.Listing.Query, listed.Listing.LitChip), "one that added and found none still ends on All, the search cleared");
    }

    [TestMethod]
    public async Task APersonWhoTouchedNothingFindsThePreviewOpenedBeforeTheDropClosedAndTheNewItemSelected()
    {
        using var listed = await ListedStash.Open();
        var older = await listed.Stash.ImportFixture("older", "still.png");
        await listed.Listing.ShowScope(Scope.All);
        await listed.Press(Key.Space, KeyModifiers.None, KeyPlace.Grid);
        Assert.IsTrue(listed.PreviewOpen);
        var held = new HeldBackFile(MediaFixtures.Path("transparent.png"));
        var running = listed.Import.Drop(held.Drop);

        held.Release();
        var added = (await running).Single().Item!;

        Assert.IsFalse(listed.PreviewOpen, "the preview closed, as an import's end closes it (G12)");
        AssertShown(listed, [added.Id], "the new item selected");
        Assert.AreNotEqual(older.Id, listed.Listing.Focused);
    }

    [TestMethod]
    public async Task AnImportEndingInAHiddenFlyoutShowsItsItemsWhichTheSummonCutsToTheFirst()
    {
        using var listed = await ListedStash.Open();
        await listed.Stash.ImportFixture("older", "still.png");
        var held = new HeldBackFile(MediaFixtures.Path("transparent.png"));
        var also = new HeldBackFile(MediaFixtures.Path("photo.jpg"));
        var running = listed.Import.Drop(new([held.Item, also.Item], false, false, []));
        listed.WindowShowing.Seen(visible: false, minimised: false);

        held.Release();
        also.Release();
        var added = (await running).Select(file => file.Item!.Id).ToList();

        AssertShown(listed, added, "a hide is no change of the person's: hidden, it ends as before (G12)");
        listed.WindowShowing.Seen(visible: true, minimised: false);
        await listed.Summon();
        CollectionAssert.AreEqual(new[] { listed.Listing.Items[0].Id }, listed.Listing.Selection.ToList(), "the summon keeps the first alone (G1)");
    }

    [TestMethod]
    public async Task APastesReturnToRecentWhileTheImportRunsIsTheWindowsOwn()
    {
        using var listed = await ListedStash.Open();
        var older = await listed.Stash.ImportFixture("older", "still.png");
        await listed.Listing.ShowScope(Scope.All);
        var held = new HeldBackFile(MediaFixtures.Path("transparent.png"));
        var running = listed.Import.Drop(held.Drop);
        var pasted = await listed.PasteIntoTarget([older]);
        Assert.IsTrue(pasted.Succeeded, pasted.Failure?.Sentence);
        Assert.AreEqual(Scope.Recent, listed.Listing.Scope, "the hidden flyout starts the next summon on Recent");

        held.Release();
        var added = (await running).Single().Item!;

        AssertShown(listed, [added.Id], "the paste's return to Recent is not the person's change (G1, G12)");
    }

    [TestMethod]
    public async Task AClickOnTheTileAlreadyAloneSelectedOrAnArrowAtTheGridsEndIsNoChange()
    {
        using var listed = await ListedStash.Open();
        var older = await listed.Stash.ImportFixture("older", "still.png");
        var newer = await listed.Stash.ImportFixture("newer", "animated.gif");
        await listed.Listing.ShowScope(Scope.All);
        Assert.AreEqual(newer.Id, listed.Listing.Focused);
        var held = new HeldBackFile(MediaFixtures.Path("transparent.png"));
        var running = listed.Import.Drop(held.Drop);

        listed.Listing.Focus(newer.Id);
        listed.Listing.MoveFocus(Direction.Left, []);
        held.Release();
        var added = (await running).Single().Item!;

        AssertShown(listed, [added.Id], "the selection never changed: the import shows its result (G12)");
        Assert.AreNotEqual(older.Id, listed.Listing.Focused);
    }

    [TestMethod]
    public async Task ANameCommittedAsTheEndClosesThePreviewIsKeptAndTheEndStillSelectsTheNewItems()
    {
        using var listed = await ListedStash.Open();
        var older = await listed.Stash.ImportFixture("older", "still.png");
        await listed.Listing.SetQuery("older");
        await listed.Press(Key.Space, KeyModifiers.None, KeyPlace.Grid);
        Assert.IsTrue(listed.PreviewOpen, "opened before the drop: no change since the import began");
        // The preview's close commits the name typed in it while the import ran, as focus leaving for the grid commits
        // it (G19): the rename and its refresh run while the end's All loads.
        Task<Outcome>? commit = null;
        listed.Import.Finishing += () => commit ??= listed.Renaming.Rename(older, "typed name");
        var (one, two) = (new HeldBackFile(MediaFixtures.Path("transparent.png")), new HeldBackFile(MediaFixtures.Path("photo.jpg")));
        var running = listed.Import.Drop(new([one.Item, two.Item], false, false, []));
        one.Release();
        two.Release();
        var added = (await running).Select(file => file.Item!.Id).ToList();

        Assert.IsTrue((await commit!).Succeeded);
        Assert.AreEqual("typed name", (await listed.Stash.Store!.Item(older.Id)).Value!.Name, "the name typed is kept (G19)");
        Assert.IsFalse(listed.PreviewOpen);
        AssertShown(listed, added, "the commit's refresh leaves the end's result as the end shows it (G12)");
        Assert.IsNull(listed.Notices.Shown.Single().Button);
        Assert.IsEmpty(listed.Failures);
    }

    /// <summary>The import's result as shown: no search, All lit, exactly <paramref name="items"/> selected, the first of them in order focused.</summary>
    internal static void AssertShown(ListedStash listed, IReadOnlyList<long> items, string because)
    {
        Assert.AreEqual("", listed.Listing.Query, because + ": the search cleared");
        Assert.AreEqual(Scope.All, listed.Listing.LitChip, because + ": All lit");
        CollectionAssert.AreEquivalent(items.ToList(), listed.Listing.Selection.ToList(), because + ": its items selected");
        Assert.AreEqual(listed.Listing.Items.First(item => items.Contains(item.Id)).Id, listed.Listing.Focused, because + ": the first of them focused");
    }
}
