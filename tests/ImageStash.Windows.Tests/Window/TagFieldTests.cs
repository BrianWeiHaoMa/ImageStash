using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ImageStash.Files;
using ImageStash.Seams;
using ImageStash.Store;
using ImageStash.Window;
using ImageStash.Windows.Seams;
using ImageStash.Windows.Tests.Support;
using SkiaSharp;

namespace ImageStash.Windows.Tests.Window;

/// <summary>
/// The item menu's tag submenus and the tag field in the stash window laid out by the real desktop platform, off every
/// display and never activated: Add tag ▸ offering Type a tag… first, then every tag in the stash, read as the menu opens;
/// Remove tag ▸ offering every tag the selection carries (G17); Type a tag… opening the field inside the window (F4,
/// G2) on the selection's tags with their ×, suggestions from the existing tags only (G17, N6), Enter committing one
/// tag per word and leaving the field open with its box empty, Esc closing it, focus going back where it was (G19);
/// the keys inside the field its own (G7), and no paste of its own (N9, R5), the window's <c>Ctrl+V</c> putting the
/// clipboard's words in its box as typed (G13); a refusal named, the field still open (G35); while nothing is typed its
/// button listing every tag, the commonest first, on one click, marking the tags every item carries, a pick giving the
/// tag to every item or taking it off all of them with the list staying open and the field following, typing hiding
/// the button, Esc closing the list and then the field (G17, G2).
/// </summary>
[TestClass]
public sealed class TagFieldTests
{
    [TestMethod]
    public async Task TypeATagOpensTheFieldInsideTheWindowWhereTypedWordsSuggestExistingTagsAndEnterGivesEachItemOneTagPerWord()
    {
        using var shown = ShownWindow.Open();
        var items = await shown.OpenStash(shown.Picture("field tagged.png"), shown.Picture("field one.png"), shown.Picture("field two.png"), shown.Picture("red.png"));
        await shown.Tag([items[0]], "reaction Scream dog");
        var noticesBefore = TrayDesktop.Get(() => shown.Notices.Shown.Count);
        TrayDesktop.Run(() =>
        {
            shown.Grid.Focus();
            shown.Listing.Select([items[1].Id, items[2].Id], items[1].Id);
        });

        var labels = TrayDesktop.Get(() => shown.RightClick(items[1].Id));
        TrayDesktop.Run(() => shown.Submenu("Add tag"));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Submenu("Add tag").Count == 4));
        var offered = TrayDesktop.Get(() => shown.Submenu("Add tag"));
        await ClickTypeATag(shown);

        CollectionAssert.AreEqual(new[] { "Copy", "Add to Favorites", "Add tag", "Delete 2 items" }, labels, "nothing to take off, so no Remove tag ▸ (G17)");
        CollectionAssert.AreEqual(new[] { "Type a tag…", "dog", "reaction", "Scream" }, offered, "Type a tag… first, then the stash's tags, not the selection's (G17)");
        TrayDesktop.Run(() =>
        {
            Assert.AreSame(shown.Window, TopLevel.GetTopLevel(shown.TagBox), "the field is inside the stash window, not a window of its own (F4)");
            Assert.IsTrue(shown.Window.IsVisible, "the flyout stays while the field shows (G2)");
            Assert.IsTrue(shown.TagBox.IsFocused);
            CollectionAssert.AreEquivalent(new[] { items[1].Id, items[2].Id }, shown.TagField.Items.Select(item => item.Id).ToList(), "the items the menu was opened on");
            Assert.IsEmpty(shown.TagField.Field.Tags, "they carry no tag yet");
            ShownWindow.Type(shown.TagBox, "re");
        });
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.TagField.Field.Suggestions.Count > 0));
        TrayDesktop.Run(() =>
        {
            CollectionAssert.AreEqual(new[] { "reaction", "Scream" }, shown.TagField.Field.Suggestions.ToList(),
                "existing tags containing the word, the one starting with it first; never the name red (G17, N6)");
            Assert.IsTrue(shown.TagField.GetVisualDescendants().OfType<Button>().Any(button => button.Content as string == "reaction" && button.IsEffectivelyVisible), "drawn inside the window");
            shown.TagBox.Text = "#Dog cat";
            ShownWindow.Press(shown.TagBox, Key.Enter);
            Assert.IsTrue(shown.TagField.IsVisible, "Enter leaves the field open for the next tag (G17)");
            Assert.IsTrue(shown.TagBox.IsFocused, "with focus in its box");
            Assert.AreEqual("", shown.TagBox.Text ?? "", "and the box emptied");
        });
        await ShownWindow.Eventually(() => shown.TagsOf(items[2].Id).Count == 2 && TrayDesktop.Get(() => shown.TagField.Field.Tags.Count == 2));

        CollectionAssert.AreEqual(new[] { "cat", "dog" }, shown.TagsOf(items[1].Id), "one tag per word, the leading # dropped, the stored spelling of dog kept (G17)");
        CollectionAssert.AreEqual(new[] { "cat", "dog" }, shown.TagsOf(items[2].Id));
        CollectionAssert.AreEqual(new[] { "dog", "reaction", "Scream" }, shown.TagsOf(items[0].Id), "an item it was not opened on is unchanged");
        TrayDesktop.Run(() =>
        {
            CollectionAssert.AreEqual(new[] { "cat", "dog" }, shown.TagField.Field.Tags.ToList(), "the chips read again, each with its ×");
            ShownWindow.Press(shown.TagBox, Key.Escape);
            Assert.IsFalse(shown.TagField.IsVisible, "Esc closes the field");
            Assert.IsTrue(shown.Grid.IsFocused, "and gives focus back to the grid, where it was (G19)");
        });
        Assert.AreEqual(0, shown.Uses(), "tagging is not a use (G10)");
        TrayDesktop.Run(() => Assert.HasCount(noticesBefore, shown.Notices.Shown, "a change that succeeded says nothing more"));
    }

    [TestMethod]
    public async Task DownThenEnterCommitsTheChosenSuggestionAndAClickOnOneCommitsItWithTheFieldStillOpen()
    {
        using var shown = ShownWindow.Open();
        var items = await shown.OpenStash(shown.Picture("choose tagged.png"), shown.Picture("choose one.png"));
        await shown.Tag([items[0]], "reaction Scream");
        TrayDesktop.Run(() =>
        {
            // The right-click on a tile moves focus to the grid (G7), where the field gives it back.
            shown.Search.Focus();
            shown.Listing.Select([items[1].Id], items[1].Id);
        });

        await OpenField(shown, items[1].Id);
        TrayDesktop.Run(() => ShownWindow.Type(shown.TagBox, "funny rea"));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.TagField.Field.Suggestions.SequenceEqual(["reaction", "Scream"])));
        TrayDesktop.Run(() =>
        {
            ShownWindow.Press(shown.TagBox, Key.Down);
            Assert.AreEqual("reaction", shown.TagField.Field.Chosen);
            ShownWindow.Press(shown.TagBox, Key.Enter);
            Assert.IsTrue(shown.TagBox.IsFocused, "focus stays in the box for the next tag (G17)");
        });
        await ShownWindow.Eventually(() => shown.TagsOf(items[1].Id).Count == 2);
        CollectionAssert.AreEqual(new[] { "funny", "reaction" }, shown.TagsOf(items[1].Id), "the chosen suggestion in place of the word being typed");
        TrayDesktop.Run(() =>
        {
            ShownWindow.Press(shown.TagBox, Key.Escape);
            Assert.IsTrue(shown.Grid.IsFocused, "Esc gives focus back to the grid, where the right-click put it (G7, G19)");
        });

        await OpenField(shown, items[1].Id);
        // It opens on the menu's items as last listed, which the listing's refresh may not have reached, then reads their tags again.
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.TagField.Field.Tags.SequenceEqual(["funny", "reaction"])));
        TrayDesktop.Run(() =>
        {
            CollectionAssert.AreEqual(new[] { "funny", "reaction" }, shown.TagField.Field.Tags.ToList(), "the field shows the tags the item carries now");
            ShownWindow.Type(shown.TagBox, "scr");
        });
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.TagField.Field.Suggestions.SequenceEqual(["Scream"])));
        TrayDesktop.Run(() => shown.TagField.GetVisualDescendants().OfType<Button>().Single(button => button.Content as string == "Scream").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
        await ShownWindow.Eventually(() => shown.TagsOf(items[1].Id).Count == 3);

        CollectionAssert.AreEqual(new[] { "funny", "reaction", "Scream" }, shown.TagsOf(items[1].Id));
        TrayDesktop.Run(() => Assert.IsTrue(shown.TagField.IsVisible && shown.TagBox.IsFocused, "a suggestion's click leaves the field open too"));
    }

    [TestMethod]
    public async Task EscCancelsAnEmptyEnterChangesNothingAndAfterwardsEnterInTheGridStillHandsOff()
    {
        using var shown = ShownWindow.Open();
        var item = (await shown.OpenStash(shown.Picture("cancel one.png"))).Single();
        TrayDesktop.Run(() => shown.Grid.Focus());

        await OpenField(shown, item.Id);
        TrayDesktop.Run(() =>
        {
            ShownWindow.Type(shown.TagBox, "never");
            ShownWindow.Press(shown.TagBox, Key.Escape);
            Assert.IsFalse(shown.TagField.IsVisible, "Esc closes the field (G19)");
            Assert.IsTrue(shown.Grid.IsFocused, "focus back where it was");
            Assert.IsTrue(shown.Window.IsVisible, "Esc in the field is the field's, not the window's hide (G2)");
        });
        await OpenField(shown, item.Id);
        TrayDesktop.Run(() =>
        {
            Assert.AreEqual("", shown.TagBox.Text ?? "", "a cancelled field opens again with nothing typed");
            ShownWindow.Press(shown.TagBox, Key.Enter);
            Assert.IsTrue(shown.TagField.IsVisible && shown.TagBox.IsFocused, "an empty Enter changes nothing and leaves the field open: nothing requires a tag (G17)");
            ShownWindow.Type(shown.TagBox, "##");
            ShownWindow.Press(shown.TagBox, Key.Enter);
            Assert.IsTrue(shown.TagField.IsVisible && shown.TagBox.IsFocused, "nor does one of only #s");
        });
        await Task.Delay(300);

        Assert.IsEmpty(shown.TagsOf(item.Id), "nothing was committed");
        Assert.IsEmpty((await shown.Listing.Store!.TagCounts()).Value!);
        TrayDesktop.Run(() =>
        {
            Assert.IsTrue(shown.TagField.IsVisible, "still open");
            ShownWindow.Press(shown.TagBox, Key.Escape);
            Assert.IsTrue(shown.Grid.IsFocused);
            ShownWindow.Press(shown.Grid, Key.Enter);
        });
        await ShownWindow.Eventually(() => shown.Uses() == 1);
        CollectionAssert.AreEqual(new[] { item.FilePath }, TestClipboard.Read().Files.ToList(), "the paste path still works after the field (G7, G8)");
    }

    [TestMethod]
    public async Task InsideTheFieldItsKeysAreItsOwnAndNoneDeletesHandsOffCopiesFavoritesOrMovesTheSelection()
    {
        using var shown = ShownWindow.Open();
        var items = await shown.OpenStash(shown.Picture("keys one.png"), shown.Picture("keys two.png"), shown.Picture("keys three.png"), shown.Picture("keys four.png"));
        var selected = items.Take(3).Select(item => item.Id).ToList();
        TrayDesktop.Run(() =>
        {
            shown.Grid.Focus();
            shown.Listing.Select(selected, selected[0]);
        });
        await OpenField(shown, selected[0]);
        var noticesBefore = TrayDesktop.Get(() => shown.Notices.Shown.Count);
        TestClipboard.PutText("held before");

        var texts = TrayDesktop.Get(() =>
        {
            shown.TagBox.Text = "abcd";
            shown.TagBox.CaretIndex = 2;
            var after = new List<string>();
            foreach (var (key, modifiers) in new[] { (Key.Delete, KeyModifiers.None), (Key.Back, KeyModifiers.None) })
            {
                ShownWindow.Press(shown.TagBox, key, modifiers);
                after.Add(shown.TagBox.Text ?? "");
            }

            foreach (var (key, modifiers) in new[] { (Key.Space, KeyModifiers.None), (Key.Left, KeyModifiers.None), (Key.Right, KeyModifiers.None), (Key.Up, KeyModifiers.None), (Key.Down, KeyModifiers.None), (Key.D, KeyModifiers.Control), (Key.A, KeyModifiers.Control), (Key.C, KeyModifiers.Control) })
            {
                ShownWindow.Press(shown.TagBox, key, modifiers);
            }

            return after;
        });
        await Task.Delay(500);

        CollectionAssert.AreEqual(new[] { "abd", "ad" }, texts, "Del and Backspace edited the field's text (G7)");
        TrayDesktop.Run(() =>
        {
            Assert.HasCount(4, shown.Listing.Items, "Del deleted nothing (G18)");
            CollectionAssert.AreEqual(selected, shown.Listing.Selection.ToList(), "the arrows and Ctrl+A left the selection as it was");
            Assert.IsFalse(shown.Listing.Items.Any(item => item.Favorite), "Ctrl+D toggled nothing (G7: in the grid only)");
            Assert.IsTrue(shown.TagField.IsVisible, "the field is still open");
            Assert.IsTrue(shown.TagBox.IsFocused);
            Assert.IsTrue(shown.Window.IsVisible);
            Assert.HasCount(noticesBefore, shown.Notices.Shown, "nothing was deleted, copied or refused");
        });
        Assert.AreEqual(0, shown.Uses(), "no handoff or copy (G10)");
        Assert.IsEmpty(TestClipboard.Read().Files, "no item went onto the clipboard");
    }

    [TestMethod]
    public async Task TheFieldsOwnPasteDoesNotReadTheClipboardAndItOffersNoPaste()
    {
        using var shown = ShownWindow.Open();
        var item = (await shown.OpenStash(shown.Picture("paste field.png"))).Single();
        TrayDesktop.Run(() => shown.Grid.Focus());
        await OpenField(shown, item.Id);
        TestClipboard.PutText("pasted words");

        TrayDesktop.Run(() =>
        {
            ShownWindow.Press(shown.TagBox, Key.Insert, KeyModifiers.Shift);
            // The text box's own Paste command, which its keys run; Ctrl+V is the window's (G13).
            shown.TagBox.Paste();
        });
        await Task.Delay(500);
        var afterPaste = TrayDesktop.Get(() => shown.TagBox.Text ?? "");
        var offered = TrayDesktop.Get(() =>
        {
            shown.TagBox.Text = "words";
            shown.TagBox.SelectAll();
            shown.TagBox.ContextFlyout?.ShowAt(shown.TagBox);
            return shown.TagBox.ContextFlyout is MenuFlyout menu ? menu.Items.OfType<MenuItem>().Select(entry => $"{entry.Header}").ToList() : [];
        });

        TrayDesktop.Run(() =>
        {
            Assert.IsTrue(shown.TagField.IsVisible);
            Assert.IsNull(shown.TagBox.ContextFlyout, "no menu offering a Paste that does nothing (G35)");
            Assert.IsNull(shown.TagBox.ContextMenu);
        });
        Assert.IsFalse(offered.Any(entry => entry.Contains("Paste", StringComparison.OrdinalIgnoreCase)));
        Assert.AreEqual("", afterPaste, "the clipboard's text was never read into the field (N9, R5)");
        Assert.IsEmpty(shown.TagsOf(item.Id));
    }

    [TestMethod]
    public async Task CtrlVInTheFieldPutsTheClipboardsWordsInItsBoxAsTypedSoEnterTagsEachAndAPictureIsStillAdded()
    {
        using var shown = ShownWindow.Open();
        var item = (await shown.OpenStash(shown.Picture("paste words.png"))).Single();
        await shown.Tag([(await shown.AddTo("paste other.png")).Single()], "wordy");
        TrayDesktop.Run(() => shown.Grid.Focus());
        await OpenField(shown, item.Id);
        TestClipboard.PutText("funny wor");
        var noticed = TrayDesktop.Get(() => shown.Notices.Shown.Count);

        TrayDesktop.Run(() =>
        {
            ShownWindow.Type(shown.TagBox, "a ");
            ShownWindow.Press(shown.TagBox, Key.V, KeyModifiers.Control);
            ShownWindow.Release(shown.TagBox, Key.V);
        });
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.TagBox.Text == "a funny wor"));
        TrayDesktop.Run(() =>
        {
            Assert.AreEqual("a funny wor".Length, shown.TagBox.CaretIndex, "at the caret, as typed (G13)");
            CollectionAssert.AreEqual(new[] { "wordy" }, shown.TagField.Field.Suggestions.ToList(), "the suggestions follow the last word");
            Assert.HasCount(noticed, shown.Notices.Shown, "no notice");
            Assert.IsTrue(shown.TagField.IsVisible && shown.TagBox.IsFocused);
        });
        Assert.IsEmpty(shown.TagsOf(item.Id), "nothing added before Enter");

        TrayDesktop.Run(() => ShownWindow.Press(shown.TagBox, Key.Enter));
        await ShownWindow.Eventually(() => shown.TagsOf(item.Id).Count == 3);
        CollectionAssert.AreEqual(new[] { "a", "funny", "wor" }, shown.TagsOf(item.Id), "each word a tag (G13, G17)");
        TrayDesktop.Run(() => Assert.IsTrue(shown.TagField.IsVisible && shown.TagBox.IsFocused, "the field still open"));

        TestClipboard.Put((TestClipboard.Png, File.ReadAllBytes(shown.Picture("pasted picture.png"))));
        var listed = TrayDesktop.Get(() => shown.Listing.Items.Count);
        TrayDesktop.Run(() =>
        {
            ShownWindow.Press(shown.TagBox, Key.V, KeyModifiers.Control);
            ShownWindow.Release(shown.TagBox, Key.V);
        });
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled && shown.Listing.Items.Count == listed + 1));
        TrayDesktop.Run(() => Assert.AreEqual("", shown.TagBox.Text ?? "", "a picture is added as from anywhere in the window, nothing typed (G13)"));
    }

    [TestMethod]
    public async Task CtrlVInTheFieldPutsWordsOnSeveralLinesOrBetweenTabsInItsBoxSoEnterTagsEveryOne()
    {
        using var shown = ShownWindow.Open();
        var item = (await shown.OpenStash(shown.Picture("paste lines.png"))).Single();
        TrayDesktop.Run(() => shown.Grid.Focus());
        await OpenField(shown, item.Id);
        // A list copied from a document or a spreadsheet: a line, or a tab, between its words.
        TestClipboard.PutText("celebrate\r\nfacepalm\tsarcasm\nwholesome");

        TrayDesktop.Run(() =>
        {
            ShownWindow.Press(shown.TagBox, Key.V, KeyModifiers.Control);
            ShownWindow.Release(shown.TagBox, Key.V);
        });
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => (shown.TagBox.Text ?? "").Length > 0));
        TrayDesktop.Run(() => Assert.AreEqual("celebrate facepalm sarcasm wholesome", shown.TagBox.Text, "every word of the clipboard's text in the box as typed (G13)"));

        TrayDesktop.Run(() => ShownWindow.Press(shown.TagBox, Key.Enter));
        await ShownWindow.Eventually(() => shown.TagsOf(item.Id).Count == 4);
        CollectionAssert.AreEqual(new[] { "celebrate", "facepalm", "sarcasm", "wholesome" }, shown.TagsOf(item.Id), "each word a tag (G13, G17)");
    }

    [TestMethod]
    public async Task WhileNothingIsTypedOneClickListsEveryTagTheCommonestFirstAndAPickGivesItAndAgainTakesItOffTheListStayingOpen()
    {
        using var shown = ShownWindow.Open();
        var item = await ThreeTagsOnAnOpenField(shown);

        TrayDesktop.Run(() =>
        {
            Assert.AreEqual("", shown.TagBox.Text ?? "");
            ShownWindow.RaiseClick(ShownWindow.EveryTagButton(shown.TagField.Field));
        });

        TrayDesktop.Run(() =>
        {
            var picker = shown.TagField.Field.Picker;
            Assert.IsTrue(picker.IsOpen, "on one click (G17)");
            CollectionAssert.AreEqual(new[] { new TagPickerEntry("a", true), new TagPickerEntry("b", false), new TagPickerEntry("c", false) }, picker.Entries.ToList(), "every tag, the commonest first, the one it carries marked");
            Assert.IsTrue(shown.TagBox.IsFocused, "focus stays in the box");
            ShownWindow.RaiseClick(ShownWindow.Entry(picker, "b"));
        });
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.TagField.Field.Tags.SequenceEqual(["a", "b"])));

        CollectionAssert.AreEqual(new[] { "a", "b" }, shown.TagsOf(item.Id), "the store's");
        var noticed = TrayDesktop.Get(() => shown.Notices.Shown.Count);
        TrayDesktop.Run(() =>
        {
            var picker = shown.TagField.Field.Picker;
            Assert.IsTrue(shown.TagField.IsVisible && picker.IsOpen, "the field and the list stay open (G17)");
            Assert.IsTrue(picker.Entries.Single(entry => entry.Tag == "b").Marked, "with it marked");
            Assert.IsTrue(shown.TagBox.IsFocused);
            ShownWindow.RaiseClick(ShownWindow.Entry(picker, "a"));
        });
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.TagField.Field.Tags.SequenceEqual(["b"])));

        CollectionAssert.AreEqual(new[] { "b" }, shown.TagsOf(item.Id), "a carried tag's pick takes it off, as Add tag ▸ does (G17)");
        TrayDesktop.Run(() =>
        {
            var picker = shown.TagField.Field.Picker;
            Assert.IsTrue(shown.TagField.IsVisible && picker.IsOpen, "the field and the list still open");
            Assert.IsFalse(picker.Entries.Single(entry => entry.Tag == "a").Marked, "a unmarked, its chip gone from the field");
            Assert.HasCount(noticed, shown.Notices.Shown, "and no failure named");
        });
    }

    [TestMethod]
    public async Task ForAMixedSelectionTheListMarksTheTagsEveryItemCarriesAndAPickGivesItToAllThenTakesItOffAll()
    {
        using var shown = ShownWindow.Open();
        var items = await shown.OpenStash(shown.Picture("mixed a.png"), shown.Picture("mixed b.png"));
        var (a, b) = (items.Single(i => i.Name == "mixed a"), items.Single(i => i.Name == "mixed b"));
        await shown.Tag([a], "joy alpha");
        await shown.Tag([b], "alpha");
        TrayDesktop.Run(() =>
        {
            shown.Grid.Focus();
            shown.Listing.Select([a.Id, b.Id], a.Id);
        });
        await OpenField(shown, a.Id);
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.TagField.Field.Tags.SequenceEqual(["alpha", "joy"]) && ShownWindow.EveryTagButton(shown.TagField.Field).IsEffectivelyVisible));
        List<string> Marked() => TrayDesktop.Get(() => shown.TagField.Field.Picker.Entries.Where(entry => entry.Marked).Select(entry => entry.Tag).ToList());

        TrayDesktop.Run(() => ShownWindow.RaiseClick(ShownWindow.EveryTagButton(shown.TagField.Field)));
        CollectionAssert.AreEqual(new[] { "alpha" }, Marked(), "alpha, which both carry, and not joy, which one does (G17)");

        TrayDesktop.Run(() => ShownWindow.RaiseClick(ShownWindow.Entry(shown.TagField.Field.Picker, "joy")));
        await ShownWindow.Eventually(() => shown.TagsOf(b.Id).Contains("joy"));
        await ShownWindow.Eventually(() => Marked().Count == 2);
        CollectionAssert.AreEquivalent(new[] { "alpha", "joy" }, Marked(), "both now carry joy");
        CollectionAssert.AreEqual(new[] { "alpha", "joy" }, shown.TagsOf(a.Id));

        TrayDesktop.Run(() => ShownWindow.RaiseClick(ShownWindow.Entry(shown.TagField.Field.Picker, "joy")));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.TagField.Field.Tags.SequenceEqual(["alpha"])));
        CollectionAssert.AreEqual(new[] { "alpha" }, shown.TagsOf(a.Id), "all carried it: off every one (G17)");
        CollectionAssert.AreEqual(new[] { "alpha" }, shown.TagsOf(b.Id));
        CollectionAssert.AreEqual(new[] { "alpha" }, Marked());
        TrayDesktop.Run(() => Assert.IsTrue(shown.TagField.Field.Picker.IsOpen, "the list stays open for the next"));

        TrayDesktop.Run(() => ShownWindow.RaiseClick(ShownWindow.Entry(shown.TagField.Field.Picker, "alpha")));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.TagField.Field.Tags.Count == 0));
        Assert.IsEmpty(shown.TagsOf(a.Id));
        Assert.IsEmpty(shown.TagsOf(b.Id));
    }

    [TestMethod]
    public async Task TypingHidesTheButtonAndItsListAndTheSuggestionsTakeOverEmptyingTheBoxBringsItBack()
    {
        using var shown = ShownWindow.Open();
        await ThreeTagsOnAnOpenField(shown);
        TrayDesktop.Run(() => ShownWindow.RaiseClick(ShownWindow.EveryTagButton(shown.TagField.Field)));

        TrayDesktop.Run(() => ShownWindow.Type(shown.TagBox, "c"));

        TrayDesktop.Run(() =>
        {
            Assert.IsFalse(ShownWindow.EveryTagButton(shown.TagField.Field).IsEffectivelyVisible, "G17: while nothing is typed");
            Assert.IsFalse(shown.TagField.Field.Picker.IsOpen);
            CollectionAssert.AreEqual(new[] { "c" }, shown.TagField.Field.Suggestions.ToList());
            shown.TagBox.Text = "";
        });
        TrayDesktop.Run(() =>
        {
            Assert.IsTrue(ShownWindow.EveryTagButton(shown.TagField.Field).IsEffectivelyVisible, "back");
            Assert.IsEmpty(shown.TagField.Field.Suggestions);
        });
    }

    [TestMethod]
    public async Task EscClosesTheListThenTheFieldAndFocusLeavingClosesBoth()
    {
        using var shown = ShownWindow.Open();
        await ThreeTagsOnAnOpenField(shown);
        TrayDesktop.Run(() => ShownWindow.RaiseClick(ShownWindow.EveryTagButton(shown.TagField.Field)));

        TrayDesktop.Run(() => ShownWindow.Press(shown.TagBox, Key.Escape));

        TrayDesktop.Run(() =>
        {
            Assert.IsFalse(shown.TagField.Field.Picker.IsOpen, "the list first (G17, G2)");
            Assert.IsTrue(shown.TagField.IsVisible && shown.TagBox.IsFocused, "the field stays with focus in its box");
            ShownWindow.Press(shown.TagBox, Key.Escape);
        });
        TrayDesktop.Run(() => Assert.IsFalse(shown.TagField.IsVisible, "Esc again closes it as before"));

        await OpenField(shown, TrayDesktop.Get(() => shown.Listing.Items[0].Id));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => ShownWindow.EveryTagButton(shown.TagField.Field).IsEffectivelyVisible));
        TrayDesktop.Run(() => ShownWindow.RaiseClick(ShownWindow.EveryTagButton(shown.TagField.Field)));
        TrayDesktop.Run(() => shown.Grid.Focus());
        TrayDesktop.Run(() =>
        {
            Assert.IsFalse(shown.TagField.IsVisible, "focus left: the field closes (G17)");
            Assert.IsFalse(shown.TagField.Field.Picker.IsOpen, "and the list with it");
        });
    }

    [TestMethod]
    public async Task WithNoTagInTheStashNoButtonShows()
    {
        using var shown = ShownWindow.Open();
        var item = (await shown.OpenStash(shown.Picture("untagged.png"))).Single();

        await OpenField(shown, item.Id);
        await Task.Delay(300);

        TrayDesktop.Run(() => Assert.IsFalse(ShownWindow.EveryTagButton(shown.TagField.Field).IsEffectivelyVisible, "nothing to pick from (G17)"));
    }

    [TestMethod]
    public async Task SixtyTagsScrollInsideTheDisplay()
    {
        using var shown = ShownWindow.Open();
        var item = (await shown.OpenStash(shown.Picture("sixty field.png"))).Single();
        var other = (await shown.AddTo("sixty carrier.png")).Single();
        await shown.Tag([other], string.Join(" ", Enumerable.Range(1, 60).Select(i => $"tag-{i:00}")));
        await OpenField(shown, item.Id);
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => ShownWindow.EveryTagButton(shown.TagField.Field).IsEffectivelyVisible));

        TrayDesktop.Run(() => ShownWindow.RaiseClick(ShownWindow.EveryTagButton(shown.TagField.Field)));
        var list = TrayDesktop.Get(() => ((Control)shown.TagField.Field.Picker.Child!).GetVisualDescendants().OfType<ScrollViewer>().First());
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => list.Viewport.Height > 0));

        TrayDesktop.Run(() =>
        {
            Assert.HasCount(60, shown.TagField.Field.Picker.Entries);
            Assert.IsGreaterThan(list.Viewport.Height, list.Extent.Height, "it scrolls (G17)");
            Assert.IsLessThan(shown.Window.Screens.ScreenFromVisual(shown.Window)!.WorkingArea.Height / shown.Window.RenderScaling, list.Bounds.Height, "inside the display");
        });
    }

    /// <summary>Tags a(3), b(2), c(1), and the field open on the item carrying a alone, read from the store.</summary>
    private static async Task<StashItem> ThreeTagsOnAnOpenField(ShownWindow shown)
    {
        var items = await shown.OpenStash(shown.Picture("three one.png"), shown.Picture("three two.png"), shown.Picture("three three.png"), shown.Picture("three four.png"));
        await shown.Tag([items[0], items[1], items[2]], "a");
        await shown.Tag([items[1], items[2]], "b");
        await shown.Tag([items[3]], "c");
        TrayDesktop.Run(() =>
        {
            shown.Grid.Focus();
            shown.Listing.Select([items[0].Id], items[0].Id);
        });
        await OpenField(shown, items[0].Id);
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.TagField.Field.Tags.SequenceEqual(["a"]) && ShownWindow.EveryTagButton(shown.TagField.Field).IsEffectivelyVisible));
        return items[0];
    }

    [TestMethod]
    public async Task RemoveTagOffersEveryTagTheSelectionCarriesAndAnXInTheFieldTakesItsTagOffEverySelectedItem()
    {
        using var shown = ShownWindow.Open();
        var items = await shown.OpenStash(shown.Picture("remove one.png"), shown.Picture("remove two.png"));
        await shown.Tag([items[0]], "x y");
        await shown.Tag([items[1]], "y");
        TrayDesktop.Run(() =>
        {
            shown.Grid.Focus();
            shown.Listing.Select([items[0].Id, items[1].Id], items[0].Id);
        });

        var labels = TrayDesktop.Get(() => shown.RightClick(items[1].Id));
        var removable = TrayDesktop.Get(() => shown.Submenu("Remove tag"));
        TrayDesktop.Run(() => shown.ClickSubmenuEntry("Remove tag", "x"));
        await ShownWindow.Eventually(() => shown.TagsOf(items[0].Id).SequenceEqual(["y"]));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Submenu("Remove tag").SequenceEqual(["y"])));
        TrayDesktop.Run(shown.CloseMenu);

        CollectionAssert.AreEqual(new[] { "Copy", "Add to Favorites", "Add tag", "Remove tag", "Delete 2 items" }, labels);
        CollectionAssert.AreEqual(new[] { "x", "y" }, removable, "every tag either carries, not only those both carry (G17)");

        await shown.Tag([items[0]], "x");
        await OpenField(shown, items[0].Id);
        TrayDesktop.Run(() => CollectionAssert.AreEqual(new[] { "x", "y" }, shown.TagField.Field.Tags.ToList(), "the selection's tags, each with its ×"));
        TrayDesktop.Run(() => shown.TagField.GetVisualDescendants().OfType<Button>().Single(button => Avalonia.Automation.AutomationProperties.GetName(button) == "Remove tag y").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.TagField.Field.Tags.SequenceEqual(["x"])));

        Assert.IsEmpty(shown.TagsOf(items[1].Id), "the × took y off every selected item (G17)");
        CollectionAssert.AreEqual(new[] { "x" }, shown.TagsOf(items[0].Id));
        TrayDesktop.Run(() => Assert.IsTrue(shown.TagField.IsVisible && shown.TagBox.IsFocused, "the field stays open on the rest"));
        CollectionAssert.AreEqual(new[] { "x" }, (await shown.Listing.Store!.TagCounts()).Value!.Select(count => count.Tag).ToList(), "a tag no item carries is gone (G17)");
    }

    [TestMethod]
    public async Task AddTagOffersEveryTagReadAsTheMenuOpensAndItsEntryTagsEveryItemTheMenuWasOpenedOn()
    {
        using var shown = ShownWindow.Open();
        var items = await shown.OpenStash(Enumerable.Range(1, 5).Select(i => shown.Picture($"every {i}.png")).ToArray());
        await shown.Tag(items, "a");
        await shown.Tag(items.Take(4).ToList(), "b");
        await shown.Tag(items.Take(3).ToList(), "c");
        await shown.Tag(items.Take(2).ToList(), "e d");
        await shown.Tag([items[0]], "h g f");
        var opened = items.Skip(2).Select(item => item.Id).ToList();
        TrayDesktop.Run(() =>
        {
            shown.Grid.Focus();
            shown.Listing.Select(opened, opened[0]);
            shown.RightClick(opened[0]);
            shown.Submenu("Add tag");
        });
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Submenu("Add tag").Count == 9));
        var every = TrayDesktop.Get(() => shown.Submenu("Add tag"));
        TrayDesktop.Run(() => shown.ClickSubmenuEntry("Add tag", "c"));
        await ShownWindow.Eventually(() => opened.All(id => shown.TagsOf(id).Contains("c")));
        TrayDesktop.Run(shown.CloseMenu);

        CollectionAssert.AreEqual(new[] { "Type a tag…", "a", "b", "c", "d", "e", "f", "g", "h" }, every, "Type a tag… first, then every tag in the stash, by count then spelling (G17)");
        Assert.AreEqual(1, shown.TagsOf(opened[0]).Count(tag => tag == "c"), "an item already carrying it carries it once");

        await shown.Listing.Store!.RemoveTag(items, "a");
        await Dispatcher.UIThread.InvokeAsync(() => shown.Listing.Refresh());
        TrayDesktop.Run(() =>
        {
            shown.RightClick(opened[0]);
            shown.Submenu("Add tag");
        });
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Submenu("Add tag").Count == 8 && !shown.Submenu("Add tag").Contains("a")));
        var after = TrayDesktop.Get(() => shown.Submenu("Add tag"));
        TrayDesktop.Run(shown.CloseMenu);

        CollectionAssert.AreEqual(new[] { "Type a tag…", "c", "b", "d", "e", "f", "g", "h" }, after, "a tag no item carries is not offered, and the counts are read anew (G17)");
        Assert.AreEqual(0, shown.Uses());
    }

    [TestMethod]
    public async Task AnItemDeletedWhileTheFieldIsOpenIsNamedAndTheOthersAreTagged()
    {
        using var shown = ShownWindow.Open();
        var items = await shown.OpenStash(shown.Picture("refused kept.png"), shown.Picture("refused gone.png"));
        TrayDesktop.Run(() =>
        {
            shown.Grid.Focus();
            shown.Listing.Select(items.Select(item => item.Id), items[0].Id);
        });
        await OpenField(shown, items[0].Id);
        var noticesBefore = TrayDesktop.Get(() => shown.Notices.Shown.Count);

        var deleted = await shown.Listing.Store!.Delete([items[1].Id]);
        TrayDesktop.Run(() =>
        {
            ShownWindow.Type(shown.TagBox, "late");
            ShownWindow.Press(shown.TagBox, Key.Enter);
        });
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Notices.Shown.Count == noticesBefore + 1));
        var named = TrayDesktop.Get(() => shown.Notices.Shown[^1].Text);
        TrayDesktop.Run(() => Assert.IsTrue(shown.TagField.IsVisible && shown.TagBox.IsFocused, "the refusal named, the field still open for the next"));
        await shown.Listing.Store!.UndoDelete([deleted.Single().Value!]);

        StringAssert.Contains(named, "refused gone", "the refused item is named (G35)");
        StringAssert.Contains(named, "no longer in the stash");
        CollectionAssert.AreEqual(new[] { "late" }, shown.TagsOf(items[0].Id), "the other item is tagged");
        Assert.IsEmpty(shown.TagsOf(items[1].Id), "the deleted one, back by Undo, was not");
    }

    [TestMethod]
    public async Task FocusMovingElsewhereInTheWindowClosesTheFieldAsEscWould()
    {
        using var shown = ShownWindow.Open();
        var item = (await shown.OpenStash(shown.Picture("focus away.png"))).Single();
        TrayDesktop.Run(() => shown.Grid.Focus());
        await OpenField(shown, item.Id);

        TrayDesktop.Run(() =>
        {
            ShownWindow.Type(shown.TagBox, "dropped");
            shown.Search.Focus();
            Assert.IsFalse(shown.TagField.IsVisible, "a summon or a click elsewhere focuses another control, which closes the field");
            Assert.IsTrue(shown.Search.IsFocused, "and leaves focus where it went");
        });
        await Task.Delay(300);

        Assert.IsEmpty(shown.TagsOf(item.Id), "nothing was committed");
        await OpenField(shown, item.Id);
        TrayDesktop.Run(() => Assert.AreEqual("", shown.TagBox.Text ?? ""));
    }

    [TestMethod]
    public async Task ATagTypedInTheFieldGetsItsChipOnceAmongTheMostCarriedSearchesByItAndRemoveTagEndsIt()
    {
        using var shown = ShownWindow.Open();
        var items = await shown.OpenStash(shown.Picture("walk a.png"), shown.Picture("walk b.png"), shown.Picture("walk c.png"));
        var (a, b, c) = (items[0], items[1], items[2]);
        var many = Enumerable.Range(1, 12).Select(i => $"stash-tag-{i:00}").ToList();
        await shown.Tag([a, b], string.Join(" ", many));
        TrayDesktop.Run(() =>
        {
            shown.Grid.Focus();
            shown.Listing.Select([c.Id], c.Id);
        });

        await OpenField(shown, c.Id);
        TrayDesktop.Run(() =>
        {
            ShownWindow.Type(shown.TagBox, "reaction");
            ShownWindow.Press(shown.TagBox, Key.Enter);
        });
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled && shown.Listing.Chips.Any(count => count.Tag == "reaction")));
        TrayDesktop.Run(() => Assert.DoesNotContain("#reaction", shown.ShownChips(), "carried by one item, it is not among the tags the most items carry that fit (G4)"));
        TrayDesktop.Run(() =>
        {
            ShownWindow.Press(shown.TagBox, Key.Escape);
            shown.Listing.Select([a.Id, b.Id], a.Id);
        });
        await OpenField(shown, a.Id);
        TrayDesktop.Run(() => ShownWindow.Type(shown.TagBox, "re"));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.TagField.Field.Suggestions.SequenceEqual(["reaction"])));
        TrayDesktop.Run(() =>
        {
            ShownWindow.Press(shown.TagBox, Key.Down);
            ShownWindow.Press(shown.TagBox, Key.Enter);
        });
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled && shown.ShownChips().Contains("#reaction")));

        TrayDesktop.Run(() =>
        {
            Assert.AreEqual("#reaction", shown.ShownChips()[3], "carried by the most items, its chip comes first after All (G4)");
            ShownWindow.Press(shown.TagBox, Key.Escape);
            ShownWindow.RaiseClick(shown.Chip("Favorites"));
        });
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled && shown.Listing.Scope == Scope.Favorites));
        TrayDesktop.Run(() => ShownWindow.RaiseClick(shown.Chip("#reaction")));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled && shown.Listing.Query == "reaction"));
        TrayDesktop.Run(() =>
        {
            CollectionAssert.AreEqual(new[] { c.Id, b.Id, a.Id }, shown.Listing.Items.Select(item => item.Id).ToList(), "the query's matches, newest-added first (G4, G5)");
            Assert.IsTrue(shown.Lit("#reaction") && shown.Lit("All"));
            ShownWindow.RaiseClick(shown.Chip("#reaction"));
        });
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled && shown.Listing.Query == ""));
        TrayDesktop.Run(() =>
        {
            Assert.IsTrue(shown.Lit("Favorites"), "the lit chip's click clears the search and the prior scope returns (G4, G5)");
            shown.Search.Text = "rea";
        });
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled && shown.Listing.Query == "rea"));
        TrayDesktop.Run(() =>
        {
            CollectionAssert.AreEqual(new[] { "Recent", "Favorites", "All", "#reaction" }, shown.ShownChips().Take(4).ToList(), "the tag being typed, though every match carries it (G5)");
            Assert.AreEqual("stash-tag-01", shown.Listing.Chips[1].Tag, "then the tags that narrow the matches (G5)");
            Assert.IsFalse(shown.Lit("#reaction"), "no word of the query is it (G5)");
        });
        foreach (var exactly in new[] { "#reaction", "reaction", "Reaction" })
        {
            TrayDesktop.Run(() => shown.Search.Text = exactly);
            await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled && shown.Listing.Query == exactly && shown.Lit("#reaction")));
        }

        TrayDesktop.Run(() => ShownWindow.RaiseClick(shown.Chip("All")));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled && shown.Listing.Query == "" && shown.Listing.Scope == Scope.All));
        TrayDesktop.Run(() =>
        {
            shown.Window.UpdateLayout();
            shown.Listing.Select(items.Select(item => item.Id), a.Id);
            shown.RightClick(a.Id);
        });
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Submenu("Add tag").Count == 14));
        var every = TrayDesktop.Get(() => shown.Submenu("Add tag"));
        var removable = TrayDesktop.Get(() => shown.Submenu("Remove tag"));
        TrayDesktop.Run(() =>
        {
            shown.ClickSubmenuEntry("Remove tag", "reaction");
            shown.CloseMenu();
        });
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled && !shown.ChipRow.Children.OfType<Button>().Any(chip => chip.Content as string == "#reaction")));
        TrayDesktop.Run(() =>
        {
            shown.Window.UpdateLayout();
            shown.Listing.Select([c.Id], c.Id);
        });
        await OpenField(shown, c.Id);
        TrayDesktop.Run(() => ShownWindow.Type(shown.TagBox, "re"));
        await Task.Delay(500);
        var suggested = TrayDesktop.Get(() => shown.TagField.Field.Suggestions.ToList());
        TrayDesktop.Run(() => ShownWindow.Press(shown.TagBox, Key.Escape));

        CollectionAssert.AreEqual(many.Prepend("reaction").Prepend("Type a tag…").ToList(), every, "Add tag ▸ offers Type a tag…, then every tag in the stash, reaction on three items first (G17)");
        CollectionAssert.Contains(removable, "reaction", "Remove tag ▸ offers it (G17)");
        Assert.IsEmpty(suggested, "with no item carrying it, the tag is gone from the suggestions as from the chips (G17)");
        Assert.IsTrue(items.All(item => !shown.TagsOf(item.Id).Contains("reaction")));
        Assert.AreEqual(0, shown.Uses());
    }

    [TestMethod]
    public async Task TheFieldStaysOpenForTagAfterTagSuggestingNoneTheItemsCarryAndATagSomeCarryTypedWholeGoesOnEvery()
    {
        using var shown = ShownWindow.Open();
        var items = await shown.OpenStash(shown.Picture("row one.png"), shown.Picture("row two.png"), shown.Picture("row known.png"));
        await shown.Tag([items[2]], "only onion");
        await shown.Tag([items[1]], "x");
        TrayDesktop.Run(() =>
        {
            shown.Grid.Focus();
            shown.Listing.Select([items[0].Id], items[0].Id);
        });

        await OpenField(shown, items[0].Id);
        foreach (var (typed, carried) in new[] { ("one", new[] { "one" }), ("two three", new[] { "one", "three", "two" }), ("brandnew", new[] { "brandnew", "one", "three", "two" }) })
        {
            TrayDesktop.Run(() =>
            {
                ShownWindow.Type(shown.TagBox, typed);
                ShownWindow.Press(shown.TagBox, Key.Enter);
                Assert.IsTrue(shown.TagField.IsVisible && shown.TagBox.IsFocused && (shown.TagBox.Text ?? "") == "", $"after {typed}: open, focus in the emptied box (G17)");
            });
            await ShownWindow.Eventually(() => shown.TagsOf(items[0].Id).SequenceEqual(carried) && TrayDesktop.Get(() => shown.TagField.Field.Tags.SequenceEqual(carried)));
        }

        TrayDesktop.Run(() => ShownWindow.Type(shown.TagBox, "on"));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.TagField.Field.Suggestions.Count > 0));
        TrayDesktop.Run(() =>
        {
            CollectionAssert.AreEqual(new[] { "onion", "only" }, shown.TagField.Field.Suggestions.ToList(), "one, which the item carries, is not suggested (G17)");
            Assert.IsTrue(shown.TagField.GetVisualDescendants().OfType<Button>().Any(button => Avalonia.Automation.AutomationProperties.GetName(button) == "Remove tag one" && button.IsEffectivelyVisible), "the chip one shows with its ×");
            ShownWindow.Press(shown.TagBox, Key.Escape);
            Assert.IsFalse(shown.TagField.IsVisible, "Esc closes it");
            Assert.IsTrue(shown.Grid.IsFocused, "focus back on the grid");
            shown.Listing.Select([items[0].Id, items[1].Id], items[0].Id);
        });

        await OpenField(shown, items[0].Id);
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.TagField.Field.Tags.Contains("x")));
        TrayDesktop.Run(() => ShownWindow.Type(shown.TagBox, "x"));
        await Task.Delay(300);
        TrayDesktop.Run(() =>
        {
            Assert.IsEmpty(shown.TagField.Field.Suggestions, "x, which one of the two carries, shows with its × and is not suggested");
            ShownWindow.Press(shown.TagBox, Key.Enter);
        });
        await ShownWindow.Eventually(() => shown.TagsOf(items[0].Id).Contains("x"));

        CollectionAssert.AreEqual(new[] { "x" }, shown.TagsOf(items[1].Id), "typed whole, it goes on the one without it and stays once on the other");
        TrayDesktop.Run(() => Assert.IsTrue(shown.TagField.IsVisible && shown.TagBox.IsFocused, "the field still open"));
        Assert.AreEqual(0, shown.Uses());
    }

    [TestMethod]
    public async Task FortyTagsScrollWithinTheFieldKeepingTheBoxAndItsSuggestionsInViewAndTheWindowItsSize()
    {
        using var shown = ShownWindow.Open();
        var items = await shown.OpenStash(shown.Picture("forty.png"), shown.Picture("forty other.png"));
        await shown.Tag([items[0]], string.Join(' ', Enumerable.Range(1, 40).Select(number => $"label{number:00}")));
        await shown.Tag([items[1]], "labelless");
        var size = TrayDesktop.Get(() => shown.Window.ClientSize);
        TrayDesktop.Run(() =>
        {
            shown.Grid.Focus();
            shown.Listing.Select([items[0].Id], items[0].Id);
        });

        await OpenField(shown, items[0].Id);
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.TagField.Field.Tags.Count == 40));
        TrayDesktop.Run(() => ShownWindow.Type(shown.TagBox, "label"));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() =>
            shown.TagField.GetVisualDescendants().OfType<Button>().FirstOrDefault(button => button.Content as string == "labelless") is { } suggestion
            && InView(shown, suggestion) && InView(shown, shown.TagBox)));

        TrayDesktop.Run(() =>
        {
            var scroller = shown.TagField.GetVisualDescendants().OfType<ScrollViewer>().First();
            Assert.IsGreaterThan(scroller.Viewport.Height, scroller.Extent.Height, "forty chips are more than its height holds, so it scrolls (G17)");
            Assert.IsLessThan(size.Height / 2, shown.TagField.Bounds.Height, "the field stays a part of the window");
            Assert.AreEqual(size, shown.Window.ClientSize, "the window keeps its size (G2)");
            CollectionAssert.AreEqual(new[] { "labelless" }, shown.TagField.Field.Suggestions.ToList(), "the forty it carries are not suggested");
        });
    }

    [TestMethod]
    public async Task TheFieldOpenWhenImportStashReplacesTheStashClosesAndTagsNoImportedItemThatReusesItsItemsNumber()
    {
        using var pointer = TestMouse.PutBackAfterwards();
        var dialogs = new PresetDialogs();
        using var shown = ShownWindow.Open(dialogs: dialogs);
        var old = (await shown.OpenStash(shown.Picture("old item.png"))).Single();
        TransferredStash.CloseNotices(shown);
        var archive = await TransferredStash.ExportOf(shown, ("replacement", SKColors.Blue));
        dialogs.PickedFile = archive;
        var stash = shown.Listing.Store!.Folder.Root;
        var settingsFolder = Path.GetDirectoryName(stash)!;
        using var shortcutSeam = new WindowsShortcuts();
        var shortcuts = new GlobalShortcuts(shown.Settings, GlobalShortcuts.Bindings(() => { }, () => { }, () => { }, () => { }),
            shortcutSeam, _ => { }, action => Dispatcher.UIThread.Post(action));
        // Nothing is registered: only the real Settings page's focus and its Import controls matter here.
        var transferring = TrayDesktop.Get(() => new Transferring(shown.Listing, shown.Notices, shown.Settings, dialogs,
            new WindowsRecycleBin(), shown.Handoff, shown.OpenWith, shown.Moving, _ => Outcome.Done, TimeProvider.System, HeldFileRetry.Duration));
        SettingsWindow? settings = null;
        try
        {
            TrayDesktop.Run(() =>
            {
                shown.Window.SettingsRequested += () =>
                {
                    var previous = settings;
                    settings = SettingsWindow.Open(previous, () => new SettingsPage(shown.Settings,
                        new SettingsLoad(shown.Settings.Current, null, true), shortcuts, new Locations(stash, settingsFolder),
                        new PresetStartWithWindows(), dialogs, shown.Moving, transferring, _ => { }, "ImageStash.Windows.exe"));
                    if (!ReferenceEquals(previous, settings)) shown.Window.Follow(settings);
                };
                shown.Window.Hide();
                shown.Window.ShowActivated = true;
                shown.Window.SummonFromTray();
                shown.Window.Position = new PixelPoint(40, 40);
            });
            await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Window.IsActive));
            TrayDesktop.Run(() =>
            {
                shown.RightClick(old.Id);
                shown.Submenu("Add tag");
                shown.ClickSubmenuEntry("Add tag", "Type a tag…");
                shown.CloseMenu();
            });
            await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.TagField.IsVisible && shown.TagBox.IsFocused));
            TrayDesktop.Run(() => ShownWindow.Type(shown.TagBox, "pending"));
            TestMouse.Click(TestMouse.Centre(TrayDesktop.Get(() => shown.SettingsButton)));
            await ShownWindow.Eventually(() => TrayDesktop.Get(() => settings is { IsActive: true }));
            // The page's own controls; only the picker is preset, as in SettingsWindowTests.
            TrayDesktop.Run(() => ShownWindow.RaiseClick(SettingsButton(settings!, SettingsPage.ImportWords)));
            await ShownWindow.Eventually(() => TrayDesktop.Get(() => SettingsButton(settings!, SettingsPage.ConfirmImportWords).IsEffectivelyVisible));
            TrayDesktop.Run(() => ShownWindow.RaiseClick(SettingsButton(settings!, SettingsPage.ConfirmImportWords)));
            await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Listing.Replacements == 1 && shown.Listing.Settled));
            var replacement = (await shown.Listing.Store!.All()).Value!.Single();
            Assert.AreEqual(old.Id, replacement.Id, "the archive's item reuses the replaced item's number");
            var survived = TrayDesktop.Get(() => shown.TagField.IsVisible);
            TrayDesktop.Run(() => settings!.Close());
            if (TrayDesktop.Get(() => shown.TagField.IsVisible))
            {
                TestMouse.Click(TestMouse.Centre(TrayDesktop.Get(() => shown.TagBox)));
                TrayDesktop.Run(() => ShownWindow.Press(shown.TagBox, Key.Enter));
                await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled));
            }
            var after = (await shown.Listing.Store!.All()).Value!.Single();
            Assert.IsFalse(survived, "the field opened on an item of the replaced stash closes when the import withdraws it (G17, G36)");
            CollectionAssert.DoesNotContain(after.Tags.ToList(), "pending", $"nothing typed before the import tags the imported item: {string.Join(", ", after.Tags)}");
        }
        finally
        {
            TrayDesktop.Run(() => settings?.Close());
            TransferredStash.RemoveImportsLeftovers(shown, archive);
        }
    }

    private static Button SettingsButton(SettingsWindow settings, string name) => settings.GetVisualDescendants().OfType<Button>()
        .Single(control => AutomationProperties.GetName(control) == name);

    private sealed record Locations(string DefaultStashFolder, string SettingsFolder) : ILocations;

    /// <summary>Whether <paramref name="control"/> lies whole inside the field's scrolled view.</summary>
    private static bool InView(ShownWindow shown, Control control)
    {
        var scroller = shown.TagField.GetVisualDescendants().OfType<ScrollViewer>().First();
        return control.TranslatePoint(default, scroller) is { } top && top.Y >= 0 && top.Y + control.Bounds.Height <= scroller.Viewport.Height + 0.5;
    }

    /// <summary>Right-clicks the tile, chooses Add tag ▸ Type a tag…, and waits for the field to open with focus in its box.</summary>
    private static async Task OpenField(ShownWindow shown, long id)
    {
        TrayDesktop.Run(() =>
        {
            shown.RightClick(id);
            shown.Submenu("Add tag");
        });
        await ClickTypeATag(shown);
    }

    private static async Task ClickTypeATag(ShownWindow shown)
    {
        TrayDesktop.Run(() =>
        {
            shown.ClickSubmenuEntry("Add tag", "Type a tag…");
            shown.CloseMenu();
        });
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.TagField.IsVisible && shown.TagBox.IsFocused));
    }
}
