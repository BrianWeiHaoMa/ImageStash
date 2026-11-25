using Avalonia.Input;
using ImageStash.Store;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// <c>Ctrl+Z</c> in the grid and in the preview (G7), through the window's routing, its one act per press and the act its
/// case runs, over a real store: the Undo of the newest notice that still offers one, passing a newer notice without an
/// Undo or one staying past its time (G16, G18); the box, a field and the editor keep their own; the notifications above
/// the tray are never reached; a refused Undo is named (G35).
/// </summary>
[TestClass]
public sealed class UndoKeyNoticeTests
{
    [TestMethod]
    public async Task AfterAnImportAndADeleteAndAFailureCtrlZUndoesTheDeleteThenTheImportThenNothing()
    {
        using var listed = await ListedStash.Open();
        var kept = await listed.Stash.Import("kept");
        await listed.Organising.AddTag([kept], "cat");
        await listed.Organising.ToggleFavorite([(await listed.Stash.All()).Single()]);
        var before = (await listed.Stash.All()).Single();
        var imported = await listed.Import.Drop(ListedStash.Contents([MediaFixtures.Path("photo.jpg"), MediaFixtures.Path("still.gif")]));
        listed.Listing.Focus(kept.Id);
        await listed.DeleteSelection();
        listed.Notices.Raise(new Failure("Copy", "“kept”", "the clipboard is held"));

        await Undo(listed, KeyPlace.Grid);

        var back = (await listed.Stash.All()).Single(item => item.Id == kept.Id);
        Assert.AreEqual((before.Name, string.Join(",", before.Tags), before.Favorite), (back.Name, string.Join(",", back.Tags), back.Favorite), "back with its data");
        Assert.IsTrue(File.Exists(back.FilePath));
        Assert.HasCount(3, await listed.Stash.All(), "the import's items still there");

        await Undo(listed, KeyPlace.Grid);

        CollectionAssert.AreEqual(new[] { kept.Id }, (await listed.Stash.All()).Select(item => item.Id).ToList(), "the import's batch gone");
        Assert.IsTrue(imported.All(file => !File.Exists(file.Item!.FilePath)));

        await Undo(listed, KeyPlace.Grid);

        CollectionAssert.AreEqual(new[] { kept.Id }, (await listed.Stash.All()).Select(item => item.Id).ToList(), "a third press does nothing");
        Assert.AreEqual("Copy failed: “kept”, the clipboard is held", listed.Notices.Shown.Single().Text);
    }

    [TestMethod]
    public async Task ANoticeStayingPastItsTimeWhoseUndoEndedIsPassedBy()
    {
        using var listed = await ListedStash.Open();
        var cat = await listed.Stash.Import("cat");
        await listed.Listing.Refresh();
        listed.Listing.Focus(cat.Id);
        await listed.DeleteSelection();
        var deleted = listed.Notices.Shown.Single();
        listed.Notices.Hover(deleted, over: true);
        await listed.Import.Drop(ListedStash.Contents([MediaFixtures.Path("photo.jpg"), listed.Stash.Source("clip.mp4", TestStash.Bytes(1))]));
        var staying = listed.Notices.Shown[^1];
        Pass(listed, TimeSpan.FromMinutes(1));
        Assert.IsTrue(listed.Notices.Shown.Contains(staying) && !listed.Notices.OffersUndo(staying), "it stays, its Undo ended");

        await Undo(listed, KeyPlace.Grid);

        Assert.IsTrue((await listed.Stash.All()).Any(item => item.Id == cat.Id), "the older Delete, still offering its Undo, undone");
        Assert.IsTrue((await listed.Stash.All()).Any(item => item.Name == "photo"), "the ended Undo not run");

        await Undo(listed, KeyPlace.Grid);

        Assert.HasCount(2, await listed.Stash.All());
        Assert.AreSame(staying, listed.Notices.Shown.Single());
    }

    [TestMethod]
    public async Task ANewerNoticeWhoseTimeRanOutBeforeAnySweepIsPassedBy()
    {
        using var listed = await ListedStash.Open();
        var (one, two) = (await listed.Stash.Import("one"), await listed.Stash.Import("two"));
        await listed.Listing.Refresh();
        listed.Listing.Focus(one.Id);
        await listed.DeleteSelection();
        listed.Notices.Hover(listed.Notices.Shown.Single(), over: true);
        listed.Listing.Focus(two.Id);
        await listed.DeleteSelection();
        var newer = listed.Notices.Shown[^1];

        listed.Stash.Clock.Now += Notices.LastingFor(newer.Words);
        await Undo(listed, KeyPlace.Grid);

        CollectionAssert.AreEqual(new[] { one.Id }, (await listed.Stash.All()).Select(item => item.Id).ToList(), "the older, still offering its Undo, undone; the newer's time had ended");
        Assert.IsFalse(listed.Notices.Shown.Contains(newer));
    }

