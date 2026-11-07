using ImageStash.Files;
using ImageStash.Store;
using ImageStash.Tests.Support;

namespace ImageStash.Tests.Store;

/// <summary>P1 and State: File names: the item's name made safe, " 2", " 3" … while taken, ignoring case, reserved names included.</summary>
[TestClass]
public sealed class ItemFileNamesTests
{
    [TestMethod]
    public async Task ANameTakenOnDiskInAnotherCaseGetsACounter()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        await stash.Import("Cat");

        var second = await stash.Import("cat");

        Assert.AreEqual("cat 2.png", second.FileName);
        Assert.AreEqual("cat", second.Name);
        Assert.IsTrue(File.Exists(stash.Folder.InRoot("Cat.png")));
    }

    [TestMethod]
    public async Task ANameADeletedMarkedRowReservesIsSkipped()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        await stash.Import("cat");
        var marked = await stash.Import("cat");
        Assert.AreEqual("cat 2.png", marked.FileName);
        stash.RecycleBin.Refusing = _ => true;
        Assert.IsFalse((await store.Remove([marked.Id])).Single().Succeeded, "the row should stay marked");
        File.Delete(marked.FilePath);

        var next = await stash.Import("cat");

        Assert.AreEqual("cat 3.png", next.FileName);
    }

    [TestMethod]
    public async Task OnlyTheWholeNameWithItsExtensionIsCompared()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        await stash.Import("cat", extension: ".png");

        var gif = await stash.Import("cat", extension: ".gif");

        Assert.AreEqual("cat.gif", gif.FileName);
    }

    [TestMethod]
    public async Task CharactersWindowsForbidsAreReplaced()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();

        var item = await stash.Import("a/b:c?");

        Assert.AreEqual("a/b:c?", item.Name);
        Assert.AreEqual("a_b_c_.png", item.FileName);
        Assert.IsTrue(File.Exists(item.FilePath));
    }

    [TestMethod]
    public async Task ADeviceNameIsNotUsedAsAFileName()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();

        var con = await stash.Import("CON");
        var nul = await stash.Import("nul.backup");

        Assert.AreNotEqual("CON.png", con.FileName);
        Assert.AreEqual("CON_.png", con.FileName);
        Assert.AreEqual("nul_.backup.png", nul.FileName);
        Assert.IsTrue(File.Exists(con.FilePath));
    }

    [TestMethod]
    public async Task AnEmptyNameGetsTheUnnamedFileName()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();

        var item = await stash.Import("");

        Assert.AreEqual("", item.Name);
        Assert.AreEqual(ItemFileNames.Unnamed + ".png", item.FileName);
    }

    [TestMethod]
    public async Task ANameThatIsAnotherFilesShortNameIsTakenSoASaveNeverReplacesThatFile()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var other = await stash.ImportFixture("long reaction picture", "still.png");
        var before = EditedItems.Hash(other.FilePath);
        RequireShortName(stash.Folder.InRoot("LONGRE~1.png"));
        var cat = await stash.ImportFixture("LONGRE~1", "still.webp");

        var saved = await stash.SaveEdit(cat, EditedItems.BlackBoxAndBox);

        Assert.IsTrue(saved.Succeeded, saved.Failure?.Sentence);
        Assert.AreEqual("LONGRE~1 2.png", saved.Value!.FileName, "LONGRE~1.png is the other item's file under its short name");
        Assert.AreEqual(before, EditedItems.Hash(other.FilePath), "which the Save's render never replaces (G29)");
        CollectionAssert.AreEquivalent(new[] { "LONGRE~1 2.png", "long reaction picture.png" }, stash.RootFiles());
    }

    [TestMethod]
    public async Task ANameThatIsAnotherOriginalsShortNameIsTakenInOriginalsSoAFirstSaveCompletesBesideIt()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        // The first file in the root takes the short name LONGRE~1.PNG there; renamed away, only originals\ has one.
        var first = await stash.Import("long reaction first", MediaFixtures.DistinctPng(10, 8, 6));
        var edited = await stash.Import("long reaction second", MediaFixtures.DistinctPng(20, 8, 6));
        Assert.IsTrue((await stash.SaveEdit(edited, EditedItems.Cropped)).Succeeded);
        Assert.IsTrue((await store.Rename(first, "dog")).Succeeded);
        var editedOriginal = stash.Folder.InOriginals("long reaction second.png");
        var before = EditedItems.Hash(editedOriginal);
        RequireShortName(stash.Folder.InOriginals("LONGRE~1.png"));
        Assert.IsFalse(File.Exists(stash.Folder.InRoot("LONGRE~1.png")), "the root has no file of that name");
        var cat = await stash.Import("LONGRE~1", MediaFixtures.DistinctPng(30, 8, 6));

        var saved = await stash.SaveEdit(cat, EditedItems.BlackBoxAndBox);
        await stash.Reopen();

        Assert.IsTrue(saved.Succeeded, saved.Failure?.Sentence);
        Assert.AreEqual("LONGRE~1 2.png", saved.Value!.FileName);
        Assert.AreEqual(stash.Folder.InOriginals("LONGRE~1 2.png"), (await EditedItems.ForEdit(stash.Store!, cat.Id)).OriginalPath, "both its files take the counter");
        Assert.AreEqual(before, EditedItems.Hash(editedOriginal), "the other item's original is left as it was (G29)");
        Assert.AreEqual(editedOriginal, (await EditedItems.ForEdit(stash.Store!, edited.Id)).OriginalPath);
    }

    [TestMethod]
    public async Task ANameShapedLikeAShortNameIsTakenSoNoFileMadeLaterCanAnswerToItBeforeItsFileIsThere()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var cat = await stash.Import("LONGRE~1", MediaFixtures.DistinctPng(10, 8, 6));
        var catOriginal = EditedItems.Hash(cat.FilePath);
        var render = await stash.Render(cat, EditedItems.BlackBoxAndBox);
        using (TestStash.Hold(cat.FilePath))
        {
            Assert.IsFalse((await store.Save(cat, EditedItems.BlackBoxAndBox, render)).Succeeded, "its original's move waits");
        }

        // Windows gives this original, moved into originals\ next, the short name LONGRE~1.PNG if no file has it yet.
        var other = await stash.Import("long reaction picture", MediaFixtures.DistinctPng(20, 8, 6));
        var otherOriginal = EditedItems.Hash(other.FilePath);
        Assert.IsTrue((await stash.SaveEdit(other, EditedItems.Cropped)).Succeeded);
        await stash.Reopen();

        var (catEdit, otherEdit) = (await EditedItems.ForEdit(stash.Store!, cat.Id), await EditedItems.ForEdit(stash.Store!, other.Id));
        Assert.AreEqual(stash.Folder.InOriginals("LONGRE~1 2.png"), catEdit.OriginalPath, "a name another file may come to answer to is taken");
        Assert.AreEqual(catOriginal, EditedItems.Hash(catEdit.OriginalPath), "so the start finishes the Save with its own original (G28, G29)");
        Assert.AreEqual(otherOriginal, EditedItems.Hash(otherEdit.OriginalPath));
        CollectionAssert.AreEquivalent(new[] { "LONGRE~1 2.png", "long reaction picture.png" }, stash.RootFiles());
    }

    [TestMethod]
    [DataRow("cat~")]
    [DataRow("~")]
    [DataRow("foo~bar")]
    [DataRow("cat~1x")]
    public async Task ATildeNameNoShortNameCanHaveIsGivenAsItIs(string name)
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();

        var item = await stash.Import(name);

        Assert.AreEqual(name + ".png", item.FileName, "Windows makes a short name with a tilde and a number at the end of its stem");
    }

    [TestMethod]
    public async Task AnAdoptionsPlaceKeepsItsNameTakenInTheRootForAnotherItemWhileItWaits()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var cat = await stash.ImportFixture("cat", "still.png");
        await OpenedWith.AwaitingRewrite(stash, cat, MediaFixtures.Bytes("photo.jpg"));
        using (TestStash.Hold(cat.FilePath))
        {
            Assert.AreEqual(HeldFileRetry.HeldReason, (await stash.Store!.AdoptRewrite(cat.Id)).Failure?.Reason, "its place to cat.jpg waits");
        }

        var other = await stash.Import("cat", extension: ".jpg");

        Assert.AreEqual("cat 2.jpg", other.FileName);
        Assert.IsTrue((await stash.Store!.Rename(cat, "cat")).Succeeded, "the adoption then finishes");
        CollectionAssert.AreEquivalent(new[] { "cat.jpg", "cat 2.jpg" }, stash.RootFiles());
    }

    [TestMethod]
    public async Task ARecordsPreviousOriginalStaysTakenInOriginalsOnceItsFileIsGoneSoItsExpiryNeverRecyclesTheItemsOriginal()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var cat = (await stash.SaveEdit(await stash.ImportFixture("cat", "still.png"), EditedItems.BlackBoxAndBox)).Value!;
        await OpenedWith.AwaitingRewrite(stash, cat, MediaFixtures.DistinctPng(5, 12, 10));
        var adopted = await OpenedWith.Adopt(stash, cat.Id);
        var previous = stash.Folder.InOriginals("cat.png");
        // Windows deleting it for good instead of recycling it: the expiry is refused, its file gone, its record kept.
        stash.RecycleBin.BeforeEach = File.Delete;
        stash.RecycleBin.Refusing = path => path == previous;
        Assert.IsFalse((await stash.Store!.ExpireAdoption(cat.Id, adopted.RenderedRevision)).Succeeded);
        stash.RecycleBin.BeforeEach = null;
        stash.RecycleBin.Refusing = _ => false;
        Assert.IsFalse(File.Exists(previous));

        var saved = await stash.SaveEdit(adopted, EditedItems.Arrow);
        var expired = await stash.Store.ExpireAdoption(cat.Id, adopted.RenderedRevision);

        Assert.IsTrue(saved.Succeeded, saved.Failure?.ToString());
        Assert.IsTrue(expired.Succeeded, expired.Failure?.ToString());
        CollectionAssert.AreEqual(new[] { "cat 2.png" }, EditedItems.OriginalFiles(stash), "the name the record kept stays the record's (G29)");
        Assert.IsNull(OpenedWith.Undo(stash, cat.Id));
    }

    [TestMethod]
    public void ALongNameIsCutWithoutSplittingACharacter()
    {
        var name = new string('a', 199) + "😀" + "tail";

        var stem = ItemFileNames.SafeStem(name);

        Assert.AreEqual(new string('a', 199), stem);
    }

    // Windows answers a file's short (8.3) name as the file; a volume that makes none has no name to collide with.
    private static void RequireShortName(string alias)
    {
        if (!File.Exists(alias))
        {
            Assert.Inconclusive($"the volume of {alias} makes no short names");
        }
    }
}
