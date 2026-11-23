using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using ImageStash.Store;
using ImageStash.Window;
using ImageStash.Windows.Tests.Support;

namespace ImageStash.Windows.Tests.Window;

/// <summary>
/// A tag or a name typed into its field kept when the person moves focus on, in the stash window laid out by the real
/// desktop platform (G17, G19, G7): a click elsewhere in the window, real or raised, or Tab, adds the words typed to every
/// item the field was opened on, the lit suggestion in place of the word typed, or renames the item, as Enter would, and
/// focus stays where the person put it; the click then does its own act on the item as it found it, after the commit
/// (G8, G18, G20); Esc still cancels. One check clicks the grid with the real pointer, so the window comes to the front
/// for a moment; one deletes a picture of its own from the preview's menu, and its Undo removes the bin's entry.
/// </summary>
[TestClass]
public sealed class FieldLeavingTests
{
    [TestMethod]
    public async Task ARealClickOnTheGridOutOfTagsOfAddsTheWordsToEveryItemAndLeavesFocusOnTheGrid()
    {
        using var shown = ShownWindow.Open();
        var items = await shown.OpenStash(shown.Picture("leave grid a.png"), shown.Picture("leave grid b.png"));
        TrayDesktop.Run(() =>
        {
            shown.Grid.Focus();
            shown.Listing.Select([items[0].Id, items[1].Id], items[0].Id);
        });
        await OpenField(shown, items[0].Id);
        TrayDesktop.Run(() =>
        {
            ShownWindow.Type(shown.TagBox, "cat funny");
            // The import's notice sits over the grid's foot, where the click goes.
            foreach (var shownNotice in shown.Notices.Shown.ToList())
            {
                shown.Notices.Dismiss(shownNotice);
            }
        });
        // The real pointer lands where the last frame drew the grid.
        await Task.Delay(200);
        using var pointer = TestMouse.PutBackAfterwards();

        TestMouse.Click(TrayDesktop.Get(() => shown.Grid.PointToScreen(new Point(shown.Grid.Bounds.Width / 2, shown.Grid.Bounds.Height - 12))));

        await ShownWindow.Eventually(() => TrayDesktop.Get(() => !shown.TagField.IsVisible));
        TrayDesktop.Run(() => Assert.AreSame(shown.Grid, shown.Focused, "focus where the click put it, so Del and the arrows act there (G7)"));
        await ShownWindow.Eventually(() => shown.TagsOf(items[0].Id).Count == 2 && shown.TagsOf(items[1].Id).Count == 2);
        CollectionAssert.AreEquivalent(new[] { "cat", "funny" }, shown.TagsOf(items[1].Id), "every item the field was opened on, one tag per word, as Enter adds them (G17)");
    }

    [TestMethod]
    public async Task TabOutOfTagsOfAddsTheWordsAndClosesIt()
    {
        using var shown = ShownWindow.Open();
        var item = (await shown.OpenStash(shown.Picture("leave tab.png"))).Single();
        TrayDesktop.Run(() => shown.Grid.Focus());
        await OpenField(shown, item.Id);

        TrayDesktop.Run(() =>
        {
            ShownWindow.Type(shown.TagBox, "dog");
            ShownWindow.Press(shown.TagBox, Key.Tab);
            Assert.IsFalse(shown.TagField.IsVisible, "Tab leaves the field, which closes it (G17)");
            Assert.IsFalse(shown.TagBox.IsFocused);
        });

        await ShownWindow.Eventually(() => shown.TagsOf(item.Id).Count == 1);
        CollectionAssert.AreEqual(new[] { "dog" }, shown.TagsOf(item.Id), "what was typed, added first");
    }

