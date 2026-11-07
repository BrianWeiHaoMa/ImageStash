using ImageStash.Files;
using ImageStash.Media;
using ImageStash.Store;
using ImageStash.Tests.Support;
using Microsoft.Data.Sqlite;

namespace ImageStash.Tests.Store;

/// <summary>
/// The folder Store keeps the Open with… copies in (Composition: Store, G30, R8): made with its marker at every open,
/// outside the stash folder; a file in its place named while the stash still opens (G35); nothing made and nothing
/// touched without a root; a folder no session names cleared at the next open, so a refresh of a copy that a kill or a
/// held store interrupted leaves one session whose recorded hash describes the copy its folder holds (G28).
/// </summary>
[TestClass]
public sealed class OpenWithRootTests
{
    [TestMethod]
    public async Task EveryOpenMakesTheRootAndItsMarkerWhereTheyAreMissing()
    {
        using var stash = new TestStash();
        var root = stash.OpenWithRoot!;
        var marker = Path.Combine(root, OpenWithRoot.MarkerName);
        Assert.IsFalse(Directory.Exists(root));

        await stash.OpenCleanly();
        CollectionAssert.AreEqual(new[] { OpenWithRoot.MarkerName }, OpenedWith.RootFiles(stash));
        Assert.IsEmpty(OpenedWith.Folders(stash));
        Assert.IsFalse(root.StartsWith(stash.Folder.Root, StringComparison.OrdinalIgnoreCase), "outside the stash folder (E2)");
        var words = File.ReadAllText(marker);
        StringAssert.Contains(words, "Open with");

        stash.Close();
        File.Delete(marker);
        await stash.OpenCleanly();
        Assert.AreEqual(words, File.ReadAllText(marker), "the marker made again");

        stash.Close();
        Directory.Delete(root, recursive: true);
        await stash.OpenCleanly();
        CollectionAssert.AreEqual(new[] { OpenWithRoot.MarkerName }, OpenedWith.RootFiles(stash), "the root and its marker made again");
    }

    [TestMethod]
    public async Task AFileWhereTheRootShouldBeIsNamedWhileTheStashOpensAndBeginIsRefused()
    {
        using var stash = new TestStash();
        File.WriteAllText(stash.OpenWithRoot!, "not a folder");

        var opening = await stash.Open();

        Assert.IsNotNull(opening.Store, string.Join("; ", opening.Failures));
        Assert.AreEqual(new Failure(OpenWithRoot.PrepareAction, stash.OpenWithRoot!, "a file is where the folder should be"), opening.Failures.Single());
        var cat = await stash.ImportFixture("cat", "still.png");
        var begun = await opening.Store.BeginOpenWith(cat);
        Assert.AreEqual(new Failure(OpenWithBegin.Action, "cat", "the folder for its copy cannot be made: a file is where the folder should be"), begun.Failure);
        Assert.IsNull(OpenedWith.Record(stash, cat.Id));
        TestStash.AssertSameItem(cat, (await stash.All()).Single());
    }

    [TestMethod]
    public async Task AStashOpenedWithoutARootMakesNoFolderLeavesItsSessionsAndRefusesBegin()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var cat = await stash.ImportFixture("cat", "still.png");
        var dog = await stash.ImportFixture("dog", "photo.jpg");
        var copy = await OpenedWith.Begin(stash, cat);
        var recorded = OpenedWith.Record(stash, cat.Id);
        var root = stash.OpenWithRoot!;
        stash.Close();
        Directory.Delete(root, recursive: true);
        stash.OpenWithRoot = null;

        var store = await stash.OpenCleanly();
        var begun = await store.BeginOpenWith(dog);
        var classified = await store.ClassifyOpenWith(cat.Id);
        var sessions = await store.OpenWithSessions();

