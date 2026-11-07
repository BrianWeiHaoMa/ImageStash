using Avalonia.Input;
using ImageStash.Editor;
using ImageStash.Media;
using ImageStash.Seams;
using ImageStash.Store;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// The item menu over a real stash: its entries for one item and for several, the handoff first, which with a paste
/// target pastes into the app it names, for one item Preview, then Add to or Remove from Favorites by G17's mixed rule, Add tag ▸,
/// Remove tag ▸ while the items carry a tag (their lists are <c>TagListsTests</c>'), Delete last, and nothing that does
/// not work yet (G8, G17, G18); the favorites entries on the items the menu was opened on, recording no use (G17,
/// G10); Save a copy… through the save picker, the file's own bytes written where
/// chosen, a path inside the stash folder refused, the flyout kept while the picker is up, every failure named (G9, G2,
/// G29, G35); Open with… between Save a copy… and Reveal, for one item only (G19, G27; its acts are
/// <c>OpenWithTests</c>'); Reveal in File Explorer on the finished picture's file (G9); Delete on the items the menu was
/// opened on, recording no use (G18, G10).
/// </summary>
[TestClass]
public sealed class ItemMenuTests
{
    [TestMethod]
    public void OneItemsMenuIsCopyPreviewEditFavoritesRenameAddTagSaveACopyOpenWithRevealAndDeleteAndSeveralItemsGetCopyFavoritesAddTagAndDelete()
    {
        var one = ItemMenu.Entries(TestItems.Many(1), null);
        var several = ItemMenu.Entries(TestItems.Many(3), null);

        CollectionAssert.AreEqual(new[] { ItemMenuAction.Copy, ItemMenuAction.Preview, ItemMenuAction.Edit, ItemMenuAction.AddToFavorites, ItemMenuAction.Rename, ItemMenuAction.AddTag, ItemMenuAction.SaveACopy, ItemMenuAction.OpenWith, ItemMenuAction.Reveal, ItemMenuAction.Delete }, one.Select(entry => entry.Action).ToList());
        CollectionAssert.AreEqual(new[] { "Copy", "Preview", "Edit", "Add to Favorites", "Rename", "Add tag", "Save a copy…", "Open with…", "Reveal in File Explorer", "Delete" }, one.Select(entry => entry.Label).ToList());
        Assert.AreEqual(new KeyGesture(Key.Enter), one[0].Gesture, "the handoff, with no paste target Copy, by its key (G8)");
        Assert.AreEqual(new KeyGesture(Key.Space), one[1].Gesture, "Preview by Space's key, after the handoff (G19)");
        Assert.AreEqual(new KeyGesture(Key.E, KeyModifiers.Control), one[2].Gesture, "Edit by Ctrl+E's key, after Preview (G20)");
        Assert.AreEqual(new KeyGesture(Key.D, KeyModifiers.Control), one[3].Gesture, "the favorites entry by Ctrl+D's key (G17)");
        Assert.AreEqual(new KeyGesture(Key.F2), one[4].Gesture, "Rename by F2's key, after the favorites entry (G17)");
        Assert.IsNull(one[7].Gesture, "Open with… by no key: the specifications give it none");
        Assert.AreEqual(new KeyGesture(Key.Delete), one[9].Gesture, "Delete by its key (G18)");
        CollectionAssert.AreEqual(new[] { false, false, false, true, false, false, true, false, false, true }, one.Select(entry => entry.AfterSeparator).ToList(), "Open with… and Reveal after Save a copy… with no separator between (G19)");
        CollectionAssert.AreEqual(new[] { ItemMenuAction.Copy, ItemMenuAction.AddToFavorites, ItemMenuAction.AddTag, ItemMenuAction.Delete }, several.Select(entry => entry.Action).ToList());
        CollectionAssert.AreEqual(new[] { "Copy", "Add to Favorites", "Add tag", "Delete 3 items" }, several.Select(entry => entry.Label).ToList());
        CollectionAssert.AreEqual(new KeyGesture?[] { new(Key.Enter), new(Key.D, KeyModifiers.Control), null, new(Key.Delete) }, several.Select(entry => entry.Gesture).ToList());
        CollectionAssert.AreEqual(new[] { false, true, false, true }, several.Select(entry => entry.AfterSeparator).ToList(), "the favorites and tag entries after the handoff, Delete last, each group after a separator");
        Assert.IsTrue(one.Concat(several).All(entry => entry.Tag is null), "the menu's own entries name no tag; their submenus do");
        foreach (var label in one.Concat(several).Select(entry => entry.Label))
        {
            foreach (var unbuilt in new[] { "Paste", "Remove tag", "Revert", "Save as new" })
            {
                Assert.DoesNotContain(unbuilt, label, "nothing whose action is not built yet, or that has nothing to act on, is shown");
            }
        }

        Assert.IsFalse(several.Any(entry => entry.Action == ItemMenuAction.Preview), "the preview shows one item, so several get no Preview (G19)");
        Assert.IsFalse(several.Any(entry => entry.Action == ItemMenuAction.Rename), "Rename edits the one item's name in its preview, so several get no Rename (G17)");
        Assert.IsFalse(several.Any(entry => entry.Action == ItemMenuAction.Edit), "G20: Edit is unavailable with several selected");
        Assert.IsFalse(several.Any(entry => entry.Action == ItemMenuAction.OpenWith), "Open with… opens one file in one dialog, so several get none (G27, S16)");
    }

