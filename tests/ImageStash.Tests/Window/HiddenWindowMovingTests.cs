using Microsoft.Data.Sqlite;
using ImageStash.Store;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// Move…, Export stash… and Import stash… with a notice whose Undo stands in a stash window that does not show, whose
/// clock therefore does not run (G16, State: Move, G30, G36): none waits for ever; the notice's Undo ends at the wait,
/// as at its time, what its end runs (a Delete's finish) is started and waited for, and only once it has answered does
/// the copy begin, a handoff still taken until then (G8); with the window showing the wait holds as before, and a window
/// that hides while the wait runs lets it go on. A notice that names a problem keeps its words when its Undo ends, by the
/// wait or by its time, and is no longer waited for.
/// </summary>
[TestClass]
public sealed class HiddenWindowMovingTests
{
    [TestMethod]
    [DataRow(false, false, DisplayName = "hidden")]
    [DataRow(true, true, DisplayName = "minimised")]
    public async Task AMoveAskedWithADeletesNoticeInAWindowNotShowingEndsItsUndoFinishesTheDeleteAndThenMoves(bool visible, bool minimised)
    {
        using var listed = await ListedStash.Open();
        var cat = await listed.Stash.Import("cat");
        var dog = await listed.Stash.Import("dog");
        await listed.Listing.ShowScope(Scope.All);
        Assert.IsTrue((await listed.Deletion.Delete([cat])).Succeeded);
        var notice = listed.Notices.Shown.Single();
        listed.WindowShowing.Seen(visible, minimised);
        var chosen = Chose(listed);

        var moved = await listed.Moving.MoveTo(null!).WaitAsync(TimeSpan.FromSeconds(20));

        Assert.IsTrue(moved.Succeeded, moved.Failure?.Sentence);
        Assert.IsFalse(listed.Notices.OffersUndo(notice), "no Undo stands across the copy");
        Assert.DoesNotContain(notice, listed.Notices.Shown, "its time ended, as at its time");
        CollectionAssert.AreEqual(new[] { dog.Id }, (await listed.Stash.All()).Select(item => item.Id).ToList());
        Assert.AreEqual(chosen, listed.Settings.Current.StashFolder);
        Assert.IsEmpty(WorkFiles(chosen), "the Delete finished before the copy: no held copy went to the new folder");
        Assert.AreEqual(0L, MarkedDeleted(chosen), "and no row marked deleted");
    }

    [TestMethod]
    public async Task AMoveWaitingOnANoticeWhileTheWindowShowsGoesOnOnceTheWindowHides()
    {
        using var listed = await ListedStash.Open();
        var cat = await listed.Stash.Import("cat");
        await listed.Stash.Import("dog");
        await listed.Listing.ShowScope(Scope.All);
        Assert.IsTrue((await listed.Deletion.Delete([cat])).Succeeded);
        var chosen = Chose(listed);

        var moving = listed.Moving.MoveTo(null!);
        await Handing.Eventually(() => listed.Moving.Busy == "Waiting for the Undo of a notice before moving your stash…");
        Assert.IsFalse(moving.IsCompleted, "with the window showing the wait holds while the Undo stands");
        listed.Turns.Act(() => listed.WindowShowing.Seen(visible: false, minimised: false));
        var moved = await moving.WaitAsync(TimeSpan.FromSeconds(20));

        Assert.IsTrue(moved.Succeeded, moved.Failure?.Sentence);
        Assert.IsEmpty(listed.Notices.Shown.Where(listed.Notices.OffersUndo).ToList());
        Assert.HasCount(1, await listed.Stash.All());
        Assert.AreEqual(chosen, listed.Settings.Current.StashFolder);
    }

