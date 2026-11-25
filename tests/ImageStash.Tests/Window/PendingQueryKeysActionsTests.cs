using Avalonia.Input;
using ImageStash.Store;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

public sealed partial class PendingQueryKeysTests
{
    [TestMethod]
    [DataRow(Key.Down, KeyModifiers.None)]
    [DataRow(Key.PageDown, KeyModifiers.None)]
    [DataRow(Key.Down, KeyModifiers.Shift)]
    public async Task ANewQuerySupersedesPendingMovementAndDelete(Key move, KeyModifiers modifiers)
    {
        using var listed = await Listed();
        await DogAndTwoCats(listed);
        var listing = listed.Listing;
        var before = listing.Items.ToArray();

        Task<KeyAction> drained;
        Task dog;
        Task<Outcome> deleted;
        using (listed.Stash.HoldStore())
        {
            _ = listing.SetQuery("cat");
            drained = listed.Press(move, modifiers, KeyPlace.Grid);
            deleted = listed.DeleteSelection();
            dog = listing.SetQuery("dog");
        }

        await Task.WhenAll(drained, dog);
        Assert.AreNotEqual(KeyAction.None, await drained);
        Assert.IsTrue((await deleted).Succeeded);

        var after = (await listed.Stash.Store!.All()).Value!;
        CollectionAssert.AreEqual(before.Select(item => item.Id).ToArray(), after.Select(item => item.Id).ToArray());
        Assert.IsEmpty(listed.Stash.RecycleBin.Asked);
        Assert.AreEqual("dog", listing.SelectedItems.Single().Name);
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    public async Task AClickSupersedesDownAndDeleteWhileReadsArePending(bool changingQuery, bool clickFocused)
    {
        using var listed = await Listed();
        var matches = await FourCats(listed);
        var listing = listed.Listing;
        listing.Focus(matches[0].Id);
        var clicked = matches[clickFocused ? 0 : 2].Id;
        var keys = Keys(listed);
        var before = listing.Items.ToArray();

        Task drained, refresh;
        Task<Outcome> deleted;
        using (listed.Stash.HoldStore())
        {
            refresh = changingQuery ? listing.SetQuery("cat") : listing.Refresh();
            drained = keys.Move(Direction.Down);
            deleted = listed.DeleteSelection();
            listing.Focus(clicked);
        }

        await Task.WhenAll(drained, refresh);
        Assert.IsTrue((await deleted).Succeeded);

        var after = (await listed.Stash.Store!.All()).Value!;
        CollectionAssert.AreEqual(before.Select(item => item.Id).ToArray(), after.Select(item => item.Id).ToArray());
        Assert.IsEmpty(listed.Stash.RecycleBin.Asked);
        Assert.AreEqual(clicked, listing.Focused);
    }

    [TestMethod]
    [DataRow(Key.Down)]
    [DataRow(Key.PageDown)]
    [DataRow(Key.End)]
    public async Task MovementThenDeleteRecyclesTheSecondArrivingMatch(Key move)
    {
        using var listed = await Listed();
        var (newer, older) = await DogAndTwoCats(listed);
        Task moved, deleted;
        using (listed.Stash.HoldStore())
        {
            _ = listed.Listing.SetQuery("cat");
            moved = listed.Press(move, KeyModifiers.None, KeyPlace.Grid);
            deleted = listed.Press(Key.Delete, KeyModifiers.None, KeyPlace.Grid);
        }

        await Task.WhenAll(moved, deleted);
        CollectionAssert.AreEqual(new[] { older.FilePath }, listed.Stash.RecycleBin.Asked);
        Assert.IsFalse(File.Exists(older.FilePath));
        Assert.IsTrue(File.Exists(newer.FilePath));
        Assert.IsNull((await listed.Stash.Store!.Item(older.Id)).Value);
        Assert.IsNotNull((await listed.Stash.Store.Item(newer.Id)).Value);
    }

    [TestMethod]
    public void AQueuedEditKeepsDeleteOffTheGridUntilTheEditorCloses() =>
        ControlThread.Run(() => ControlThread.FinishOnDispatcher(async () =>
        {
            using var listed = await Listed();
            var (newer, older) = await DogAndTwoCats(listed);
            var store = listed.Stash.Store!;
            Task moved, edited, deleted, copied;
            using (listed.Stash.HoldStore())
            {
                _ = listed.Listing.SetQuery("cat");
                Assert.IsFalse(listed.Listing.Settled);
                moved = listed.Press(Key.Down, KeyModifiers.None, KeyPlace.Grid);
                edited = listed.Press(Key.E, KeyModifiers.Control, KeyPlace.Grid);
                deleted = listed.Press(Key.Delete, KeyModifiers.None, KeyPlace.Grid);
                copied = listed.Press(Key.C, KeyModifiers.Control, KeyPlace.Grid);
            }

            await Task.WhenAll(moved, edited, deleted, copied);
            Assert.IsNotNull((await store.Item(older.Id)).Value, "Del behind Edit removed the selected cat");
            Assert.IsTrue(File.Exists(older.FilePath), "Del behind Edit recycled the selected cat's finished file");
            Assert.IsEmpty(listed.Stash.RecycleBin.Asked);
            Assert.AreEqual(older.Id, listed.Editing.Session?.Item.Id);
            Assert.HasCount(1, listed.Clipboard.Written, "the queued Copy still runs");
            CollectionAssert.AreEqual(new[] { older.FilePath }, listed.Clipboard.Written.Single().Content.FilePaths.ToArray());

            Assert.IsTrue(listed.Editing.Cancel());
            await listed.Press(Key.Delete, KeyModifiers.None, KeyPlace.Grid);

            CollectionAssert.AreEqual(new[] { older.FilePath }, listed.Stash.RecycleBin.Asked);
            Assert.IsNull((await store.Item(older.Id)).Value);
            Assert.IsFalse(File.Exists(older.FilePath));
            Assert.IsTrue(File.Exists(newer.FilePath));
        }));

    [TestMethod]
    public void DeleteAfterAnUnavailableQueuedEditRecyclesTheMultiSelection() =>
        ControlThread.Run(() => ControlThread.FinishOnDispatcher(async () =>
        {
            using var listed = await Listed();
            var (newer, older) = await DogAndTwoCats(listed);
            var store = listed.Stash.Store!;
            Task selected, edited, deleted;
            using (listed.Stash.HoldStore())
            {
                _ = listed.Listing.SetQuery("cat");
                Assert.IsFalse(listed.Listing.Settled);
                selected = listed.Press(Key.Down, KeyModifiers.Shift, KeyPlace.Grid);
                edited = listed.Press(Key.E, KeyModifiers.Control, KeyPlace.Grid);
                deleted = listed.Press(Key.Delete, KeyModifiers.None, KeyPlace.Grid);
            }

            await Task.WhenAll(selected, edited, deleted);
            Assert.IsNull(listed.Editing.Session);
            CollectionAssert.AreEquivalent(new[] { newer.FilePath, older.FilePath }, listed.Stash.RecycleBin.Asked);
            Assert.AreEqual("dog", (await store.All()).Value!.Single().Name);
        }));

    [TestMethod]
    public void ASupersededOpeningDoesNotBlockDeleteAfterTheCurrentEditCloses() =>
        ControlThread.Run(() => ControlThread.FinishOnDispatcher(async () =>
        {
            using var listed = await ListedStash.Open(editRetry: TimeSpan.FromSeconds(10));
            var (newer, older) = await DogAndTwoCats(listed);
            var store = listed.Stash.Store!;
            var original = (await store.ForEdit(older.Id)).Value!.OriginalPath;
            Task earlierEdit;
            using (File.Open(original, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                listed.Listing.Focus(older.Id);
                earlierEdit = listed.Press(Key.E, KeyModifiers.Control, KeyPlace.Grid);
                // Let the earlier Edit's store read reach its held file before starting another Edit.
                await store.ForEdit(older.Id);
                Assert.IsTrue((await listed.Editing.Open(newer, EditOrigin.Grid)).Succeeded);
                Assert.AreEqual(newer.Id, listed.Editing.Session?.Item.Id);
                Assert.IsTrue(listed.Editing.Cancel());
                Assert.IsFalse(earlierEdit.IsCompleted, "the superseded edit's original is still held");

                await listed.Press(Key.Delete, KeyModifiers.None, KeyPlace.Grid);
            }

            await earlierEdit;
            CollectionAssert.AreEqual(new[] { newer.FilePath }, listed.Stash.RecycleBin.Asked);
            Assert.IsTrue(File.Exists(older.FilePath));
        }));

    [TestMethod]
    [DataRow(Key.PageDown)]
    [DataRow(Key.Down)]
    public async Task AQueuedTagFieldKeepsMovementAndDeleteOffTheGrid(Key move)
    {
        using var listed = await Listed();
        var (newer, older) = await DogAndTwoCats(listed);
        Task opened, moved, deleted;
        using (listed.Stash.HoldStore())
        {
            _ = listed.Listing.SetQuery("cat");
            Assert.IsFalse(listed.Listing.Settled);
            opened = listed.Press(Key.T, KeyModifiers.Control, KeyPlace.Grid);
            moved = listed.Press(move, KeyModifiers.None, KeyPlace.Grid);
            deleted = listed.Press(Key.Delete, KeyModifiers.None, KeyPlace.Grid);
        }

        await Task.WhenAll(opened, moved, deleted);
        Assert.AreEqual(newer.Id, listed.TagFieldItems.Single().Id);
        Assert.IsEmpty(listed.Stash.RecycleBin.Asked);
        Assert.AreEqual(newer.Id, listed.Listing.Focused);
        CollectionAssert.AreEqual(new[] { newer.Id, older.Id },
            (await listed.Stash.Store!.Search("cat")).Value!.Select(item => item.Id).ToArray());
    }

    [TestMethod]
    [DataRow(Key.Down, KeyModifiers.None)]
    [DataRow(Key.Down, KeyModifiers.Shift)]
    [DataRow(Key.D3, KeyModifiers.Control)]
    public async Task ADiscardedTagFieldMoveCannotRetargetTheWaitingDeleteAfterEscape(Key blocked, KeyModifiers modifiers)
    {
        using var listed = await Listed();
        var (newer, older) = await DogAndTwoCats(listed);
        var listing = listed.Listing;
        Assert.IsTrue((await listing.Store!.SetFavorite([older], true)).Single().Succeeded);
        await listing.ShowScope(Scope.All);

        Task scope, beforeClick, opened, moved, deleted;
        using (listed.Stash.HoldStore())
        {
            scope = listing.ShowScope(Scope.Favorites);
            Assert.IsFalse(listing.Settled);
            beforeClick = listed.Press(Key.Down, KeyModifiers.None, KeyPlace.Grid);
            listing.Focus(newer.Id);
            opened = listed.Press(Key.T, KeyModifiers.Control, KeyPlace.Grid);
            moved = listed.Press(blocked, modifiers, KeyPlace.Grid);
            listed.SelectionKeys.Then(() =>
            {
                Assert.AreEqual(newer.Id, listed.TagFieldItems.Single().Id);
                listed.TagFieldItems = [];
            });
            deleted = listed.Press(Key.Delete, KeyModifiers.None, KeyPlace.Grid);
        }

        await Task.WhenAll(scope, beforeClick, opened, moved, deleted);
        Assert.IsEmpty(listed.Stash.RecycleBin.Asked);
        Assert.AreEqual(older.Id, listing.Focused);
        Assert.IsTrue(File.Exists(newer.FilePath) && File.Exists(older.FilePath));

        await listed.Press(Key.Delete, KeyModifiers.None, KeyPlace.Grid);
        CollectionAssert.AreEqual(new[] { older.FilePath }, listed.Stash.RecycleBin.Asked);
    }

    [TestMethod]
    public async Task DeletePressedOnAnExplicitTileDuringAScopeReadCannotFollowItsReplacementSelection()
    {
        using var listed = await Listed();
        var (favorite, clicked) = await DogAndTwoCats(listed);
        var store = listed.Stash.Store!;
        await store.SetFavorite([favorite], true);
        await listed.Listing.Refresh();

        Task scopeRead, delete;
        using (listed.Stash.HoldStore())
        {
            scopeRead = listed.Listing.ShowScope(Scope.Favorites);
            listed.Listing.Focus(clicked.Id);
            delete = listed.Press(Key.Delete, KeyModifiers.None, KeyPlace.Grid);
        }

        await Task.WhenAll(scopeRead, delete);
        CollectionAssert.AreEqual(new[] { clicked.FilePath }, listed.Stash.RecycleBin.Asked);
        Assert.IsTrue(File.Exists(favorite.FilePath));
        Assert.AreEqual(favorite.Id, listed.Listing.Focused);
    }

    [TestMethod]
    public async Task AnAcceptedChipSupersedesTheEarlierDownAndDelete()
    {
        using var listed = await Listed();
        var (_, older) = await DogAndTwoCats(listed);
        var store = listed.Stash.Store!;
        await store.AddTag([older], "older");
        await listed.Listing.Refresh();

        Task moved, picked;
        Task<Outcome> deleted;
        using (listed.Stash.HoldStore())
        {
            _ = listed.Listing.SetQuery("cat");
            moved = listed.SelectionKeys.Move(Direction.Down);
            deleted = listed.DeleteSelection();
            picked = listed.Listing.ChooseTag("older");
        }

        await Task.WhenAll(moved, deleted, picked);
        Assert.AreEqual("cat older", listed.Listing.Query);
        Assert.IsEmpty(listed.Stash.RecycleBin.Asked);
        Assert.IsTrue(File.Exists(older.FilePath));
        Assert.AreEqual(older.Id, listed.Listing.SelectedItems.Single().Id);
    }

    [TestMethod]
    [DataRow(Key.Down, KeyModifiers.None)]
    [DataRow(Key.PageDown, KeyModifiers.None)]
    [DataRow(Key.Down, KeyModifiers.Shift)]
    public async Task AStashReopenedWithReusedIdsDoesNotInheritAnUnresolvedDelete(Key move, KeyModifiers modifiers)
    {
        using var listed = await Listed();
        var (old, _) = await DogAndTwoCats(listed);
        using var replacement = new TestStash();
        var newStore = await replacement.OpenCleanly();
        var newItems = new List<StashItem>();
        for (var seed = 4; seed <= 6; seed++)
        {
            newItems.Insert(0, await replacement.Import($"cat replacement {seed}", MediaFixtures.DistinctPng(seed)));
        }

        Assert.AreEqual(old.Id, newItems[0].Id, "the replacement reused an ID with different content");
        var listing = listed.Listing;
        if (modifiers == KeyModifiers.Shift)
        {
            listing.Focus(listing.Items[1].Id);
            await listed.Press(Key.Up, KeyModifiers.Shift, KeyPlace.Grid);
            Assert.HasCount(2, listing.SelectedItems);
        }

        Task<KeyAction> drained;
        Task reading;
        Task<Outcome> deleted;
        using (listed.Stash.HoldStore())
        {
            reading = listing.SetQuery("cat");
            drained = listed.Press(move, modifiers, KeyPlace.Grid);
            deleted = listed.DeleteSelection();
            await listing.Withdraw();
            listing.StashReplaced();
            await listing.StashOpened(newStore);
        }

        await Task.WhenAll(drained, reading);
        Assert.AreNotEqual(KeyAction.None, await drained);
        Assert.IsTrue((await deleted).Succeeded);

        var after = (await newStore.All()).Value!;
        CollectionAssert.AreEqual(newItems.Select(item => (item.Id, item.Name)).ToArray(),
            after.Select(item => (item.Id, item.Name)).ToArray());
        for (var index = 0; index < newItems.Count; index++)
        {
            CollectionAssert.AreEqual(MediaFixtures.DistinctPng(6 - index), File.ReadAllBytes(after[index].FilePath));
        }

        Assert.IsEmpty(replacement.RecycleBin.Asked);
        Assert.IsEmpty(listed.Stash.RecycleBin.Asked);
        if (modifiers == KeyModifiers.Shift)
        {
            await listed.Press(Key.Down, KeyModifiers.Shift, KeyPlace.Grid);
            CollectionAssert.AreEqual(newItems.Take(2).Select(item => item.Id).ToArray(),
                listing.SelectedItems.Select(item => item.Id).ToArray(), "the fresh key starts at the replacement's first tile");
        }
    }

    [TestMethod]
    [DataRow(false, true)]
    [DataRow(true, true)]
    [DataRow(true, false)]
    public async Task AFailedScopeReadCannotSupplyArrowsButAnExplicitlyClickedItemCanStillBeCopied(bool clickTile, bool move)
    {
        using var listed = await ListedStash.Open(storeRetry: TimeSpan.Zero);
        await DogAndTwoCats(listed);
        var listing = listed.Listing;
        var keys = Keys(listed);
        var clicked = listing.Items[1];

        using (listed.Stash.HoldStore())
        {
            await listing.ShowScope(Scope.Favorites);
            if (clickTile)
            {
                listing.Focus(clicked.Id);
            }

            if (move)
            {
                await keys.Move(Direction.Down);
            }

            await CopySelection(listed);
        }

        Assert.IsNotEmpty(listed.Failures);
        var written = listed.Clipboard.Written.SelectMany(write => write.Content.FilePaths).ToArray();
        CollectionAssert.AreEqual(clickTile && !move ? new[] { clicked.FilePath } : [], written);
    }

    [TestMethod]
    public async Task DownCopyDownCopyUsesBothArrivingPicturesInOrderWhileTheFirstCopyWaits()
    {
        using var listed = await Listed();
        var matches = await FourCats(listed);
        var clipboard = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        listed.Clipboard.Hold = clipboard.Task;
        Task moved, movedAgain, copied, copiedAgain;
        using (listed.Stash.HoldStore())
        {
            _ = listed.Listing.SetQuery("cat");
            moved = listed.Press(Key.Down, KeyModifiers.None, KeyPlace.Grid);
            copied = listed.Press(Key.C, KeyModifiers.Control, KeyPlace.Grid);
            movedAgain = listed.Press(Key.Down, KeyModifiers.None, KeyPlace.Grid);
            copiedAgain = listed.Press(Key.C, KeyModifiers.Control, KeyPlace.Grid);
        }

        try
        {
            await Task.WhenAll(moved, movedAgain);
            Assert.AreEqual(matches[2].Id, listed.Listing.Focused);
            Assert.IsFalse(copied.IsCompleted);
        }
        finally
        {
            clipboard.SetResult();
            await Task.WhenAll(copied, copiedAgain);
        }

        CollectionAssert.AreEqual(new[] { matches[1].FilePath, matches[2].FilePath },
            listed.Clipboard.Written.Select(write => write.Content.FilePaths.Single()).ToArray());
        var store = listed.Stash.Store!;
        Assert.AreEqual(2L, listed.Uses());
        Assert.AreEqual(1L, (await store.UseHistory(matches[1].Id)).Value!.Count);
        Assert.AreEqual(1L, (await store.UseHistory(matches[2].Id)).Value!.Count);
    }

    [TestMethod]
    [DataRow(Key.T, KeyModifiers.Control)]
    [DataRow(Key.E, KeyModifiers.Control)]
    public async Task KeysThatOpenAnOverlayAcquireTheItemReached(Key key, KeyModifiers modifiers)
    {
        using var listed = await Listed();
        var (_, older) = await DogAndTwoCats(listed);
        Task arrow, act;
        using (listed.Stash.HoldStore())
        {
            _ = listed.Listing.SetQuery("cat");
            arrow = listed.Press(Key.Down, KeyModifiers.None, KeyPlace.Grid);
            act = listed.Press(key, modifiers, KeyPlace.Grid);
        }

        await Task.WhenAll(arrow, act);
        switch (key)
        {
            case Key.T:
                await FieldLeaving.AddTags(listed.Organising, listed.TagFieldItems, "chosen");
                CollectionAssert.AreEqual(new[] { older.Id }, (await listed.Stash.Store!.Search("chosen")).Value!.Select(item => item.Id).ToArray());
                break;
            case Key.E:
                Assert.AreEqual(older.Id, listed.Editing.Session?.Item.Id);
                break;
        }
    }

    [TestMethod]
    public async Task OpeningAPreviewSupersedesDownAndDelete()
    {
        using var listed = await Listed();
        var (newer, older) = await DogAndTwoCats(listed);
        Task moved, deleted;
        using (listed.Stash.HoldStore())
        {
            _ = listed.Listing.Refresh();
            moved = listed.Press(Key.Down, KeyModifiers.None, KeyPlace.Grid);
            deleted = listed.Press(Key.Delete, KeyModifiers.None, KeyPlace.Grid);
            // Ctrl+double-click on the currently focused tile opens its preview on that same item.
            listed.PreviewOpen = true;
            listed.PreviewItem = newer;
            listed.Listing.FocusForPreview(newer.Id);
        }

        await Task.WhenAll(moved, deleted);
        Assert.AreEqual(newer.Id, listed.Listing.Focused);
        Assert.AreEqual(newer.Id, listed.PreviewItem?.Id);
        Assert.IsTrue(File.Exists(newer.FilePath) && File.Exists(older.FilePath));
        Assert.IsEmpty(listed.Stash.RecycleBin.Asked);
    }
}
