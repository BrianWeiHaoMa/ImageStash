using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using ImageStash.Files;
using ImageStash.Store;
using ImageStash.Window;
using ImageStash.Windows.Tests.Support;

namespace ImageStash.Windows.Tests.Window;

/// <summary>
/// The preview's name edited in place in the stash window laid out by the real desktop platform, off every display and
/// never activated (G17, G19): F2 in the grid opening the preview on the focused item with its name selected, F2 in the
/// preview, a click on the name and both menus' Rename editing it, F2 in the box the box's; Enter committing through the
/// store, the item and its file carrying the name once the store has it (P1, R9), " 2" while it is taken, an empty name
/// committed; Esc cancelling; the keys inside the field its own (G7) and no paste of its own (N9, R5); a renamed item that
/// left the query still the preview's (G8); a refused rename named, the field closed (G35); focus moving elsewhere ending
/// the edit, and a summon bringing it back as it was left (G1). The summon shows the window on the primary display for
/// a moment; the renames are of the check's own pictures under <c>%TEMP%</c>.
/// </summary>
[TestClass]
public sealed class NameFieldTests
{
    [TestMethod]
    public async Task F2InTheGridOpensThePreviewWithTheNameSelectedAndEnterRenamesTheItemAndItsFileOnceTheStoreHasIt()
    {
        using var shown = ShownWindow.Open();
        var items = await shown.OpenStash(shown.Picture("name one.png"), shown.Picture("name two.png"), shown.Picture("name three.png"));
        var renamed = items[1];
        TrayDesktop.Run(() =>
        {
            shown.Listing.Select(items.Select(item => item.Id), renamed.Id);
            shown.Grid.Focus();
            ShownWindow.Press(shown.Grid, Key.F2);
        });

        TrayDesktop.Run(() =>
        {
            Assert.AreEqual(renamed.Id, shown.Preview.Item?.Id, "the preview on the focused item, the one with the ring");
            CollectionAssert.AreEqual(new[] { renamed.Id }, shown.Listing.Selection.ToList(), "that item alone selected");
            Assert.IsTrue(shown.Preview.EditingName && shown.NameBox.IsFocused, "its name in edit, focus in it (G17)");
            Assert.AreSame(TopLevel.GetTopLevel(shown.Preview), TopLevel.GetTopLevel(shown.NameBox), "in place, inside the window (F4)");
            Assert.AreEqual("name two", shown.NameBox.Text);
            Assert.AreEqual((0, "name two".Length), (shown.NameBox.SelectionStart, shown.NameBox.SelectionEnd), "the name selected, so typing replaces it");
            ShownWindow.Type(shown.NameBox, "dog");
            Assert.AreEqual("dog", shown.NameBox.Text);
            ShownWindow.Press(shown.NameBox, Key.Enter);
            Assert.IsFalse(shown.Preview.EditingName, "Enter closes the field");
            Assert.AreSame(shown.PreviewPicture, shown.Focused, "focus back in the preview");
            Assert.AreEqual("name two", shown.NameField.Shown, "no new name shows before the store has it (G28, R9)");
        });
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Preview.Item?.Name == "dog" && shown.NameField.Shown == "dog"));

        var root = shown.Listing.Store!.Folder.Root;
        TrayDesktop.Run(() =>
        {
            Assert.AreEqual(Path.Combine(root, "dog.png"), shown.Preview.Item!.FilePath, "the file carries the name (P1)");
            Assert.AreEqual("dog", shown.Listing.Items.Single(item => item.Id == renamed.Id).Name, "the grid shows the store's answer");
            Assert.IsTrue(shown.Preview.IsOpen);
        });
        Assert.IsTrue(File.Exists(Path.Combine(root, "dog.png")));
        Assert.IsFalse(File.Exists(renamed.FilePath), "renamed, not copied (G29)");
        Assert.AreEqual("dog", (await shown.Listing.Store!.Item(renamed.Id)).Value!.Name);
        Assert.AreEqual(0L, shown.Uses(), "Enter inside the field is the field's, not a handoff (G19)");

        TrayDesktop.Run(() => ShownWindow.Press(shown.PreviewPicture, Key.Enter));
        await ShownWindow.Eventually(() => shown.Uses() == 1);
        CollectionAssert.AreEqual(new[] { Path.Combine(root, "dog.png") }, TestClipboard.Read().Files.ToList(), "Enter outside the field hands the renamed item off, by its new file (G8, P1)");
    }

    [TestMethod]
    public async Task F2InTheBoxIsTheBoxsAndF2AClickOnTheNameAndBothMenusRenameEditItWhileSeveralGetNoRename()
    {
        using var shown = ShownWindow.Open();
        var items = await shown.OpenStash(shown.Picture("ways one.png"), shown.Picture("ways two.png"));
        TrayDesktop.Run(() =>
        {
            shown.Search.Focus();
            ShownWindow.Press(shown.Search, Key.F2);
            Assert.IsFalse(shown.Preview.IsOpen, "F2 in the box opens nothing (G7)");
            Assert.IsTrue(shown.Search.IsFocused);
        });

        TrayDesktop.Run(() => shown.OpenPreview(items[0]));
        TrayDesktop.Run(() =>
        {
            Assert.IsFalse(shown.Preview.EditingName, "Space opens the preview with the name as it is");
            ShownWindow.Press(shown.PreviewPicture, Key.F2);
            Assert.IsTrue(shown.Preview.EditingName && shown.NameBox.IsFocused, "F2 in the preview");
            ShownWindow.Press(shown.NameBox, Key.Escape);
            Assert.IsFalse(shown.Preview.EditingName);
            shown.NameButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.IsTrue(shown.Preview.EditingName && shown.NameBox.IsFocused, "a click on the name (G17)");
            Assert.AreEqual(items[0].Name, shown.NameBox.SelectedText);
            ShownWindow.Press(shown.NameBox, Key.Escape);
        });

        var more = TrayDesktop.Get(shown.ClickMore);
        TrayDesktop.Run(() =>
        {
            shown.ClickMenuEntry("Rename");
            shown.CloseMenu();
        });
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Preview.EditingName && shown.NameBox.IsFocused));
        TrayDesktop.Run(() =>
        {
            ShownWindow.Press(shown.NameBox, Key.Escape);
            ShownWindow.Press(shown.PreviewPicture, Key.Escape);
            Assert.IsFalse(shown.Preview.IsOpen);
        });

        var one = TrayDesktop.Get(() => shown.RightClick(items[1].Id));
        TrayDesktop.Run(() =>
        {
            shown.ClickMenuEntry("Rename");
            shown.CloseMenu();
        });
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Preview.IsOpen && shown.Preview.EditingName && shown.NameBox.IsFocused));
        TrayDesktop.Run(() =>
        {
            Assert.AreEqual(items[1].Id, shown.Preview.Item!.Id, "the item menu's Rename opens the preview on its item, the name selected, once the menu has closed");
            Assert.AreEqual(items[1].Name, shown.NameBox.SelectedText);
            ShownWindow.Press(shown.NameBox, Key.Escape);
            ShownWindow.Press(shown.PreviewPicture, Key.Escape);
            shown.Listing.Select(items.Select(item => item.Id), items[0].Id);
        });
        var several = TrayDesktop.Get(() => shown.RightClick(items[0].Id));
        TrayDesktop.Run(shown.CloseMenu);

        CollectionAssert.AreEqual(new[] { "Edit", "Rename", "—", "Save a copy…", "Open with…", "Reveal in File Explorer", "—", "Delete" }, more, "the ⋯ menu opens on Edit, then Rename (G19, G20)");
        CollectionAssert.AreEqual(new[] { "Copy", "Preview", "Edit", "Add to Favorites", "Rename", "Add tag", "Save a copy…", "Open with…", "Reveal in File Explorer", "Delete" }, one, "after the favorites entry, the handoff first and Delete last (G8, G18)");
        Assert.DoesNotContain("Rename", several, "several items get no Rename");
        Assert.AreEqual(0L, shown.Uses());
    }

    [TestMethod]
    public async Task EscInsideTheNameCancelsWritingNothingTheSameNameAgainChangesNothingAndThenEnterHandsOff()
    {
        using var shown = ShownWindow.Open();
        var item = (await shown.OpenStash(shown.Picture("cancel me.png"))).Single();
        TrayDesktop.Run(() =>
        {
            shown.OpenPreview(item);
            ShownWindow.Press(shown.PreviewPicture, Key.F2);
            ShownWindow.Type(shown.NameBox, "dog");
            ShownWindow.Press(shown.NameBox, Key.Escape);
            Assert.IsTrue(shown.Preview.IsOpen, "Esc inside the field cancels the edit, not the preview (G19, G2)");
            Assert.IsFalse(shown.Preview.EditingName);
            Assert.AreSame(shown.PreviewPicture, shown.Focused, "focus back in the preview");
            Assert.AreEqual("cancel me", shown.NameField.Shown);

            ShownWindow.Press(shown.PreviewPicture, Key.F2);
            Assert.AreEqual("cancel me", shown.NameBox.Text, "the next edit starts from the name, not from what was cancelled");
            ShownWindow.Press(shown.NameBox, Key.Enter);
        });
        await Task.Delay(500);

        var stored = (await shown.Listing.Store!.Item(item.Id)).Value!;
        Assert.AreEqual(("cancel me", item.FileName), (stored.Name, stored.FileName), "nothing was written");
        Assert.IsTrue(File.Exists(item.FilePath));
        TrayDesktop.Run(() => Assert.AreEqual("", shown.Said(), "nothing is said"));

        TrayDesktop.Run(() => ShownWindow.Press(shown.PreviewPicture, Key.Enter));
        await ShownWindow.Eventually(() => shown.Uses() == 1);
        CollectionAssert.AreEqual(new[] { item.FilePath }, TestClipboard.Read().Files.ToList(), "the paste path still works after the field (G7, G8)");
    }

    [TestMethod]
    public async Task InsideTheNameItsKeysAreItsOwnAndNoneDeletesHandsOffCopiesFavoritesOrClosesThePreview()
    {
        using var shown = ShownWindow.Open();
        var items = await shown.OpenStash(shown.Picture("name keys one.png"), shown.Picture("name keys two.png"));
        TrayDesktop.Run(() =>
        {
            shown.OpenPreview(items[0]);
            ShownWindow.Press(shown.PreviewPicture, Key.F2);
        });
        TestClipboard.PutText("held before");

        var texts = TrayDesktop.Get(() =>
        {
            shown.NameBox.Text = "abcd";
            shown.NameBox.CaretIndex = 2;
            var after = new List<string>();
            foreach (var key in new[] { Key.Delete, Key.Back })
            {
                ShownWindow.Press(shown.NameBox, key);
                after.Add(shown.NameBox.Text ?? "");
            }

            foreach (var (key, modifiers) in new[] { (Key.Space, KeyModifiers.None), (Key.Left, KeyModifiers.None), (Key.Right, KeyModifiers.None), (Key.Up, KeyModifiers.None), (Key.Down, KeyModifiers.None), (Key.F2, KeyModifiers.None), (Key.D, KeyModifiers.Control), (Key.A, KeyModifiers.Control), (Key.C, KeyModifiers.Control) })
            {
                ShownWindow.Press(shown.NameBox, key, modifiers);
            }

            return after;
        });
        await Task.Delay(500);

        CollectionAssert.AreEqual(new[] { "abd", "ad" }, texts, "Del and Backspace edited the name's text (G7)");
        TrayDesktop.Run(() =>
        {
            Assert.HasCount(2, shown.Listing.Items, "Del deleted nothing (G18)");
            Assert.IsFalse(shown.Listing.Items.Any(item => item.Favorite), "Ctrl+D toggled nothing");
            Assert.IsTrue(shown.Preview.IsOpen && shown.Preview.EditingName && shown.NameBox.IsFocused, "the preview and the field still open, focus in it");
            Assert.AreEqual("ad", shown.NameBox.SelectedText, "Ctrl+A selected the field's own text");
            Assert.AreEqual("", shown.Said(), "nothing was deleted, copied or refused");
        });
        Assert.AreEqual(0L, shown.Uses(), "no handoff or copy (G10)");
        Assert.IsEmpty(TestClipboard.Read().Files, "Ctrl+C copied the field's text, and no item went onto the clipboard");

        TrayDesktop.Run(() =>
        {
            ShownWindow.Press(shown.NameBox, Key.Escape);
            ShownWindow.Press(shown.PreviewPicture, Key.Escape);
            Assert.IsFalse(shown.Preview.IsOpen, "outside the field Esc closes the preview (G2)");
        });
        Assert.AreEqual("name keys one", (await shown.Listing.Store!.Item(items[0].Id)).Value!.Name);
    }

    [TestMethod]
    public async Task TheNamesOwnPasteDoesNotReadTheClipboardAndItOffersNoPaste()
    {
        using var shown = ShownWindow.Open();
        var item = (await shown.OpenStash(shown.Picture("paste name.png"))).Single();
        TrayDesktop.Run(() =>
        {
            shown.OpenPreview(item);
            ShownWindow.Press(shown.PreviewPicture, Key.F2);
        });
        TestClipboard.PutText("pasted words");

        TrayDesktop.Run(() =>
        {
            ShownWindow.Press(shown.NameBox, Key.Insert, KeyModifiers.Shift);
            ShownWindow.Press(shown.NameBox, Key.V, KeyModifiers.Control);
            // The text box's own Paste command, which its keys run.
            shown.NameBox.Paste();
        });
        await Task.Delay(500);
        var afterPaste = TrayDesktop.Get(() => shown.NameBox.Text ?? "");
        var offered = TrayDesktop.Get(() =>
        {
            shown.NameBox.ContextFlyout?.ShowAt(shown.NameBox);
            return shown.NameBox.ContextFlyout is MenuFlyout menu ? menu.Items.OfType<MenuItem>().Select(entry => $"{entry.Header}").ToList() : [];
        });

        TrayDesktop.Run(() =>
        {
            Assert.IsTrue(shown.Preview.EditingName);
            Assert.IsNull(shown.NameBox.ContextFlyout, "no menu offering a Paste that does nothing (G35)");
            Assert.IsNull(shown.NameBox.ContextMenu);
        });
        Assert.IsFalse(offered.Any(entry => entry.Contains("Paste", StringComparison.OrdinalIgnoreCase)));
        Assert.AreEqual("paste name", afterPaste, "the clipboard's text was never read into the field (N9, R5)");
        Assert.AreEqual(0L, shown.Uses());
    }

    [TestMethod]
    public async Task ARenamedItemThatLeavesTheQueryStaysInThePreviewWhoseEnterAndButtonHandItOff()
    {
        var target = new PresetTarget();
        using var shown = ShownWindow.Open(target: target);
        var items = await shown.OpenStash(shown.Picture("cat 1.png"), shown.Picture("cat 2.png"));
        TrayDesktop.Run(() => shown.Search.Text = "cat");
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled && shown.Listing.Items.Count == 2));
        var previewed = items[0];
        TrayDesktop.Run(() =>
        {
            shown.OpenPreview(previewed);
            ShownWindow.Press(shown.PreviewPicture, Key.F2);
            ShownWindow.Type(shown.NameBox, "dog");
            ShownWindow.Press(shown.NameBox, Key.Enter);
        });
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled && shown.Preview.Item?.Name == "dog" && shown.Listing.Items.Count == 1));

        TrayDesktop.Run(() =>
        {
            Assert.IsTrue(shown.Preview.IsOpen, "an item that only left the query keeps the preview on it");
            Assert.AreEqual(previewed.Id, shown.Preview.Item!.Id);
            Assert.AreEqual(items[1].Id, shown.Listing.Items.Single().Id, "the grid beneath lists the query's other match");
            ShownWindow.Press(shown.PreviewPicture, Key.Enter);
        });
        await ShownWindow.Eventually(() => shown.Uses() == 1);
        TrayDesktop.Run(() => shown.HandoffButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
        await ShownWindow.Eventually(() => shown.Uses() == 2);

        Assert.AreEqual(2, (await shown.Listing.Store!.UseHistory(previewed.Id)).Value!.Count, "both on the renamed item, not on the grid's first match (G8, G10)");
        Assert.AreEqual(0, (await shown.Listing.Store!.UseHistory(items[1].Id)).Value!.Count);
        CollectionAssert.AreEqual(new[] { Path.Combine(shown.Listing.Store!.Folder.Root, "dog.png") }, TestClipboard.Read().Files.ToList());
    }

    [TestMethod]
    public async Task ATakenNameGetsTwoRevealShowsTheFileByItsNewNameAndAnEmptyNameIsCommitted()
    {
        var dialogs = new PresetDialogs();
        using var shown = ShownWindow.Open(dialogs: dialogs);
        var items = await shown.OpenStash(shown.Picture("dog.png"), shown.Picture("cat.png"));
        var cat = items[1];
        TrayDesktop.Run(() =>
        {
            shown.OpenPreview(cat);
            ShownWindow.Press(shown.PreviewPicture, Key.F2);
            ShownWindow.Type(shown.NameBox, "dog");
            ShownWindow.Press(shown.NameBox, Key.Enter);
        });
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Preview.Item?.FileName == "dog 2.png"));
        TrayDesktop.Run(() =>
        {
            shown.ClickMore();
            shown.ClickMenuEntry("Reveal in File Explorer");
            shown.CloseMenu();
        });

        var root = shown.Listing.Store!.Folder.Root;
        CollectionAssert.AreEqual(new[] { Path.Combine(root, "dog 2.png") }, dialogs.Revealed, "File Explorer is shown the file by the item's name, \" 2\" while taken (P1)");
        Assert.IsTrue(File.Exists(Path.Combine(root, "dog.png")) && File.Exists(Path.Combine(root, "dog 2.png")) && !File.Exists(cat.FilePath));

        TrayDesktop.Run(() =>
        {
            ShownWindow.Press(shown.PreviewPicture, Key.F2);
            Assert.IsTrue(shown.Preview.EditingName && shown.NameBox.IsFocused, "in edit after the menu");
            Assert.AreEqual("dog", shown.NameBox.SelectedText);
        });
        // The next key once the box has been laid out, as a person's next keystroke comes.
        TrayDesktop.Run(() =>
        {
            ShownWindow.Press(shown.NameBox, Key.Back);
            Assert.AreEqual("", shown.NameBox.Text, "Backspace takes the selected name away");
            ShownWindow.Press(shown.NameBox, Key.Enter);
        });
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Preview.Item?.FileName == "Untitled.png"));

        TrayDesktop.Run(() =>
        {
            Assert.AreEqual("", shown.Preview.Item!.Name, "nothing requires a name (G17)");
            Assert.AreEqual("", shown.NameField.Shown);
            CollectionAssert.AreEqual(new[] { "Untitled.png" }, ShownWindow.Words(shown.NameButton), "the head shows the file it is named by meanwhile");
        });
        Assert.IsTrue(File.Exists(Path.Combine(root, "Untitled.png")));
        TrayDesktop.Run(() => Assert.AreEqual("", shown.Said(), "nothing is said"));
    }

    [TestMethod]
    public async Task ARenameRefusedForAHeldFileIsNamedTheFieldClosedAndThePreviewShowsTheStoresAnswerUntilTheSameNameAgainFinishesIt()
    {
        using var shown = ShownWindow.Open();
        var cat = (await shown.OpenStash(shown.Picture("cat.png"))).Single();
        var root = shown.Listing.Store!.Folder.Root;
        // The preview as a person sees it before renaming: shown, and its opening read applied.
        TrayDesktop.Run(() => shown.OpenPreview(cat));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Preview.Facts.Count > 0));
        TrayDesktop.Run(() =>
        {
            ShownWindow.Press(shown.PreviewPicture, Key.F2);
            ShownWindow.Type(shown.NameBox, "dog");
        });

        using (new FileStream(cat.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            TrayDesktop.Run(() =>
            {
                ShownWindow.Press(shown.NameBox, Key.Enter);
                Assert.AreEqual("cat", shown.NameField.Shown, "the name as it was until the store answers (G28)");
                Assert.Contains("Renaming “cat”…", Busy(shown), "the busy state names the rename while the held file is retried (G35)");
            });
            await Task.Delay(300);
            TrayDesktop.Run(() => CollectionAssert.AreEqual(new[] { "cat" }, ShownWindow.Words(shown.NameButton), "the head still reads the name as it was while the store works (G28, R9)"));
            await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Said().Length > 0 && shown.Preview.Item?.Name == "dog"));
        }

        TrayDesktop.Run(() =>
        {
            Assert.AreEqual($"Rename failed: cat.png, {HeldFileRetry.HeldReason}", shown.Said(), "named once, by its file and the reason (G35)");
            Assert.IsTrue(shown.Preview.IsOpen);
            Assert.IsFalse(shown.Preview.EditingName, "the field closed, the window's keys free");
            Assert.AreEqual("dog", shown.NameField.Shown, "the store's answer: the name committed");
            Assert.AreEqual(cat.FilePath, shown.Preview.Item!.FilePath, "the picture on disk where it is (State: A live failure)");
            Assert.IsEmpty(Busy(shown));
        });

        TrayDesktop.Run(() =>
        {
            ShownWindow.Press(shown.PreviewPicture, Key.F2);
            Assert.AreEqual("dog", shown.NameBox.Text);
            ShownWindow.Press(shown.NameBox, Key.Enter);
        });
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Preview.Item?.FilePath == Path.Combine(root, "dog.png")));

        Assert.IsTrue(File.Exists(Path.Combine(root, "dog.png")), "once released, the same name again finishes the rename");
        Assert.IsFalse(File.Exists(cat.FilePath));
        TrayDesktop.Run(() => Assert.AreEqual($"Rename failed: cat.png, {HeldFileRetry.HeldReason}", shown.Said(), "and says nothing more"));
    }

    [TestMethod]
    public async Task AHandoffAndAnEscPressedWhileARenameIsRetriedWaitForItSoTheFileHandedOverIsTheRenamedOne()
    {
        using var shown = ShownWindow.Open();
        var cat = (await shown.OpenStash(shown.Picture("cat.png"))).Single();
        var dog = Path.Combine(shown.Listing.Store!.Folder.Root, "dog.png");
        TrayDesktop.Run(() =>
        {
            shown.OpenPreview(cat);
            ShownWindow.Press(shown.PreviewPicture, Key.F2);
            ShownWindow.Type(shown.NameBox, "dog");
        });
        TestClipboard.PutText("held before");

        using (var scanner = new FileStream(cat.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            TrayDesktop.Run(() =>
            {
                ShownWindow.Press(shown.NameBox, Key.Enter);
                ShownWindow.Press(shown.PreviewPicture, Key.Enter);
                ShownWindow.Press(shown.PreviewPicture, Key.Escape);
            });
            await Task.Delay(700);
            TrayDesktop.Run(() => Assert.IsTrue(shown.Preview.IsOpen, "Esc closes the preview only once the rename has been answered"));
            Assert.AreEqual(0L, shown.Uses(), "the handoff waits for the rename");
            Assert.IsEmpty(TestClipboard.Read().Files, "nothing handed over while the file is still to be renamed");
        }

        await ShownWindow.Eventually(() => shown.Uses() == 1 && TrayDesktop.Get(() => !shown.Preview.IsOpen));
        CollectionAssert.AreEqual(new[] { dog }, TestClipboard.Read().Files.ToList(), "the file as renamed, which exists, not the one it was (G8, P1)");
        Assert.IsTrue(File.Exists(dog));

        TrayDesktop.Run(() => ShownWindow.Press(shown.Grid, Key.Enter));
        await ShownWindow.Eventually(() => shown.Uses() == 2);
        CollectionAssert.AreEqual(new[] { dog }, TestClipboard.Read().Files.ToList(), "the grid the preview uncovered lists it as renamed");
    }

    [TestMethod]
    public async Task AnEscThenAnEnterPressedWhileARenameIsRetriedCloseThePreviewAndStillHandTheRenamedFileOver()
    {
        using var shown = ShownWindow.Open();
        var cat = (await shown.OpenStash(shown.Picture("cat.png"))).Single();
        var dog = Path.Combine(shown.Listing.Store!.Folder.Root, "dog.png");
        TrayDesktop.Run(() =>
        {
            shown.OpenPreview(cat);
            ShownWindow.Press(shown.PreviewPicture, Key.F2);
            ShownWindow.Type(shown.NameBox, "dog");
        });
        TestClipboard.PutText("held before");

        using (var scanner = new FileStream(cat.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            TrayDesktop.Run(() =>
            {
                ShownWindow.Press(shown.NameBox, Key.Enter);
                ShownWindow.Press(shown.PreviewPicture, Key.Escape);
                ShownWindow.Press(shown.PreviewPicture, Key.Enter);
            });
            await Task.Delay(700);
            TrayDesktop.Run(() => Assert.IsTrue(shown.Preview.IsOpen, "Esc closes the preview only once the rename has been answered"));
            Assert.AreEqual(0L, shown.Uses(), "the handoff waits for the rename");
        }

        // Both keys were pressed while the preview showed the item: the close and the handoff both happen (G2, G8).
        await ShownWindow.Eventually(() => shown.Uses() == 1 && TrayDesktop.Get(() => !shown.Preview.IsOpen));
        CollectionAssert.AreEqual(new[] { dog }, TestClipboard.Read().Files.ToList(), "the file as renamed, which exists (G8, P1)");
        Assert.IsTrue(File.Exists(dog));
    }

    [TestMethod]
    public async Task ADelPressedWhileARenameIsRetriedDeletesItsItemThoughAPasteClosedThePreviewAndAnotherOpenedMeanwhile()
    {
        using var shown = ShownWindow.Open();
        var items = await shown.OpenStash(shown.Picture("queued delete.png"), shown.Picture("opened meanwhile.png"));
        var (pressedOn, openedMeanwhile) = (items[0], items[1]);
        var video = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(pressedOn.FilePath)!, "..", "clip.mp4"));
        File.WriteAllBytes(video, [1, 2, 3]);
        TestClipboard.Put(TestClipboard.FileList(video));
        TrayDesktop.Run(() =>
        {
            shown.OpenPreview(pressedOn);
            ShownWindow.Press(shown.PreviewPicture, Key.F2);
            ShownWindow.Type(shown.NameBox, "renamed");
        });

        using (var scanner = new FileStream(pressedOn.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            TrayDesktop.Run(() =>
            {
                ShownWindow.Press(shown.NameBox, Key.Enter);
                ShownWindow.Press(shown.PreviewPicture, Key.Delete);
                // A paste whose file is skipped by its name ends at once, and its end closes the preview.
                ShownWindow.Press(shown.PreviewPicture, Key.V, KeyModifiers.Control);
                ShownWindow.Release(shown.PreviewPicture, Key.V);
            });
            await ShownWindow.Eventually(() => TrayDesktop.Get(() => !shown.Preview.IsOpen));
            TrayDesktop.Run(() => shown.OpenPreview(openedMeanwhile));
            Assert.IsTrue(File.Exists(pressedOn.FilePath), "the rename still waits for the held file");
        }

        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Notices.Shown.Any(notice => notice.Text.StartsWith("Deleted "))));
        var notice = TrayDesktop.Get(() => shown.Notices.Shown.Single(notice => notice.Text.StartsWith("Deleted ")));
        TrayDesktop.Run(() =>
        {
            Assert.AreEqual("Deleted “renamed”.", notice.Text, "Del acts on the item it was pressed on, renamed meanwhile (G7, G19)");
            CollectionAssert.AreEqual(new[] { openedMeanwhile.Id }, shown.Listing.Items.Select(item => item.Id).ToList());
            Assert.AreEqual(openedMeanwhile.Id, shown.Preview.Item?.Id, "the item opened meanwhile stays, in the preview");
        });

        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Reachable(notice)));
        TrayDesktop.Run(() => _ = shown.Notices.Undo(notice));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Listing.Items.Count == 2));
        Assert.IsEmpty(RecycleBinEntries.Of(Path.Combine(Path.GetDirectoryName(pressedOn.FilePath)!, "renamed.png")), "Undo took its entry out of the Recycle Bin again");
    }

    [TestMethod]
    public async Task FocusMovingElsewhereInTheWindowEndsTheEditAndASummonBringsAnEditBackAsItWasLeft()
    {
        using var shown = ShownWindow.Open();
        var item = (await shown.OpenStash(shown.Picture("left editing.png"))).Single();
        TrayDesktop.Run(() =>
        {
            shown.OpenPreview(item);
            ShownWindow.Press(shown.PreviewPicture, Key.F2);
            ShownWindow.Type(shown.NameBox, "dropped");
            shown.BackArrow.Focus(NavigationMethod.Tab);
            Assert.IsFalse(shown.Preview.EditingName, "focus moving elsewhere in the window ends the edit as Esc would");
            Assert.AreSame(shown.BackArrow, shown.Focused, "and leaves focus where it went");
            ShownWindow.Press(shown.BackArrow, Key.F2);
            Assert.AreEqual("left editing", shown.NameBox.Text, "nothing was kept of what was typed");
            ShownWindow.Type(shown.NameBox, "do");
        });

        TrayDesktop.Run(shown.Deactivate);
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => !shown.Window.IsVisible));
        TrayDesktop.Run(shown.Window.SummonFromTray);
        await TrayDesktop.Get(() => shown.Listing.Settle());

        TrayDesktop.Run(() =>
        {
            Assert.IsTrue(shown.Window.IsVisible);
            Assert.IsTrue(shown.Preview.IsOpen && shown.Preview.EditingName, "the name still in edit (G1: as it was left)");
            Assert.IsTrue(shown.NameBox.IsFocused, "focus in it");
            Assert.AreEqual("do", shown.NameBox.Text, "with what was typed");
            ShownWindow.Press(shown.NameBox, Key.Escape);
        });
        Assert.AreEqual("left editing", (await shown.Listing.Store!.Item(item.Id)).Value!.Name, "nothing was committed");
    }

    /// <summary>The words of the footer's busy state while it shows; empty while it does not.</summary>
    private static string Busy(ShownWindow shown) =>
        string.Join(" · ", shown.Window.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible && text.Text is { } words && words.StartsWith("Renaming")).Select(text => text.Text));
}
