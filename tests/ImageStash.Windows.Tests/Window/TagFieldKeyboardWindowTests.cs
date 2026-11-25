using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using ImageStash.Seams;
using ImageStash.Window;
using ImageStash.Windows.Tests.Support;
using static ImageStash.Windows.Tests.Support.ShownWindow;

namespace ImageStash.Windows.Tests.Window;

/// <summary>
/// Keyboard keys traverse the shown Tags of overlay, preview tag row and Every tag popup (G17, G19),
/// leaving the query and grid selection intact (G2, G7) while the store follows the edit's own items.
/// </summary>
[TestClass]
public sealed class TagFieldKeyboardWindowTests
{
    private const string Query = "reaction";

    [TestMethod]
    public async Task EmptyBackspaceHighlightsTheLastChipUntilEscapeTypingOrAnotherBackspace()
    {
        using var pointer = TestMouse.PutBackAfterwards();
        using var shown = Open();
        var items = await shown.OpenStash(shown.Picture("reaction one.png"), shown.Picture("reaction two.png"), shown.Picture("reaction other.png"));
        await shown.Tag([items[0], items[1]], "blue");
        await shown.Tag([items[0], items[2]], "zebra");
        var field = await OpenTags(shown, [items[0].Id, items[1].Id]);
        await Eventually(() => TrayDesktop.Get(() => field.Tags.SequenceEqual(["blue", "zebra"])));
        // The field is already open on the mixed selection when the grid's selection changes.
        TrayDesktop.Run(() => shown.Listing.Select([items[2].Id], items[2].Id));
        var blue = TrayDesktop.Get(() => ChipLook(field, "blue"));
        var zebra = TrayDesktop.Get(() => ChipLook(field, "zebra"));

        PressKey(shown, Key.Back);
        await Eventually(() => TrayDesktop.Get(() => field.Tags.Contains("zebra") && ChipLook(field, "zebra") != zebra));
        Assert.AreEqual(blue, TrayDesktop.Get(() => ChipLook(field, "blue")), "only the last chip changes appearance");
        CollectionAssert.AreEqual(new[] { "blue", "zebra" }, shown.TagsOf(items[0].Id), "one Backspace removes nothing");

        PressKey(shown, Key.Escape);
        await Eventually(() => TrayDesktop.Get(() => !shown.TagField.IsVisible && shown.Grid.IsFocused && ChipLook(field, "zebra") == zebra));
        // Reopen on the original mixed selection, then move the grid selection again.
        TrayDesktop.Run(() => shown.Listing.Select([items[0].Id, items[1].Id], items[0].Id));
        PressKey(shown, Key.T, KeyModifiers.Control);
        await Eventually(() => TrayDesktop.Get(() => shown.TagBox.IsFocused && field.Tags.Contains("zebra")));
        TrayDesktop.Run(() => shown.Listing.Select([items[2].Id], items[2].Id));
        PressKey(shown, Key.Back);
        await Eventually(() => TrayDesktop.Get(() => field.Tags.Contains("zebra") && ChipLook(field, "zebra") != zebra));
        PressKey(shown, Key.Q);
        await Eventually(() => TrayDesktop.Get(() => field.Text.Length == 1 && ChipLook(field, "zebra") == zebra));
        PressKey(shown, Key.Back);
        await Eventually(() => TrayDesktop.Get(() => field.Text.Length == 0));
        Assert.AreEqual(zebra, TrayDesktop.Get(() => ChipLook(field, "zebra")), "Backspace with text erases the character");

        PressKey(shown, Key.Back);
        await Eventually(() => TrayDesktop.Get(() => field.Tags.Contains("zebra") && ChipLook(field, "zebra") != zebra));
        CollectionAssert.AreEqual(new[] { "blue", "zebra" }, shown.TagsOf(items[0].Id), "typing left the next empty Backspace to highlight again");
        PressKey(shown, Key.Back);
        await Eventually(() => TrayDesktop.Get(() => field.Tags.SequenceEqual(["blue"])));

        CollectionAssert.AreEqual(new[] { "blue" }, shown.TagsOf(items[0].Id));
        CollectionAssert.AreEqual(new[] { "blue" }, shown.TagsOf(items[1].Id));
        CollectionAssert.AreEqual(new[] { "zebra" }, shown.TagsOf(items[2].Id), "the newly selected item keeps its tag");
        TrayDesktop.Run(() => Assert.IsTrue(shown.TagField.IsVisible && shown.TagBox.IsFocused));
        AssertWindowState(shown, [items[2].Id]);
    }

