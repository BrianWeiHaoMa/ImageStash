using ImageStash.Editor;
using ImageStash.Layers;
using ImageStash.Media;
using ImageStash.Store;
using ImageStash.Tests.Support;

namespace ImageStash.Tests.Editor;

/// <summary>
/// The one render-then-commit routine (Composition: Editor; State: Save; F3, N7, G35): the render in the finished format
/// Store derived, at a path of its own extension; a render refused, or a commit refused before it, leaving nothing in
/// <c>.work\</c>; an empty list rendering nothing.
/// </summary>
[TestClass]
public sealed class SavedEditTests
{
    [TestMethod]
    public async Task AStillWebPRendersAPngUnderItsOwnExtension()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var cat = await stash.ImportFixture("cat", "transparent.webp");
        var edit = await EditedItems.ForEdit(store, cat.Id);
        var asked = new List<(ImageFormat Format, string Extension)>();
        FinishedPictureRenderer recording = (original, name, layers, format, work, retry) =>
        {
            asked.Add((format, Path.GetExtension(work)));
            return FinishedPicture.Render(original, name, layers, format, work, retry);
        };

        var saved = await SavedEdit.Save(store, cat, edit, EditedItems.Arrow, recording, TestStash.ShortRetry);

        Assert.IsTrue(saved.Succeeded, saved.Failure?.Sentence);
        CollectionAssert.AreEqual(new[] { (ImageFormat.Png, ".png") }, asked);
        Assert.AreEqual(ImageFormat.Png, MediaDecoder.ReadFacts(saved.Value!.FilePath, "cat", TestStash.ShortRetry).Value!.Facts.Format);
        Assert.AreEqual("cat.png", Path.GetFileName(saved.Value.FilePath));
    }

    [TestMethod]
    public async Task ACommitRefusedBeforeItLeavesNothingInTheWorkFolder()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var cat = await stash.ImportFixture("cat", "still.png");
        var dog = await stash.ImportFixture("dog", "photo.jpg");
        var catEdit = await EditedItems.ForEdit(store, cat.Id);
        var dogEdit = await EditedItems.ForEdit(store, dog.Id);
        FinishedPictureRenderer thenRemoved = (original, name, layers, format, work, retry) =>
        {
            var rendered = FinishedPicture.Render(original, name, layers, format, work, retry);
            store.Remove([name == "cat" ? cat.Id : dog.Id]).GetAwaiter().GetResult();
            return rendered;
        };

        var saved = await SavedEdit.Save(store, cat, catEdit, EditedItems.Arrow, thenRemoved, TestStash.ShortRetry);
        var made = await SavedEdit.SaveAsNew(store, dog, dogEdit, EditedItems.Arrow, thenRemoved, TestStash.ShortRetry);

        Assert.AreEqual("Save failed: cat, it is no longer in the stash", saved.Failure?.Sentence);
        Assert.IsNull(saved.Value);
        Assert.AreEqual("dog", made.Failure?.Subject, made.Failure?.Sentence);
        Assert.IsNull(made.Value);
        Assert.IsEmpty(stash.WorkFiles());
        Assert.IsEmpty(await stash.All());
    }

    [TestMethod]
    public async Task ARefusedRenderIsAnsweredAsNamedAndLeavesNothingInTheWorkFolder()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var cat = await stash.ImportFixture("cat", "still.png");
        var edit = await EditedItems.ForEdit(store, cat.Id);
        FinishedPictureRenderer partial = (_, name, _, _, work, _) =>
        {
            File.WriteAllBytes(work, [1, 2, 3]);
            return new Failure("Save the picture", name, "the disk refused the write");
        };

        var saved = await SavedEdit.Save(store, cat, edit, EditedItems.Arrow, partial, TestStash.ShortRetry);

        Assert.AreEqual("Save the picture failed: cat, the disk refused the write", saved.Failure?.Sentence);
        Assert.IsEmpty(stash.WorkFiles());
        TestStash.AssertSameItem(cat, await EditedItems.Listed(store, cat.Id));
    }

    [TestMethod]
    public async Task AnEmptyListRendersNothing()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var cat = await stash.ImportFixture("cat", "still.png");
        var edit = await EditedItems.ForEdit(store, cat.Id);
        var renders = 0;
        FinishedPictureRenderer counting = (original, name, layers, format, work, retry) =>
        {
            renders++;
            return FinishedPicture.Render(original, name, layers, format, work, retry);
        };

        var saved = await SavedEdit.Save(store, cat, edit, LayerList.Empty, counting, TestStash.ShortRetry);
        var copy = await SavedEdit.SaveAsNew(store, cat, edit, LayerList.Empty, counting, TestStash.ShortRetry);

        Assert.IsTrue(saved.Succeeded && copy.Succeeded, saved.Failure?.Sentence ?? copy.Failure?.Sentence);
        Assert.AreEqual(0, renders);
        Assert.AreEqual("cat 2", copy.Value!.Name);
        CollectionAssert.AreEqual(MediaFixtures.Bytes("still.png"), File.ReadAllBytes(copy.Value.FilePath));
    }
}
