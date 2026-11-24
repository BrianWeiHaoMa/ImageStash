using ImageStash.Store;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

[TestClass]
public sealed class BrowsingStashTests
{
    [TestMethod]
    public async Task CopyBelowTheFirstRowsKeepsTheLayoutUntilSummonEvenWithACaptureRefresh()
    {
        using var listed = await ListedStash.Open(SetClock.Ahead);
        var stash = listed.Stash;
        var store = stash.Store!;
        var start = stash.Clock.Now;
        for (var i = 0; i < 24; i++)
        {
            var picture = await stash.ImportPicture($"Picture {i}", "picture.png", MediaFixtures.DistinctPng(i + 1, 70 + i % 4 * 20, 80));
            Assert.IsTrue((await store.RecordUses([picture], start.AddSeconds(i))).Succeeded);
        }

        var used = (await store.Recent()).Value!;
        Assert.IsTrue((await store.AddTag([used[0]], "cat dog")).Single().Succeeded);
        Assert.IsTrue((await store.AddTag([used[1]], "cat")).Single().Succeeded);
        var unused = await stash.Import("Unused import");
        Assert.IsTrue((await store.AddTag([unused], "spare")).Single().Succeeded);
        await listed.Listing.ShowScope(Scope.Recent);
        var rows = Rows(listed.Listing);
        Assert.IsGreaterThan(4, rows.Count, "several rows, with uneven tile widths");
        var selected = listed.Listing.Items.Single(item => item.Id == rows[4].Tiles[0].Id);
        listed.Listing.Focus(selected.Id);
        stash.Clock.Now = start.AddMinutes(1);

        Assert.IsTrue((await listed.Handoff.Copy(null!, listed.Listing.SelectedItems)).Succeeded);
        Assert.AreEqual(selected.Id, (await store.Recent()).Value![0].Id, "the store has the latest use");
        CollectionAssert.AreEqual(rows.SelectMany(row => row.Tiles).ToArray(), Rows(listed.Listing).SelectMany(row => row.Tiles).ToArray(), "the displayed pictures keep their places");
        Assert.AreEqual(selected.Id, listed.Listing.Focused);

        stash.Clock.Now += TimeSpan.FromSeconds(1);
        var capture = await stash.Capture("New capture");
        Task summon;
        Task refresh;
        using (stash.HoldStore())
        {
            summon = listed.Summon(pinned: true);
            Assert.IsFalse(listed.Listing.Settled, "the summon read has not settled");
            refresh = listed.Listing.Refresh();
        }

        await Task.WhenAll(summon, refresh);
        CollectionAssert.AreEqual(new[] { capture.Id, selected.Id }, listed.Listing.Items.Take(2).Select(item => item.Id).ToArray(), "the capture refresh takes the order requested at summon");
        Assert.AreEqual(selected.Id, listed.Listing.Focused, "selection is preserved by identity");

        Assert.IsTrue((await listed.Organising.AddTag([used[1], used[2]], "dog")).Succeeded);
        CollectionAssert.AreEqual(new[] { "cat", "dog", "spare" }, listed.Listing.Chips.Select(count => count.Tag).ToArray(), "the chip places hold");
        CollectionAssert.AreEqual(new[] { "dog", "cat", "spare" }, listed.Listing.Tags.Select(count => count.Tag).ToArray(), "Every tag reflects the current counts");
        Assert.IsTrue((await listed.Organising.RemoveTag([unused], "spare")).Succeeded);
        Assert.AreEqual(new TagCount("spare", 0), listed.Listing.Chips[2]);
        CollectionAssert.AreEqual(new[] { "dog", "cat" }, listed.Listing.Tags.Select(count => count.Tag).ToArray(), "the dead place is absent from the picker");
    }

    [TestMethod]
    public async Task ARefreshUpdatesThePicturesInPlaceAndAppendsACaptureBeforeItsFirstUse()
    {
        using var listed = await ListedStash.Open(SetClock.Ahead);
        var stash = listed.Stash;
        var store = stash.Store!;
        var older = await stash.Import("older");
        var newer = await stash.Import("newer");
        await store.RecordUses([older], stash.Clock.Now);
        await store.RecordUses([newer], stash.Clock.Now.AddSeconds(1));
        await listed.Listing.ShowScope(Scope.Recent);
        listed.Listing.Focus(older.Id);

        stash.Clock.Now += TimeSpan.FromSeconds(2);
        var capture = await stash.Capture("first use still to come");
        Assert.IsTrue((await store.Rename(older, "renamed")).Succeeded);
        await listed.Listing.Refresh();

        CollectionAssert.AreEqual(new[] { newer.Id, older.Id, capture.Id }, listed.Listing.Items.Select(item => item.Id).ToArray());
        Assert.AreEqual("renamed", listed.Listing.Items[1].Name);
        Assert.AreEqual(older.Id, listed.Listing.Focused);
        await store.Remove([newer.Id]);
        await listed.Listing.Refresh();
        CollectionAssert.AreEqual(new[] { older.Id, capture.Id }, listed.Listing.Items.Select(item => item.Id).ToArray());
    }

