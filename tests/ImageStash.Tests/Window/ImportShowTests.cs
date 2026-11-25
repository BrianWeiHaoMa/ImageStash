using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.LogicalTree;
using ImageStash.Store;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// The import notice's Show, offered when its end kept what the person left (G12): it clears the search and shows All
/// with the import's items selected, the ones the stash still holds, closing a preview that covers the grid and leaving
/// an open edit open (G26); it acts for as long as the notice shows (G16), is refused by name while a Move waits (G36,
/// G35), and selects nothing of a stash an import has replaced, whose items take the same identities. Its selection is an
/// import's, which a summon of the hidden flyout cuts to the first (G1). Its button takes no focus, as the Undo's (G7).
/// </summary>
[TestClass]
public sealed class ImportShowTests
{
    [TestMethod]
    public async Task ShowClearsTheSearchShowsAllWithTheNewItemsSelectedClosesThePreviewAndASummonKeepsTheFirst()
    {
        using var listed = await ListedStash.Open();
        var older = await listed.Stash.ImportFixture("older", "still.png");
        await listed.Listing.ShowScope(Scope.All);
        var (one, two) = (new HeldBackFile(MediaFixtures.Path("transparent.png")), new HeldBackFile(MediaFixtures.Path("photo.jpg")));
        var running = listed.Import.Drop(new([one.Item, two.Item], false, false, []));
        await listed.Press(Key.Space, KeyModifiers.None, KeyPlace.Grid);
        await listed.Listing.SetQuery("old");
        one.Release();
        two.Release();
        var added = (await running).Select(file => file.Item!.Id).ToList();
        Assert.IsTrue(listed.PreviewOpen, "kept at the end");
        Assert.AreEqual("old", listed.Listing.Query);

        await Show(listed);

        Assert.IsFalse(listed.PreviewOpen, "Show closes the preview that covers the grid");
        ImportEndTests.AssertShown(listed, added, "Show shows All that way (G12)");
        listed.WindowShowing.Seen(visible: false, minimised: false);
        listed.WindowShowing.Seen(visible: true, minimised: false);
        await listed.Summon();
        CollectionAssert.AreEqual(new[] { listed.Listing.Items[0].Id }, listed.Listing.Selection.ToList(), "its selection is an import's: the summon keeps the first alone (G1)");
        Assert.AreNotEqual(older.Id, listed.Listing.Items[0].Id);
        Assert.IsEmpty(listed.Failures);
    }

    [TestMethod]
    public async Task ANameCommittedAsShowClosesThePreviewIsKeptAndShowStillSelectsTheNewItems()
    {
        using var listed = await ListedStash.Open();
        var older = await listed.Stash.ImportFixture("older", "still.png");
        await listed.Listing.ShowScope(Scope.All);
        var (one, two) = (new HeldBackFile(MediaFixtures.Path("transparent.png")), new HeldBackFile(MediaFixtures.Path("photo.jpg")));
        var running = listed.Import.Drop(new([one.Item, two.Item], false, false, []));
        await listed.Press(Key.Space, KeyModifiers.None, KeyPlace.Grid);
        one.Release();
        two.Release();
        var added = (await running).Select(file => file.Item!.Id).ToList();
        Assert.IsTrue(listed.PreviewOpen, "kept at the end");
        // The preview commits the name as it closes (G19), including the refresh after the rename.
        Task<Outcome>? commit = null;
        listed.Import.FinishPreview = () => commit = listed.Renaming.Rename(older, "typed name");

        await Show(listed);

        Assert.IsTrue(commit!.IsCompletedSuccessfully, "Show waited for the preview's close");
        Assert.IsTrue((await commit).Succeeded);
        Assert.AreEqual("typed name", (await listed.Stash.Store!.Item(older.Id)).Value!.Name, "the name typed is kept (G19)");
        ImportEndTests.AssertShown(listed, added, "the commit's refresh leaves Show's result as Show shows it (G12)");
        Assert.IsEmpty(listed.Failures);
    }

