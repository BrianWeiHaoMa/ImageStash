using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using ImageStash.Editor;
using ImageStash.Layers;
using ImageStash.Store;
using ImageStash.Windows.Seams;
using ImageStash.Windows.Tests.Support;

namespace ImageStash.Windows.Tests.Editor;

/// <summary>
/// The editor's pane on the desktop (G20, G21, G25, F4, F8): in the pinned window's default size the colour picker and
/// the All fonts… list stand beside the picture, inside the stash window, the picture re-fitting into the room left,
/// whatever the picture's size; narrowed to the flyout's width the pane floats over the editor, whole inside the window,
/// and widened it stands beside the picture again; a drag on the square or the hue strip colours the selected text or
/// mark on the picture at each move as no step, and its let-go is one step of Undo and one recent colour; a drag that
/// Esc, Ctrl+Z, Enter in the hex, a press elsewhere or a Shrink chosen ends puts its colour back and records nothing of its own; the font
/// list stays inside the picture's row when the window is shortened. Pointer events are raised on the editor's controls.
/// </summary>
[TestClass]
public sealed class EditorPaneTests
{
    private static readonly Rgba White = new(255, 255, 255, 255);

    [TestMethod]
    [DataRow(1600, 1200, DisplayName = "a picture larger than its room")]
    [DataRow(200, 150, DisplayName = "a picture smaller than its room, drawn at its own size")]
    public async Task AtThePinnedDefaultThePickerStandsBesideThePictureInsideTheWindowNeverOverIt(int width, int height)
    {
        using var shown = ShownWindow.Open();
        using var editor = new ShownEditor(shown);
        await TextSelected(shown, editor, PinnedDefault, width, height);
        var windowsBefore = ProcessWindows.Visible();

        await OpenFrom(shown, editor, "More colors");

        TrayDesktop.Run(() =>
        {
            var card = Card(editor)!;
            Assert.AreSame(shown.Window, TopLevel.GetTopLevel(card), "inside the stash window (F4)");
            Assert.AreEqual(windowsBefore, ProcessWindows.Visible(), "never a window of its own (F4)");
            Beside(shown, editor, card, "from More");
        });
    }

