using System.Drawing;
using ImageStash.Editor;
using ImageStash.Layers;
using ImageStash.Media;
using ImageStash.Store;
using ImageStash.Tests.Support;
using SkiaSharp;
using static ImageStash.Tests.Support.EditorSessions;

namespace ImageStash.Tests.Editor;

/// <summary>
/// How a session ends (G20, G26, Composition: Editor; State: Save, Save as new item, A live failure; G16, G28, G35, E4,
/// R9): saved, rendered from the live layers over the original where it is now and published before it answers; refused
/// by name with the session kept; failed after the commit, named, the session ended on the item as committed; gone when
/// the item left the stash during the render, the render discarded; nothing changed for an unedited item with no layers;
/// Save as new item going on with the new item, the source left as it was; the layers unchanged while a Save runs;
/// Cancel answering the discarded layers.
/// </summary>
[TestClass]
public sealed class EditorSessionEndingTests
{
    [TestMethod]
    public async Task SaveRendersTheLiveLayersCommitsAndPublishesThenEnds()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var cat = await stash.ImportFixture("cat", "still.png");
        var commits = await EditedItems.Commits(store, cat.Id);
        using var session = await Open(stash, cat);
        session.Tool = session.Hide();
        var endings = new List<EditResult>();
        session.Ended += endings.Add;
        session.DragThrough((1, 1), (4, 3));
        session.Drawing(MarkKind.Box);
        session.DragThrough((0, 0), (8, 6));
        var live = session.Layers;

        var saved = await session.Save();

