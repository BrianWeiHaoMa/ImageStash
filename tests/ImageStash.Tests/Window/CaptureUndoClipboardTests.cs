using Avalonia;
using ImageStash.Capture;
using ImageStash.Seams;
using ImageStash.Store;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// A capture card's Undo and the clipboard (G16), through the card's own Undo over a real stash: the clipboard emptied,
/// read as a paste would get it, when ImageStash's latest write there holds the capture, alone or among others, whichever
/// act wrote it, and nothing has changed it since; left as it is after another item's copy or another program's write,
/// or when the capture was never copied, and while the removal is refused with the picture still there; emptied when
/// the removal fails only after the picture has gone. Judged without reading clipboard data (N9, R5) and recording no
/// use (G10); after the copies' one write at a time (G8); a clipboard held past its retries named as its own act, the
/// removal standing (E3, G35); Move, Export and Import waiting for it (G36); and the write forgotten, so a later Save's
/// refresh writes nothing back (G26).
/// </summary>
[TestClass]
public sealed class CaptureUndoClipboardTests
{
    private static readonly PixelRect Region = new(100, 50, 30, 20);

    [TestMethod]
    public async Task UndoOfACopiedScreenshotEmptiesTheClipboardReadingNothingAndRecordingNoUse()
    {
        using var listed = await Taking();
        var shot = await Screenshot(listed);
        CollectionAssert.AreEqual(new[] { shot.FilePath }, listed.Clipboard.Holds!.FilePaths.ToList(), "Also copy put it there (G14)");

        OnControls(() => UndoOnTheCard(listed));

        Assert.IsNull(listed.Clipboard.Holds, "a paste gets nothing: no file now in the Recycle Bin");
        Assert.IsEmpty(await listed.Stash.All(), "the capture removed");
        Assert.IsEmpty(listed.Notifications.Shown, "the card dismissed, nothing failed");
        Assert.AreEqual((0, 0), (listed.Clipboard.Reads, listed.Clipboard.ImageReads), "judged without reading the clipboard (N9, R5)");
    }

    [TestMethod]
    public async Task ACopyOfSeveralItemsHoldingTheCaptureAmongThemIsEmptied()
    {
        using var listed = await Taking();
        var other = await listed.Stash.ImportFixture("other", "still.png");
        var shot = await Screenshot(listed);
        Assert.IsTrue((await listed.Handoff.Copy(null!, [other, shot])).Succeeded);

        OnControls(() => UndoOnTheCard(listed));

        Assert.IsNull(listed.Clipboard.Holds, "it would hand over a file that is gone");
        CollectionAssert.AreEqual(new[] { other.Id }, (await listed.Stash.All()).Select(item => item.Id).ToList());
        Assert.AreEqual(1L, listed.Uses(), "the copy's use of the other item alone: the emptying records none (G10)");
        Assert.IsEmpty(listed.Notifications.Shown);
    }

    [TestMethod]
    public async Task AnotherItemCopiedSinceStaysOnTheClipboard()
    {
        using var listed = await Taking();
        var other = await listed.Stash.ImportFixture("other", "still.png");
        await Screenshot(listed);
        Assert.IsTrue((await listed.Handoff.Copy(null!, [other])).Succeeded);

        OnControls(() => UndoOnTheCard(listed));

        CollectionAssert.AreEqual(new[] { other.FilePath }, listed.Clipboard.Holds!.FilePaths.ToList(), "ImageStash's latest write holds no removed capture");
    }

    [TestMethod]
    public async Task AnotherProgramsWriteSinceStaysOnTheClipboard()
    {
        using var listed = await Taking();
        await Screenshot(listed);
        var elsewhere = new ClipboardContent([listed.Stash.Outside("elsewhere.png")], null, null, []);
        listed.Clipboard.AnotherProgramWrites(elsewhere);

        OnControls(() => UndoOnTheCard(listed));

        Assert.AreSame(elsewhere, listed.Clipboard.Holds, "someone else's clipboard is left as it is");
        Assert.AreEqual((0, 0), (listed.Clipboard.Reads, listed.Clipboard.ImageReads), "told apart without reading its data (N9, R5)");
    }

