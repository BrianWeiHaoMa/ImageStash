using Avalonia;
using Avalonia.Input;
using Avalonia.Threading;
using ImageStash.Store;
using ImageStash.Window;
using ImageStash.Windows.Tests.Support;
using static ImageStash.Windows.Tests.Support.ShownWindow;

namespace ImageStash.Windows.Tests.Window;

public sealed partial class ThumbnailGridTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task GridPagesAndEndpointsKeepTheFocusedTileVisibleAndCopyIt(bool pinned)
    {
        using var pointer = TestMouse.PutBackAfterwards();
        TestMouse.MoveTo(TestMouse.Away);
        using var shown = await Pile();
        await TrayDesktop.Get(() => shown.Listing.ShowScope(Scope.All));
        TrayDesktop.Run(pinned ? shown.Window.SummonPinned : shown.Window.SummonFromTray);
        await Eventually(() => TrayDesktop.Get(() => shown.Window.IsActive && shown.Search.IsFocused && shown.Listing.Settled));
        var scroller = TrayDesktop.Get(() => Scroller(shown));
        var (first, last) = TrayDesktop.Get(() => (shown.Listing.Items[0], shown.Listing.Items[^1]));
        TrayDesktop.Run(() =>
        {
            Tap(shown.Search, Key.Tab);
            Assert.IsTrue(shown.Grid.IsFocused);
            Tap(shown.Grid, Key.End);
            Assert.AreEqual(last.Id, shown.Listing.Focused);
        });
        await TheFocusedTileShowsInViewWithItsRing(shown, "End");

        TrayDesktop.Run(scroller.ScrollToHome);
        await Eventually(() => TrayDesktop.Get(() => scroller.Offset.Y == 0));
        TrayDesktop.Run(() => Tap(shown.Grid, Key.End));
        await TheFocusedTileShowsInViewWithItsRing(shown, "End again after scrolling away from the same selection");

        var paged = TrayDesktop.Get(() => Page(Key.PageUp));
        await TheFocusedTileShowsInViewWithItsRing(shown, "Page Up from the end");
        TrayDesktop.Run(() => Tap(shown.Grid, Key.C, KeyModifiers.Control));
        await Eventually(() => shown.Uses() == 1);
        CollectionAssert.AreEqual(new[] { paged.FilePath }, TestClipboard.Read().Files.ToArray());

        var oldRowCount = TrayDesktop.Get(() => shown.Grid.Rows.Count);
        TrayDesktop.Run(() => Tap(shown.Grid, Key.Add, KeyModifiers.Control));
        await Eventually(() => TrayDesktop.Get(() => shown.Settings.Current.ThumbnailSize == ThumbnailSize.Large &&
            shown.Grid.Rows.Count != oldRowCount));
        TrayDesktop.Run(() => Page(Key.PageDown));
        await TheFocusedTileShowsInViewWithItsRing(shown, "Page Down after changing thumbnail size");

        TrayDesktop.Run(() =>
        {
            Tap(shown.Grid, Key.Home);
            Assert.AreEqual(first.Id, shown.Listing.Focused);
            Assert.IsTrue(shown.Grid.IsFocused);
        });
        await TheFocusedTileShowsInViewWithItsRing(shown, "Home");

        StashItem Page(Key key)
        {
            var rows = shown.Grid.Rows;
            var source = rows.Single(row => row.Tiles.Any(tile => tile.Id == shown.Listing.Focused));
            var x = source.Tiles.Single(tile => tile.Id == shown.Listing.Focused).CentreX;
            var height = scroller.Viewport.Height;
            var y = Math.Clamp(source.Y + source.Height / 2 + (key == Key.PageUp ? -height : height), 0, rows[^1].Y + rows[^1].Height);

            Tap(shown.Grid, key);

            var target = rows.Single(row => row.Tiles.Any(tile => tile.Id == shown.Listing.Focused));
            Assert.AreNotSame(source, target);
            var top = key == Key.PageDown ? target.Y - JustifiedRows.Gap : target.Y;
            var bottom = key == Key.PageUp ? target.Y + target.Height + JustifiedRows.Gap : target.Y + target.Height;
            Assert.IsTrue(y >= top && y <= bottom,
                "the new row is one viewport away, clamped at the ends");
            var tile = target.Tiles.FirstOrDefault(tile => tile.X <= x && x < tile.X + tile.Width)
                ?? target.Tiles.MinBy(tile => Math.Abs(x - tile.CentreX))!;
            Assert.AreEqual(tile.Id, shown.Listing.Focused);
            CollectionAssert.AreEqual(new[] { tile.Id }, shown.Listing.Selection.ToArray());
            Assert.IsTrue(shown.Grid.IsFocused);
            return shown.Listing.SelectedItems.Single();
        }
    }

    [TestMethod]
    public async Task HomeAndEndStillMoveTheCaretAndExtendTheSearchBoxsOwnSelection()
    {
        using var shown = ShownWindow.Open();
        await shown.OpenStash(shown.Picture("cat first.png"), shown.Picture("cat second.png"));
        TrayDesktop.Run(shown.Window.SummonFromTray);
        TrayDesktop.Run(() => shown.Search.Text = "cat");
        await Eventually(() => TrayDesktop.Get(() => shown.Search.IsFocused && shown.Listing.Settled && shown.Listing.Query == "cat"));

        TrayDesktop.Run(() =>
        {
            var first = shown.Listing.Focused;
            Tap(shown.Search, Key.End);
            Assert.AreEqual(3, shown.Search.CaretIndex);
            Tap(shown.Search, Key.Home, KeyModifiers.Shift);
            Assert.AreEqual("cat", shown.Search.SelectedText);
            Tap(shown.Search, Key.Home);
            Assert.AreEqual(0, shown.Search.CaretIndex);
            Tap(shown.Search, Key.End, KeyModifiers.Shift);
            Assert.AreEqual("cat", shown.Search.SelectedText);
            Assert.AreEqual(first, shown.Listing.Focused);
            Assert.IsTrue(shown.Search.IsFocused);
        });
    }

    [TestMethod]
    public async Task GridNavigationLeavesAFocusedButtonAnOpenMenuAndThePreviewInCharge()
    {
        using var shown = ShownWindow.Open();
        await shown.OpenStash(shown.Picture("first.png"), shown.Picture("middle.png"), shown.Picture("last.png"));
        await TrayDesktop.Get(() => shown.Listing.ShowScope(Scope.All));
        TrayDesktop.Run(shown.Window.SummonPinned);
        await Eventually(() => TrayDesktop.Get(() => shown.Search.IsFocused && shown.Listing.Settled));

        TrayDesktop.Run(() =>
        {
            var middle = shown.Listing.Items[1];
            shown.Listing.Focus(middle.Id);
            shown.AddButton.Focus();
            Tap(shown.AddButton, Key.End);
            Assert.AreEqual(middle.Id, shown.Listing.Focused);

            shown.Grid.Focus();
            var menu = shown.AddButton.Flyout!;
            menu.ShowAt(shown.AddButton);
            try
            {
                Assert.IsTrue(shown.Window.OpenedPopups.Any(popup => popup.Child is not Avalonia.Controls.ToolTip));
                Tap(shown.Grid, Key.End);
                Assert.AreEqual(middle.Id, shown.Listing.Focused);
            }
            finally
            {
                menu.Hide();
            }

            shown.OpenPreview(middle);
            Assert.IsTrue(shown.Preview.IsOpen);
            Tap(shown.PreviewPicture, Key.PageDown);
            Tap(shown.PreviewPicture, Key.Home);
            Assert.AreEqual(middle.Id, shown.Listing.Focused);
            Tap(shown.PreviewPicture, Key.Right);
            Assert.AreEqual(shown.Listing.Items[2].Id, shown.Listing.Focused, "the preview still steps on Right");
        });
    }

    [TestMethod]
    public async Task AnAddMenuOpenedAfterEndAndCopyDoesNotRetargetTheWaitingCopy()
    {
        using var shown = ShownWindow.Open();
        await shown.OpenStash(shown.Picture("cat first.png"), shown.Picture("cat second.png"), shown.Picture("dog.png"));
        var expected = (await shown.Listing.Store!.Search("cat")).Value![^1];
        TrayDesktop.Run(shown.Window.SummonPinned);
        await Eventually(() => TrayDesktop.Get(() => shown.Search.IsFocused && shown.Listing.Settled));

        var menu = TrayDesktop.Get(() => shown.AddButton.Flyout!);
        try
        {
            using (shown.HoldStore())
            {
                TrayDesktop.Run(() =>
                {
                    shown.Search.Text = "cat";
                    Assert.IsFalse(shown.Listing.Settled);
                    Tap(shown.Search, Key.Tab);
                    Assert.IsTrue(shown.Grid.IsFocused);
                    Tap(shown.Grid, Key.End);
                    Tap(shown.Grid, Key.C, KeyModifiers.Control);
                    menu.ShowAt(shown.AddButton);
                    Assert.IsTrue(shown.Window.OpenedPopups.Any(popup => popup.Child is not Avalonia.Controls.ToolTip));
                });
            }

            await Eventually(() => shown.Uses() == 1);
            Assert.AreEqual(expected.Id, TrayDesktop.Get(() => shown.Listing.Focused));
            CollectionAssert.AreEqual(new[] { expected.FilePath }, TestClipboard.Read().Files.ToArray());
        }
        finally
        {
            TrayDesktop.Run(menu.Hide);
        }
    }

    [TestMethod]
    public async Task AnEndpointReachedWhileTheFlyoutIsHiddenIsRevealedAtTheNextSummon()
    {
        using var shown = await Pile();
        await TrayDesktop.Get(() => shown.Listing.ShowScope(Scope.All));
        TrayDesktop.Run(shown.Window.SummonFromTray);
        await Eventually(() => TrayDesktop.Get(() => shown.Window.IsActive && shown.Search.IsFocused && shown.Listing.Settled));
        var scroller = TrayDesktop.Get(() => Scroller(shown));
        var last = TrayDesktop.Get(() => shown.Listing.Items[^1]);
        using (shown.HoldStore())
        {
            TrayDesktop.Run(() =>
            {
                scroller.ScrollToHome();
                _ = shown.Listing.Refresh();
                Assert.IsFalse(shown.Listing.Settled);
                Tap(shown.Search, Key.Tab);
                Tap(shown.Grid, Key.End);
                shown.Window.SummonFromShortcut(new SummonAnchor(null));
                Assert.IsFalse(shown.Window.IsVisible);
            });
        }

        await Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled && shown.Listing.Focused == last.Id));
        // Let the first reveal attempt run while the flyout is still hidden.
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        TrayDesktop.Run(shown.Window.SummonFromTray);
        await Eventually(() => TrayDesktop.Get(() =>
        {
            if (!shown.Search.IsFocused || !shown.Listing.Settled || shown.Listing.Focused != last.Id ||
                scroller.Presenter is not { } presenter || shown.Grid.Rows.Count == 0)
            {
                return false;
            }

            var tile = shown.Grid.Rows[^1].Tiles[^1];
            var top = shown.Grid.TranslatePoint(new Point(tile.X, tile.Y), presenter)!.Value.Y;
            return tile.Id == last.Id && top >= -0.5 && top + tile.Height <= presenter.Bounds.Height + 0.5;
        }));
    }
}
