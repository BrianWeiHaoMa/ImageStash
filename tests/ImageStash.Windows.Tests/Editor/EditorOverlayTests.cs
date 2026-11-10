using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.VisualTree;
using ImageStash.Editor;
using ImageStash.Layers;
using ImageStash.Seams;
using ImageStash.Store;
using ImageStash.Window;
using ImageStash.Windows.Tests.Support;
using SkiaSharp;

namespace ImageStash.Windows.Tests.Editor;

/// <summary>
/// The editor inside the stash window laid out by the real desktop platform (G20, F4, G6, G7): opened by <c>Ctrl+E</c>
/// in the search box and the grid, the item menu's Edit, the preview's Edit and its ⋯ Edit, on the one selected item and
/// on nothing with several; inside the window, never a window of its own; its head, tool row, option row and footer;
/// the box, the chips and the footer's hints out of sight and reach and the grid covered; a transparent picture on the
/// checkerboard; the tools and every row of choices a radio, a second click on the current one leaving it shown chosen;
/// a colour being picked from the picture ended by another tool or another layer selected, not by its picker closing.
/// </summary>
[TestClass]
public sealed partial class EditorOverlayTests
{
    [TestMethod]
    public async Task CtrlEInTheBoxAndTheGridTheItemMenuAndThePreviewsEditOpenTheEditorInsideTheWindowOnTheOneSelectedItem()
    {
        using var shown = ShownWindow.Open();
        using var editor = new ShownEditor(shown);
        var items = await shown.OpenStash(shown.Fixture("transparent.png", "transparent.png"), shown.Fixture("still.png", "still.png"));
        var (transparent, still) = (items[0], items[1]);
        var windowsBefore = ProcessWindows.Visible();

        // With two selected, Ctrl+E opens nothing and says nothing, and the menu offers no Edit (G20).
        TrayDesktop.Run(() =>
        {
            shown.Listing.Select([transparent.Id, still.Id], still.Id);
            shown.Grid.Focus();
            ShownWindow.Press(shown.Grid, Key.E, KeyModifiers.Control);
        });
        await Task.Delay(300);
        TrayDesktop.Run(() =>
        {
            Assert.IsFalse(shown.Editing.IsOpen, "unavailable with several selected, the focused one included");
            Assert.AreEqual("", shown.Said(), "named in no notice");
            CollectionAssert.DoesNotContain(shown.RightClick(still.Id), "Edit", "the two-item menu lists no Edit");
            shown.CloseMenu();
        });

        // Ctrl+E in the search box.
        TrayDesktop.Run(() =>
        {
            shown.Listing.Focus(transparent.Id);
            shown.Search.Focus();
            ShownWindow.Press(shown.Search, Key.E, KeyModifiers.Control);
        });
        await OpenOn(shown, editor, transparent);
        Assert.AreEqual(windowsBefore, ProcessWindows.Visible(), "no window of its own (F4)");
        await Cancelled(shown);

        // Ctrl+E in the grid.
        TrayDesktop.Run(() =>
        {
            shown.Listing.Focus(still.Id);
            shown.Grid.Focus();
            ShownWindow.Press(shown.Grid, Key.E, KeyModifiers.Control);
        });
        await OpenOn(shown, editor, still);
        await Cancelled(shown);

        // The item menu's Edit, after Preview.
        TrayDesktop.Run(() =>
        {
            shown.Listing.Focus(transparent.Id);
            var entries = shown.RightClick(transparent.Id);
            Assert.AreEqual(entries.IndexOf("Preview") + 1, entries.IndexOf("Edit"), string.Join(", ", entries));
            shown.ClickMenuEntry("Edit");
            shown.CloseMenu();
        });
        await OpenOn(shown, editor, transparent);
        await Cancelled(shown);

        // The preview's Edit button, on the preview's item whatever the grid beneath selects.
        TrayDesktop.Run(() =>
        {
            shown.OpenPreview(still);
            shown.Listing.Select([transparent.Id, still.Id], transparent.Id);
            ShownWindow.RaiseClick(shown.EditButton);
        });
        await OpenOn(shown, editor, still);
        await Cancelled(shown);

        // The preview's ⋯ Edit.
        TrayDesktop.Run(() =>
        {
            Assert.IsTrue(shown.Preview.IsOpen, "the cancelled edit returned to the preview it started from");
            Assert.AreEqual("Edit", shown.ClickMore()[0]);
            shown.ClickMenuEntry("Edit");
            shown.CloseMenu();
        });
        await OpenOn(shown, editor, still);
        Assert.AreEqual(windowsBefore, ProcessWindows.Visible(), "no window of its own (F4)");
    }

