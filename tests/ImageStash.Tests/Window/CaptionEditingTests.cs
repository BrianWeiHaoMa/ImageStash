using System.Drawing;
using Avalonia.Input;
using ImageStash.Editor;
using ImageStash.Files;
using ImageStash.Layers;
using ImageStash.Media;
using ImageStash.Store;
using ImageStash.Tests.Support;
using ImageStash.Window;
using static ImageStash.Tests.Support.CaptionedItems;

namespace ImageStash.Tests.Window;

[TestClass]
public sealed class CaptionEditingTests
{
    [TestMethod]
    [DataRow(EditOrigin.Grid)]
    [DataRow(EditOrigin.Preview)]
    public async Task AnActiveSearchFollowsSaveRevertAndUndoButNeverTheDraft(EditOrigin origin)
    {
        using var listed = await ListedStash.Open();
        var source = await SaveCaption(listed.Stash, "echidna");
        var other = await Save(listed.Stash, await listed.Stash.ImportFixture("greeting", "photo.jpg"), Caption("echidna"));
        await listed.Listing.SetQuery("echidna");
        listed.Listing.Focus(source.Id);
        if (origin == EditOrigin.Preview)
        {
            await listed.Press(Key.Space, KeyModifiers.None, KeyPlace.Grid);
            // Preview.Open always tells Listing which item the preview shows.
            listed.Editing.Returned += back => listed.Listing.FocusForPreview(back.Item!.Id, byPerson: false);
        }
        var session = await OpenFromResults(listed, origin == EditOrigin.Grid ? KeyPlace.SearchBox : KeyPlace.Preview);
        ChangeCaption(session);

        await Finds(listed.Stash.Store!, "platypus");
        Assert.AreEqual(source.Id, listed.Listing.Selection.Single(), "the draft is absent from search");

        var saved = await listed.Editing.Save();

        Assert.IsTrue(saved.Succeeded, saved.Failure?.ToString());
        Assert.AreEqual("echidna", listed.Listing.Query);
        Assert.AreEqual(other.Id, listed.Listing.Items.Single().Id, "the old caption drops out with the new picture");
        CollectionAssert.AreEqual(new[] { other.Id }, listed.Listing.Selection.ToArray(), "the first remaining caption hit is ready to hand off");
        await listed.Listing.SetQuery("platypus");
        var edited = listed.Listing.Items.Single();
        Assert.AreEqual(source.Id, edited.Id);
        var finished = File.ReadAllBytes(edited.FilePath);

        var reverted = await listed.Editing.RevertToOriginal(edited);

        Assert.IsTrue(reverted.Succeeded, reverted.Failure?.ToString());
        Assert.IsEmpty(listed.Listing.Items);
        await listed.Notices.Undo(listed.Notices.Shown.Single());
        var restored = listed.Listing.Items.Single();
        Assert.AreEqual(source.Id, restored.Id);
        CollectionAssert.AreEqual(finished, File.ReadAllBytes(restored.FilePath));
    }

    [TestMethod]
    public async Task SavingACopyOutsideTheActiveSearchLeavesTheSourceSelectedOnReturn()
    {
        using var listed = await ListedStash.Open();
        var source = await SaveCaption(listed.Stash, "echidna");
        await listed.Listing.SetQuery("echidna");
        var session = await OpenFromResults(listed);
        ChangeCaption(session);

        var copied = await listed.Editing.SaveAsNewItem();

        Assert.IsTrue(copied.Succeeded, copied.Failure?.ToString());
        Assert.AreNotEqual(source.Id, session.Item.Id, "the editor continues on the saved copy");
        Assert.IsTrue(listed.Editing.Cancel());
        Assert.AreEqual(source.Id, listed.Listing.Items.Single().Id);
        CollectionAssert.AreEqual(new[] { source.Id }, listed.Listing.Selection.ToArray());
    }

    [TestMethod]
    public async Task SearchFollowsAnOpenWithRewriteAndTheNotificationsUndo()
    {
        using var listed = await ListedStash.Open();
        var saved = await SaveCaption(listed.Stash, "echidna");
        var rendered = File.ReadAllBytes(saved.FilePath);
        await listed.Listing.SetQuery("echidna");
        Assert.AreEqual(saved.Id, listed.Listing.Items.Single().Id);

        var notification = await OpenedWith.ReplacedThroughTheMenu(listed, saved, MediaFixtures.Bytes("photo.jpg"));

        Assert.IsTrue(listed.Listing.NoMatch, "the rewritten picture has no layers");
        await listed.Notifications.Undo(notification);
        var restored = listed.Listing.Items.Single();
        Assert.AreEqual(saved.Id, restored.Id);
        CollectionAssert.AreEqual(rendered, File.ReadAllBytes(restored.FilePath));
    }

