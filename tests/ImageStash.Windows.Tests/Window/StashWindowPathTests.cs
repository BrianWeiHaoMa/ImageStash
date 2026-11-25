using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using ImageStash.Layers;
using ImageStash.Seams;
using ImageStash.Window;
using ImageStash.Windows.Seams;
using ImageStash.Windows.Tests.Support;
using SkiaSharp;
using static ImageStash.Windows.Tests.Support.ShownWindow;

namespace ImageStash.Windows.Tests.Window;

/// <summary>
/// One path through the real window with real keys (G12, G16, G7, G18, G17): a drop whose batch ends after the flyout
/// hid leaves its notice for the summon, counting what was added and naming the file that was not; Del then <c>Ctrl+Z</c>
/// brings the item back; <c>Ctrl+T</c>, a tag typed and a real click on the grid tag the item. It shows the stash window,
/// clicks its grid with the real pointer and deletes a picture of its own into the Recycle Bin, whose entry the Undo
/// removes. The first run's pinned empty stash gives way to the grid once a real click on its Paste row adds the picture
/// on the clipboard (G2, G13). A flyout whose focus goes while the pointer's primary button is held stays, takes a drag
/// and its drop, and hides; a press with no drag hides it once the button is up, and the open shortcut at once (G2, G11).
/// A screenshot taken while the flyout is hidden, a real drag over a pattern of the check's own, is the first tile of
/// Recent at the next summon, unused, ahead of the picture used before it (G4, G1).
/// </summary>
[TestClass]
public sealed partial class StashWindowPathTests
{
    [TestInitialize]
    public void UseProductTheme() => TrayDesktop.UseProductTheme();

    [TestMethod]
    public async Task EmptySearchRightRightEnterHandsOverTheThirdPictureWithoutMovingFocus()
    {
        var relay = PresetTarget.App("Relay");
        using var shown = ShownWindow.Open();
        await shown.OpenStash(shown.Picture("first.png"), shown.Picture("second.png"), shown.Picture("third.png"));
        await TrayDesktop.Get(() => shown.Listing.ShowScope(Scope.All));
        TrayDesktop.Run(() => shown.Window.SummonFromShortcut(new ForegroundReading(false, relay), new SummonAnchor(null)));
        await Eventually(() => TrayDesktop.Get(() => shown.Search.IsFocused && shown.Listing.Settled));

        var expected = TrayDesktop.Get(() =>
        {
            var third = shown.Listing.Items[2];
            Assert.AreEqual("", shown.Search.Text ?? "");
            Tap(shown.Search, Key.Right);
            Tap(shown.Search, Key.Right);
            Assert.AreEqual(third.Id, shown.Listing.Focused);
            Assert.IsTrue(shown.Search.IsFocused);
            Press(shown.Search, Key.Enter);
            Press(shown.Search, Key.Enter);
            Release(shown.Search, Key.Enter);
            return third;
        });

        await Eventually(() => shown.Target.Pasted.Count == 1 && shown.Uses() == 1);
        CollectionAssert.AreEqual(new[] { expected.FilePath }, TestClipboard.Read().Files.ToArray());
        Assert.AreEqual(1, (await shown.Listing.Store!.UseHistory(expected.Id)).Value!.Count);
        CollectionAssert.AreEqual(new[] { relay }, shown.Target.Pasted);
        TrayDesktop.Run(() => Assert.IsFalse(shown.Window.IsVisible));
    }

    [TestMethod]
    [DataRow(" ")]
    [DataRow("cat")]
    public async Task HorizontalArrowsInTheSearchBoxKeepTextEditingEvenWithNoSearchWords(string text)
    {
        using var shown = ShownWindow.Open();
        await shown.OpenStash(shown.Picture("cat first.png"), shown.Picture("cat second.png"));
        await TrayDesktop.Get(() => shown.Listing.ShowScope(Scope.All));
        TrayDesktop.Run(shown.Window.SummonPinned);
        TrayDesktop.Run(() => shown.Search.Text = text);
        await Eventually(() => TrayDesktop.Get(() => shown.Search.IsFocused && shown.Listing.Settled && shown.Listing.Query == text));

        TrayDesktop.Run(() =>
        {
            var first = shown.Listing.Focused;
            shown.Search.SelectAll();
            Tap(shown.Search, Key.Right);
            Assert.AreEqual(text.Length, shown.Search.CaretIndex);
            Tap(shown.Search, Key.Right);
            Assert.AreEqual(text.Length, shown.Search.CaretIndex);
            Tap(shown.Search, Key.Left);
            Assert.AreEqual(text.Length - 1, shown.Search.CaretIndex);
            Assert.AreEqual(first, shown.Listing.Focused);
            Assert.IsTrue(shown.Search.IsFocused);
        });
    }