    [TestMethod]
    public void ThePreviewsMenuIsEditRenameThenSaveACopyOpenWithRevealInFileExplorerAndAfterASeparatorDelete()
    {
        var entries = ItemMenu.PreviewEntries(TestItems.Sized(1, 100, 100));

        CollectionAssert.AreEqual(new[] { ItemMenuAction.Edit, ItemMenuAction.Rename, ItemMenuAction.SaveACopy, ItemMenuAction.OpenWith, ItemMenuAction.Reveal, ItemMenuAction.Delete }, entries.Select(entry => entry.Action).ToList(), "G19's ⋯ menu as far as its actions are built");
        CollectionAssert.AreEqual(new[] { "Edit", "Rename", "Save a copy…", "Open with…", "Reveal in File Explorer", "Delete" }, entries.Select(entry => entry.Label).ToList());
        CollectionAssert.AreEqual(new[] { false, false, true, false, false, true }, entries.Select(entry => entry.AfterSeparator).ToList(), "nothing before Edit and Rename, Save a copy… and Delete each after a separator");
        Assert.AreEqual(new KeyGesture(Key.E, KeyModifiers.Control), entries[0].Gesture, "Edit by Ctrl+E's key, as the preview's Ctrl+E does it (G20)");
        Assert.AreEqual(new KeyGesture(Key.F2), entries[1].Gesture, "Rename by F2's key, as the preview's F2 does it (G17)");
        Assert.AreEqual(new KeyGesture(Key.Delete), entries[5].Gesture, "Delete by Del's key, as the preview's Del does it (G18)");
    }

    [TestMethod]
    public void AnEditedItemsMenusOfferRevertToOriginalAndSaveAsNewItemAfterEditAndNeitherWithSeveralOrOnAnUneditedItem()
    {
        var edited = TestItems.Sized(1, 100, 100) with { Edited = true };
        var other = TestItems.Sized(2, 100, 100) with { Edited = true };

        var one = ItemMenu.Entries([edited], null);
        var pasting = ItemMenu.Entries([edited], RecordingTarget.App("Relay"));
        var two = ItemMenu.Entries([edited, other], null);
        var preview = ItemMenu.PreviewEntries(edited);

        CollectionAssert.AreEqual(new[] { "Copy", "Preview", "Edit", "Revert to original", "Save as new item", "Add to Favorites", "Rename", "Add tag", "Save a copy…", "Open with…", "Reveal in File Explorer", "Delete" },
            one.Select(entry => entry.Label).ToList(), "G19, G26: after Edit, on the one edited item");
        CollectionAssert.AreEqual(new[] { false, false, false, false, false, true, false, false, true, false, false, true }, one.Select(entry => entry.AfterSeparator).ToList());
        CollectionAssert.AreEqual(new[] { ItemMenuAction.RevertToOriginal, ItemMenuAction.SaveAsNewItem }, one.Skip(3).Take(2).Select(entry => entry.Action).ToList());
        CollectionAssert.AreEqual(new[] { "Paste into Relay", "Copy", "Preview", "Edit", "Revert to original", "Save as new item", "Add to Favorites" }, pasting.Take(7).Select(entry => entry.Label).ToList());
        Assert.IsFalse(two.Any(entry => entry.Action is ItemMenuAction.RevertToOriginal or ItemMenuAction.SaveAsNewItem or ItemMenuAction.Edit), "each acts on one item, so two get neither");
        CollectionAssert.AreEqual(new[] { "Edit", "Rename", "—", "Revert to original", "Save as new item", "—", "Save a copy…", "Open with…", "Reveal in File Explorer", "—", "Delete" },
            preview.SelectMany(entry => entry.AfterSeparator ? new[] { "—", entry.Label } : [entry.Label]).ToList(), "the preview's ⋯ menu on an edited item (G19)");
        CollectionAssert.AreEqual(new[] { "Edit", "Rename", "—", "Save a copy…", "Open with…", "Reveal in File Explorer", "—", "Delete" },
            ItemMenu.PreviewEntries(TestItems.Sized(3, 100, 100)).SelectMany(entry => entry.AfterSeparator ? new[] { "—", entry.Label } : [entry.Label]).ToList(), "and without the middle group on an unedited one");
        Assert.IsTrue(one.Concat(preview).Where(entry => entry.Action is ItemMenuAction.RevertToOriginal or ItemMenuAction.SaveAsNewItem).All(entry => entry.Gesture is null), "neither has a key");
    }

