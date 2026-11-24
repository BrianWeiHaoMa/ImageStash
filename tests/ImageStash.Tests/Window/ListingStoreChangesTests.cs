using ImageStash.Store;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// The listing across the store's changes: a Move switching stores (G9, G30), a stash that did not open after it (G35),
/// and an import withdrawing the stash and replacing it (G36); nothing of the old store listed, applied or named.
/// </summary>
[TestClass]
public sealed class ListingStoreChangesTests
{
    private readonly List<Failure> failures = [];

    private Listing NewListing() => new(failures.Add);

    [TestMethod]
    public async Task ALoadInFlightWhenAMoveSwitchesStoresIsAbandonedNeitherAppliedNorThrownFromTheClosedStore()
    {
        using var before = new TestStash();
        using var after = new TestStash();
        await before.OpenCleanly();
        await after.OpenCleanly();
        await before.Import("old");
        var moved = await after.Import("moved");
        var listing = NewListing();
        await listing.StashOpened(before.Store!);

        Task loading;
        using (before.HoldStore())
        {
            loading = listing.ShowScope(Scope.All);
            await listing.StashOpened(after.Store!);
            // Closed while its read still waits on the held store, as Move closes the store it switched from (G30).
            before.Close();
        }

        await loading;
        CollectionAssert.AreEqual(new[] { moved.Id }, listing.Items.Select(item => item.Id).ToList(), "the new store's items, the abandoned load never applied");
        Assert.IsEmpty(failures, "the old store's refusal is not named: its load was abandoned");
    }

    [TestMethod]
    public async Task TheStoreAMoveOpenedTakesTheListingWithNothingListedFromTheOldOneLeftToHandOver()
    {
        using var before = new TestStash();
        using var after = new TestStash();
        await before.OpenCleanly();
        await after.OpenCleanly();
        await before.Import("old");
        var moved = await after.Import("moved");
        var listing = NewListing();
        await listing.StashOpened(before.Store!);
        await listing.ShowScope(Scope.All);
        Assert.HasCount(1, listing.SelectedItems);

        Task loading;
        using (after.HoldStore())
        {
            loading = listing.StashOpened(after.Store!);
            Assert.IsEmpty(listing.Items, "the old folder's items gone at once, before the new store has answered (G9, G30)");
            Assert.IsEmpty(listing.SelectedItems);
        }

        await loading;
        CollectionAssert.AreEqual(new[] { moved.Id }, listing.Items.Select(item => item.Id).ToList());
    }

    [TestMethod]
    public async Task AStashThatDidNotOpenAfterAMoveLeavesTheListingHoldingNoStoreAndNothingShown()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        await stash.Import("cat");
        var listing = NewListing();
        await listing.StashOpened(stash.Store!);
        var failure = new Failure("Open the stash", "new", "the store could not be read");

        listing.StashFailed(failure);

