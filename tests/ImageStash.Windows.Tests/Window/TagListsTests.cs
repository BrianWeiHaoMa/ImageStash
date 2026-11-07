using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using ImageStash.Window;
using ImageStash.Windows.Tests.Support;
using static ImageStash.Windows.Tests.Support.ShownTagLists;
using static ImageStash.Windows.Tests.Support.ShownWindow;

namespace ImageStash.Windows.Tests.Window;

/// <summary>
/// The item menu's tag lists in the stash window as a person's mouse reaches them (G17, F4): Add tag ▸ marking the
/// tags every selected item carries, a click giving the tag or taking it off with the mark following and the menu and
/// the list still open; Remove tag ▸ taking each clicked tag off and staying open until nothing is left, then gone
/// until a tag is given again;
/// Add tag ▸ offering Type a tag… first, in view as it opens; a list of sixty tags kept within the display's working area, no taller than the tag lists' one height rule gives,
/// with a scroll bar, scrolled by the wheel to its last entry and by Down to the entry it selects, Remove tag ▸ alike,
/// and a list of four as tall as its entries alone; the
/// menu's other entries closing it as before, and Enter in the grid handing off afterwards (G8, G7). These checks
/// right-click a tile of the stash window at the primary display's left edge and click its menus with real mouse input.
/// </summary>
[TestClass]
public sealed class TagListsTests
{
    [TestMethod]
    public async Task AddTagsClicksToggleTheTagWithItsMarkWhileTheMenuAndTheListStayOpenAndEscClosesThem()
    {
        using var shown = Open();
        var items = await shown.OpenStash(shown.Picture("toggle one.png"), shown.Picture("toggle other.png"));
        var (item, other) = (items[0], items[1]);
        await shown.Tag([other], "first second carried");
        await shown.Tag([item], "carried");
        using var pointer = TestMouse.PutBackAfterwards();

        var list = await OpenList(shown, item.Id, "Add tag", entries: 4);
        var marked = TrayDesktop.Get(() => Marked(list));
        await ClickEntry(list, "first");
        await Eventually(() => shown.TagsOf(item.Id).Contains("first") && TrayDesktop.Get(() => Entry(list, "first").IsChecked));
        TrayDesktop.Run(() => Assert.IsTrue(MenuAndListOpen(shown, list), "the menu and the list stay open for the next (G17)"));
        await ClickEntry(list, "second");
        await Eventually(() => shown.TagsOf(item.Id).Contains("second") && TrayDesktop.Get(() => Entry(list, "second").IsChecked));
        TrayDesktop.Run(() => Assert.IsTrue(MenuAndListOpen(shown, list)));
        await ClickEntry(list, "first");
        await Eventually(() => !shown.TagsOf(item.Id).Contains("first") && TrayDesktop.Get(() => !Entry(list, "first").IsChecked));
        TrayDesktop.Run(() => Assert.IsTrue(MenuAndListOpen(shown, list), "a click on a marked tag took it off, the list still open"));

        for (var presses = 0; presses < 3 && TrayDesktop.Get(() => shown.OpenMenu!.IsOpen); presses++)
        {
            TrayDesktop.Run(() => shown.SendKey(Key.Escape));
            await Task.Delay(200);
        }

        CollectionAssert.AreEqual(new[] { "carried" }, marked, "the one tag it carries marked as the list opened (G17)");
        CollectionAssert.AreEqual(new[] { "carried", "second" }, shown.TagsOf(item.Id));
        TrayDesktop.Run(() =>
        {
            Assert.IsFalse(shown.OpenMenu!.IsOpen, "Esc closes the menu");
            Assert.AreSame(shown.Window, TopLevel.GetTopLevel(shown.Grid), "the lists were popups of the stash window, which stays (F4, G2)");
            Assert.IsTrue(shown.Window.IsVisible);
        });
        Assert.AreEqual(0, shown.Uses(), "tagging is not a use (G10)");
    }