    [TestMethod]
    [DataRow(Key.Space, false)]
    [DataRow(Key.Space, true)]
    [DataRow(Key.F2, false)]
    public async Task PendingArrowsAndPreviewKeysUseTheFocusedWindowAndThePictureShown(Key opener, bool selectAllWhilePreviewed)
    {
        using var shown = ShownWindow.Open();
        await shown.OpenStash(shown.Picture("cat first.png"), shown.Picture("cat second.png"), shown.Picture("cat third.png"));
        var expectedOrder = (await shown.Listing.Store!.Search("cat")).Value!;
        TrayDesktop.Run(shown.Window.SummonPinned);
        await Eventually(() => TrayDesktop.Get(() => shown.Search.IsFocused && shown.Listing.Settled));

        using (shown.HoldStore())
        {
            TrayDesktop.Run(() =>
            {
                shown.Search.Text = "cat";
                Assert.IsFalse(shown.Listing.Settled);
                Tap(shown.Search, Key.Tab);
                Assert.IsTrue(shown.Grid.IsFocused);
                Tap(shown.Grid, Key.Right);
                Tap(shown.Grid, opener);
                if (selectAllWhilePreviewed)
                {
                    Tap(shown.Grid, Key.A, KeyModifiers.Control);
                    Tap(shown.Grid, Key.Space);
                    Tap(shown.Grid, Key.C, KeyModifiers.Control);
                }
                else if (opener == Key.Space)
                {
                    Tap(shown.Grid, Key.Right);
                    Tap(shown.Grid, Key.C, KeyModifiers.Control);
                }
            });
        }

        if (opener == Key.F2)
        {
            await Eventually(() => TrayDesktop.Get(() => shown.Preview.IsOpen && shown.NameBox.IsFocused));
            TrayDesktop.Run(() =>
            {
                Assert.AreEqual(expectedOrder[1].Id, shown.Preview.Item?.Id);
                Assert.AreEqual(expectedOrder[1].Name, shown.NameBox.SelectedText);
            });
        }
        else if (selectAllWhilePreviewed)
        {
            await Eventually(() => shown.Uses() == 1);
            TrayDesktop.Run(() =>
            {
                Assert.IsFalse(shown.Preview.IsOpen);
                CollectionAssert.AreEqual(new[] { expectedOrder[1].Id }, shown.Listing.Selection.ToArray());
                Assert.IsTrue(shown.Grid.IsFocused);
            });
            CollectionAssert.AreEqual(new[] { expectedOrder[1].FilePath }, TestClipboard.Read().Files.ToArray());
        }
        else
        {
            await Eventually(() => shown.Uses() == 1);
            TrayDesktop.Run(() =>
            {
                Assert.AreEqual(expectedOrder[2].Id, shown.Preview.Item?.Id);
                Assert.IsTrue(shown.PreviewPicture.IsFocused);
            });
            CollectionAssert.AreEqual(new[] { expectedOrder[2].FilePath }, TestClipboard.Read().Files.ToArray());
        }
    }

