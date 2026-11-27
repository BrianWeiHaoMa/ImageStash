using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ImageStash.Store;
using ImageStash.Window;
using ImageStash.Windows.Tests.Support;
using static ImageStash.Windows.Tests.Support.ShownWindow;

namespace ImageStash.Windows.Tests.Window;

/// <summary>
/// The grid's last row and the window's notices on the real scroller (G16, G7): scrolled to the end with a problem notice
/// staying, the last row's bottom stands above the notices; closed, the extent returns and the grid is not left scrolled
/// past its end. Del on a tile deep in the pile, whose notice then shows, keeps the scroller where it was and selects the
/// item now standing in the deleted one's place (G18, G4).
/// </summary>
public sealed partial class ThumbnailGridTests
{
    [TestMethod]
    public async Task ScrolledToTheEndTheLastRowStandsClearOfAStayingNoticeAndItsCloseGivesTheExtentBack()
    {
        using var shown = await Pile();
        await TrayDesktop.Get(() => shown.Listing.ShowScope(Scope.All));
        TrayDesktop.Run(shown.Window.SummonPinned);
        await Eventually(() => TrayDesktop.Get(() => shown.Window.IsVisible && shown.Listing.Settled));
        var scroller = TrayDesktop.Get(() => Scroller(shown));
        await Dispatcher.UIThread.InvokeAsync(shown.Window.UpdateLayout, DispatcherPriority.Background);
        var extentWithout = TrayDesktop.Get(() => scroller.Extent.Height);

        var problem = TrayDesktop.Get(() => shown.Notices.Raise(new Failure("Add to the stash", "dog.png", "it is unreadable")));
        await Eventually(() => TrayDesktop.Get(() => scroller.Extent.Height > extentWithout));
        TrayDesktop.Run(scroller.ScrollToEnd);
        await Dispatcher.UIThread.InvokeAsync(shown.Window.UpdateLayout, DispatcherPriority.Background);
        TrayDesktop.Run(() =>
        {
            var root = (Control)shown.Window.Content!;
            var area = NoticeMotion.StashStack(shown.Window).View.FindAncestorOfType<ScrollViewer>()!;
            var last = shown.Grid.Rows[^1];
            var lastBottom = shown.Grid.TranslatePoint(new Point(0, last.Y + last.Height), root)!.Value.Y;
            var noticesTop = area.TranslatePoint(default, root)!.Value.Y;
            Assert.IsLessThanOrEqualTo(noticesTop, lastBottom, "scrolled to the end, the last row's bottom stands above the notices");
        });

        TrayDesktop.Run(() =>
        {
            shown.Notices.Close(problem);
            NoticeMotion.StashStack(shown.Window).Finish();
        });
        await Eventually(() => TrayDesktop.Get(() => Math.Abs(scroller.Extent.Height - extentWithout) < 0.5));
        TrayDesktop.Run(() => Assert.IsLessThanOrEqualTo(scroller.Extent.Height - scroller.Viewport.Height + 0.5, scroller.Offset.Y,
            "the extent back, the grid is not left scrolled past its end"));
    }

    [TestMethod]
    public async Task DelOnATileDeepInThePileKeepsTheScrollerWhereItWasAndSelectsTheItemInItsPlace()
    {
        using var shown = await Pile();
        await TrayDesktop.Get(() => shown.Listing.ShowScope(Scope.All));
        TrayDesktop.Run(shown.Window.SummonPinned);
        await Eventually(() => TrayDesktop.Get(() => shown.Window.IsVisible && shown.Listing.Settled));
        var scroller = TrayDesktop.Get(() => Scroller(shown));
        var place = TrayDesktop.Get(() => shown.Listing.Items.Count / 2);
        var deep = TrayDesktop.Get(() => shown.Listing.Items[place]);
        var next = TrayDesktop.Get(() => shown.Listing.Items[place + 1]);
        TrayDesktop.Run(() =>
        {
            shown.Listing.Focus(deep.Id);
            shown.Grid.Focus(NavigationMethod.Tab);
        });
        await Eventually(() => TrayDesktop.Get(() => scroller.Offset.Y > scroller.Viewport.Height));
        // The deep tile's row near the viewport's top, so the item that takes its place stands clear of the Delete's notice.
        TrayDesktop.Run(() =>
        {
            var tile = shown.Grid.Rows.SelectMany(row => row.Tiles).Single(tile => tile.Id == deep.Id);
            scroller.Offset = new Vector(0, tile.Y);
        });
        await Dispatcher.UIThread.InvokeAsync(shown.Window.UpdateLayout, DispatcherPriority.Background);
        var before = TrayDesktop.Get(() => scroller.Offset);

        TrayDesktop.Run(() => Press(shown.Grid, Key.Delete));
        await Eventually(() => TrayDesktop.Get(() => shown.Listing.Items.All(item => item.Id != deep.Id) && shown.Listing.Selection.Contains(next.Id)));
        await Dispatcher.UIThread.InvokeAsync(shown.Window.UpdateLayout, DispatcherPriority.Background);
        TrayDesktop.Run(() =>
        {
            Assert.AreEqual(before.Y, scroller.Offset.Y, 0.5, "the grid stays where it was through the Delete and its notice (G4)");
            CollectionAssert.AreEqual(new[] { next.Id }, shown.Listing.Selection.ToList(), "the item now in the deleted one's place is selected");
            Assert.IsNotEmpty(shown.Notices.Shown, "the Delete's notice shows over the grid meanwhile");
        });
    }
}
