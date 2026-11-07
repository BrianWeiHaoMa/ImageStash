using ImageStash.Files;
using ImageStash.Seams;
using ImageStash.Store;
using ImageStash.Tests.Support;

namespace ImageStash.Tests.Window;

/// <summary>
/// The handoff's items confirmed with the store before anything is written, over a real stash with real pictures (G8,
/// G10): items a Delete or an import's Undo took while they still showed, and a new item that took a selected item's
/// file name, named by what the user acted on (G35) with nothing written, pasted or used; items read before a later
/// change refused while another program holds the store (E3), and handed over as they are now once it answers; an item
/// renamed meanwhile handed over under its new name; and one wait for a held store, whatever the selection's size.
/// </summary>
[TestClass]
public sealed class HandoffConfirmationTests
{
    [TestMethod]
    public async Task ItemsWhoseFilesADeleteTookWhileTheyStillShowedAreNamedAndNothingIsWrittenPastedOrUsed()
    {
        using var handing = await Handing.Open("animated.gif", "still.png");
        // As the item menu opened, or the grid still selected, while the Delete ran (G8, G35).
        var shown = handing.Items;
        await handing.Stash.Store!.Delete([shown[0].Id]);

        var pasted = await handing.Paste(shown, RecordingTarget.Relay);
        var copied = await handing.Copy([shown[0]]);

        Assert.AreEqual("Copy failed: animated, it is no longer in the stash", pasted.Failure?.Sentence);
        Assert.AreEqual(pasted.Failure!.Sentence, copied.Failure?.Sentence, "an animated item alone, which goes as its file only, too");
        Assert.AreEqual(0, handing.Clipboard.Begun, "the clipboard is not touched");
        Assert.IsEmpty(handing.Steps, "nothing confirmed, hidden or pasted");
        Assert.IsEmpty(handing.Uses());
    }

    [TestMethod]
    [DataRow("transparent.png", "still.png", false, false)]
    [DataRow("animated.gif", "still.gif", false, true)]
    [DataRow("transparent.png", "still.png", true, true)]
    public async Task AQueuedCopyNeverHandsOverANewItemThatTookTheSelectedItemsFileName(string selectedFixture, string replacementFixture, bool several, bool paste)
    {
        using var handing = await Handing.Open("photo.jpg", selectedFixture);
        var (first, selected) = (handing.Items[0], handing.Items[1]);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        handing.Clipboard.Hold = release.Task;
        var firstCopy = handing.Copy([first]);
        await Handing.Eventually(() => handing.Clipboard.Begun == 1);
        StashItem[] selection = several ? [selected, first] : [selected];
        var queued = paste ? handing.Paste(selection, RecordingTarget.Relay) : handing.Copy(selection);
        try
        {
            // While it waits: the import's Undo takes the selected item, and a new file comes in under its file name.
            Assert.IsTrue((await handing.Stash.Store!.Remove([selected.Id])).Single().Succeeded);
            var replacementPath = handing.Stash.Source(selected.FileName, MediaFixtures.Bytes(replacementFixture));
            var replacement = (await handing.Import.Drop(new DropContents([Handing.File(replacementPath)], false, false, []))).Single().Item!;
            Assert.AreEqual(selected.FilePath, replacement.FilePath);
        }
        finally
        {
            release.TrySetResult();
            await Task.WhenAll(firstCopy, queued);
        }

        Assert.HasCount(1, handing.Clipboard.Written, "only the first copy writes: a file name is never an item (G8)");
        Assert.AreEqual($"Copy failed: {selected.Name}, it is no longer in the stash", (await queued).Failure?.Sentence);
    }