    [TestMethod]
    public void RemoveTagShowsWhileTheItemsCarryATagForOneItemAsForSeveralAfterAddTag()
    {
        var tagged = TestItems.Sized(1, 100, 100) with { Tags = ["x"] };
        var plain = TestItems.Sized(2, 100, 100);
        var other = TestItems.Sized(3, 100, 100);

        CollectionAssert.AreEqual(new[] { "Copy", "Preview", "Edit", "Add to Favorites", "Rename", "Add tag", "Remove tag", "Save a copy…", "Open with…", "Reveal in File Explorer", "Delete" }, ItemMenu.Entries([tagged], null).Select(entry => entry.Label).ToList());
        CollectionAssert.AreEqual(new[] { "Copy", "Add to Favorites", "Add tag", "Remove tag", "Delete 3 items" }, ItemMenu.Entries([plain, tagged, other], null).Select(entry => entry.Label).ToList(), "one of several carrying a tag is enough");
        CollectionAssert.AreEqual(new[] { "Copy", "Add to Favorites", "Add tag", "Delete 2 items" }, ItemMenu.Entries([plain, other], null).Select(entry => entry.Label).ToList(), "nothing to take off, so no Remove tag ▸");
        CollectionAssert.AreEqual(new[] { false, true, false, false, true }, ItemMenu.Entries([plain, tagged, other], null).Select(entry => entry.AfterSeparator).ToList());
        Assert.AreEqual(ItemMenuAction.RemoveTag, ItemMenu.Entries([tagged], null)[6].Action);
    }

    [TestMethod]
    public void WithAPasteTargetTheMenuOpensOnPasteIntoThatAppThenCopyOnItsOwnKey()
    {
        var relay = RecordingTarget.App("Relay");

        var one = ItemMenu.Entries(TestItems.Many(1), relay);
        var several = ItemMenu.Entries(TestItems.Many(3), relay);

        CollectionAssert.AreEqual(new[] { "Paste into Relay", "Copy", "Preview", "Edit", "Add to Favorites", "Rename", "Add tag", "Save a copy…", "Open with…", "Reveal in File Explorer", "Delete" }, one.Select(entry => entry.Label).ToList());
        CollectionAssert.AreEqual(new[] { ItemMenuAction.Paste, ItemMenuAction.Copy, ItemMenuAction.Preview, ItemMenuAction.Edit, ItemMenuAction.AddToFavorites, ItemMenuAction.Rename, ItemMenuAction.AddTag, ItemMenuAction.SaveACopy, ItemMenuAction.OpenWith, ItemMenuAction.Reveal, ItemMenuAction.Delete }, one.Select(entry => entry.Action).ToList());
        CollectionAssert.AreEqual(new KeyGesture?[] { new(Key.Enter), new(Key.C, KeyModifiers.Control), new(Key.Space), new(Key.E, KeyModifiers.Control), new(Key.D, KeyModifiers.Control), new(Key.F2), null, null, null, null, new(Key.Delete) }, one.Select(entry => entry.Gesture).ToList(),
            "Enter pastes, Ctrl+C copies, Space previews, Ctrl+E edits, Ctrl+D favorites, F2 renames, Del deletes (G8, G9, G17, G18, G19, G20)");
        CollectionAssert.AreEqual(new[] { false, false, false, false, true, false, false, true, false, false, true }, one.Select(entry => entry.AfterSeparator).ToList());
        CollectionAssert.AreEqual(new[] { "Paste into Relay", "Copy", "Add to Favorites", "Add tag", "Delete 3 items" }, several.Select(entry => entry.Label).ToList());
        CollectionAssert.AreEqual(new[] { false, false, true, false, true }, several.Select(entry => entry.AfterSeparator).ToList());
    }

    [TestMethod]
    public void TheFavoritesEntryRemovesOnlyWhileEveryItemIsAFavoriteAndAMixedSelectionIsAdded()
    {
        var favorite = TestItems.Sized(1, 100, 100) with { Favorite = true };
        var plain = TestItems.Sized(2, 100, 100);

        string Label(params StashItem[] items) => ItemMenu.Entries(items, null).Single(entry => entry.Action is ItemMenuAction.AddToFavorites or ItemMenuAction.RemoveFromFavorites).Label;

        Assert.AreEqual("Remove from Favorites", Label(favorite));
        Assert.AreEqual("Add to Favorites", Label(plain));
        Assert.AreEqual("Add to Favorites", Label(favorite, plain), "a mixed selection becomes all favorites (G17)");
        Assert.AreEqual("Remove from Favorites", Label(favorite, favorite with { Id = 3 }));
        Assert.AreEqual(ItemMenuAction.RemoveFromFavorites, ItemMenu.Entries([favorite], null)[3].Action);
    }

