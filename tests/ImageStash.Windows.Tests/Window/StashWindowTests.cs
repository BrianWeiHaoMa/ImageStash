using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ImageStash.Files;
using ImageStash.Seams;
using ImageStash.Store;
using ImageStash.Window;
using ImageStash.Windows.Seams;
using ImageStash.Windows.Tests.Support;
using Microsoft.Data.Sqlite;
using SkiaSharp;
using static ImageStash.Windows.Tests.Support.ShownWindow;

namespace ImageStash.Windows.Tests.Window;

/// <summary>
/// The stash window laid out by the real desktop platform with the Fluent theme, at the flyout's size, off every
/// display and never activated: a notice's Undo stays where a click reaches it however many files its batch names
/// (G12, G16); Del in the grid and the item menu's Delete send the selection to the real Recycle Bin, and the notice's
/// Undo brings it back and removes the bin's entries, while Del in the search box deletes a character (G18, G7, S9);
/// <c>Ctrl+D</c> in the grid toggles favorite on the selection and in the box changes nothing (G17, G7).
/// </summary>
[TestClass]
public sealed class StashWindowTests
{
    [TestMethod]
    public async Task TheSettingsButtonBesideThePlusIsNamedTippedNeverFocusedAndAsksForThePageInBothStates()
    {
        using var shown = ShownWindow.Open();
        await shown.OpenStash();
        var asked = 0;

        TrayDesktop.Run(() =>
        {
            shown.Window.SettingsRequested += () => asked++;
            var button = shown.SettingsButton;
            Assert.AreEqual("Settings", ToolTip.GetTip(button), "tipped as the + button is");
            Assert.IsFalse(button.Focusable, "Tab goes from the box to the grid (G7)");
            Assert.AreEqual(Grid.GetColumn(shown.AddButton) - 1, Grid.GetColumn(button), "beside the + button, on its left (G32)");
            ShownWindow.RaiseClick(button);
            Assert.AreEqual(1, asked, "from the flyout (G2)");
            shown.Window.SummonPinned();
            ShownWindow.RaiseClick(shown.SettingsButton);
        });

        Assert.AreEqual(2, asked, "and from the pinned window (G2, G32)");
    }

    [TestMethod]
    public async Task ABatchOfTwoHundredFilesKeepsItsNoticesUndoInsideTheWindow()
    {
        using var shown = ShownWindow.Open();

        TrayDesktop.Run(() => shown.Notices.Raise(Import.NoticeText(Added(200)), "Undo", () => Task.FromResult(Outcome.Done)));
        await Task.Delay(500);

        TrayDesktop.Run(() => Assert.IsTrue(shown.Reachable(shown.Notices.Shown[^1]), "the Undo of a 200-file batch is where a click reaches it"));
    }

    [TestMethod]
    public async Task ManyLongNoticesKeepTheNewestUndoInsideTheWindow()
    {
        using var shown = ShownWindow.Open();

        TrayDesktop.Run(() =>
        {
            for (var batch = 0; batch < 6; batch++)
            {
                shown.Notices.Raise(Import.NoticeText(Added(40, $"batch {batch} ")), "Undo", () => Task.FromResult(Outcome.Done));
            }
        });

        // The hit test sees the stack scrolled to its end only once the renderer has caught up, which on a PC whose
        // display is off took longer than half a second; the newest Undo must come within reach, however long that takes.
        await Eventually(() => TrayDesktop.Get(() => shown.Reachable(shown.Notices.Shown[^1])));
    }

    [TestMethod]
    public async Task EnterCtrlCAndADoubleClickCopyTheSelectionFromTheBoxAndTheGridAndTheWindowStays()
    {
        using var shown = ShownWindow.Open();
        var item = (await shown.OpenStash(shown.Picture("window check corner.png"))).Single();
        TrayDesktop.Run(() => Assert.IsTrue(shown.Window.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == "Enter · Copy" && text.IsEffectivelyVisible),
            "with no paste target the footer reads Enter · Copy (G8)"));

        TrayDesktop.Run(() => ShownWindow.Press(shown.Search, Key.Enter));
        await Eventually(() => shown.Uses() == 1);
        var read = TestClipboard.Read();
        CollectionAssert.AreEqual(new[] { item.FilePath }, read.Files.ToList(), "Enter from the box copies the selection");
        Assert.IsNotNull(read.Png);
        Assert.IsNotNull(read.DibV5);