    [TestMethod]
    [DataRow("transparent.png", "still.png")]
    [DataRow("animated.gif", "still.gif")]
    public async Task WhileTheStoreCannotBeReadARemovedItemIsNamedAndANewItemUnderItsFileNameNeverGoesInItsPlace(string fixture, string replacementFixture)
    {
        using var handing = await Handing.Open(fixture);
        var selected = handing.Items.Single();
        // While the selection still shows it: the import's Undo takes it, and a new file comes in under its file name.
        Assert.IsTrue((await handing.Stash.Store!.Remove([selected.Id])).Single().Succeeded);
        var source = handing.Stash.Source(selected.FileName, MediaFixtures.Bytes(replacementFixture));
        var replacement = (await handing.Import.Drop(new DropContents([Handing.File(source)], false, false, []))).Single().Item!;
        Assert.AreEqual(selected.FilePath, replacement.FilePath);
        foreach (var notice in handing.Notices.Shown.ToList())
        {
            handing.Notices.Close(notice);
        }

        Outcome pasted, copied, pastedBeside;
        using (handing.Stash.HoldStore())
        {
            pasted = await handing.Paste([selected], RecordingTarget.Relay);
            copied = await handing.Copy([selected]);
            pastedBeside = await handing.Paste([selected, replacement], RecordingTarget.Relay);
        }

        var named = HeldStore(selected.Name);
        Assert.AreEqual(named, pasted.Failure?.Sentence);
        Assert.AreEqual(named, copied.Failure?.Sentence);
        Assert.AreEqual(named, pastedBeside.Failure?.Sentence, "the item the store may have changed is named; the new one, read since, is not");
        CollectionAssert.AreEqual(new[] { named, named, named }, handing.Notices.Shown.Select(notice => notice.Text).ToList());
        Assert.AreEqual(0, handing.Clipboard.Begun, "nothing of the new item, nor anything else, is written (G8)");
        Assert.IsEmpty(handing.Steps, "nothing confirmed, hidden or pasted");
        Assert.IsEmpty(handing.Uses());
    }

    [TestMethod]
    public async Task SeveralItemsTheStashNoLongerHoldsAreNamedByCountAndNothingIsWrittenPastedOrUsed()
    {
        using var handing = await Handing.Open("animated.gif", "still.png");
        var (a, b) = (handing.Items[0], handing.Items[1]);
        Assert.IsTrue((await handing.Stash.Store!.Remove([a.Id, b.Id])).All(removed => removed.Succeeded));

        var pasted = await handing.Paste([a, b], RecordingTarget.Relay);
        var copied = await handing.Copy([a, b, a]);

        Assert.AreEqual("Copy failed: 2 items, they are no longer in the stash", pasted.Failure?.Sentence);
        Assert.AreEqual(pasted.Failure!.Sentence, copied.Failure?.Sentence, "an item selected twice is one item");
        Assert.AreEqual(0, handing.Clipboard.Begun);
        Assert.IsEmpty(handing.Steps);
        Assert.IsEmpty(handing.Uses());
    }

