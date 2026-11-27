using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using ImageStash.Seams;
using ImageStash.Store;
using ImageStash.Window;
using ImageStash.Windows.Tests.Support;
using static ImageStash.Windows.Tests.Support.ShownWindow;

namespace ImageStash.Windows.Tests.Window;

[TestClass]
public sealed class TagRenameWindowTests
{
    private const string FocusTag = "focus";
    private const string Draft = "discarded";
    private const string Renamed = "renamed";
    private const ushort Escape = 0x1B;
    private const ushort Enter = 0x0D;

    [TestMethod]
    [DataRow("chip", false)]
    [DataRow("chip picker", false)]
    [DataRow("selection picker", false)]
    [DataRow("preview picker", false)]
    [DataRow("chip", true)]
    [DataRow("chip picker", true)]
    [DataRow("selection picker", true)]
    [DataRow("preview picker", true)]
    public async Task TheTagMenuRenamesInPlaceAndItsKeysStayInTheRename(string origin, bool pinned)
    {
        using var pointer = TestMouse.PutBackAfterwards();
        using var shown = Open();
        var items = await shown.OpenStash(shown.Picture("shown.png"), shown.Picture("outside.png"));
        var match = items.Single(item => item.Name == "shown");
        var carrier = items.Single(item => item.Name == "outside");
        var tag = origin == "chip" ? "elsewhere" : "zz-elsewhere";
        var scrollTags = origin == "chip" ? Array.Empty<string>() :
            Enumerable.Range(1, 60).Select(i => $"scroll-tag-{i:00}").ToArray();
        await shown.Tag([match], FocusTag);
        await shown.Tag([carrier], string.Join(' ', scrollTags.Append(tag)));

        if (pinned)
        {
            TrayDesktop.Run(shown.Window.SummonPinned);
        }

        await shown.ClickSearchInFront();
        TrayDesktop.Run(() => RaiseClick(shown.Chip("#" + FocusTag)));
        await QueryIs(shown, FocusTag);

        var picker = origin == "chip" ? null : await OpenPicker(shown, match, origin, tag);
        var source = await FindAnchor(shown, picker, tag);
        if (picker is null)
        {
            TrayDesktop.Run(() =>
            {
                Assert.IsTrue(source.IsEffectivelyEnabled, "the grey chip still represents a live tag");
                Assert.IsLessThan(1, source.Opacity, "the match does not carry the tag");
            });
        }

        var editor = await OpenRename(source, tag);
        await TypeDraft(editor, Draft);
        TestHotKeys.Tap(Escape);
        await RenameClosed(editor);
        AssertInteraction(shown, match, origin, picker);
        CollectionAssert.AreEquivalent(scrollTags.Append(tag).ToArray(), shown.TagsOf(carrier.Id), "Esc discarded the draft");
        CollectionAssert.AreEqual(new[] { FocusTag }, shown.TagsOf(match.Id));

        var itemPick = origin is "selection picker" or "preview picker";
        await PickAfterCancel(shown, match, origin, picker, tag, itemPick);

        source = await FindAnchor(shown, picker, tag, atStart: itemPick);
        editor = await OpenRename(source, tag);
        await TypeDraft(editor, Renamed);
        TestHotKeys.Tap(Enter);
        await RenameClosed(editor);
        var carriers = itemPick ? 2 : 1;
        await Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled &&
            shown.Listing.Tags.Any(count => count.Tag == Renamed && count.Items == carriers)));

        AssertInteraction(shown, match, origin, picker);
        CollectionAssert.AreEquivalent(scrollTags.Append(Renamed).ToArray(), shown.TagsOf(carrier.Id));
        string[] expectedMatchTags = itemPick ? [FocusTag, Renamed] : [FocusTag];
        CollectionAssert.AreEqual(expectedMatchTags, shown.TagsOf(match.Id), "Enter did not pick a tag or add field text");
        Assert.AreEqual(0L, shown.Uses(), "the rename keys never handed off an item");
    }

    [TestMethod]
    public async Task ARecasedChipTrimsItsWiderSpellingAndKeepsTheNeighboringTargets()
    {
        using var pointer = TestMouse.PutBackAfterwards();
        using var shown = Open();
        var item = (await shown.OpenStash(shown.Picture("one.png"))).Single();
        const string original = "wwwwwwww";
        const string corrected = "WWWWWWWW";
        await shown.Tag([item], $"alpha {original} zulu");
        await shown.ClickSearchInFront();
        TrayDesktop.Run(() => RaiseClick(shown.Chip("All")));
        await Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled &&
            shown.ShownChips().Contains("#zulu")));
        var (source, left, right) = TrayDesktop.Get(() =>
            (shown.Chip("#" + original), shown.Chip("#alpha"), shown.Chip("#zulu")));
        var places = TrayDesktop.Get(() => (source.Bounds, left.Bounds, right.Bounds));
        var editor = await OpenRename(source, original);
        TrayDesktop.Run(() => Type(editor, corrected));
        TestHotKeys.Tap(Enter);
        await RenameClosed(editor);
        await Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled &&
            Equals(ToolTip.GetTip(source), "#" + corrected)));

        TrayDesktop.Run(() =>
        {
            Assert.AreSame(source, shown.Chip("#" + corrected));
            Assert.AreEqual(places, (source.Bounds, left.Bounds, right.Bounds));
            Assert.AreEqual("#" + corrected, AutomationProperties.GetName(source));
        });
        TestMouse.Click(await HitPoint(right));
        await QueryIs(shown, "zulu");
    }

    [TestMethod]
    public async Task AClickElsewhereInTheWindowKeepsTheTypedNameAsEnterDoes()
    {
        using var pointer = TestMouse.PutBackAfterwards();
        using var shown = Open();
        var carrier = (await shown.OpenStash(shown.Picture("carrier.png"))).Single();
        await shown.Tag([carrier], "elsewhere");
        await shown.ClickSearchInFront();

        var editor = await OpenRename(await FindAnchor(shown, null, "elsewhere"), "elsewhere");
        await TypeDraft(editor, Renamed);
        TestMouse.Click(TrayDesktop.Get(() => shown.TileOnScreen(carrier.Id)));
        await RenameClosed(editor);
        await Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled && shown.Listing.Tags.Any(count => count.Tag == Renamed)));

        CollectionAssert.AreEqual(new[] { Renamed }, shown.TagsOf(carrier.Id), "focus moving to another part of the window renames, as Enter does (G17)");
        TrayDesktop.Run(() => Assert.IsFalse(shown.Search.IsFocused, "focus stays where the click put it, not given back to the search box it came from"));
    }

    private static async Task PickAfterCancel(ShownWindow shown, StashItem match, string origin, TagPicker? picker,
        string tag, bool itemPick)
    {
        // The live grey chip cannot filter in this query. Use the lit chip to prove the row still accepts a normal pick.
        var pick = picker is null ? FocusTag : tag;
        TestMouse.Click(await HitPoint(await FindAnchor(shown, picker, pick)));
        if (itemPick)
        {
            // The picked tag now has two carriers and moves to the top of the list.
            await Eventually(() => TrayDesktop.Get(() => picker!.Entries.FirstOrDefault() is { Count: 2, Marked: true } entry &&
                entry.Tag == tag));
            CollectionAssert.AreEqual(new[] { FocusTag, tag }, shown.TagsOf(match.Id), "the original row picked its tag after Esc");
            return;
        }

        await QueryIs(shown, picker is null ? "" : $"{FocusTag} {tag}");

        // Restore the filter with a second ordinary pick before opening the next rename.
        if (picker is not null && !TrayDesktop.Get(() => picker.IsOpen))
        {
            await OpenPicker(shown, match, origin, tag);
        }

        TestMouse.Click(await HitPoint(await FindAnchor(shown, picker, pick)));
        await QueryIs(shown, FocusTag);
        if (picker is not null && !TrayDesktop.Get(() => picker.IsOpen))
        {
            await OpenPicker(shown, match, origin, tag);
        }
    }

    private static Task QueryIs(ShownWindow shown, string query) => Eventually(() => TrayDesktop.Get(() =>
        shown.Listing.Settled && shown.Listing.Query == query && shown.Search.Text == query));

    private static void AssertInteraction(ShownWindow shown, StashItem match, string origin, TagPicker? picker)
    {
        TrayDesktop.Run(() =>
        {
            Assert.IsTrue(shown.Window.IsVisible);
            Assert.AreEqual(FocusTag, shown.Search.Text, "the rename did not pick a filter or clear the query");
            if (picker is not null)
            {
                Assert.IsTrue(picker.IsOpen, "the rename key left Every tag open");
            }

            if (origin == "preview picker")
            {
                Assert.AreEqual(match.Id, shown.Preview.Item?.Id, "the preview stayed open on the same item");
                Assert.AreEqual("", shown.PreviewTags.Field.Text, "the preview's tag field received no draft text");
            }
            else if (origin == "selection picker")
            {
                Assert.IsTrue(shown.TagField.IsVisible, "the selection's tag field stayed open");
                CollectionAssert.AreEqual(new[] { match.Id }, shown.Listing.Selection.ToArray());
                Assert.AreEqual("", shown.TagField.Field.Text, "the selection's tag field received no draft text");
            }
        });
    }

    private static async Task<TextBox> OpenRename(Button source, string tag)
    {
        TestMouse.RightClick(await HitPoint(source));
        await Eventually(() => TrayDesktop.Get(() => source.ContextMenu is { IsOpen: true } menu &&
            menu.Items.OfType<MenuItem>().First().Bounds.Height > 0));
        var rename = TrayDesktop.Get(() => ShownTagLists.Entry(source.ContextMenu!, "Rename tag…"));
        TestMouse.Click(await HitPoint(rename));

        TextBox? editor = null;
        await Eventually(() => TrayDesktop.Get(() =>
        {
            editor = source.GetVisualDescendants().OfType<TextBox>().SingleOrDefault(box =>
                AutomationProperties.GetName(box) == "Rename tag" && box.IsEffectivelyVisible);
            return editor is { IsFocused: true } && editor.Bounds.Height > 0 && source.ContextMenu is { IsOpen: false };
        }));
        TrayDesktop.Run(() => Assert.AreEqual(tag, editor!.Text, "the inline box starts with the chosen tag"));
        return editor!;
    }

    private static async Task TypeDraft(TextBox editor, string text)
    {
        TestHotKeys.Press(Shortcut.Parse("Ctrl+A").Value!);
        foreach (var letter in text)
        {
            TestHotKeys.Tap(char.ToUpperInvariant(letter));
        }

        await Eventually(() => TrayDesktop.Get(() => editor.IsFocused && editor.Text == text));
    }

    private static Task RenameClosed(TextBox editor) => Eventually(() => TrayDesktop.Get(() =>
        !editor.IsEffectivelyVisible || TopLevel.GetTopLevel(editor) is null));

    private static async Task<TagPicker> OpenPicker(ShownWindow shown, StashItem match, string origin, string tag)
    {
        var picker = TrayDesktop.Get(() =>
        {
            if (origin == "chip picker")
            {
                CollectionAssert.DoesNotContain(shown.ShownChips(), "#" + tag);
                RaiseClick(shown.Chip("Every tag"));
                return shown.ChipRow.Picker;
            }

            shown.Listing.Select([match.Id], match.Id);
            if (origin == "preview picker")
            {
                shown.OpenPreview(match);
                Tap(shown.PreviewPicture, Key.T, KeyModifiers.Control);
                return shown.PreviewTags.Field.Picker;
            }

            shown.Grid.Focus();
            Tap(shown.Grid, Key.T, KeyModifiers.Control);
            return shown.TagField.Field.Picker;
        });

        if (origin != "chip picker")
        {
            var opener = TrayDesktop.Get(() => EveryTagButton(origin == "preview picker" ? shown.PreviewTags.Field : shown.TagField.Field));
            await Eventually(() => TrayDesktop.Get(() => opener.IsEffectivelyVisible));
            TrayDesktop.Run(() => RaiseClick(opener));
        }

        return picker;
    }

    private static async Task<Button> FindAnchor(ShownWindow shown, TagPicker? picker, string tag, bool atStart = false)
    {
        if (picker is null)
        {
            return TrayDesktop.Get(() => shown.Chip("#" + tag));
        }

        var scroll = TrayDesktop.Get(() => (ScrollViewer)((Border)picker.Child!).Child!);
        await Eventually(() => TrayDesktop.Get(() => picker.IsOpen && scroll.Extent.Height > scroll.Viewport.Height));
        TrayDesktop.Run(atStart ? scroll.ScrollToHome : scroll.ScrollToEnd);
        await Eventually(() => TrayDesktop.Get(() => scroll.GetVisualDescendants().OfType<Button>()
            .Any(button => AutomationProperties.GetName(button) == tag && ShownTagLists.InView(scroll, button))));
        return TrayDesktop.Get(() => Entry(picker, tag));
    }

    private static async Task<PixelPoint> HitPoint(Control control)
    {
        PixelPoint at = default;
        await Eventually(() => TrayDesktop.Get(() =>
        {
            at = TestMouse.Centre(control);
            return TopLevel.GetTopLevel(control) is { } root
                && root.InputHitTest(root.PointToClient(at)) is Visual hit
                && hit.GetSelfAndVisualAncestors().Contains(control);
        }));
        return at;
    }
}