    [TestMethod]
    public async Task ReverseTabThroughThePreviewTagRowScrollsItsRemoveButtonsIntoViewAndEnterUsesTheFocusedOne()
    {
        using var pointer = TestMouse.PutBackAfterwards();
        using var shown = Open();
        var items = await shown.OpenStash(shown.Picture("reaction preview.png"), shown.Picture("reaction selected.png"));
        var tags = Enumerable.Range(1, 12).Select(number => $"reaction-label-for-keyboard-{number:00}").ToArray();
        await shown.Tag([items[0]], string.Join(' ', tags));
        await shown.Tag([items[1]], tags[0]);
        var field = await OpenTags(shown, [items[1].Id], preview: items[0].Id);
        var scroller = TrayDesktop.Get(() => shown.PreviewTags.GetVisualDescendants().OfType<ScrollViewer>().First());
        await Eventually(() => TrayDesktop.Get(() => field.Tags.SequenceEqual(tags) && ShownTagLists.InView(scroller, shown.PreviewTagBox)));
        TrayDesktop.Run(() => Assert.IsFalse(ShownTagLists.InView(scroller, RemoveButton(field, tags[0])), "the first × starts above the viewport"));

        TrayDesktop.Run(scroller.ScrollToHome);
        await Eventually(() => TrayDesktop.Get(() => !ShownTagLists.InView(scroller, RemoveButton(field, tags[^1]))));
        PressKey(shown, Key.Back);
        await Eventually(() => TrayDesktop.Get(() => ShownTagLists.InView(scroller, RemoveButton(field, tags[^1]))));

        foreach (var tag in tags.Reverse())
        {
            var remove = TrayDesktop.Get(() => RemoveButton(field, tag));
            PressKey(shown, Key.Tab, KeyModifiers.Shift);
            await Eventually(() => TrayDesktop.Get(() => remove.IsFocused && ShownTagLists.InView(scroller, remove)));
        }

        // Traverse forwards once too, then use the first × rather than the last chip in the row.
        PressKey(shown, Key.Tab);
        await Eventually(() => TrayDesktop.Get(() => RemoveButton(field, tags[1]).IsFocused));
        PressKey(shown, Key.Tab, KeyModifiers.Shift);
        await Eventually(() => TrayDesktop.Get(() => RemoveButton(field, tags[0]).IsFocused));
        PressKey(shown, Key.Enter);
        await Eventually(() => TrayDesktop.Get(() => field.Tags.SequenceEqual(tags.Skip(1))));

        CollectionAssert.AreEqual(tags[1..], shown.TagsOf(items[0].Id), "Enter used the focused × on the preview's item");
        CollectionAssert.AreEqual(new[] { tags[0] }, shown.TagsOf(items[1].Id), "the grid's selected item keeps its tag");
        TrayDesktop.Run(() => Assert.AreEqual(items[0].Id, shown.Preview.Item?.Id));
        AssertWindowState(shown, [items[1].Id]);
    }