    [TestMethod]
    public async Task CancelAndUndoAfterARefusalRestoreTheDraftTheUserDiscarded()
    {
        using var listed = await ListedStash.Open();
        var source = await SaveCaption(listed.Stash, "echidna");
        await listed.Listing.SetQuery("echidna");
        var session = await OpenFromResults(listed);
        ChangeCaption(session);
        session.EndTyping();
        listed.Renderer = EditedItems.Refusing;

        var busyDuringRefresh = new List<string?>();
        void DuringRefresh()
        {
            busyDuringRefresh.Add(session.Busy);
            session.Undo();
        }
        listed.Listing.Changed += DuringRefresh;
        var refused = await listed.Editing.SaveAsNewItem();
        listed.Listing.Changed -= DuringRefresh;

        Assert.IsNotEmpty(busyDuringRefresh);
        Assert.IsTrue(busyDuringRefresh.All(busy => busy is not null), "the editor still owns the refusal while the results refresh");
        Assert.IsTrue(listed.Editing.Cancel());
        Assert.IsFalse(listed.Editing.IsOpen);
        Assert.Contains(refused.Failure!.Sentence, listed.Notices.Shown.Select(notice => notice.Text));
        Assert.AreEqual(source.Id, listed.Listing.Selection.Single());
        await listed.Notices.Undo(listed.Notices.Shown.Single(listed.Notices.OffersUndo));
        Assert.AreEqual("platypus", ((CaptionBarLayer)listed.Editing.Session!.Layers.Layers.Single()).Text);
    }

    [TestMethod]
    [DataRow(false, DisplayName = "A refused copy with just its original placed stays out of the results")]
    [DataRow(true, DisplayName = "A refused copy with both files placed appears in the results")]
    public async Task ReturningFromARefusedSaveAsNewShowsExactlyThePlacedCopies(bool placed)
    {
        using var listed = await ListedStash.Open();
        var stash = listed.Stash;
        var source = await SaveCaption(stash, "signal");
        var originalPicture = File.ReadAllBytes(source.FilePath);
        await listed.Listing.SetQuery("signal");
        var session = await OpenFromResults(listed);
        var heldRenders = new List<FileStream>();
        if (placed)
        {
            RefusePictureCompletion(stash);
        }
        else
        {
            listed.Renderer = EditedItems.Holding(heldRenders);
        }

        listed.Clipboard.LastWritten = [source.Id];
        try
        {
            var refused = await listed.Editing.SaveAsNewItem();

            AssertRefusal(listed, refused, placed ? CompletionRefused : HeldFileRetry.HeldReason);
            Assert.AreSame(session, listed.Editing.Session);
            Assert.AreEqual(source.Id, session.Item.Id);
            var copyId = stash.SqlQueryWhileOpen("SELECT id FROM items WHERE id <> $source AND pending_steps IS NOT NULL",
                row => row.GetInt64(0), ("$source", source.Id)).Single();
            Assert.IsTrue(File.Exists(stash.Folder.InOriginals("picture 2.png")), "the original copy landed");
            Assert.AreEqual(placed, File.Exists(stash.Folder.InRoot("picture 2.png")), "the finished file is what makes it listable");

            Assert.IsTrue(listed.Editing.Cancel());
            await AssertMatchesStore(listed, placed ? [source.Id, copyId] : [source.Id]);
            CollectionAssert.AreEqual(originalPicture, File.ReadAllBytes(source.FilePath));
        }
        finally
        {
            foreach (var hold in heldRenders)
            {
                hold.Dispose();
            }
        }
    }

