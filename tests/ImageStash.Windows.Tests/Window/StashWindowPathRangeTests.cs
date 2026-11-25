using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ImageStash.Seams;
using ImageStash.Store;
using ImageStash.Window;
using ImageStash.Windows.Tests.Support;
using static ImageStash.Windows.Tests.Support.ShownWindow;

namespace ImageStash.Windows.Tests.Window;

[TestClass]
public sealed class StashWindowPathRangeTests
{
    private const string Query = "cat";

    [TestMethod]
    public async Task DeleteAlreadyQueuedAfterRenameDoesNotRecycleARangeItemUnderTheNameField()
    {
        using var shown = Open();
        await shown.OpenStash(shown.Picture("cat older.png"), shown.Picture("cat newer.png"), shown.Picture("dog.png"));
        var store = shown.Listing.Store!;
        var matches = (await store.Search(Query)).Value!;
        using (shown.HoldStore())
        {
            TrayDesktop.Run(() =>
            {
                shown.Search.Text = Query;
                Assert.IsFalse(shown.Listing.Settled);
                Assert.IsTrue(shown.Grid.Focus());
                Tap(shown.Grid, Key.Right, KeyModifiers.Shift);
                Tap(shown.Grid, Key.F2);
                Tap(shown.Grid, Key.Delete);
            });
        }

        await TrayDesktop.Get(() => shown.Listing.Settle());
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        var remaining = (await store.Search(Query)).Value!;
        CollectionAssert.AreEqual(Ids(matches), Ids(remaining), "Del cannot recycle the item F2 opened for editing");
        TrayDesktop.Run(() =>
        {
            Assert.AreEqual(matches[1].Id, shown.Preview.Item?.Id, "F2 opens the picture at the range's moving end");
            Assert.IsTrue(shown.Preview.EditingName);
        });
    }