    [TestMethod]
    public async Task AFailedShowReadKeepsTheUsableSearchAndCanBeRetried()
    {
        using var listed = await ListedStash.Open(storeRetry: TimeSpan.Zero);
        var older = await listed.Stash.ImportFixture("older", "still.png");
        var arrival = new HeldBackFile(MediaFixtures.Path("transparent.png"));
        var running = listed.Import.Drop(arrival.Drop);
        await listed.Listing.SetQuery("older");
        arrival.Release();
        var added = (await running).Single().Item!;

        using (listed.Stash.HoldStore())
        {
            await Show(listed);
        }

        Assert.AreEqual("older", listed.Listing.Query);
        Assert.AreEqual(older.Id, listed.Listing.SelectedItems.Single().Id);
        Assert.IsNotEmpty(listed.Failures, "the failed read is named");
        await Show(listed);
        ImportEndTests.AssertShown(listed, [added.Id], "a later Show uses the released store");
    }

    [TestMethod]
    public async Task AFailedShowDoesNotCountAsMovingOnDuringALaterBatch()
    {
        using var listed = await ListedStash.Open(storeRetry: TimeSpan.Zero);
        await listed.Stash.ImportFixture("older", "still.png");
        var first = new HeldBackFile(MediaFixtures.Path("transparent.png"));
        var firstRunning = listed.Import.Drop(first.Drop);
        await listed.Listing.SetQuery("older");
        first.Release();
        await firstRunning;

        var later = new HeldBackFile(MediaFixtures.Path("photo.jpg"));
        var laterRunning = listed.Import.Drop(later.Drop);
        using (listed.Stash.HoldStore())
        {
            await Show(listed);
        }

        later.Release();
        var added = (await laterRunning).Single().Item!;
        ImportEndTests.AssertShown(listed, [added.Id], "the failed Show left the view alone, so the later batch shows its result");
    }

    [TestMethod]
    public async Task ShowLeavesOutAnItemDeletedSinceActsPastTheNoticesTimeAndNothingOnceItsGone()
    {
        using var listed = await ListedStash.Open();
        await listed.Stash.ImportFixture("older", "still.png");
        await listed.Listing.ShowScope(Scope.All);
        var (one, two) = (new HeldBackFile(MediaFixtures.Path("transparent.png")), new HeldBackFile(MediaFixtures.Path("photo.jpg")));
        var running = listed.Import.Drop(new([one.Item, two.Item, ListedStash.File(listed.Stash.Source("clip.mp4", TestStash.Bytes(1)))], false, false, []));
        await listed.Listing.SetQuery("old");
        one.Release();
        two.Release();
        var added = (await running).Where(file => file.Item is not null).Select(file => file.Item!).ToList();
        var notice = listed.Notices.Shown.Single();
        Assert.IsNotNull(notice.Button, "the search typed meanwhile kept: its notice offers Show");
        await listed.Deletion.Delete([added[0]]);
        Pass(listed, TimeSpan.FromMinutes(1));
        Assert.IsTrue(listed.Notices.Shown.Contains(notice), "naming a skipped file, it stays past its time (G16)");
        Assert.IsFalse(listed.Notices.OffersUndo(notice));

        await NoticeButton.Press(listed.Notices, notice);

        ImportEndTests.AssertShown(listed, [added[1].Id], "the ones the stash still holds (G12)");

        await listed.Deletion.Delete([added[1]]);
        await listed.Listing.SetQuery("older");
        await NoticeButton.Press(listed.Notices, notice);
        Assert.AreEqual(("", Scope.All), (listed.Listing.Query, listed.Listing.LitChip));
        Assert.IsEmpty(listed.Listing.Selection, "with none of them left, none selected, not some other picture");

        await listed.Listing.SetQuery("older");
        listed.Notices.Close(notice);
        await NoticeButton.Press(listed.Notices, notice);
        Assert.AreEqual("older", listed.Listing.Query, "a notice closed by its × shows nothing");
    }