    [TestMethod]
    public async Task WithAlsoCopyOffAnOlderCopyStaysAndTheCaptureCopiedByCtrlCIsTakenOff()
    {
        using var listed = await Taking(alsoCopy: false);
        var older = await listed.Stash.ImportFixture("older", "still.png");
        Assert.IsTrue((await listed.Handoff.Copy(null!, [older])).Succeeded);
        await Screenshot(listed);

        OnControls(() => UndoOnTheCard(listed));

        CollectionAssert.AreEqual(new[] { older.FilePath }, listed.Clipboard.Holds!.FilePaths.ToList(), "a capture never copied leaves the clipboard as it is");

        var shot = await Screenshot(listed);
        Assert.IsTrue((await listed.Handoff.Copy(null!, [shot])).Succeeded);

        OnControls(() => UndoOnTheCard(listed));

        Assert.IsNull(listed.Clipboard.Holds, "judged by the write, not by the setting");
    }

    [TestMethod]
    public async Task ARemovalTheStoreRefusesWholeKeepsTheCaptureOnTheClipboard()
    {
        using var listed = await Taking();
        var shot = await Screenshot(listed);
        var copied = listed.Clipboard.Holds;

        using (listed.Stash.HoldStore())
        {
            OnControls(() => UndoOnTheCard(listed));
        }

        Assert.IsTrue(listed.Notifications.Shown.Any(notice => notice.Text.StartsWith("Couldn't remove from the stash", StringComparison.Ordinal)), Shown(listed));
        Assert.AreSame(copied, listed.Clipboard.Holds, "the capture is still in the stash, and still on the clipboard");
        Assert.IsEmpty(listed.Clipboard.EmptiesAsked, "nothing emptied before the removal");
        CollectionAssert.AreEqual(new[] { shot.Id }, (await listed.Stash.All()).Select(item => item.Id).ToList());
    }

    [TestMethod]
    public async Task ARemovalWhoseRecycleIsRefusedLeavesThePictureAndTheClipboardAsTheyAre()
    {
        using var listed = await Taking();
        var shot = await Screenshot(listed);
        var copied = listed.Clipboard.Holds;
        listed.Stash.RecycleBin.Refusing = path => path == shot.FilePath;

        OnControls(() => UndoOnTheCard(listed));

        Assert.IsTrue(File.Exists(shot.FilePath), "the Recycle Bin refused the picture, which stays where it was");
        Assert.IsTrue(listed.Notifications.Shown.Any(notice => notice.Text.StartsWith("Couldn't move to the Recycle Bin", StringComparison.Ordinal)), Shown(listed));
        Assert.AreSame(copied, listed.Clipboard.Holds, "a paste still gets a file that is there");
        Assert.IsEmpty(listed.Clipboard.EmptiesAsked);
    }

    [TestMethod]
    public async Task ARemovalFailingAfterThePictureHasGoneEmptiesTheClipboardAndIsNamed()
    {
        using var listed = await Taking();
        var shot = await Screenshot(listed);
        Microsoft.Data.Sqlite.SqliteConnection? holder = null;
        // Held once the row is marked, as the picture goes to the Recycle Bin: the row's end is refused after it.
        listed.Stash.RecycleBin.BeforeEach = _ => holder ??= listed.Stash.HoldStore();

        try
        {
            OnControls(() => UndoOnTheCard(listed));
        }
        finally
        {
            holder?.Dispose();
        }

        Assert.IsFalse(File.Exists(shot.FilePath), "the picture went to the Recycle Bin");
        Assert.IsTrue(listed.Notifications.Shown.Any(notice => notice.Text.StartsWith("Couldn't remove from the stash", StringComparison.Ordinal)), Shown(listed));
        Assert.IsNull(listed.Clipboard.Holds, "the clipboard no longer hands over the file that has gone");
    }