    [TestMethod]
    public async Task WhileTheStoreCannotBeReadItemsReadBeforeALaterChangeAreNamedByCountWithWhyAndNothingIsWritten()
    {
        using var handing = await Handing.Open("animated.gif", "still.png", "photo.jpg");
        var shown = handing.Listing.Items;
        Assert.IsTrue((await handing.Stash.Store!.AddTag([shown[2]], "mood")).Single().Succeeded);

        Outcome copied;
        using (handing.Stash.HoldStore())
        {
            copied = await handing.Copy([shown[0], shown[1]]);
        }

        Assert.AreEqual(HeldStore("2 items"), copied.Failure?.Sentence);
        Assert.AreEqual(0, handing.Clipboard.Begun);
        Assert.IsEmpty(handing.Uses());
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task WhileTheStoreCannotBeReadAnItemReadBeforeALaterChangeIsNamedAndOnceItCanBeReadItGoesAsItIsNow(bool renamedItself)
    {
        using var handing = await Handing.Open("photo.jpg", "transparent.png");
        var selected = handing.Listing.Items.Single(item => item.Name == "transparent");
        var other = handing.Listing.Items.Single(item => item.Id != selected.Id);
        // A change since the selection was read: to the item itself, whose old file name a new import could take, or to any other.
        Assert.IsTrue(renamedItself
            ? (await handing.Stash.Store!.Rename(selected, "renamed")).Succeeded
            : (await handing.Stash.Store!.AddTag([other], "mood")).Single().Succeeded);

        Outcome refused;
        using (handing.Stash.HoldStore())
        {
            refused = await handing.Copy([selected]);
        }

        var copied = await handing.Copy([selected]);

        Assert.AreEqual(HeldStore("transparent"), refused.Failure?.Sentence);
        Assert.IsTrue(copied.Succeeded, copied.Failure?.Sentence);
        var path = renamedItself ? handing.Stash.Folder.InRoot("renamed.png") : selected.FilePath;
        CollectionAssert.AreEqual(new[] { path }, handing.Clipboard.Written.Single().Content.FilePaths.ToList(), "written once the store answered, under its file as it is now");
        CollectionAssert.AreEqual(new[] { selected.Id }, handing.Uses().Select(use => use.Item).ToList());
    }

    [TestMethod]
    public async Task AQueuedCopyOfAnItemRenamedMeanwhileGoesUnderItsNewNameAndNothingOfANewItemUnderItsOldOne()
    {
        using var handing = await Handing.Open("photo.jpg", "transparent.png");
        var (first, selected) = (handing.Items[0], handing.Items[1]);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        handing.Clipboard.Hold = release.Task;
        var firstCopy = handing.Copy([first]);
        await Handing.Eventually(() => handing.Clipboard.Begun == 1);
        var queued = handing.Copy([selected]);
        StashItem renamed;
        try
        {
            renamed = (await handing.Stash.Store!.Rename(selected, "renamed")).Value!;
            var source = handing.Stash.Source(selected.FileName, MediaFixtures.Bytes("still.png"));
            var replacement = (await handing.Import.Drop(new DropContents([Handing.File(source)], false, false, []))).Single().Item!;
            Assert.AreEqual(selected.FilePath, replacement.FilePath);
        }
        finally
        {
            release.TrySetResult();
            await Task.WhenAll(firstCopy, queued);
        }

        Assert.IsTrue((await queued).Succeeded, (await queued).Failure?.Sentence);
        var (content, _) = handing.Clipboard.Written[1];
        CollectionAssert.AreEqual(new[] { renamed.FilePath }, content.FilePaths.ToList(), "its own file, under its new name (G8, P1)");
        CollectionAssert.AreEqual(MediaFixtures.Bytes("transparent.png"), content.Png, "its own picture, not the new item's");
        CollectionAssert.AreEqual(new[] { first.Id, selected.Id }, handing.Uses().Select(use => use.Item).ToList());
    }

    [TestMethod]
    public async Task WhileTheStoreIsHeldFourItemsWaitForItOnceAsOneItemDoes()
    {
        using var handing = await Handing.Open("still.png", "photo.jpg", "animated.gif", "transparent.png");
        var all = handing.Listing.Items;
        TimeSpan one, four;
        Outcome pastedOne, pastedFour;
        using (handing.Stash.HoldStore())
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var pasting = handing.Paste([all[0]], RecordingTarget.Relay);
            await Handing.Eventually(() => handing.Clipboard.Begun == 1);
            one = clock.Elapsed;
            pastedOne = await pasting;

            clock.Restart();
            pasting = handing.Paste(all, RecordingTarget.Relay);
            await Handing.Eventually(() => handing.Clipboard.Begun == 2);
            four = clock.Elapsed;
            pastedFour = await pasting;
        }

        var times = $"one item {one.TotalMilliseconds:F0} ms, four {four.TotalMilliseconds:F0} ms";
        Assert.IsLessThan(one.TotalMilliseconds * 2, four.TotalMilliseconds, "one wait for the store, not one per item: " + times);
        Assert.IsLessThan(3000, four.TotalMilliseconds, times);
        Assert.IsTrue(pastedOne.Succeeded && pastedFour.Succeeded, "nothing committed since they were read, so both go (G10)");
        Assert.HasCount(4, handing.Clipboard.Written[1].Content.FilePaths);
        Assert.HasCount(2, handing.Notices.Shown.Where(notice => notice.Text.StartsWith("Record a use failed: ")).ToList());
    }

    /// <summary>A handoff refused, naming <paramref name="subject"/>, while another program holds the store.</summary>
    private static string HeldStore(string subject) => $"Copy failed: {subject}, the stash could not be read ({StashFolder.StoreFileName}: {HeldFileRetry.HeldReason})";
}