    [TestMethod]
    public async Task AHideAddsNothingAndTheSummonPuttingFocusInTheSearchBoxAddsTheWordsTypedInTagsOf()
    {
        using var shown = ShownWindow.Open();
        var item = (await shown.OpenStash(shown.Picture("leave summon.png"))).Single();
        TrayDesktop.Run(() => shown.Grid.Focus());
        await OpenField(shown, item.Id);
        TrayDesktop.Run(() => ShownWindow.Type(shown.TagBox, "later"));

        TrayDesktop.Run(shown.Deactivate);
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => !shown.Window.IsVisible));
        await Task.Delay(300);
        Assert.IsEmpty(shown.TagsOf(item.Id), "a hide moves no focus inside the window: nothing added while it is away (G1)");
        TrayDesktop.Run(shown.Window.SummonFromTray);
        await TrayDesktop.Get(() => shown.Listing.Settle());

        TrayDesktop.Run(() =>
        {
            Assert.IsFalse(shown.TagField.IsVisible, "the summon puts focus in the search box, which the field takes for focus leaving it (G1, G17)");
            Assert.IsTrue(shown.Search.IsFocused);
        });
        await ShownWindow.Eventually(() => shown.TagsOf(item.Id).Count == 1);
        CollectionAssert.AreEqual(new[] { "later" }, shown.TagsOf(item.Id), "the words typed added first, as Enter would");
    }

    [TestMethod]
    public async Task AClickOnThePreviewsNameAddsTheTagTypedThenEditsTheName()
    {
        using var shown = ShownWindow.Open();
        var item = (await shown.OpenStash(shown.Picture("leave name click.png"))).Single();
        await OpenAndRead(shown, item);

        TrayDesktop.Run(() =>
        {
            shown.PreviewTagBox.Focus();
            ShownWindow.Type(shown.PreviewTagBox, "named");
            // The name takes no focus itself: its click puts focus in its box.
            ShownWindow.RaiseClick(shown.NameButton);
            Assert.IsTrue(shown.Preview.EditingName && shown.NameBox.IsFocused, "the click does what it does: the name in edit");
            Assert.AreEqual("", shown.PreviewTagBox.Text ?? "");
        });

        await ShownWindow.Eventually(() => shown.TagsOf(item.Id).Count == 1);
        CollectionAssert.AreEqual(new[] { "named" }, shown.TagsOf(item.Id), "focus left the tag box for the name: the tag added (G17)");
        TrayDesktop.Run(() => ShownWindow.Press(shown.NameBox, Key.Escape));
    }

    [TestMethod]
    public async Task EscWithWordsTypedAddsNothingAndLeavesTheNameAsItWas()
    {
        using var shown = ShownWindow.Open();
        var item = (await shown.OpenStash(shown.Picture("leave esc.png"))).Single();
        TrayDesktop.Run(() => shown.Grid.Focus());
        await OpenField(shown, item.Id);
        TrayDesktop.Run(() =>
        {
            ShownWindow.Type(shown.TagBox, "nope");
            ShownWindow.Press(shown.TagBox, Key.Escape);
            Assert.IsFalse(shown.TagField.IsVisible, "Esc closes the field");
        });
        await OpenAndRead(shown, item);

        TrayDesktop.Run(() =>
        {
            shown.PreviewTagBox.Focus();
            ShownWindow.Type(shown.PreviewTagBox, "nope");
            ShownWindow.Press(shown.PreviewTagBox, Key.Escape);
            Assert.AreSame(shown.PreviewPicture, shown.Focused, "Esc gives focus back to the preview, which commits nothing (G19)");
            ShownWindow.Press(shown.PreviewPicture, Key.F2);
            ShownWindow.Type(shown.NameBox, "nope");
            ShownWindow.Press(shown.NameBox, Key.Escape);
            Assert.IsFalse(shown.Preview.EditingName);
        });
        await Task.Delay(300);

        Assert.IsEmpty(shown.TagsOf(item.Id), "Esc cancels: no tag added (G19)");
        Assert.AreEqual("leave esc", (await shown.Listing.Store!.Item(item.Id)).Value!.Name, "and the name as it was");
    }

    [TestMethod]
    public async Task AClickAwayAddsTheLitSuggestionNotTheLetterTyped()
    {
        using var shown = ShownWindow.Open();
        var items = await shown.OpenStash(shown.Picture("leave lit.png"), shown.Picture("leave lit other.png"));
        var item = items.Single(each => each.Name == "leave lit");
        await shown.Tag([items.Single(each => each.Id != item.Id)], "reaction");
        await OpenAndRead(shown, item);
        TrayDesktop.Run(() =>
        {
            shown.PreviewTagBox.Focus();
            ShownWindow.Type(shown.PreviewTagBox, "r");
        });
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.PreviewTags.Field.Suggestions.Contains("reaction")));

        TrayDesktop.Run(() =>
        {
            ShownWindow.Press(shown.PreviewTagBox, Key.Down);
            shown.Click(shown.PreviewPicture);
            Assert.AreSame(shown.PreviewPicture, shown.Focused, "focus where the click put it");
            Assert.AreEqual("", shown.PreviewTagBox.Text ?? "", "the box emptied, as Enter empties it");
        });

        await ShownWindow.Eventually(() => shown.TagsOf(item.Id).Count == 1);
        CollectionAssert.AreEqual(new[] { "reaction" }, shown.TagsOf(item.Id), "the lit suggestion, as Enter would take it (G17)");
    }

    [TestMethod]
    public async Task AClickOnTheHandoffButtonAddsTheTagOrRenamesThenHandsOverWithFocusOnTheButton()
    {
        using var shown = ShownWindow.Open();
        var item = (await shown.OpenStash(shown.Picture("leave handoff.png"))).Single();
        await OpenAndRead(shown, item);
        TrayDesktop.Run(() =>
        {
            shown.PreviewTagBox.Focus();
            ShownWindow.Type(shown.PreviewTagBox, "cat");
            Clicked(shown.HandoffButton);
            Assert.AreSame(shown.HandoffButton, shown.Focused, "the button keeps the focus the click gave it");
        });

        await ShownWindow.Eventually(() => shown.TagsOf(item.Id).Count == 1 && shown.Uses() == 1);
        CollectionAssert.AreEqual(new[] { "cat" }, shown.TagsOf(item.Id), "the tag added, then the picture handed over (G8, G10)");

        TrayDesktop.Run(() =>
        {
            ShownWindow.Press(shown.HandoffButton, Key.F2);
            ShownWindow.Type(shown.NameBox, "handed over");
            Clicked(shown.HandoffButton);
            Assert.AreSame(shown.HandoffButton, shown.Focused, "out of the name too: focus stays where the person put it (G19)");
        });
        await ShownWindow.Eventually(() => shown.Uses() == 2);
        Assert.AreEqual("handed over", (await shown.Listing.Store!.Item(item.Id)).Value!.Name, "renamed before the handoff, which waits for it");
    }

    [TestMethod]
    public async Task ANameTypedThenAClickOnEditOpensTheEditorOnTheRenamedItem()
    {
        using var shown = ShownWindow.Open();
        var item = (await shown.OpenStash(shown.Picture("leave edit.png"))).Single();
        await OpenAndRead(shown, item);

        TrayDesktop.Run(() =>
        {
            ShownWindow.Press(shown.PreviewPicture, Key.F2);
            ShownWindow.Type(shown.NameBox, "renamed for edit");
            Clicked(shown.EditButton);
        });

        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Editing.Session is not null));
        TrayDesktop.Run(() => Assert.AreEqual("renamed for edit", shown.Editing.Session!.Item.Name, "Edit opens once the rename has run (G20)"));
        TrayDesktop.Run(() => shown.Editing.Cancel());
    }

    [TestMethod]
    public async Task ATagTypedThenDeleteFromThePreviewsMenuDeletesTheItemWithItsTagAndNamesNoFailure()
    {
        using var shown = ShownWindow.Open();
        var item = (await shown.OpenStash(shown.Picture("leave delete.png"))).Single();
        await OpenAndRead(shown, item);

        TrayDesktop.Run(() =>
        {
            shown.PreviewTagBox.Focus();
            ShownWindow.Type(shown.PreviewTagBox, "gone");
            Clicked(shown.MoreButton);
            shown.ClickMenuEntry("Delete");
        });

        await ShownWindow.Eventually(() => TrayDesktop.Get(() => !shown.Preview.IsOpen && shown.Listing.Items.Count == 0));
        TrayDesktop.Run(() => Assert.IsFalse(shown.Notices.Shown.Any(notice => notice.NamesAProblem), "no failure about an item that is gone: " + shown.Said()));
        var notice = TrayDesktop.Get(() => shown.Notices.Shown.Single(each => each.Text.StartsWith("Deleted ")));
        TrayDesktop.Run(() => _ = shown.Notices.Undo(notice));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Listing.Items.Count == 1));
        CollectionAssert.AreEqual(new[] { "gone" }, shown.TagsOf(item.Id), "the tag was added before the Delete took the item (G18)");
        Assert.IsEmpty(RecycleBinEntries.Of(item.FilePath), "Undo took its entry out of the Recycle Bin again");
    }

    /// <summary>A click on <paramref name="button"/> as the pointer makes it: focus moved by the pointer, then its Click; on the UI thread.</summary>
    private static void Clicked(Button button)
    {
        button.Focus(NavigationMethod.Pointer);
        ShownWindow.RaiseClick(button);
    }

    private static async Task OpenField(ShownWindow shown, long id)
    {
        TrayDesktop.Run(() =>
        {
            shown.RightClick(id);
            shown.Submenu("Add tag");
            shown.ClickSubmenuEntry("Add tag", "Type a tag…");
            shown.CloseMenu();
        });
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.TagField.IsVisible && shown.TagBox.IsFocused));
    }

    private static async Task OpenAndRead(ShownWindow shown, StashItem item)
    {
        TrayDesktop.Run(() => shown.OpenPreview(item));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Preview.Facts.Count > 0));
    }
}