    [TestMethod]
    public async Task SummoningRecentLeavesAnIntentionallyEmptySelectionEmpty()
    {
        using var listed = await ListedStash.Open();
        var item = await listed.Stash.Capture("Recent picture");
        await listed.Listing.ShowScope(Scope.Recent);
        listed.Listing.Toggle(item.Id);
        Assert.IsEmpty(listed.Listing.Selection);

        await listed.Summon(pinned: false);

        Assert.IsEmpty(listed.Listing.Selection);
        Assert.IsNull(listed.Listing.Focused);
    }

    [TestMethod]
    public async Task CapturesDiscoveredByOneRefreshJoinRecentInArrivalOrder()
    {
        using var listed = await ListedStash.Open(SetClock.Ahead);
        var stash = listed.Stash;
        var existing = await stash.Capture("existing");
        await listed.Listing.ShowScope(Scope.Recent);
        stash.Clock.Now += TimeSpan.FromSeconds(1);
        var first = await stash.Capture("first");
        stash.Clock.Now += TimeSpan.FromSeconds(1);
        var second = await stash.Capture("second", "transparent.png");

        await listed.Listing.Refresh();

        CollectionAssert.AreEqual(new[] { existing.Id, first.Id, second.Id }, listed.Listing.Items.Select(item => item.Id).ToArray());
    }

    [TestMethod]
    public async Task ARefreshCanFinishTheRecentReorderAfterASummonReadWasRefused()
    {
        using var listed = await ListedStash.Open(SetClock.Ahead);
        var stash = listed.Stash;
        var store = stash.Store!;
        var older = await stash.Capture("older");
        stash.Clock.Now += TimeSpan.FromSeconds(1);
        var newer = await stash.Capture("newer", "transparent.png");
        await listed.Listing.ShowScope(Scope.Recent);
        stash.Clock.Now += TimeSpan.FromSeconds(1);
        Assert.IsTrue((await store.RecordUses([older], stash.Clock.Now)).Succeeded);
        using (stash.HoldStore())
        {
            await listed.Summon(pinned: true);
            Assert.AreEqual(newer.Id, listed.Listing.Items[0].Id, "the failed read leaves the displayed order");
        }

        await listed.Listing.Refresh();

        Assert.AreEqual(older.Id, listed.Listing.Items[0].Id, "the first good read applies the order requested by the summon");
        Assert.AreEqual(newer.Id, listed.Listing.Focused);
    }

    [TestMethod]
    public async Task RecentRemembersWhichPictureJoinedFirstWhileASearchCoveredIt()
    {
        using var listed = await ListedStash.Open(SetClock.Ahead);
        var stash = listed.Stash;
        var store = stash.Store!;
        var used = await stash.ImportFixture("used", "still.png");
        var first = await stash.ImportFixture("match first", "transparent.png");
        var second = await stash.ImportFixture("match second", "animated.gif");
        Assert.IsTrue((await store.RecordUses([used], stash.Clock.Now)).Succeeded);
        await listed.Listing.ShowScope(Scope.Recent);
        await listed.Listing.SetQuery("match");

        foreach (var item in new[] { first, second })
        {
            stash.Clock.Now += TimeSpan.FromSeconds(1);
            listed.Listing.Focus(item.Id);
            Assert.IsTrue((await listed.Handoff.Copy(null!, listed.Listing.SelectedItems)).Succeeded);
        }

        await listed.Listing.SetQuery("");
        CollectionAssert.AreEqual(new[] { used.Id, first.Id, second.Id }, listed.Listing.Items.Select(item => item.Id).ToArray());
        await listed.Summon(pinned: true);
        CollectionAssert.AreEqual(new[] { second.Id, first.Id, used.Id }, listed.Listing.Items.Select(item => item.Id).ToArray());
    }

    private static IReadOnlyList<GridRow> Rows(Listing listing) => JustifiedRows.Layout(listing.Items, 460, 100);
}