        Assert.AreEqual(EditEnding.Saved, saved.Ending, saved.Failure?.Sentence);
        Assert.AreEqual(1, saved.Item!.RenderedRevision);
        Assert.AreEqual(stash.Folder.InRoot("cat.png"), saved.Item.FilePath);
        CollectionAssert.AreEqual(Composed(session.Frame, live), MediaFixtures.FirstFrame(saved.Item.FilePath).Pixels);
        CollectionAssert.AreEqual(MediaFixtures.Bytes("still.png"), File.ReadAllBytes(stash.Folder.InOriginals("cat.png")));
        Assert.AreEqual(saved, endings.Single());
        Assert.IsFalse(session.IsOpen);
        Assert.IsEmpty(stash.WorkFiles());
        Assert.AreEqual(commits + 2, await EditedItems.Commits(store, cat.Id));
        Assert.AreEqual(live, (await EditedItems.ForEdit(store, cat.Id)).Layers);
    }

    [TestMethod]
    public async Task ARefusedRenderIsNamedAndTheSessionStaysOpenOnItsLayers()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var cat = await stash.ImportFixture("cat", "still.png");
        var commits = await EditedItems.Commits(store, cat.Id);
        var files = EditedItems.FilesWithHashes(stash);
        using var session = await Open(stash, cat, EditedItems.Refusing);
        session.Tool = session.Hide();
        var endings = new List<EditResult>();
        session.Ended += endings.Add;
        session.DragThrough((1, 1), (4, 3));
        var live = (session.Layers, session.Selected);

        var refused = await session.Save();

        Assert.AreEqual(EditEnding.Refused, refused.Ending);
        Assert.AreEqual($"Save the picture failed: cat, {EditedItems.RefusedReason}", refused.Failure!.Sentence);
        Assert.IsTrue(session.IsOpen);
        Assert.IsEmpty(endings);
        Assert.AreEqual(live, (session.Layers, session.Selected));
        Assert.IsTrue(session.CanUndo);
        Assert.IsNull(session.Busy);
        CollectionAssert.AreEqual(files, EditedItems.FilesWithHashes(stash));
        Assert.IsEmpty(stash.WorkFiles());
        Assert.AreEqual(commits, await EditedItems.Commits(store, cat.Id));
    }

    [TestMethod]
    public async Task AStepRefusedAfterTheCommitIsNamedAndEndsTheSessionOnTheItemAsCommitted()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var cat = await stash.ImportFixture("cat", "still.png");
        var holds = new List<FileStream>();
        try
        {
            using var session = await Open(stash, cat, EditedItems.Holding(holds));
            session.Tool = session.Hide();
            session.DragThrough((1, 1), (4, 3));
            var live = session.Layers;

            var failed = await session.Save();

            Assert.AreEqual(EditEnding.FailedAfterCommit, failed.Ending);
            Assert.IsNotNull(failed.Failure);
            Assert.IsFalse(session.IsOpen);
            var edit = await EditedItems.ForEdit(store, cat.Id);
            Assert.AreEqual(edit.OriginalPath, (await EditedItems.Listed(store, cat.Id)).FilePath, "listed on the picture on disk, the original");
            Assert.AreEqual(live, edit.Layers);
        }
        finally
        {
            holds.ForEach(hold => hold.Dispose());
        }
    }

    [TestMethod]
    public async Task AfterAnEarlierPendingSaveFinishedOnTheWaySaveRendersFromTheOriginalWhereItIsNow()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var cat = await stash.ImportFixture("cat", "still.png");
        // A first Save of a 4 × 3 crop, committed while another program held the original: its steps wait.
        var cropped = await stash.Render(cat, EditedItems.Cropped);
        using (TestStash.Hold(cat.FilePath))
        {
            Assert.IsFalse((await store.Save(cat, EditedItems.Cropped, cropped)).Succeeded);
        }

        var holds = new List<FileStream>();
        var renders = 0;
        FinishedPictureRenderer holdingTheFirst = (original, name, layers, format, work, retry) =>
        {
            var rendered = FinishedPicture.Render(original, name, layers, format, work, retry);
            if (renders++ == 0 && rendered.Succeeded)
            {
                holds.Add(TestStash.Hold(work));
            }

            return rendered;
        };
        try
        {
            using var session = await Open(stash, cat, holdingTheFirst);
            session.Tool = session.Hide();
            Assert.AreEqual(stash.Folder.InRoot("cat.png"), session.Edit.OriginalPath);
            session.DragThrough((1, 1), (3, 3));

            // Store finishes the waiting Save first, moving the original into originals\ and putting the crop's render in
            // the root; the new item's own render is then refused its place.
            var refused = await session.SaveAsNewItem();
            Assert.AreEqual(EditEnding.Refused, refused.Ending, refused.Failure?.Sentence);
            Assert.AreEqual(cat.Id, session.Item.Id);
            holds.ForEach(hold => hold.Dispose());
            var live = session.Layers;

            var saved = await session.Save();

            Assert.AreEqual(EditEnding.Saved, saved.Ending, saved.Failure?.Sentence);
            Assert.AreEqual((8, 6), (saved.Item!.Facts.Width, saved.Item.Facts.Height));
            CollectionAssert.AreEqual(Composed(session.Frame, live), MediaFixtures.FirstFrame(saved.Item.FilePath).Pixels);
            CollectionAssert.AreEqual(MediaFixtures.Bytes("still.png"), File.ReadAllBytes(stash.Folder.InOriginals("cat.png")));
        }
        finally
        {
            holds.ForEach(hold => hold.Dispose());
        }
    }

    [TestMethod]
    public async Task TheItemRemovedDuringTheRenderEndsTheSessionAsGoneWithTheRenderDiscarded()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var cat = await stash.ImportFixture("cat", "still.png");
        using var rendering = new SemaphoreSlim(0);
        using var removed = new SemaphoreSlim(0);
        FinishedPictureRenderer waiting = (original, name, layers, format, work, retry) =>
        {
            rendering.Release();
            removed.Wait();
            return FinishedPicture.Render(original, name, layers, format, work, retry);
        };
        using var session = await Open(stash, cat, waiting);
        session.Tool = session.Hide();
        session.DragThrough((1, 1), (4, 3));

        var saving = session.Save();
        Assert.IsTrue(await rendering.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.IsTrue((await store.Remove([cat.Id])).Single().Succeeded);
        removed.Release();
        var gone = await saving;

        Assert.AreEqual(EditEnding.Gone, gone.Ending);
        Assert.IsNull(gone.Failure);
        Assert.IsFalse(session.IsOpen);
        Assert.IsEmpty(stash.WorkFiles());
    }

    [TestMethod]
    public async Task NoLayersOnAnUneditedItemChangesNothingAndEndsTheSession()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var cat = await stash.ImportFixture("cat", "still.png");
        var commits = await EditedItems.Commits(store, cat.Id);
        using var session = await Open(stash, cat);

        var saved = await session.Save();

        Assert.AreEqual(EditEnding.Saved, saved.Ending);
        Assert.AreEqual(commits, await EditedItems.Commits(store, cat.Id));
        Assert.IsFalse(session.IsOpen);
    }

    [TestMethod]
    public async Task NoLayersOnASavedItemLeavesItOneFileAgain()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var cat = (await stash.SaveEdit(await stash.ImportFixture("cat", "still.png"), EditedItems.BlackBoxAndBox)).Value!;
        using var session = await Open(stash, cat);
        session.RevertToOriginal();

        var saved = await session.Save();

        Assert.AreEqual(EditEnding.Saved, saved.Ending, saved.Failure?.Sentence);
        CollectionAssert.AreEqual(new[] { "cat.png" }, stash.RootFiles());
        Assert.IsEmpty(EditedItems.OriginalFiles(stash));
        CollectionAssert.AreEqual(MediaFixtures.Bytes("still.png"), File.ReadAllBytes(saved.Item!.FilePath));
    }

    [TestMethod]
    public async Task SaveAsNewItemGoesOnWithTheNewItemAndLeavesTheSourceAsItWas()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var cat = await stash.ImportFixture("cat", "still.png");
        stash.Tag(cat.Id, "yes");
        stash.SqlWhileOpen("UPDATE items SET favorite = 1 WHERE id = $id", ("$id", cat.Id));
        using var session = await Open(stash, cat);
        session.Tool = session.Hide();
        session.DragThrough((1, 1), (4, 3));
        var live = session.Layers;
        var source = EditedItems.Row(stash, cat.Id);
        var sourceFiles = EditedItems.FilesWithHashes(stash).Where(file => file.StartsWith("cat.png")).ToList();

        var continued = await session.SaveAsNewItem();

        Assert.AreEqual(EditEnding.Continued, continued.Ending, continued.Failure?.Sentence);
        var made = continued.Item!;
        Assert.AreEqual("cat 2", made.Name);
        Assert.AreNotEqual(cat.Id, made.Id);
        Assert.AreEqual(made.Id, session.Item.Id);
        Assert.AreEqual(live, session.Layers);
        Assert.IsFalse(session.CanUndo);
        Assert.IsFalse(session.CanRedo);
        Assert.IsTrue(session.IsOpen);
        Assert.AreEqual(live, (await EditedItems.ForEdit(store, made.Id)).Layers);
        CollectionAssert.AreEqual(new[] { "yes" }, made.Tags.ToArray());
        Assert.IsFalse(made.Favorite);
        Assert.AreEqual(source, EditedItems.Row(stash, cat.Id));

        session.Drawing(MarkKind.Arrow);
        session.DragThrough((0, 0), (7, 5));
        var saved = await session.Save();

        Assert.AreEqual(EditEnding.Saved, saved.Ending, saved.Failure?.Sentence);
        Assert.AreEqual(made.Id, saved.Item!.Id);
        Assert.AreEqual(session.Layers, (await EditedItems.ForEdit(store, made.Id)).Layers);
        Assert.AreEqual(source, EditedItems.Row(stash, cat.Id));
        CollectionAssert.AreEqual(sourceFiles, EditedItems.FilesWithHashes(stash).Where(file => file.StartsWith("cat.png")).ToList());
    }

    [TestMethod]
    public async Task ARefusedSaveAsNewItemIsNamedAndKeepsTheSessionOnTheItem()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var cat = await stash.ImportFixture("cat", "still.png");
        using var session = await Open(stash, cat, EditedItems.Refusing);
        session.Tool = session.Hide();
        session.DragThrough((1, 1), (4, 3));

        var refused = await session.SaveAsNewItem();

        Assert.AreEqual(EditEnding.Refused, refused.Ending);
        Assert.AreEqual("cat", refused.Failure!.Subject);
        Assert.AreEqual(cat.Id, session.Item.Id);
        Assert.IsTrue(session.IsOpen);
        Assert.IsTrue(session.CanUndo);
        Assert.HasCount(1, await stash.All());
        Assert.IsEmpty(stash.WorkFiles());
    }

    [TestMethod]
    public async Task WhileASaveRunsItsBusyStateNamesTheItemAndNothingChangesTheLayers()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var cat = await stash.ImportFixture("cat", "still.png");
        using var rendering = new SemaphoreSlim(0);
        using var go = new SemaphoreSlim(0);
        FinishedPictureRenderer waiting = (original, name, layers, format, work, retry) =>
        {
            rendering.Release();
            go.Wait();
            return FinishedPicture.Render(original, name, layers, format, work, retry);
        };
        using var session = await Open(stash, cat, waiting);
        session.Tool = session.Hide();
        session.DragThrough((1, 1), (4, 3));
        var live = (session.Layers, session.Selected);

        var saving = session.Save();
        Assert.IsTrue(await rendering.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.AreEqual("Saving “cat”…", session.Busy);
        session.DragThrough((5, 4), (7, 5));
        session.Undo();
        session.DeleteSelected();
        session.Shrink = Shrink.Half;
        Assert.AreEqual(live, (session.Layers, session.Selected));
        Assert.AreSame(saving, session.Save(), "a second Save answers the running one");
        Assert.IsNull(session.Cancel(), "a running Save is not cancelled");
        go.Release();

        Assert.AreEqual(EditEnding.Saved, (await saving).Ending);
        Assert.IsNull(session.Busy);
    }

    [TestMethod]
    public async Task CancelEndsTheSessionAnsweringTheDiscardedLayersAndChangesNothing()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var cat = await stash.ImportFixture("cat", "still.png");
        var commits = await EditedItems.Commits(store, cat.Id);
        var files = EditedItems.FilesWithHashes(stash);
        using var session = await Open(stash, cat);
        session.Tool = session.Hide();
        var endings = new List<EditResult>();
        session.Ended += endings.Add;
        session.DragThrough((1, 1), (3, 3));
        session.Drawing(MarkKind.Arrow);
        session.DragThrough((0, 0), (7, 5));
        var live = session.Layers;

        var discarded = session.Cancel();

        Assert.AreEqual(live, discarded);
        Assert.IsFalse(session.IsOpen);
        Assert.AreEqual(EditEnding.Cancelled, endings.Single().Ending);
        Assert.AreEqual(live, endings.Single().Discarded);
        Assert.AreEqual(commits, await EditedItems.Commits(store, cat.Id));
        CollectionAssert.AreEqual(files, EditedItems.FilesWithHashes(stash));
        Assert.IsEmpty(stash.WorkFiles());
        session.Press(new PointF(1, 1));
        Assert.AreEqual(live, session.Layers, "an ended session takes no change");
    }

    /// <summary>What Media composes of an opaque <paramref name="original"/> under <paramref name="layers"/>, whose pixels premultiplied are as they are straight.</summary>
    private static byte[] Composed(MediaFrame original, LayerList layers)
    {
        var size = LayerComposer.OutputSize(original.Width, original.Height, layers);
        using var bitmap = new SKBitmap(new SKImageInfo(size.Width, size.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Transparent);
            Assert.IsTrue(LayerComposer.Compose(canvas, original, layers, 0, "cat").Succeeded);
        }

        return bitmap.Bytes;
    }
}