    [TestMethod]
    public async Task AClickTheStoreRefusesForAnItemDeletedMeanwhileIsNamedAndLeavesTheTagUnmarked()
    {
        using var shown = Open();
        var items = await shown.OpenStash(shown.Picture("refused tile.png"), shown.Picture("refused other.png"));
        await shown.Tag([items[1]], "kept");
        using var pointer = TestMouse.PutBackAfterwards();
        var list = await OpenList(shown, items[0].Id, "Add tag", entries: 2);
        var noticesBefore = TrayDesktop.Get(() => shown.Notices.Shown.Count);
        var deleted = await shown.Listing.Store!.Delete([items[0].Id]);

        await ClickEntry(list, "kept");
        await Eventually(() => TrayDesktop.Get(() => shown.Notices.Shown.Count == noticesBefore + 1));
        await Task.Delay(300);
        var named = TrayDesktop.Get(() => shown.Notices.Shown[^1].Text);
        var marked = TrayDesktop.Get(() => Entry(list, "kept").IsChecked);
        var open = TrayDesktop.Get(() => MenuAndListOpen(shown, list));
        TrayDesktop.Run(shown.CloseMenu);
        await shown.Listing.Store!.UndoDelete([deleted.Single().Value!]);

        Assert.AreEqual("Add a tag failed: refused tile, it is no longer in the stash", named, "the refusal named (G35)");
        Assert.IsFalse(marked, "the mark shows the store's answer, not the menu's own flip of it");
        Assert.IsTrue(open, "the list stays open");
        Assert.IsEmpty(shown.TagsOf(items[0].Id));
    }

    [TestMethod]
    public async Task SixtyTagsStayWithinTheWorkingAreaAndTheWheelScrollsTheListToItsLastTag()
    {
        using var shown = Open();
        var items = await shown.OpenStash(shown.Picture("sixty one.png"), shown.Picture("sixty other.png"));
        var sixty = Enumerable.Range(1, 60).Select(number => $"tag{number:00}").ToList();
        await shown.Tag([items[1]], string.Join(' ', sixty));
        using var pointer = TestMouse.PutBackAfterwards();

        var list = await OpenList(shown, items[0].Id, "Add tag", entries: 61);
        await Eventually(() => TrayDesktop.Get(() => Entry(list, "tag01").Bounds.Height > 0));
        var (popup, workingArea, scroller) = TrayDesktop.Get(() =>
        {
            var root = (PopupRoot)TopLevel.GetTopLevel(Entry(list, "tag01"))!;
            var at = root.PointToScreen(default);
            var size = PixelSize.FromSize(root.Bounds.Size, root.RenderScaling);
            return (new PixelRect(at, size), shown.Window.Screens.ScreenFromPoint(at)!.WorkingArea, Scroller(list));
        });
        await CappedWithABar(shown, scroller);
        TrayDesktop.Run(() =>
        {
            CollectionAssert.AreEqual(new[] { TypeATag }.Concat(sixty).ToList(), Labels(list), "Type a tag… first, then every tag, the commonest first (G17)");
            Assert.AreEqual(0, scroller.Offset.Y, "the list opens at its top");
            Assert.IsTrue(InView(scroller, Entry(list, TypeATag)), "Type a tag… in view without scrolling (G17)");
        });
        Assert.AreEqual(TrayDesktop.Get(() => ((PopupRoot)TopLevel.GetTopLevel(Entry(list, "tag01"))!).TryGetPlatformHandle()!.Handle), FrontWindow.Under(popup.Center), $"the list at {popup} is what stands there: \"{FrontWindow.NativeText(FrontWindow.Under(popup.Center))}\"");
        // The pointer reaches the list before the wheel turns, as a person's does: a wheel sent with the move in one
        // batch can land, on a loaded machine, before the list has seen the pointer arrive, and then reaches nothing.
        TestMouse.MoveTo(popup.Center);
        await Eventually(() => TrayDesktop.Get(() => scroller.IsPointerOver));
        TestMouse.WheelDown(popup.Center, 3);
        await Eventually(() => TrayDesktop.Get(() => scroller.Offset.Y > 0));
        for (var turns = 0; turns < 40 && !TrayDesktop.Get(() => InView(scroller, Entry(list, "tag60"))); turns++)
        {
            TestMouse.WheelDown(popup.Center, 3);
            await Task.Delay(100);
        }

        TrayDesktop.Run(() => Assert.IsTrue(InView(scroller, Entry(list, "tag60")), "the wheel brought the last tag into the list's view"));
        await ClickEntry(list, "tag60");
        // The store holds the tag before the list reads it again, so the mark is waited for, never read at once.
        await Eventually(() => shown.TagsOf(items[0].Id).SequenceEqual(["tag60"]) && TrayDesktop.Get(() => Entry(list, "tag60").IsChecked));

        TrayDesktop.Run(scroller.ScrollToEnd);
        await Eventually(() => TrayDesktop.Get(() => InView(scroller, Entry(list, "tag60"))));

        Assert.IsTrue(workingArea.Contains(popup), $"the list {popup} stays within the working area {workingArea} (G17)");
        TrayDesktop.Run(() =>
        {
            Assert.IsTrue(MenuAndListOpen(shown, list), "the list stays open after the click");
            Assert.IsTrue(Entry(list, "tag60").IsChecked, "and marks the tag now carried");
            shown.CloseMenu();
        });
    }

