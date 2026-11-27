using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ImageStash.Layers;
using ImageStash.Seams;
using ImageStash.Store;
using ImageStash.Tests.Support;
using ImageStash.Window;
using static ImageStash.Tests.Support.TagMenuControls;

namespace ImageStash.Tests.Window;

[TestClass]
public sealed partial class TagRenameTests
{
    [TestMethod]
    [DataRow("chip")]
    [DataRow("chip picker")]
    [DataRow("selection picker")]
    [DataRow("preview picker")]
    public async Task RenamingAtTheTagEditsOnlyThatWordFromAnyOrigin(string origin)
    {
        using var listed = await ListedStash.Open();
        var item = await listed.Stash.Import("picture");
        var carrier = await listed.Stash.Import("outside");
        listed.Stash.Tag(item.Id, "focus");
        listed.Stash.Tag(carrier.Id, "shared");

        OnControls(async () =>
        {
            await listed.Listing.ShowScope(Scope.All);
            await listed.Listing.ChooseTag("focus");
            var source = await OpenOrigin(listed, origin, item);
            var before = source.Content;
            if (origin == "chip")
            {
                Assert.IsLessThan(1, source.Opacity, "the queried picture does not carry #shared");
            }

            var cancelled = OpenRename(source);
            cancelled.Text = "discarded";
            Press(cancelled, Key.Escape);
            Assert.AreSame(before, source.Content);
            Assert.IsFalse(listed.Organising.Changing);
            Assert.AreEqual(KeyAction.Held, listed.Keys.Acting(Key.Escape, KeyAction.Escape));
            listed.Keys.Released(Key.Escape);
            Assert.AreEqual("shared", (await Stored(listed, carrier)).Tags.Single());

            var filesAndUses = listed.Stash.FilesAndUses();
            source = await OpenOrigin(listed, origin, item);
            var box = OpenRename(source);
            var presenter = new ContentPresenter { Content = source.Content, ContentTemplate = source.ContentTemplate };
            presenter.UpdateChild();
            Assert.AreSame(box, presenter.Child, "Avalonia must present the editor itself as the tag's face");
            Assert.AreEqual("shared", box.Text);
            Assert.AreEqual("shared", box.SelectedText);
            Assert.AreEqual("Rename tag", AutomationProperties.GetName(box));
            box.Text = "different";
            await listed.WaitForOrganising(() => Press(box, Key.Enter));
            Assert.AreEqual(KeyAction.Held, listed.Keys.Acting(Key.Enter, KeyAction.Handoff));
            listed.Keys.Released(Key.Enter);

            Assert.AreEqual("focus", listed.Listing.Query);
            CollectionAssert.AreEqual(new[] { item.Id }, listed.Listing.Selection.ToArray());
            Assert.AreEqual("focus", (await Stored(listed, item)).Tags.Single());
            var renamed = await Stored(listed, carrier);
            Assert.AreEqual("different", renamed.Tags.Single());
            CollectionAssert.AreEqual(filesAndUses, listed.Stash.FilesAndUses());
            Assert.IsEmpty(listed.Clipboard.Written);

            await listed.Listing.SetQuery("");
            CollectionAssert.AreEqual(new[] { new TagCount("focus", 1), new TagCount("different", 1) },
                listed.Listing.Chips.ToArray(), "the renamed place keeps its place under its new name");
        });
    }

    [TestMethod]
    public async Task AFieldPickerCanRenameATagFromItsLiveCatalogueBeforeTheGridReadsIt()
    {
        using var listed = await ListedStash.Open();
        var item = await listed.Stash.Import("picture");
        OnControls(async () =>
        {
            await listed.Listing.ShowScope(Scope.All);
            var store = listed.Listing.Store!;
            Assert.IsTrue((await store.AddTag([item], "shared")).Single().Succeeded);
            Assert.IsEmpty(listed.Listing.Tags);

            var box = OpenRename(await OpenOrigin(listed, "selection picker", item));
            box.Text = "current";
            await listed.WaitForOrganising(() => Press(box, Key.Enter));
            Assert.AreEqual("current", (await Stored(listed, item)).Tags.Single());
        });
    }