    [TestMethod]
    public async Task AnEditOpenedWhileTheImportRunsIsNoChangeAndShowUnderAnOpenEditLeavesItOpen()
    {
        using var listed = await ListedStash.Open();
        var older = await listed.Stash.ImportFixture("older", "still.png");
        await listed.Listing.ShowScope(Scope.All);
        listed.Listing.Focus(older.Id);
        var held = new HeldBackFile(MediaFixtures.Path("transparent.png"));
        var running = listed.Import.Drop(held.Drop);
        Assert.AreEqual(KeyAction.Edit, await listed.Press(Key.E, KeyModifiers.Control, KeyPlace.Grid));
        Assert.IsTrue(listed.Editing.IsOpen);

        held.Release();
        var added = (await running).Single().Item!;

        ImportEndTests.AssertShown(listed, [added.Id], "opening an edit is not among the acts that keep the window (G12)");
        Assert.IsTrue(listed.Editing.IsOpen, "and the import's end leaves the edit open (G26)");

        var later = new HeldBackFile(MediaFixtures.Path("photo.jpg"));
        running = listed.Import.Drop(later.Drop);
        await listed.Listing.SetQuery("older");
        later.Release();
        var laterAdded = (await running).Single().Item!;
        Assert.AreEqual("older", listed.Listing.Query);

        await Show(listed);

        ImportEndTests.AssertShown(listed, [laterAdded.Id], "Show changes the list beneath the edit");
        Assert.IsTrue(listed.Editing.IsOpen, "and leaves the edit open (G20, G26)");
        Assert.AreEqual(older.Id, listed.Editing.Session?.Item.Id);
    }

    [TestMethod]
    public async Task WhileAMoveWaitsForTheNoticeShowStartsNothing()
    {
        using var listed = await ListedStash.Open();
        var older = await listed.Stash.ImportFixture("older", "still.png");
        var held = new HeldBackFile(MediaFixtures.Path("transparent.png"));
        var running = listed.Import.Drop(held.Drop);
        await listed.Listing.SetQuery("older");
        held.Release();
        await running;
        var notice = listed.Notices.Shown.Single();
        var chosen = MovingStash.Chosen(listed.Stash);
        listed.Dialogs.PickFolderAnswer = () => Task.FromResult<string?>(chosen);

        var moving = listed.Moving.MoveTo(null!);
        await Handing.Eventually(() => listed.Moving.Busy == "Waiting for the Undo of a notice before moving your stash…");
        await listed.Turns.Act(() => NoticeButton.Press(listed.Notices, notice));

        Assert.AreEqual("older", listed.Listing.Query, "a Move asked: Show starts nothing (G36)");
        CollectionAssert.AreEqual(new[] { older.Id }, listed.Listing.Selection.ToList());
        var refused = new Failure(ImportEnd.Show, ImportEnd.ShownSubject, MoveReasons.InProgress);
        Assert.IsTrue(listed.Notices.Shown.Any(shown => shown.Text == refused.Sentence), "and is refused by name (G35)");
        await listed.Turns.Act(() =>
        {
            listed.Notices.Close(notice);
            return Task.CompletedTask;
        });
        var moved = await moving.WaitAsync(TimeSpan.FromSeconds(20));
        Assert.IsTrue(moved.Succeeded, moved.Failure?.Sentence);
    }

    [TestMethod]
    public async Task AfterImportStashReplacedTheStashShowSelectsNoneOfTheItemsThatTookItsItemsIdentities()
    {
        using var exporting = await ListedStash.Open();
        foreach (var name in new[] { "a", "b", "c", "d" })
        {
            await exporting.Stash.Import(name);
        }

        var archive = exporting.Stash.Outside("theirs.zip");
        exporting.Dialogs.SaveAnswer = () => Task.FromResult<string?>(archive);
        Assert.IsTrue((await exporting.Page().Export(null!)).Succeeded);

        using var listed = await ListedStash.Open();
        var older = await listed.Stash.ImportFixture("older", "still.png");
        await listed.Stash.ImportFixture("newer", "animated.gif");
        await listed.Listing.ShowScope(Scope.All);
        var held = new HeldBackFile(MediaFixtures.Path("transparent.png"));
        var running = listed.Import.Drop(new([held.Item, ListedStash.File(listed.Stash.Source("clip.mp4", TestStash.Bytes(1)))], false, false, []));
        listed.Listing.Focus(older.Id);
        held.Release();
        var added = (await running).First(file => file.Item is not null).Item!;
        var notice = listed.Notices.Shown.Single();
        Pass(listed, TimeSpan.FromMinutes(1));

        var imported = await listed.ImportStash(archive);
        Assert.IsTrue(imported.Succeeded, imported.Failure?.Sentence);
        Assert.IsTrue(listed.Listing.Items.Any(item => item.Id == added.Id), "an item of the imported stash took the added item's identity");
        Assert.IsTrue(listed.Notices.Shown.Contains(notice), "the notice naming a skipped file stays");

        await NoticeButton.Press(listed.Notices, notice);

        Assert.AreEqual(("", Scope.All), (listed.Listing.Query, listed.Listing.LitChip), "All with no search");
        Assert.IsNotEmpty(listed.Listing.Items, "the imported stash listed");
        Assert.IsEmpty(listed.Listing.Selection, "nothing of the new stash selected, by the old identity or otherwise (G12)");
    }