        Assert.IsFalse(Directory.Exists(root), "no root made");
        Assert.IsFalse(Directory.EnumerateDirectories(stash.Outside(""), "open-with", SearchOption.AllDirectories).Any(), "nor any other");
        Assert.AreEqual(new Failure(OpenWithBegin.Action, "dog", OpenWithCopies.NoRoot), begun.Failure);
        Assert.AreEqual(OpenWithCopies.NoRoot, classified.Failure?.Reason);
        Assert.IsTrue(sessions.Succeeded, sessions.Failure?.ToString());
        Assert.IsEmpty(sessions.Value!.Sessions);
        Assert.AreEqual(recorded, OpenedWith.Record(stash, cat.Id), "a recorded session is left as it was");
        Assert.IsNull(OpenedWith.Record(stash, dog.Id));
        Assert.IsFalse(File.Exists(copy));
    }

    [TestMethod]
    public async Task AFolderNoSessionNamesIsClearedAtOpenWhileASessionsFolderTheMarkerAndOtherFilesStay()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var cat = await stash.ImportFixture("cat", "still.png");
        var copy = await OpenedWith.Begin(stash, cat);
        stash.Close();
        var orphan = Path.Combine(stash.OpenWithRoot!, "orphan");
        Directory.CreateDirectory(Path.Combine(orphan, "nested"));
        File.WriteAllBytes(Path.Combine(orphan, "cat.png"), MediaFixtures.Bytes("still.png"));
        File.WriteAllText(Path.Combine(stash.OpenWithRoot!, "notes.txt"), "a file someone left");

        await stash.OpenCleanly();

        CollectionAssert.AreEqual(new[] { OpenedWith.FolderOf(copy) }, OpenedWith.Folders(stash));
        CollectionAssert.AreEquivalent(new[] { OpenWithRoot.MarkerName, "notes.txt" }, OpenedWith.RootFiles(stash));
        CollectionAssert.AreEqual(MediaFixtures.Bytes("still.png"), File.ReadAllBytes(copy));
    }

    [TestMethod]
    public async Task ARefreshKilledBeforeItsRecordLeavesTheEarlierSessionWithItsCopy()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var cat = await stash.ImportFixture("cat", "still.png");
        var copy = await OpenedWith.Begin(stash, cat);
        var saved = await stash.SaveEdit(cat, EditedItems.BlackBoxAndBox);
        Assert.IsTrue(saved.Succeeded, saved.Failure?.ToString());
        var recorded = OpenedWith.Record(stash, cat.Id);
        stash.Close();
        // The fresh copy a refresh had written when the kill came, its session not yet recorded.
        var fresh = Path.Combine(stash.OpenWithRoot!, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fresh);
        File.Copy(saved.Value!.FilePath, Path.Combine(fresh, "cat.png"));

        await stash.OpenCleanly();

        Assert.AreEqual(recorded, OpenedWith.Record(stash, cat.Id));
        CollectionAssert.AreEqual(new[] { OpenedWith.FolderOf(copy) }, OpenedWith.Folders(stash), "the fresh folder cleared");
        Assert.AreEqual(OpenWithCopy.Unchanged, await OpenedWith.Classify(stash, cat.Id), "the copy is the bytes its session records");
    }

    [TestMethod]
    public async Task ARefreshKilledAfterItsRecordLeavesTheFreshSessionWithItsCopy()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var cat = await stash.ImportFixture("cat", "still.png");
        var copy = await OpenedWith.Begin(stash, cat);
        var saved = await stash.SaveEdit(cat, EditedItems.BlackBoxAndBox);
        Assert.IsTrue(saved.Succeeded, saved.Failure?.ToString());
        stash.Close();
        // The fresh copy recorded in one transaction, the kill coming before the earlier folder was deleted.
        var fresh = Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(Path.Combine(stash.OpenWithRoot!, fresh));
        File.Copy(saved.Value!.FilePath, Path.Combine(stash.OpenWithRoot!, fresh, "cat.png"));
        var session = new OpenWithRecord(fresh, "cat.png", EditedItems.Hash(saved.Value.FilePath), Awaiting: false);
        stash.Sql("UPDATE items SET open_with_session = $session WHERE id = $id", ("$session", session.Encode()), ("$id", cat.Id));

        await stash.OpenCleanly();

        Assert.AreEqual(session, OpenedWith.Record(stash, cat.Id));
        CollectionAssert.AreEqual(new[] { fresh }, OpenedWith.Folders(stash), "the earlier folder cleared");
        Assert.IsFalse(File.Exists(copy));
        Assert.AreEqual(OpenWithCopy.Unchanged, await OpenedWith.Classify(stash, cat.Id), "the copy is the bytes its session records");
    }

    [TestMethod]
    public async Task ARefreshWhoseRecordTheStoreRefusesLeavesTheEarlierSessionAndNoFreshFolder()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var cat = await stash.ImportFixture("cat", "still.png");
        var copy = await OpenedWith.Begin(stash, cat);
        var recorded = OpenedWith.Record(stash, cat.Id);
        SqliteConnection? holder = null;
        // The store is held from the moment the fresh copy is read until the refresh has answered.
        stash.Reader = (path, name, retry) =>
        {
            if (path.StartsWith(stash.OpenWithRoot!, StringComparison.OrdinalIgnoreCase))
            {
                holder ??= stash.HoldStore();
            }

            return MediaDecoder.ReadPicture(path, name, retry);
        };

        var begun = await store.BeginOpenWith(cat);
        holder?.Dispose();

        Assert.IsNotNull(holder, "the fresh copy was read");
        Assert.AreEqual(HeldFileRetry.HeldReason, begun.Failure?.Reason, begun.Failure?.ToString());
        Assert.AreEqual(recorded, OpenedWith.Record(stash, cat.Id));
        CollectionAssert.AreEqual(new[] { OpenedWith.FolderOf(copy) }, OpenedWith.Folders(stash), "the fresh folder deleted");
        CollectionAssert.AreEqual(MediaFixtures.Bytes("still.png"), File.ReadAllBytes(copy));
        Assert.AreEqual(OpenWithCopy.Unchanged, await OpenedWith.Classify(stash, cat.Id));
    }
}