        TrayDesktop.Run(() => shown.Search.Text = "corner");
        await Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled && shown.Listing.Selection.Contains(item.Id)));
        TrayDesktop.Run(() =>
        {
            shown.Search.Focus();
            shown.Search.ClearSelection();
            ShownWindow.Press(shown.Search, Key.C, KeyModifiers.Control);
        });
        await Eventually(() => shown.Uses() == 2);
        CollectionAssert.AreEqual(new[] { item.FilePath }, TestClipboard.Read().Files.ToList(), "Ctrl+C in the box with no text selected copies the items (G7)");

        TrayDesktop.Run(() =>
        {
            shown.Grid.Focus();
            ShownWindow.Press(shown.Grid, Key.C, KeyModifiers.Control);
        });
        await Eventually(() => shown.Uses() == 3);

        TrayDesktop.Run(() => shown.DoubleClick(item.Id));
        await Eventually(() => shown.Uses() == 4);
        CollectionAssert.AreEqual(new[] { item.FilePath }, TestClipboard.Read().Files.ToList());
        TrayDesktop.Run(() => Assert.IsTrue(shown.Window.IsVisible, "a copy does not hide the window (G2)"));
    }

    [TestMethod]
    public async Task CtrlCInTheBoxWithTextSelectedCopiesThatTextAndNoItemAndRecordsNoUse()
    {
        using var shown = ShownWindow.Open();
        var item = (await shown.OpenStash(shown.Picture("cat.png"))).Single();
        TrayDesktop.Run(() =>
        {
            shown.Search.Text = "cat";
            shown.Search.Focus();
            shown.Search.SelectionStart = 0;
            shown.Search.SelectionEnd = 2;
        });
        await Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled && shown.Listing.Query == "cat"));
        TestClipboard.Empty();

        TrayDesktop.Run(() => ShownWindow.Press(shown.Search, Key.C, KeyModifiers.Control));
        await Eventually(() => TestClipboard.Text() == "ca");
        await Task.Delay(300);

        Assert.IsEmpty(TestClipboard.Read().Files, "no item written (G7)");
        Assert.AreEqual(0, shown.Uses(), "and no use recorded (G10)");
        TrayDesktop.Run(() => CollectionAssert.AreEqual(new[] { item.Id }, shown.Listing.Selection.ToList()));
    }

    [TestMethod]
    [DataRow(Key.C, "cat")]
    [DataRow(Key.X, "cat")]
    public async Task ACopyOrCutOfTheBoxsTextWhileAnotherProgramHoldsTheClipboardIsNamedAndTheTextStays(Key key, string left)
    {
        using var shown = ShownWindow.Open();
        await shown.OpenStash(shown.Picture("cat.png"));
        TrayDesktop.Run(() =>
        {
            shown.Search.Text = "cat";
            shown.Search.Focus();
            shown.Search.SelectAll();
        });
        await Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled && shown.Listing.Query == "cat"));

        using (OtherProgramHoldingTheClipboard.Start())
        {
            TrayDesktop.Run(() => ShownWindow.Press(shown.Search, key, KeyModifiers.Control));
            await Eventually(() => shown.Notices.Shown.Any(notice => notice.Text.Contains(HeldClipboard.Reason)));
        }

        TrayDesktop.Run(() => Assert.AreEqual(left, shown.Search.Text, "what could not be written stays (G35)"));
    }

    [TestMethod]
    public async Task CtrlXInTheBoxPutsTheTextOnTheClipboardAndCtrlZBringsItBack()
    {
        using var shown = ShownWindow.Open();
        await shown.OpenStash(shown.Picture("cat.png"));
        TrayDesktop.Run(() =>
        {
            shown.Search.Text = "cat";
            shown.Search.Focus();
            shown.Search.SelectAll();
        });
        await Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled && shown.Listing.Query == "cat"));
        TestClipboard.Empty();

        TrayDesktop.Run(() => ShownWindow.Press(shown.Search, Key.X, KeyModifiers.Control));
        await Eventually(() => TestClipboard.Text() == "cat");
        await Eventually(() => TrayDesktop.Get(() => shown.Search.Text is null or ""));

        TrayDesktop.Run(() => ShownWindow.Press(shown.Search, Key.Z, KeyModifiers.Control));
        TrayDesktop.Run(() => Assert.AreEqual("cat", shown.Search.Text, "as in any Windows text box (G7)"));
    }

    [TestMethod]
    public async Task TheBoxsCaretKeysMoveItsCaretAndNeverTheGridWhileUpAndDownMoveTheSelectionAndLeaveFocusInTheBox()
    {
        using var shown = ShownWindow.Open();
        await shown.OpenStash(Enumerable.Range(1, 12).Select(i => shown.Picture($"cat {i}.png")).ToArray());
        TrayDesktop.Run(() =>
        {
            shown.Search.Text = "cat";
            shown.Search.Focus();
            shown.Search.CaretIndex = 3;
        });
        await Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled && shown.Listing.Query == "cat"));
        var selected = TrayDesktop.Get(() => shown.Listing.Selection.ToList());

        TrayDesktop.Run(() =>
        {
            ShownWindow.Press(shown.Search, Key.Left);
            Assert.AreEqual(2, shown.Search.CaretIndex, "Left (G7)");
            ShownWindow.Press(shown.Search, Key.Right);
            Assert.AreEqual(3, shown.Search.CaretIndex, "Right");
            ShownWindow.Press(shown.Search, Key.Home);
            Assert.AreEqual(0, shown.Search.CaretIndex, "Home");
            ShownWindow.Press(shown.Search, Key.End);
            Assert.AreEqual(3, shown.Search.CaretIndex, "End");
            ShownWindow.Press(shown.Search, Key.Left, KeyModifiers.Shift);
            Assert.AreEqual("t", shown.Search.SelectedText, "Shift with Left");
            CollectionAssert.AreEqual(selected, shown.Listing.Selection.ToList(), "never the grid");
            Assert.IsTrue(shown.Search.IsFocused);

            ShownWindow.Press(shown.Search, Key.Down);
            Assert.IsTrue(shown.Search.IsFocused, "Down moves no focus");
            Assert.AreEqual("cat", shown.Search.Text);
            CollectionAssert.AreNotEqual(selected, shown.Listing.Selection.ToList(), "Down moved the grid's selection");

            shown.Grid.Focus();
            var fromGrid = shown.Listing.Selection.ToList();
            ShownWindow.Press(shown.Grid, Key.Right);
            CollectionAssert.AreNotEqual(fromGrid, shown.Listing.Selection.ToList(), "from the grid, Right moves it");
        });
    }

    [TestMethod]
    public async Task EnterPressedRightAfterTypingCopiesTheFirstMatchNotWhatShowedBefore()
    {
        using var shown = ShownWindow.Open();
        var items = await shown.OpenStash(shown.Picture("window check corner.png"), shown.Picture("window check stripe.png"));
        var (corner, stripe) = (items[0], items[1]);
        Assert.AreNotEqual(corner.Id, stripe.Id, "two items");
        TrayDesktop.Run(() => shown.Listing.Focus(corner.Id));

        using (var holder = shown.HoldStore())
        {
            // While another connection holds the store, the query's matches cannot arrive before Enter is pressed.
            TrayDesktop.Run(() =>
            {
                shown.Search.Text = "stripe";
                ShownWindow.Press(shown.Search, Key.Enter);
            });
            await Task.Delay(300);
        }

        await Eventually(() => shown.Uses() == 1);

        CollectionAssert.AreEqual(new[] { stripe.FilePath }, TestClipboard.Read().Files.ToList(), "the selection moves to the first match (G5) before Enter acts on it");
    }

    [TestMethod]
    public async Task TheSearchBoxsOwnPasteDoesNotReadTheClipboardOnlyTheWindowsCtrlVDoes()
    {
        using var shown = ShownWindow.Open();
        TestClipboard.PutText("pasted words");

        TrayDesktop.Run(() =>
        {
            shown.Search.Focus();
            ShownWindow.Press(shown.Search, Key.Insert, KeyModifiers.Shift);
            // The text box's own Paste command, which its keys run.
            shown.Search.Paste();
        });
        await Task.Delay(500);

        TrayDesktop.Run(() => Assert.AreEqual("", shown.Search.Text ?? "", "the clipboard's text was not read into the box (N9, R5)"));
    }

    [TestMethod]
    public async Task TheSearchBoxOffersNoPasteThatWouldDoNothing()
    {
        using var shown = ShownWindow.Open();

        var offered = TrayDesktop.Get(() =>
        {
            shown.Search.Text = "words";
            shown.Search.SelectAll();
            shown.Search.ContextFlyout?.ShowAt(shown.Search);
            return shown.Search.ContextFlyout is MenuFlyout menu ? menu.Items.OfType<MenuItem>().Select(item => $"{item.Header}|{item.Name}|{item.IsEffectivelyEnabled}").ToList() : [];
        });
        await Task.Delay(300);
        TrayDesktop.Run(() => shown.Search.ContextFlyout?.Hide());

        Assert.IsFalse(offered.Any(entry => entry.Contains("Paste", StringComparison.OrdinalIgnoreCase)),
            "the box's Paste may not read the clipboard (N9), so it is not offered to do nothing (G35): " + string.Join(", ", offered));
        TrayDesktop.Run(() => Assert.IsNull(shown.Search.ContextMenu));
    }

    [TestMethod]
    public async Task AnImportMadeWhileAQueryIsTypedClearsTheSearchAndSelectsEveryNewItem()
    {
        using var shown = ShownWindow.Open();
        var earlier = (await shown.OpenStash(shown.Picture("typed probe one.png"))).Single();
        TrayDesktop.Run(() => shown.Search.Text = "probe");
        await Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled && shown.Listing.Query == "probe"));

        var paths = new[] { shown.Picture("typed probe two.png"), shown.Picture("typed probe three.png") };
        var added = await Dispatcher.UIThread.InvokeAsync(async () =>
            (await shown.Import.Drop(new DropContents(paths.Select(path => new DroppedItem(Path.GetFileName(path), path, null, IsFolder: false)).ToList(), false, false, [])))
                .Select(file => file.Item!.Id).ToList());
        // Whatever the import left posted, the box's TextChanged among it, runs before the check looks.
        await Task.Delay(500);
        await Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled));

        Assert.HasCount(2, added);
        TrayDesktop.Run(() =>
        {
            Assert.AreEqual("", shown.Search.Text ?? "", "the search is cleared (G12)");
            Assert.AreEqual(Scope.All, shown.Listing.LitChip, "All shows (G12)");
            CollectionAssert.AreEquivalent(added, shown.Listing.Items.Take(2).Select(item => item.Id).ToList(), "the new items first (G12)");
            Assert.AreEqual(earlier.Id, shown.Listing.Items[2].Id);
            CollectionAssert.AreEquivalent(added, shown.Listing.Selection.ToList(), "every new item selected (G12)");
        });
    }

    [TestMethod]
    public async Task TextTypedIntoTheBoxIsKeptWhenTheListingChangesBeforeItsTextChangedRuns()
    {
        using var shown = ShownWindow.Open();
        var items = await shown.OpenStash(shown.Picture("kept text one.png"), shown.Picture("kept text two.png"));

        TrayDesktop.Run(() =>
        {
            // Avalonia posts the box's TextChanged; a change of the listing before it runs, as a load arriving, may neither
            // put back the old query nor lose the keystroke.
            shown.Search.Text = "kept";
            shown.Listing.Focus(items[1].Id);
        });
        await Task.Delay(500);
        await Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled));

        TrayDesktop.Run(() =>
        {
            Assert.AreEqual("kept", shown.Search.Text ?? "", "the box takes every typed character (G7)");
            Assert.AreEqual("kept", shown.Listing.Query, "and searches it (G5)");
        });
    }

    [TestMethod]
    public async Task EditingTheQueryBackToWhatItWasMovesTheSelectionToTheFirstMatch()
    {
        using var shown = ShownWindow.Open();
        var items = await shown.OpenStash(shown.Picture("edited cat older.png"), shown.Picture("edited cat newer.png"));
        TrayDesktop.Run(() => shown.Search.Text = "cat");
        await Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled && shown.Listing.Query == "cat"));
        TrayDesktop.Run(() => shown.Listing.Focus(items[0].Id));

        TrayDesktop.Run(() =>
        {
            // Backspace, then the letter again, before the box's posted TextChanged has run.
            shown.Search.Text = "ca";
            shown.Search.Text = "cat";
        });
        await Task.Delay(500);
        await Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled));

        TrayDesktop.Run(() => CollectionAssert.AreEqual(new[] { items[1].Id }, shown.Listing.Selection.ToList(), "typing moves the selection to the first match (G5)"));
    }

    [TestMethod]
    public async Task ALetterTypedRightAfterASummonStartsANewQuery()
    {
        using var shown = ShownWindow.Open();
        var items = await shown.OpenStash(shown.Picture("summoned dog.png"), shown.Picture("summoned cat.png"));
        TrayDesktop.Run(() => shown.Search.Text = "dog");
        await Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled && shown.Listing.Query == "dog"));

        TrayDesktop.Run(() =>
        {
            shown.Window.Hide();
            shown.Window.SummonFromTray();
            ShownWindow.Type(shown.Search, "c");
        });
        await Task.Delay(500);
        await Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled));

        TrayDesktop.Run(() =>
        {
            Assert.AreEqual("c", shown.Search.Text ?? "", "a summon selects the box's text, so that the next letter replaces it (G1)");
            CollectionAssert.AreEqual(new[] { items[1].Id }, shown.Listing.Items.Select(item => item.Id).ToList(), "and that letter is the search (G5)");
        });
    }

    [TestMethod]
    public async Task AScopeClickedRightAfterAKeystrokeClearsTheSearch()
    {
        using var shown = ShownWindow.Open();
        await shown.OpenStash(shown.Picture("cleared one.png"), shown.Picture("cleared two.png"));

        TrayDesktop.Run(() =>
        {
            shown.Search.Text = "x";
            shown.Window.GetVisualDescendants().OfType<Button>().Single(button => button.Content as string == "All").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        });
        await Task.Delay(500);
        await Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled));

        TrayDesktop.Run(() =>
        {
            Assert.AreEqual("", shown.Search.Text ?? "", "clicking All clears the search (G4)");
            Assert.AreEqual("", shown.Listing.Query);
            Assert.HasCount(2, shown.Listing.Items, "and shows All");
        });
    }

    [TestMethod]
    public async Task LettersAndABackspaceTypedIntoTheBoxSearchAsTheyAreTyped()
    {
        using var shown = ShownWindow.Open();
        var items = await shown.OpenStash(shown.Picture("typed cat.png"), shown.Picture("typed car.png"), shown.Picture("typed dog.png"));
        var (cat, car) = (items[0], items[1]);

        TrayDesktop.Run(() =>
        {
            // What a keyboard's letters reach: the box's own text input, as the platform raises it on the focused box.
            shown.Search.Focus();
            foreach (var letter in new[] { "c", "a", "t" })
            {
                shown.Search.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = letter, Source = shown.Search });
            }
        });
        await Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled && shown.Listing.Query == "cat"));
        TrayDesktop.Run(() =>
        {
            Assert.AreEqual("cat", shown.Search.Text ?? "", "the box takes every typed character (G7)");
            CollectionAssert.AreEqual(new[] { cat.Id }, shown.Listing.Items.Select(item => item.Id).ToList(), "typing searches (G5)");
            ShownWindow.Press(shown.Search, Key.Back);
        });
        await Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled && shown.Listing.Query == "ca"));

        TrayDesktop.Run(() =>
        {
            CollectionAssert.AreEqual(new[] { car.Id, cat.Id }, shown.Listing.Items.Select(item => item.Id).ToList(), "the matches newest-added first (G5)");
            CollectionAssert.AreEqual(new[] { car.Id }, shown.Listing.Selection.ToList(), "the selection on the first match (G5)");
        });
    }

    [TestMethod]
    public async Task ADragFromOneOfThreeSelectedTilesCarriesAllThreeKeepsTheFlyoutUntilItEndsAndRecordsTheirUses()
    {
        var drags = new PresetDrags();
        using var shown = ShownWindow.Open(drags);
        var items = await shown.OpenStash(shown.Picture("drag one.png"), shown.Picture("drag two.png"), shown.Picture("drag three.png"));
        TrayDesktop.Run(() => shown.Listing.Select(items.Select(item => item.Id), items[0].Id));
        var inOrder = TrayDesktop.Get(() => shown.Listing.SelectedItems.Select(item => item.FilePath).ToList());
        var drop = new TaskCompletionSource<Outcome<bool>>();
        drags.Pending = drop.Task;
        // Another app takes focus while the drag is in progress, as the app it is dragged over may.
        drags.During = shown.Deactivate;

        TrayDesktop.Run(() =>
        {
            shown.Press(items[1].Id);
            shown.MovePointer(items[1].Id, 2, 1);
            Assert.IsEmpty(drags.Dragged, "a move of a pixel or two is not a drag");
            shown.MovePointer(items[1].Id, 12, 3);
        });
        await Task.Delay(500);

        CollectionAssert.AreEqual(inOrder, drags.Dragged.Single().ToList(), "every selected tile's file, in the grid's order (G9)");
        TrayDesktop.Run(() => Assert.IsTrue(shown.Window.DragInProgress && shown.Window.IsVisible, "the flyout stays while its drag is in progress (G2)"));
        Assert.AreEqual(0, shown.Uses(), "no use before the drop has landed (G10)");

        TrayDesktop.Run(() => drop.SetResult(Outcome<bool>.Of(true)));
        await Eventually(() => shown.Uses() == 3);

        TrayDesktop.Run(() =>
        {
            Assert.IsFalse(shown.Window.DragInProgress);
            CollectionAssert.AreEquivalent(items.Select(item => item.Id).ToList(), shown.Listing.Selection.ToList(), "the press did not collapse the selection");
        });
        await Eventually(() => TrayDesktop.Get(() => !shown.Window.IsVisible));
    }

    [TestMethod]
    public async Task APressOnASelectedTileReleasedWithoutADragSelectsThatTileAlone()
    {
        var drags = new PresetDrags();
        using var shown = ShownWindow.Open(drags);
        var items = await shown.OpenStash(shown.Picture("press one.png"), shown.Picture("press two.png"), shown.Picture("press three.png"));
        TrayDesktop.Run(() => shown.Listing.Select(items.Select(item => item.Id), items[0].Id));

        TrayDesktop.Run(() =>
        {
            shown.Press(items[2].Id);
            Assert.HasCount(3, shown.Listing.Selection, "kept while the button is down");
            shown.MovePointer(items[2].Id, 1, 1);
            shown.Release(items[2].Id);
        });

        TrayDesktop.Run(() => CollectionAssert.AreEqual(new[] { items[2].Id }, shown.Listing.Selection.ToList(), "released without a drag, a click (G17)"));
        Assert.IsEmpty(drags.Dragged);
        Assert.AreEqual(0, shown.Uses());
    }

    [TestMethod]
    public async Task TheWindowsOwnDragDroppedBackOntoItAddsNothingAndIsNotAUse()
    {
        var drags = new PresetDrags();
        using var shown = ShownWindow.Open(drags);
        var item = (await shown.OpenStash(shown.Picture("dropped back.png"))).Single();
        var noticesBefore = TrayDesktop.Get(() => shown.Notices.Shown.Count);
        var data = await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var transfer = new DataTransfer();
            transfer.Add(DataTransferItem.CreateFile((await shown.Window.StorageProvider.TryGetFileFromPathAsync(item.FilePath))!));
            return transfer;
        });
        var effects = new List<DragDropEffects>();
        drags.During = () =>
        {
            foreach (var routed in new[] { DragDrop.DragEnterEvent, DragDrop.DropEvent })
            {
                var dropped = new DragEventArgs(routed, data, shown.Grid, new Point(5, 5), KeyModifiers.None) { DragEffects = DragDropEffects.Copy };
                shown.Grid.RaiseEvent(dropped);
                effects.Add(dropped.DragEffects);
            }
        };
        drags.Ends = Outcome<bool>.Of(false);

        TrayDesktop.Run(() =>
        {
            shown.Press(item.Id);
            shown.MovePointer(item.Id, 20, 0);
        });
        await Task.Delay(500);

        CollectionAssert.AreEqual(new[] { DragDropEffects.None, DragDropEffects.None }, effects, "not taken back as a drop into the stash");
        TrayDesktop.Run(() =>
        {
            Assert.HasCount(noticesBefore, shown.Notices.Shown, "nothing imported, nothing said");
            Assert.HasCount(1, shown.Listing.Items);
        });
        Assert.AreEqual(0, shown.Uses());
    }

    [TestMethod]
    public async Task ARightClickSelectsAnUnselectedTileAndItsMenusFirstEntryCopiesWhileASelectedTileKeepsTheSelection()
    {
        using var shown = ShownWindow.Open();
        var items = await shown.OpenStash(shown.Picture("menu one.png"), shown.Picture("menu two.png"), shown.Picture("menu three.png"));
        TrayDesktop.Run(() => shown.Listing.Select([items[0].Id, items[1].Id], items[0].Id));

        var several = TrayDesktop.Get(() => shown.RightClick(items[1].Id));
        TrayDesktop.Run(() => CollectionAssert.AreEquivalent(new[] { items[0].Id, items[1].Id }, shown.Listing.Selection.ToList(), "a selected tile keeps the selection"));
        var one = TrayDesktop.Get(() => shown.RightClick(items[2].Id));
        TrayDesktop.Run(() => CollectionAssert.AreEqual(new[] { items[2].Id }, shown.Listing.Selection.ToList(), "an unselected tile becomes the selection"));

        CollectionAssert.AreEqual(new[] { "Copy", "Add to Favorites", "Add tag", "Delete 2 items" }, several);
        CollectionAssert.AreEqual(new[] { "Copy", "Preview", "Edit", "Add to Favorites", "Rename", "Add tag", "Save a copy…", "Open with…", "Reveal in File Explorer", "Delete" }, one, "the handoff first (G8), Delete last (G18)");
        TrayDesktop.Run(() => shown.ClickMenuEntry("Copy"));
        await Eventually(() => shown.Uses() == 1);
        CollectionAssert.AreEqual(new[] { items[2].FilePath }, TestClipboard.Read().Files.ToList());
        TrayDesktop.Run(() => shown.CloseMenu());
    }

    [TestMethod]
    public async Task DelInTheGridSendsTheThreeSelectedToTheRecycleBinAtOnceAndUndoBringsThemBackWithTheEntriesGone()
    {
        using var shown = ShownWindow.Open();
        var items = await shown.OpenStash(shown.Picture("del one.png"), shown.Picture("del two.png"), shown.Picture("del three.png"), shown.Picture("del kept.png"));
        var deleted = items.Take(3).ToList();
        var bytes = deleted.Select(item => File.ReadAllBytes(item.FilePath)).ToList();
        var work = Path.Combine(Path.GetDirectoryName(deleted[0].FilePath)!, ".work");
        TrayDesktop.Run(() => shown.Listing.Select(deleted.Select(item => item.Id), deleted[0].Id));

        using (shown.HoldStore())
        {
            TrayDesktop.Run(() =>
            {
                shown.Grid.Focus();
                ShownWindow.Press(shown.Grid, Key.Delete);
            });
            await Eventually(() => TrayDesktop.Get(() => BusyText(shown) == "Deleting 3 items…"));
        }

        await Eventually(() => TrayDesktop.Get(() => shown.Notices.Shown[^1].Text.StartsWith("Deleted ")));
        var notice = TrayDesktop.Get(() => shown.Notices.Shown[^1]);
        TrayDesktop.Run(() =>
        {
            CollectionAssert.AreEqual(new[] { items[3].Id }, shown.Listing.Items.Select(item => item.Id).ToList(), "the three left the grid");
            Assert.AreEqual("Deleted “del three”, “del two” and “del one”.", notice.Text, "named in the grid's order, newest added first");
            Assert.IsNull(BusyText(shown), "the busy state ended with the delete");
            Assert.IsTrue(shown.Window.IsVisible, "a delete does not hide the flyout (G2)");
        });
        foreach (var (item, original) in deleted.Zip(bytes))
        {
            var entry = RecycleBinEntries.Of(item.FilePath).Single();
            CollectionAssert.AreEqual(original, File.ReadAllBytes(entry.Recycled), "its file is in the real Recycle Bin");
            Assert.IsFalse(File.Exists(item.FilePath));
        }

        Assert.HasCount(3, Directory.GetFiles(work), "a held copy of each");
        await Eventually(() => TrayDesktop.Get(() => shown.Reachable(notice)));

        TrayDesktop.Run(() => _ = shown.Notices.Undo(notice));
        await Eventually(() => TrayDesktop.Get(() => shown.Listing.Items.Count == 4));

        CollectionAssert.AreEqual(bytes, deleted.Select(item => File.ReadAllBytes(item.FilePath)).ToList(), "back in place with the same bytes");
        Assert.IsTrue(deleted.All(item => RecycleBinEntries.Of(item.FilePath).Count == 0), "the bin's entries are gone, $R and $I");
        Assert.IsEmpty(Directory.GetFiles(work));
        Assert.AreEqual(0L, shown.Uses(), "neither records a use (G10)");
        TrayDesktop.Run(() => Assert.IsTrue(shown.Notices.Shown.All(n => n.Text.StartsWith("Added ")), "nothing failed"));
    }

    [TestMethod]
    public async Task TheMenusDeleteSendsTheItemsItWasOpenedOnToTheRecycleBinAndUndoBringsThemBack()
    {
        using var shown = ShownWindow.Open();
        var items = await shown.OpenStash(shown.Picture("menu delete one.png"), shown.Picture("menu delete two.png"), shown.Picture("menu delete kept.png"));
        TrayDesktop.Run(() => shown.Listing.Select([items[0].Id, items[1].Id], items[0].Id));

        var labels = TrayDesktop.Get(() => shown.RightClick(items[1].Id));
        TrayDesktop.Run(() =>
        {
            shown.Listing.Select([items[2].Id], items[2].Id);
            shown.ClickMenuEntry("Delete 2 items");
        });

        CollectionAssert.AreEqual(new[] { "Copy", "Add to Favorites", "Add tag", "Delete 2 items" }, labels);
        await Eventually(() => TrayDesktop.Get(() => shown.Notices.Shown[^1].Text.StartsWith("Deleted ")));
        TrayDesktop.Run(() => CollectionAssert.AreEqual(new[] { items[2].Id }, shown.Listing.Items.Select(item => item.Id).ToList(),
            "the two the menu was opened on, not the selection made since"));
        Assert.IsTrue(items.Take(2).All(item => RecycleBinEntries.Of(item.FilePath).Count == 1));

        TrayDesktop.Run(() => _ = shown.Notices.Undo(shown.Notices.Shown[^1]));
        await Eventually(() => TrayDesktop.Get(() => shown.Listing.Items.Count == 3));

        Assert.IsTrue(items.All(item => File.Exists(item.FilePath)));
        Assert.IsTrue(items.Take(2).All(item => RecycleBinEntries.Of(item.FilePath).Count == 0));
        TrayDesktop.Run(() => shown.CloseMenu());
    }

    [TestMethod]
    public async Task CtrlDInTheGridMakesAMixedSelectionAllFavoritesThenNoneAndInTheBoxChangesNothing()
    {
        using var shown = ShownWindow.Open();
        var items = await shown.OpenStash(shown.Picture("fav one.png"), shown.Picture("fav two.png"));
        await shown.Listing.Store!.SetFavorite([items[0]], true);
        await Dispatcher.UIThread.InvokeAsync(() => shown.Listing.Refresh());
        var noticesBefore = TrayDesktop.Get(() => shown.Notices.Shown.Count);
        var menuLabel = TrayDesktop.Get(() =>
        {
            shown.Listing.Select(items.Select(item => item.Id), items[0].Id);
            return shown.RightClick(items[0].Id)[1];
        });
        TrayDesktop.Run(() => shown.CloseMenu());

        TrayDesktop.Run(() =>
        {
            shown.Grid.Focus();
            ShownWindow.Press(shown.Grid, Key.D, KeyModifiers.Control);
        });
        await Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled && shown.Listing.Items.All(item => item.Favorite)));
        TrayDesktop.Run(() =>
        {
            CollectionAssert.AreEquivalent(items.Select(item => item.Id).ToList(), shown.Listing.Selection.ToList(), "the selection kept");
            Assert.AreEqual("Remove from Favorites", shown.RightClick(items[0].Id)[1], "the menu follows the store's answer");
            shown.CloseMenu();
            shown.Grid.Focus();
            ShownWindow.Press(shown.Grid, Key.D, KeyModifiers.Control);
        });
        await Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled && shown.Listing.Items.All(item => !item.Favorite)));

        TrayDesktop.Run(() =>
        {
            shown.Search.Focus();
            shown.Search.Text = "fav";
            ShownWindow.Press(shown.Search, Key.D, KeyModifiers.Control);
        });
        await Task.Delay(300);
        await Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled));

        Assert.AreEqual("Add to Favorites", menuLabel, "a mixed selection is offered Add (G17)");
        TrayDesktop.Run(() =>
        {
            Assert.IsTrue(shown.Listing.Items.All(item => !item.Favorite), "Ctrl+D in the box acts on nothing (G7)");
            Assert.AreEqual("fav", shown.Search.Text, "and leaves the box's text as it was");
            Assert.HasCount(noticesBefore, shown.Notices.Shown, "nothing said");
        });
        Assert.AreEqual(0, shown.Uses(), "favoriting is not a use (G10)");
    }

    [TestMethod]
    public async Task DelInTheSearchBoxDeletesACharacterAndNothingElseWhileInTheGridItDeletesTheSelection()
    {
        using var shown = ShownWindow.Open();
        var item = (await shown.OpenStash(shown.Picture("del box cat.png"))).Single();
        TrayDesktop.Run(() =>
        {
            shown.Search.Focus();
            shown.Search.Text = "cat";
            shown.Search.CaretIndex = 3;
        });
        await Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled && shown.Listing.Selection.Contains(item.Id)));

        TrayDesktop.Run(() => ShownWindow.Press(shown.Search, Key.Delete));
        TrayDesktop.Run(() => Assert.AreEqual("cat", shown.Search.Text, "nothing to the right of the caret"));
        TrayDesktop.Run(() =>
        {
            shown.Search.CaretIndex = 0;
            ShownWindow.Press(shown.Search, Key.Delete);
        });
        await Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled && shown.Listing.Query == "at"));

        TrayDesktop.Run(() =>
        {
            Assert.AreEqual("at", shown.Search.Text, "Del deleted the character after the caret (G7)");
            CollectionAssert.AreEqual(new[] { item.Id }, shown.Listing.Items.Select(i => i.Id).ToList(), "and nothing was deleted");
            Assert.IsTrue(shown.Notices.Shown.All(n => n.Text.StartsWith("Added ")));
        });
        Assert.IsTrue(File.Exists(item.FilePath));
        Assert.IsEmpty(RecycleBinEntries.Of(item.FilePath));

        TrayDesktop.Run(() =>
        {
            shown.Grid.Focus();
            ShownWindow.Press(shown.Grid, Key.Delete);
        });
        await Eventually(() => TrayDesktop.Get(() => shown.Notices.Shown[^1].Text.StartsWith("Deleted ")));
        TrayDesktop.Run(() => Assert.AreEqual("at", shown.Search.Text, "the grid's Del leaves the box's text as it was"));
        Assert.HasCount(1, RecycleBinEntries.Of(item.FilePath));

        TrayDesktop.Run(() => _ = shown.Notices.Undo(shown.Notices.Shown[^1]));
        await Eventually(() => File.Exists(item.FilePath) && RecycleBinEntries.Of(item.FilePath).Count == 0);
    }

    [TestMethod]
    public async Task DownAndEnterPressedWhileASearchIsPendingHandOverTheRowDownReachedInItsMatches()
    {
        using var shown = ShownWindow.Open();
        var paths = Enumerable.Range(0, 24).Select(index => shown.Picture($"cat {index:D2}.png", 20, 20,
            (_, _) => new SKColor((byte)(index * 8), 30, 70))).ToArray();
        await shown.OpenStash(paths);
        await shown.AddTo("dog.png");
        await TrayDesktop.Get(() => shown.Listing.SetQuery("cat"));
        await Eventually(() => TrayDesktop.Get(() => shown.Grid.Rows.Sum(row => row.Tiles.Count) == 24));
        // Down with the matches settled and laid out: the row below the first match, in the grid as it lays them out.
        var expected = TrayDesktop.Get(() =>
        {
            shown.Search.Focus();
            ShownWindow.Press(shown.Search, Key.Down);
            return shown.Listing.SelectedItems.Single();
        });
        Assert.AreNotEqual(TrayDesktop.Get(() => shown.Listing.Items[0].Id), expected.Id, "Down moved off the first match");
        await TrayDesktop.Get(() => shown.Listing.SetQuery(""));
        await Eventually(() => TrayDesktop.Get(() => shown.Grid.Rows.Sum(row => row.Tiles.Count) == 25));

        using (shown.HoldStore())
        {
            TrayDesktop.Run(() =>
            {
                shown.Search.Text = "cat";
                Assert.IsFalse(shown.Listing.Settled, "the search is still pending when Down and Enter are pressed");
                ShownWindow.Press(shown.Search, Key.Down);
                ShownWindow.Press(shown.Search, Key.Enter);
            });
        }

        await Eventually(() => shown.Uses() == 1);
        var files = TestClipboard.Read().Files.ToList();
        CollectionAssert.AreEqual(new[] { expected.FilePath }, files,
            $"the row Down reached in the search's matches, {expected.FileName}, handed over (G5, G7, G8): {string.Join(", ", files.Select(Path.GetFileName))}");
    }

    [TestMethod]
    public async Task RightSpaceEscPressedWhileTheListingReloadsOpenTheItemRightReachedThenCloseItInTheOrderPressed()
    {
        using var shown = ShownWindow.Open();
        var items = await shown.OpenStash(shown.Picture("order one.png"), shown.Picture("order two.png"));
        TrayDesktop.Run(() => shown.Grid.Focus());

        using (shown.HoldStore())
        {
            TrayDesktop.Run(() =>
            {
                _ = shown.Listing.Refresh();
                Assert.IsFalse(shown.Listing.Settled, "the reload is still pending when the keys are pressed");
                ShownWindow.Press(shown.Grid, Key.Right);
                ShownWindow.Press(shown.Grid, Key.Space);
                ShownWindow.Press(shown.Grid, Key.Escape);
            });
        }

        await Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled));
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        TrayDesktop.Run(() =>
        {
            Assert.AreEqual(items[0].Id, shown.Listing.Focused, "Right reached the second tile");
            Assert.IsFalse(shown.Preview.IsOpen, "Esc closed the preview Space opened, as pressed (G2)");
            Assert.IsTrue(shown.Window.IsVisible, "and went no further: Esc closes the preview first (G2)");
        });
    }

    /// <summary>The footer's busy words while one shows, else null.</summary>
    private static string? BusyText(ShownWindow shown) =>
        shown.Window.GetVisualDescendants().OfType<ProgressBar>().SingleOrDefault(bar => bar.IsEffectivelyVisible && bar.Parent is StackPanel)?.Parent is StackPanel row
            ? row.Children.OfType<TextBlock>().Single().Text
            : null;

    private static List<ImportedFile> Added(int count, string prefix = "") =>
        Enumerable.Range(0, count).Select(i => new ImportedFile($"{prefix}reaction {i:000}.png", ImportResultKind.Added)).ToList();
}