    [TestMethod]
    public async Task ATransparentPictureShowsOnTheCheckerboard()
    {
        using var shown = ShownWindow.Open();
        using var editor = new ShownEditor(shown);
        // The left half fully transparent, wider than two of the checkerboard's eight-unit squares.
        var items = await shown.OpenStash(shown.Picture("half clear.png", 64, 48, (x, _) => x < 32 ? SKColors.Transparent : new SKColor(0, 160, 0)));
        await Open(shown, items[0]);

        var rendered = await RenderedWindow.Of(shown);
        var (picture, dark) = TrayDesktop.Get(() => (editor.PictureInRendering(), shown.Window.ActualThemeVariant == ThemeVariant.Dark));
        var left = rendered.Colors(picture.X + 1, picture.Y + 1, picture.Width / 2 - 2, picture.Height - 2);
        var right = rendered.Colors(picture.X + picture.Width / 2 + 1, picture.Y + 1, picture.Width / 2 - 2, picture.Height - 2);

        var squares = dark ? new[] { 0xFF3A3B42u, 0xFF2E2F35u } : new[] { 0xFFFFFFFFu, 0xFFE6E7ECu };
        var commonest = rendered.Commonest(new Rect(picture.X + 1, picture.Y + 1, picture.Width / 2 - 2, picture.Height - 2), 2);
        CollectionAssert.AreEquivalent(squares, commonest, $"G6: the checkerboard's two colours show through, the most of any: {string.Join(", ", rendered.ColorCounts(picture.X + 1, picture.Y + 1, picture.Width / 2 - 2, picture.Height - 2).OrderByDescending(pair => pair.Value).Take(5).Select(pair => $"{pair.Key:X8} × {pair.Value}"))}");
        // A square's edge falling between two display pixels at a fractional scale blends the two; nothing else shows.
        Assert.IsTrue(left.All(color => RenderedWindow.Between(color, squares[0], squares[1])), $"every other colour a blend of the two: {string.Join(", ", left.Select(color => color.ToString("X8")))}");
        CollectionAssert.AreEquivalent(new[] { 0xFF00A000u }, right.ToList(), "the opaque half as it is");
    }