    [TestMethod]
    public async Task PastingTextFromAFocusedRemoveButtonReturnsToTheBoxForEnter()
    {
        using var pointer = TestMouse.PutBackAfterwards();
        using var shown = Open();
        var item = (await shown.OpenStash(shown.Picture("reaction paste.png"))).Single();
        await shown.Tag([item], "blue");
        var field = await OpenTags(shown, [item.Id]);
        PressKey(shown, Key.Tab, KeyModifiers.Shift);
        await Eventually(() => TrayDesktop.Get(() => RemoveButton(field, "blue").IsFocused));

        TestClipboard.PutText("pasted words");
        PressKey(shown, Key.V, KeyModifiers.Control);
        await Eventually(() => TrayDesktop.Get(() => shown.TagBox.IsFocused && field.Text == "pasted words"));
        PressKey(shown, Key.Enter);
        await Eventually(() => shown.TagsOf(item.Id).SequenceEqual(["blue", "pasted", "words"]));

        AssertWindowState(shown, [item.Id]);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task DownFocusesTheFirstPickerEntryEnterTogglesAndEscapeUnwindsTheTagEdit(bool preview)
    {
        using var pointer = TestMouse.PutBackAfterwards();
        using var shown = Open();
        var items = await shown.OpenStash(shown.Picture("reaction one.png"), shown.Picture("reaction two.png"),
            shown.Picture("other three.png"), shown.Picture("other four.png"));
        await shown.Tag([items[0], items[1], items[2]], "blue");
        // Keep both entries in the list through either host's toggle. The two outside the query are never edited.
        await shown.Tag([items[1], items[2], items[3]], "amber");
        long[] selected = preview ? [items[1].Id] : [items[0].Id, items[1].Id];
        var field = await OpenTags(shown, selected, preview ? items[0].Id : null);
        await Eventually(() => TrayDesktop.Get(() => EveryTagButton(field).IsEffectivelyVisible));
        var picker = field.Picker;

        PressKey(shown, Key.Down);
        await PickerFocus(picker, "amber");
        TrayDesktop.Run(() => CollectionAssert.AreEqual(new[] { new TagPickerEntry("amber", 3, false), new TagPickerEntry("blue", 3, true) }, picker.Entries.ToArray()));
        PressKey(shown, Key.Enter, picker: picker);
        await Eventually(() => TrayDesktop.Get(() => picker.IsOpen && picker.Entries.SequenceEqual([new("amber", 4, true), new("blue", 3, true)])
            && field.Tags.SequenceEqual(["amber", "blue"])));
        await PickerFocus(picker, "amber");
        CollectionAssert.AreEqual(new[] { "amber", "blue" }, shown.TagsOf(items[0].Id));
        CollectionAssert.AreEqual(new[] { "amber", "blue" }, shown.TagsOf(items[1].Id));

        PressKey(shown, Key.Down, picker: picker);
        await PickerFocus(picker, "blue");
        PressKey(shown, Key.Enter, picker: picker);
        await Eventually(() => TrayDesktop.Get(() => picker.IsOpen && picker.Entries.SequenceEqual([new("amber", 4, true), new("blue", preview ? 2 : 1, false)])
            && field.Tags.SequenceEqual(["amber"])));
        await PickerFocus(picker, "blue");
        PressKey(shown, Key.Up, picker: picker);
        await PickerFocus(picker, "amber");

        CollectionAssert.AreEqual(new[] { "amber" }, shown.TagsOf(items[0].Id));
        CollectionAssert.AreEqual(preview ? new[] { "amber", "blue" } : new[] { "amber" }, shown.TagsOf(items[1].Id));
        CollectionAssert.AreEqual(new[] { "amber", "blue" }, shown.TagsOf(items[2].Id), "picks are scoped to the edit's items");
        CollectionAssert.AreEqual(new[] { "amber" }, shown.TagsOf(items[3].Id));
        AssertWindowState(shown, selected);

        var box = TrayDesktop.Get(() => preview ? shown.PreviewTagBox : shown.TagBox);
        PressKey(shown, Key.Escape, picker: picker);
        await Eventually(() => TrayDesktop.Get(() => !picker.IsOpen && box.IsFocused));
        TrayDesktop.Run(() => Assert.IsTrue(preview ? shown.Preview.IsOpen : shown.TagField.IsVisible, "the first Esc closes only the picker (G17)"));
        PressKey(shown, Key.Escape);
        if (preview)
        {
            await Eventually(() => TrayDesktop.Get(() => shown.Preview.IsOpen && shown.PreviewPicture.IsFocused));
            PressKey(shown, Key.Escape);
            await Eventually(() => TrayDesktop.Get(() => !shown.Preview.IsOpen));
        }
        else
        {
            await Eventually(() => TrayDesktop.Get(() => !shown.TagField.IsVisible && shown.Grid.IsFocused));
        }

        AssertWindowState(shown, selected);
    }

    private static async Task<TagField> OpenTags(ShownWindow shown, long[] selected, long? preview = null)
    {
        await shown.ClickSearchInFront();
        TrayDesktop.Run(() => shown.Search.Text = Query);
        await Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled && shown.Listing.Query == Query));
        TrayDesktop.Run(() =>
        {
            shown.Listing.Select(preview is long id ? [id] : selected, preview ?? selected[0]);
            Assert.IsTrue(shown.Grid.Focus());
        });
        if (preview is long previewId)
        {
            PressKey(shown, Key.Space);
            await Eventually(() => TrayDesktop.Get(() => shown.Preview.Item?.Id == previewId && shown.PreviewPicture.IsFocused));
            TrayDesktop.Run(() => shown.Listing.Select(selected, selected[0]));
        }