    [TestMethod]
    public async Task NarrowedToTheFlyoutsWidthThePickerFloatsWholeInsideTheWindowAndWidenedItStandsBesideThePictureAgain()
    {
        using var shown = ShownWindow.Open();
        using var editor = new ShownEditor(shown);
        await TextSelected(shown, editor, PinnedDefault, 1600, 1200);
        await OpenFrom(shown, editor, "Outline color");
        var hexBefore = TrayDesktop.Get(() => Hex(editor).Text);

        TrayDesktop.Run(() => shown.Window.Width = WindowSize.FlyoutDefault.Width);
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => Math.Abs(shown.Window.Bounds.Width - WindowSize.FlyoutDefault.Width) < 1 && Card(editor) is { } card && !Docked(card)));
        TrayDesktop.Run(() =>
        {
            shown.Window.UpdateLayout();
            var card = Card(editor)!;
            TextOptionRowTests.AssertInside(shown, card, "floating at the flyout's width (G20)");
            Assert.AreEqual(hexBefore, Hex(editor).Text, "still open, on the colour it showed (G21)");
        });

        TrayDesktop.Run(() => shown.Window.Width = WindowSize.PinnedDefault.Width);
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => Card(editor) is { } card && Docked(card)));
        TrayDesktop.Run(() =>
        {
            shown.Window.UpdateLayout();
            Beside(shown, editor, Card(editor)!, "widened again");
        });
    }

    [TestMethod]
    [DataRow("ValueSquare")]
    [DataRow("HueStrip")]
    public async Task ADragColoursTheTextAtEachMoveAsNoStepAndItsLetGoIsOneStepAndOneRecentColour(string surface)
    {
        using var shown = ShownWindow.Open();
        using var editor = new ShownEditor(shown);
        await TextSelected(shown, editor, PinnedDefault, 1600, 1200);
        await OpenFrom(shown, editor, "More colors");
        var points = new[] { (0.2, 0.3), (0.5, 0.6), (0.8, 0.2) };
        // The strip turns the hue alone, which a white does not carry: it starts from a colour of its own, one step.
        var seed = new Rgba(0xC0, 0x40, 0x20, 0xFF);

        TrayDesktop.Run(() =>
        {
            if (surface == "HueStrip")
            {
                var typed = Hex(editor);
                typed.Focus();
                typed.SelectAll();
                ShownWindow.Type(typed, ColorChoices.Hex(seed));
                ShownWindow.Press(typed, Key.Enter);
            }

            var start = Text(editor).Fill;
            var recentBefore = editor.Session!.Colors.Recent.ToList();
            var on = Surface(editor, surface);
            var shownColours = new List<Rgba>();
            foreach (var (x, y, i) in points.Select((point, i) => (point.Item1, point.Item2, i)))
            {
                var at = new Point(on.Bounds.Width * x, on.Bounds.Height * y);
                if (i == 0)
                {
                    editor.Press(on, at);
                }
                else
                {
                    editor.Move(on, at);
                }

                var hex = ColorChoices.ParseHex(Hex(editor).Text ?? "")!.Value;
                Assert.AreEqual(hex, Text(editor).Fill, $"after move {i}, the text in the colour the picker shows (G21)");
                shownColours.Add(hex);
            }

            Assert.AreEqual(3, shownColours.Distinct().Count(), "three colours shown as the pointer moved");
            CollectionAssert.AreEqual(recentBefore, editor.Session.Colors.Recent.ToList(), "nothing recorded while the drag runs");
            editor.Release(on, new Point(on.Bounds.Width * 0.8, on.Bounds.Height * 0.2));
            var released = Text(editor).Fill;
            Assert.AreEqual(shownColours[^1], released);
            CollectionAssert.AreEqual(recentBefore.Prepend(released).ToList(), editor.Session.Colors.Recent.ToList(), "one recent colour, the released one (G21)");

            editor.Session.Undo();
            Assert.AreEqual(start, Text(editor).Fill, "one Ctrl+Z puts back the fill before the press (G25)");
            if (surface == "HueStrip")
            {
                editor.Session.Undo();
            }

            Assert.AreEqual(White, Text(editor).Fill);
            editor.Session.Undo();
            Assert.IsEmpty(editor.Session.Layers.Layers, "then the text's adding: the drag was one step");
        });
    }

    [TestMethod]
    public async Task WhileTheSquareIsHeldThePictureAsDrawnChangesAtEachMove()
    {
        using var shown = ShownWindow.Open();
        using var editor = new ShownEditor(shown);
        await TextSelected(shown, editor, PinnedDefault, 1600, 1200);
        await OpenFrom(shown, editor, "More colors");
        var drawn = new List<uint[]> { await PictureAsDrawn(shown, editor) };
        var square = TrayDesktop.Get(() => Surface(editor, "ValueSquare"));
        var points = new[] { new Point(20, 20), new Point(120, 40), new Point(200, 120) };

        for (var i = 0; i < points.Length; i++)
        {
            var at = points[i];
            TrayDesktop.Run(() =>
            {
                if (i == 0)
                {
                    editor.Press(square, at);
                }
                else
                {
                    editor.Move(square, at);
                }
            });
            drawn.Add(await PictureAsDrawn(shown, editor));
            var changed = drawn[^1].Zip(drawn[^2]).Count(pair => pair.First != pair.Second);
            Assert.IsGreaterThan(20, changed, $"the picture as rendered changed at step {i} while the button is held, not after its let-go (G21)");
        }

        TrayDesktop.Run(() => editor.Release(square, points[^1]));
    }

    [TestMethod]
    public async Task AHueLetGoOnThenThePickerOpenedAgainShowsThatColoursOwnHue()
    {
        using var shown = ShownWindow.Open();
        using var editor = new ShownEditor(shown);
        await TextSelected(shown, editor, PinnedDefault, 1600, 1200);
        await OpenFrom(shown, editor, "More colors");
        TrayDesktop.Run(() =>
        {
            var strip = Surface(editor, "HueStrip");
            var square = Surface(editor, "ValueSquare");
            shown.Click(strip);
            editor.Press(square, new Point(square.Bounds.Width * 0.9, square.Bounds.Height * 0.1));
            editor.Release(square, new Point(square.Bounds.Width * 0.9, square.Bounds.Height * 0.1));
        });
        var (fill, hex) = TrayDesktop.Get(() => (Text(editor).Fill, Hex(editor).Text));
        TrayDesktop.Run(() => ShownWindow.Press(editor.Canvas, Key.Escape));
        await OpenFrom(shown, editor, "More colors");

        TrayDesktop.Run(() =>
        {
            Assert.AreEqual(180, ColorChoices.ToHsv(fill).Hue, 1, "the strip's middle hue, given by the square (G21)");
            Assert.AreEqual(hex, Hex(editor).Text, "opened again on the colour let go on");
            var square = Surface(editor, "ValueSquare");
            editor.Press(square, new Point(square.Bounds.Width * 0.5, square.Bounds.Height * 0.5));
            editor.Release(square, new Point(square.Bounds.Width * 0.5, square.Bounds.Height * 0.5));
            Assert.AreEqual(180, ColorChoices.ToHsv(Text(editor).Fill).Hue, 1, "the square, opened again, still gives that colour's own hue (G21)");
        });
    }

    [TestMethod]
    public async Task DraggedOnABoxMarkTheColourShowsLiveAndCommitsOnceAndWithNoMarkTheLetGoIsTheNextMarksColour()
    {
        using var shown = ShownWindow.Open();
        using var editor = new ShownEditor(shown);
        var items = await shown.OpenStash(shown.Picture("grey.png", 1600, 1200, (_, _) => TextFieldTests.Grey));
        TrayDesktop.Run(() =>
        {
            shown.Window.Width = WindowSize.PinnedDefault.Width;
            shown.Window.Height = WindowSize.PinnedDefault.Height;
        });
        await EditorOverlayTests.Open(shown, items[0]);
        TrayDesktop.Run(() =>
        {
            editor.Choose("Draw");
            editor.Choose("Box");
            editor.Drag((0.25, 0.25), (0.75, 0.75));
        });
        await OpenFrom(shown, editor, "More colors");

        TrayDesktop.Run(() =>
        {
            var square = Surface(editor, "ValueSquare");
            editor.Press(square, new Point(10, 10));
            editor.Move(square, new Point(square.Bounds.Width - 10, 20));
            var live = ColorChoices.ParseHex(Hex(editor).Text ?? "")!.Value;
            Assert.AreEqual(live, Mark(editor).Color, "the mark shows the colour as the drag passes it (G23)");
            editor.Release(square, new Point(square.Bounds.Width - 10, 20));
            Assert.AreEqual(live, Mark(editor).Color);
            editor.Session!.Undo();
            Assert.AreEqual(ColorChoices.Red, Mark(editor).Color, "one step (G25)");

            editor.Session.Deselect();
            var layers = editor.Session.Layers;
            editor.Press(square, new Point(30, 30));
            editor.Release(square, new Point(30, 30));
            var next = ColorChoices.ParseHex(Hex(editor).Text ?? "")!.Value;
            Assert.AreEqual(layers, editor.Session.Layers, "with no mark selected no layer changes");
            Assert.AreEqual(next, editor.Session.Tools.OfType<DrawTool>().Single().Color, "the released colour is the next mark's (G23)");
        });
    }

    [TestMethod]
    public async Task EscOrAPressElsewhereDuringADragPutsTheColourBackAndRecordsNothing()
    {
        using var shown = ShownWindow.Open();
        using var editor = new ShownEditor(shown);
        await TextSelected(shown, editor, PinnedDefault, 1600, 1200);
        foreach (var ending in new[] { "Esc", "a press on the picture" })
        {
            await OpenFrom(shown, editor, "More colors");
            TrayDesktop.Run(() =>
            {
                var square = Surface(editor, "ValueSquare");
                editor.Press(square, new Point(20, 20));
                editor.Move(square, new Point(square.Bounds.Width - 20, 30));
                Assert.AreNotEqual(White, Text(editor).Fill, "the drag shows its colour");
                if (ending == "Esc")
                {
                    ShownWindow.Press(Hex(editor), Key.Escape);
                }
                else
                {
                    editor.Press(editor.At(0.9, 0.9));
                    editor.Release(editor.At(0.9, 0.9));
                }

                Assert.IsNull(Card(editor), $"{ending} closed the picker");
                Assert.AreEqual(White, Text(editor).Fill, $"{ending}: the fill before the press (G20)");
                editor.Release(square, new Point(square.Bounds.Width - 20, 30));
                Assert.AreEqual(White, Text(editor).Fill, "the late let-go applies nothing");
                Assert.IsEmpty(editor.Session!.Colors.Recent, "nothing recorded");
            });
        }

        TrayDesktop.Run(() =>
        {
            editor.Session!.Undo();
            Assert.IsEmpty(editor.Session.Layers.Layers, "one Undo undoes the text's adding: no drag left a step (G25)");
        });
    }

    [TestMethod]
    public async Task CtrlZOrEnterInTheHexDuringADragEndsItTheUndoOrTheHexBeingTheStepAndTheLateLetGoAppliesNothing()
    {
        using var shown = ShownWindow.Open();
        using var editor = new ShownEditor(shown);
        await TextSelected(shown, editor, PinnedDefault, 1600, 1200);
        var blue = ColorChoices.Swatches[4];
        TrayDesktop.Run(() => editor.Choose(ColorChoices.Hex(blue)));
        await OpenFrom(shown, editor, "More colors");

        TrayDesktop.Run(() =>
        {
            var square = Surface(editor, "ValueSquare");
            editor.Press(square, new Point(20, 20));
            editor.Move(square, new Point(square.Bounds.Width - 20, 30));
            ShownWindow.Press(editor.Canvas, Key.Z, KeyModifiers.Control);
            Assert.AreEqual(White, Text(editor).Fill, "the drag put back, then the swatch's step undone: one Ctrl+Z (G25)");
            editor.Move(square, new Point(40, 60));
            Assert.AreEqual(White, Text(editor).Fill, "a move with the button still held begins nothing again");
            editor.Release(square, new Point(40, 60));
            Assert.AreEqual(White, Text(editor).Fill, "the late let-go applies nothing");
            Assert.IsEmpty(editor.Session!.Colors.Recent);

            editor.Press(square, new Point(20, 20));
            editor.Move(square, new Point(square.Bounds.Width - 20, 30));
            var hex = Hex(editor);
            hex.Focus();
            hex.SelectAll();
            ShownWindow.Type(hex, "#FF7A1A");
            ShownWindow.Press(hex, Key.Enter);
            var typed = new Rgba(0xFF, 0x7A, 0x1A, 0xFF);
            Assert.AreEqual(typed, Text(editor).Fill, "the hex's colour, applied at once");
            editor.Release(square, new Point(square.Bounds.Width - 20, 30));
            Assert.AreEqual(typed, Text(editor).Fill, "the late let-go applies nothing");
            CollectionAssert.AreEqual(new[] { typed }, editor.Session.Colors.Recent.ToArray(), "the hex's colour alone recorded");
            editor.Session.Undo();
            Assert.AreEqual(White, Text(editor).Fill, "the hex was one step, from the fill before the drag");
        });
    }

    [TestMethod]
    public async Task AShrinkChosenFromTheSizeChipDuringADragPutsTheColourBackAndIsItsOwnStep()
    {
        using var shown = ShownWindow.Open();
        using var editor = new ShownEditor(shown);
        await TextSelected(shown, editor, PinnedDefault, 1600, 1200);
        await OpenFrom(shown, editor, "More colors");

        TrayDesktop.Run(() =>
        {
            var square = Surface(editor, "ValueSquare");
            editor.Press(square, new Point(20, 20));
            editor.Move(square, new Point(square.Bounds.Width - 20, 30));
            Assert.AreNotEqual(White, Text(editor).Fill, "the drag shows its colour");
            // The size chip's Click and the menu entry's, as the keyboard raises them while the button stays down on the square.
            editor.ChooseShrink("Half size");
            Assert.AreEqual(Shrink.Half, editor.Session!.Shrink);
            Assert.AreEqual(White, Text(editor).Fill, "the fill before the press: the Shrink carried no dragged colour (G25)");
            editor.Release(square, new Point(square.Bounds.Width - 20, 30));
            Assert.AreEqual(White, Text(editor).Fill, "the late let-go applies nothing");
            Assert.IsEmpty(editor.Session.Colors.Recent, "no colour used, none recent (G21)");
            editor.Session.Undo();
            Assert.AreEqual(Shrink.Full, editor.Session.Shrink, "one Undo takes back the Shrink alone");
            Assert.AreEqual(White, Text(editor).Fill);
        });
    }

    [TestMethod]
    public async Task ASecondPointerOnTheStripWhileTheSquareIsHeldIsIgnoredAndTheFirstPointersEndedDragStaysEnded()
    {
        using var shown = ShownWindow.Open();
        using var editor = new ShownEditor(shown);
        await TextSelected(shown, editor, PinnedDefault, 1600, 1200);
        var blue = ColorChoices.Swatches[4];
        TrayDesktop.Run(() => editor.Choose(ColorChoices.Hex(blue)));
        await OpenFrom(shown, editor, "More colors");

        TrayDesktop.Run(() =>
        {
            var square = Surface(editor, "ValueSquare");
            var strip = Surface(editor, "HueStrip");
            var first = new Pointer(Pointer.GetNextFreeId(), PointerType.Touch, true);
            var second = new Pointer(Pointer.GetNextFreeId(), PointerType.Touch, false);
            Raise(shown, square, first, new Point(20, 20), PointerUpdateKind.LeftButtonPressed);
            Raise(shown, square, first, new Point(square.Bounds.Width - 20, 30), PointerUpdateKind.Other);
            ShownWindow.Press(editor.Canvas, Key.Z, KeyModifiers.Control);
            Assert.AreEqual(White, Text(editor).Fill, "Ctrl+Z ended the drag and undid the swatch");

            Raise(shown, strip, second, new Point(strip.Bounds.Width / 2, strip.Bounds.Height / 2), PointerUpdateKind.LeftButtonPressed);
            Raise(shown, square, first, new Point(40, 60), PointerUpdateKind.Other);
            Raise(shown, square, first, new Point(40, 60), PointerUpdateKind.LeftButtonReleased);
            Raise(shown, strip, second, new Point(strip.Bounds.Width / 2, strip.Bounds.Height / 2), PointerUpdateKind.LeftButtonReleased);

            Assert.AreEqual(White, Text(editor).Fill, "neither the ended pointer nor the second one coloured the text");
            Assert.IsEmpty(editor.Session!.Colors.Recent, "nothing recorded (G21)");
            editor.Session.Undo();
            Assert.IsEmpty(editor.Session.Layers.Layers, "no step of either pointer's: the next Undo undoes the text's adding (G25)");
        });
    }

    [TestMethod]
    public async Task AllFontsStandsInThePaneBesideThePictureAndShortenedStaysInsideThePicturesRowWithItsLastFamilyReachable()
    {
        using var shown = ShownWindow.Open();
        using var editor = new ShownEditor(shown);
        await TextSelected(shown, editor, PinnedDefault, 1600, 1200);
        await Task.Delay(200);
        TrayDesktop.Run(() =>
        {
            editor.ClickMenuButton("Font");
            editor.ClickMenuEntry("All fonts…");
        });
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => FontList(editor) is not null));
        TrayDesktop.Run(() =>
        {
            shown.Window.UpdateLayout();
            Beside(shown, editor, (Control)FontList(editor)!.GetVisualParent()!, "All fonts…");
        });

        TrayDesktop.Run(() => shown.Window.Height = WindowSize.Minimum.Height);
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => Math.Abs(shown.Window.Bounds.Height - WindowSize.Minimum.Height) < 1));
        TrayDesktop.Run(() =>
        {
            shown.Window.UpdateLayout();
            var list = FontList(editor)!;
            var row = editor.Canvas.GetVisualParent<Grid>()!;
            var docked = list.GetVisualAncestors().OfType<ScrollViewer>().First();
            var rowBox = new Rect(row.TranslatePoint(default, shown.Window)!.Value, row.Bounds.Size);
            var shownBox = new Rect(docked.TranslatePoint(default, shown.Window)!.Value, docked.Bounds.Size);
            Assert.IsTrue(rowBox.Inflate(0.5).Contains(shownBox), $"the pane at {shownBox} inside the picture's row {rowBox}, none of it over the tool row (G20)");
            var tools = editor.Button("Text");
            Assert.IsLessThanOrEqualTo(tools.TranslatePoint(default, shown.Window)!.Value.Y + 0.5, shownBox.Bottom, "above the tool row");
        });

        // Made as it scrolls into view, from an extent first estimated: scrolled to the end until the last family is made.
        var lastFamily = new WindowsFonts().Families().Last();
        Button? last = null;
        await ShownWindow.Eventually(() => TrayDesktop.Get(() =>
        {
            FontList(editor)!.ScrollToEnd();
            shown.Window.UpdateLayout();
            last = FontList(editor)!.GetVisualDescendants().OfType<Button>().FirstOrDefault(entry => entry.IsEffectivelyVisible && AutomationProperties.GetName(entry) == lastFamily);
            return last is not null;
        }));
        var found = last!;
        TrayDesktop.Run(() =>
        {
            found.BringIntoView();
            shown.Window.UpdateLayout();
            var docked = FontList(editor)!.GetVisualAncestors().OfType<ScrollViewer>().First();
            var shownBox = new Rect(docked.TranslatePoint(default, shown.Window)!.Value, docked.Bounds.Size);
            var listBox = new Rect(FontList(editor)!.TranslatePoint(default, shown.Window)!.Value, FontList(editor)!.Bounds.Size);
            var lastBox = new Rect(found.TranslatePoint(default, shown.Window)!.Value, found.Bounds.Size);
            Assert.IsTrue(shownBox.Inflate(0.5).Contains(lastBox) && listBox.Inflate(0.5).Contains(lastBox), $"the last family, {lastFamily}, at {lastBox} inside the list {listBox} and the pane's view {shownBox} (F8)");
        });
    }

    private static readonly WindowSize PinnedDefault = WindowSize.PinnedDefault;

    /// <summary>A pointer event of <paramref name="pointer"/>'s raised on <paramref name="on"/> at <paramref name="at"/>, in its units.</summary>
    private static void Raise(ShownWindow shown, Control on, Pointer pointer, Point at, PointerUpdateKind kind)
    {
        var inWindow = on.TranslatePoint(at, shown.Window)!.Value;
        var held = new PointerPointProperties(RawInputModifiers.LeftMouseButton, kind);
        on.RaiseEvent(kind switch
        {
            PointerUpdateKind.LeftButtonPressed => new PointerPressedEventArgs(on, pointer, shown.Window, inWindow, 0, held, KeyModifiers.None, 1),
            PointerUpdateKind.LeftButtonReleased => new PointerReleasedEventArgs(on, pointer, shown.Window, inWindow, 0, new PointerPointProperties(RawInputModifiers.None, kind), KeyModifiers.None, MouseButton.Left),
            _ => new PointerEventArgs(InputElement.PointerMovedEvent, on, pointer, shown.Window, inWindow, 0, held, KeyModifiers.None),
        });
    }

    /// <summary>The picture's pixels as the real renderer draws the window now, row by row.</summary>
    private static async Task<uint[]> PictureAsDrawn(ShownWindow shown, ShownEditor editor)
    {
        var rendered = await RenderedWindow.Of(shown.Window);
        var picture = TrayDesktop.Get(editor.PictureInRendering);
        var (left, top, right, bottom) = ((int)picture.X, (int)picture.Y, (int)picture.Right, (int)picture.Bottom);
        return [.. Enumerable.Range(top, bottom - top).SelectMany(y => Enumerable.Range(left, right - left).Select(x => rendered.Pixels[y * rendered.Width + x]))];
    }

    /// <summary>The window at <paramref name="size"/>, the editor open over a grey still of that size, a Meme with words added and selected.</summary>
    private static async Task TextSelected(ShownWindow shown, ShownEditor editor, WindowSize size, int width, int height)
    {
        var items = await shown.OpenStash(shown.Picture("grey.png", width, height, (_, _) => TextFieldTests.Grey));
        TrayDesktop.Run(() =>
        {
            shown.Window.Width = size.Width;
            shown.Window.Height = size.Height;
        });
        await EditorOverlayTests.Open(shown, items[0]);
        TrayDesktop.Run(() =>
        {
            CloseNotices(shown);
            editor.Click(0.5, 0.25);
            ShownWindow.Type(editor.Field!, "hi");
            ShownWindow.Press(editor.Field!, Key.Enter, KeyModifiers.Control);
            Assert.AreEqual(White, Text(editor).Fill);
        });
    }

    /// <summary>The import's notice would lie over the editor's foot for its six seconds; the user's × closes it.</summary>
    private static void CloseNotices(ShownWindow shown)
    {
        foreach (var close in shown.Window.GetVisualDescendants().OfType<Button>().Where(button => button.IsEffectivelyVisible && AutomationProperties.GetName(button) == "Close" && button.GetVisualAncestors().Any(parent => parent.GetType().Name == "NoticeCard")).ToList())
        {
            ShownWindow.RaiseClick(close);
        }
    }

    /// <summary>A click on the opener, once the row is drawn, and the picker showing.</summary>
    private static async Task OpenFrom(ShownWindow shown, ShownEditor editor, string opener)
    {
        await Task.Delay(200);
        TrayDesktop.Run(() => shown.Click(editor.Button(opener)));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => Card(editor) is not null));
        TrayDesktop.Run(shown.Window.UpdateLayout);
    }

    /// <summary>
    /// The card stands beside the picture: inside the editor and the window, its rectangle and the picture's drawn one
    /// apart, and the canvas left at least as wide as the card (G20, G21).
    /// </summary>
    private static void Beside(ShownWindow shown, ShownEditor editor, Control card, string what)
    {
        Assert.IsTrue(Docked(card), $"{what}: docked beside the picture");
        TextOptionRowTests.AssertInside(shown, card, what);
        var cardBox = new Rect(card.TranslatePoint(default, shown.Window)!.Value, card.Bounds.Size);
        var box = editor.Box;
        var picture = new Rect(editor.Canvas.TranslatePoint(box.TopLeft, shown.Window)!.Value, box.Size);
        Assert.IsFalse(cardBox.Intersects(picture), $"{what}: the card at {cardBox} clear of the picture drawn at {picture}");
        Assert.IsGreaterThanOrEqualTo(card.Bounds.Width, editor.Canvas.Bounds.Width, $"{what}: the picture's room at least as wide as the card");
    }

    /// <summary>Whether the card stands in the pane's column beside the picture, not over the editor.</summary>
    private static bool Docked(Control card) => card.GetVisualAncestors().OfType<ScrollViewer>().Any();

    /// <summary>The colour picker's card while it shows.</summary>
    private static Control? Card(ShownEditor editor) =>
        editor.Overlay.GetVisualDescendants().OfType<Control>().FirstOrDefault(control => control.IsEffectivelyVisible && AutomationProperties.GetName(control) == "Color picker")?.GetVisualParent() as Control;

    private static ScrollViewer? FontList(ShownEditor editor) =>
        editor.Overlay.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault(control => control.IsEffectivelyVisible && AutomationProperties.GetName(control) == "All fonts");

    private static TextBox Hex(ShownEditor editor) =>
        (TextBox)Card(editor)!.GetVisualDescendants().OfType<Control>().First(control => AutomationProperties.GetName(control) == "Hex");

    private static Control Surface(ShownEditor editor, string type) =>
        Card(editor)!.GetVisualDescendants().OfType<Control>().Single(control => control.GetType().Name == type);

    private static TextLayer Text(ShownEditor editor) => editor.Session!.Layers.Layers.OfType<TextLayer>().Single();

    private static DrawMarkLayer Mark(ShownEditor editor) => editor.Session!.Layers.Layers.OfType<DrawMarkLayer>().Single();
}
