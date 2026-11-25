using Avalonia.Input;
using ImageStash.Seams;
using ImageStash.Store;
using ImageStash.Tests.Support;
using ImageStash.Window;
using static ImageStash.Tests.Support.CaptionedItems;

namespace ImageStash.Tests.Window;

[TestClass]
public sealed class CaptionHandoffTests
{
    private const string CaptionQuery = "LAND #KYLIN";
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(30);

    [TestMethod]
    public async Task FromFavoritesSearchFindsBothCaptionsAndAnOrdinaryTagNarrowsTheHandoff()
    {
        using var listed = await ListedStash.Open();
        var saved = await SaveCaption(listed);
        var other = await Save(listed.Stash, await listed.Stash.ImportFixture("greeting", "photo.jpg"), Caption("Morning skyline"));
        listed.Stash.Tag(saved.Id, "team");
        var store = listed.Stash.Store!;
        Assert.IsTrue((await store.SetFavorite([saved], true)).Single().Succeeded);
        Assert.IsTrue((await store.RecordUses([other], listed.Stash.Clock.Now)).Succeeded);
        var listing = listed.Listing;
        await listing.ShowScope(Scope.Favorites);
        Assert.AreEqual(saved.Id, listing.Items.Single().Id);

        await listing.SetQuery("kylin ");

        Assert.AreEqual(Scope.All, listing.LitChip);
        CollectionAssert.AreEqual(new[] { other.Id, saved.Id }, listing.Items.Select(item => item.Id).ToArray());
        Assert.AreEqual(other.Id, listing.Selection.Single(), "the latest use puts the other caption hit first");
        CollectionAssert.AreEqual(new[] { ("team", 1) }, ListedStash.Chips(listing).ToArray());

        await listing.ChooseTag("team");

        Assert.AreEqual(saved.Id, listing.Items.Single().Id);
        Assert.AreEqual(saved.Id, listing.Selection.Single());
        CollectionAssert.AreEqual(new[] { "team" }, listing.LitTags.ToArray());
        var finished = File.ReadAllBytes(saved.FilePath);
        listed.PasteHidesFlyout = false;
        AimAtRelay(listed);
        Assert.AreEqual(KeyAction.Handoff, await PressEnter(listed).WaitAsync(Bound));
        await AssertHandedToRelay(listed, saved, finished, totalUses: 2);
        Assert.AreEqual(1, (await store.UseHistory(other.Id)).Value!.Count, "only the selected caption gained a use");

        await listing.SetQuery("kylin unfindable");
        Assert.IsTrue(listing.NoMatch);
        Assert.IsFalse(listing.IsEmptyStash);
        var view = EmptyStashShown.Over(listed);
        view.ShowListing();
        var words = view.ReadLines();
        Assert.Contains("Nothing matches “kylin unfindable”", words);
        Assert.Contains("Every word has to appear in a name, tag or saved caption. Try fewer words.", words);
        await listing.SetQuery("");
        Assert.AreEqual(Scope.Favorites, listing.LitChip);
        Assert.AreEqual(saved.Id, listing.Items.Single().Id);
    }

    [TestMethod]
    public async Task TagChipMatchesTheSavedCaptionAndTheRealTagButCountsOnlyTheTag()
    {
        using var listed = await ListedStash.Open();
        var saved = await SaveCaption(listed);
        var tagged = await listed.Stash.ImportFixture("landscape postcard", "photo.jpg");
        listed.Stash.Tag(tagged.Id, "cobalt");
        await listed.Stash.ImportFixture("cobalt portrait", "transparent.png");
        var listing = listed.Listing;

        await listing.SetQuery(CaptionQuery);
        Assert.AreEqual(saved.Id, listing.Items.Single().Id, "both the name word and the saved caption word must match (G5)");
        Assert.AreEqual(saved.Id, listing.SelectedItems.Single().Id);

        await listing.SetQuery("LAND ");
        await listing.ChooseTag("cobalt");

        CollectionAssert.AreEqual(new[] { "cobalt" }, listing.LitTags.ToArray());
        CollectionAssert.AreEqual(new[] { tagged.Id, saved.Id }, listing.Items.Select(item => item.Id).ToArray(),
            "the chip adds a search word, so the tag and the caption each match (G4, G5)");
        CollectionAssert.AreEqual(new[] { ("cobalt", 1) }, ListedStash.Chips(listing).ToArray());
        CollectionAssert.AreEqual(new[] { new TagCount("cobalt", 1) }, listing.Tags.ToArray());
        Assert.IsEmpty(listing.Items.Single(item => item.Id == saved.Id).Tags);

        Assert.AreEqual(KeyAction.MoveDown, await listed.Press(Key.Down, KeyModifiers.None, KeyPlace.SearchBox));
        Assert.AreEqual(saved.Id, listing.SelectedItems.Single().Id);
        var original = MediaFixtures.Bytes("still.png");
        var render = File.ReadAllBytes(saved.FilePath);
        CollectionAssert.AreNotEqual(original, render, "the saved render includes the caption");
        AimAtRelay(listed);

        var enter = PressEnter(listed);
        Assert.AreEqual(KeyAction.Handoff, await enter.WaitAsync(Bound));

        await AssertHandedToRelay(listed, saved, render);
    }