    [TestMethod]
    public async Task ASecondClickOnTheCurrentToolPresetMarkThicknessOrSwatchLeavesItShownChosenWhileOneToOneStillTurnsOff()
    {
        using var shown = ShownWindow.Open();
        using var editor = new ShownEditor(shown);
        var items = await shown.OpenStash(shown.Fixture("still.png", "still.png"));
        await Open(shown, items[0]);

        // Text, current at open, clicked again: still chosen, its option row still Text's (G20, G21).
        await ClickedStaysChosen(shown, editor, "Text");
        TrayDesktop.Run(() => Assert.IsTrue(editor.Shows("Meme") && editor.Shows("Caption bar") && editor.Shows("Label"), "Text's option row stays"));
        await ClickedStaysChosen(shown, editor, TrayDesktop.Get(() => TheChosen(editor, "Meme", "Caption bar", "Label")));

        // Draw, clicked once, is chosen by the session's answer and Text no longer shows chosen.
        await ClickReaching(shown, editor, "Draw");
        TrayDesktop.Run(() => Assert.IsTrue(Chosen(editor, "Draw") && !Chosen(editor, "Text") && editor.Shows("Pen"), "Draw chosen, its option row shown"));
        await ClickedStaysChosen(shown, editor, TrayDesktop.Get(() => TheChosen(editor, "Pen", "Marker", "Arrow", "Box")));
        await ClickedStaysChosen(shown, editor, TrayDesktop.Get(() => TheChosen(editor, "Thin", "Medium", "Thick")));
        await ClickedStaysChosen(shown, editor, TrayDesktop.Get(() => TheChosen(editor, [.. ColorChoices.Swatches.Select(ColorChoices.Hex)])));

        // Crop's 1 : 1 is an on and off, not a choice among others: a second click turns it off (G22).
        await ClickReaching(shown, editor, "Crop");
        TrayDesktop.Run(() => Assert.IsTrue(Chosen(editor, "Crop") && !Chosen(editor, "1:1"), "Crop chosen, 1 : 1 off"));
        await ClickReaching(shown, editor, "1:1");
        TrayDesktop.Run(() => Assert.IsTrue(Chosen(editor, "1:1"), "1 : 1 on"));
        await ClickReaching(shown, editor, "1:1");
        TrayDesktop.Run(() => Assert.IsTrue(!Chosen(editor, "1:1") && Chosen(editor, "Crop"), "1 : 1 off again, Crop still chosen"));
    }

    /// <summary>A real click on the overlay's chosen button of <paramref name="words"/> leaves it chosen.</summary>
    private static async Task ClickedStaysChosen(ShownWindow shown, ShownEditor editor, string words)
    {
        TrayDesktop.Run(() => Assert.IsTrue(Chosen(editor, words), $"{words} chosen before the click"));
        await ClickReaching(shown, editor, words);
        TrayDesktop.Run(() => Assert.IsTrue(Chosen(editor, words), $"{words} still shown chosen after a second click (G20, G21)"));
    }

    /// <summary>
    /// A click, its press and its release, on the overlay's button of <paramref name="words"/> once the button can be hit
    /// where it lies, as a row a click just built cannot be until it is drawn; fails unless the button saw the click.
    /// </summary>
    internal static async Task ClickReaching(ShownWindow shown, ShownEditor editor, string words)
    {
        await ShownWindow.Eventually(() => TrayDesktop.Get(() =>
        {
            var button = editor.Button(words);
            return button.GetVisualsAt(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2)).Any(hit => hit == button || button.IsVisualAncestorOf(hit));
        }));
        TrayDesktop.Run(() =>
        {
            var button = editor.Button(words);
            var clicks = 0;
            void Counted(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => clicks++;
            button.Click += Counted;
            shown.Click(button);
            button.Click -= Counted;
            Assert.AreEqual(1, clicks, $"the click reached {words}");
        });
    }

    private static bool Chosen(ShownEditor editor, string words) => editor.Button(words) is ToggleButton { IsChecked: true };

    /// <summary>The one of <paramref name="words"/> shown chosen.</summary>
    private static string TheChosen(ShownEditor editor, params string[] words) => words.Single(each => Chosen(editor, each));