    [TestMethod]
    public async Task TheFavoritesEntrySetsTheFlagOnTheItemsTheMenuWasOpenedOnAndRecordsNoUse()
    {
        using var menu = await Menu.Open("photo.jpg", "still.png", "animated.gif");
        var opened = menu.Items.Take(2).ToList();
        menu.Listing.Select([menu.Items[2].Id], null);

        var added = await menu.ItemMenu.Act(ItemMenuAction.AddToFavorites, null!, opened, null, () => { }, menu.DialogUp);
        var favorites = (await menu.Stash.Store!.Favorites()).Value!.Select(item => item.Id).ToList();
        var removed = await menu.ItemMenu.Act(ItemMenuAction.RemoveFromFavorites, null!, [opened[0]], null, () => { }, menu.DialogUp);
        var after = (await menu.Stash.Store!.Favorites()).Value!.Select(item => item.Id).ToList();

        Assert.IsTrue(added.Succeeded);
        Assert.IsTrue(removed.Succeeded);
        CollectionAssert.AreEquivalent(opened.Select(item => item.Id).ToList(), favorites, "the items the menu was opened on, not the selection now");
        CollectionAssert.AreEqual(new[] { opened[1].Id }, after);
        CollectionAssert.AreEqual(new[] { menu.Items[2].Id }, menu.Listing.Selection.ToList(), "the listing refreshed, keeping the selection");
        Assert.IsEmpty(menu.Notices.Shown, "a change that succeeded says nothing more");
        Assert.IsEmpty(menu.Uses(), "favoriting is not a use (G10)");
    }

    [TestMethod]
    public async Task ThePasteEntryPastesIntoTheTargetItNamedAndCopyCopiesWithoutHiding()
    {
        using var menu = await Menu.Open("transparent.png");
        var relay = RecordingTarget.App("Relay");
        var hides = 0;

        var pasted = await menu.ItemMenu.Act(ItemMenuAction.Paste, null!, menu.Items, relay, () => hides++, menu.DialogUp);
        var copied = await menu.ItemMenu.Act(ItemMenuAction.Copy, null!, menu.Items, relay, () => hides++, menu.DialogUp);

        Assert.IsTrue(pasted.Succeeded, pasted.Failure?.Sentence);
        Assert.IsTrue(copied.Succeeded, copied.Failure?.Sentence);
        Assert.AreEqual(relay, menu.Target.Pasted.Single().Target, "one paste, into the app the entry named (G8)");
        Assert.AreEqual(1, hides, "only the paste hides the flyout (G2, G9)");
        Assert.HasCount(2, menu.Clipboard.Written);
        Assert.HasCount(2, menu.Uses());
    }

    [TestMethod]
    public async Task DeleteDeletesTheItemsTheMenuWasOpenedOnNotTheSelectionAsItIsNowAndRecordsNoUse()
    {
        using var menu = await Menu.Open("transparent.png", "photo.jpg", "animated.gif");
        var openedOn = menu.Items.Take(2).ToList();
        menu.Listing.Select([menu.Items[2].Id], menu.Items[2].Id);

        var deleted = await menu.ItemMenu.Act(ItemMenuAction.Delete, null!, openedOn, null, () => Assert.Fail("a delete hides nothing (G2)"), menu.DialogUp);

        Assert.IsTrue(deleted.Succeeded, deleted.Failure?.Sentence);
        CollectionAssert.AreEqual(new[] { menu.Items[2].Id }, menu.Listing.Items.Select(item => item.Id).ToList(), "the two it was opened on are gone, the one selected since stays");
        CollectionAssert.AreEqual(openedOn.Select(item => item.FilePath).ToList(), menu.Stash.RecycleBin.Asked);
        Assert.AreEqual("Deleted “transparent” and “photo”.", menu.Notices.Shown.Single().Text);
        Assert.IsEmpty(menu.Clipboard.Written, "Delete is not a handoff");
        Assert.IsEmpty(menu.Uses(), "a Delete records no use (G10)");
    }

    [TestMethod]
    public async Task TheFirstEntryCopiesTheSelectionAsEnterDoes()
    {
        using var menu = await Menu.Open("transparent.png");

        var copied = await menu.ItemMenu.Act(ItemMenuAction.Copy, null!, menu.Items, null, () => Assert.Fail("a copy hides nothing"), menu.DialogUp);

        Assert.IsTrue(copied.Succeeded, copied.Failure?.Sentence);
        var content = menu.Clipboard.Written.Single().Content;
        CollectionAssert.AreEqual(new[] { menu.Items[0].FilePath }, content.FilePaths.ToList());
        Assert.IsNotNull(content.Png, "a single still also as its PNG and bitmap (G8)");
        Assert.IsNotNull(content.Bitmap);
        Assert.HasCount(1, menu.Uses(), "one use (G10)");
    }