    [TestMethod]
    public async Task EnterAcceptedWhileAdoptionWaitsHandsOffTheSameItemAfterItsCaptionStopsMatching()
    {
        using var listed = await ListedStash.Open(storeRetry: Bound);
        var saved = await SaveCaption(listed);
        var rewrite = MediaFixtures.Bytes("photo.jpg");
        await OpenedWith.AwaitingRewrite(listed.Stash, saved, rewrite);
        await listed.Listing.SetQuery(CaptionQuery);
        Assert.AreEqual(saved.Id, listed.Listing.SelectedItems.Single().Id);
        AimAtRelay(listed);
        listed.PasteHidesFlyout = false;
        var store = listed.Stash.Store!;
        var adoptedPath = listed.Stash.Folder.InRoot("landscape.jpg");
        listed.Clipboard.AtWrite = async () =>
        {
            await Finds(store, CaptionQuery);
            var published = await store.Item(saved.Id);
            Assert.IsTrue(published.Succeeded, published.Failure?.Sentence);
            Assert.IsNotNull(published.Value);
            Assert.IsFalse(published.Value.StepsPendingWhenRead, "the adoption has published before the write");
            var written = listed.Clipboard.Written.Single().Content;
            CollectionAssert.AreEqual(new[] { adoptedPath }, written.FilePaths.ToArray());
            CollectionAssert.AreEqual(rewrite, File.ReadAllBytes(written.FilePaths.Single()), "the new file is in place at the write");
            Assert.AreEqual(new UseHistory(0, null), (await store.UseHistory(saved.Id)).Value);
        };

        using var heldRender = TestStash.Hold(saved.FilePath);
        var adopting = store.AdoptRewrite(saved.Id);
        Task<KeyAction>? enter = null;
        try
        {
            await Handing.Eventually(() => EditedItems.Row(listed.Stash, saved.Id).Steps.Count > 0);
            Assert.AreEqual(saved.FilePath, (await Finds(store, CaptionQuery, saved.Id)).Single().FilePath);
            Assert.IsFalse(File.Exists(adoptedPath), "the held outgoing render prevents placement");

            enter = PressEnter(listed);

            Assert.IsNotNull(listed.Handoff.Busy, "Enter already acquired the resolved selection and started its handoff");
            Assert.IsFalse(enter.IsCompleted, "confirmation waits for the adoption");
            Assert.IsFalse(adopting.IsCompleted, "Enter was accepted before publication");
            Assert.AreEqual(0, listed.Clipboard.Begun);
            Assert.IsEmpty(listed.Target.Pasted);
        }
        finally
        {
            heldRender.Dispose();
            await Task.WhenAll(adopting, enter ?? Task.CompletedTask).WaitAsync(Bound);
        }

        Assert.AreEqual(KeyAction.Handoff, await enter);
        var result = await adopting;
        Assert.IsTrue(result.Succeeded, result.Failure?.Sentence);
        var adopted = result.Value!;
        Assert.AreEqual((saved.Id, adoptedPath), (adopted.Id, adopted.FilePath));
        Assert.IsFalse(File.Exists(saved.FilePath));
        Assert.AreEqual(CaptionQuery, listed.Listing.Query);
        Assert.IsTrue(listed.Listing.NoMatch, "the refreshed caption query no longer selects the adopted picture");
        await AssertHandedToRelay(listed, adopted, rewrite);
    }