    [TestMethod]
    public async Task AClipboardHeldPastItsRetriesIsNamedAsItsOwnActAndTheRemovalStands()
    {
        using var listed = await Taking();
        await Screenshot(listed);
        var copied = listed.Clipboard.Holds;
        listed.Clipboard.FailNextEmpty = new Failure("Clear the clipboard", "the clipboard", HeldClipboard.Reason);

        OnControls(() => UndoOnTheCard(listed));

        Assert.IsEmpty(await listed.Stash.All(), "the removal stands");
        CollectionAssert.AreEqual(new[] { $"Couldn't clear the clipboard: {HeldClipboard.Reason}." }, listed.Notifications.Shown.Select(notice => notice.Text).ToList(),
            "the card gone, the emptying named as its own act, the removal not named (G35)");
        Assert.AreSame(copied, listed.Clipboard.Holds);
    }

    [TestMethod]
    public async Task ARemovalFailingAfterThePictureAndAHeldClipboardAreEachNamed()
    {
        using var listed = await Taking();
        await Screenshot(listed);
        Microsoft.Data.Sqlite.SqliteConnection? holder = null;
        listed.Stash.RecycleBin.BeforeEach = _ => holder ??= listed.Stash.HoldStore();
        listed.Clipboard.FailNextEmpty = new Failure("Clear the clipboard", "the clipboard", HeldClipboard.Reason);

        try
        {
            OnControls(() => UndoOnTheCard(listed));
        }
        finally
        {
            holder?.Dispose();
        }

        var shown = listed.Notifications.Shown.Select(notice => notice.Text).ToList();
        Assert.IsTrue(shown.Any(text => text.StartsWith("Couldn't remove from the stash", StringComparison.Ordinal)), Shown(listed));
        Assert.Contains($"Couldn't clear the clipboard: {HeldClipboard.Reason}.", shown, "the emptying named as an act of its own, beside the removal's failure (G35)");
    }

    [TestMethod]
    public async Task TheEmptyingWaitsForACopysWriteAndJudgesAfterIt()
    {
        using var listed = await Taking();
        var other = await listed.Stash.ImportFixture("other", "still.png");
        var shot = await Screenshot(listed);
        var release = new TaskCompletionSource();
        listed.Clipboard.Hold = release.Task;

        OnControls(async () =>
        {
            var copying = listed.Handoff.Copy(null!, [other]);
            await Handing.Eventually(() => listed.Clipboard.Begun == 2);
            CaptureCardPresses.Click(CaptureCardPresses.Newest(listed.Notifications), "Undo");
            await Handing.Eventually(() => listed.Stash.RecycleBin.Asked.Contains(shot.FilePath));
            // The store's next call runs only once the removal has answered; the Undo then goes straight to the gate.
            await listed.Stash.Store!.All();
            await Task.Delay(100);

            Assert.IsEmpty(listed.Clipboard.EmptiesAsked, "the emptying takes the copies' one gate, never a write's moment (G8)");
            release.SetResult();
            await copying;
            await Handing.Eventually(() => !listed.CaptureNotification.Undoing);
        });

        CollectionAssert.AreEqual(new[] { shot.Id }, listed.Clipboard.EmptiesAsked);
        CollectionAssert.AreEqual(new[] { other.FilePath }, listed.Clipboard.Holds!.FilePaths.ToList(), "judged after the copy, whose write holds no removed capture");
    }