        Assert.AreEqual(StashState.Failed, listing.State);
        Assert.AreEqual(failure, listing.OpenFailure);
        Assert.IsNull(listing.Store, "parts refuse by name instead of reaching a closed store (G35)");
        Assert.IsEmpty(listing.Items);
        Assert.IsEmpty(listing.Selection);
        Assert.AreEqual("your stash could not be opened", listing.CannotStartReason);
        await listing.Settle().WaitAsync(TimeSpan.FromSeconds(5));
    }

    [TestMethod]
    public async Task AnImportsWithdrawalListsNothingRefusesEveryChangeByNameAndCompletesOnceEveryPartHasLetGo()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        stash.Tag((await stash.Import("cat")).Id, "funny");
        var listing = NewListing();
        await listing.StashOpened(stash.Store!);
        await listing.ShowScope(Scope.All);
        var lettingGo = new TaskCompletionSource();
        var asked = 0;
        listing.LetGoWhenWithdrawn(() =>
        {
            asked++;
            return lettingGo.Task;
        });

        var withdrawn = listing.Withdraw();

        Assert.AreEqual(1, asked, "every registered part told to let go");
        Assert.IsFalse(withdrawn.IsCompleted, "not before it has");
        Assert.AreEqual(StashState.Replacing, listing.State);
        Assert.IsNull(listing.Store);
        Assert.IsEmpty(listing.Items, "nothing of the stash listed (G36)");
        Assert.IsEmpty(listing.Selection);
        Assert.IsEmpty(listing.Chips);
        Assert.AreEqual(StashImport.InProgress, listing.CannotStartReason, "every change refused by name (G35)");
        Assert.AreEqual("replacing", StashStateView.Key(listing), "the window shows the import's busy state, nothing of the stash");
        lettingGo.SetResult();
        await withdrawn.WaitAsync(TimeSpan.FromSeconds(5));
        await listing.Settle().WaitAsync(TimeSpan.FromSeconds(5));
    }

    [TestMethod]
    public async Task AReplacementStartsItsOwnRecentAndTagOrderWhenPriorReadsAreStillPending()
    {
        using var before = new TestStash();
        using var after = new TestStash();
        await before.OpenCleanly();
        await after.OpenCleanly();
        var oldFirst = await before.Capture("Old first");
        var oldSecond = await before.Capture("Old second", "transparent.png");
        var newFirst = await after.Capture("New first");
        var newSecond = await after.Capture("New second", "transparent.png");
        Assert.AreEqual(oldFirst.Id, newFirst.Id);
        Assert.AreEqual(oldSecond.Id, newSecond.Id);
        before.Tag(oldFirst.Id, "dog");
        before.Tag(oldSecond.Id, "old");
        after.Tag(newFirst.Id, "new");
        after.Tag(newSecond.Id, "dog");
        await before.Store!.RecordUses([oldFirst], before.Clock.Now.AddSeconds(2));
        await after.Store!.RecordUses([newSecond], after.Clock.Now.AddSeconds(2));
        var listing = NewListing();
        await listing.StashOpened(before.Store);
        await listing.ShowScope(Scope.Recent);
        CollectionAssert.AreEqual(new[] { oldFirst.Id, oldSecond.Id }, listing.Items.Select(item => item.Id).ToArray());
        CollectionAssert.AreEqual(new[] { "dog", "old" }, listing.Chips.Select(tag => tag.Tag).ToArray());

        Task priorRead;
        using (before.HoldStore())
        {
            priorRead = listing.Refresh();
            await listing.Withdraw();
            await listing.StashOpened(after.Store);
        }

        await priorRead;
        CollectionAssert.AreEqual(new[] { "New second", "New first" }, listing.Items.Select(item => item.Name).ToArray());
        CollectionAssert.AreEqual(new[] { "dog", "new" }, listing.Chips.Select(tag => tag.Tag).ToArray());
        Assert.IsEmpty(failures);
    }

    [TestMethod]
    public async Task ReplacingTheStashDiscardsHeldSearchPlacesAndRejectsAChoiceWaitingOnTheOldStore()
    {
        using var before = new TestStash();
        using var after = new TestStash();
        await before.OpenCleanly();
        await after.OpenCleanly();
        var previous = await before.Import("picture");
        var replacement = await after.Import("picture");
        var another = await after.Import("another");
        Assert.AreEqual(previous.Id, replacement.Id);
        before.Tag(previous.Id, "cat");
        before.Tag(previous.Id, "dog");
        after.Tag(replacement.Id, "cat");
        after.Tag(replacement.Id, "dog");
        after.Tag(replacement.Id, "new");
        after.Tag(another.Id, "cat");
        var listing = NewListing();
        await listing.StashOpened(before.Store!);
        await listing.ShowScope(Scope.All);

        Task first, choice;
        using (before.HoldStore())
        {
            first = listing.ChooseTag("cat");
            Assert.IsFalse(listing.Settled);
            choice = listing.ChooseTag("dog");
            await listing.Withdraw();
            listing.StashReplaced();
            await listing.StashOpened(after.Store!);
            Assert.IsFalse(choice.IsCompleted, "the old search that would have licensed dog is still held");
        }
        await Task.WhenAll(first, choice);

        Assert.AreEqual("cat", listing.Query);
        CollectionAssert.AreEqual(new[] { "cat", "dog", "new" }, listing.Chips.Select(chip => chip.Tag).ToArray(), "the replacement ranks its own tags");
        CollectionAssert.AreEqual(new[] { another.Id, replacement.Id }, listing.Items.Select(item => item.Id).ToArray());
        CollectionAssert.AreEqual(new[] { "cat", "dog", "new" }, listing.Tags.Select(chip => chip.Tag).ToArray());
        Assert.IsEmpty(failures);
    }

    [TestMethod]
    [DataRow(false, DisplayName = "the stash listed again")]
    [DataRow(true, DisplayName = "the stash not opened again")]
    public async Task ALoadOfTheOldStoreInFlightWhenAnImportWithdrawsTheListingAppliesNothing(bool reopenFails)
    {
        using var before = new TestStash();
        using var after = new TestStash();
        await before.OpenCleanly();
        await after.OpenCleanly();
        before.Tag((await before.Import("old")).Id, "gone");
        var imported = await after.Import("imported");
        var listing = NewListing();
        await listing.StashOpened(before.Store!);

        Task loading;
        using (before.HoldStore())
        {
            loading = listing.ShowScope(Scope.All);
            await listing.Withdraw();
            if (reopenFails)
            {
                listing.StashFailed(new Failure(StashImport.FinishAction, "stash", TransferReasons.Unfinished));
            }
            else
            {
                await listing.StashOpened(after.Store!);
            }
        }

        await loading;
        CollectionAssert.AreEqual(reopenFails ? Array.Empty<long>() : [imported.Id], listing.Items.Select(item => item.Id).ToList(), "no old item came back (G35, G36)");
        Assert.IsFalse(listing.Tags.Any(tag => tag.Tag == "gone"), "nor an old tag");
        Assert.IsEmpty(failures, "the abandoned load names nothing");
    }
}
