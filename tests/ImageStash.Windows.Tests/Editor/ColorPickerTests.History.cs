using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.VisualTree;
using ImageStash.Editor;
using ImageStash.Layers;
using ImageStash.Windows.Tests.Support;

namespace ImageStash.Windows.Tests.Editor;

public sealed partial class ColorPickerTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task UndoOfAConfirmedHexDoesNotLeaveTheUndoneColorInThePicker(bool useButtons)
    {
        var clipboard = new PresetDrags();
        using var shown = ShownWindow.Open(drags: clipboard);
        using var editor = new ShownEditor(shown);
        var item = (await shown.OpenStash(shown.Picture("checker.png", 400, 300, EditorCanvasTests.Checker))).Single();
        await EditorOverlayTests.Open(shown, item);

        TrayDesktop.Run(() =>
        {
            editor.Choose("Draw");
            editor.Choose("Box");
            editor.Drag((0.1, 0.1), (0.4, 0.4));
            editor.Choose("More colors");

            var hex = FocusHex(shown, editor);
            hex.SelectAll();
            ShownWindow.Type(hex, "#FF7A1A");
            ShownWindow.Tap(hex, Key.Enter);
            ApplyHex(hex, clipboard, new Rgba(0x12, 0x34, 0x56, 255));
            Assert.AreEqual("#123456", SelectedColor());

            StepHistory(Key.Z, "Undo", "#FF7A1A");
            StepHistory(Key.Y, "Redo", "#123456");

            string SelectedColor() => ColorChoices.Hex(((DrawMarkLayer)editor.Session!.SelectedLayer!).Color);

            void StepHistory(Key key, string button, string expected)
            {
                if (useButtons)
                {
                    editor.Choose(button);
                }
                else
                {
                    ShownWindow.Tap(hex, key, KeyModifiers.Control);
                }

                Assert.AreEqual(expected, SelectedColor());
                Assert.AreSame(hex, shown.Focused);
                var displayed = hex.Text;
                ShownWindow.Tap(hex, Key.Enter);
                Assert.AreEqual(expected, SelectedColor(), $"Enter applied the color shown in Hex ({displayed}).");
                Assert.AreEqual(expected, displayed);
            }
        });
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task UndoOfAnOutlineWithTheSameHexAsAutoRequiresAFreshChoice(bool rejectedPaste)
    {
        var clipboard = new PresetDrags();
        using var shown = ShownWindow.Open(drags: clipboard);
        using var editor = new ShownEditor(shown);
        var item = (await shown.OpenStash(shown.Picture("checker.png", 400, 300, EditorCanvasTests.Checker))).Single();
        await OpenTextFill(shown, editor, item);

        TrayDesktop.Run(() =>
        {
            ShownWindow.Tap(FocusHex(shown, editor), Key.Escape);
            ShownWindow.Type(editor.Field!, "Label");
            ShownWindow.Tap(editor.Field!, Key.Enter, KeyModifiers.Control);
            editor.Choose("Outline color");
            var hex = FocusHex(shown, editor);
            var chosen = new Rgba(0, 0, 0, 255);
            Assert.AreEqual(Outline.Auto, Text(editor).Outline);

            ApplyHex(hex, clipboard, chosen);
            Assert.AreEqual(Outline.Of(chosen), Text(editor).Outline);
            ShownWindow.Tap(hex, Key.Z, KeyModifiers.Control);
            Assert.AreEqual(Outline.Auto, Text(editor).Outline);
            Assert.AreSame(hex, shown.Focused);
            Assert.AreEqual(ColorChoices.Hex(chosen), hex.Text);
            Assert.IsTrue(Named(Picker(editor)!, ColorPicker.Auto) is ToggleButton { IsChecked: true });

            if (rejectedPaste)
            {
                hex.SelectAll();
                PasteHex(hex, clipboard, "\r\n");
            }
            else
            {
                var square = ColorSquare(editor);
                var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Touch, true);
                var at = square.TranslatePoint(new Point(20, 20), shown.Window)!.Value;
                var pressed = new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed);
                square.RaiseEvent(new PointerPressedEventArgs(square, pointer, shown.Window, at, 0, pressed, KeyModifiers.None, 1));
                Assert.AreNotEqual(Outline.Auto, Text(editor).Outline, "the pointer previews a color");
                pointer.Capture(null);
            }

            Assert.AreEqual(ColorChoices.Hex(chosen), hex.Text);
            ShownWindow.Tap(hex, Key.Enter);
            Assert.AreEqual(Outline.Auto, Text(editor).Outline, "the restored Auto stays Auto with the same displayed Hex");
            ApplyHex(hex, clipboard, chosen);
            Assert.AreEqual(Outline.Of(chosen), Text(editor).Outline, "a fresh paste of the same Hex can choose an explicit outline");
        });
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task UndoDuringAColorDragKeepsTheRestoredColorAndTheNextHexDraft(bool undoSize)
    {
        var clipboard = new PresetDrags();
        using var shown = ShownWindow.Open(drags: clipboard);
        using var editor = new ShownEditor(shown);
        var item = (await shown.OpenStash(shown.Picture("checker.png", 400, 300, EditorCanvasTests.Checker))).Single();
        await EditorOverlayTests.Open(shown, item);

        TrayDesktop.Run(() =>
        {
            editor.Choose("Draw");
            editor.Choose("Box");
            editor.Drag((0.1, 0.1), (0.4, 0.4));
            var original = SelectedColor();
            if (undoSize)
            {
                editor.ChooseShrink("Half size");
            }

            editor.Choose("More colors");
            var hex = FocusHex(shown, editor);
            if (!undoSize)
            {
                ApplyHex(hex, clipboard, new Rgba(0xFF, 0x7A, 0x1A, 255));
            }

            var square = ColorSquare(editor);
            var at = new Point(20, 20);
            editor.Press(square, at);
            Assert.AreNotEqual(original, SelectedColor(), "the pointer previews a color");
            ShownWindow.Tap(hex, Key.Z, KeyModifiers.Control);
            Assert.AreEqual(Shrink.Full, editor.Session!.Shrink);
            Assert.AreEqual(original, SelectedColor());
            ShownWindow.Tap(hex, Key.Enter);
            Assert.AreEqual(original, SelectedColor(), "Enter leaves the discarded preview alone");

            hex.SelectAll();
            PasteHex(hex, clipboard, "#123456");
            editor.Move(square, new Point(40, 60));
            editor.Release(square, at);
            Assert.AreEqual("#123456", hex.Text, "the abandoned pointer leaves the new draft intact");
            ShownWindow.Tap(hex, Key.Enter);
            Assert.AreEqual("#123456", SelectedColor());

            string SelectedColor() => ColorChoices.Hex(((DrawMarkLayer)editor.Session!.SelectedLayer!).Color);
        });
    }

    private static Control ColorSquare(ShownEditor editor) =>
        Picker(editor)!.GetVisualDescendants().OfType<Control>().Single(control => control.GetType().Name == "ValueSquare");
}