    [TestMethod]
    public async Task TheEditorsKeysActEnterPastesNothingCtrlCCopiesTheItemAsLastSavedAndTheGridsKeysDoNothing()
    {
        var target = new PresetTarget { Before = null };
        using var shown = ShownWindow.Open(target: target);
        using var editor = new ShownEditor(shown);
        var items = await shown.OpenStash(shown.Picture("checker.png", 64, 48, EditorCanvasTests.Checker), shown.Picture("other.png"));
        var checker = items.Single(item => item.Name == "checker");
        TrayDesktop.Run(() =>
        {
            shown.Window.Hide();
            shown.Window.SummonFromShortcut(new ForegroundReading(false, PresetTarget.App("Relay")), new SummonAnchor(null));
        });
        await TrayDesktop.Get(() => shown.Listing.Settle());
        await Open(shown, checker);
        TestClipboard.PutText("as it was");

        TrayDesktop.Run(() =>
        {
            editor.Choose("Hide");
            editor.Choose("Black box");
            editor.Drag((0.25, 0.25), (0.75, 0.75));
            var drawn = editor.Session!.Layers;
            ShownWindow.Press(editor.Canvas, Key.Z, KeyModifiers.Control);
            Assert.IsTrue(editor.Session.Layers.IsEmpty, "Ctrl+Z takes the hide away (G25)");
            ShownWindow.Press(editor.Canvas, Key.Y, KeyModifiers.Control);
            Assert.AreEqual(drawn, editor.Session.Layers, "Ctrl+Y puts it back");
            Assert.AreEqual(0, editor.Session.Selected);
            ShownWindow.Press(editor.Canvas, Key.Escape);
            Assert.IsNull(editor.Session.Selected, "Esc with a layer selected deselects it");
            Assert.IsTrue(shown.Editing.IsOpen, "and leaves the editor open");

            // The grid's keys reach nothing of the grid's while the editor covers it (G20, G7).
            ShownWindow.Press(editor.Canvas, Key.Space);
            ShownWindow.Press(editor.Canvas, Key.Delete);
            ShownWindow.Press(editor.Canvas, Key.F2);
            ShownWindow.Press(editor.Canvas, Key.D, KeyModifiers.Control);
            ShownWindow.Press(editor.Canvas, Key.A, KeyModifiers.Control);
            ShownWindow.Press(editor.Canvas, Key.Enter);
        });
        await Task.Delay(300);
        TrayDesktop.Run(() =>
        {
            Assert.IsEmpty(target.Pasted, "Enter never pastes in the editor (G20, N3)");
            Assert.IsTrue(shown.Window.IsVisible && shown.Editing.IsOpen, "the window and the editor stay");
            Assert.IsFalse(shown.Preview.IsOpen, "Space opened no preview");
            Assert.AreEqual(1, drawnCount(), "Del with nothing selected removed nothing");
            Assert.DoesNotContain("Deleted", shown.Said(), "nothing deleted");
            Assert.IsFalse(shown.Listing.Items.Single(item => item.Id == checker.Id).Favorite, "Ctrl+D left the favorite flag");
            CollectionAssert.AreEqual(new[] { checker.Id }, shown.Listing.Selection.ToList(), "Ctrl+A selected nothing more");
            Assert.AreEqual("checker", shown.Listing.Items.Single(item => item.Id == checker.Id).Name, "F2 renamed nothing");
        });
        Assert.IsEmpty(TestClipboard.Read().Files, "the clipboard as it was");

        // Ctrl+C copies the item as last saved, its one file before any Save, and the window stays (G9).
        TrayDesktop.Run(() => ShownWindow.Press(editor.Canvas, Key.C, KeyModifiers.Control));
        await ShownWindow.Eventually(() => TestClipboard.Read().Files.SequenceEqual([checker.FilePath]));
        TrayDesktop.Run(() => Assert.IsTrue(shown.Window.IsVisible && shown.Editing.IsOpen));

        int drawnCount() => editor.Session!.Layers.Layers.Count;
    }

