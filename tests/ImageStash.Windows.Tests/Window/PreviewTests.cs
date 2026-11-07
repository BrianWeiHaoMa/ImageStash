using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using ImageStash.Seams;
using ImageStash.Store;
using ImageStash.Window;
using ImageStash.Windows.Tests.Support;
using SkiaSharp;

namespace ImageStash.Windows.Tests.Window;

/// <summary>
/// The preview inside the stash window laid out by the real desktop platform (G19, F4): opened by Space in the grid, a
/// <c>Ctrl</c>+double-click and the item menu's Preview, closed by Space, Esc and its back arrow, by pointer and by Tab;
/// the box, the chips and the footer's hints out of sight and reach, and no window of its own; the facts; Enter, <c>Ctrl+C</c>, Del and <c>Ctrl+D</c> on its item whatever the grid selects (G8, G9, G17, G18);
/// Esc's first step (G2); a summon leaving it open; an import's end and its item's going closing it; only its latest
/// read applied; the paste path through it with the keyboard alone and with the pointer alone (G7).
/// </summary>
[TestClass]
public sealed class PreviewTests
{
    private static readonly PasteTarget Relay = PresetTarget.App("Relay");

    [TestMethod]
    public async Task SpaceInTheGridOpensThePreviewInsideTheWindowOverTheBoxAndTheChipsTabStaysInsideAndSpaceClosesIt()
    {
        using var shown = ShownWindow.Open();
        var items = await shown.OpenStash(shown.Picture("preview one.png"), shown.Picture("preview two.png"));
        var windowsBefore = ProcessWindows.Visible();

        TrayDesktop.Run(() =>
        {
            shown.Listing.Select([items[0].Id, items[1].Id], items[1].Id);
            shown.Grid.Focus();
            ShownWindow.Press(shown.Grid, Key.Space);
        });

        TrayDesktop.Run(() =>
        {
            Assert.IsTrue(shown.Preview.IsOpen);
            Assert.AreEqual(items[1].Id, shown.Preview.Item!.Id, "the focused item, the one with the ring");
            Assert.AreSame(shown.Window, TopLevel.GetTopLevel(shown.Preview), "inside the stash window (F4)");
            CollectionAssert.AreEqual(new[] { items[1].Id }, shown.Listing.Selection.ToList(), "that item alone selected");
            Assert.AreSame(shown.PreviewPicture, shown.Focused, "focus inside the preview, on its picture, which rings it (G7)");
            Assert.IsFalse(shown.Search.IsEffectivelyVisible, "the box is out of sight and reach");
            Assert.IsFalse(shown.ChipRow.IsEffectivelyVisible, "so are the chips");
            Assert.IsEmpty(shown.FooterHints(), "and the footer's hints");
        });
        Assert.AreEqual(windowsBefore, ProcessWindows.Visible(), "no window of its own (F4)");
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => HitAt(shown, shown.Grid) is Visual hit && hit.GetSelfAndVisualAncestors().Contains(shown.Preview)));

        // With no target the handoff button is the copy, and Copy beside it is not shown (G19).
        var round = new List<object?>();
        for (var press = 0; press < 6; press++)
        {
            TrayDesktop.Run(() =>
            {
                ShownWindow.Press((Control)shown.Focused!, Key.Tab);
                round.Add(shown.Focused);
            });
        }

        TrayDesktop.Run(() => CollectionAssert.AreEqual(new object?[] { shown.PreviewTagBox, shown.HandoffButton, shown.EditButton, shown.BackArrow, shown.MoreButton, shown.PreviewPicture }, round,
            "Tab goes round the preview's own controls, its tag field, handoff button, Edit, back arrow and ⋯ button, never to the search box or the grid"));

        TrayDesktop.Run(() => ShownWindow.Press(shown.PreviewPicture, Key.Space));
        TrayDesktop.Run(() =>
        {
            Assert.IsFalse(shown.Preview.IsOpen, "Space again closes it");
            Assert.IsTrue(shown.Grid.IsFocused, "focus back on the grid");
            Assert.IsTrue(shown.Search.IsEffectivelyVisible);
            Assert.IsNotEmpty(shown.FooterHints());
        });

        TrayDesktop.Run(() =>
        {
            shown.Search.Focus();
            ShownWindow.Press(shown.Search, Key.Space);
            shown.Search.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = " ", Source = shown.Search });
        });
        TrayDesktop.Run(() =>
        {
            Assert.IsFalse(shown.Preview.IsOpen, "Space in the box opens nothing");
            Assert.AreEqual(" ", shown.Search.Text, "it is a space typed (G7)");
        });
    }

    [TestMethod]
    public async Task TheBackArrowClosesItByPointerAndByTabAndSpaceWhileEnterThereHandsOff()
    {
        using var shown = ShownWindow.Open();
        var item = (await shown.OpenStash(shown.Picture("preview back.png"))).Single();
        TrayDesktop.Run(() => shown.OpenPreview(item));

        TrayDesktop.Run(() => shown.BackArrow.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
        TrayDesktop.Run(() => Assert.IsFalse(shown.Preview.IsOpen, "the pointer's way out (G7)"));

        TrayDesktop.Run(() => shown.OpenPreview(item));
        TrayDesktop.Run(() => ShownWindow.Press(shown.PreviewPicture, Key.Tab));
        TrayDesktop.Run(() => ShownWindow.Press(shown.PreviewTagBox, Key.Tab));
        TrayDesktop.Run(() => ShownWindow.Press(shown.HandoffButton, Key.Tab));
        TrayDesktop.Run(() => ShownWindow.Press(shown.EditButton, Key.Tab));
        TrayDesktop.Run(() => Assert.AreSame(shown.BackArrow, shown.Focused, "Tab reaches the back arrow after the tag field, the handoff button and Edit"));
        TrayDesktop.Run(() => ShownWindow.Press(shown.BackArrow, Key.Enter));
        await ShownWindow.Eventually(() => shown.Uses() == 1);
        TrayDesktop.Run(() => Assert.IsTrue(shown.Preview.IsOpen, "Enter on the back arrow is the handoff, not a close"));

        TrayDesktop.Run(() =>
        {
            ShownWindow.Press(shown.BackArrow, Key.Space);
            ShownWindow.Release(shown.BackArrow, Key.Space);
        });
        TrayDesktop.Run(() => Assert.IsFalse(shown.Preview.IsOpen, "Space presses the focused back arrow"));
    }

    [TestMethod]
    public async Task ACtrlDoubleClickOpensThePreviewAPlainDoubleClickHandsOffAndTheMenusPreviewOpensItOnOneItem()
    {
        using var pointer = TestMouse.PutBackAfterwards();
        using var shown = ShownWindow.Open();
        var items = await shown.OpenStash(shown.Picture("preview click one.png"), shown.Picture("preview click two.png"));

        TrayDesktop.Run(() => shown.DoubleClick(items[0].Id, KeyModifiers.Control));
        TrayDesktop.Run(() => Assert.AreEqual(items[0].Id, shown.Preview.Item?.Id, "Ctrl+double-click previews the tile (G19)"));
        TrayDesktop.Run(() => ShownWindow.Press(shown.PreviewPicture, Key.Escape));

        TrayDesktop.Run(() => shown.DoubleClick(items[1].Id));
        await ShownWindow.Eventually(() => shown.Uses() == 1);
        TrayDesktop.Run(() => Assert.IsFalse(shown.Preview.IsOpen, "a plain double-click is the handoff (G8)"));

        TrayDesktop.Run(() => shown.Listing.Select([items[0].Id, items[1].Id], items[0].Id));
        var several = TrayDesktop.Get(() => shown.RightClick(items[0].Id));
        TrayDesktop.Run(() => shown.Listing.Focus(items[1].Id));
        var one = TrayDesktop.Get(() => shown.RightClick(items[1].Id));
        // A real click, as a person's: a click event raised on the entry may leave the focus on it, under the closing menu.
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.MenuEntry("Preview").Bounds.Height > 0));
        TestMouse.Click(TestMouse.Centre(TrayDesktop.Get(() => shown.MenuEntry("Preview"))));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Preview.IsOpen && ReferenceEquals(shown.Focused, shown.PreviewPicture)));

        Assert.DoesNotContain("Preview", several, "several items get no Preview");
        CollectionAssert.AreEqual(new[] { "Copy", "Preview", "Edit", "Add to Favorites", "Rename", "Add tag", "Save a copy…", "Open with…", "Reveal in File Explorer", "Delete" }, one, "after the handoff, Delete last (G8, G18)");
        TrayDesktop.Run(() => Assert.AreEqual(items[1].Id, shown.Preview.Item!.Id, "on the item the menu was opened on, focus inside it once the menu closed"));
    }

    [TestMethod]
    public async Task TheFactsAreTheItemsAndEnterAndCtrlCActOnThePreviewsItemWhateverTheGridSelects()
    {
        using var shown = ShownWindow.Open();
        var gif = shown.Fixture("animated.gif", "preview facts.gif");
        var items = await shown.OpenStash(gif, shown.Picture("preview facts one.png"), shown.Picture("preview facts two.png"));
        var animated = items.Single(item => item.Name == "preview facts");

        TrayDesktop.Run(() => shown.OpenPreview(animated));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Preview.Facts.Count == 2));
        CollectionAssert.AreEqual(new[] { "GIF · 8 × 6 · 1 KB · 0.6 s", "Added today · Never used" }, TrayDesktop.Get(() => shown.Preview.Facts.ToList()),
            "its type, size, file length on disk and duration, when added and its uses (G19)");

        TrayDesktop.Run(() =>
        {
            shown.Listing.Select(items.Select(item => item.Id), items[0].Id);
            ShownWindow.Press(shown.PreviewPicture, Key.Enter);
        });
        await ShownWindow.Eventually(() => shown.Uses() == 1);
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Preview.Facts.Count == 2 && shown.Preview.Facts[1] == "Added today · Used once, just now"));
        Assert.AreEqual(1, (await shown.Listing.Store!.UseHistory(animated.Id)).Value!.Count, "the previewed item, not the three the grid selects (G8, G10)");
        TrayDesktop.Run(() => Assert.IsTrue(shown.Window.IsVisible && shown.Preview.IsOpen, "without a target Enter copies and the window stays"));
        CollectionAssert.AreEqual(new[] { animated.FilePath }, TestClipboard.Read().Files.ToList());

        TrayDesktop.Run(() => ShownWindow.Press(shown.PreviewPicture, Key.C, KeyModifiers.Control));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Preview.Facts.Count == 2 && shown.Preview.Facts[1].StartsWith("Added today · Used 2 times, last ")));
        Assert.AreEqual(2L, shown.Uses(), "Ctrl+C's use on the same item, the window staying (G9, G10)");
    }

    [TestMethod]
    public async Task WithATargetEnterPastesThePreviewsItemTheFlyoutHidesWithThePreviewOpenAndATraySummonFindsItThere()
    {
        var target = new PresetTarget { Before = null };
        using var shown = ShownWindow.Open(target: target);
        var items = await shown.OpenStash(shown.Picture("preview paste one.png"), shown.Picture("preview paste two.png"));
        TrayDesktop.Run(() =>
        {
            shown.Window.Hide();
            shown.Window.SummonFromShortcut(new ForegroundReading(false, Relay), new SummonAnchor(null));
        });
        await TrayDesktop.Get(() => shown.Listing.Settle());
        TrayDesktop.Run(() => shown.OpenPreview(items[1]));

        TrayDesktop.Run(() => ShownWindow.Press(shown.PreviewPicture, Key.Enter));
        await ShownWindow.Eventually(() => target.Pasted.Count == 1);
        TrayDesktop.Run(() =>
        {
            Assert.IsFalse(shown.Window.IsVisible, "the flyout hid for the paste (G2, G8)");
            Assert.AreEqual(items[1].Id, shown.Preview.Item?.Id, "with the preview still open on its item");
        });
        CollectionAssert.AreEqual(new[] { items[1].FilePath }, TestClipboard.Read().Files.ToList());

        TrayDesktop.Run(shown.Window.SummonFromTray);
        await TrayDesktop.Get(() => shown.Listing.Settle());
        TrayDesktop.Run(() =>
        {
            Assert.IsTrue(shown.Window.IsVisible);
            Assert.AreEqual(items[1].Id, shown.Preview.Item?.Id, "summoned again, the preview is open on the same item");
            Assert.AreSame(shown.PreviewPicture, shown.Focused, "focus inside it, not in the box it covers");
            ShownWindow.Press(shown.PreviewPicture, Key.C, KeyModifiers.Control);
        });
        await ShownWindow.Eventually(() => shown.Uses() == 2);
        Assert.AreEqual(2, (await shown.Listing.Store!.UseHistory(items[1].Id)).Value!.Count, "the keys act on it");
    }

    [TestMethod]
    public async Task CtrlDOnTheSoleFavoriteSeenInFavoritesKeepsThePreviewOnItAndActsOnItsLatestRead()
    {
        var target = new PresetTarget();
        using var shown = ShownWindow.Open(target: target);
        var items = await shown.OpenStash(shown.Picture("preview favorite.png"), shown.Picture("preview other.png"));
        var favorite = items[0];
        await shown.Listing.Store!.SetFavorite([favorite], true);
        await TrayDesktop.Get(() => shown.Listing.ShowScope(Scope.Favorites));
        TrayDesktop.Run(() => shown.Window.PasteTarget.FromPress(new ForegroundReading(false, Relay)));
        TrayDesktop.Run(() => shown.OpenPreview(favorite));

        TrayDesktop.Run(() => ShownWindow.Press(shown.PreviewPicture, Key.D, KeyModifiers.Control));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled && shown.Listing.Items.Count == 0 && shown.Preview.Item is { Favorite: false }));
        TrayDesktop.Run(() => Assert.AreEqual(favorite.Id, shown.Preview.Item!.Id, "Favorites is empty now, and the preview stays on its item"));

        TrayDesktop.Run(() => ShownWindow.Press(shown.PreviewPicture, Key.D, KeyModifiers.Control));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Preview.Item is { Favorite: true }));
        TrayDesktop.Run(() => ShownWindow.Press(shown.PreviewPicture, Key.Enter));
        await ShownWindow.Eventually(() => target.Pasted.Count == 1);

        Assert.AreEqual(1, (await shown.Listing.Store!.UseHistory(favorite.Id)).Value!.Count, "handed off once, the item the preview shows");
        Assert.AreEqual(1L, shown.Uses());

        var listed = new List<int>();
        TrayDesktop.Run(() => shown.Listing.Changed += () => listed.Add(shown.Listing.Items.Count));
        using (shown.HoldStore())
        {
            // Two presses before the first is written: each hands over the item as the preview last read it.
            TrayDesktop.Run(() =>
            {
                ShownWindow.Press(shown.PreviewPicture, Key.D, KeyModifiers.Control);
                ShownWindow.Press(shown.PreviewPicture, Key.D, KeyModifiers.Control);
            });
            await Task.Delay(200);
        }

        // Favorites empties after the first press's change and lists the item again after the second's.
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => listed.IndexOf(0) is >= 0 and var emptied && listed.Skip(emptied).Contains(1)
            && shown.Listing.Settled && shown.Preview.Item is { Favorite: true }));
        CollectionAssert.AreEqual(new[] { favorite.Id }, (await shown.Listing.Store!.Favorites()).Value!.Select(item => item.Id).ToList(),
            "two toggles bring the flag back, the second deciding from the first's answer (G17)");
    }

    [TestMethod]
    public async Task DelDeletesThePreviewsItemWithItsNoticeAboveTheClosedPreviewAndUndoBringsItBack()
    {
        using var shown = ShownWindow.Open();
        var items = await shown.OpenStash(shown.Picture("preview delete.png"), shown.Picture("preview kept.png"));
        var deleted = items[0];
        TrayDesktop.Run(() => shown.OpenPreview(deleted));

        using (shown.HoldStore())
        {
            TrayDesktop.Run(() => ShownWindow.Press(shown.PreviewPicture, Key.Delete));
            await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Window.GetVisualDescendants().OfType<TextBlock>()
                .Any(text => text.IsEffectivelyVisible && text.Text == "Deleting “preview delete”…")));
        }

        await ShownWindow.Eventually(() => TrayDesktop.Get(() => !shown.Preview.IsOpen && shown.Notices.Shown.Any(notice => notice.Text.StartsWith("Deleted "))));
        var notice = TrayDesktop.Get(() => shown.Notices.Shown.Single(notice => notice.Text.StartsWith("Deleted ")));
        TrayDesktop.Run(() =>
        {
            Assert.AreEqual("Deleted “preview delete”.", notice.Text);
            CollectionAssert.AreEqual(new[] { items[1].Id }, shown.Listing.Items.Select(item => item.Id).ToList());
            Assert.IsTrue(shown.Grid.IsFocused, "the preview closed with its item gone, focus on the grid");
        });
        Assert.HasCount(1, RecycleBinEntries.Of(deleted.FilePath), "its file in the Recycle Bin (G18)");
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Reachable(notice)));

        TrayDesktop.Run(() => _ = shown.Notices.Undo(notice));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Listing.Items.Count == 2));
        TrayDesktop.Run(() => Assert.IsFalse(shown.Preview.IsOpen, "back in the grid, the preview still closed"));
        Assert.IsEmpty(RecycleBinEntries.Of(deleted.FilePath));
    }

    [TestMethod]
    public async Task EscClosesThePreviewThenClearsTheSearchThenHidesAndPinnedGoesNoFurther()
    {
        using var shown = ShownWindow.Open();
        var item = (await shown.OpenStash(shown.Picture("preview escape.png"))).Single();
        TrayDesktop.Run(() => shown.Search.Text = "escape");
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled && shown.Listing.Items.Count == 1));
        TrayDesktop.Run(() => shown.OpenPreview(item));

        TrayDesktop.Run(() => ShownWindow.Press(shown.PreviewPicture, Key.Escape));
        TrayDesktop.Run(() => Assert.IsTrue(!shown.Preview.IsOpen && shown.Search.Text == "escape" && shown.Window.IsVisible, "the preview first"));
        TrayDesktop.Run(() => ShownWindow.Press(shown.Grid, Key.Escape));
        TrayDesktop.Run(() => Assert.IsTrue(shown.Search.Text == "" && shown.Window.IsVisible, "then the search"));
        TrayDesktop.Run(() => ShownWindow.Press(shown.Grid, Key.Escape));
        TrayDesktop.Run(() => Assert.IsFalse(shown.Window.IsVisible, "then the flyout hides (G2)"));

        TrayDesktop.Run(shown.Window.SummonPinned);
        await TrayDesktop.Get(() => shown.Listing.Settle());
        TrayDesktop.Run(() => shown.OpenPreview(item));
        TrayDesktop.Run(() => ShownWindow.Press(shown.PreviewPicture, Key.Escape));
        TrayDesktop.Run(() => ShownWindow.Press(shown.Grid, Key.Escape));
        TrayDesktop.Run(() => Assert.IsTrue(!shown.Preview.IsOpen && shown.Window.IsVisible, "pinned, it closes the preview and goes no further"));
    }

    [TestMethod]
    public async Task WithThePreviewOpenTheArrowsCtrlAAndTypingChangeNothingTheSizeKeysStillWorkAndANoticeShowsAboveIt()
    {
        using var shown = ShownWindow.Open();
        var items = await shown.OpenStash(shown.Picture("preview keys one.png"), shown.Picture("preview keys two.png"), shown.Picture("preview keys three.png"));
        TrayDesktop.Run(() => shown.OpenPreview(items[1]));
        var rowHeight = TrayDesktop.Get(() => shown.Grid.Rows[0].Height);

        TrayDesktop.Run(() =>
        {
            ShownWindow.Press(shown.PreviewPicture, Key.Down);
            ShownWindow.Press(shown.PreviewPicture, Key.A, KeyModifiers.Control);
            shown.PreviewPicture.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = "x", Source = shown.PreviewPicture });
            ShownWindow.Press(shown.PreviewPicture, Key.OemMinus, KeyModifiers.Control);
        });
        await ShownWindow.Eventually(() => TrayDesktop.Get(() =>
        {
            shown.Window.UpdateLayout();
            return shown.Grid.Rows[0].Height < rowHeight;
        }));

        TrayDesktop.Run(() =>
        {
            CollectionAssert.AreEqual(new[] { items[1].Id }, shown.Listing.Selection.ToList(), "neither an arrow nor Ctrl+A reaches the grid it covers");
            Assert.AreEqual("", shown.Search.Text ?? "", "nothing typed reaches the box");
            Assert.AreEqual(items[1].Id, shown.Preview.Item?.Id, "the preview still on its item");
        });

        var notice = TrayDesktop.Get(() => shown.Notices.Raise("A notice while the preview is open", "Undo", () => Task.FromResult(Outcome.Done)));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Preview.IsOpen && shown.Reachable(notice)));
    }

    [TestMethod]
    public async Task OnlyTheLatestReadAppliesWhenThePreviewMovesFromOneItemToAnotherWhileTheStoreIsHeld()
    {
        using var shown = ShownWindow.Open();
        var gif = shown.Fixture("animated.gif", "preview first.gif");
        var items = await shown.OpenStash(gif, shown.Picture("preview second.png"));
        var (first, second) = (items.Single(item => item.Name == "preview first"), items.Single(item => item.Name == "preview second"));
        TrayDesktop.Run(() => shown.OpenPreview(first));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Preview.Facts.Count == 2));

        using (shown.HoldStore())
        {
            TrayDesktop.Run(() =>
            {
                _ = shown.Preview.Refresh();
                shown.Preview.Close();
                shown.Preview.Open(second);
            });
            await Task.Delay(200);
        }

        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Preview.Facts.Count == 2 && shown.PreviewPicture.Image is not null));
        await Task.Delay(300);
        TrayDesktop.Run(() =>
        {
            Assert.AreEqual(second.Id, shown.Preview.Item!.Id);
            Assert.IsTrue(shown.Preview.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == "preview second"), "its name");
            Assert.StartsWith("PNG · 6 × 4 · ", shown.Preview.Facts[0], "its facts, not the first item's");
            Assert.AreEqual(new PixelSize(6, 4), shown.PreviewPicture.Image!.PixelSize, "its pixels");
            Assert.IsFalse(shown.PreviewPicture.Animated);
        });
        await ShownWindow.Eventually(() => !ShownWindow.Held(first.FilePath));
    }

    [TestMethod]
    public async Task ADropWhileThePreviewIsOpenShowsItsCardAboveItThenClosesItBeforeAllShowsTheNewItem()
    {
        using var shown = ShownWindow.Open();
        var item = (await shown.OpenStash(shown.Picture("preview before drop.png"))).Single();
        var dropped = shown.Picture("preview dropped.png");
        TrayDesktop.Run(() => shown.OpenPreview(item));
        var data = await shown.Files(dropped);
        var without = await RenderedWindow.Of(shown);

        TrayDesktop.Run(() => ShownWindow.RaiseDrag(shown.Window, DragDrop.DragEnterEvent, data));
        var with = await RenderedWindow.Of(shown);
        Assert.IsGreaterThan(without.Area.Width * 10, without.Differences(with), "the drop card shows above the preview (G11)");

        TrayDesktop.Run(() => ShownWindow.RaiseDrag(shown.Window, DragDrop.DropEvent, data));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Listing.Items.Count == 2 && shown.Listing.Selection.Count == 1 && shown.Listing.Selection.First() != item.Id));
        TrayDesktop.Run(() =>
        {
            Assert.IsFalse(shown.Preview.IsOpen, "the import's end closed it (G12)");
            Assert.AreEqual(Scope.All, shown.Listing.LitChip);
            Assert.IsTrue(shown.Search.IsEffectivelyVisible);
        });
    }

    [TestMethod]
    public async Task ItsItemDeletedElsewhereThenCtrlDIsRefusedByNameAndThePreviewCloses()
    {
        using var shown = ShownWindow.Open();
        var items = await shown.OpenStash(shown.Picture("preview gone.png"), shown.Picture("preview stays.png"));
        TrayDesktop.Run(() => shown.OpenPreview(items[0]));
        var deleted = await shown.Listing.Store!.Delete([items[0].Id]);

        TrayDesktop.Run(() => ShownWindow.Press(shown.PreviewPicture, Key.D, KeyModifiers.Control));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => !shown.Preview.IsOpen));

        TrayDesktop.Run(() => Assert.IsTrue(shown.Notices.Shown.Any(notice => notice.Text.Contains("preview gone") && notice.Text.EndsWith("it is no longer in the stash")),
            string.Join(" | ", shown.Notices.Shown.Select(notice => notice.Text))));
        await shown.Listing.Store!.UndoDelete(deleted.Select(outcome => outcome.Value!).ToList());
        Assert.IsEmpty(RecycleBinEntries.Of(items[0].FilePath), "the check leaves nothing in the bin");
    }

    [TestMethod]
    public async Task WithTheKeyboardAloneTheShortcutTwoLettersTabSpaceAndEnterPreviewTheMatchAndPasteIt()
    {
        var target = new PresetTarget { Before = null };
        using var shown = ShownWindow.Open(target: target);
        var items = await shown.OpenStash(shown.Picture("keys alpha.png"), shown.Picture("keys beta.png"), shown.Picture("other.png"));
        var beta = items.Single(item => item.Name == "keys beta");
        TrayDesktop.Run(() =>
        {
            shown.Window.Hide();
            shown.Window.SummonFromShortcut(new ForegroundReading(false, Relay), new SummonAnchor(null));
        });
        await TrayDesktop.Get(() => shown.Listing.Settle());

        // Every step a key on whatever has focus: no pointer, and nothing focused or selected for it (G7).
        TrayDesktop.Run(() => ShownWindow.Type((Control)shown.Focused!, "be"));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled && shown.Listing.Items.Count == 1));
        TrayDesktop.Run(() => ShownWindow.Press((Control)shown.Focused!, Key.Tab));
        TrayDesktop.Run(() => ShownWindow.Press((Control)shown.Focused!, Key.Space));
        TrayDesktop.Run(() =>
        {
            Assert.AreEqual(beta.Id, shown.Preview.Item?.Id, "the first match previewed");
            ShownWindow.Press((Control)shown.Focused!, Key.Enter);
        });
        await ShownWindow.Eventually(() => target.Pasted.Count == 1 && shown.Uses() == 1);

        TrayDesktop.Run(() => Assert.IsFalse(shown.Window.IsVisible, "the handoff left the preview, the flyout hidden for the paste (G8)"));
        CollectionAssert.AreEqual(new[] { beta.FilePath }, TestClipboard.Read().Files.ToList());
    }

    [TestMethod]
    public async Task WithThePointerAloneTheMenusPreviewAndTheHandoffButtonPasteTheItemAndTheBackArrowLeaves()
    {
        var target = new PresetTarget { Before = Relay };
        using var shown = ShownWindow.Open(target: target);
        var items = await shown.OpenStash(shown.Picture("pointer one.png"), shown.Picture("pointer two.png"));
        TrayDesktop.Run(() =>
        {
            // One selected, not the import's two, which a summon keeps (G1), so the menu offers Preview.
            shown.Listing.Focus(items[0].Id);
            shown.Window.Hide();
            shown.Window.SummonFromTray();
        });
        await TrayDesktop.Get(() => shown.Listing.Settle());

        // Every step a click: no key pressed (G7).
        TrayDesktop.Run(() => shown.RightClick(items[1].Id));
        TrayDesktop.Run(() => shown.ClickMenuEntry("Preview"));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Preview.Item?.Id == items[1].Id));
        TrayDesktop.Run(() => shown.BackArrow.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
        TrayDesktop.Run(() => Assert.IsFalse(shown.Preview.IsOpen, "the back arrow leaves it"));

        TrayDesktop.Run(() => shown.RightClick(items[1].Id));
        TrayDesktop.Run(() => shown.ClickMenuEntry("Preview"));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Preview.Item?.Id == items[1].Id));
        TrayDesktop.Run(() => shown.HandoffButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
        await ShownWindow.Eventually(() => target.Pasted.Count == 1 && shown.Uses() == 1);

        TrayDesktop.Run(() => Assert.IsFalse(shown.Window.IsVisible, "the handoff button left the preview, the flyout hidden for the paste (G8)"));
        CollectionAssert.AreEqual(new[] { items[1].FilePath }, TestClipboard.Read().Files.ToList());
    }

    [TestMethod]
    public async Task AnImportConfirmedWhileThePreviewPlaysAnAnimationClosesItReplacesTheStashAndShowsNothingOfTheReplacedItem()
    {
        using var pointer = TestMouse.PutBackAfterwards();
        TestMouse.MoveTo(TestMouse.Away);
        using var shown = ShownWindow.Open();
        var items = await shown.OpenStash(shown.Fixture("animated.gif", "previewed import.gif"));
        var previewed = items.Single();
        TransferredStash.CloseNotices(shown);
        TrayDesktop.Run(() => shown.OpenPreview(previewed));
        await ShownWindow.Eventually(() => ShownWindow.Held(previewed.FilePath));
        var folder = TrayDesktop.Get(() => shown.Listing.Store!.Folder.Root);
        // The imported item 1 is another picture: the preview must show neither it nor the replaced one (G19).
        var blue = new SKColor(0x20, 0x30, 0xD0);
        var archive = await TransferredStash.ExportOf(shown, ("previewed import", blue));
        try
        {
            var imported = await TransferredStash.Import(shown, archive);

            Assert.IsTrue(imported.Succeeded, imported.Failure?.Sentence);
            await TrayDesktop.Get(() => shown.Listing.ShowScope(Scope.All));
            TrayDesktop.Run(() =>
            {
                Assert.IsFalse(shown.Preview.IsOpen, "the window shows the grid and no preview (G19, G6)");
                Assert.IsNull(shown.PreviewPicture.Image);
                Assert.AreEqual(folder, shown.Listing.Store!.Folder.Root, "listed again from the same folder (G36)");
                Assert.AreEqual(previewed.Id, shown.Listing.Items.Single().Id, "the imported store gives the replaced item's identity to another");
            });
            await TransferredStash.EveryTileShows(shown, new() { ["previewed import"] = (uint)blue }, "the imported stash");
        }
        finally
        {
            TransferredStash.RemoveImportsLeftovers(shown, archive);
        }
    }

    [TestMethod]
    public async Task LettingGoForAnImportWaitsUntilAStillsReadUnderWayHasClosedItsFile()
    {
        using var shown = ShownWindow.Open();
        var still = (await shown.OpenStash(shown.Picture("let go still.png"))).Single();
        TransferredStash.CloseNotices(shown);
        Task lettingGo;
        // Held by another program as the preview opens, so its read is still under way, retrying, when the preview is let go;
        // taken once the grid's thumbnail read, or a scanner, has let go of the fresh file (E3).
        FileStream? hold = null;
        await ShownWindow.Eventually(() =>
        {
            try
            {
                hold = new FileStream(still.FilePath, FileMode.Open, FileAccess.Read, FileShare.None);
                return true;
            }
            catch (IOException)
            {
                return false;
            }
        });
        using (hold)
        {
            TrayDesktop.Run(() => shown.OpenPreview(still));
            lettingGo = TrayDesktop.Get(() => shown.Preview.LetGo());
            Assert.IsFalse(lettingGo.IsCompleted,"not let go while its read may yet open the file, which would keep the stash folder from its rename (G36)");
        }

        await lettingGo.WaitAsync(TimeSpan.FromSeconds(20));
        Assert.IsFalse(ShownWindow.Held(still.FilePath), "its file closed once let go");
        TrayDesktop.Run(() => Assert.IsFalse(shown.Preview.IsOpen));
    }

    /// <summary>The element a click at the centre of <paramref name="control"/> lands on, in the rendered window.</summary>
    private static IInputElement? HitAt(ShownWindow shown, Control control)
    {
        var root = (Control)shown.Window.Content!;
        var centre = control.TranslatePoint(new Point(control.Bounds.Width / 2, Math.Min(control.Bounds.Height / 2, 40)), root)!.Value;
        return root.InputHitTest(centre);
    }
}
