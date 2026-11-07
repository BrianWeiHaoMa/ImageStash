using ImageStash.Layers;
using ImageStash.Media;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Store;

/// <summary>
/// What a listed item says of the file handed over (G6, G8, G19): an edited item's facts are its render's, an unedited
/// item's its one file's; and whether it is edited, which the menus and the preview follow (G19), read from the committed
/// list without parsing it.
/// </summary>
[TestClass]
public sealed class StashItemTests
{
    [TestMethod]
    public async Task AnEditedItemDescribesItsRenderAndAnUneditedOneItsOneFile()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var edited = await stash.Import("cat", extension: ".webp");
        var plain = await stash.Import("plain", extension: ".webp");
        stash.Close();
        stash.MakeEdited(edited, TestStash.Bytes(1));
        File.Move(stash.Folder.InRoot("cat.webp"), stash.Folder.InRoot("cat.gif"));
        stash.Sql(
            """
            UPDATE items SET finished_file = 'cat.gif', finished_format = 'Gif', finished_width = 10, finished_height = 7,
                finished_animated = 1, finished_frame_count = 2, finished_duration_ms = 500
            WHERE id = $id
            """,
            ("$id", edited.Id));
        await stash.OpenCleanly();

        var all = await stash.All();
        var read = (await stash.Store!.Item(edited.Id)).Value!;

        var render = new MediaFacts(ImageFormat.Gif, 10, 7, true, 2, 500);
        Assert.AreEqual(render, all.Single(item => item.Id == edited.Id).Facts);
        Assert.AreEqual(render, read.Facts);
        Assert.AreEqual(TestStash.StillPng with { Format = ImageFormat.WebP }, all.Single(item => item.Id == plain.Id).Facts);
        Assert.AreEqual("GIF", TileBadge.Of(read));
        Assert.IsNull(TileBadge.Of(all.Single(item => item.Id == plain.Id)));
    }

    [TestMethod]
    public async Task AnItemIsEditedWhileItsCommittedListHasLayersAndNoListingParsesTheList()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var cat = await stash.ImportFixture("cat", "still.png");
        Assert.IsFalse(cat.Edited, "an import has no layers");
        Assert.IsFalse((await stash.All()).Single().Edited);

        var saved = (await stash.SaveEdit(cat, EditedItems.BlackBoxAndBox)).Value!;
        Assert.IsTrue(saved.Edited, "the Save's answer");
        Assert.IsTrue((await stash.Store!.Item(cat.Id)).Value!.Edited, "and as the stash lists it");

        var copy = (await stash.Store.SaveAsNewItem(saved, EditedItems.BlackBoxAndBox, await stash.Render(saved, EditedItems.BlackBoxAndBox))).Value!;
        Assert.AreEqual("cat 2", copy.Name);
        Assert.IsTrue(copy.Edited, "Save as new item carries the layers");
        Assert.IsTrue((await stash.All()).Single(item => item.Id == copy.Id).Edited);

        var reverted = (await stash.Store.Save(saved, LayerList.Empty, null)).Value!;
        Assert.IsFalse(reverted.Edited, "Revert to original removes every layer");
        Assert.IsFalse((await stash.Store.Item(cat.Id)).Value!.Edited);

        stash.SqlWhileOpen("UPDATE items SET layers = '{' WHERE id = $id", ("$id", copy.Id));
        var listed = await stash.All();
        Assert.IsTrue(listed.Single(item => item.Id == copy.Id).Edited, "a list the module cannot read is still a list: the listing parses none and works");
        Assert.IsFalse((await stash.Store.ForEdit(copy.Id)).Succeeded, "only Edit reads the list, and names it");
    }

    [TestMethod]
    public async Task AFirstSaveWaitingOnALiveFailureIsEditedThoughItsPictureIsStillTheOriginal()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var cat = await EditedItems.FirstSaveCommitted(stash);

        var listed = (await stash.All()).Single();

        Assert.AreEqual(0, listed.RenderedRevision, "the grid shows the picture on disk, the original (State: A live failure)");
        Assert.IsTrue(listed.Edited, "while its committed list has the layers the next start renders");
        Assert.AreEqual(cat.Id, listed.Id);
    }
}