    [TestMethod]
    public async Task TheCopyAndItsRefusalOfHandoffsBeginOnlyOnceTheDeletesFinishHasAnswered()
    {
        using var listed = await ListedStash.Open(storeRetry: TimeSpan.FromSeconds(10));
        var cat = await listed.Stash.Import("cat");
        var dog = await listed.Stash.Import("dog", MediaFixtures.Bytes("still.png"));
        await listed.Listing.ShowScope(Scope.All);
        Assert.IsTrue((await listed.Deletion.Delete([cat])).Succeeded);
        listed.WindowShowing.Seen(visible: false, minimised: false);
        var chosen = Chose(listed);

        Task<Outcome> moving;
        Task<Outcome> copying;
        using (HoldTheHeldCopy(listed))
        {
            moving = listed.Moving.MoveTo(null!);
            await Handing.Eventually(() => listed.Moving.Busy == "Waiting for a notice's change to finish before moving your stash…");
            Assert.IsEmpty(listed.Notices.Shown, "the Undo ended at the wait");
            Assert.IsFalse(listed.Listing.WholeStashActBegun, "the copy has not begun");
            // Its use is recorded once the store's thread is free of the finish.
            copying = listed.Turns.Act(() => listed.Handoff.Copy(null!, [dog]));
            Assert.IsFalse(Directory.EnumerateFileSystemEntries(chosen).Any(), "nothing copied yet");
        }

        var copied = await copying.WaitAsync(TimeSpan.FromSeconds(20));
        var moved = await moving.WaitAsync(TimeSpan.FromSeconds(20));

        Assert.IsTrue(copied.Succeeded, "a handoff asked while the finish runs is taken (G8): " + copied.Failure?.Sentence);
        Assert.IsTrue(moved.Succeeded, moved.Failure?.Sentence);
        Assert.IsEmpty(listed.Notices.Shown.Where(notice => notice.Text.Contains("failed")).ToList(), "the finish answered once the file was let go");
        CollectionAssert.AreEqual(new[] { dog.Id }, (await listed.Stash.All()).Select(item => item.Id).ToList());
        Assert.IsEmpty(WorkFiles(chosen), "the held copy deleted by the finish, not carried to the new folder");
        Assert.AreEqual(0L, MarkedDeleted(chosen));
    }

    [TestMethod]
    public async Task AnExportAskedWithADeletesNoticeInTheHiddenWindowFinishesTheDeleteAndExportsTheRest()
    {
        using var listed = await ListedStash.Open(storeRetry: TimeSpan.FromSeconds(10));
        var cat = await listed.Stash.Import("cat");
        await listed.Stash.Import("dog");
        var archive = listed.Stash.Outside("mine.zip");
        listed.Dialogs.SaveAnswer = () => Task.FromResult<string?>(archive);
        Assert.IsTrue((await listed.Deletion.Delete([cat])).Succeeded);
        listed.WindowShowing.Seen(visible: false, minimised: false);

        Task<Outcome> exporting;
        using (HoldTheHeldCopy(listed))
        {
            exporting = listed.Transferring.Export(null!);
            await Handing.Eventually(() => listed.Transferring.ExportLine == "Waiting for a notice's change to finish before exporting your stash…");
            Assert.IsFalse(listed.Listing.WholeStashActBegun, "the export's copy has not begun");
            Assert.IsFalse(File.Exists(archive));
        }

        var exported = await exporting.WaitAsync(TimeSpan.FromSeconds(20));

        Assert.IsTrue(exported.Succeeded, exported.Failure?.Sentence);
        Assert.IsEmpty(listed.Notices.Shown.Where(listed.Notices.OffersUndo).ToList());
        Assert.AreEqual(1, StashImport.Summary(archive, TestStash.ShortRetry).Value!.Items, "cat's Delete finished before the export");
    }

    [TestMethod]
    public async Task AnImportOfAStashConfirmedWithADeletesNoticeInTheHiddenWindowFinishesTheDeleteAndReplacesTheStash()
    {
        using var listed = await ListedStash.Open(storeRetry: TimeSpan.FromSeconds(10));
        var cat = await listed.Stash.Import("cat");
        await listed.Stash.Import("dog");
        var archive = await ExportedStash.Of(listed.Stash.Outside("export.zip"), null, ["one"]);
        Assert.IsTrue((await listed.Deletion.Delete([cat])).Succeeded);
        var page = await listed.Offered(archive);
        listed.WindowShowing.Seen(visible: false, minimised: false);

        Task<Outcome> importing;
        using (HoldTheHeldCopy(listed))
        {
            importing = page.ConfirmImport();
            await Handing.Eventually(() => page.ImportLine == "Waiting for a notice's change to finish before importing export.zip…");
            Assert.IsFalse(listed.Listing.WholeStashActBegun, "the import's replacement has not begun");
        }

        var imported = await importing.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.IsTrue(imported.Succeeded, imported.Failure?.Sentence);
        Assert.IsEmpty(listed.Notices.Shown.Where(listed.Notices.OffersUndo).ToList());
        await listed.Listing.ShowScope(Scope.All);
        CollectionAssert.AreEqual(new[] { "one" }, listed.Listing.Items.Select(item => item.Name).ToList());
    }