    [TestMethod]
    public async Task SaveACopyWritesTheFilesOwnBytesWhereChosenWithThePickerUpMeanwhile()
    {
        using var menu = await Menu.Open("animated.gif");
        var item = menu.Items.Single();
        var stashBytes = File.ReadAllBytes(item.FilePath);
        var chosen = menu.Stash.Outside("party.gif");
        var answer = new TaskCompletionSource<string?>();
        menu.Dialogs.SaveAnswer = () => answer.Task;

        var saving = menu.ItemMenu.SaveACopy(null!, item, menu.DialogUp);
        var upWhilePending = menu.Up;
        answer.SetResult(chosen);
        var saved = await saving;

        Assert.IsTrue(saved.Succeeded, saved.Failure?.Sentence);
        var (title, suggested, type) = menu.Dialogs.SavePickers.Single();
        Assert.AreEqual(("Save a copy", item.FileName), (title, suggested), "the item's file name suggested, extension included");
        Assert.AreEqual("GIF picture", type.Name);
        CollectionAssert.AreEqual(new[] { "*.gif" }, type.Patterns.ToList(), "its one format");
        Assert.IsTrue(upWhilePending, "the flyout stays while the picker is up (G2)");
        Assert.IsFalse(menu.Up);
        CollectionAssert.AreEqual(stashBytes, File.ReadAllBytes(chosen), "the finished picture byte for byte, not re-encoded");
        CollectionAssert.AreEqual(stashBytes, File.ReadAllBytes(item.FilePath), "the stash file unchanged");
        Assert.IsEmpty(menu.Notices.Shown, "a copy that worked says nothing");
        Assert.IsEmpty(menu.Uses(), "Save a copy… is not a use (G10)");
    }

    [TestMethod]
    public async Task SaveACopyOntoAnExistingFileReplacesItOnceThePickerHasAsked()
    {
        using var menu = await Menu.Open("photo.jpg");
        var item = menu.Items.Single();
        var chosen = menu.Stash.Outside("old.jpg");
        File.WriteAllBytes(chosen, [1, 2, 3]);
        menu.Dialogs.SaveAnswer = () => Task.FromResult<string?>(chosen);

        var saved = await menu.ItemMenu.SaveACopy(null!, item, menu.DialogUp);

        Assert.IsTrue(saved.Succeeded, saved.Failure?.Sentence);
        CollectionAssert.AreEqual(File.ReadAllBytes(item.FilePath), File.ReadAllBytes(chosen));
        CollectionAssert.AreEqual(new[] { "*.jpg", "*.jpeg" }, menu.Dialogs.SavePickers.Single().FileType.Patterns.ToList());
    }

    [TestMethod]
    public async Task ACancelledPickerWritesNothingAndSaysNothing()
    {
        using var menu = await Menu.Open("photo.jpg");
        var rootFiles = menu.Stash.RootFiles();

        var saved = await menu.ItemMenu.SaveACopy(null!, menu.Items[0], menu.DialogUp);

        Assert.IsTrue(saved.Succeeded, "a cancel is not a failure");
        Assert.IsFalse(menu.Up);
        Assert.IsEmpty(menu.Notices.Shown);
        CollectionAssert.AreEquivalent(rootFiles, menu.Stash.RootFiles());
    }

    [TestMethod]
    public async Task APickerThatThrowsIsNamedAndTheFlyoutsHideRuleReturns()
    {
        using var menu = await Menu.Open("photo.jpg");
        menu.Dialogs.SaveAnswer = () => throw new InvalidOperationException("the dialog could not open");

        var saved = await menu.ItemMenu.SaveACopy(null!, menu.Items[0], menu.DialogUp);

        Assert.AreEqual("Save a copy failed: the file picker, the dialog could not open", saved.Failure?.Sentence);
        Assert.AreEqual(saved.Failure!.Sentence, menu.Notices.Shown.Single().Text);
        Assert.IsFalse(menu.Up);
    }