    [TestMethod]
    public async Task UndoAndCtrlZStayAsTheyAreBesideShow()
    {
        using var listed = await ListedStash.Open();
        var older = await listed.Stash.ImportFixture("older", "still.png");
        await listed.Stash.ImportFixture("newer", "animated.gif");
        await listed.Listing.ShowScope(Scope.All);
        var held = new HeldBackFile(MediaFixtures.Path("transparent.png"));
        var running = listed.Import.Drop(held.Drop);
        listed.Listing.Focus(older.Id);
        held.Release();
        var added = (await running).Single().Item!;
        Assert.IsNotNull(listed.Notices.Shown.Single().Button);

        Assert.AreEqual(KeyAction.UndoNotice, await listed.Press(Key.Z, KeyModifiers.Control, KeyPlace.Grid));
        listed.Keys.Released(Key.Z);

        Assert.IsFalse((await listed.Stash.All()).Any(item => item.Id == added.Id), "Ctrl+Z runs the import's Undo (G7, G12)");
        Assert.IsEmpty(listed.Notices.Shown, "which takes the notice, Show with it");
    }

    [TestMethod]
    public async Task TheCardShowsShowBeforeUndoNeitherTakingFocusAndANoticeWithoutOneHasNone()
    {
        using var listed = await ListedStash.Open();
        var older = await listed.Stash.ImportFixture("older", "still.png");
        await listed.Stash.ImportFixture("newer", "animated.gif");
        await listed.Listing.ShowScope(Scope.All);
        var held = new HeldBackFile(MediaFixtures.Path("transparent.png"));
        var running = listed.Import.Drop(held.Drop);
        listed.Listing.Focus(older.Id);
        held.Release();
        await running;
        var kept = listed.Notices.Shown.Single();
        await listed.Import.Drop(ListedStash.Contents([MediaFixtures.Path("photo.jpg")]));
        var shown = listed.Notices.Shown.Single(notice => notice != kept);

        ControlThread.Run(() =>
        {
            var buttons = Buttons(NoticeCard.For(kept, listed.Notices));
            CollectionAssert.AreEqual(new[] { ImportEnd.Show, "Undo" }, buttons.Select(button => button.Content as string).OfType<string>().ToList(), "Show beside the Undo, before it");
            Assert.IsTrue(buttons.All(button => !button.Focusable), "no button takes focus, the × included (G7)");

            var plain = Buttons(NoticeCard.For(shown, listed.Notices));
            CollectionAssert.AreEqual(new[] { "Undo" }, plain.Select(button => button.Content as string).OfType<string>().ToList(), "an end that showed its result offers no Show");
        });
    }

    private static List<Button> Buttons(Control card) => card.GetLogicalDescendants().OfType<Button>().ToList();

    /// <summary>A press on the newest notice's Show, as its button's click presses it.</summary>
    private static Task Show(ListedStash listed) => NoticeButton.Press(listed.Notices, listed.Notices.Shown.Last(notice => notice.Button is not null));

    /// <summary>Time passing, the sweep run as the window's timer runs it.</summary>
    private static void Pass(ListedStash listed, TimeSpan time)
    {
        listed.Stash.Clock.Now += time;
        listed.Notices.Sweep();
    }
}