    [TestMethod]
    public async Task CtrlZHeldUndoesOnceAndPressedAgainOnceMore()
    {
        using var listed = await ListedStash.Open();
        var (one, two) = (await listed.Stash.Import("one"), await listed.Stash.Import("two"));
        await listed.Listing.Refresh();
        listed.Listing.Focus(one.Id);
        await listed.DeleteSelection();
        listed.Listing.Focus(two.Id);
        await listed.DeleteSelection();

        var held = new[]
        {
            await listed.Press(Key.Z, KeyModifiers.Control, KeyPlace.Grid),
            await listed.Press(Key.Z, KeyModifiers.Control, KeyPlace.Grid),
            await listed.Press(Key.Z, KeyModifiers.Control, KeyPlace.Grid),
        };

        CollectionAssert.AreEqual(new[] { KeyAction.UndoNotice, KeyAction.Held, KeyAction.Held }, held);
        CollectionAssert.AreEqual(new[] { two.Id }, (await listed.Stash.All()).Select(item => item.Id).ToList(), "undone once");

        listed.Keys.Released(Key.Z);
        await listed.Press(Key.Z, KeyModifiers.Control, KeyPlace.Grid);

        CollectionAssert.AreEquivalent(new[] { one.Id, two.Id }, (await listed.Stash.All()).Select(item => item.Id).ToList(), "once more");
    }

    [TestMethod]
    [DataRow(KeyPlace.SearchBox, DisplayName = "the search box keeps its own")]
    [DataRow(KeyPlace.Field, DisplayName = "a name or tag field keeps its own")]
    [DataRow(KeyPlace.Editor, DisplayName = "the editor keeps its own")]
    public async Task CtrlZWhereItIsAnotherControlsOwnRunsNoNoticesUndo(KeyPlace place)
    {
        using var listed = await ListedStash.Open();
        var cat = await listed.Stash.Import("cat");
        await listed.Listing.Refresh();
        listed.Listing.Focus(cat.Id);
        await listed.DeleteSelection();

        Assert.AreEqual(KeyAction.None, await listed.Press(Key.Z, KeyModifiers.Control, place));

        Assert.IsEmpty(await listed.Stash.All());
        Assert.IsTrue(listed.Notices.OffersUndo(listed.Notices.Shown.Single()));
    }

    [TestMethod]
    [DataRow(KeyPlace.Grid, DisplayName = "focus back on the grid")]
    [DataRow(KeyPlace.Preview, DisplayName = "the preview open on another item")]
    [DataRow(KeyPlace.PreviewButton, DisplayName = "one of the preview's buttons focused")]
    public async Task ADeleteFromThePreviewIsUndoneByCtrlZ(KeyPlace place)
    {
        using var listed = await ListedStash.Open();
        var cat = await listed.Stash.Import("cat");
        await listed.Stash.Import("dog");
        // What the preview's Del deletes: its own item, through the window's one Delete (Preview.Delete).
        await listed.Deletion.Delete([cat]);

        await Undo(listed, place);

        Assert.IsTrue((await listed.Stash.All()).Any(item => item.Id == cat.Id));
        Assert.IsEmpty(listed.Notices.Shown);
    }

    [TestMethod]
    public async Task FromTheGridCtrlZBringsADiscardedEditBack()
    {
        using var listed = await ListedStash.Open();
        var cat = await listed.Stash.ImportFixture("cat", "still.png");
        await listed.Listing.Refresh();
        await listed.Editing.Open(cat, EditOrigin.Grid);
        var session = listed.Editing.Session!;
        session.Tool = session.Hide();
        session.DragThrough((1, 1), (4, 3));
        var discarded = session.Layers;
        Assert.IsTrue(listed.Editing.Cancel());

        await Undo(listed, KeyPlace.Grid);

        Assert.IsTrue(listed.Editing.IsOpen, "the discarded edit is back (G20)");
        Assert.AreEqual(discarded, listed.Editing.Session!.Layers);
    }

    [TestMethod]
    public async Task CtrlZNeverRunsTheUndoOfANotificationAboveTheTray()
    {
        using var listed = await ListedStash.Open();
        var ran = false;
        var capture = listed.Notifications.Raise("Screenshot stashed: “cat”.", "Undo", () =>
        {
            ran = true;
            return Task.FromResult(Outcome.Done);
        });

        await Undo(listed, KeyPlace.Grid);

        Assert.IsFalse(ran);
        Assert.IsTrue(listed.Notifications.OffersUndo(capture));
        Assert.IsNull(listed.Notifications.UndoKey, "its Undo button names no key");
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(listed.Notifications.UndoNewest);
    }

    [TestMethod]
    public async Task AnUndoTheStoreRefusesIsNamedAsAFailure()
    {
        using var listed = await ListedStash.Open();
        var lost = await listed.Stash.Import("lost");
        await listed.Listing.Refresh();
        listed.Listing.Focus(lost.Id);
        await listed.DeleteSelection();
        File.Delete(listed.Stash.HeldCopy(lost.Id));

        await Undo(listed, KeyPlace.Grid);

        Assert.AreEqual(new Failure("Undo the delete", lost.FileName, "its held copy is gone").Sentence, listed.Notices.Shown.Single().Text);
    }

    /// <summary><c>Ctrl+Z</c> pressed at <paramref name="place"/> and let go, as the window takes it.</summary>
    private static async Task Undo(ListedStash listed, KeyPlace place)
    {
        Assert.AreEqual(KeyAction.UndoNotice, await listed.Press(Key.Z, KeyModifiers.Control, place));
        listed.Keys.Released(Key.Z);
    }

    /// <summary>Time the window shows passing, the sweep run as the window's timer runs it.</summary>
    private static void Pass(ListedStash listed, TimeSpan shown)
    {
        listed.Stash.Clock.Now += shown;
        listed.Notices.Sweep();
    }
}