    [TestMethod]
    [DataRow(@"{root}\{other}")]
    [DataRow(@"{ROOT}\new name.jpg")]
    [DataRow(@"{root}\originals\photo.jpg")]
    [DataRow(@"{root}\.WORK\photo.jpg")]
    public async Task APathInsideTheStashFolderIsRefusedByNameAndNothingThereChanges(string spelled)
    {
        using var menu = await Menu.Open("photo.jpg", "still.png");
        var (item, other) = (menu.Items[0], menu.Items[1]);
        var otherBytes = File.ReadAllBytes(other.FilePath);
        var rootFiles = menu.Stash.RootFiles();
        var root = menu.Stash.Folder.Root;
        var chosen = spelled.Replace("{root}", root).Replace("{ROOT}", root.ToUpperInvariant()).Replace("{other}", other.FileName.ToUpperInvariant());
        menu.Dialogs.SaveAnswer = () => Task.FromResult<string?>(chosen);

        var saved = await menu.ItemMenu.SaveACopy(null!, item, menu.DialogUp);

        Assert.AreEqual(ItemMenu.SaveACopyAction, saved.Failure?.Action);
        Assert.AreEqual(Path.GetFileName(chosen), saved.Failure!.Subject);
        StringAssert.Contains(saved.Failure.Reason, "stash folder");
        Assert.AreEqual(saved.Failure.Sentence, menu.Notices.Shown.Single().Text, "refused by name (G35)");
        CollectionAssert.AreEqual(otherBytes, File.ReadAllBytes(other.FilePath), "another item's file is not replaced (G29)");
        CollectionAssert.AreEquivalent(rootFiles, menu.Stash.RootFiles());
        Assert.IsTrue(string.Equals(chosen, other.FilePath, StringComparison.OrdinalIgnoreCase) || !File.Exists(chosen), "nothing written there");
        Assert.IsEmpty(menu.Uses());
    }