    [TestMethod]
    public async Task ShiftDownWithEveryTagOpenExtendsTheRangeWithoutSelectingTextInTheFocusedBox()
    {
        using var shown = Open();
        var pictures = Enumerable.Range(1, 12).Select(i => shown.Picture($"cat {i:00}.png")).ToArray();
        var items = await shown.OpenStash(pictures);
        await shown.Tag([items[0]], string.Join(' ', Enumerable.Range(1, 12).Select(i => $"long-filter-tag-{i:00}")));
        await TrayDesktop.Get(() =>
        {
            shown.Search.Text = Query;
            return shown.Listing.Settle();
        });

        TrayDesktop.Run(() =>
        {
            Assert.IsTrue(shown.Search.Focus());
            shown.Search.CaretIndex = 1;
            shown.Search.SelectionStart = shown.Search.SelectionEnd = 1;
            shown.Window.UpdateLayout();
            var rows = shown.Grid.Rows;
            var end = InColumn(rows[1], rows[0].Tiles[0]);
            var state = TextState(shown.Search);
            RaiseClick(shown.Chip("Every tag"));
            Assert.IsTrue(shown.ChipRow.Picker.IsOpen && shown.Search.IsFocused);
            Tap(shown.Search, Key.Down, KeyModifiers.Shift);

            Assert.AreEqual(state, TextState(shown.Search));
            AssertSelection(shown, shown.Search, end.Id,
                shown.Listing.Items.Take(end.Index + 1).Select(item => item.Id).ToArray(), "with Every tag open");
        });
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task SearchArrowsExtendThenContractTheRangeAndCtrlCCopiesFilesOrPersonallySelectedText(bool pinned)
    {
        using var shown = Open();
        var all = await SearchStash(shown, pinned, 24);
        var matches = all.Where(item => item.Name.StartsWith(Query + " ", StringComparison.Ordinal)).ToArray();
        Summon(shown, pinned);
        var search = TrayDesktop.Get(() => shown.Search);
        await Eventually(() => TrayDesktop.Get(() => shown.Window.IsActive && search.IsFocused && shown.Listing.Settled));

        var (summoned, ends, nextEnd) = TrayDesktop.Get(() =>
        {
            Assert.AreEqual(Query, search.SelectedText, "the summon selects the whole query (G1)");
            Assert.AreEqual(Scope.All, shown.Listing.LitChip);
            AssertSelection(shown, search, matches[0].Id, [matches[0].Id], "at the summon");
            shown.Window.UpdateLayout();
            var rows = shown.Grid.Rows;
            CollectionAssert.AreEqual(matches.Select(item => item.Id).ToArray(), rows.SelectMany(row => row.Tiles).Select(tile => tile.Id).ToArray());
            var second = InColumn(rows[1], rows[0].Tiles[0]);
            var third = InColumn(rows[2], second);
            var back = InColumn(rows[1], third);
            return (TextState(search), new[] { second.Id, third.Id, back.Id }, InColumn(rows[2], back).Id);
        });

        var keys = new[] { Key.Down, Key.Down, Key.Up };
        for (var i = 0; i < keys.Length; i++)
        {
            var end = ends[i];
            await Move(shown, search, keys[i], end, Ids(RangeTo(matches, end)), KeyModifiers.Shift);
            TrayDesktop.Run(() => Assert.AreEqual(summoned, TextState(search), $"Shift+{keys[i]} preserves the query, its selection and caret (G7)"));
        }

        var copied = RangeTo(matches, ends[^1]);
        await CopyFiles(shown, search, copied);
        var selected = Ids(copied);
        TrayDesktop.Run(() =>
        {
            AssertSelection(shown, search, ends[^1], selected, "after copying the range");
            Assert.AreEqual(summoned, TextState(search));
        });

        KeyFrom(shown, search, Key.Home);
        await Eventually(() => TrayDesktop.Get(() => search.CaretIndex == 0 && search.SelectedText == ""));
        KeyFrom(shown, search, Key.Right, KeyModifiers.Shift);
        await Eventually(() => TrayDesktop.Get(() => search.SelectedText == "c"));
        var personal = TrayDesktop.Get(() => TextState(search));
        var nextSelection = Ids(RangeTo(matches, nextEnd));
        await Move(shown, search, Key.Down, nextEnd, nextSelection, KeyModifiers.Shift);
        TrayDesktop.Run(() => Assert.AreEqual(personal, TextState(search), "vertical range input preserves a personal text selection too"));
        KeyFrom(shown, search, Key.C, KeyModifiers.Control);
        await Eventually(() => TestClipboard.Text() == "c");
        Assert.IsEmpty(TestClipboard.Read().Files, "personal text selection copies text alone (G7)");
        Assert.AreEqual((long)copied.Length, shown.Uses(), "text copy records no item use (G10)");
        TrayDesktop.Run(() =>
        {
            AssertSelection(shown, search, nextEnd, nextSelection, "after copying personal text");
            Assert.AreEqual(personal, TextState(search));
        });
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task GridRangeKeepsItsMovingEndThroughPinnedReflowAndTagsTheExactSelectionBeforeSettings(bool pinned)
    {
        const string tag = "range";
        using var shown = Open();
        var all = await SearchStash(shown, pinned, 96);
        var (search, grid) = TrayDesktop.Get(() => (shown.Search, shown.Grid));

        KeyFrom(shown, search, Key.Escape);
        await Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled && shown.Listing.Query == "" && TextState(search) == ("", 0, 0, 0)));
        TrayDesktop.Run(() =>
        {
            Assert.AreEqual(Scope.All, shown.Listing.LitChip);
            CollectionAssert.AreEqual(Ids(all), Ids(shown.Listing.Items), "clearing restores All in its listing order (G5)");
            AssertSelection(shown, search, all[0].Id, [all[0].Id], "after clearing the query");
        });
        await Move(shown, search, Key.Right, all[1].Id, [all[1].Id]);
        TrayDesktop.Run(() => Assert.AreEqual(("", 0, 0, 0), TextState(search)));

        KeyFrom(shown, search, Key.Tab);
        await Eventually(() => TrayDesktop.Get(() => grid.IsFocused));
        TrayDesktop.Run(() => AssertSelection(shown, grid, all[1].Id, [all[1].Id], "Tab"));
        await Move(shown, grid, Key.End, all[^1].Id, [all[^1].Id]);
        var page = TrayDesktop.Get(() =>
        {
            shown.Window.UpdateLayout();
            var rows = grid.Rows;
            var from = rows[^1].Tiles[^1];
            Assert.AreEqual(all[^1].Id, from.Id, "End reaches the last tile in the laid-out grid");
            // Project the last row's centre up one viewport. A gap belongs to the row above it.
            var y = from.Y + from.Height / 2 - Viewport(shown).Bounds.Height;
            var row = rows.Take(rows.Count - 1).LastOrDefault(candidate => candidate.Y <= y) ?? rows[0];
            return InColumn(row, from).Id;
        });
        await Move(shown, grid, Key.PageUp, page, [page]);
        var index = Array.FindIndex(all, item => item.Id == page);
        Assert.IsGreaterThan(0, index, "the fixture leaves a tile to the left of the page destination");
        var movingEnd = all[index - 1].Id;
        long[] selected = [movingEnd, page];
        await Move(shown, grid, Key.Left, movingEnd, selected, KeyModifiers.Shift);
        await MovingEndShows(shown, movingEnd);

        if (pinned)
        {
            var (columns, oldBottom) = TrayDesktop.Get(() =>
            {
                var view = Viewport(shown);
                var bottom = view.TranslatePoint(default, grid)!.Value.Y + view.Bounds.Height;
                var old = grid.Rows[0].Tiles.Count;
                shown.Window.Width = WindowSize.Minimum.Width;
                return (old, bottom);
            });
            await Eventually(() => TrayDesktop.Get(() => grid.Rows[0].Tiles.Count < columns));
            TrayDesktop.Run(() =>
            {
                AssertSelection(shown, grid, movingEnd, selected, "after pinned reflow, before any more keys");
                Assert.IsGreaterThanOrEqualTo(oldBottom, Tile(shown, movingEnd).Y, "the moved tile would be below the old viewport without scrolling");
            });
            await MovingEndShows(shown, movingEnd);
            TrayDesktop.Run(() => AssertSelection(shown, grid, movingEnd, selected, "after the reflowed tile is visible"));
        }

        await CopyFiles(shown, grid, all[(index - 1)..(index + 1)]);
        TrayDesktop.Run(() => AssertSelection(shown, grid, movingEnd, selected, "after copying from the grid"));
        await TagThenOpenSettings(shown, all, selected, movingEnd, tag);
    }