    [TestMethod]
    public async Task AnExportRunWithAnImportsNoticeNamingASkippedFileInTheHiddenWindowLeavesItsWordsWithoutItsUndo()
    {
        using var listed = await ListedStash.Open();
        await listed.Import.Drop(ListedStash.Contents([MediaFixtures.Path("still.png"), listed.Stash.Source("clip.mp4", TestStash.Bytes(1))]));
        var notice = listed.Notices.Shown.Single();
        listed.WindowShowing.Seen(visible: false, minimised: false);
        listed.Dialogs.SaveAnswer = () => Task.FromResult<string?>(listed.Stash.Outside("mine.zip"));

        var exported = await listed.Transferring.Export(null!).WaitAsync(TimeSpan.FromSeconds(20));
        listed.WindowShowing.Seen(visible: true, minimised: false);
        listed.Stash.Clock.Now += TimeSpan.FromMinutes(1);
        listed.Notices.Sweep();

        Assert.IsTrue(exported.Succeeded, exported.Failure?.Sentence);
        Assert.Contains(notice, listed.Notices.Shown, "at the next summon its words are there");
        Assert.IsFalse(listed.Notices.OffersUndo(notice), "without its Undo, which the export ended");
        Assert.HasCount(1, await listed.Stash.All(), "and the export removed nothing");
    }

    [TestMethod]
    public async Task AMoveWithTheWindowShowingWaitsNotForANoticeThatStaysOnceItsUndoHasEnded()
    {
        using var listed = await ListedStash.Open();
        await listed.Import.Drop(ListedStash.Contents([MediaFixtures.Path("still.png"), listed.Stash.Source("clip.mp4", TestStash.Bytes(1))]));
        var notice = listed.Notices.Shown.Single();
        listed.Stash.Clock.Now += Notices.LastingFor(notice.Words);
        listed.Notices.Sweep();
        var chosen = Chose(listed);

        var moved = await listed.Moving.MoveTo(null!).WaitAsync(TimeSpan.FromSeconds(20));

        Assert.IsTrue(moved.Succeeded, moved.Failure?.Sentence);
        Assert.AreEqual(chosen, listed.Settings.Current.StashFolder);
        Assert.Contains(notice, listed.Notices.Shown, "its words stay past the Move");
    }

    /// <summary>
    /// The one held copy a Delete left in the stash's <c>.work</c> folder, held as a scanner might hold it (E3), so the
    /// Delete's finish retries, still running, until it is let go.
    /// </summary>
    private static FileStream HoldTheHeldCopy(ListedStash listed) =>
        new(WorkFiles(listed.Stash.Folder.Root).Single(), FileMode.Open, FileAccess.Read, FileShare.None);

    private static string Chose(ListedStash listed)
    {
        var chosen = MovingStash.Chosen(listed.Stash);
        listed.Dialogs.PickFolderAnswer = () => Task.FromResult<string?>(chosen);
        return chosen;
    }

    /// <summary>The files in <paramref name="folder"/>'s <c>.work</c> folder, where a Delete's held copies wait.</summary>
    private static List<string> WorkFiles(string folder)
    {
        var work = new StashFolder(folder).InWork("");
        return Directory.Exists(work) ? Directory.GetFiles(work, "*", SearchOption.AllDirectories).ToList() : [];
    }

    /// <summary>The rows marked deleted in the store of the stash at <paramref name="folder"/>, read between its calls.</summary>
    private static long MarkedDeleted(string folder)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = new StashFolder(folder).StoreFile, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM items WHERE deleted = 1";
        return (long)command.ExecuteScalar()!;
    }
}