    [TestMethod]
    public async Task RemoveTagOverSixtyCarriedTagsStandsAtTheSameCapWithABarAndAClickTakesATagOffWithTheListOpen()
    {
        using var shown = Open();
        var item = (await shown.OpenStash(shown.Picture("sixty carried.png"))).Single();
        await shown.Tag([item], string.Join(' ', Enumerable.Range(1, 60).Select(number => $"tag{number:00}")));
        using var pointer = TestMouse.PutBackAfterwards();

        var list = await OpenList(shown, item.Id, "Remove tag", entries: 60);
        var scroller = TrayDesktop.Get(() => Scroller(list));
        await CappedWithABar(shown, scroller);
        TrayDesktop.Run(() => Entry(list, "tag60").BringIntoView());
        await Eventually(() => TrayDesktop.Get(() => InView(scroller, Entry(list, "tag60"))));
        await Task.Delay(200);
        await ClickEntry(list, "tag60");

        await Eventually(() => !shown.TagsOf(item.Id).Contains("tag60") && TrayDesktop.Get(() => !Labels(list).Contains("tag60")));
        TrayDesktop.Run(() =>
        {
            Assert.HasCount(59, shown.TagsOf(item.Id), "the clicked tag alone taken off (G17)");
            Assert.IsTrue(MenuAndListOpen(shown, list), "the list stays open for the next (G17)");
            shown.CloseMenu();
        });
    }

    [TestMethod]
    public async Task AddTagOverFourTagsStandsAsTallAsItsEntriesWithNoScrollBar()
    {
        using var shown = Open();
        var items = await shown.OpenStash(shown.Picture("four one.png"), shown.Picture("four other.png"));
        await shown.Tag([items[1]], "one two three four");
        using var pointer = TestMouse.PutBackAfterwards();

        var list = await OpenList(shown, items[0].Id, "Add tag", entries: 5);
        var scroller = TrayDesktop.Get(() => Scroller(list));
        await Eventually(() => TrayDesktop.Get(() => scroller.Extent.Height > 0));
        TrayDesktop.Run(() =>
        {
            Assert.AreEqual(scroller.Extent.Height, scroller.Bounds.Height, 0.5, "as tall as its entries and separator, with no empty space: the cap is a maximum");
            Assert.IsTrue(InView(scroller, Entry(list, TypeATag)), "Type a tag… in view");
            Assert.IsFalse(Bar(scroller).IsEffectivelyVisible, "no scroll bar while nothing scrolls");
            shown.CloseMenu();
        });
    }