    [TestMethod]
    public async Task EnterQueuedForACaptionMatchCannotUseTheSameIdentityAfterAnImport()
    {
        using var listed = await ListedStash.Open();
        var saved = await SaveCaption(listed);
        var other = await listed.Stash.ImportFixture("other", "transparent.png");
        const string incomingName = "landscape skyline replacement";
        var archive = await ExportedStash.Of(listed.Stash.Outside("export.zip"), null, incomingName);
        var page = await listed.Offered(archive);
        var listing = listed.Listing;
        await listing.SetQuery(CaptionQuery);
        Assert.AreEqual(saved.Id, listing.SelectedItems.Single().Id);
        AimAtRelay(listed);

        var releaseClipboard = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var copyClearedBusy = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseQueuedHandoff = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        listed.Clipboard.Hold = releaseClipboard.Task;
        listed.Handoff.Changed += HoldCopyBeforeItReleasesTheGate;
        var copying = listed.Handoff.CopyCapture(null!, other);
        Task<KeyAction>? enter = null;
        try
        {
            await Handing.Eventually(() => listed.Clipboard.Begun == 1);
            enter = PressEnter(listed);
            Assert.IsFalse(enter.IsCompleted, "Enter is queued behind the clipboard write");
            releaseClipboard.SetResult();
            await copyClearedBusy.Task.WaitAsync(Bound);

            var imported = await page.ConfirmImport().WaitAsync(Bound);
            Assert.IsTrue(imported.Succeeded, imported.Failure?.Sentence);
            Assert.AreEqual(1, listing.Replacements);
            var incoming = listing.SelectedItems.Single();
            Assert.AreEqual((saved.Id, incomingName), (incoming.Id, incoming.Name), "the archive reused the old match's ID");
            Assert.IsFalse(enter.IsCompleted, "the replacement has opened before the old request can run");
        }
        finally
        {
            releaseClipboard.TrySetResult();
            releaseQueuedHandoff.TrySetResult();
            await Task.WhenAll(copying, enter ?? Task.CompletedTask).WaitAsync(Bound);
            listed.Handoff.Changed -= HoldCopyBeforeItReleasesTheGate;
        }

        Assert.AreEqual(KeyAction.Handoff, await enter);
        Assert.IsTrue((await copying).Succeeded);
        Assert.AreEqual(1, listed.Clipboard.Begun, "only the earlier write reached the clipboard");
        CollectionAssert.AreEqual(new[] { other.Id }, listed.Clipboard.Written.Single().Content.Items.ToArray());
        Assert.IsEmpty(listed.Target.Steps, "the queued Enter never confirmed or activated the target");
        Assert.AreEqual(new UseHistory(0, null), (await listing.Store!.UseHistory(saved.Id)).Value,
            "the incoming item received no use");
        Assert.AreEqual(0L, listed.Uses());
        Assert.Contains(new Failure(Handoff.Action, saved.Name, Listing.ReplacedSinceReason).Sentence,
            listed.Notices.Shown.Select(notice => notice.Text));
        await listing.SetQuery("cobalt");
        Assert.IsTrue(listing.NoMatch, "text from the displaced item does not follow its identity");
        await listing.SetQuery("replacement");
        Assert.AreEqual(incomingName, listing.Items.Single().Name);

        void HoldCopyBeforeItReleasesTheGate()
        {
            // Transfer can proceed once the copy clears Busy. Keep Enter behind the gate until the new store opens.
            if (listed.Handoff.Busy is null && copyClearedBusy.TrySetResult())
            {
                releaseQueuedHandoff.Task.WaitAsync(Bound).GetAwaiter().GetResult();
            }
        }
    }

    private static async Task<StashItem> SaveCaption(ListedStash listed) =>
        await Save(listed.Stash, await listed.Stash.ImportFixture("landscape", "still.png"), Caption("Cobalt skyline"));

    private static void AimAtRelay(ListedStash listed) =>
        listed.PasteTarget.FromPress(new ForegroundReading(false, RecordingTarget.Relay));

    private static Task<KeyAction> PressEnter(ListedStash listed)
    {
        var enter = listed.Press(Key.Enter, KeyModifiers.None, KeyPlace.SearchBox);
        listed.Keys.Released(Key.Enter);
        return enter;
    }

    private static async Task AssertHandedToRelay(ListedStash listed, StashItem item, byte[] bytes, long totalUses = 1)
    {
        var written = listed.Clipboard.Written.Single().Content;
        CollectionAssert.AreEqual(new[] { item.Id }, written.Items.ToArray());
        CollectionAssert.AreEqual(new[] { item.FilePath }, written.FilePaths.ToArray());
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(written.FilePaths.Single()));
        Assert.IsNotNull(written.Png);
        Assert.IsNotNull(written.Bitmap);
        var png = listed.Stash.Outside("handed.png");
        File.WriteAllBytes(png, written.Png);
        var finished = MediaFixtures.FirstFrame(item.FilePath);
        CollectionAssert.AreEqual(finished.Pixels, MediaFixtures.FirstFrame(png).Pixels);
        Assert.AreEqual((finished.Width, finished.Height), (written.Bitmap.Width, written.Bitmap.Height));
        CollectionAssert.AreEqual(finished.Pixels, written.Bitmap.Pixels);
        Assert.AreEqual(RecordingTarget.Relay, listed.Target.Pasted.Single().Target);
        Assert.AreEqual(new UseHistory(1, listed.Stash.Clock.Now), (await listed.Listing.Store!.UseHistory(item.Id)).Value);
        Assert.AreEqual(totalUses, listed.Uses());
    }
}