    [TestMethod]
    [DataRow("Move")]
    [DataRow("Export")]
    [DataRow("Import")]
    public async Task AWholeStashActAskedWhileTheEmptyingIsHeldWaitsForIt(string act)
    {
        using var listed = await Taking();
        await Screenshot(listed);
        var chosen = MovingStash.Chosen(listed.Stash);
        listed.Dialogs.PickFolderAnswer = () => Task.FromResult<string?>(chosen);
        var archive = listed.Stash.Outside("mine.zip");
        listed.Dialogs.SaveAnswer = () => Task.FromResult<string?>(archive);
        var exported = act == "Import" ? await ExportedStash.Of(listed.Stash.Outside("export.zip"), null, "one") : null;
        var held = new TaskCompletionSource();
        listed.Clipboard.HoldEmpty = held.Task;

        // The click alone on the controls' thread: the Undo then runs on, as a whole-stash act is asked beside it.
        ControlThread.Run(() => CaptureCardPresses.Click(CaptureCardPresses.Newest(listed.Notifications), "Undo"));
        await Handing.Eventually(() => listed.Clipboard.EmptiesAsked.Count == 1);
        Assert.IsEmpty(listed.Notifications.Shown, "the card has gone");
        // Offered once the capture has left, so the import's count at the press is the offer's (G36).
        var offered = exported is null ? null : await listed.Offered(exported);
        var acting = act switch
        {
            "Move" => listed.Moving.MoveTo(null!),
            "Export" => listed.Transferring.Export(null!),
            _ => offered!.ConfirmImport(),
        };
        await Handing.Eventually(() => new[] { listed.Moving.Busy, listed.Transferring.ExportLine, offered?.ImportLine }
            .Any(line => line?.StartsWith("Waiting for the Undo of a notification", StringComparison.Ordinal) == true));
        Assert.IsFalse(acting.IsCompleted, "nothing copied while the Undo's emptying has not answered");
        Assert.IsFalse(Directory.EnumerateFileSystemEntries(chosen).Any() || File.Exists(archive));
        Assert.IsFalse(listed.Listing.WholeStashActBegun, "neither copying nor replacing the stash has begun");

        held.SetResult();
        var done = await acting.WaitAsync(TimeSpan.FromSeconds(20));

        Assert.IsTrue(done.Succeeded, done.Failure?.Sentence);
        Assert.IsNull(listed.Clipboard.Holds, "emptied before the stash was copied or replaced");
    }

    [TestMethod]
    public async Task AfterTheEmptyingASavesRefreshWritesNothingBack()
    {
        using var listed = await Taking();
        var other = await listed.Stash.ImportFixture("other", "still.png");
        var shot = await Screenshot(listed);
        Assert.IsTrue((await listed.Handoff.Copy(null!, [shot, other])).Succeeded);
        OnControls(() => UndoOnTheCard(listed));

        Assert.IsTrue((await listed.Handoff.RefreshAfterSave(other)).Succeeded);

        Assert.IsEmpty(listed.Clipboard.WrittenAgain, "the emptied write is forgotten (G26)");
        Assert.IsNull(listed.Clipboard.Holds);
    }

    private static async Task<ListedStash> Taking(bool alsoCopy = true)
    {
        var listed = await ListedStash.Open();
        listed.Selection.Answer = new SelectedRegion(RecordingScreen.Primary, Region);
        if (!alsoCopy)
        {
            listed.Settings.Change(settings => settings with { AlsoCopyCapture = false });
        }

        return listed;
    }

    /// <summary>A screenshot through the act, its card raised above the tray; the item it added.</summary>
    private static async Task<StashItem> Screenshot(ListedStash listed)
    {
        var before = (await listed.Stash.All()).Select(item => item.Id).ToHashSet();
        await listed.Screenshot.Take();
        return (await listed.Stash.All()).Single(item => !before.Contains(item.Id));
    }

    /// <summary>The newest card's own Undo clicked, then waited for until its removal and emptying have answered.</summary>
    private static async Task UndoOnTheCard(ListedStash listed)
    {
        CaptureCardPresses.Click(CaptureCardPresses.Newest(listed.Notifications), "Undo");
        Assert.IsTrue(listed.CaptureNotification.Undoing, "running from the click");
        await Handing.Eventually(() => !listed.CaptureNotification.Undoing);
    }

    private static string Shown(ListedStash listed) => string.Join(" | ", listed.Notifications.Shown.Select(notice => notice.Text));

    private static void OnControls(Func<Task> act) => ControlThread.Run(() => ControlThread.FinishOnDispatcher(act));
}