    [TestMethod]
    public async Task DownInAddTagOverSixtyTagsBringsEachSelectedEntryIntoTheCappedView()
    {
        using var shown = Open();
        var items = await shown.OpenStash(shown.Picture("keys one.png"), shown.Picture("keys other.png"));
        await shown.Tag([items[1]], string.Join(' ', Enumerable.Range(1, 60).Select(number => $"tag{number:00}")));
        using var pointer = TestMouse.PutBackAfterwards();
        var list = await OpenList(shown, items[0].Id, "Add tag", entries: 61);
        var scroller = TrayDesktop.Get(() => Scroller(list));
        await CappedWithABar(shown, scroller);
        // The pointer away from the list, so the keys alone choose the entry.
        TestMouse.MoveTo(TestMouse.Away);
        TrayDesktop.Run(() => Entry(list, TypeATag).Focus(NavigationMethod.Directional));

        for (var down = 1; down <= 30; down++)
        {
            TrayDesktop.Run(() => shown.SendKey(Key.Down));
            var selected = TrayDesktop.Get(() => list.Items.OfType<MenuItem>().Single(entry => entry.IsFocused));
            await Eventually(() => TrayDesktop.Get(() => InView(scroller, selected)));
        }

        TrayDesktop.Run(() =>
        {
            Assert.AreEqual("tag30", Label(list.Items.OfType<MenuItem>().Single(entry => entry.IsFocused)), "Down moved from Type a tag… through the entries, past the separator");
            Assert.IsGreaterThan(0, scroller.Offset.Y, "the list scrolled to keep the selected entry in view");
            shown.CloseMenu();
        });
    }

    [TestMethod]
    public async Task RemoveTagsClicksTakeEachTagOffWithTheListOpenUntilNothingIsLeftThenTheMenusOtherEntriesAndEnterActAsBefore()
    {
        using var shown = Open();
        var item = (await shown.OpenStash(shown.Picture("remove both.png"))).Single();
        await shown.Tag([item], "a b");
        using var pointer = TestMouse.PutBackAfterwards();

        var list = await OpenList(shown, item.Id, "Remove tag", entries: 2);
        var marked = TrayDesktop.Get(() => Marked(list));
        await ClickEntry(list, "a");
        await Eventually(() => shown.TagsOf(item.Id).SequenceEqual(["b"]) && TrayDesktop.Get(() => Labels(list).SequenceEqual(["b"])));
        TrayDesktop.Run(() => Assert.IsTrue(MenuAndListOpen(shown, list), "the list offers b alone and stays open (G17)"));
        await ClickEntry(list, "b");
        await Eventually(() => shown.TagsOf(item.Id).Count == 0 && TrayDesktop.Get(() => !list.IsVisible));
        var stillOpen = TrayDesktop.Get(() => shown.OpenMenu!.IsOpen && !list.IsSubMenuOpen);
        var addTag = TrayDesktop.Get(() => Entry(shown.OpenMenu!, "Add tag"));
        TestMouse.Click(TestMouse.Centre(addTag));
        await Eventually(() => TrayDesktop.Get(() => addTag.IsSubMenuOpen));
        await ClickEntry(addTag, "a");
        await Eventually(() => shown.TagsOf(item.Id).SequenceEqual(["a"]) && TrayDesktop.Get(() => list.IsVisible && Labels(list).SequenceEqual(["a"])));
        var back = TrayDesktop.Get(() => MenuAndListOpen(shown, addTag));
        TestMouse.Click(TestMouse.Centre(TrayDesktop.Get(() => Entry(shown.OpenMenu!, "Copy"))));
        await Eventually(() => TrayDesktop.Get(() => !shown.OpenMenu!.IsOpen) && shown.Uses() == 1);
        var labels = TrayDesktop.Get(() => shown.RightClick(item.Id));
        TrayDesktop.Run(shown.CloseMenu);
        TrayDesktop.Run(() =>
        {
            shown.Grid.Focus();
            Press(shown.Grid, Key.Enter);
        });
        await Eventually(() => shown.Uses() == 2);

        CollectionAssert.AreEqual(new[] { "a", "b" }, marked, "the one item carries both, so both are marked");
        Assert.IsTrue(stillOpen, "emptied, Remove tag ▸ closed and hid with its list while the menu stayed open: nothing inert (G17)");
        Assert.IsTrue(back, "a, given again from Add tag ▸, brought Remove tag ▸ back listing it, Add tag ▸ still open");
        CollectionAssert.Contains(labels, "Remove tag", "the menu's next opening offers Remove tag ▸ for a");
        CollectionAssert.AreEqual(new[] { item.FilePath }, TestClipboard.Read().Files.ToList(), "Copy closed the menu as before, and Enter in the grid still hands off (G8, G7)");
    }
}
