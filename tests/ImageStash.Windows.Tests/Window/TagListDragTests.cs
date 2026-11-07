using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.VisualTree;
using ImageStash.Windows.Tests.Support;
using static ImageStash.Windows.Tests.Support.ShownTagLists;
using static ImageStash.Windows.Tests.Support.ShownWindow;

namespace ImageStash.Windows.Tests.Window;

/// <summary>
/// The scroll bar of the item menu's Add tag ▸ and Remove tag ▸ lists under the real pointer (G17: "A list longer than
/// fits scrolls", "a click on a listed tag adds it"): a drag begun on the bar's thumb keeps scrolling the list for as
/// long as the button is held, wherever the pointer wanders over the entries, and the let-go over an entry gives or
/// takes off no tag, the menu and the list still open; a press on the bar's strip pages the list and clicks nothing.
/// These checks right-click a tile of the stash window at the primary display's left edge and press, move and release
/// the real mouse on the list beside it.
/// </summary>
[TestClass]
public sealed class TagListDragTests
{
    [TestMethod]
    public async Task AddTagsThumbDraggedOffTheBarOverTheEntriesKeepsScrollingAndTheLetGoGivesNoTag()
    {
        using var shown = Open();
        var items = await shown.OpenStash(shown.Picture("drag one.png"), shown.Picture("drag other.png"));
        await shown.Tag([items[1]], string.Join(' ', Sixty));
        using var pointer = TestMouse.PutBackAfterwards();
        var list = await OpenList(shown, items[0].Id, "Add tag", entries: 61);
        var noticesBefore = TrayDesktop.Get(() => shown.Notices.Shown.Count);

        var released = await DragTheThumbOverTheEntries(shown, list);

        Assert.IsEmpty(shown.TagsOf(items[0].Id), $"the let-go over {released} ended the bar's drag and gave no tag (G17)");
        Assert.HasCount(60, shown.TagsOf(items[1].Id), "the other item keeps its sixty");
        TrayDesktop.Run(() =>
        {
            Assert.HasCount(noticesBefore, shown.Notices.Shown, "no notice: nothing was changed or refused");
            shown.CloseMenu();
        });
    }

    [TestMethod]
    public async Task RemoveTagsThumbDraggedOffTheBarOverTheEntriesKeepsScrollingAndTheLetGoTakesNoTagOff()
    {
        using var shown = Open();
        var item = (await shown.OpenStash(shown.Picture("drag carried.png"))).Single();
        await shown.Tag([item], string.Join(' ', Sixty));
        using var pointer = TestMouse.PutBackAfterwards();
        var list = await OpenList(shown, item.Id, "Remove tag", entries: 60);

        var released = await DragTheThumbOverTheEntries(shown, list);

        Assert.HasCount(60, shown.TagsOf(item.Id), $"the let-go over {released} ended the bar's drag and took no tag off (G17)");
        TrayDesktop.Run(shown.CloseMenu);
    }

    [TestMethod]
    public async Task APressOnTheBarsStripBelowTheThumbPagesTheListAndClicksNoTag()
    {
        using var shown = Open();
        var items = await shown.OpenStash(shown.Picture("strip one.png"), shown.Picture("strip other.png"));
        await shown.Tag([items[1]], string.Join(' ', Sixty));
        using var pointer = TestMouse.PutBackAfterwards();
        var list = await OpenList(shown, items[0].Id, "Add tag", entries: 61);
        var scroller = TrayDesktop.Get(() => Scroller(list));
        await CappedWithABar(shown, scroller);
        var (root, bar, thumb) = TrayDesktop.Get(() => Parts(scroller));
        var below = TrayDesktop.Get(() => bar.PointToScreen(new Point(bar.Bounds.Width / 2, (thumb.TranslatePoint(new Point(0, thumb.Bounds.Height), bar)!.Value.Y + bar.Bounds.Height) / 2)));
        await DrawnAt(root, below, part => part is RepeatButton { Name: "PART_PageDownButton" });

        TestMouse.Click(below);

        await Eventually(() => TrayDesktop.Get(() => scroller.Offset.Y > scroller.Viewport.Height / 2));
        await Task.Delay(300);
        Assert.IsEmpty(shown.TagsOf(items[0].Id), "the strip's press paged the list and gave no tag (G17)");
        TrayDesktop.Run(() =>
        {
            Assert.IsTrue(MenuAndListOpen(shown, list), "the menu and the list stay open");
            shown.CloseMenu();
        });
    }

