using ImageStash.Store;
using ImageStash.Tests.Support;

namespace ImageStash.Tests.Store;

/// <summary>.work\ (State: Files, R2): paths the store allocates, staged copies that leave the source alone (G11, R3), discards, and G35's held source.</summary>
[TestClass]
public sealed class WorkFolderTests
{
    [TestMethod]
    public async Task AllocatedPathsLieInsideTheWorkFolderCarryTheExtensionAndDiffer()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();

        var first = store.AllocateWorkPath(".GIF");
        var second = store.AllocateWorkPath(".gif");

        Assert.AreEqual(stash.Folder.Work, Path.GetDirectoryName(first.Value));
        Assert.AreEqual(stash.Folder.Work, Path.GetDirectoryName(second.Value));
        Assert.AreEqual(".gif", Path.GetExtension(first.Value));
        Assert.AreEqual(".gif", Path.GetExtension(second.Value));
        Assert.AreNotEqual(first.Value, second.Value);
    }

    [TestMethod]
    public async Task AnExtensionThatCouldLeaveTheWorkFolderIsRefused()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();

        Assert.IsFalse(store.AllocateWorkPath(@".png\..\..\x").Succeeded);
        Assert.IsFalse(store.AllocateWorkPath("png").Succeeded);
    }

    [TestMethod]
    public async Task StagingCopiesTheSourceAndLeavesItAsItWas()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var bytes = TestStash.Bytes(1, 100_000);
        var source = stash.Source("reaction.PNG", bytes);
        var written = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(source, written);

        var staged = await store.Stage(source);

        Assert.IsTrue(staged.Succeeded, staged.Failure?.ToString());
        Assert.AreEqual(stash.Folder.Work, Path.GetDirectoryName(staged.Value));
        Assert.AreEqual(".png", Path.GetExtension(staged.Value));
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(staged.Value!));
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(source));
        Assert.AreEqual(written, File.GetLastWriteTimeUtc(source));
    }

    [TestMethod]
    public async Task AReadOnlySourceIsStagedAsACopyTheStoreCanDiscard()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var bytes = TestStash.Bytes(6);
        var source = stash.Source("from a camera.png", bytes);
        File.SetAttributes(source, FileAttributes.ReadOnly);
        try
        {
            var staged = await store.Stage(source);
            Assert.IsTrue(staged.Succeeded, staged.Failure?.ToString());

            var discarded = await store.Discard(staged.Value!);

            Assert.IsTrue(discarded.Succeeded, discarded.Failure?.ToString());
            Assert.IsEmpty(stash.WorkFiles());
            Assert.AreEqual(FileAttributes.ReadOnly, File.GetAttributes(source) & FileAttributes.ReadOnly);
            CollectionAssert.AreEqual(bytes, File.ReadAllBytes(source));
        }
        finally
        {
            File.SetAttributes(source, FileAttributes.Normal);
        }
    }

    [TestMethod]
    public async Task AReadOnlySourceAlreadyInTheStashLeavesNothingBehind()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var bytes = TestStash.Bytes(7);
        var existing = await stash.Import("first", bytes);
        var source = stash.Source("again.png", bytes);
        File.SetAttributes(source, FileAttributes.ReadOnly);
        try
        {
            var staged = await store.Stage(source);
            Assert.IsTrue(staged.Succeeded, staged.Failure?.ToString());

            var committed = await store.CommitImport(TestStash.Candidate(staged.Value!, "again", bytes));

            Assert.IsTrue(committed.Succeeded, committed.Failure?.ToString());
            Assert.IsTrue(committed.Value!.AlreadyInStash);
            Assert.AreEqual(existing.Id, committed.Value.Item.Id);
            Assert.IsEmpty(stash.WorkFiles());
        }
        finally
        {
            File.SetAttributes(source, FileAttributes.Normal);
        }
    }

    [TestMethod]
    public async Task ASourceHeldPastTheRetryIsNamedAndLeavesNothingStaged()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var source = stash.Source("held.gif", TestStash.Bytes(2));

        Outcome<string> staged;
        using (new FileStream(source, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            staged = await store.Stage(source);
        }

        Assert.IsFalse(staged.Succeeded);
        Assert.AreEqual("held.gif", staged.Failure.Subject);
        Assert.IsEmpty(stash.WorkFiles());
    }

    [TestMethod]
    public async Task ASourceReleasedWithinTheRetryIsStaged()
    {
        using var stash = new TestStash { Retry = TimeSpan.FromSeconds(4) };
        var store = await stash.OpenCleanly();
        var bytes = TestStash.Bytes(3);
        var source = stash.Source("briefly.jpg", bytes);

        var hold = new FileStream(source, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var staging = store.Stage(source);
        await Task.Delay(1000);
        hold.Dispose();
        var staged = await staging;

        Assert.IsTrue(staged.Succeeded, staged.Failure?.ToString());
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(staged.Value!));
    }

    [TestMethod]
    public async Task DiscardingAStagedFileRemovesOnlyIt()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        await stash.Import("kept");
        var staged = await stash.Stage("dropped.png", TestStash.Bytes(4));
        var other = await stash.Stage("other.png", TestStash.Bytes(5));

        var discarded = await store.Discard(staged);

        Assert.IsTrue(discarded.Succeeded, discarded.Failure?.ToString());
        CollectionAssert.AreEquivalent(new[] { Path.GetFileName(other) }, stash.WorkFiles());
        CollectionAssert.AreEquivalent(new[] { "kept.png" }, stash.RootFiles());
    }

    [TestMethod]
    public async Task AtStartAWorkFileNoRowRefersToGoesThoughAWaitingRenamesFileHasItsName()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var item = await stash.Import("cat");
        stash.Close();
        stash.MakeEdited(item, TestStash.Bytes(8));
        var store = await stash.OpenCleanly();
        // No row refers to it, as a kill before an import's insert leaves one; its name is the edited item's files'.
        var unreferenced = stash.Folder.InWork(item.FileName);
        File.WriteAllBytes(unreferenced, TestStash.Bytes(9));
        using var original = TestStash.Hold(stash.Folder.InOriginals(item.FileName));
        var renamed = await store.Rename(item, "kitten");
        Assert.IsFalse(renamed.Succeeded, "the held original keeps the rename waiting");
        using var finished = TestStash.Hold(renamed.Value!.FilePath);

        var opening = await stash.Reopen();

        Assert.IsNotNull(opening.Store);
        Assert.IsTrue(File.Exists(renamed.Value.FilePath), "the rename still waits, neither finished nor undone");
        Assert.IsFalse(File.Exists(unreferenced), "a rename's files are in the root and originals\\, so this file is no row's (State: Files)");
    }

    [TestMethod]
    public async Task AtStartAPreviousOriginalAnAdoptionHoldsIsKeptWhileItsRecordStands()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var cat = await stash.ImportFixture("cat", "still.png");
        await OpenedWith.AwaitingRewrite(stash, cat, MediaFixtures.DistinctPng(5, 12, 10));
        await OpenedWith.Adopt(stash, cat.Id);
        var record = OpenedWith.Undo(stash, cat.Id)!;
        stash.Close();
        stash.RecycleBin.Refusing = _ => true;

        var opening = await stash.Open();

        CollectionAssert.AreEqual(new[] { new Failure(AdoptionUndo.ExpireAction, "cat", "the test Recycle Bin refuses it") }, opening.Failures.ToList(), "its expiry refused");
        Assert.AreEqual(record, OpenedWith.Undo(stash, cat.Id));
        CollectionAssert.AreEqual(new[] { record.HeldCopy }, stash.WorkFiles(), "the held original is no stray file, though no step names it");
        CollectionAssert.AreEqual(MediaFixtures.Bytes("still.png"), File.ReadAllBytes(stash.Folder.InWork(record.HeldCopy!)));
    }

    [TestMethod]
    public async Task DiscardRefusesAFileOutsideTheWorkFolder()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var item = await stash.Import("kept");

        var discarded = await store.Discard(item.FilePath);

        Assert.IsFalse(discarded.Succeeded);
        Assert.IsTrue(File.Exists(item.FilePath));
    }
}