    [TestMethod]
    public async Task ARefusedCopyAfterFinishingTheSourcesPendingSaveShowsItsNewCaption()
    {
        using var listed = await ListedStash.Open();
        var stash = listed.Stash;
        var source = await SaveCaption(stash, "echidna");
        var later = Caption("platypus");
        var next = await stash.Render(source, later);
        var oldBytes = File.ReadAllBytes(source.FilePath);
        using (TestStash.Hold(source.FilePath))
        {
            var pending = await stash.Store!.Save(source, later, next);
            Assert.AreEqual(HeldFileRetry.HeldReason, pending.Failure!.Reason);
        }

        await listed.Listing.SetQuery("echidna");
        var session = await OpenFromResults(listed);
        listed.Clipboard.LastWritten = [source.Id];
        FileStream? heldOriginal = null;
        stash.Renderer = (original, name, layers, format, work, retry) =>
        {
            var rendered = FinishedPicture.Render(original, name, layers, format, work, retry);
            if (rendered.Succeeded)
            {
                // A scanner takes the original after recovery reads it. Only the subsequent copy needs to open it.
                heldOriginal = new FileStream(original, FileMode.Open, FileAccess.Read, FileShare.None);
            }
            return rendered;
        };

        Outcome refused;
        try
        {
            refused = await listed.Editing.SaveAsNewItem();
        }
        finally
        {
            heldOriginal?.Dispose();
        }

        AssertRefusal(listed, refused, HeldFileRetry.HeldReason);
        Assert.HasCount(1, stash.RenderCalls);
        Assert.IsEmpty(EditedItems.Row(stash, source.Id).Steps, "the older Save has finished");
        Assert.AreEqual(source.Id, stash.SqlQueryWhileOpen("SELECT id FROM items", row => row.GetInt64(0)).Single());
        CollectionAssert.AreNotEqual(oldBytes, File.ReadAllBytes(source.FilePath));
        Assert.AreSame(session, listed.Editing.Session);
        Assert.IsTrue(listed.Editing.Cancel());
        await AssertMatchesStore(listed, []);
        await listed.Listing.SetQuery("platypus");
        Assert.AreEqual(source.Id, listed.Listing.Items.Single().Id);
    }

    [TestMethod]
    public async Task TheWindowFindsAndHandsOverAnExistingVersionFourCaption()
    {
        using var earlier = await ListedStash.Open();
        var item = await SaveCaption(earlier.Stash, "echidna");
        var earlierRender = File.ReadAllBytes(item.FilePath);

        using var current = await earlier.Restart(stash => VersionFourStore.Make(stash.Folder.StoreFile));
        await current.Listing.SetQuery("echidna");
        Assert.AreEqual(item.Id, current.Listing.Selection.Single());

        var enter = current.Press(Key.Enter, KeyModifiers.None, KeyPlace.SearchBox);
        current.Keys.Released(Key.Enter);
        await enter;

        var handed = current.Clipboard.Written.Single().Content;
        CollectionAssert.AreEqual(new[] { item.FilePath }, handed.FilePaths.ToArray());
        CollectionAssert.AreEqual(earlierRender, File.ReadAllBytes(handed.FilePaths.Single()));
        Assert.AreEqual(1, (await current.Stash.Store!.UseHistory(item.Id)).Value!.Count);
    }

    private static async Task<StashItem> SaveCaption(TestStash stash, string text) =>
        await Save(stash, await stash.ImportPicture("picture", "picture.png", StillInTheEditor.Gradient()), Caption(text));

    private static async Task<EditorSession> OpenFromResults(ListedStash listed, KeyPlace place = KeyPlace.SearchBox)
    {
        await listed.Press(Key.E, KeyModifiers.Control, place);
        Assert.IsTrue(listed.Editing.IsOpen);
        return listed.Editing.Session!;
    }

    private static void ChangeCaption(EditorSession session)
    {
        session.Tool = session.Text();
        session.DoubleClick(new PointF(2, -2));
        Assert.IsTrue(session.Typing, "the saved bar is open for text editing");
        session.SetTypedText("platypus");
    }

    private static void AssertRefusal(ListedStash listed, Outcome refused, string reason)
    {
        Assert.IsFalse(refused.Succeeded);
        Assert.AreEqual(Editing.SaveAsNewItemAction, refused.Failure.Action);
        Assert.Contains(reason, refused.Failure.Reason);
        Assert.AreEqual(refused.Failure.Sentence, listed.Notices.Shown.Single().Text);
        Assert.IsEmpty(listed.Clipboard.Written);
        Assert.IsEmpty(listed.Clipboard.WrittenAgain);
    }

    private static async Task AssertMatchesStore(ListedStash listed, long[] ids)
    {
        var fresh = await listed.Stash.Store!.Search(listed.Listing.Query);
        Assert.IsTrue(fresh.Succeeded, fresh.Failure?.ToString());
        CollectionAssert.AreEquivalent(ids, fresh.Value!.Select(item => item.Id).ToArray());
        CollectionAssert.AreEqual(fresh.Value!.Select(item => item.Id).ToArray(), listed.Listing.Items.Select(item => item.Id).ToArray());
    }
}
