using System.Runtime.Versioning;
using ImageStash.Store;
using ImageStash.Tests.Support;
using static ImageStash.Tests.Support.EditedItems;
using static ImageStash.Tests.Support.MovingStash;

namespace ImageStash.Tests.Store;

/// <summary>
/// The folders a Move copies into and removes (State: Move, G28, G29): a chosen folder inside the old one, under its own
/// spelling or another, kept whole when the old is removed; a folder a settings field names emptied at start only while
/// it is a stash's copy, and never when it is, or holds, the stash itself under any name.
/// </summary>
[TestClass]
[SupportedOSPlatform("windows")]
public sealed class StashMoveFoldersTests
{
    private readonly List<Failure> failures = [];

    private SettingsKeeper Keeper(TestStash stash) => MovingStash.Keeper(stash, failures);

    [TestMethod]
    public async Task AFolderInsideTheOldOneTakesTheWholeStashAndTheOldKeepsOnlyIt()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        await stash.SaveEdit(await stash.ImportFixture("edited", "still.png"), BlackBoxAndBox);
        await stash.ImportFixture("plain", "photo.jpg");
        var chosen = Directory.CreateDirectory(Path.Combine(stash.Folder.Originals, "new")).FullName;
        var before = Tree(stash.Folder.Root).Where(entry => !entry.StartsWith("originals\\new", StringComparison.Ordinal)).ToList();

        var moved = await store.Move(Keeper(stash), chosen);

        Assert.IsTrue(moved.Succeeded, moved.Failure?.Sentence);
        CollectionAssert.AreEquivalent(before, Tree(chosen));
        CollectionAssert.AreEquivalent(new[] { "originals\\", "originals\\new\\" }, Tree(stash.Folder.Root).Where(entry => !entry.StartsWith("originals\\new\\", StringComparison.Ordinal) || entry == "originals\\new\\").ToList(), "the old root holds only the folders on the way to the new");
        Assert.AreEqual(chosen, Saved(stash).StashFolder);
    }

    [TestMethod]
    public async Task AFolderNamedThroughAJunctionIsTheFolderItNamesToMoveAndToStart()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        await stash.ImportFixture("plain", "photo.jpg");
        var alias = stash.Outside("alias");
        using var link = await Junction(alias, stash.Folder.Root);
        var chosen = Directory.CreateDirectory(Path.Combine(alias, "new")).FullName;
        var inside = Path.Combine(stash.Folder.Root, "new");
        var before = Tree(stash.Folder.Root).Where(entry => entry != "new\\").ToList();
        var keeper = Keeper(stash);

        var moved = await store.Move(keeper, chosen);

        Assert.IsTrue(moved.Succeeded, moved.Failure?.Sentence);
        CollectionAssert.AreEquivalent(before, Tree(inside), "the whole stash in the chosen folder, which the old one holds under another name");
        CollectionAssert.AreEquivalent(new[] { "new\\" }, Tree(stash.Folder.Root).Where(entry => !entry.StartsWith("new\\", StringComparison.Ordinal) || entry == "new\\").ToList(), "the old root keeps only it");
        stash.Close();
        var stashThere = Tree(inside);
        Assert.IsTrue((await keeper.ChangeNow(s => s with { MoveCopyInProgress = Path.Combine(alias, "new") })).Succeeded);

        var finished = await StashMove.FinishAtStart(keeper, inside, stash.Retry);

        Assert.AreEqual(new Failure(StashMove.FinishAction, "new", MoveReasons.TheStashItself), finished.Single(), "a field naming the stash under another name never empties it");
        CollectionAssert.AreEquivalent(stashThere, Tree(inside));
        Assert.IsNull(Saved(stash).MoveCopyInProgress);
    }

    [TestMethod]
    public async Task AtStartAChosenFolderAKillLeftHoldingOnlyAMovesMarkerIsEmptied()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        await stash.ImportFixture("cat", "still.png");
        stash.Close();
        // What a kill leaves while the Move looks for the chosen folder under another spelling: its marker, and the record.
        var chosen = Chosen(stash);
        File.WriteAllBytes(Path.Combine(chosen, ".imagestash-move-" + Guid.NewGuid().ToString("N")), []);
        var keeper = Keeper(stash);
        Assert.IsTrue((await keeper.ChangeNow(s => s with { MoveCopyInProgress = chosen })).Succeeded);

        var finished = await StashMove.FinishAtStart(keeper, stash.Folder.Root, stash.Retry);

        Assert.IsEmpty(finished, string.Join("; ", finished.Select(failure => failure.Sentence)));
        Assert.IsEmpty(Directory.GetFileSystemEntries(chosen), "nothing of the Move left in the person's folder (G28)");
        Assert.IsNull(Saved(stash).MoveCopyInProgress);
        Assert.IsTrue((await (await stash.OpenCleanly()).Move(keeper, chosen)).Succeeded, "and the folder taken as empty by the next Move (G30)");
        Assert.IsFalse(Directory.EnumerateFiles(chosen, ".imagestash-move-*", SearchOption.AllDirectories).Any(), "no marker in the moved stash");
    }

    [TestMethod]
    public async Task AtStartAPartialCopyIsEmptiedAndAFolderThatIsNotAStashIsLeftAsItIs()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        await stash.ImportFixture("plain", "photo.jpg");
        await stash.ImportFixture("cat", "still.png");
        stash.Close();
        var partial = Chosen(stash, "partial");
        File.Copy(stash.Folder.StoreFile, Path.Combine(partial, StashFolder.StoreFileName));
        File.Copy(Directory.GetFiles(stash.Folder.Root, "plain.*").Single(), Path.Combine(partial, "plain.jpg"));
        Directory.CreateDirectory(Path.Combine(partial, "originals"));
        var stranger = Chosen(stash, "stranger");
        File.WriteAllText(Path.Combine(stranger, "notes.txt"), "mine");
        var before = Tree(stash.Folder.Root);

        var keeper = Keeper(stash);
        Assert.IsTrue((await keeper.ChangeNow(s => s with { MoveCopyInProgress = partial })).Succeeded);
        Assert.IsEmpty(await StashMove.FinishAtStart(keeper, stash.Folder.Root, stash.Retry));
        Assert.IsEmpty(Directory.GetFileSystemEntries(partial), "the partial copy deleted, the chosen folder kept");
        Assert.IsNull(Saved(stash).MoveCopyInProgress);

        foreach (var (named, reason) in new[] { (stranger, MoveReasons.NotAStash), (stash.Folder.Root, MoveReasons.TheStashItself) })
        {
            Assert.IsTrue((await keeper.ChangeNow(s => s with { MoveCopyInProgress = named })).Succeeded);
            Assert.AreEqual(new Failure(StashMove.FinishAction, Path.GetFileName(named), reason), (await StashMove.FinishAtStart(keeper, stash.Folder.Root, stash.Retry)).Single());
            Assert.IsNull(Saved(stash).MoveCopyInProgress, "the field cleared");
        }

        Assert.AreEqual("notes.txt", Path.GetFileName(Directory.GetFiles(stranger).Single()), "nothing deleted");
        CollectionAssert.AreEquivalent(before, Tree(stash.Folder.Root));
        Assert.IsNull(Saved(stash).PreviousStashFolderPendingRemoval);
    }
}
