using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using ImageStash.Window;
using static ImageStash.Windows.Tests.Support.ShownWindow;

namespace ImageStash.Windows.Tests.Support;

/// <summary>
/// The item menu's Add tag ▸ and Remove tag ▸ lists of a shown stash window, for the desktop checks: opened and clicked
/// with the real mouse, their entries found by the words they show, their scroller and its bar found in the submenu's
/// popup. The members other than <see cref="OpenList"/> and <see cref="ClickEntry"/> run on the UI thread.
/// </summary>
public static class ShownTagLists
{
    public const string TypeATag = "Type a tag…";

    /// <summary>
    /// Selects the item, right-clicks its tile and clicks the menu's entry with <paramref name="label"/>, all with the real
    /// mouse, and waits until its list is open with <paramref name="entries"/> entries laid out; the list's entry.
    /// </summary>
    public static async Task<MenuItem> OpenList(ShownWindow shown, long id, string label, int entries)
    {
        // A click reaches only what has been drawn: the grid's first frame first.
        await Eventually(() => TrayDesktop.Get(() => shown.Window.InputHitTest(shown.Window.PointToClient(shown.TileOnScreen(id))) is ThumbnailGrid));
        TrayDesktop.Run(() =>
        {
            shown.Grid.Focus();
            shown.Listing.Select([id], id);
        });
        TestMouse.RightClick(TrayDesktop.Get(() => shown.TileOnScreen(id)));
        await Eventually(() => TrayDesktop.Get(() => shown.OpenMenu is { IsOpen: true } menu && Entry(menu, label).Bounds.Height > 0));
        var list = TrayDesktop.Get(() => Entry(shown.OpenMenu!, label));
        TestMouse.Click(TestMouse.Centre(list));
        await Eventually(() => TrayDesktop.Get(() => list.IsSubMenuOpen && Labels(list).Count == entries && list.Items.OfType<MenuItem>().First().Bounds.Height > 0));
        return list;
    }

    /// <summary>A real click on the list's entry with <paramref name="label"/>, once it is laid out.</summary>
    public static async Task ClickEntry(MenuItem list, string label)
    {
        await Eventually(() => TrayDesktop.Get(() => Entry(list, label).Bounds.Height > 0));
        TestMouse.Click(TestMouse.Centre(TrayDesktop.Get(() => Entry(list, label))));
    }

    public static bool MenuAndListOpen(ShownWindow shown, MenuItem list) => shown.OpenMenu!.IsOpen && list.IsSubMenuOpen;

    /// <summary>The list's own scroller, in its submenu.</summary>
    public static ScrollViewer Scroller(MenuItem list) => list.GetVisualDescendants().OfType<Popup>().First().Child!.GetVisualDescendants().OfType<ScrollViewer>().First();

    /// <summary>The scroller's own vertical bar.</summary>
    public static ScrollBar Bar(ScrollViewer scroller) =>
        scroller.GetVisualDescendants().OfType<ScrollBar>().Single(bar => bar.Orientation == Avalonia.Layout.Orientation.Vertical && bar.TemplatedParent == scroller);

    /// <summary>
    /// The list's scroller stands no taller than the tag lists' one height rule gives on the stash window's display,
    /// short of its entries, with a vertical scroll bar showing a thumb (G17).
    /// </summary>
    public static async Task CappedWithABar(ShownWindow shown, ScrollViewer scroller)
    {
        await Eventually(() => TrayDesktop.Get(() => scroller.Extent.Height > scroller.Viewport.Height));
        TrayDesktop.Run(() =>
        {
            var screen = shown.Window.Screens.ScreenFromVisual(shown.Window)!;
            var cap = TagListHeight.For(screen.WorkingArea.Height / screen.Scaling);
            Assert.IsLessThanOrEqualTo(cap + 0.5, scroller.Bounds.Height, $"the list no taller than the tag lists' height rule's {cap} (G17)");
            Assert.IsGreaterThan(cap - 40, scroller.Bounds.Height, "the list as tall as the rule lets it stand");
            var bar = Bar(scroller);
            Assert.IsTrue(bar.IsEffectivelyVisible, "a scroll bar shows on the list");
            var thumb = bar.GetVisualDescendants().OfType<Thumb>().Single();
            Assert.IsTrue(thumb.Bounds.Width > 0 && thumb.Bounds.Height > 0 && thumb.IsEffectivelyVisible && thumb.Opacity > 0, $"the bar's thumb laid out and shown, not {thumb.Bounds}");
            Assert.IsTrue(thumb.GetVisualDescendants().OfType<Border>().First().Background is ISolidColorBrush { Color.A: > 0, Opacity: > 0 }, "the bar's thumb painted");
        });
    }

    public static MenuItem Entry(ItemsControl menu, string label) => menu.Items.OfType<MenuItem>().Single(item => Label(item) == label);

    public static List<string> Labels(MenuItem list) => list.Items.OfType<MenuItem>().Select(Label).ToList();

    public static List<string> Marked(MenuItem list) => list.Items.OfType<MenuItem>().Where(item => item.IsChecked).Select(Label).ToList();

    public static string Label(MenuItem item) => ((string)item.Header!).Replace("__", "_");

    /// <summary>Whether the entry lies whole inside the list's scrolled view.</summary>
    public static bool InView(ScrollViewer scroller, Control entry) =>
        entry.TranslatePoint(default, scroller) is { } top && top.Y >= 0 && top.Y + entry.Bounds.Height <= scroller.Viewport.Height + 0.5;
}