    [TestMethod]
    public async Task AFieldPickerLetsEscapeCancelTheRenameBeforeTheTagFieldHandlesIt()
    {
        using var listed = await ListedStash.Open();
        var item = await listed.Stash.Import("picture");
        listed.Stash.Tag(item.Id, "shared");
        OnControls(async () =>
        {
            await listed.Listing.ShowScope(Scope.All);
            var field = new TagField();
            field.Picker.ConfigureTagManagement(listed.Listing, listed.Organising, listed.Keys, listed.Paste);
            field.Show([], listed.Listing.Tags, []);
            Click(EveryTag(field));
            var source = PickerRow(field.Picker, "shared");
            var box = OpenRename(source);
            // The native popup forwards the key through its owner. Build that tunnel without a desktop.
            field.Children.Add(box);
            box.Text = "discarded";
            Press(box, Key.Escape);
            Assert.IsTrue(field.Picker.IsOpen);
            Assert.IsNotInstanceOfType<TextBox>(source.Content);
            Assert.IsFalse(listed.Organising.Changing);
        });
    }

    [TestMethod]
    public async Task AChipPickerPassesKeyReleaseToTheNextPasteAndKeepsTextUndoLocal()
    {
        using var listed = await ListedStash.Open();
        var item = await listed.Stash.Import("picture");
        listed.Stash.Tag(item.Id, "shared");
        OnControls(async () =>
        {
            await listed.Listing.ShowScope(Scope.All);
            var row = Chips(listed);
            Click(EveryTag(row));
            var source = PickerRow(row.Picker, "shared");
            var box = OpenRename(source);
            var presenter = new ContentPresenter { Content = source.Content };
            presenter.UpdateChild();
            ((Border)row.Picker.Child!).Child = presenter;
            listed.Clipboard.Reading = Outcome<ClipboardReading>.Of(new([], null, null, false, "a"));

            Press(box, Key.V, KeyModifiers.Control);
            box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = Key.V });
            Press(box, Key.V, KeyModifiers.Control);
            Assert.AreEqual(2, listed.Clipboard.Reads);

            box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Z, KeyModifiers = KeyModifiers.Control });
            Press(box, Key.Escape);
            Assert.AreEqual(KeyAction.Held, listed.Keys.Acting(Key.Z, KeyAction.UndoNotice));
        });
    }

    [TestMethod]
    public async Task AChipMenuOpenedBeforeRecasingStartsWithTheSpellingItsChipNowShows()
    {
        using var listed = await ListedStash.Open();
        var item = await listed.Stash.Import("picture");
        listed.Stash.Tag(item.Id, "shared");
        OnControls(async () =>
        {
            await listed.Listing.ShowScope(Scope.All);
            var source = Chip(Chips(listed), "shared");
            Assert.IsTrue(RequestMenu(source).Handled);
            var command = source.ContextMenu!.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Rename tag…"));
            Assert.IsTrue((await listed.Listing.Store!.RenameTag("shared", "SHARED")).Succeeded);
            await listed.Listing.Refresh();

            Click(command);
            var box = (TextBox)source.Content!;
            Assert.AreEqual("SHARED", box.Text);
            Press(box, Key.Enter);
            Assert.AreEqual("SHARED", (await Stored(listed, item)).Tags.Single());
        });
    }

    [TestMethod]
    public async Task ReorderingOtherPickerRowsKeepsTheDraftAtItsOriginalPlace()
    {
        using var listed = await ListedStash.Open();
        var first = await listed.Stash.Import("first");
        var second = await listed.Stash.Import("second");
        var third = await listed.Stash.Import("third");
        foreach (var item in new[] { first, second, third })
        {
            listed.Stash.Tag(item.Id, "alpha");
        }

        listed.Stash.Tag(first.Id, "beta");
        listed.Stash.Tag(first.Id, "zulu");
        OnControls(async () =>
        {
            await listed.Listing.ShowScope(Scope.All);
            var row = Chips(listed);
            Click(EveryTag(row));
            var source = PickerRow(row.Picker, "alpha");
            var box = OpenRename(source);
            box.Text = "current";

            Assert.IsTrue((await listed.Organising.ToggleTag([third], "zulu")).Succeeded);
            CollectionAssert.AreEqual(new[] { "alpha", "beta", "zulu" }, row.Picker.Entries.Select(entry => entry.Tag).ToArray(), "the open list holds the order it opened with");
            Assert.AreSame(box, source.Content);
            Assert.AreEqual("current", box.Text);
            await listed.WaitForOrganising(() => Press(box, Key.Enter));
            Assert.AreEqual(3, listed.Listing.Tags.Single(tag => tag.Tag == "current").Items);
        });
    }

    [TestMethod]
    public async Task AnUntouchedDraftCannotUndoACaseCorrectionAlreadyQueued()
    {
        using var listed = await ListedStash.Open();
        var item = await listed.Stash.Import("picture");
        listed.Stash.Tag(item.Id, "shared");
        OnControls(async () =>
        {
            await listed.Listing.ShowScope(Scope.All);
            var box = OpenRename(Chip(Chips(listed), "shared"));
            box.Text = "SHARED";
            Task corrected;
            using (listed.Stash.HoldStore())
            {
                corrected = listed.WaitForOrganising(() => Press(box, Key.Enter));
                listed.Keys.Released(Key.Enter);
                var untouched = OpenRename(Chip(Chips(listed), "shared"));
                Assert.AreEqual("shared", untouched.Text);
                Press(untouched, Key.Enter);
            }

            await corrected;
            Assert.AreEqual("SHARED", (await Stored(listed, item)).Tags.Single());
        });
    }

    [TestMethod]
    public async Task ACaseCorrectionKeepsChipTargetsAndAMergeChangesOnlyLiveCarriers()
    {
        using var listed = await ListedStash.Open();
        var first = await listed.Stash.Import("match first");
        var outside = await listed.Stash.Import("elsewhere");
        var held = await listed.Stash.Import("held");
        var caption = await listed.Stash.ImportFixture("captioned", "still.png");
        const string before = "iiïiiii";
        const string after = "IIÏIIII";
        foreach (var item in new[] { first, outside, held })
        {
            listed.Stash.Tag(item.Id, before);
        }

        listed.Stash.Tag(first.Id, "alpha");
        listed.Stash.Tag(outside.Id, "Zoë");
        listed.Stash.Tag(caption.Id, "zulu");
        var layers = new LayerList([new CaptionBarLayer($"match {before}", "Segoe UI", 12, new(0, 0, 0, 255), Outline.None)]);
        Assert.IsTrue((await listed.Stash.SaveEdit(caption, layers)).Succeeded);
        var store = listed.Listing.Store!;
        var deleted = (await store.Delete([held.Id])).Single();
        Assert.IsTrue(deleted.Succeeded);

        OnControls(async () =>
        {
            await listed.Listing.ShowScope(Scope.All);
            var row = Chips(listed);
            var size = new Size(650, 40);
            row.Measure(size);
            row.Arrange(new Rect(size));
            var source = Chip(row, before);
            var frame = source.Bounds;
            var neighbor = Chip(row, "alpha");
            var neighborFrame = neighbor.Bounds;
            var box = OpenRename(source);
            box.Text = after;
            await listed.WaitForOrganising(() => Press(box, Key.Enter));
            listed.Keys.Released(Key.Enter);
            row.Measure(size);
            row.Arrange(new Rect(size));

            Assert.AreSame(source, Chip(row, after));
            Assert.AreEqual(frame, source.Bounds);
            Assert.AreSame(neighbor, Chip(row, "alpha"));
            Assert.AreEqual(neighborFrame, neighbor.Bounds);
            Assert.AreEqual("#" + after, AutomationProperties.GetName(source));
            Click(source);
            await listed.Listing.Settle();
            Assert.AreEqual(after, listed.Listing.Query, "primary picks read the live spelling");
            await listed.Listing.SetQuery($"match {before}");
            listed.Listing.Select([caption.Id], caption.Id);
            var merge = OpenRename(Chip(row, after));
            merge.Text = "zoë";
            await listed.WaitForOrganising(() => Press(merge, Key.Enter));
            listed.Keys.Released(Key.Enter);

            Assert.AreEqual("match Zoë", listed.Listing.Query, "the merged tag's word becomes the destination's");
            CollectionAssert.AreEqual(new[] { first.Id }, listed.Listing.Selection.ToArray(), "the caption's match left; the item that took its place is selected");
            Assert.AreEqual(2, listed.Listing.Tags.Single(tag => tag.Tag == "Zoë").Items);
            Assert.IsFalse(Chip(row, after).IsEnabled);
            CollectionAssert.AreEquivalent(new[] { "alpha", "Zoë" }, (await Stored(listed, first)).Tags.ToArray());
            Assert.AreEqual("Zoë", (await Stored(listed, outside)).Tags.Single());
            Assert.AreEqual("zulu", (await Stored(listed, caption)).Tags.Single());

            Assert.IsTrue((await store.UndoDelete([deleted.Value!])).Single().Succeeded);
            Assert.IsEmpty((await listed.Reopen()).Failures);
            Assert.AreEqual(after, (await Stored(listed, held)).Tags.Single());
            Assert.AreEqual("Zoë", (await Stored(listed, outside)).Tags.Single());
        });
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task InvalidInputAndARefusedWriteNameTheTagAndKeepTheCommittedMemberships(bool refuseWrite)
    {
        using var listed = await ListedStash.Open();
        var first = await listed.Stash.Import("first");
        var second = await listed.Stash.Import("second");
        listed.Stash.Tag(first.Id, "shared");
        listed.Stash.Tag(second.Id, "shared");
        listed.Stash.Tag(first.Id, "destination");
        if (refuseWrite)
        {
            listed.Stash.SqlWhileOpen($"CREATE TRIGGER refuse_tag BEFORE DELETE ON tags WHEN OLD.item_id = {second.Id} BEGIN SELECT RAISE(ABORT, 'write refused'); END");
        }

        OnControls(async () =>
        {
            await listed.Listing.ShowScope(Scope.All);
            var box = OpenRename(Chip(Chips(listed), "shared"));
            box.Text = refuseWrite ? "destination" : "two words";
            await listed.WaitForOrganising(() => Press(box, Key.Enter));
            var notice = listed.Notices.Shown.Single();
            // Words that are no tag are refused; a refused write failed (G35).
            StringAssert.Contains(notice.Text, refuseWrite ? "Couldn't rename tag: #shared" : "Can't rename tag (#shared)");
            StringAssert.Contains(notice.Text, refuseWrite ? "write refused" : "enter one tag word");
            Assert.IsFalse(listed.Notices.OffersUndo(notice));
            Assert.AreEqual(2, listed.Listing.Tags.Single(tag => tag.Tag == "shared").Items);
            Assert.AreEqual(1, listed.Listing.Tags.Single(tag => tag.Tag == "destination").Items);
            CollectionAssert.AreEquivalent(new[] { "destination", "shared" }, (await Stored(listed, first)).Tags.ToArray());
            Assert.AreEqual("shared", (await Stored(listed, second)).Tags.Single());
        });
    }

    [TestMethod]
    public async Task PasteReadsOnceAndOnlyTheDraftTakesItsWords()
    {
        using var listed = await ListedStash.Open();
        var item = await listed.Stash.Import("picture");
        listed.Stash.Tag(item.Id, "shared");

        OnControls(async () =>
        {
            await listed.Listing.SetQuery("shared");
            var box = OpenRename(await OpenOrigin(listed, "selection picker", item));
            listed.Clipboard.Reading = Outcome<ClipboardReading>.Of(new([], null, null, false, "two\nwords"));
            Press(box, Key.V, KeyModifiers.Control);
            Press(box, Key.V, KeyModifiers.Control);
            Assert.AreEqual(1, listed.Clipboard.Reads);
            Assert.AreEqual("two words", box.Text);
            Assert.AreEqual("shared", listed.Listing.Query);
            Assert.AreEqual("shared", (await Stored(listed, item)).Tags.Single());
            var nativePaste = new RoutedEventArgs(TextBox.PastingFromClipboardEvent);
            box.RaiseEvent(nativePaste);
            Assert.IsTrue(nativePaste.Handled);
            Assert.AreEqual(1, listed.Clipboard.Reads);

            listed.Keys.Released(Key.V);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            listed.Clipboard.HoldRead = release.Task;
            listed.Clipboard.Reading = Outcome<ClipboardReading>.Of(new([], null, null, false, "late"));
            Press(box, Key.V, KeyModifiers.Control);
            Press(box, Key.Escape);
            release.SetResult();
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            Assert.AreEqual("two words", box.Text);
            Assert.AreEqual(2, listed.Clipboard.Reads);
            Assert.AreEqual("shared", (await Stored(listed, item)).Tags.Single());
        });
    }

    [TestMethod]
    public async Task AWithdrawnDraftCannotWriteToItsReplacementOrToANewPickerRow()
    {
        using var listed = await ListedStash.Open();
        using var replacement = await ListedStash.Open();
        var old = await listed.Stash.ImportFixture("old", "still.png");
        var newItem = await replacement.Stash.ImportFixture("new", "still.png");
        Assert.AreEqual(old.Id, newItem.Id);
        listed.Stash.Tag(old.Id, "shared");
        replacement.Stash.Tag(newItem.Id, "shared");
        var archive = replacement.Stash.Outside("replacement.zip");
        replacement.Dialogs.SaveAnswer = () => Task.FromResult<string?>(archive);
        Assert.IsTrue((await replacement.Transferring.Export(null!)).Succeeded);

        OnControls(async () =>
        {
            await listed.Listing.ShowScope(Scope.All);
            var row = Chips(listed);
            Click(EveryTag(row));
            var entry = PickerRow(row.Picker, "shared");
            var replacedRow = OpenRename(entry);
            replacedRow.Text = "unwanted";
            row.Picker.Show([new TagPickerEntry("another", 1, false)]);
            Press(replacedRow, Key.Enter);
            listed.Keys.Released(Key.Enter);
            Assert.AreEqual("shared", (await Stored(listed, old)).Tags.Single());

            var source = Chip(row, "shared");
            var box = OpenRename(source);
            box.Text = "unwanted";
            Assert.IsTrue((await listed.ImportStash(archive)).Succeeded);
            Assert.AreEqual("#shared", source.Content);
            Press(box, Key.Enter);
            Assert.IsFalse(listed.Organising.Changing);
            var actual = (await listed.Listing.Store!.All()).Value!.Single();
            Assert.AreEqual("new", actual.Name);
            Assert.AreEqual("shared", actual.Tags.Single());
        });
    }

    [TestMethod]
    public async Task ExportWaitsForTheAcceptedMergeAndContainsItsWholeResult()
    {
        using var listed = await ListedStash.Open();
        using var reopened = await ListedStash.Open();
        var first = await listed.Stash.ImportFixture("first", "still.png");
        var second = await listed.Stash.ImportFixture("second", "transparent.png");
        listed.Stash.Tag(first.Id, "shared");
        listed.Stash.Tag(first.Id, "destination");
        listed.Stash.Tag(second.Id, "shared");
        var archive = listed.Stash.Outside("tagged.zip");
        listed.Dialogs.SaveAnswer = () => Task.FromResult<string?>(archive);

        OnControls(async () =>
        {
            await listed.Listing.ShowScope(Scope.All);
            var box = OpenRename(Chip(Chips(listed), "shared"));
            box.Text = "destination";
            Task merged;
            Task<Outcome> export;
            const string waiting = "Waiting for the tag or favorite change before exporting your stash…";
            var state = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            void Changed()
            {
                if (listed.Transferring.ExportLine == waiting)
                {
                    state.TrySetResult(true);
                }
                else if (listed.Listing.WholeStashActBegun)
                {
                    state.TrySetResult(false);
                }
            }

            using (listed.Stash.HoldStore())
            {
                merged = listed.WaitForOrganising(() => Press(box, Key.Enter));
                Assert.IsTrue(listed.Organising.Changing);
                listed.Transferring.Changed += Changed;
                try
                {
                    export = listed.Transferring.Export(null!);
                    Assert.IsTrue(await state.Task, "export must wait before asking the store to copy");
                    Assert.IsFalse(export.IsCompleted);
                }
                finally
                {
                    listed.Transferring.Changed -= Changed;
                }
            }

            await merged;
            // The merge offers Undo, which the export waits out as it waits out every standing Undo (State: Move).
            listed.Stash.Clock.Now += TimeSpan.FromSeconds(30);
            listed.Notices.Sweep();
            var exported = await export;
            Assert.IsTrue(exported.Succeeded, exported.Failure?.Sentence);
            var imported = await reopened.ImportStash(archive);
            Assert.IsTrue(imported.Succeeded, imported.Failure?.Sentence);
            var restored = (await reopened.Listing.Store!.All()).Value!;
            Assert.HasCount(2, restored);
            Assert.IsTrue(restored.All(item => item.Tags.SequenceEqual(["destination"])));
        });
    }

    [TestMethod]
    public async Task ExportRefusesANewRenameOnceCopyingStarts()
    {
        using var listed = await ListedStash.Open();
        using var reopened = await ListedStash.Open();
        var carrier = await listed.Stash.ImportFixture("carrier", "still.png");
        listed.Stash.Tag(carrier.Id, "shared");
        var archive = listed.Stash.Outside("tagged.zip");
        listed.Dialogs.SaveAnswer = () => Task.FromResult<string?>(archive);

        OnControls(async () =>
        {
            await listed.Listing.ShowScope(Scope.All);
            var box = OpenRename(Chip(Chips(listed), "shared"));
            box.Text = "different";
            var tried = false;
            void Changed()
            {
                if (!tried && listed.Listing.WholeStashActBegun)
                {
                    tried = true;
                    Press(box, Key.Enter);
                }
            }

            listed.Transferring.Changed += Changed;
            try
            {
                var exported = await listed.Transferring.Export(null!);
                Assert.IsTrue(exported.Succeeded, exported.Failure?.Sentence);
            }
            finally
            {
                listed.Transferring.Changed -= Changed;
            }

            Assert.IsTrue(tried, "the key was pressed at the start of the copy");
            Assert.IsTrue(listed.Notices.Shown.Any(notice =>
                notice.Text == "Can't rename tag (#shared): your stash is being exported."));
            Assert.AreEqual("shared", (await Stored(listed, carrier)).Tags.Single());
            Assert.IsTrue((await reopened.ImportStash(archive)).Succeeded);
            Assert.AreEqual("shared", (await reopened.Listing.Store!.All()).Value!.Single().Tags.Single());
        });
    }

    private static TextBox OpenRename(Button source)
    {
        Assert.IsTrue(RequestMenu(source).Handled);
        Click(source.ContextMenu!.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Rename tag…")));
        return source.Content as TextBox ?? throw new AssertFailedException("The tag's place did not become a text box.");
    }

    private static void Press(TextBox box, Key key, KeyModifiers modifiers = KeyModifiers.None)
    {
        var args = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers };
        box.RaiseEvent(args);
        Assert.IsTrue(args.Handled);
    }

    private static void OnControls(Func<Task> act) => ControlThread.Run(() => ControlThread.FinishOnDispatcher(act));

    private static async Task<StashItem> Stored(ListedStash listed, StashItem item) =>
        (await listed.Stash.All()).Single(row => row.Id == item.Id);
}