    [TestMethod]
    public async Task ADropEndingHiddenThenDelAndCtrlZThenCtrlTTypeAndClickTheGrid()
    {
        using var shown = ShownWindow.Open();
        await shown.OpenStash(shown.Picture("path kept.png"));
        TrayDesktop.Run(() => shown.Notices.Close(shown.Notices.Shown.Single()));
        var pictures = new[] { "path one.png", "path two.png", "path three.png", "path four.png" }.Select(shown.Picture).ToList();
        var clip = Path.Combine(Path.GetDirectoryName(pictures[0])!, "clip.mp4");
        File.WriteAllBytes(clip, [1, 2, 3]);
        var dropped = new DropContents([.. pictures.Select(path => new DroppedItem(Path.GetFileName(path), path, null, IsFolder: false)), new DroppedItem("clip.mp4", clip, null, IsFolder: false)], false, false, []);
        var clock = (StoppedClock)shown.Notices.Clock;

        // The drop starts, and the flyout hides in the same turn, before its batch ends.
        var importing = TrayDesktop.Get(() =>
        {
            var started = shown.Import.Drop(dropped);
            shown.Window.Hide();
            return started;
        });
        var four = (await importing).Where(file => file.Item is not null).Select(file => file.Item!).ToList();
        clock.Advance(TimeSpan.FromHours(1));
        TrayDesktop.Run(shown.Notices.Sweep);
        TrayDesktop.Run(() => shown.Window.SummonFromShortcut(default, new SummonAnchor(null)));

        await Eventually(() => TrayDesktop.Get(() => shown.Window.IsVisible && CardSays(shown, "Added 4 · 1 skipped")));
        TrayDesktop.Run(() => Assert.IsTrue(shown.Window.GetVisualDescendants().OfType<TextBlock>().Any(block => block.IsEffectivelyVisible && block.Text is { } text && text.StartsWith("Skipped clip.mp4 (a video)")), "the file not added, named with its reason"));

        var chosen = four[1];
        TrayDesktop.Run(() =>
        {
            shown.Listing.Focus(chosen.Id);
            shown.Grid.Focus();
            Tap(shown.Grid, Key.Delete);
        });
        await Eventually(() => TrayDesktop.Get(() => shown.Listing.Items.All(item => item.Id != chosen.Id)));
        TrayDesktop.Run(() => Tap(shown.Grid, Key.Z, KeyModifiers.Control));
        await Eventually(() => TrayDesktop.Get(() => shown.Listing.Items.Any(item => item.Id == chosen.Id)));
        Assert.IsTrue(File.Exists(chosen.FilePath), "back with its file");

        TrayDesktop.Run(() =>
        {
            shown.Listing.Focus(chosen.Id);
            shown.Grid.Focus();
            Tap(shown.Grid, Key.T, KeyModifiers.Control);
        });
        await Eventually(() => TrayDesktop.Get(() => shown.TagField.IsVisible && shown.TagBox.IsFocused));
        TrayDesktop.Run(() =>
        {
            Type(shown.TagBox, "cat");
            // The import's notice sits over the grid's foot, where the click goes.
            foreach (var notice in shown.Notices.Shown.ToList())
            {
                shown.Notices.Dismiss(notice);
            }
        });
        // The real pointer lands where the last frame drew the grid.
        await Task.Delay(200);
        using var pointer = TestMouse.PutBackAfterwards();

        TestMouse.Click(TrayDesktop.Get(() => shown.Grid.PointToScreen(new Point(shown.Grid.Bounds.Width / 2, shown.Grid.Bounds.Height - 12))));

        await Eventually(() => shown.TagsOf(chosen.Id).Contains("cat"));
        TrayDesktop.Run(() => Assert.IsFalse(shown.TagField.IsVisible));
    }

