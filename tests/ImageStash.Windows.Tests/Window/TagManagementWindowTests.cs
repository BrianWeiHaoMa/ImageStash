using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using ImageStash.Store;
using ImageStash.Window;
using ImageStash.Windows.Tests.Support;
using static ImageStash.Windows.Tests.Support.ShownWindow;

namespace ImageStash.Windows.Tests.Window;

[TestClass]
public sealed class TagManagementWindowTests
{
    [TestMethod]
    [DataRow("chip", false)]
    [DataRow("chip picker", false)]
    [DataRow("selection picker", false)]
    [DataRow("preview picker", false)]
    [DataRow("chip", true)]
    [DataRow("chip picker", true)]
    [DataRow("selection picker", true)]
    [DataRow("preview picker", true)]
    public async Task ARealRightClickManagesTheTagWithoutPickingIt(string origin, bool pinned)
    {
        using var pointer = TestMouse.PutBackAfterwards();
        using var shown = Open();
        var items = await shown.OpenStash(shown.Picture("shown.png"), shown.Picture("outside.png"));
        var match = items.Single(item => item.Name == "shown");
        var carrier = items.Single(item => item.Name == "outside");
        var tag = origin == "chip" ? "elsewhere" : "zz-elsewhere";
        await shown.Tag([match], "focus");
        await shown.Tag([carrier], tag + (origin == "chip" ? "" :
            " " + string.Join(" ", Enumerable.Range(1, 60).Select(i => $"scroll-tag-{i:00}"))));
        TrayDesktop.Run(() =>
        {
            if (pinned)
            {
                shown.Window.SummonPinned();
            }

            RaiseClick(shown.Chip("#focus"));
        });
        await Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled && shown.Listing.Query == "focus"));

        var source = origin == "chip"
            ? TrayDesktop.Get(() => shown.Chip("#" + tag))
            : await OpenScrolledPicker(shown, match, origin, tag);
        if (origin == "chip")
        {
            TrayDesktop.Run(() => Assert.IsLessThan(1, source.Opacity, "a live chip without a carrier in the matches"));
        }

        TestMouse.RightClick(await HitPoint(source));
        await Eventually(() => TrayDesktop.Get(() => source.ContextMenu is { IsOpen: true } menu &&
            menu.Items.OfType<MenuItem>().First().Bounds.Height > 0));
        TrayDesktop.Run(() => Assert.AreEqual("focus", shown.Search.Text, "right-click did not pick a filter"));
        Assert.DoesNotContain(tag, shown.TagsOf(match.Id), "right-click did not toggle the selected item's tag");
        var remove = TrayDesktop.Get(() => ShownTagLists.Entry(source.ContextMenu!, "Remove from all 1 item"));

        TestMouse.Click(await HitPoint(remove));
        await Eventually(() => TrayDesktop.Get(() => shown.Notices.Shown.Any(notice => notice.Text == $"Removed #{tag} from 1 item")));
        Assert.DoesNotContain(tag, shown.TagsOf(carrier.Id));
        Assert.DoesNotContain(tag, shown.TagsOf(match.Id));

        if (pinned)
        {
            TrayDesktop.Run(() =>
            {
                shown.ChipRow.Picker.Close();
                shown.TagField.Field.CloseList();
                shown.PreviewTags.Field.CloseList();
                shown.Preview.Close();
                shown.Grid.Focus();
                Tap(shown.Grid, Key.Z, KeyModifiers.Control);
            });
        }
        else
        {
            Button? undo = null;
            await Eventually(() => TrayDesktop.Get(() =>
            {
                undo = shown.Window.GetVisualDescendants().OfType<Button>()
                    .SingleOrDefault(button => Equals(button.Content, "Undo") && button.IsEffectivelyVisible);
                return undo is not null;
            }));
            TestMouse.Click(await HitPoint(undo!));
        }

        await Eventually(() => TrayDesktop.Get(() => shown.Listing.Tags.Any(count => count.Tag == tag && count.Items == 1)));
        Assert.Contains(tag, shown.TagsOf(carrier.Id));
        Assert.DoesNotContain(tag, shown.TagsOf(match.Id));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task APickerReorderWhileItsMenuHasFocusReturnsFocusToTheSameTag(bool inPreview)
    {
        using var pointer = TestMouse.PutBackAfterwards();
        using var shown = Open();
        var items = await shown.OpenStash(shown.Picture("first.png"), shown.Picture("second.png"), shown.Picture("third.png"));
        var first = items.Single(item => item.Name == "first");
        var second = items.Single(item => item.Name == "second");
        var third = items.Single(item => item.Name == "third");
        await shown.Tag([first], "alpha");
        await shown.Tag([second], "beta");
        var (box, field) = TrayDesktop.Get(() =>
        {
            shown.Listing.Select([first.Id], first.Id);
            if (inPreview)
            {
                shown.OpenPreview(first);
                Tap(shown.PreviewPicture, Key.T, KeyModifiers.Control);
                return (shown.PreviewTagBox, shown.PreviewTags.Field);
            }

            shown.Grid.Focus();
            Tap(shown.Grid, Key.T, KeyModifiers.Control);
            return (shown.TagBox, shown.TagField.Field);
        });
        await Eventually(() => TrayDesktop.Get(() => box.IsFocused && EveryTagButton(field).IsEffectivelyVisible));
        TrayDesktop.Run(() => Tap(box, Key.Down));
        var entry = TrayDesktop.Get(() => Entry(field.Picker, "alpha"));
        TrayDesktop.Run(() => Assert.AreSame(entry, shown.Focused));
        TestMouse.RightClick(await HitPoint(entry));
        await Eventually(() => TrayDesktop.Get(() => entry.ContextMenu is { IsOpen: true }));

        await shown.Tag([third], "beta");

        await Eventually(() => TrayDesktop.Get(() => field.Picker.Entries.FirstOrDefault()?.Tag == "beta"));
        TrayDesktop.Run(() =>
        {
            Assert.IsTrue(field.Picker.IsOpen);
            Assert.IsFalse(entry.ContextMenu!.IsOpen);
            Assert.AreSame(Entry(field.Picker, "alpha"), shown.Focused);
            shown.SendKey(Key.Enter);
        });
        await Eventually(() => !shown.TagsOf(first.Id).Contains("alpha"));
        Assert.AreEqual(0L, shown.Uses());
    }

    private static async Task<Button> OpenScrolledPicker(ShownWindow shown, StashItem match, string origin, string tag)
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

        var scroll = TrayDesktop.Get(() => (ScrollViewer)((Border)picker.Child!).Child!);
        await Eventually(() => TrayDesktop.Get(() => picker.IsOpen && scroll.Extent.Height > scroll.Viewport.Height));
        TrayDesktop.Run(scroll.ScrollToEnd);
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