    [TestMethod]
    public async Task APathReachingTheStashFolderThroughAJunctionIsRefusedToo()
    {
        using var menu = await Menu.Open("photo.jpg", "still.png");
        var (item, other) = (menu.Items[0], menu.Items[1]);
        var otherBytes = File.ReadAllBytes(other.FilePath);
        var alias = menu.Stash.Outside("alias");
        // A junction needs no elevation, where a symbolic link does.
        using (var mklink = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{alias}\" \"{menu.Stash.Folder.Root}\"") { CreateNoWindow = true, UseShellExecute = false })!)
        {
            await mklink.WaitForExitAsync();
        }

        try
        {
            Assert.IsTrue(File.Exists(Path.Combine(alias, other.FileName)), "the junction reaches the stash folder");
            menu.Dialogs.SaveAnswer = () => Task.FromResult<string?>(Path.Combine(alias, other.FileName));

            var saved = await menu.ItemMenu.SaveACopy(null!, item, menu.DialogUp);

            StringAssert.Contains(saved.Failure?.Reason, "stash folder", saved.Failure?.Sentence);
            CollectionAssert.AreEqual(otherBytes, File.ReadAllBytes(other.FilePath), "another item's file is not replaced through another spelling (G29)");
        }
        finally
        {
            // The junction alone, never what it reaches.
            Directory.Delete(alias);
        }
    }

    [TestMethod]
    public async Task AFileMadeThereAfterThePickerReturnedIsNotReplacedWithoutBeingAsked()
    {
        using var menu = await Menu.Open("photo.jpg");
        var item = menu.Items[0];
        var chosen = menu.Stash.Outside("appeared.jpg");
        menu.Dialogs.SaveAnswer = () => Task.FromResult<string?>(chosen);
        // A held stash file keeps the copy retrying while another program makes the chosen file.
        var slow = new ItemMenu(menu.Listing, menu.Notices, null!, null!, null!, null!, menu.OpenWith, menu.Dialogs, TimeSpan.FromSeconds(5));

        Outcome saved;
        using (var held = new FileStream(item.FilePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var saving = slow.SaveACopy(null!, item, menu.DialogUp);
            await Task.Delay(300);
            File.WriteAllBytes(chosen, [9, 9, 9]);
            held.Dispose();
            saved = await saving;
        }

        Assert.AreEqual((ItemMenu.SaveACopyAction, "appeared.jpg"), (saved.Failure?.Action, saved.Failure?.Subject), saved.Failure?.Sentence);
        CollectionAssert.AreEqual(new byte[] { 9, 9, 9 }, File.ReadAllBytes(chosen), "the picker asked about no file there, so none is replaced (G9)");
        Assert.AreEqual(saved.Failure!.Sentence, menu.Notices.Shown.Single().Text);
    }

    [TestMethod]
    public async Task AFileMadeThereAsThePickerClosesIsNotReplacedWithoutBeingAsked()
    {
        using var menu = await Menu.Open("photo.jpg");
        var chosen = menu.Stash.Outside("appeared.jpg");
        menu.Dialogs.SaveAnswer = () => Task.FromResult<string?>(chosen);

        // Another program makes the file once the picker has answered, before the item is read again.
        var saved = await menu.ItemMenu.SaveACopy(null!, menu.Items[0], up =>
        {
            if (!up)
            {
                File.WriteAllBytes(chosen, [9, 9, 9]);
            }
        });

        Assert.AreEqual((ItemMenu.SaveACopyAction, "appeared.jpg"), (saved.Failure?.Action, saved.Failure?.Subject), saved.Failure?.Sentence);
        CollectionAssert.AreEqual(new byte[] { 9, 9, 9 }, File.ReadAllBytes(chosen), "the picker asked about no file there, so none is replaced (G9)");
    }

    [TestMethod]
    public async Task ACopyRetryingAHeldFileNeverWritesThePictureTwoRenamesPutAtItsPath()
    {
        using var menu = await Menu.Open("photo.jpg");
        var store = menu.Stash.Store!;
        var slow = new ItemMenu(menu.Listing, menu.Notices, null!, null!, null!, null!, menu.OpenWith, menu.Dialogs, TimeSpan.FromSeconds(5));
        // Rounds at varied moments: a retry that falls between the two renames finds no file there, and fails for that instead.
        for (var round = 0; round < 16; round++)
        {
            var cat = await menu.Stash.ImportPicture($"cat {round}", $"cat {round}.png", MediaFixtures.DistinctPng(10 + round, 8, 6));
            var other = await menu.Stash.ImportPicture($"other {round}", $"other {round}.png", MediaFixtures.DistinctPng(90 + round, 12, 10));
            var catBytes = File.ReadAllBytes(cat.FilePath);
            var chosen = menu.Stash.Outside($"kept {round}.png");
            menu.Dialogs.SaveAnswer = () => Task.FromResult<string?>(chosen);

            Outcome saved;
            // Held as a scanner holds it (E3), reading barred and renaming allowed: the copy retries while two renames put
            // the other item's picture at the path it is reading.
            using (var held = new FileStream(cat.FilePath, FileMode.Open, FileAccess.Read, FileShare.Delete))
            {
                var saving = slow.SaveACopy(null!, cat, menu.DialogUp);
                await Task.Delay(300 + 17 * round);
                var renames = await Task.WhenAll(store.Rename(cat, $"moved {round}"), store.Rename(other, $"cat {round}"));
                Assert.IsTrue(renames.All(renamed => renamed.Succeeded), string.Join(" ", renames.Select(renamed => renamed.Failure?.Sentence)));
                held.Dispose();
                saved = await saving;
            }

            Assert.AreEqual(saved.Succeeded, File.Exists(chosen), saved.Failure?.Sentence);
            Assert.IsTrue(saved.Succeeded || saved.Failure!.Action == ItemMenu.SaveACopyAction, "a failure is named (G35)");
            if (saved.Succeeded)
            {
                CollectionAssert.AreEqual(catBytes, File.ReadAllBytes(chosen), $"round {round}: the item's own finished picture, never the other's (G9)");
            }
        }
    }

    [TestMethod]
    public async Task ACopyThatCannotBeWrittenIsNamedAndNothingElseChanges()
    {
        using var menu = await Menu.Open("photo.jpg");
        var item = menu.Items[0];
        var held = menu.Stash.Outside("held.jpg");
        File.WriteAllBytes(held, [1, 2, 3]);
        menu.Dialogs.SaveAnswer = () => Task.FromResult<string?>(held);

        Outcome saved;
        using (new FileStream(held, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            saved = await menu.ItemMenu.SaveACopy(null!, item, menu.DialogUp);
        }

        Assert.AreEqual("Save a copy failed: held.jpg, another program is holding it open", saved.Failure?.Sentence, "a held file is retried, then named (G35)");
        Assert.AreEqual(saved.Failure!.Sentence, menu.Notices.Shown.Single().Text);
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, File.ReadAllBytes(held));
        Assert.IsFalse(menu.Up);

        var missingFolder = menu.Stash.Outside(Path.Combine("no such folder", "photo.jpg"));
        menu.Dialogs.SaveAnswer = () => Task.FromResult<string?>(missingFolder);
        var refused = await menu.ItemMenu.SaveACopy(null!, item, menu.DialogUp);

        Assert.AreEqual("Save a copy failed: photo.jpg, its folder does not exist", refused.Failure?.Sentence);
    }

    [TestMethod]
    public async Task SaveACopyAndRevealOfAnItemTheStoreSavedActOnItsRender()
    {
        using var menu = await Menu.Open("still.webp");
        var saved = (await menu.Stash.SaveEdit(menu.Items.Single(), EditedItems.BlackBoxAndBox)).Value!;
        var render = File.ReadAllBytes(menu.Stash.Folder.InRoot("still.png"));
        var chosen = menu.Stash.Outside("party.png");
        menu.Dialogs.SaveAnswer = () => Task.FromResult<string?>(chosen);

        var copied = await menu.ItemMenu.SaveACopy(null!, saved, menu.DialogUp);
        var revealed = await menu.ItemMenu.Act(ItemMenuAction.Reveal, null!, [saved], null, () => { }, menu.DialogUp);

        Assert.IsTrue(copied.Succeeded && revealed.Succeeded, $"{copied.Failure?.Sentence} {revealed.Failure?.Sentence}");
        var (_, suggested, type) = menu.Dialogs.SavePickers.Single();
        Assert.AreEqual(("still.png", "PNG picture"), (suggested, type.Name), "the render's name and format, not the original's (G9)");
        CollectionAssert.AreEqual(new[] { "*.png" }, type.Patterns.ToList());
        CollectionAssert.AreEqual(render, File.ReadAllBytes(chosen), "the render byte for byte");
        CollectionAssert.AreEqual(new[] { menu.Stash.Folder.InRoot("still.png") }, menu.Dialogs.Revealed, "the finished picture's file");
    }

    [TestMethod]
    public async Task RevealActsOnTheFinishedPicturesFileAndNamesAFailure()
    {
        using var menu = await Menu.Open("photo.jpg");
        var item = menu.Items[0];

        var revealed = await menu.ItemMenu.Act(ItemMenuAction.Reveal, null!, menu.Items, null, () => { }, menu.DialogUp);
        menu.Dialogs.RevealAnswer = new Failure("Reveal in File Explorer", item.FileName, "The system cannot find the file specified.");
        var failed = await menu.ItemMenu.Act(ItemMenuAction.Reveal, null!, menu.Items, null, () => { }, menu.DialogUp);

        Assert.IsTrue(revealed.Succeeded);
        CollectionAssert.AreEqual(new[] { item.FilePath, item.FilePath }, menu.Dialogs.Revealed);
        Assert.AreEqual("Reveal in File Explorer failed: photo.jpg, The system cannot find the file specified.", menu.Notices.Shown.Single().Text);
        Assert.AreEqual(menu.Notices.Shown.Single().Text, failed.Failure?.Sentence);
        Assert.IsEmpty(menu.Uses(), "Reveal is not a use (G10)");
    }

    /// <summary>The item menu over an open test stash holding the given fixtures, with recording seams and a dialog flag of its own.</summary>
    private sealed class Menu : IDisposable
    {
        private Menu(TestStash stash)
        {
            Stash = stash;
            Notices = new Notices(stash.Clock);
            Listing = new Listing(failure => Assert.Fail(failure.Sentence));
            Clipboard = new RecordingClipboard(stash.Clock);
            var handoff = new Handoff(Listing, Notices, new Notices(stash.Clock), Clipboard, Target, stash.Clock, TestStash.ShortRetry, TestStash.ShortRetry);
            var editing = new Editing(Listing, Notices, handoff, FinishedPicture.Render, new ColorChoices(), TestStash.ShortRetry);
            OpenWith = new OpenWith(Listing, new Notices(stash.Clock), editing, Dialogs, new SteppedClock(), action => action());
            ItemMenu = new ItemMenu(Listing, Notices, handoff, new Organising(Listing, Notices), new Deletion(Listing, Notices), editing, OpenWith, Dialogs, TestStash.ShortRetry);
        }

        public TestStash Stash { get; }

        public Notices Notices { get; }

        public Listing Listing { get; }

        public RecordingClipboard Clipboard { get; }

        public RecordingDialogs Dialogs { get; } = new();

        public RecordingTarget Target { get; } = new();

        public OpenWith OpenWith { get; }

        public ItemMenu ItemMenu { get; }

        public IReadOnlyList<StashItem> Items { get; private set; } = [];

        /// <summary>Whether the window would hold a dialog up now.</summary>
        public bool Up { get; private set; }

        public Action<bool> DialogUp => up => Up = up;

        public static async Task<Menu> Open(params string[] fixtures)
        {
            var menu = new Menu(new TestStash());
            await menu.Stash.OpenCleanly();
            await menu.Listing.StashOpened(menu.Stash.Store!);
            var import = new Import(menu.Listing, menu.Notices, TestStash.ShortRetry);
            var results = await import.Drop(new DropContents(fixtures.Select(MediaFixtures.Path).Select(path => new DroppedItem(Path.GetFileName(path), path, null, IsFolder: false)).ToList(), false, false, []));
            Assert.IsTrue(results.All(file => file.Result == ImportResultKind.Added), Import.NoticeText(results));
            menu.Items = results.Select(file => file.Item!).ToList();
            foreach (var notice in menu.Notices.Shown.ToList())
            {
                menu.Notices.Close(notice);
            }

            return menu;
        }

        /// <summary>The items of every use recorded; closes the store to read its file.</summary>
        public List<long> Uses()
        {
            Stash.Close();
            return Stash.SqlQuery("SELECT item_id FROM uses", row => row.GetInt64(0));
        }

        public void Dispose()
        {
            OpenWith.Dispose();
            Stash.Dispose();
        }
    }
}
