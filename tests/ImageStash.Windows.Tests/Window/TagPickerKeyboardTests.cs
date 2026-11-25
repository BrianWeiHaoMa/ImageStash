using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.VisualTree;
using ImageStash.Window;
using ImageStash.Windows.Tests.Support;

namespace ImageStash.Windows.Tests.Window;

/// <summary>Routed keys and actual focus in the shown picker, including updates from the store while a key is held.</summary>
[TestClass]
public sealed class TagPickerKeyboardTests
{
    [TestMethod]
    public async Task AKeyboardPickKeepsItsFocusWhenStoreCountsReorderTheList()
    {
        using var shown = ShownWindow.Open();
        var items = await shown.OpenStash(shown.Picture("pick one.png"), shown.Picture("pick two.png"),
            shown.Picture("donor one.png"), shown.Picture("donor two.png"));
        var targets = items.Where(item => item.Name.StartsWith("pick", StringComparison.Ordinal)).ToList();
        var donors = items.Where(item => item.Name.StartsWith("donor", StringComparison.Ordinal)).ToList();
        await shown.Tag(donors, "alpha");
        await shown.Tag([donors[0]], "beta");
        var ids = targets.Select(item => item.Id).ToArray();
        TrayDesktop.Run(() =>
        {
            shown.Listing.Select(ids, ids[0]);
            shown.Grid.Focus();
            ShownWindow.Tap(shown.Grid, Key.T, KeyModifiers.Control);
        });
        var field = shown.TagField.Field;
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.TagBox.IsFocused &&
            ShownWindow.EveryTagButton(field).IsEffectivelyVisible));

        TrayDesktop.Run(() =>
        {
            ShownWindow.Tap(shown.TagBox, Key.Down);
            Assert.IsTrue(field.Picker.IsOpen, "Down in the empty field opens the list");
            var alpha = FocusedEntry(shown, "alpha");
            ShownWindow.Press(alpha, Key.Space);
            ShownWindow.Tap(alpha, Key.Down);
            var beta = FocusedEntry(shown, "beta");
            ShownWindow.Release(beta, Key.Space);
            Assert.IsEmpty(shown.TagsOf(targets[0].Id), "the release of Space on a different row picks nothing");
            ShownWindow.Press(beta, Key.Enter);
            ShownWindow.Press(beta, Key.Enter);
        });
        await ShownWindow.Eventually(() => TrayDesktop.Get(() =>
            field.Picker.Entries.FirstOrDefault() is { Tag: "beta", Count: 3, Marked: true }));

        TrayDesktop.Run(() =>
        {
            Assert.IsTrue(shown.TagField.IsVisible && field.Picker.IsOpen);
            var beta = FocusedEntry(shown, "beta");
            ShownWindow.Press(beta, Key.Enter);
            ShownWindow.Release(beta, Key.Enter);
            Assert.IsTrue(field.Picker.Entries[0].Marked);
        });
        foreach (var item in targets)
        {
            CollectionAssert.AreEqual(new[] { "beta" }, shown.TagsOf(item.Id));
        }

        TrayDesktop.Run(() => ShownWindow.Tap(FocusedEntry(shown, "beta"), Key.Enter));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() =>
            field.Picker.Entries.LastOrDefault() is { Tag: "beta", Count: 1, Marked: false }));
        foreach (var item in targets)
        {
            Assert.IsEmpty(shown.TagsOf(item.Id));
        }

        TrayDesktop.Run(() =>
        {
            ShownWindow.Tap(FocusedEntry(shown, "beta"), Key.Escape);
            Assert.IsFalse(field.Picker.IsOpen);
            Assert.IsTrue(shown.TagField.IsVisible && shown.TagBox.IsFocused, "Esc leaves the field open at the box");
            CollectionAssert.AreEquivalent(ids, shown.Listing.SelectedItems.Select(item => item.Id).ToArray());
        });
    }

    [TestMethod]
    public async Task ThePickerStaysUsableWhenTheFocusedTagCeasesToExist()
    {
        using var shown = ShownWindow.Open();
        var items = await shown.OpenStash(shown.Picture("picked.png"), shown.Picture("keeper.png"));
        var picked = items.Single(item => item.Name == "picked");
        var keeper = items.Single(item => item.Name == "keeper");
        await shown.Tag([picked], "alpha");
        await shown.Tag([keeper], "beta");
        TrayDesktop.Run(() =>
        {
            shown.OpenPreview(picked);
            ShownWindow.Tap(shown.PreviewPicture, Key.T, KeyModifiers.Control);
        });
        var field = shown.PreviewTags.Field;
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.PreviewTagBox.IsFocused &&
            ShownWindow.EveryTagButton(field).IsEffectivelyVisible));

        TrayDesktop.Run(() =>
        {
            ShownWindow.Tap(shown.PreviewTagBox, Key.Down);
            ShownWindow.Tap(FocusedEntry(shown, "alpha"), Key.Enter);
        });
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => field.Picker.Entries.SequenceEqual([new("beta", 1, false)])));

        TrayDesktop.Run(() =>
        {
            Assert.IsTrue(shown.Preview.IsOpen && field.Picker.IsOpen);
            ShownWindow.Tap(FocusedEntry(shown, "beta"), Key.Enter);
        });
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => field.Picker.Entries.SequenceEqual([new("beta", 2, true)])));
        CollectionAssert.AreEqual(new[] { "beta" }, shown.TagsOf(picked.Id));
        CollectionAssert.AreEqual(new[] { "beta" }, shown.TagsOf(keeper.Id));
        Assert.AreEqual(0L, shown.Uses());
    }

    [TestMethod]
    public async Task ScrollingBeyondTheFocusedEntryKeepsTheKeyboardInThePicker()
    {
        using var shown = ShownWindow.Open();
        var items = await shown.OpenStash(shown.Picture("scroll target.png"), shown.Picture("scroll donor.png"));
        var target = items.Single(item => item.Name == "scroll target");
        var donor = items.Single(item => item.Name == "scroll donor");
        await shown.Tag([donor], string.Join(' ', Enumerable.Range(1, 30).Select(i => $"tag{i:00}")));
        TrayDesktop.Run(() =>
        {
            shown.Listing.Select([target.Id], target.Id);
            shown.Grid.Focus();
            ShownWindow.Tap(shown.Grid, Key.T, KeyModifiers.Control);
        });
        var field = shown.TagField.Field;
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.TagBox.IsFocused));
        // Press Down without waiting for the field's own catalogue read.
        TrayDesktop.Run(() => ShownWindow.Tap(shown.TagBox, Key.Down));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => field.Picker.IsOpen && shown.Focused is Button));
        var scroller = TrayDesktop.Get(() =>
        {
            FocusedEntry(shown, "tag01");
            return field.Picker.Child!.GetVisualDescendants().OfType<ScrollViewer>().First();
        });
        TrayDesktop.Run(scroller.ScrollToEnd);
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => scroller.Offset.Y > scroller.Viewport.Height));

        TrayDesktop.Run(() => ShownWindow.Tap(FocusedEntry(shown, "tag01"), Key.Down));
        TrayDesktop.Run(() => ShownWindow.Tap(FocusedEntry(shown, "tag02"), Key.Enter));
        await ShownWindow.Eventually(() => shown.TagsOf(target.Id).SequenceEqual(["tag02"]));
        Assert.AreEqual(0L, shown.Uses());
    }

    [TestMethod]
    public async Task EarlyDownOpensOnTheFirstTagStillInTheStash()
    {
        using var shown = ShownWindow.Open();
        var items = await shown.OpenStash(shown.Picture("current tags.png"), shown.Picture("old tags.png"),
            shown.Picture("remaining tags.png"));
        var target = items.Single(item => item.Name == "current tags");
        var old = items.Single(item => item.Name == "old tags");
        var remaining = items.Single(item => item.Name == "remaining tags");
        await shown.Tag([old], "alpha");
        await shown.Tag([remaining], "beta");
        await shown.Listing.Store!.RemoveTag([old], "alpha");
        TrayDesktop.Run(() =>
        {
            Assert.AreEqual("alpha", shown.Listing.Tags[0].Tag, "the previous listing still includes the tag that ceased to exist");
            shown.OpenPreview(target);
            ShownWindow.Tap(shown.PreviewPicture, Key.T, KeyModifiers.Control);
            ShownWindow.Tap(shown.PreviewTagBox, Key.Down);
        });
        var field = shown.PreviewTags.Field;
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => field.Picker.IsOpen && shown.Focused is Button));
        TrayDesktop.Run(() =>
        {
            Assert.AreEqual("beta", field.Picker.Entries.Single().Tag);
            ShownWindow.Tap(FocusedEntry(shown, "beta"), Key.Enter);
        });
        await ShownWindow.Eventually(() => shown.TagsOf(target.Id).SequenceEqual(["beta"]));
    }

    private static Button FocusedEntry(ShownWindow shown, string tag)
    {
        Assert.IsInstanceOfType<Button>(shown.Focused, $"the picker row for {tag} has actual focus");
        var button = (Button)shown.Focused!;
        Assert.AreEqual(tag, AutomationProperties.GetName(button));
        Assert.IsTrue(AdornerLayer.GetAdornerLayer(button)?.Children.Any(child => AdornerLayer.GetAdornedElement(child) == button),
            $"keyboard focus shows on {tag}");
        return button;
    }
}
