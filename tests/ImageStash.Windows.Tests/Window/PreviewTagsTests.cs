using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using ImageStash.Store;
using ImageStash.Window;
using ImageStash.Windows.Tests.Support;

namespace ImageStash.Windows.Tests.Window;

/// <summary>
/// The preview's tags row in the stash window laid out by the real desktop platform, off every display and never
/// activated (G17, G19): the item's tags each with its ×, which takes it off; the tag field with suggestions from the
/// existing tags it does not show (N6), Enter committing one tag per word with focus left in the box for the next, Esc
/// cancelling with focus back on the preview, whose Enter then hands off (G7); the row read again from the store after
/// every change, wherever it was made; the keys
/// inside the field its own and no paste of its own (N9, R5), the window's <c>Ctrl+V</c> putting the clipboard's words
/// in its box as typed (G13); a tag refused for an item no longer in the stash named and the preview closed (G35); focus
/// moving away ending the edit as Esc would (G17), and a summon bringing the field back as it was left (G1); the field's
/// list of every tag, whose pick gives the tag to the preview's item or takes it off, and whose Esc closes it alone,
/// wherever focus is, before the preview (G17, G19, G2).
/// The summon shows the window on
/// the primary display for a moment; one check deletes a picture of its own and its Undo removes the bin's entry.
/// </summary>
[TestClass]
public sealed class PreviewTagsTests
{
    [TestMethod]
    public async Task ThePreviewsFieldListsEveryTagAndAPickAddsItToItsItemAndAgainTakesItOffThePreviewAndTheListStayingOpen()
    {
        using var shown = ShownWindow.Open();
        var items = await shown.OpenStash(shown.Picture("list lion.png"), shown.Picture("list other.png"));
        var lion = items.Single(item => item.Name == "list lion");
        await shown.Tag([items.Single(item => item.Name == "list other")], "stare");
        await shown.Tag([lion], "lion");
        await OpenAndRead(shown, lion);
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => ShownWindow.EveryTagButton(shown.PreviewTags.Field).IsEffectivelyVisible));

        TrayDesktop.Run(() =>
        {
            shown.PreviewTagBox.Focus();
            ShownWindow.RaiseClick(ShownWindow.EveryTagButton(shown.PreviewTags.Field));
        });
        TrayDesktop.Run(() =>
        {
            var picker = shown.PreviewTags.Field.Picker;
            CollectionAssert.AreEquivalent(new[] { new TagPickerEntry("lion", true), new TagPickerEntry("stare", false) }, picker.Entries.ToList(), "the same list (G17)");
            ShownWindow.RaiseClick(ShownWindow.Entry(picker, "stare"));
        });
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.PreviewTags.Field.Tags.Contains("stare")));

        CollectionAssert.AreEqual(new[] { "lion", "stare" }, shown.TagsOf(lion.Id), "added to the preview's item (G19)");
        TrayDesktop.Run(() =>
        {
            Assert.IsTrue(shown.Preview.IsOpen && shown.PreviewTagBox.IsFocused, "the preview still open, focus in the box");
            Assert.IsTrue(shown.PreviewTags.Field.Picker.IsOpen && shown.PreviewTags.Field.Picker.Entries.All(entry => entry.Marked), "the list open with it marked");
            ShownWindow.RaiseClick(ShownWindow.Entry(shown.PreviewTags.Field.Picker, "stare"));
        });
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => !shown.PreviewTags.Field.Tags.Contains("stare")));

        CollectionAssert.AreEqual(new[] { "lion" }, shown.TagsOf(lion.Id), "a pick of a tag it carries takes it off, as Add tag ▸ does (G17)");
        TrayDesktop.Run(() =>
        {
            var picker = shown.PreviewTags.Field.Picker;
            Assert.IsTrue(shown.Preview.IsOpen && picker.IsOpen, "the preview and the list still open");
            Assert.IsFalse(picker.Entries.Single(entry => entry.Tag == "stare").Marked, "stare unmarked");
            ShownWindow.Press(shown.PreviewTagBox, Key.Escape);
            Assert.IsFalse(shown.PreviewTags.Field.Picker.IsOpen, "Esc closes the list first");
            Assert.IsTrue(shown.Preview.IsOpen);
        });
    }

    [TestMethod]
    public async Task WithFocusOnThePictureEscClosesTheListTheChevronOpenedAloneAndTheNextEscThePreview()
    {
        using var shown = ShownWindow.Open();
        var items = await shown.OpenStash(shown.Picture("esc lion.png"), shown.Picture("esc other.png"));
        var lion = items.Single(item => item.Name == "esc lion");
        await shown.Tag([items.Single(item => item.Name == "esc other")], "stare");
        await OpenAndRead(shown, lion);
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => ShownWindow.EveryTagButton(shown.PreviewTags.Field).IsEffectivelyVisible));

        // Clicked with the pointer, with focus where the preview's opening left it, not in the field's box.
        TrayDesktop.Run(() => ShownWindow.RaiseClick(ShownWindow.EveryTagButton(shown.PreviewTags.Field)));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.PreviewTags.Field.Picker.IsOpen));
        TrayDesktop.Run(() =>
        {
            Assert.AreSame(shown.PreviewPicture, shown.Focused, "the route that closed the preview with the list: focus on the picture");
            ShownWindow.Press(shown.PreviewPicture, Key.Escape);
            Assert.IsFalse(shown.PreviewTags.Field.Picker.IsOpen, "the list closes (G17)");
            Assert.IsTrue(shown.Preview.IsOpen, "alone: the preview stays (G17, G2)");
            Assert.AreSame(shown.PreviewPicture, shown.Focused, "focus unchanged");
            ShownWindow.Press(shown.PreviewPicture, Key.Escape);
            Assert.IsFalse(shown.Preview.IsOpen, "the next Esc closes the preview (G2)");
        });
    }

    [TestMethod]
    public async Task CtrlVInThePreviewsTagFieldPutsTheClipboardsWordsInItsBoxAsTypedSoEnterTagsEach()
    {
        using var shown = ShownWindow.Open();
        var item = (await shown.OpenStash(shown.Picture("paste preview.png"))).Single();
        await OpenAndRead(shown, item);
        TestClipboard.PutText("lion stare");
        var noticed = TrayDesktop.Get(() => shown.Notices.Shown.Count);

        TrayDesktop.Run(() =>
        {
            shown.PreviewTagBox.Focus();
            ShownWindow.Press(shown.PreviewTagBox, Key.V, KeyModifiers.Control);
            ShownWindow.Release(shown.PreviewTagBox, Key.V);
        });
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.PreviewTagBox.Text == "lion stare"));
        TrayDesktop.Run(() =>
        {
            Assert.HasCount(noticed, shown.Notices.Shown, "no notice (G13)");
            Assert.IsTrue(shown.Preview.IsOpen && shown.PreviewTagBox.IsFocused);
        });
        Assert.IsEmpty(shown.TagsOf(item.Id), "nothing added before Enter");

        TrayDesktop.Run(() => ShownWindow.Press(shown.PreviewTagBox, Key.Enter));
        await ShownWindow.Eventually(() => shown.TagsOf(item.Id).Count == 2);
        CollectionAssert.AreEqual(new[] { "lion", "stare" }, shown.TagsOf(item.Id), "each word a tag on the preview's item (G13, G17)");
    }

    [TestMethod]
    public async Task WordsReadAfterTheirEditWasCancelledLandNowhere()
    {
        using var shown = ShownWindow.Open();
        var item = (await shown.OpenStash(shown.Picture("paste late.png"))).Single();
        await OpenAndRead(shown, item);
        TestClipboard.PutText("too late");
        using (var holder = OtherProgramHoldingTheClipboard.Start())
        {
            TrayDesktop.Run(() =>
            {
                shown.PreviewTagBox.Focus();
                ShownWindow.Press(shown.PreviewTagBox, Key.V, KeyModifiers.Control);
                ShownWindow.Release(shown.PreviewTagBox, Key.V);
                ShownWindow.Press(shown.PreviewTagBox, Key.Escape);
            });
            await Task.Delay(300);
            TrayDesktop.Run(() => shown.PreviewTagBox.Focus());
            holder.Release();
        }

        await Task.Delay(1000);
        TrayDesktop.Run(() => Assert.AreEqual("", shown.PreviewTagBox.Text ?? "", "Esc cancelled the edit the paste was asked in (G19)"));
        Assert.IsEmpty(shown.TagsOf(item.Id));
    }

    [TestMethod]
    public async Task TheRowShowsTheItemsTagsInsideThePreviewAndAnXTakesOneOffWithNothingSaid()
    {
        using var shown = ShownWindow.Open();
        var items = await shown.OpenStash(shown.Picture("row lion.png"), shown.Picture("row other.png"));
        var lion = items.Single(item => item.Name == "row lion");
        await shown.Tag([lion], "lion stare");
        await OpenAndRead(shown, lion);

        TrayDesktop.Run(() =>
        {
            CollectionAssert.AreEqual(new[] { "lion", "stare" }, shown.PreviewTags.Field.Tags.ToList(), "its tags (G19)");
            Assert.IsTrue(shown.PreviewTags.IsEffectivelyVisible);
            Assert.AreSame(shown.Window, TopLevel.GetTopLevel(shown.PreviewTagBox), "the field is inside the stash window, not a window of its own (F4)");
            RemoveButton(shown, "stare").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        });
        await ShownWindow.Eventually(() => shown.TagsOf(lion.Id).Count == 1 && TrayDesktop.Get(() => shown.PreviewTags.Field.Tags.Count == 1));

        CollectionAssert.AreEqual(new[] { "lion" }, shown.TagsOf(lion.Id), "the × took that tag off the item (G17)");
        TrayDesktop.Run(() =>
        {
            CollectionAssert.AreEqual(new[] { "lion" }, shown.PreviewTags.Field.Tags.ToList(), "the row shows the store's answer");
            Assert.IsTrue(shown.Preview.IsOpen);
            Assert.AreEqual("", shown.Said(), "a change that succeeded says nothing more");
        });
        Assert.IsEmpty(shown.TagsOf(items.Single(item => item.Id != lion.Id).Id), "no other item was touched");
        Assert.AreEqual(0L, shown.Uses(), "tagging is not a use (G10)");
    }

    [TestMethod]
    public async Task TypingSuggestsExistingTagsNotCarriedEnterGivesTheItemOneTagPerWordKeepingFocusAndEscOrAnEmptyEnterChangeNothing()
    {
        using var shown = ShownWindow.Open();
        var items = await shown.OpenStash(shown.Picture("field plain.png"), shown.Picture("red rest.png"));
        var plain = items.Single(item => item.Name == "field plain");
        await shown.Tag([plain], "lion");
        await shown.Tag([items.Single(item => item.Id != plain.Id)], "reaction Scream dog");
        await OpenAndRead(shown, plain);

        TrayDesktop.Run(() => ShownWindow.Press(shown.PreviewPicture, Key.Tab));
        TrayDesktop.Run(() =>
        {
            Assert.AreSame(shown.PreviewTagBox, shown.Focused, "Tab reaches the field from the picture (G7)");
            ShownWindow.Type(shown.PreviewTagBox, "re");
        });
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.PreviewTags.Field.Suggestions.Count > 0));
        TrayDesktop.Run(() =>
        {
            CollectionAssert.AreEqual(new[] { "reaction", "Scream" }, shown.PreviewTags.Field.Suggestions.ToList(),
                "the existing tags containing the word, the one starting with it first; never the name red rest (G17, N6)");
            shown.PreviewTagBox.Text = "#Dog cat";
            ShownWindow.Press(shown.PreviewTagBox, Key.Enter);
            Assert.IsTrue(shown.Preview.IsOpen, "Enter inside the field is the field's, not a handoff or a close (G19)");
            Assert.AreSame(shown.PreviewTagBox, shown.Focused, "focus left in the box for the next tag (G17)");
            Assert.AreEqual("", shown.PreviewTagBox.Text, "the field emptied");
        });
        await ShownWindow.Eventually(() => shown.TagsOf(plain.Id).Count == 3 && TrayDesktop.Get(() => shown.PreviewTags.Field.Tags.Count == 3));

        CollectionAssert.AreEqual(new[] { "cat", "dog", "lion" }, shown.TagsOf(plain.Id), "one tag per word, the # dropped, the stored spelling of dog kept (G17)");
        TrayDesktop.Run(() =>
        {
            CollectionAssert.AreEqual(new[] { "cat", "dog", "lion" }, shown.PreviewTags.Field.Tags.ToList(), "the row read again");
            ShownWindow.Type(shown.PreviewTagBox, "d");
        });
        await Task.Delay(300);
        TrayDesktop.Run(() =>
        {
            Assert.IsEmpty(shown.PreviewTags.Field.Suggestions, "dog, which the item carries now, is not suggested (G17)");
            ShownWindow.Type(shown.PreviewTagBox, "ance");
            ShownWindow.Press(shown.PreviewTagBox, Key.Enter);
            Assert.AreSame(shown.PreviewTagBox, shown.Focused, "a second tag, entered the same way");
        });
        await ShownWindow.Eventually(() => shown.TagsOf(plain.Id).Count == 4 && TrayDesktop.Get(() => shown.PreviewTags.Field.Tags.Count == 4));
        CollectionAssert.AreEqual(new[] { "cat", "dance", "dog", "lion" }, shown.TagsOf(plain.Id));

        TrayDesktop.Run(() =>
        {
            ShownWindow.Type(shown.PreviewTagBox, "never");
            ShownWindow.Press(shown.PreviewTagBox, Key.Escape);
            Assert.IsTrue(shown.Preview.IsOpen, "Esc inside the field cancels its edit, not the preview (G19, G2)");
            Assert.AreSame(shown.PreviewPicture, shown.Focused, "focus back in the preview");
            Assert.AreEqual("", shown.PreviewTagBox.Text, "nothing kept of what was cancelled");
        });
        TrayDesktop.Run(() => shown.PreviewTagBox.Focus());
        TrayDesktop.Run(() =>
        {
            ShownWindow.Press(shown.PreviewTagBox, Key.Enter);
            Assert.AreSame(shown.PreviewTagBox, shown.Focused, "an empty Enter changes nothing, focus staying in the box (G17: nothing requires a tag)");
            ShownWindow.Press(shown.PreviewTagBox, Key.Escape);
            Assert.AreSame(shown.PreviewPicture, shown.Focused, "Esc leaves the field");
        });
        await Task.Delay(500);
        CollectionAssert.AreEqual(new[] { "cat", "dance", "dog", "lion" }, shown.TagsOf(plain.Id), "Esc and the empty Enter changed nothing");
        TrayDesktop.Run(() => Assert.AreEqual("", shown.Said(), "nothing was refused or said"));
        Assert.AreEqual(0L, shown.Uses());

        TrayDesktop.Run(() => ShownWindow.Press(shown.PreviewPicture, Key.Enter));
        await ShownWindow.Eventually(() => shown.Uses() == 1);
        CollectionAssert.AreEqual(new[] { plain.FilePath }, TestClipboard.Read().Files.ToList(), "Enter outside the field hands off the previewed item (G7, G8)");
    }

    [TestMethod]
    public async Task DownThenEnterCommitsTheChosenSuggestionAndAClickOnOneCommitsItInPlaceOfTheWordBeingTyped()
    {
        using var shown = ShownWindow.Open();
        var items = await shown.OpenStash(shown.Picture("choose plain.png"), shown.Picture("choose tagged.png"));
        var plain = items.Single(item => item.Name == "choose plain");
        await shown.Tag([items.Single(item => item.Id != plain.Id)], "reaction Scream");
        await OpenAndRead(shown, plain);

        TrayDesktop.Run(() =>
        {
            shown.PreviewTagBox.Focus();
            ShownWindow.Type(shown.PreviewTagBox, "funny rea");
        });
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.PreviewTags.Field.Suggestions.SequenceEqual(["reaction", "Scream"])));
        TrayDesktop.Run(() =>
        {
            ShownWindow.Press(shown.PreviewTagBox, Key.Down);
            Assert.AreEqual("reaction", shown.PreviewTags.Field.Chosen);
            ShownWindow.Press(shown.PreviewTagBox, Key.Enter);
        });
        await ShownWindow.Eventually(() => shown.TagsOf(plain.Id).Count == 2);
        CollectionAssert.AreEqual(new[] { "funny", "reaction" }, shown.TagsOf(plain.Id), "the chosen suggestion in place of the word being typed");

        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.PreviewTags.Field.Tags.Count == 2));
        TrayDesktop.Run(() =>
        {
            shown.PreviewTagBox.Focus();
            ShownWindow.Type(shown.PreviewTagBox, "big scr");
        });
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.PreviewTags.Field.Suggestions.SequenceEqual(["Scream"])));
        TrayDesktop.Run(() => shown.PreviewTags.GetVisualDescendants().OfType<Button>().Single(button => button.Content as string == "Scream").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
        await ShownWindow.Eventually(() => shown.TagsOf(plain.Id).Count == 4);

        CollectionAssert.AreEqual(new[] { "big", "funny", "reaction", "Scream" }, shown.TagsOf(plain.Id), "a click on a suggestion commits what was typed with it");
        TrayDesktop.Run(() =>
        {
            Assert.IsTrue(shown.Preview.IsOpen);
            Assert.AreSame(shown.PreviewTagBox, shown.Focused, "focus left in the box, as Enter leaves it (G17)");
        });
    }

    [TestMethod]
    public async Task InsideTheTagFieldItsKeysAreItsOwnAndNoneDeletesHandsOffFavoritesOrClosesThePreview()
    {
        using var shown = ShownWindow.Open();
        var items = await shown.OpenStash(shown.Picture("tag keys one.png"), shown.Picture("tag keys two.png"));
        await OpenAndRead(shown, items[0]);
        TrayDesktop.Run(() => shown.PreviewTagBox.Focus());
        TestClipboard.PutText("held before");

        var texts = TrayDesktop.Get(() =>
        {
            shown.PreviewTagBox.Text = "abcd";
            shown.PreviewTagBox.CaretIndex = 2;
            var after = new List<string>();
            foreach (var key in new[] { Key.Delete, Key.Back })
            {
                ShownWindow.Press(shown.PreviewTagBox, key);
                after.Add(shown.PreviewTagBox.Text ?? "");
            }

            foreach (var (key, modifiers) in new[] { (Key.Space, KeyModifiers.None), (Key.Left, KeyModifiers.None), (Key.Right, KeyModifiers.None), (Key.Up, KeyModifiers.None), (Key.Down, KeyModifiers.None), (Key.F2, KeyModifiers.None), (Key.D, KeyModifiers.Control), (Key.A, KeyModifiers.Control), (Key.C, KeyModifiers.Control) })
            {
                ShownWindow.Press(shown.PreviewTagBox, key, modifiers);
            }

            return after;
        });
        await Task.Delay(500);

        CollectionAssert.AreEqual(new[] { "abd", "ad" }, texts, "Del and Backspace edited the field's text (G7)");
        TrayDesktop.Run(() =>
        {
            Assert.HasCount(2, shown.Listing.Items, "Del deleted nothing (G18)");
            Assert.IsFalse(shown.Listing.Items.Any(item => item.Favorite), "Ctrl+D toggled nothing");
            Assert.IsTrue(shown.Preview.IsOpen && shown.PreviewTagBox.IsFocused, "the preview still open, focus in the field");
            Assert.IsFalse(shown.Preview.EditingName, "F2 there is the field's, not Rename");
            Assert.AreEqual("ad", shown.PreviewTagBox.SelectedText, "Ctrl+A selected the field's own text");
            Assert.AreEqual("", shown.Said(), "nothing was deleted, copied or refused");
        });
        Assert.AreEqual(0L, shown.Uses(), "no handoff or copy (G10)");
        Assert.IsEmpty(TestClipboard.Read().Files, "Ctrl+C copied the field's text, and no item went onto the clipboard");

        TrayDesktop.Run(() =>
        {
            ShownWindow.Press(shown.PreviewTagBox, Key.Escape);
            Assert.IsTrue(shown.Preview.IsOpen, "Esc inside the field is the field's");
            ShownWindow.Press(shown.PreviewPicture, Key.Escape);
            Assert.IsFalse(shown.Preview.IsOpen, "outside the field Esc closes the preview (G2)");
        });
        Assert.IsEmpty(shown.TagsOf(items[0].Id), "Esc committed nothing");
    }

    [TestMethod]
    public async Task TheTagFieldsOwnPasteDoesNotReadTheClipboardAndItOffersNoPaste()
    {
        using var shown = ShownWindow.Open();
        var item = (await shown.OpenStash(shown.Picture("paste tag.png"))).Single();
        await OpenAndRead(shown, item);
        TrayDesktop.Run(() =>
        {
            shown.PreviewTagBox.Focus();
            ShownWindow.Type(shown.PreviewTagBox, "typed");
        });
        TestClipboard.PutText("pasted words");

        TrayDesktop.Run(() =>
        {
            ShownWindow.Press(shown.PreviewTagBox, Key.Insert, KeyModifiers.Shift);
            // The text box's own Paste command, which its keys run; Ctrl+V is the window's (G13).
            shown.PreviewTagBox.Paste();
        });
        await Task.Delay(500);
        var afterPaste = TrayDesktop.Get(() => shown.PreviewTagBox.Text ?? "");
        var offered = TrayDesktop.Get(() =>
        {
            shown.PreviewTagBox.ContextFlyout?.ShowAt(shown.PreviewTagBox);
            return shown.PreviewTagBox.ContextFlyout is MenuFlyout menu ? menu.Items.OfType<MenuItem>().Select(entry => $"{entry.Header}").ToList() : [];
        });

        TrayDesktop.Run(() =>
        {
            Assert.IsTrue(shown.Preview.IsOpen);
            Assert.IsNull(shown.PreviewTagBox.ContextFlyout, "no menu offering a Paste that does nothing (G35)");
            Assert.IsNull(shown.PreviewTagBox.ContextMenu);
        });
        Assert.IsFalse(offered.Any(entry => entry.Contains("Paste", StringComparison.OrdinalIgnoreCase)));
        Assert.AreEqual("typed", afterPaste, "the clipboard's text was never read into the field (N9, R5)");
        Assert.AreEqual(0L, shown.Uses());
        Assert.IsEmpty(shown.TagsOf(item.Id));
    }

    [TestMethod]
    public async Task TheRowShowsTheTagsTheStoreHoldsAfterAChangeInTheGridAndWhileOpenAfterAChangeElsewhere()
    {
        using var shown = ShownWindow.Open();
        var items = await shown.OpenStash(shown.Picture("reread lion.png"), shown.Picture("reread other.png"));
        var lion = items.Single(item => item.Name == "reread lion");
        await shown.Tag([lion], "lion stare");
        await OpenAndRead(shown, lion);
        TrayDesktop.Run(() => ShownWindow.Press(shown.PreviewPicture, Key.Escape));

        TrayDesktop.Run(() =>
        {
            shown.Grid.Focus();
            shown.Listing.Focus(lion.Id);
            shown.RightClick(lion.Id);
            shown.ClickSubmenuEntry("Remove tag", "stare");
            shown.CloseMenu();
        });
        await ShownWindow.Eventually(() => shown.TagsOf(lion.Id).Count == 1);
        await TrayDesktop.Get(() => shown.Listing.Settle());
        await OpenAndRead(shown, lion);
        TrayDesktop.Run(() => CollectionAssert.AreEqual(new[] { "lion" }, shown.PreviewTags.Field.Tags.ToList(), "reopened, the row shows what the store holds, not a copy (G19)"));

        await shown.Tag([lion], "zebra");
        await shown.Tag([items.Single(item => item.Id != lion.Id)], "zebu");
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.PreviewTags.Field.Tags.SequenceEqual(["lion", "zebra"])));
        TrayDesktop.Run(() =>
        {
            shown.PreviewTagBox.Focus();
            ShownWindow.Type(shown.PreviewTagBox, "zeb");
        });
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.PreviewTags.Field.Suggestions.SequenceEqual(["zebu"])));
        await shown.Tag([lion], "unused");
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.PreviewTags.Field.Tags.SequenceEqual(["lion", "unused", "zebra"])));
        TrayDesktop.Run(() => Assert.AreEqual("zeb", shown.PreviewTagBox.Text, "a re-read keeps what is being typed"));
    }

    [TestMethod]
    public async Task ATagRefusedForAnItemDeletedMeanwhileIsNamedAndThePreviewCloses()
    {
        using var shown = ShownWindow.Open();
        var items = await shown.OpenStash(shown.Picture("tag gone.png"), shown.Picture("tag stays.png"));
        var gone = items.Single(item => item.Name == "tag gone");
        await OpenAndRead(shown, gone);
        var deleted = await shown.Listing.Store!.Delete([gone.Id]);

        TrayDesktop.Run(() =>
        {
            shown.PreviewTagBox.Focus();
            ShownWindow.Type(shown.PreviewTagBox, "late");
            ShownWindow.Press(shown.PreviewTagBox, Key.Enter);
        });
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => !shown.Preview.IsOpen));

        TrayDesktop.Run(() => Assert.IsTrue(shown.Notices.Shown.Any(notice => notice.Text.Contains("tag gone") && notice.Text.EndsWith("it is no longer in the stash")), shown.Said()));
        await shown.Listing.Store!.UndoDelete(deleted.Select(outcome => outcome.Value!).ToList());
        Assert.IsEmpty(shown.TagsOf(gone.Id), "the refused tag was not added");
        Assert.IsEmpty(RecycleBinEntries.Of(gone.FilePath), "the check leaves nothing in the bin");
    }

    [TestMethod]
    public async Task FocusMovingAwayEndsTheEditAsEscWouldAndASummonBringsTheFieldBackAsItWasLeft()
    {
        using var shown = ShownWindow.Open();
        var items = await shown.OpenStash(shown.Picture("left tagging.png"), shown.Picture("left other.png"));
        var item = items.Single(each => each.Name == "left tagging");
        await shown.Tag([items.Single(each => each.Id != item.Id)], "happy");
        await OpenAndRead(shown, item);
        TrayDesktop.Run(() =>
        {
            shown.PreviewTagBox.Focus();
            ShownWindow.Type(shown.PreviewTagBox, "ha");
        });
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.PreviewTags.Field.Suggestions.SequenceEqual(["happy"])));
        TrayDesktop.Run(() =>
        {
            shown.PreviewPicture.Focus(NavigationMethod.Tab);
            Assert.AreEqual("", shown.PreviewTagBox.Text ?? "", "focus moving elsewhere in the window ends the edit as Esc would (G17)");
            Assert.IsEmpty(shown.PreviewTags.Field.Suggestions, "its suggestions with it");
            Assert.AreSame(shown.PreviewPicture, shown.Focused, "leaving focus where it went");
            shown.PreviewTagBox.Focus();
            ShownWindow.Type(shown.PreviewTagBox, "half");
        });

        TrayDesktop.Run(shown.Deactivate);
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => !shown.Window.IsVisible));
        TrayDesktop.Run(shown.Window.SummonFromTray);
        await TrayDesktop.Get(() => shown.Listing.Settle());

        TrayDesktop.Run(() =>
        {
            Assert.IsTrue(shown.Window.IsVisible);
            Assert.IsTrue(shown.Preview.IsOpen, "the preview as it was left (G1)");
            Assert.IsTrue(shown.PreviewTagBox.IsFocused, "focus back in the field");
            Assert.AreEqual("half", shown.PreviewTagBox.Text, "with what was typed");
            ShownWindow.Press(shown.PreviewTagBox, Key.Enter);
        });
        await ShownWindow.Eventually(() => shown.TagsOf(item.Id).Count == 1);
        CollectionAssert.AreEqual(new[] { "half" }, shown.TagsOf(item.Id), "nothing of the ended edit was committed");
        TrayDesktop.Run(() => ShownWindow.Press(shown.PreviewTagBox, Key.Escape));

        TrayDesktop.Run(shown.Deactivate);
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => !shown.Window.IsVisible));
        TrayDesktop.Run(shown.Window.SummonFromTray);
        await TrayDesktop.Get(() => shown.Listing.Settle());
        TrayDesktop.Run(() => Assert.AreSame(shown.PreviewPicture, shown.Focused, "left on the picture, the summon brings focus back there"));
    }

    [TestMethod]
    public async Task ManyTagsAndALongOneScrollWithinTheRowLeavingThePictureTheHandoffButtonAndEveryXInTheWindow()
    {
        using var shown = ShownWindow.Open();
        var items = await shown.OpenStash(shown.Picture("many tags.png"), shown.Picture("other tags.png"));
        var item = items.Single(each => each.Name == "many tags");
        var longTag = new string('w', 300);
        await shown.Tag([item], string.Join(' ', Enumerable.Range(1, 80).Select(number => $"tag{number:00}").Append(longTag)));
        await shown.Tag([items.Single(each => each.Id != item.Id)], "tag01 tagless");
        await OpenAndRead(shown, item);

        TrayDesktop.Run(() =>
        {
            shown.Window.UpdateLayout();
            var client = shown.Window.ClientSize;
            Assert.HasCount(81, shown.PreviewTags.Field.Tags);
            var handoff = shown.HandoffButton.TranslatePoint(new Point(shown.HandoffButton.Bounds.Width, shown.HandoffButton.Bounds.Height), shown.Window)!.Value;
            Assert.IsTrue(handoff.X <= client.Width && handoff.Y <= client.Height, $"the handoff button stays in the window (G19, G7): {handoff} in {client}");
            Assert.IsGreaterThan(100.0, shown.PreviewPicture.Bounds.Height, "the picture keeps its place");
            var longX = RemoveButton(shown, longTag);
            var right = longX.TranslatePoint(new Point(longX.Bounds.Width, 0), shown.Window)!.Value;
            Assert.IsLessThanOrEqualTo(client.Width, right.X, "a long tag is trimmed, its × inside the window (G17)");
        });

        TrayDesktop.Run(() => ShownWindow.Press(shown.PreviewPicture, Key.Tab));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.PreviewTagBox.IsFocused && InView(shown, shown.PreviewTagBox)));
        TrayDesktop.Run(() => ShownWindow.Type(shown.PreviewTagBox, "tag"));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() =>
            shown.PreviewTags.GetVisualDescendants().OfType<Button>().FirstOrDefault(button => button.Content as string == "tagless") is { } suggestion && InView(shown, suggestion) && InView(shown, shown.PreviewTagBox)));
        TrayDesktop.Run(() => CollectionAssert.AreEqual(new[] { "tagless" }, shown.PreviewTags.Field.Suggestions.ToList(), "the eighty the item carries are not suggested (G17)"));
    }

    /// <summary>Whether <paramref name="control"/> lies whole inside the tags row's scrolled view.</summary>
    private static bool InView(ShownWindow shown, Control control)
    {
        var row = shown.PreviewTags.GetVisualDescendants().OfType<ScrollViewer>().First();
        return control.TranslatePoint(default, row) is { } top && top.Y >= 0 && top.Y + control.Bounds.Height <= row.Viewport.Height + 0.5;
    }

    /// <summary>Space on the item in the grid, then the preview's opening read applied, so what the row shows is the store's.</summary>
    private static async Task OpenAndRead(ShownWindow shown, StashItem item)
    {
        TrayDesktop.Run(() => shown.OpenPreview(item));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Preview.Facts.Count > 0));
    }

    private static Button RemoveButton(ShownWindow shown, string tag) =>
        shown.PreviewTags.GetVisualDescendants().OfType<Button>().Single(button => Avalonia.Automation.AutomationProperties.GetName(button) == $"Remove tag {tag}");
}