    private static async Task<StashItem[]> SearchStash(ShownWindow shown, bool pinned, int count)
    {
        // Same aspect ratio and distinct pixels. Equal add times leave identity as the fixed tie-break.
        var pictures = Enumerable.Range(1, count).Select(i => shown.Picture(
            $"{(i % 4 == 0 ? "other" : Query)} {i:000}.png", 12, 8, (_, _) => AddedColour(i))).ToArray();
        var items = await shown.OpenStash(new StoppedClock(), pictures);
        await TrayDesktop.Get(() => shown.Listing.ShowScope(Scope.All));
        TransferredStash.CloseNotices(shown);
        Summon(shown, pinned);
        await Eventually(() => TrayDesktop.Get(() => shown.Window.IsActive && shown.Search.IsFocused && shown.Listing.Settled));
        TrayDesktop.Run(() => Type(shown.Search, Query));
        await Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled && shown.Listing.Query == Query));
        return items.OrderByDescending(item => item.Id).ToArray();
    }

    private static void Summon(ShownWindow shown, bool pinned) => TrayDesktop.Run(() =>
    {
        if (pinned)
        {
            shown.Window.SummonPinned();
        }
        else
        {
            shown.Window.Hide();
            shown.Window.SummonFromShortcut(default, new SummonAnchor(null));
        }

        Assert.AreEqual(pinned, shown.Window.ShowInTaskbar);
    });

    private static void KeyFrom(ShownWindow shown, Control origin, Key key, KeyModifiers modifiers = KeyModifiers.None)
    {
        TrayDesktop.Run(() =>
        {
            Assert.AreSame(origin, shown.Focused, $"focus before {modifiers}+{key}");
            Assert.IsTrue(origin.IsFocused);
            Assert.AreEqual(shown.Window.TryGetPlatformHandle()!.Handle, FrontWindow.GetForegroundWindow(), "the native key goes to the stash window");
        });
        TestHotKeys.Press(new Shortcut(new KeyGesture(key, modifiers), key.ToString()));
    }

    private static async Task Move(ShownWindow shown, Control origin, Key key, long end, long[] selection, KeyModifiers modifiers = KeyModifiers.None)
    {
        KeyFrom(shown, origin, key, modifiers);
        await Eventually(() => TrayDesktop.Get(() => shown.Listing.Focused == end));
        TrayDesktop.Run(() => AssertSelection(shown, origin, end, selection, $"{modifiers}+{key}"));
    }

    private static void AssertSelection(ShownWindow shown, Control origin, long end, long[] selected, string step)
    {
        Assert.AreSame(origin, shown.Focused, $"{step}: keyboard focus (G7)");
        Assert.IsTrue(origin.IsFocused, $"{step}: the control has focus (G7)");
        Assert.AreEqual(end, shown.Listing.Focused, $"{step}: moving end (G7)");
        CollectionAssert.AreEquivalent(selected, shown.Listing.Selection.ToArray(), $"{step}: exact selected tile identities (G7, G17)");
    }

    private static (string Text, int Start, int End, int Caret) TextState(TextBox box) =>
        (box.Text ?? "", box.SelectionStart, box.SelectionEnd, box.CaretIndex);

    private static StashItem[] RangeTo(StashItem[] ordered, long end) =>
        ordered[..(Array.FindIndex(ordered, item => item.Id == end) + 1)];

    private static long[] Ids(IEnumerable<StashItem> items) => items.Select(item => item.Id).ToArray();

    private static GridTile InColumn(GridRow row, GridTile from) =>
        row.Tiles.SingleOrDefault(tile => tile.X <= from.CentreX && from.CentreX < tile.X + tile.Width)
        ?? row.Tiles.OrderBy(tile => Math.Abs(tile.CentreX - from.CentreX)).First();

    private static GridTile Tile(ShownWindow shown, long id) => shown.Grid.Rows.SelectMany(row => row.Tiles).Single(tile => tile.Id == id);

    private static ScrollContentPresenter Viewport(ShownWindow shown) => shown.Grid.FindAncestorOfType<ScrollContentPresenter>()!;

    private static async Task MovingEndShows(ShownWindow shown, long id)
    {
        await Eventually(() => TrayDesktop.Get(() =>
        {
            var view = Viewport(shown);
            return new Rect(view.Bounds.Size).Contains(TileBox(shown, id, view));
        }));
        var frame = await RenderedWindow.Of(shown.Window);
        TrayDesktop.Run(() =>
        {
            var box = TileBox(shown, id, (Control)shown.Window.Content!);
            var x = (int)(box.Center.X * frame.Scaling);
            var y = (int)(box.Y * frame.Scaling);
            var ring = shown.Grid.ActualThemeVariant == ThemeVariant.Dark ? 255 : 0;
            Assert.IsTrue(Enumerable.Range(1, 4).Any(up => Enumerable.Range(0, 3).All(channel =>
                Math.Abs((int)((frame.At(x, y - up) >> (8 * channel)) & 0xFF) - ring) < 64)),
                "the moving end has the visible keyboard focus ring (G7)");
        });
    }

    private static Rect TileBox(ShownWindow shown, long id, Control relativeTo)
    {
        var tile = Tile(shown, id);
        return new Rect(shown.Grid.TranslatePoint(new Point(tile.X, tile.Y), relativeTo)!.Value, new Size(tile.Width, tile.Height));
    }

    private static async Task CopyFiles(ShownWindow shown, Control origin, StashItem[] expected)
    {
        TestClipboard.PutText("before range copy");
        KeyFrom(shown, origin, Key.C, KeyModifiers.Control);
        await Eventually(() => shown.Uses() >= expected.Length
            && TrayDesktop.Get(() => shown.Listing.Settled && shown.Handoff.Busy is null));
        var clipboard = TestClipboard.Read();
        CollectionAssert.AreEqual(expected.Select(item => item.FilePath).ToArray(), clipboard.Files.ToArray(), "the range's exact files in listing order (G7–G9)");
        Assert.IsNull(clipboard.Png);
        Assert.IsNull(clipboard.DibV5);
        Assert.IsFalse(TestClipboard.Holds(TestClipboard.CfBitmap) || TestClipboard.Holds(TestClipboard.CfDib));
        Assert.IsNull(TestClipboard.Text(), "a range copies files alone (G8)");
        Assert.AreEqual((long)expected.Length, shown.Uses());
        foreach (var item in expected)
        {
            Assert.AreEqual(1, (await shown.Listing.Store!.UseHistory(item.Id)).Value!.Count, "one use per copied item (G10)");
        }

        TrayDesktop.Run(() => Assert.IsTrue(shown.Window.IsVisible && shown.Window.IsActive, "Ctrl+C leaves the window open and active (G9)"));
    }

    private static async Task TagThenOpenSettings(ShownWindow shown, StashItem[] all, long[] selected, long movingEnd, string tag)
    {
        var grid = TrayDesktop.Get(() => shown.Grid);
        KeyFrom(shown, grid, Key.T, KeyModifiers.Control);
        await Eventually(() => TrayDesktop.Get(() => shown.TagField.IsEffectivelyVisible && shown.TagBox.IsFocused));
        var box = TrayDesktop.Get(() =>
        {
            CollectionAssert.AreEquivalent(selected, shown.TagField.Items.Select(item => item.Id).ToArray(), "the field opens on the range (G17)");
            Type(shown.TagBox, tag);
            shown.Window.UpdateLayout();
            return shown.TagBox;
        });
        KeyFrom(shown, box, Key.Enter);
        await Eventually(() => TrayDesktop.Get(() => shown.TagField.Field.Tags.SequenceEqual([tag])));
        TrayDesktop.Run(() =>
        {
            Assert.IsTrue(shown.TagField.IsEffectivelyVisible && box.IsFocused, "Enter leaves the field open, with focus (G17)");
            Assert.AreEqual("", box.Text ?? "");
        });
        var stored = (await shown.Listing.Store!.All()).Value!;
        CollectionAssert.AreEquivalent(Ids(all), Ids(stored));
        foreach (var item in stored)
        {
            CollectionAssert.AreEqual(selected.Contains(item.Id) ? new[] { tag } : Array.Empty<string>(), item.Tags.ToArray(), $"exact stored tags on {item.Name} (G17)");
        }

        KeyFrom(shown, box, Key.Escape);
        await Eventually(() => TrayDesktop.Get(() => !shown.TagField.IsVisible && grid.IsFocused));
        TrayDesktop.Run(() => AssertSelection(shown, grid, movingEnd, selected, "Esc from the tag field"));

        using var settings = new ShownSettings.SettingsFolder();
        SettingsWindow? open = null;
        try
        {
            TrayDesktop.Run(() => shown.Window.SettingsRequested += () =>
            {
                open = SettingsWindow.Open(open, () => settings.Page());
                shown.Window.Follow(open);
            });
            KeyFrom(shown, grid, Key.OemComma, KeyModifiers.Control);
            await Eventually(() => TrayDesktop.Get(() => open is { IsVisible: true, IsActive: true }
                && open.TryGetPlatformHandle()!.Handle == FrontWindow.GetForegroundWindow()));
        }
        finally
        {
            TrayDesktop.Run(() => open?.Close());
        }
    }
}