    /// <summary>
    /// Ctrl+D stars the middle tile. Space opens its preview, and the arrow keys and ‹ step to both ends.
    /// Ctrl+E edits the picture shown. Clipboard words go into its Meme, and a clipboard picture raises a notice.
    /// </summary>
    [TestMethod]
    public async Task CtrlDInAllStarsOnlyTheMiddleTileAndClickThenSpaceShowsItsStarBeforeArrowsStepToBothEnds()
    {
        using var pointer = TestMouse.PutBackAfterwards();
        using var shown = ShownWindow.Open();
        await shown.OpenStash(
            shown.Picture("path red.png", 32, 32, (_, _) => SKColors.Maroon),
            shown.Picture("path green.png", 32, 32, (_, _) => SKColors.Green),
            shown.Picture("path blue.png", 32, 32, (_, _) => SKColors.Navy));
        TransferredStash.CloseNotices(shown);
        await TrayDesktop.Get(() => shown.Listing.ShowScope(Scope.All));
        await TransferredStash.EveryTileShows(shown, new()
        {
            ["path red"] = (uint)SKColors.Maroon,
            ["path green"] = (uint)SKColors.Green,
            ["path blue"] = (uint)SKColors.Navy,
        }, "the three pictures in All");
        var tiles = TrayDesktop.Get(() => shown.Grid.Rows.Single().Tiles.ToArray());
        Assert.HasCount(3, tiles);
        var middle = tiles[1].Id;

        TrayDesktop.Run(() =>
        {
            Assert.AreEqual(Scope.All, shown.Listing.LitChip);
            shown.Listing.Focus(middle);
            shown.Grid.Focus();
            Assert.AreSame(shown.Grid, shown.Focused);
            Tap(shown.Grid, Key.D, KeyModifiers.Control);
        });
        await Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled && shown.Listing.Items.Any(item => item.Id == middle && item.Favorite)));
        var rendered = await RenderedWindow.Of(shown.Window);
        TrayDesktop.Run(() =>
        {
            Assert.IsGreaterThan(0, TileStarTests.StarInk(rendered, TileStarTests.TileBox(shown, middle)), "the middle tile shows the star (G6)");
            foreach (var tile in new[] { tiles[0], tiles[2] })
            {
                Assert.AreEqual(0, TileStarTests.StarInk(rendered, TileStarTests.TileBox(shown, tile.Id)), $"tile {tile.Id} shows no star (G6)");
            }

            ChipRowRoomTests.AssertScopeGlyphs(shown);
            RaiseClick(shown.Chip("Favorites"));
        });
        await Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled && shown.Listing.Scope == Scope.Favorites));
        TrayDesktop.Run(() =>
        {
            shown.Window.UpdateLayout();
            CollectionAssert.AreEqual(new[] { middle }, shown.Grid.Rows.SelectMany(row => row.Tiles).Select(tile => tile.Id).ToArray(),
                "the Favorites chip shows only the starred tile (G4)");
            RaiseClick(shown.Chip("All"));
        });
        await Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled && shown.Listing.Scope == Scope.All));
        TrayDesktop.Run(() =>
        {
            shown.Window.UpdateLayout();
            CollectionAssert.AreEqual(tiles.Select(tile => tile.Id).ToArray(), shown.Grid.Rows.SelectMany(row => row.Tiles).Select(tile => tile.Id).ToArray(),
                "the All chip restores all three tiles (G4)");
            shown.Search.Focus();
        });

        TestMouse.Click(TrayDesktop.Get(() => shown.TileOnScreen(middle)));
        await Eventually(() => TrayDesktop.Get(() => shown.Grid.IsFocused && shown.Listing.Selection.SequenceEqual([middle])));
        TrayDesktop.Run(() =>
        {
            Tap(shown.Grid, Key.Space);
            Assert.AreEqual(middle, shown.Preview.Item?.Id);
            Assert.AreEqual(FavoriteStar.Filled, ((TextBlock)PreviewStarTests.StarButton(shown).Content!).Text, "the preview opens with the same favorite flag (G19)");

            Tap(shown.PreviewPicture, Key.Right);
            Assert.AreEqual(tiles[2].Id, shown.Preview.Item?.Id, "Right shows the next item in All (G19)");
            CollectionAssert.AreEqual(new[] { tiles[2].Id }, shown.Listing.Selection.ToList());
            Assert.AreEqual(FavoriteStar.Outlined, ((TextBlock)PreviewStarTests.StarButton(shown).Content!).Text, "the last item is plain");

            Tap(shown.PreviewPicture, Key.Right);
            Assert.AreEqual(tiles[2].Id, shown.Preview.Item?.Id, "Right at the last item stays there (G19)");
            CollectionAssert.AreEqual(new[] { tiles[2].Id }, shown.Listing.Selection.ToList());

            Tap(shown.PreviewPicture, Key.Left);
            Assert.AreEqual(middle, shown.Preview.Item?.Id);
        });

        await PreviewStepButtonsTests.ClickStep(shown, shown.Preview.StepButtons.Back);
        await Eventually(() => TrayDesktop.Get(() => shown.Preview.Item?.Id == tiles[0].Id));
        TrayDesktop.Run(() =>
        {
            Assert.AreEqual(tiles[0].Id, shown.Preview.Item?.Id, "Left followed by Previous reaches the first item in All (G19)");
            Assert.IsTrue(shown.Preview.IsOpen);
            CollectionAssert.AreEqual(new[] { tiles[0].Id }, shown.Listing.Selection.ToList());
        });

        // (3) From the preview into the editor, where Ctrl+V acts on the open layer's text (G13, G25).
        await PasteIntoMemeFromPreview(shown, tiles[0].Id, tiles.Length);
    }

    private static async Task PasteIntoMemeFromPreview(ShownWindow shown, long previewedId, int itemCount)
    {
        using var editor = new ShownEditor(shown);
        TrayDesktop.Run(() => Tap(shown.PreviewPicture, Key.E, KeyModifiers.Control));
        await Eventually(() => TrayDesktop.Get(() => editor.Session?.Item.Id == previewedId));
        await Eventually(() => TrayDesktop.Get(() =>
            editor.Canvas.Mapping() is not null &&
            editor.Canvas.TranslatePoint(editor.At(0.5, 0.25), shown.Window) is { } point &&
            ReferenceEquals(shown.Window.InputHitTest(point), editor.Canvas)));
        TestMouse.Click(TrayDesktop.Get(() => editor.Canvas.PointToScreen(editor.At(0.5, 0.25))));
        await Eventually(() => TrayDesktop.Get(() => editor.Field is { IsFocused: true } && editor.Session!.Typing));

        TestClipboard.PutText("so true");
        TrayDesktop.Run(() => Tap(editor.Field!, Key.V, KeyModifiers.Control));
        await Eventually(() => TrayDesktop.Get(() => editor.Field?.Text is { Length: > 0 } || shown.Notices.Shown.Count > 0));
        Assert.HasCount(itemCount, (await shown.Listing.Store!.All()).Value!, "the words add no stash item");
        TrayDesktop.Run(() =>
        {
            var layer = (TextLayer)editor.Session!.Layers.Layers.Single();
            Assert.AreEqual(TextPreset.Meme, layer.Preset);
            Assert.AreEqual("SO TRUE", layer.Text);
            Assert.AreEqual("SO TRUE", editor.Field?.Text);
            Assert.IsEmpty(shown.Notices.Shown, "the words paste without a notice");
        });

        TestClipboard.Put((TestClipboard.Png, File.ReadAllBytes(shown.Picture("path clipboard.png"))));
        TrayDesktop.Run(() => Tap(editor.Field!, Key.V, KeyModifiers.Control));
        await Eventually(() => TrayDesktop.Get(() => shown.Notices.Shown.Count > 0));
        Assert.HasCount(itemCount, (await shown.Listing.Store!.All()).Value!, "the picture adds no stash item");
        TrayDesktop.Run(() =>
        {
            Assert.AreEqual("SO TRUE", editor.Field?.Text);
            Assert.AreEqual("SO TRUE", ((TextLayer)editor.Session!.Layers.Layers.Single()).Text);
            Assert.AreEqual("Paste failed: the clipboard, it holds no text to paste", shown.Said());
        });
    }

    /// <summary>
    /// The first run's window, pinned over an empty stash (G2): its rows and its line show, and neither showing them nor
    /// the pointer resting on the Paste row reads the clipboard (N9); with a picture on the clipboard, a real click on
    /// the Paste row reads it once and adds it, the grid takes the empty stash's place, and the focus stays in the search
    /// box (G13, G7).
    /// </summary>
    [TestMethod]
    public async Task TheFirstRunsPinnedEmptyStashThenARealClickOnItsPasteRowShowsTheGrid()
    {
        var clipboard = new PresetDrags();
        using var shown = ShownWindow.Open(drags: clipboard);
        using var pointer = TestMouse.PutBackAfterwards();
        await shown.OpenStash();
        TrayDesktop.Run(shown.Window.SummonPinned);
        await Eventually(() => TrayDesktop.Get(() => shown.Window.IsActive && shown.Search.IsFocused && ShownEmptyStash.View(shown) is not null));
        await Task.Delay(200);
        TrayDesktop.Run(() =>
        {
            Assert.HasCount(5, ShownEmptyStash.Rows(shown), "every way in as a row");
            StringAssert.Contains(ShownEmptyStash.Lines(shown)[^1], "Closing this window keeps it running");
        });

        TestClipboard.Put((TestClipboard.Png, File.ReadAllBytes(shown.Picture("path first run.png"))));
        var row = TrayDesktop.Get(() => ShownEmptyStash.Row(shown, "Paste a copied image"));
        var paste = TestMouse.Centre(row);
        TestMouse.MoveTo(paste);
        await Eventually(() => TrayDesktop.Get(() => row.IsPointerOver));
        await TrayDesktop.Get(() => shown.Listing.Settle());
        Assert.AreEqual(0, clipboard.Reads, "showing the empty stash, and the pointer resting on a row, read nothing of the clipboard (N9)");

        TestMouse.Click(paste);

        await Eventually(() => TrayDesktop.Get(() => shown.Listing.Items.Count == 1));
        Assert.AreEqual(1, clipboard.Reads, "the click read it once (G13)");
        await TrayDesktop.Get(() => shown.Listing.Settle());
        TrayDesktop.Run(() =>
        {
            Assert.IsNull(ShownEmptyStash.View(shown), "the empty stash gives way (G2)");
            Assert.IsTrue(shown.Grid.IsEffectivelyVisible, "to the grid with the pasted picture");
            Assert.IsTrue(shown.Search.IsFocused, "the focus where it was (G7)");
        });
    }

    [TestMethod]
    public async Task AFlyoutWhoseFocusGoesWhileTheButtonIsHeldStaysTakesADragAndItsDropHidesThenShowsTheImportAtTheNextSummon()
    {
        using var shown = ShownWindow.Open();
        var kept = (await shown.OpenStash(shown.Picture("held kept.png"))).Single();
        TrayDesktop.Run(() => shown.Notices.Close(shown.Notices.Shown.Single()));
        var data = await shown.Files(shown.Picture("held dropped.png"));
        TrayDesktop.Run(() =>
        {
            shown.Window.Hide();
            shown.Window.SummonFromShortcut(new ForegroundReading(false, PresetTarget.App("Relay")), new SummonAnchor(null));
            shown.OpenPreview(kept);
        });

        // A press on a picture in a browser takes the focus before its drag begins.
        shown.QuietWindows.ButtonHeld = true;
        TrayDesktop.Run(shown.Deactivate);
        await Task.Delay(300);
        TrayDesktop.Run(() =>
        {
            Assert.IsTrue(shown.Window.IsVisible, "the flyout stays while the press begun in the other app is held (G2)");
            RaiseDrag(shown.Window, DragDrop.DragEnterEvent, data);
            RaiseDrag(shown.Window, DragDrop.DragOverEvent, data);
        });
        // The button comes up over the flyout before the drop reaches it.
        shown.QuietWindows.ButtonHeld = false;
        await Task.Delay(300);
        TrayDesktop.Run(() =>
        {
            Assert.IsTrue(shown.Window.IsVisible, "a drag over it keeps it until the drop (G11)");
            Assert.AreEqual(DragDropEffects.Copy, RaiseDrag(shown.Window, DragDrop.DropEvent, data));
            Assert.IsFalse(shown.Window.IsVisible, "the drop is followed by the hide in its own turn, focus being elsewhere (G2)");
            Assert.IsFalse(shown.Preview.IsOpen, "the window's one hide closed the preview (G1)");
        });

        await Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled && shown.Listing.Items.Count == 2 && shown.Import.Busy is null));
        TrayDesktop.Run(() => shown.Window.SummonFromShortcut(new ForegroundReading(false, PresetTarget.App("Relay")), new SummonAnchor(null)));
        await Eventually(() => TrayDesktop.Get(() => shown.Window.IsVisible && shown.Listing.Settled && CardSays(shown, "Added 1 to your stash")));
        TrayDesktop.Run(() =>
        {
            var added = shown.Listing.Items.Single(item => item.Id != kept.Id);
            Assert.AreEqual(Scope.All, shown.Listing.LitChip, "the import ended on All (G12)");
            CollectionAssert.AreEqual(new[] { added.Id }, shown.Listing.Selection.ToArray(), "with the new picture first and selected");
            Assert.IsTrue(shown.Search.IsFocused, "the summon lands in the search box (G1)");
            var buttons = shown.Window.GetVisualDescendants().OfType<Button>().Where(button => button.IsEffectivelyVisible).Select(button => button.Content as string).ToList();
            CollectionAssert.Contains(buttons, "Undo", "the import's notice waited for the summon with its Undo (G16)");
            CollectionAssert.DoesNotContain(buttons, ImportEnd.Show, "nothing the person did moved them on, so nothing to Show (G12)");
        });
    }

    [TestMethod]
    public async Task AClickElsewhereHidesTheFlyoutOnceTheButtonIsUpAndTheOpenShortcutHidesAWaitingFlyoutAtOnce()
    {
        using var shown = ShownWindow.Open();
        await shown.OpenStash(shown.Picture("held click.png"));
        var relay = new ForegroundReading(false, PresetTarget.App("Relay"));
        TrayDesktop.Run(() =>
        {
            shown.Window.Hide();
            shown.Window.SummonFromShortcut(relay, new SummonAnchor(null));
        });

        shown.QuietWindows.ButtonHeld = true;
        TrayDesktop.Run(shown.Deactivate);
        await Task.Delay(300);
        TrayDesktop.Run(() => Assert.IsTrue(shown.Window.IsVisible, "a click held in another app no longer hides it at the press (G2)"));
        shown.QuietWindows.ButtonHeld = false;
        await Eventually(() => TrayDesktop.Get(() => !shown.Window.IsVisible), TimeSpan.FromSeconds(2));

        TrayDesktop.Run(() =>
        {
            shown.Window.SummonFromShortcut(relay, new SummonAnchor(null));
            Assert.IsTrue(shown.Window.IsVisible);
        });
        shown.QuietWindows.ButtonHeld = true;
        TrayDesktop.Run(shown.Deactivate);
        await Task.Delay(300);
        TrayDesktop.Run(() =>
        {
            Assert.IsTrue(shown.Window.IsVisible, "waiting on the held button");
            shown.Window.SummonFromShortcut(relay, new SummonAnchor(null));
            Assert.IsFalse(shown.Window.IsVisible, "the open shortcut hides it at once, waiting for nothing (G2)");
        });
        shown.QuietWindows.ButtonHeld = false;
    }

    [TestMethod]
    public async Task AScreenshotTakenWithTheFlyoutHiddenIsRecentsFirstTileAtTheNextSummon()
    {
        using var desk = await CaptureDesk.Open(["used before the screenshot.png"]);
        var used = desk.Items().Single();
        await TrayDesktop.Get(() => desk.Shown.Listing.Store!.RecordUses([used], desk.Clock.GetUtcNow()));
        await TrayDesktop.Get(() => desk.Shown.Listing.ShowScope(Scope.Recent));
        TrayDesktop.Run(() => Assert.IsFalse(desk.Shown.Window.IsVisible, "the flyout hidden"));
        using var pattern = PatternWindow.Show();
        await pattern.OnScreen(new WindowsScreen());

        var taking = desk.Take();
        await desk.OverlayInFront();
        await CaptureDesk.Drag(pattern.Blocks[0].Block.Position + new PixelVector(20, 20), pattern.Blocks[3].Block.Position + new PixelVector(80, 50));
        await taking;
        TrayDesktop.Run(desk.Shown.Window.SummonFromTray);
        await Eventually(() => TrayDesktop.Get(() => desk.Shown.Window.IsVisible));

        var shot = desk.Items().Single(each => each.Id != used.Id);
        TrayDesktop.Run(() =>
        {
            Assert.AreEqual(Scope.Recent, desk.Shown.Listing.LitChip, "the summon shows Recent, as it was left (G1)");
            CollectionAssert.AreEqual(new[] { shot.Id, used.Id }, desk.Shown.Listing.Items.Select(each => each.Id).ToList(),
                "G4: the screenshot, never used, first, by when it was added; then the picture used before it");
        });
        Assert.AreEqual(1L, desk.Shown.Uses(), "the screenshot is no use (G10)");
    }

    private static bool CardSays(ShownWindow shown, string text) =>
        shown.Window.GetVisualDescendants().OfType<TextBlock>().Any(block => block.Text == text && block.IsEffectivelyVisible);
}
