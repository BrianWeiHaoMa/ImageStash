using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using ImageStash.Layers;
using ImageStash.Store;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

[TestClass]
public sealed class TagManagementTests
{
    [TestMethod]
    [DataRow("chip")]
    [DataRow("chip picker")]
    [DataRow("selection picker")]
    [DataRow("preview picker")]
    public async Task TheTagMenuRemovesAllCarriersAndUndoRestoresOnlyThoseCarriers(string origin)
    {
        using var listed = await ListedStash.Open();
        var carrier = await listed.Stash.Import("reaction by tag");
        var outside = await listed.Stash.Import("outside the search");
        var caption = await listed.Stash.ImportFixture("captioned", "still.png");
        var other = await listed.Stash.Import("third carrier after removal");
        var saved = await listed.Stash.SaveEdit(caption,
            new LayerList([new CaptionBarLayer("reaction shared", "Segoe UI", 12, new(0, 0, 0, 255), Outline.None)]));
        Assert.IsTrue(saved.Succeeded, saved.Failure?.Sentence);
        listed.Stash.Tag(carrier.Id, "shared");
        listed.Stash.Tag(outside.Id, "shared");
        listed.Stash.Tag(outside.Id, "keep");

        ControlThread.Run(() => ControlThread.FinishOnDispatcher(async () =>
        {
            await listed.Listing.ShowScope(Scope.All);
            await listed.Listing.SetQuery("reaction shared");
            listed.Listing.Select([carrier.Id], carrier.Id);
            var places = listed.Listing.Chips.Select(chip => chip.Tag).ToArray();
            var source = await OpenOrigin(listed, origin, carrier);
            var command = RemoveCommand(source, "Remove from all 2 items");
            Assert.AreEqual("reaction shared", listed.Listing.Query);
            CollectionAssert.AreEquivalent(new[] { carrier.Id, caption.Id }, listed.Listing.Items.Select(item => item.Id).ToArray());
            CollectionAssert.AreEqual(new[] { "shared" }, await Tags(listed, carrier));

            await listed.WaitForOrganising(() => Click(command));

            CollectionAssert.AreEqual(new[] { caption.Id }, listed.Listing.Items.Select(item => item.Id).ToArray());
            CollectionAssert.AreEqual(places, listed.Listing.Chips.Select(chip => chip.Tag).ToArray());
            Assert.AreEqual(0, listed.Listing.Chips.Single(chip => chip.Tag == "shared").Items);
            CollectionAssert.AreEqual(new[] { "keep" }, await Tags(listed, outside));
            Assert.IsEmpty(await Tags(listed, caption));
            var removal = listed.Notices.Shown.Single();
            Assert.AreEqual("Removed #shared from 2 items", removal.Text);
            Assert.IsTrue(listed.Notices.OffersUndo(removal));

            await listed.Listing.ShowScope(Scope.All);
            listed.Listing.Select([other.Id], other.Id);
            Assert.IsTrue((await listed.Organising.AddTag([other], "shared")).Succeeded);
            if (origin == "chip")
            {
                await listed.Press(Key.Z, KeyModifiers.Control, KeyPlace.Grid);
            }
            else
            {
                await listed.Notices.Undo(removal);
            }

            Assert.AreEqual(3, listed.Listing.Tags.Single(tag => tag.Tag == "shared").Items);
            CollectionAssert.AreEqual(new[] { "shared" }, await Tags(listed, carrier));
            CollectionAssert.AreEquivalent(new[] { "keep", "shared" }, await Tags(listed, outside));
            CollectionAssert.AreEqual(new[] { "shared" }, await Tags(listed, other));
            Assert.IsEmpty(await Tags(listed, caption));
            CollectionAssert.AreEqual(new[] { other.Id }, listed.Listing.SelectedItems.Select(item => item.Id).ToArray());
        }));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task AGreyLiveChipOffersRemovalAndItsUndoEndsOnDismissalOrExpiry(bool expiring)
    {
        using var listed = await ListedStash.Open();
        var carrier = await listed.Stash.Import("outside");
        var match = await listed.Stash.Import("inside");
        listed.Stash.Tag(carrier.Id, "shared");
        listed.Stash.Tag(match.Id, "inside");

        ControlThread.Run(() => ControlThread.FinishOnDispatcher(async () =>
        {
            await listed.Listing.ShowScope(Scope.All);
            var row = Chips(listed);
            Click(Chip(row, "inside"));
            await listed.Listing.Settle();
            var grey = Chip(row, "shared");
            Assert.IsLessThan(1, grey.Opacity);
            Click(grey);
            Assert.AreEqual("inside", listed.Listing.Query);

            var remove = RemoveCommand(grey, "Remove from all 1 item");
            await listed.WaitForOrganising(() => Click(remove));

            Assert.AreSame(grey, Chip(row, "shared"));
            Assert.IsFalse(grey.IsEnabled);
            Assert.IsFalse(RequestMenu(grey).Handled);
            var notice = listed.Notices.Shown.Single();
            Assert.AreEqual("Removed #shared from 1 item", notice.Text);
            Assert.IsTrue(listed.Notices.OffersUndo(notice));
            if (expiring)
            {
                listed.Stash.Clock.Now += TimeSpan.FromSeconds(20);
                listed.Notices.Sweep();
            }
            else
            {
                listed.Notices.Dismiss(notice);
            }

            Assert.IsFalse(listed.Notices.OffersUndo(notice));
            await listed.Notices.Undo(notice);
            Assert.IsEmpty(await Tags(listed, carrier));
        }));
    }

    [TestMethod]
    public async Task AChipThatNarrowsTheMatchesOffersTheWholeStashCount()
    {
        using var listed = await ListedStash.Open();
        var first = await listed.Stash.Import("animal one");
        await listed.Stash.Import("animal two");
        var third = await listed.Stash.Import("elsewhere");
        listed.Stash.Tag(first.Id, "shared");
        listed.Stash.Tag(third.Id, "shared");

        ControlThread.Run(() => ControlThread.FinishOnDispatcher(async () =>
        {
            await listed.Listing.SetQuery("animal ");
            Assert.AreEqual(1, listed.Listing.Chips.Single().Items, "one match carries the narrowing tag");
            var remove = RemoveCommand(Chip(Chips(listed), "shared"), "Remove from all 2 items");
            await listed.WaitForOrganising(() => Click(remove));

            Assert.AreEqual("Removed #shared from 2 items", listed.Notices.Shown.Single().Text);
            Assert.IsEmpty(await Tags(listed, first));
            Assert.IsEmpty(await Tags(listed, third));
        }));
    }

    [TestMethod]
    [DataRow(Key.Enter, KeyModifiers.None, KeyAction.Handoff)]
    [DataRow(Key.Z, KeyModifiers.Control, KeyAction.UndoNotice)]
    [DataRow(Key.Z, KeyModifiers.Control, KeyAction.None)]
    [DataRow(Key.V, KeyModifiers.Control, KeyAction.Paste)]
    [DataRow(Key.Escape, KeyModifiers.None, KeyAction.Escape)]
    public async Task AKeyPressedInTheTagMenuStaysHeldWhenFocusReturnsToTheStash(
        Key key, KeyModifiers modifiers, KeyAction action)
    {
        using var listed = await ListedStash.Open();
        var carrier = await listed.Stash.Import("carrier");
        listed.Stash.Tag(carrier.Id, "shared");
        await listed.Listing.ShowScope(Scope.All);

        ControlThread.Run(() => ControlThread.FinishOnDispatcher(async () =>
        {
            var keys = new PressOnce();
            var chip = Chip(new ChipRow(listed.Listing, listed.Organising, () => null, keys), "shared");
            var remove = RemoveCommand(chip, "Remove from all 1 item");

            chip.ContextMenu!.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers });
            await listed.WaitForOrganising(() => Click(remove));

            var repeat = StashKeys.WithTagListOpen(key, modifiers, keys.Acting(key, action));
            Assert.AreEqual(KeyAction.Held, repeat);
            keys.Released(key);
            Assert.AreEqual(action, keys.Acting(key, action));
        }));
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(3)]
    public async Task AStaleCountChangesNothingAndRefreshesTheNextOffer(int carriersAtClick)
    {
        using var listed = await ListedStash.Open();
        var first = await listed.Stash.Import("one");
        var second = await listed.Stash.Import("two");
        var third = await listed.Stash.Import("three");
        var store = listed.Stash.Store!;
        Assert.IsTrue((await store.AddTag([first, second], "shared")).All(result => result.Succeeded));

        ControlThread.Run(() => ControlThread.FinishOnDispatcher(async () =>
        {
            await listed.Listing.ShowScope(Scope.All);
            var remove = RemoveCommand(Chip(Chips(listed), "shared"), "Remove from all 2 items");
            if (carriersAtClick == 3)
            {
                Assert.IsTrue((await store.AddTag([third], "shared")).Single().Succeeded);
            }
            else
            {
                Assert.IsTrue((await store.RemoveTag(carriersAtClick == 0 ? [first, second] : [second], "shared")).All(result => result.Succeeded));
            }

            await listed.WaitForOrganising(() => Click(remove));

            Assert.AreEqual(carriersAtClick, listed.Listing.Tags.SingleOrDefault()?.Items ?? 0);
            Assert.AreEqual(carriersAtClick, (await listed.Stash.All()).Count(item => item.Tags.Contains("shared")));
            var notice = listed.Notices.Shown.Single();
            StringAssert.Contains(notice.Text, carriersAtClick == 0 ? "no item carries this tag" : $"{carriersAtClick} item");
            Assert.IsFalse(listed.Notices.OffersUndo(notice));
            if (carriersAtClick > 0)
            {
                var fresh = RemoveCommand(Chip(Chips(listed), "shared"),
                    carriersAtClick == 1 ? "Remove from all 1 item" : "Remove from all 3 items");
                await listed.WaitForOrganising(() => Click(fresh));
                Assert.IsEmpty(listed.Listing.Tags);
            }
        }));
    }

    [TestMethod]
    public async Task ARefusedRemovalNamesTheFailureAndLeavesEveryCarrier()
    {
        using var listed = await ListedStash.Open();
        var first = await listed.Stash.Import("one");
        var second = await listed.Stash.Import("two");
        listed.Stash.Tag(first.Id, "shared");
        listed.Stash.Tag(second.Id, "shared");
        listed.Stash.SqlWhileOpen($"CREATE TRIGGER refuse_tag BEFORE DELETE ON tags WHEN OLD.item_id = {second.Id} BEGIN SELECT RAISE(ABORT, 'write refused'); END");

        ControlThread.Run(() => ControlThread.FinishOnDispatcher(async () =>
        {
            await listed.Listing.ShowScope(Scope.All);
            var remove = RemoveCommand(Chip(Chips(listed), "shared"), "Remove from all 2 items");
            await listed.WaitForOrganising(() => Click(remove));

            var notice = listed.Notices.Shown.Single();
            StringAssert.Contains(notice.Text, "Remove tag from all items");
            StringAssert.Contains(notice.Text, "#shared");
            StringAssert.Contains(notice.Text, "write refused");
            Assert.IsFalse(listed.Notices.OffersUndo(notice));
            Assert.AreEqual(2, listed.Listing.Tags.Single().Items);
            CollectionAssert.AreEqual(new[] { "shared" }, await Tags(listed, first));
            CollectionAssert.AreEqual(new[] { "shared" }, await Tags(listed, second));
        }));
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task ExportIncludesAnAcceptedUndoAndEndsAnUndoInAHiddenWindow(bool showing)
    {
        using var listed = await ListedStash.Open();
        using var reopened = await ListedStash.Open();
        var carrier = await listed.Stash.ImportFixture("carrier", "still.png");
        listed.Stash.Tag(carrier.Id, "shared");
        var archive = listed.Stash.Outside("tagged.zip");
        listed.Dialogs.SaveAnswer = () => Task.FromResult<string?>(archive);

        ControlThread.Run(() => ControlThread.FinishOnDispatcher(async () =>
        {
            await listed.Listing.ShowScope(Scope.All);
            var remove = RemoveCommand(Chip(Chips(listed), "shared"), "Remove from all 1 item");
            await listed.WaitForOrganising(() => Click(remove));
            var undo = listed.Notices.Shown.Single();
            Assert.IsTrue(listed.Notices.OffersUndo(undo));

            listed.WindowShowing.Seen(visible: showing, minimised: false);
            const string waitingForUndo = "Waiting for the Undo of a notice before exporting your stash…";
            var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void Changed()
            {
                if (listed.Transferring.ExportLine == waitingForUndo)
                {
                    waiting.TrySetResult();
                }
            }

            listed.Transferring.Changed += Changed;
            var export = listed.Transferring.Export(null!);
            if (showing)
            {
                try
                {
                    Assert.AreSame(waiting.Task, await Task.WhenAny(waiting.Task, export));
                    Assert.IsFalse(export.IsCompleted);
                }
                finally
                {
                    await listed.Notices.Undo(undo);
                }
            }

            listed.Transferring.Changed -= Changed;
            var exported = await export;
            Assert.IsTrue(exported.Succeeded, exported.Failure?.Sentence);
            Assert.IsFalse(listed.Notices.OffersUndo(undo));
            var imported = await reopened.ImportStash(archive);
            Assert.IsTrue(imported.Succeeded, imported.Failure?.Sentence);
            var restored = (await reopened.Listing.Store!.All()).Value!.Single();
            CollectionAssert.AreEqual(showing ? new[] { "shared" } : [], restored.Tags.ToArray());
        }));
    }

    [TestMethod]
    public async Task AMenuFromThePreviousStashCannotRemoveTheReplacementTag()
    {
        using var listed = await ListedStash.Open();
        using var replacement = await ListedStash.Open();
        var before = await listed.Stash.ImportFixture("old carrier", "still.png");
        var after = await replacement.Stash.ImportFixture("replacement carrier", "still.png");
        Assert.AreEqual(before.Id, after.Id);
        listed.Stash.Tag(before.Id, "shared");
        replacement.Stash.Tag(after.Id, "shared");
        var archive = replacement.Stash.Outside("replacement.zip");
        replacement.Dialogs.SaveAnswer = () => Task.FromResult<string?>(archive);
        Assert.IsTrue((await replacement.Transferring.Export(null!)).Succeeded);

        ControlThread.Run(() => ControlThread.FinishOnDispatcher(async () =>
        {
            await listed.Listing.ShowScope(Scope.All);
            var remove = RemoveCommand(Chip(Chips(listed), "shared"), "Remove from all 1 item");
            Assert.IsTrue((await listed.ImportStash(archive)).Succeeded);

            Click(remove);
            Assert.IsFalse(listed.Organising.Changing, "the replaced stash has no live command from the old menu");
            var result = (await listed.Listing.Store!.All()).Value!.Single();

            Assert.AreEqual(after.Name, result.Name);
            CollectionAssert.AreEqual(new[] { "shared" }, result.Tags.ToArray());
            Assert.IsFalse(listed.Notices.Shown.Any(listed.Notices.OffersUndo));
        }));
    }

    [TestMethod]
    public async Task SearchAndCataloguePaintTogetherWhenARemovalCommitsBeforeTheReadReachesTheWindow()
    {
        using var listed = await ListedStash.Open();
        var carrier = await listed.Stash.Import("carrier");
        listed.Stash.Tag(carrier.Id, "shared");
        await listed.Listing.SetQuery("shared");

        ControlThread.Run(() => ControlThread.FinishOnDispatcher(async () =>
        {
            Task reading;
            Task<Outcome<TagRemoval>> removing;
            using (listed.Stash.HoldStore())
            {
                reading = listed.Listing.Refresh();
                removing = listed.Stash.Store!.RemoveTagFromAll("shared", 1);
            }

            // Hold the dispatcher here so the write can land before the completed read is painted.
            var removed = removing.GetAwaiter().GetResult();
            Assert.IsNotNull(removed.Value?.Receipt, removed.Failure?.Sentence);
            await reading;

            CollectionAssert.AreEqual(new[] { carrier.Id }, listed.Listing.Items.Select(item => item.Id).ToArray());
            CollectionAssert.AreEqual(new[] { new TagCount("shared", 1) }, listed.Listing.Tags.ToArray());
            await listed.Listing.Refresh();
            Assert.IsEmpty(listed.Listing.Items);
            Assert.IsEmpty(listed.Listing.Tags);
        }));
    }

    private static async Task<Button> OpenOrigin(ListedStash listed, string origin, StashItem item)
    {
        if (origin is "chip" or "chip picker")
        {
            var row = Chips(listed);
            if (origin == "chip")
            {
                return Chip(row, "shared");
            }

            Click(EveryTag(row));
            return PickerRow(row.Picker, "shared");
        }

        TagField field;
        if (origin == "selection picker")
        {
            var host = new TagFieldOverlay(listed.Listing, listed.Organising, listed.Notices);
            host.Open(listed.Listing.SelectedItems, new Button());
            field = host.Field;
        }
        else
        {
            var preview = new Preview(listed.Listing, listed.Handoff, listed.Organising, listed.Deletion,
                listed.Renaming, listed.Editing, listed.ItemMenu, listed.Notices, listed.Stash.Clock, listed.PasteTarget, _ => { }, new Button());
            preview.Open(item);
            field = preview.GetVisualDescendants().OfType<PreviewTags>().Single().Field;
        }

        var opener = EveryTag(field);
        if (!opener.IsVisible)
        {
            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void Changed(object? sender, AvaloniaPropertyChangedEventArgs args)
            {
                if (args.Property == Visual.IsVisibleProperty && opener.IsVisible)
                {
                    ready.TrySetResult();
                }
            }

            opener.PropertyChanged += Changed;
            try
            {
                await ready.Task;
            }
            finally
            {
                opener.PropertyChanged -= Changed;
            }
        }

        Click(opener);
        return PickerRow(field.Picker, "shared");
    }

    private static ChipRow Chips(ListedStash listed)
    {
        var row = new ChipRow(listed.Listing, listed.Organising, () => null);
        row.Measure(new Size(600, 40));
        return row;
    }

    private static Button Chip(ChipRow row, string tag) => row.Children.OfType<Button>().Single(button => Equals(button.Content, "#" + tag));

    private static Button EveryTag(Control host) => host.GetVisualDescendants().OfType<Button>()
        .Single(button => AutomationProperties.GetName(button) == "Every tag");

    private static Button PickerRow(TagPicker picker, string tag)
    {
        Assert.IsTrue(picker.IsOpen);
        var items = (ItemsControl)((ScrollViewer)((Border)picker.Child!).Child!).Content!;
        var index = picker.Entries.ToList().FindIndex(entry => entry.Tag == tag);
        return (Button)items.ItemTemplate!.Build(items.Items[index])!;
    }

    private static ContextRequestedEventArgs RequestMenu(Control source)
    {
        var request = new ContextRequestedEventArgs { RoutedEvent = InputElement.ContextRequestedEvent };
        source.RaiseEvent(request);
        return request;
    }

    private static MenuItem RemoveCommand(Button source, string label)
    {
        Assert.IsTrue(RequestMenu(source).Handled, "the requested tag has a context menu");
        return source.ContextMenu!.Items.OfType<MenuItem>().Single(item => Equals(item.Header, label));
    }

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static void Click(MenuItem item) => item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

    private static async Task<string[]> Tags(ListedStash listed, StashItem item) =>
        (await listed.Stash.All()).Single(row => row.Id == item.Id).Tags.ToArray();
}