    private static readonly IReadOnlyList<string> Sixty = Enumerable.Range(1, 60).Select(number => $"tag{number:00}").ToList();

    /// <summary>
    /// With the real pointer: presses the list's thumb once it is drawn under the pointer, moves down fifteen pixels, then
    /// forty to the left over the entries, then down in six steps of fifteen, the list's offset growing at each; lets go
    /// there, over an entry, once the popup has drawn it under the pointer, and waits 300 ms. Asserts the drag followed the pointer with the thumb held throughout and
    /// that the menu and the list stay open with the offset the last move left; the label of the entry let go over.
    /// </summary>
    private static async Task<string> DragTheThumbOverTheEntries(ShownWindow shown, MenuItem list)
    {
        var scroller = TrayDesktop.Get(() => Scroller(list));
        await CappedWithABar(shown, scroller);
        var (root, _, thumb) = TrayDesktop.Get(() => Parts(scroller));
        var start = TestMouse.Centre(thumb);
        await DrawnAt(root, start, part => part == thumb);
        var at = start;
        var offsets = new List<double>();
        var held = true;
        TestMouse.Press(start);
        try
        {
            at = new PixelPoint(start.X, start.Y + 15);
            TestMouse.MoveTo(at);
            await Eventually(() => TrayDesktop.Get(() => scroller.Offset.Y > 0));
            at = new PixelPoint(start.X - 40, at.Y);
            TestMouse.MoveTo(at);
            await Task.Delay(50);
            offsets.Add(TrayDesktop.Get(() => scroller.Offset.Y));
            for (var step = 1; step <= 6; step++)
            {
                at = new PixelPoint(at.X, at.Y + 15);
                TestMouse.MoveTo(at);
                var before = offsets[^1];
                await Eventually(() => TrayDesktop.Get(() => scroller.Offset.Y) > before);
                offsets.Add(TrayDesktop.Get(() => scroller.Offset.Y));
                held &= TrayDesktop.Get(() => thumb.Classes.Contains(":pressed"));
            }

            // The let-go is hit-tested against the popup's last frame: wait for the frame that shows the list where the
            // drag left it, so the release lands over the entry a person would see under the pointer.
            var shownUnder = TrayDesktop.Get(() => EntryAt(list, root, at));
            await DrawnAt(root, at, part => part == shownUnder);
        }
        finally
        {
            TestMouse.Release(at);
        }

        var left = offsets[^1];
        await Task.Delay(300);
        var under = TrayDesktop.Get(() => root.InputHitTest(root.PointToClient(at)) is Visual hit ? hit.GetSelfAndVisualAncestors().OfType<MenuItem>().FirstOrDefault() : null);
        TrayDesktop.Run(() =>
        {
            Assert.IsTrue(held, "the thumb stayed pressed while the pointer moved over the entries");
            Assert.IsNotNull(under, "the let-go was over an entry of the list");
            Assert.IsTrue(MenuAndListOpen(shown, list), "the menu and the list stay open after the let-go (G17)");
            Assert.AreEqual(left, scroller.Offset.Y, 0.5, "the list stays where the drag left it");
        });
        return TrayDesktop.Get(() => Label(under!));
    }

    /// <summary>The list's entry whose laid-out bounds hold <paramref name="at"/>, in physical pixels on the desktop.</summary>
    private static MenuItem EntryAt(MenuItem list, TopLevel root, PixelPoint at) =>
        list.Items.OfType<MenuItem>().Single(entry => root.TranslatePoint(root.PointToClient(at), entry) is { } point && new Rect(entry.Bounds.Size).Contains(point));

    /// <summary>The scroller's popup root, its vertical bar and the bar's thumb.</summary>
    private static (TopLevel Root, ScrollBar Bar, Thumb Thumb) Parts(ScrollViewer scroller)
    {
        var bar = Bar(scroller);
        return (TopLevel.GetTopLevel(scroller)!, bar, bar.GetVisualDescendants().OfType<Thumb>().Single());
    }

    /// <summary>
    /// The real pointer reaches only what has been drawn: waits until the popup's last frame shows <paramref name="part"/>
    /// at <paramref name="at"/>, in physical pixels on the desktop.
    /// </summary>
    private static Task DrawnAt(TopLevel root, PixelPoint at, Func<Visual, bool> part) =>
        Eventually(() => TrayDesktop.Get(() => root.InputHitTest(root.PointToClient(at)) is Visual hit && hit.GetSelfAndVisualAncestors().Any(part)));
}
