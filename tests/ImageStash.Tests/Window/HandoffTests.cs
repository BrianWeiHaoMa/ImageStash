using ImageStash.Seams;
using ImageStash.Store;
using ImageStash.Tests.Support;
using ImageStash.Window;
using SkiaSharp;

namespace ImageStash.Tests.Window;

/// <summary>
/// The handoff's copy and its uses over a real stash with real pictures: what each selection puts on the clipboard
/// (G8); one use per item, with its time, only once the write has completed, and none for one that failed (G10); one
/// write at a time (G8); Recent holds its order while the window stays (G4); every failure named by what the user acted on
/// (G35, E3).
/// </summary>
[TestClass]
public sealed class HandoffTests
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    [TestMethod]
    public async Task ASingleStillGoesAsItsFileItsPngAndItsBitmapAndItsUseIsRecordedAfterTheWrite()
    {
        using var handing = await Handing.Open("transparent.png");
        var item = handing.Items.Single();
        var usedDuringTheWrite = -1;
        handing.Clipboard.AtWrite = async () => usedDuringTheWrite = (await handing.Stash.Store!.Recent()).Value!.Count;

        var copied = await handing.Copy([item]);

        Assert.IsTrue(copied.Succeeded, copied.Failure?.Sentence);
        var (content, writtenAt) = handing.Clipboard.Written.Single();
        CollectionAssert.AreEqual(new[] { item.FilePath }, content.FilePaths.ToList());
        CollectionAssert.AreEqual(File.ReadAllBytes(item.FilePath), content.Png, "a PNG goes as the stash file's own bytes");
        Assert.AreEqual((8, 6), (content.Bitmap!.Width, content.Bitmap.Height));
        Assert.AreEqual(128, MediaFixtures.Alpha(content.Bitmap, 1, 1), "the bitmap keeps the half-transparent pixel");
        Assert.AreEqual(255, MediaFixtures.Alpha(content.Bitmap, 0, 0));
        Assert.AreEqual(0, usedDuringTheWrite, "no use exists until the write has completed");
        Assert.IsEmpty(handing.Notices.Shown, "a copy that worked says nothing");
        Assert.IsNull(handing.Handoff.Busy);
        CollectionAssert.AreEqual(new[] { (item.Id, writtenAt.ToUnixTimeMilliseconds()) }, handing.Uses(), "one use, at the clock's UTC time");
    }

    [TestMethod]
    [DataRow("photo.jpg")]
    [DataRow("still.gif")]
    [DataRow("still.webp")]
    public async Task EveryStillAloneAlsoGoesAsAPngAndABitmap(string fixture)
    {
        using var handing = await Handing.Open(fixture);

        await handing.Copy(handing.Items);

        var content = handing.Clipboard.Written.Single().Content;
        CollectionAssert.AreEqual(PngSignature, content.Png!.Take(8).ToArray());
        using (var decoded = SKBitmap.Decode(content.Png))
        {
            Assert.AreEqual((8, 6), (decoded.Width, decoded.Height));
        }

        Assert.AreEqual((8, 6), (content.Bitmap!.Width, content.Bitmap.Height));
        Assert.HasCount(1, handing.Uses());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task AnItemTheStoreSavedGoesAsItsRenderAndAStillsPngAndBitmapAreTheRendersPixels(bool paste)
    {
        using var handing = await Handing.Open("still.webp", "animated.webp");
        var still = (await handing.Stash.SaveEdit(handing.Items[0], EditedItems.BlackBoxAndBox)).Value!;
        var clip = (await handing.Stash.SaveEdit(handing.Items[1], EditedItems.BlackBoxAndBox)).Value!;

        foreach (var item in new[] { still, clip })
        {
            var handed = paste ? await handing.Paste([item], RecordingTarget.Relay) : await handing.Copy([item]);
            Assert.IsTrue(handed.Succeeded, handed.Failure?.Sentence);
        }

        var (stillContent, clipContent) = (handing.Clipboard.Written[0].Content, handing.Clipboard.Written[1].Content);
        CollectionAssert.AreEqual(new[] { handing.Stash.Folder.InRoot("still.png") }, stillContent.FilePaths.ToList(), "the render, not the original (G8)");
        var rendered = MediaFixtures.FirstFrame(still.FilePath).Pixels;
        Assert.AreNotEqual(EditedItems.Hash(MediaFixtures.FirstFrame(handing.Stash.Folder.InOriginals("still.webp")).Pixels), EditedItems.Hash(rendered));
        CollectionAssert.AreEqual(File.ReadAllBytes(still.FilePath), stillContent.Png, "a still render goes as its own PNG bytes");
        CollectionAssert.AreEqual(rendered, stillContent.Bitmap!.Pixels, "the bitmap is the render's pixels");
        CollectionAssert.AreEqual(new[] { handing.Stash.Folder.InRoot("animated.gif") }, clipContent.FilePaths.ToList());
        Assert.IsNull(clipContent.Png, "an animation goes as its file only");
        Assert.IsNull(clipContent.Bitmap);
    }

    [TestMethod]
    [DataRow("animated.gif")]
    [DataRow("animated.webp")]
    public async Task AnAnimatedItemGoesAsItsFileOnly(string fixture)
    {
        using var handing = await Handing.Open(fixture);

        await handing.Copy(handing.Items);

        var content = handing.Clipboard.Written.Single().Content;
        CollectionAssert.AreEqual(new[] { handing.Items[0].FilePath }, content.FilePaths.ToList());
        Assert.IsNull(content.Png, "its first frame would paste as a still");
        Assert.IsNull(content.Bitmap);
        Assert.HasCount(1, handing.Uses());
    }

    [TestMethod]
    public async Task SeveralItemsGoAsTheirFilesInOrderWithOneUseEachAndRecentListsThemFirst()
    {
        using var handing = await Handing.Open("still.png", "photo.jpg", "animated.gif", "transparent.png");
        var older = handing.Items[0];
        var three = handing.Items.Skip(1).ToList();
        await handing.Copy([older]);
        handing.Stash.Clock.Now += TimeSpan.FromMinutes(1);

        await handing.Copy(three);

        var content = handing.Clipboard.Written[^1].Content;
        CollectionAssert.AreEqual(three.Select(item => item.FilePath).ToList(), content.FilePaths.ToList());
        Assert.IsNull(content.Png, "several items go as files only, a still among them");
        Assert.IsNull(content.Bitmap);
        var recent = (await handing.Stash.Store!.Recent()).Value!;
        CollectionAssert.AreEqual(three.Select(item => item.Id).Reverse().Append(older.Id).ToList(), recent.Select(item => item.Id).ToList(),
            "the three first, later identities first among equal times");
        var at = handing.Stash.Clock.Now.ToUnixTimeMilliseconds();
        CollectionAssert.AreEquivalent(three.Select(item => (item.Id, at)).ToList(), handing.Uses().Skip(1).ToList(), "one use per item, one time");
    }

    [TestMethod]
    public async Task CopyRecordsUseWhileRecentKeepsItsOrderUntilSummon()
    {
        using var handing = await Handing.Open("photo.jpg", "animated.gif");
        var (first, second) = (handing.Items[0], handing.Items[1]);
        await handing.Copy([first]);
        handing.Stash.Clock.Now += TimeSpan.FromMinutes(1);
        await handing.Copy([second]);
        await handing.Listing.ShowScope(Scope.Recent);
        CollectionAssert.AreEqual(new[] { second.Id, first.Id }, handing.Listing.Items.Select(item => item.Id).ToList());
        handing.Listing.Focus(first.Id);
        handing.Stash.Clock.Now += TimeSpan.FromMinutes(1);

        await handing.Copy(handing.Listing.SelectedItems);

        CollectionAssert.AreEqual(new[] { second.Id, first.Id }, handing.Listing.Items.Select(item => item.Id).ToList(), "Recent while it shows (G4)");
        CollectionAssert.AreEqual(new[] { first.Id, second.Id }, (await handing.Stash.Store!.Recent()).Value!.Select(item => item.Id).ToList(), "the copy already recorded its use");
        await handing.Listing.Summoned(flyout: false);
        CollectionAssert.AreEqual(new[] { first.Id, second.Id }, handing.Listing.Items.Select(item => item.Id).ToList(), "the next summon takes the latest order");
        CollectionAssert.AreEqual(new[] { first.Id }, handing.Listing.Selection.ToList());
        Assert.AreEqual(first.Id, handing.Listing.Focused);
    }

    [TestMethod]
    public async Task AHeldClipboardIsNamedByTheItemAndRecordsNothingAndTheNextCopyWorks()
    {
        using var handing = await Handing.Open("transparent.png", "photo.jpg", "animated.gif");
        handing.Clipboard.FailNext = RecordingClipboard.Held;

        var one = await handing.Copy([handing.Items[0]]);
        Assert.AreEqual(one.Failure?.Sentence, handing.Notices.Shown.Single().Text, "named in a notice");
        handing.Clipboard.FailNext = RecordingClipboard.Held;
        var several = await handing.Copy(handing.Items);

        Assert.AreEqual("Couldn't copy (transparent): another program is holding the clipboard open.", one.Failure?.Sentence);
        Assert.AreEqual("Couldn't copy (3 items): another program is holding the clipboard open.", several.Failure?.Sentence);
        Assert.AreEqual(several.Failure!.Sentence, handing.Notices.Shown.Single().Text, "the next copy's failure takes the earlier one's place (G16)");
        Assert.IsEmpty(handing.Clipboard.Written);
        Assert.IsEmpty((await handing.Stash.Store!.Recent()).Value!, "a write that failed records nothing (G10)");

        var again = await handing.Copy([handing.Items[0]]);

        Assert.IsTrue(again.Succeeded);
        Assert.HasCount(1, handing.Clipboard.Written);
        Assert.HasCount(1, handing.Uses());
    }

    [TestMethod]
    public async Task AnItemWithoutANameIsNamedByItsFile()
    {
        using var handing = await Handing.Open();
        var source = handing.Stash.Source(".png", MediaFixtures.Bytes("still.png"));
        var unnamed = (await handing.Import.Drop(new DropContents([Handing.File(source)], false, false, []))).Single().Item!;
        handing.Clipboard.FailNext = RecordingClipboard.Held;

        var copied = await handing.Copy([unnamed]);

        Assert.AreEqual($"Couldn't copy ({ItemFileNames.Unnamed}.png): {HeldClipboard.Reason}.", copied.Failure?.Sentence);
    }

    [TestMethod]
    public async Task AUseThatCannotBeRecordedIsNamedBesideACopyThatStaysDone()
    {
        using var handing = await Handing.Open("animated.gif");
        Outcome copied;
        using (handing.Stash.HoldStore())
        {
            copied = await handing.Copy(handing.Items);
        }

        Assert.IsTrue(copied.Succeeded, "the write completed, so the copy is done (G10)");
        Assert.HasCount(1, handing.Clipboard.Written, "the clipboard keeps what was written");
        Assert.StartsWith("Couldn't record a use: ", handing.Notices.Shown.Single().Text);
        Assert.IsEmpty(handing.Uses());
    }

    [TestMethod]
    public async Task ACopyThatCompletesWhileItsItemIsDeletedRecordsItsUseWhichTheDeletesUndoBringsBack()
    {
        using var handing = await Handing.Open("animated.gif");
        var item = handing.Items.Single();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        handing.Clipboard.Hold = release.Task;
        var copying = handing.Copy([item]);
        await Handing.Eventually(() => handing.Clipboard.Begun == 1);

        var deleted = (await handing.Stash.Store!.Delete([item.Id])).Single();
        release.SetResult();
        var copied = await copying;

        Assert.IsTrue(deleted.Succeeded, deleted.Failure?.Sentence);
        Assert.IsTrue(copied.Succeeded, "the write completed, so the copy is done (G10)");
        Assert.IsEmpty(handing.Notices.Shown, "its use is recorded, so nothing is to be named");
        Assert.IsTrue((await handing.Stash.Store.UndoDelete([deleted.Value!])).Single().Succeeded);
        Assert.AreEqual(1, (await handing.Stash.Store.UseHistory(item.Id)).Value!.Count, "back with every use, the completed copy's too (G10, G18)");
    }

    [TestMethod]
    public async Task ACopyThatCompletesAfterItsItemLeftTheStashNamesTheUseItCouldNotRecord()
    {
        using var handing = await Handing.Open("animated.gif");
        var item = handing.Items.Single();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        handing.Clipboard.Hold = release.Task;
        var copying = handing.Copy([item]);
        await Handing.Eventually(() => handing.Clipboard.Begun == 1);

        // The import's Undo, from its notice, while the write waits for a clipboard another program holds.
        Assert.IsTrue((await handing.Stash.Store!.Remove([item.Id])).Single().Succeeded);
        release.SetResult();
        var copied = await copying;

        Assert.IsTrue(copied.Succeeded, "the write completed, so the copy is done (G10)");
        Assert.AreEqual("Couldn't record a use: animated is no longer in the stash.", handing.Notices.Shown.Single().Text);
        Assert.IsEmpty(handing.Uses());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task UsesRecordedBesideOneThatHadNoItemLeftJoinRecent(bool drag)
    {
        using var handing = await Handing.Open("animated.gif", "transparent.png", "photo.jpg");
        var (older, survivor, gone) = (handing.Items[0], handing.Items[1], handing.Items[2]);
        await handing.Copy([older]);
        await handing.Listing.ShowScope(Scope.Recent);
        handing.Stash.Clock.Now += TimeSpan.FromMinutes(1);
        async Task GoneOnceWritten()
        {
            Assert.IsTrue((await handing.Stash.Store!.Remove([gone.Id])).Single().Succeeded);
            await handing.Listing.Refresh();
        }

        if (drag)
        {
            await handing.Handoff.DragOut([survivor, gone], async _ =>
            {
                await GoneOnceWritten();
                return Outcome<bool>.Of(true);
            }, _ => { }, () => { });
        }
        else
        {
            handing.Clipboard.AtWrite = GoneOnceWritten;
            await handing.Copy([survivor, gone]);
        }

        Assert.AreEqual("Couldn't record a use: photo is no longer in the stash.", handing.Notices.Shown.Single().Text);
        CollectionAssert.AreEqual(new[] { older.Id, survivor.Id }, handing.Listing.Items.Select(item => item.Id).ToList(), "the newly used item joins the end");
        Assert.AreEqual(survivor.Id, (await handing.Stash.Store!.Recent()).Value![0].Id, "its use is already the latest in the stash");
    }

    [TestMethod]
    public async Task AStillWhoseFileIsGoneIsNamedAndNothingIsWritten()
    {
        using var handing = await Handing.Open("transparent.png");
        File.Delete(handing.Items[0].FilePath);

        var copied = await handing.Copy(handing.Items);

        Assert.AreEqual("Couldn't read the picture (transparent): the file does not exist.", copied.Failure?.Sentence);
        Assert.AreEqual(copied.Failure!.Sentence, handing.Notices.Shown.Single().Text);
        Assert.AreEqual(0, handing.Clipboard.Begun, "the clipboard is not touched");
        Assert.IsEmpty(handing.Uses());
    }

    [TestMethod]
    public async Task NothingSelectedCopiesNothing()
    {
        using var handing = await Handing.Open("transparent.png");
        var footer = Footer(handing);

        var copied = await handing.Copy([]);

        Assert.IsTrue(copied.Succeeded);
        Assert.AreEqual(0, handing.Clipboard.Begun);
        Assert.IsEmpty(handing.Notices.Shown);
        Assert.IsNull(footer.Confirmation, "nothing went on the clipboard, so nothing is confirmed");
        Assert.IsEmpty(handing.Uses());
    }

    [TestMethod]
    public async Task ACopyIsConfirmedOnlyOnceItsWriteHasReturnedAndNeverBesideItsBusyState()
    {
        using var handing = await Handing.Open("transparent.png");
        var footer = Footer(handing);
        string? busyAtConfirmation = "never confirmed";
        footer.Changed += () => busyAtConfirmation = footer.Confirmation is null ? busyAtConfirmation : handing.Handoff.Busy;
        var release = new TaskCompletionSource();
        handing.Clipboard.Hold = release.Task;

        var copying = handing.Copy([handing.Items[0]]);
        await Handing.Eventually(() => handing.Clipboard.Begun == 1);
        var busyDuringTheWrite = handing.Handoff.Busy;
        var confirmedDuringTheWrite = footer.Confirmation;
        release.SetResult();
        var copied = await copying;

        Assert.IsTrue(copied.Succeeded, copied.Failure?.Sentence);
        Assert.AreEqual("Copying transparent to the clipboard…", busyDuringTheWrite, "while the copy runs its busy state names it (G35)");
        Assert.IsNull(confirmedDuringTheWrite, "no success before the write it reports has returned (R9, G28)");
        Assert.IsNull(busyAtConfirmation, "the confirmation shows once the copy's busy state has gone, never beside it");
        Assert.AreEqual("✓ Copied “transparent”", footer.Confirmation);
    }

    [TestMethod]
    public async Task AnEarlierCopysConfirmationGivesWayWhileTheNextCopyRuns()
    {
        using var handing = await Handing.Open("animated.gif", "photo.jpg");
        var footer = Footer(handing);
        await handing.Copy([handing.Items[0]]);
        var first = footer.Confirmation;
        var release = new TaskCompletionSource();
        handing.Clipboard.Hold = release.Task;

        var copying = handing.Copy([handing.Items[1]]);
        await Handing.Eventually(() => handing.Clipboard.Begun == 2);
        var whileBusy = (handing.Handoff.Busy, footer.Confirmation);
        release.SetResult();
        await copying;

        Assert.IsNotNull(first);
        Assert.AreEqual(("Copying photo to the clipboard…", (string?)null), whileBusy, "the busy state names the copy running, and no earlier success stands beside it (G35)");
        Assert.AreEqual("✓ Copied “photo”", footer.Confirmation, "then the latest copy is confirmed");
    }

    [TestMethod]
    public async Task ACopyRenamedWhileItWaitedItsTurnIsConfirmedUnderTheNameItWentUnder()
    {
        using var handing = await Handing.Open("animated.gif", "transparent.png");
        var footer = Footer(handing);
        var (first, waiting) = (handing.Items[0], handing.Items[1]);
        var release = new TaskCompletionSource();
        handing.Clipboard.Hold = release.Task;

        var copyingFirst = handing.Copy([first]);
        await Handing.Eventually(() => handing.Clipboard.Begun == 1);
        var copyingWaiting = handing.Copy([waiting]);
        var renamed = await handing.Stash.Store!.Rename(waiting, "new cat");
        release.SetResult();
        await Task.WhenAll(copyingFirst, copyingWaiting);

        Assert.IsTrue(renamed.Succeeded, renamed.Failure?.Sentence);
        Assert.AreEqual(renamed.Value!.FilePath, handing.Clipboard.Written[^1].Content.FilePaths.Single(), "the file under its new name went on the clipboard");
        Assert.AreEqual("✓ Copied “new cat”", footer.Confirmation, "so the confirmation names what went (G10)");
    }

    [TestMethod]
    public async Task AClipboardThatCannotBeWrittenConfirmsNothingAndItsFailureIsNamed()
    {
        using var handing = await Handing.Open("transparent.png", "photo.jpg");
        var footer = Footer(handing);
        handing.Clipboard.FailNext = RecordingClipboard.Held;

        var copied = await handing.Copy(handing.Items);

        Assert.IsFalse(copied.Succeeded);
        Assert.IsNull(footer.Confirmation, "a write that failed confirms nothing (R9)");
        Assert.AreEqual(copied.Failure!.Sentence, handing.Notices.Shown.Single().Text, "its failure's notice says so (G35)");
    }

    [TestMethod]
    public async Task AUseTheStoreRefusesLeavesTheCopyConfirmedAndTheFailureNamed()
    {
        using var handing = await Handing.Open("animated.gif");
        var footer = Footer(handing);
        Outcome copied;
        using (handing.Stash.HoldStore())
        {
            copied = await handing.Copy(handing.Items);
        }

        Assert.IsTrue(copied.Succeeded, "the copy stands (G10)");
        Assert.AreEqual("✓ Copied “animated”", footer.Confirmation, "so it is confirmed");
        Assert.StartsWith("Couldn't record a use: ", handing.Notices.Shown.Single().Text, "and the use it could not record is named (G10, G35)");
    }

    [TestMethod]
    public async Task AnItemWithoutANamesCopyIsConfirmedByItsFilesName()
    {
        using var handing = await Handing.Open();
        var footer = Footer(handing);
        var source = handing.Stash.Source(".png", MediaFixtures.Bytes("still.png"));
        var unnamed = (await handing.Import.Drop(new DropContents([Handing.File(source)], false, false, []))).Single().Item!;

        var copied = await handing.Copy([unnamed]);

        Assert.IsTrue(copied.Succeeded, copied.Failure?.Sentence);
        Assert.AreEqual($"✓ Copied “{ItemFileNames.Unnamed}.png”", footer.Confirmation);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task APasteIntoATargetConfirmsNothingInTheWindowWhicheverWayItEnds(bool targetTakesIt)
    {
        using var handing = await Handing.Open("transparent.png");
        var footer = Footer(handing);
        if (!targetTakesIt)
        {
            handing.Target.Pastes = new Failure(PasteTargetReasons.Action, RecordingTarget.Relay.AppName, "it is gone");
        }

        var pasted = await handing.Paste([handing.Items[0]], RecordingTarget.Relay);

        Assert.AreEqual(targetTakesIt, pasted.Succeeded, pasted.Failure?.Sentence);
        Assert.HasCount(1, handing.Clipboard.Written);
        Assert.IsNull(footer.Confirmation, "a paste never says it arrived; its fallback's notification is its own word (G10, G8)");
        CollectionAssert.AreEqual(targetTakesIt ? Array.Empty<string>() : [PasteFallback.Copied], handing.Notifications.Shown.Select(notice => notice.Heading).ToList());
    }

    [TestMethod]
    public async Task ACapturesCopyConfirmsNothingInTheWindow()
    {
        using var handing = await Handing.Open("transparent.png");
        var footer = Footer(handing);

        var copied = await handing.Handoff.CopyCapture(null!, handing.Items[0]);

        Assert.IsTrue(copied.Succeeded, copied.Failure?.Sentence);
        Assert.IsNull(footer.Confirmation, "the capture's notification says it is on the clipboard (G14, G16)");
    }

    /// <summary>The footer's words over the handoff, as the window composes them, with no paste target.</summary>
    private static FooterHints Footer(Handing handing) => new(handing.Listing, new PasteTargetHolder(), handing.Handoff, handing.Stash.Clock);

    [TestMethod]
    public async Task ACopyPressedDuringAnotherWritesWholeAfterIt()
    {
        using var handing = await Handing.Open("animated.gif", "animated.webp");
        var (first, second) = (handing.Items[0], handing.Items[1]);
        var release = new TaskCompletionSource();
        handing.Clipboard.Hold = release.Task;

        var copyingFirst = handing.Copy([first]);
        var copyingSecond = handing.Copy([second]);
        await Handing.Eventually(() => handing.Clipboard.Begun == 1);
        await Task.Delay(100);

        Assert.AreEqual(1, handing.Clipboard.Begun, "the second write waits for the first");
        Assert.AreEqual("Copying animated to the clipboard…", handing.Handoff.Busy);
        release.SetResult();
        await Task.WhenAll(copyingFirst, copyingSecond);
        CollectionAssert.AreEqual(new[] { first.FilePath, second.FilePath }, handing.Clipboard.Written.Select(write => write.Content.FilePaths.Single()).ToList());
        Assert.IsNull(handing.Handoff.Busy);
        CollectionAssert.AreEquivalent(new[] { first.Id, second.Id }, handing.Uses().Select(use => use.Item).ToList(), "each completed write records its own use");
    }
}