        PressKey(shown, Key.T, KeyModifiers.Control);
        await Eventually(() => TrayDesktop.Get(() => preview.HasValue ? shown.PreviewTagBox.IsFocused : shown.TagField.IsVisible && shown.TagBox.IsFocused));
        return TrayDesktop.Get(() => preview.HasValue ? shown.PreviewTags.Field : shown.TagField.Field);
    }

    private static void PressKey(ShownWindow shown, Key key, KeyModifiers modifiers = KeyModifiers.None, TagPicker? picker = null)
    {
        var foreground = FrontWindow.GetForegroundWindow();
        Assert.IsTrue(TrayDesktop.Get(() => foreground == shown.Window.TryGetPlatformHandle()?.Handle
            || (picker is { IsOpen: true, Child: { } card } && foreground == TopLevel.GetTopLevel(card)?.TryGetPlatformHandle()?.Handle)),
            $"the stash window or its picker must receive the real {key} key");
        TestHotKeys.Press(new Shortcut(new KeyGesture(key, modifiers), key.ToString()));
    }

    private static Task PickerFocus(TagPicker picker, string tag) => Eventually(() => TrayDesktop.Get(() =>
        picker.IsOpen && picker.Child!.GetVisualDescendants().OfType<Button>()
            .Any(button => button.IsFocused && AutomationProperties.GetName(button) == tag)));

    private static Button RemoveButton(TagField field, string tag) => field.GetVisualDescendants().OfType<Button>()
        .Single(button => AutomationProperties.GetName(button) == $"Remove tag {tag}");

    private static (IBrush? Background, IBrush? Border, Thickness Thickness, IBrush? Words) ChipLook(TagField field, string tag)
    {
        var chip = RemoveButton(field, tag).FindAncestorOfType<Border>()!;
        var words = chip.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text == tag);
        return (chip.Background, chip.BorderBrush, chip.BorderThickness, words.Foreground);
    }

    private static void AssertWindowState(ShownWindow shown, long[] selection)
    {
        TrayDesktop.Run(() =>
        {
            Assert.IsTrue(shown.Window.IsVisible);
            Assert.AreEqual(Query, shown.Search.Text);
            Assert.AreEqual(Query, shown.Listing.Query);
            Assert.AreEqual(selection[0], shown.Listing.Focused);
            CollectionAssert.AreEquivalent(selection, shown.Listing.Selection.ToArray());
            Assert.IsFalse(shown.Notices.Shown.Any(notice => notice.NamesAProblem), shown.Said());
        });
        Assert.AreEqual(0L, shown.Uses(), "tag-editing keys never handed off an item");
    }
}