    [TestMethod]
    public async Task WithTheEditorsMenuOpenCtrlZAndCtrlSStillUndoAndSaveAndEscClosesOnlyTheMenu()
    {
        using var shown = ShownWindow.Open();
        using var editor = new ShownEditor(shown);
        var item = (await shown.OpenStash(shown.Picture("checker.png", 64, 48, EditorCanvasTests.Checker))).Single();
        await Open(shown, item);
        TrayDesktop.Run(() =>
        {
            editor.Choose("Hide");
            editor.Choose("Black box");
            editor.Drag((0.25, 0.25), (0.75, 0.75));
            editor.ClickMore();
            var entry = editor.OpenMenu!.Items.OfType<MenuItem>().First();
            ShownWindow.Press(entry, Key.Escape);
            Assert.IsTrue(shown.Editing.IsOpen, "Esc in the menu is the menu's, never the edit's Cancel");
            editor.ClickMore();
            ShownWindow.Press(editor.OpenMenu!.Items.OfType<MenuItem>().First(), Key.Z, KeyModifiers.Control);
            Assert.IsTrue(editor.Session!.Layers.IsEmpty, "G25: Ctrl+Z undoes with the ⋯ menu open");
            Assert.IsFalse(editor.OpenMenu?.IsOpen ?? false, "and the menu steps aside");
            ShownWindow.Press(editor.Canvas, Key.Y, KeyModifiers.Control);
            editor.ClickMenuButton(EditorOverlay.OutputSize);
            ShownWindow.Press(editor.OpenMenu!.Items.OfType<MenuItem>().First(), Key.S, KeyModifiers.Control);
        });
        // The edit closes once the store has published it; the return follows once the listing is read again.
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => !shown.Editing.IsOpen && shown.Grid.IsFocused));
        Assert.AreEqual(1, TrayDesktop.Get(() => shown.Listing.Items.Single().RenderedRevision), "G25: Ctrl+S saves with the size chip's menu open");
    }

    [TestMethod]
    public async Task CompareHeldByCEndsWhenTheWindowLosesTheForegroundWhereItsReleaseIsNeverSeen()
    {
        using var shown = ShownWindow.Open();
        using var editor = new ShownEditor(shown);
        var item = (await shown.OpenStash(shown.Picture("checker.png", 64, 48, EditorCanvasTests.Checker))).Single();
        await Open(shown, item);

        TrayDesktop.Run(() => ShownWindow.Press(editor.Canvas, Key.C));
        TrayDesktop.Run(() => Assert.IsTrue(editor.Session!.Compare, "C held shows the original"));
        TrayDesktop.Run(shown.Deactivate);

        TrayDesktop.Run(() => Assert.IsFalse(editor.Session!.Compare, "G20: Compare shows the original only while held, and a C let go in another app is never seen"));
    }

    [TestMethod]
    public async Task EscWithNothingSelectedCancelAndTheBackArrowLeaveTheItemAsItWasWithANoticeWhoseUndoBringsTheEditBack()
    {
        using var shown = ShownWindow.Open();
        using var editor = new ShownEditor(shown);
        var items = await shown.OpenStash(shown.Picture("checker.png", 64, 48, EditorCanvasTests.Checker));
        var checker = items[0];
        await Open(shown, checker);
        TrayDesktop.Run(() =>
        {
            editor.Choose("Hide");
            editor.Choose("Black box");
            editor.Drag((0.25, 0.25), (0.75, 0.75));
            ShownWindow.Press(editor.Canvas, Key.Escape);
            ShownWindow.Press(editor.Canvas, Key.Escape);
        });
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => !shown.Editing.IsOpen));

        var notice = TrayDesktop.Get(() =>
        {
            Assert.IsTrue(shown.Grid.IsFocused, "back to the grid, where Edit was started");
            CollectionAssert.AreEqual(new[] { checker.Id }, shown.Listing.Selection.ToList(), "the item selected");
            Assert.IsTrue(shown.Listing.Items.Single().Facts.Width == 64 && shown.Listing.Items.Single().RenderedRevision == 0, "the item as it was");
            return shown.Notices.Shown.Single(each => each.Text == "Discarded the edit of “checker”.");
        });
        Assert.AreEqual("Undo", notice.UndoLabel);
        await TrayDesktop.Get(() => shown.Notices.Undo(notice));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Editing.IsOpen));
        TrayDesktop.Run(() =>
        {
            Assert.IsTrue(editor.Session!.Layers.Layers.Single() is HideLayer { Kind: HideKind.Black }, "the discarded edit back (G20)");
            editor.Click(0.5, 0.5);
            Assert.AreEqual(0, editor.Session.Selected, "live and selectable");
            editor.Choose("Cancel");
        });
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => !shown.Editing.IsOpen));
        Assert.AreEqual(1, TrayDesktop.Get(() => shown.Notices.Shown.Count(each => each.Text == "Discarded the edit of “checker”.")), "the first notice left with its Undo, and Cancel put up one of its own");

        await Open(shown, checker);
        TrayDesktop.Run(() => editor.Choose("Back"));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => !shown.Editing.IsOpen));
        Assert.AreEqual(2, TrayDesktop.Get(() => shown.Notices.Shown.Count(each => each.Text == "Discarded the edit of “checker”.")), "and so does the back arrow, whether or not anything changed");
    }

    [TestMethod]
    public async Task ALongNameEndsWithAnEllipsisClearOfCompareAndMoreAtTheMinimumWidthAndAShortOneShowsWholeBesideEditing()
    {
        using var shown = ShownWindow.Open();
        using var editor = new ShownEditor(shown);
        var longName = string.Concat(Enumerable.Repeat("a very long name ", 8))[..120];
        var items = await shown.OpenStash(shown.Picture(longName + ".png"), shown.Picture("short.png"));
        TrayDesktop.Run(() =>
        {
            shown.Window.Width = 1;
            shown.Window.Height = 1;
        });
        await TrayDesktop.Get(() => shown.Editing.Open(items[0], EditOrigin.Grid));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Editing.Session?.Item.Id == items[0].Id));

        TextBlock Title(string words) => editor.Overlay.GetVisualDescendants().OfType<TextBlock>().First(text => text.IsEffectivelyVisible && text.Text == words);
        double RightOf(Control control) => control.TranslatePoint(new Point(control.Bounds.Width, 0), editor.Overlay)!.Value.X;
        double LeftOf(Control control) => control.TranslatePoint(default, editor.Overlay)!.Value.X;
        TrayDesktop.Run(() =>
        {
            shown.Window.UpdateLayout();
            var name = Title(longName);
            var natural = new TextBlock { Text = longName, FontSize = name.FontSize, FontWeight = name.FontWeight };
            natural.Measure(Size.Infinity);
            Assert.IsLessThan(natural.DesiredSize.Width, name.Bounds.Width, "the name is cut, which its CharacterEllipsis shows as … (G21)");
            Assert.AreEqual(longName, ToolTip.GetTip(name), "and its tip holds it whole");
            var editing = Title("editing");
            var compare = editor.Button("Compare");
            var more = editor.Button("More options");
            Assert.IsLessThanOrEqualTo(LeftOf(editing), RightOf(name), "the name ends before editing");
            Assert.IsLessThanOrEqualTo(LeftOf(compare), RightOf(editing), "editing ends before Compare");
            Assert.IsLessThanOrEqualTo(editor.Overlay.Bounds.Width, RightOf(more), "⋯ inside the window at its minimum width (G20)");
            ShownWindow.Press(editor.Canvas, Key.Escape);
        });
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => !shown.Editing.IsOpen));

        await TrayDesktop.Get(() => shown.Editing.Open(items[1], EditOrigin.Grid));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Editing.Session?.Item.Id == items[1].Id));
        TrayDesktop.Run(() =>
        {
            shown.Window.UpdateLayout();
            var (name, editing) = (Title("short"), Title("editing"));
            Assert.AreEqual(8, LeftOf(editing) - RightOf(name), 1, "a short name shows whole with editing beside it");
        });
    }

    [TestMethod]
    public async Task WhileASaveRunsItsBusyWordsCancelTheOutputSizeAndSaveLieInsideTheWindowAtItsMinimum()
    {
        using var shown = ShownWindow.Open();
        using var editor = new ShownEditor(shown);
        // Named as a screenshot is (F12), and large enough that its Save still runs when the window is read next.
        const string name = "Screenshot 2026-10-23 14.05.33 of the whole conversation";
        var item = (await shown.OpenStash(shown.Picture(name + ".png", 2400, 1800, (x, y) => new SKColor((byte)x, (byte)y, 90)))).Single();
        TrayDesktop.Run(() =>
        {
            shown.Window.Width = 1;
            shown.Window.Height = 1;
        });
        await Open(shown, item);
        TrayDesktop.Run(() =>
        {
            editor.Choose("Draw");
            editor.Choose("Box");
            editor.Drag((0.2, 0.2), (0.8, 0.8));
            ShownWindow.Press(editor.Canvas, Key.S, KeyModifiers.Control);
        });

        TrayDesktop.Run(() =>
        {
            Assert.IsNotNull(shown.Editing.Busy, "the Save still runs");
            shown.Window.UpdateLayout();
            var window = shown.Window.Bounds;
            WindowMinimum.AssertAt(new Size(window.Width, window.Height), "the window at its minimum");
            var words = editor.Overlay.GetVisualDescendants().OfType<TextBlock>().Single(text => text.IsEffectivelyVisible && (text.Text ?? "").StartsWith("Saving “", StringComparison.Ordinal));
            foreach (var control in new Control[] { words, editor.Button("Cancel"), editor.Button(EditorOverlay.OutputSize), editor.Button("Save") })
            {
                var at = control.TranslatePoint(default, shown.Window)!.Value;
                Assert.IsTrue(at.X >= 0 && at.Y >= 0 && at.X + control.Bounds.Width <= window.Width + 0.5 && at.Y + control.Bounds.Height <= window.Height + 0.5,
                    $"{AutomationProperties.GetName(control) ?? words.Text} at {new Rect(at, control.Bounds.Size)} inside the {window.Size} window while the Save runs (G20, G35)");
            }

            Assert.IsGreaterThanOrEqualTo($"Saving “{name[..30]}".Length, words.TextLayout.TextLines[0].Length, "the words show which Save runs (G35)");
        });
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => !shown.Editing.IsOpen));
    }

    [TestMethod]
    public async Task AColourBeingPickedOutlivesItsPickersClosingAndEndsWhenAnotherToolOrLayerIsChosenSoTheNextDragDraws()
    {
        using var shown = ShownWindow.Open();
        using var editor = new ShownEditor(shown);
        var item = (await shown.OpenStash(shown.Picture("grey.png", 400, 300, (_, _) => TextFieldTests.Grey))).Single();
        await Open(shown, item);
        TrayDesktop.Run(() =>
        {
            editor.Click(0.5, 0.2);
            ShownWindow.Type(editor.Field!, "hi");
            ShownWindow.Press(editor.Field!, Key.Enter, KeyModifiers.Control);
            shown.Window.UpdateLayout();
            PickFromThePicture(shown, editor);
            Assert.IsTrue(editor.Canvas.Picking, "the picker closed as the pick began, and the pick stands (G21)");

            editor.Choose("Draw");
            Assert.IsFalse(editor.Canvas.Picking, "another tool ends it");
            editor.Drag((0.1, 0.7), (0.8, 0.7));
            Assert.HasCount(1, editor.Session!.Layers.Layers.OfType<DrawMarkLayer>(), "so the first drag after choosing Draw draws (G23)");
            Assert.AreEqual(1, editor.Session.Selected);

            editor.Drag((0.1, 0.9), (0.8, 0.9));
            Assert.AreEqual(2, editor.Session.Selected);
            shown.Window.UpdateLayout();
            PickFromThePicture(shown, editor);
            Assert.IsTrue(editor.Canvas.Picking);
            // Ctrl+Z takes the second mark back and selects the first again, Draw still current.
            ShownWindow.Press(editor.Canvas, Key.Z, KeyModifiers.Control);
            Assert.AreEqual(1, editor.Session.Selected);
            Assert.IsInstanceOfType<DrawTool>(editor.Session.Tool);
            Assert.IsFalse(editor.Canvas.Picking, "another layer selected, under the same tool, ends it, so its colour reaches no layer it was not picked for");
        });
    }

    /// <summary>More colors opened from the option row, then its Pick from the picture clicked.</summary>
    private static void PickFromThePicture(ShownWindow shown, ShownEditor editor)
    {
        editor.Choose("More colors");
        shown.Window.UpdateLayout();
        var picker = editor.Overlay.GetVisualDescendants().OfType<Control>().First(control => control.IsEffectivelyVisible && AutomationProperties.GetName(control) == "Color picker");
        ShownWindow.RaiseClick(picker.GetVisualDescendants().OfType<Button>().Single(button => AutomationProperties.GetName(button) == "Pick from the picture"));
    }

    /// <summary>Opens the editor on <paramref name="item"/> as Ctrl+E in the grid does, and waits for it.</summary>
    internal static async Task Open(ShownWindow shown, StashItem item)
    {
        TrayDesktop.Run(() =>
        {
            shown.Listing.Focus(item.Id);
            shown.Grid.Focus();
            ShownWindow.Press(shown.Grid, Key.E, KeyModifiers.Control);
        });
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Editing.Session?.Item.Id == item.Id));
        await Task.Delay(100);
    }

    /// <summary>Esc with nothing selected, which leaves the edit as Cancel does, and waits for the editor to close.</summary>
    internal static async Task Cancelled(ShownWindow shown)
    {
        TrayDesktop.Run(() => ShownWindow.Press((Control)shown.Focused!, Key.Escape));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => !shown.Editing.IsOpen));
    }

    /// <summary>Waits for the editor to open on <paramref name="item"/> and checks what it shows, and what it hides.</summary>
    private static async Task OpenOn(ShownWindow shown, ShownEditor editor, StashItem item)
    {
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Editing.Session?.Item.Id == item.Id));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => SizeWords().IsMatch(editor.Size())));
        TrayDesktop.Run(() =>
        {
            Assert.AreSame(shown.Window, TopLevel.GetTopLevel(editor.Overlay), "inside the stash window (F4)");
            Assert.IsTrue(editor.Overlay.IsEffectivelyVisible);
            var texts = editor.Texts();
            CollectionAssert.IsSubsetOf(new[] { item.Name, "editing", "Click the picture to add text" }, texts, string.Join(" | ", texts));
            Assert.IsTrue(editor.Shows("Compare") && editor.Shows("More options") && editor.Shows("Back"), "the head");
            CollectionAssert.AreEqual(new[] { "Text", "Crop", "Hide", "Draw" }, editor.Tools(), "the tools built, and nothing inert");
            Assert.IsTrue(editor.Shows("Meme") && editor.Shows("Caption bar") && editor.Shows("Label"), "Text's option row, Text current at open");
            Assert.IsTrue(editor.Shows("Cancel") && editor.Shows("Save"), "the footer");
            Assert.IsFalse(shown.Search.IsEffectivelyVisible, "the box is out of sight and reach");
            Assert.IsFalse(shown.ChipRow.IsEffectivelyVisible, "so are the chips");
            Assert.IsEmpty(shown.FooterHints(), "and the footer's hints");
            Assert.IsTrue(shown.Grid.Covered, "the grid plays nothing beneath (G6)");
            Assert.IsTrue(shown.Focused is Visual { IsEffectivelyVisible: true } focus && editor.Overlay.IsVisualAncestorOf(focus), "focus inside the editor, on what shows");
        });
    }

    [GeneratedRegex(@"^8 × 6 · \d+ KB$")]
    private static partial Regex SizeWords();
}
