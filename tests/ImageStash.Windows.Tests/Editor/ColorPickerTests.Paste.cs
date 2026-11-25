using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ImageStash.Editor;
using ImageStash.Layers;
using ImageStash.Media;
using ImageStash.Seams;
using ImageStash.Store;
using ImageStash.Window;
using ImageStash.Windows.Tests.Support;

namespace ImageStash.Windows.Tests.Editor;

public sealed partial class ColorPickerTests
{
    [TestMethod]
    public async Task AHexTypedOrPastedAndEnteredColoursTheSelectedMarkShowsInsideTheMoreSwatchAndInRecent()
    {
        var clipboard = new PresetDrags();
        using var shown = ShownWindow.Open(drags: clipboard);
        using var editor = new ShownEditor(shown);
        var items = await shown.OpenStash(shown.Picture("checker.png", 64, 48, EditorCanvasTests.Checker));
        await EditorOverlayTests.Open(shown, items[0]);
        var windowsBefore = ProcessWindows.Visible();
        TrayDesktop.Run(() =>
        {
            editor.Choose("Draw");
            editor.Choose("Box");
            editor.Drag((0.25, 0.25), (0.75, 0.75));
        });
        TrayDesktop.Run(() =>
        {
            editor.Choose("More colors");
            shown.Window.UpdateLayout();
            var content = Picker(editor)!;
            Assert.AreSame(shown.Window, TopLevel.GetTopLevel(content), "inside the stash window (F4)");
            Assert.AreEqual(windowsBefore, ProcessWindows.Visible(), "never a window of its own (F4)");
            Assert.IsTrue(Named(content, "Pick from the picture") is Button, "the eyedropper");
            Assert.Contains("Recent", content.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text).ToList());
            var hex = (TextBox)Named(content, "Hex")!;
            hex.Focus();
            hex.SelectAll();
            ShownWindow.Type(hex, "#FF7A1A");
            ShownWindow.Tap(hex, Key.Enter);

            var orange = new Rgba(255, 122, 26, 255);
            Assert.AreEqual(orange, ((DrawMarkLayer)editor.Session!.Layers.Layers.Single()).Color, "the selected box takes the colour (G21)");
            Assert.AreEqual(Color.FromRgb(255, 122, 26), MoreSwatchFill(editor), "the More swatch shows it");
            Assert.Contains("#FF7A1A", content.GetVisualDescendants().OfType<Button>().Select(AutomationProperties.GetName).ToList(), "Recent holds it");
        });

        var read = new TaskCompletionSource<Outcome<ClipboardReading>>(TaskCreationOptions.RunContinuationsAsynchronously);
        TrayDesktop.Run(() =>
        {
            var hex = FocusHex(shown, editor);
            clipboard.ReadClipboard = () => Task.FromResult(Words("123"));
            hex.SelectionStart = 7;
            hex.SelectionEnd = 1;
            ShownWindow.Press(hex, Key.V, KeyModifiers.Control);
            ShownWindow.Press(hex, Key.V, KeyModifiers.Control);
            ShownWindow.Release(hex, Key.V);
            Assert.AreEqual(("#123", 4, 4), (hex.Text, hex.SelectionStart, hex.SelectionEnd), "the selection replaced once by the text read");

            clipboard.ReadClipboard = () => read.Task;
            hex.SelectAll();
            ShownWindow.Tap(hex, Key.V, KeyModifiers.Control);
            Assert.AreEqual(2, clipboard.Reads, "one read per press including the held key (N9, R5)");
            Assert.AreEqual("#123", hex.Text, "the held read has not answered");
            hex.SelectionStart = hex.SelectionEnd = hex.CaretIndex = 4;
        });

        read.SetResult(Words("456"));
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var content = Picker(editor)!;
            var hex = (TextBox)Named(content, "Hex")!;
            Assert.AreEqual(("#123456", 7, 7), (hex.Text, hex.SelectionStart, hex.SelectionEnd), "a delayed read uses the open hex's current caret");
            Assert.AreEqual(new Rgba(255, 122, 26, 255), ((DrawMarkLayer)editor.Session!.SelectedLayer!).Color, "paste edits the draft");
            ShownWindow.Tap(hex, Key.Enter);
            Assert.AreEqual(new Rgba(0x12, 0x34, 0x56, 255), ((DrawMarkLayer)editor.Session.SelectedLayer!).Color, "Enter applies the pasted hex");
            Assert.AreEqual(Color.FromRgb(0x12, 0x34, 0x56), MoreSwatchFill(editor));
            Assert.Contains("#123456", content.GetVisualDescendants().OfType<Button>().Select(AutomationProperties.GetName).ToList());
            Assert.AreEqual("", shown.Said(), "no paste notice for text");
        }, DispatcherPriority.Background);
        Assert.HasCount(1, (await shown.Listing.Store!.All()).Value!);

        // Esc leaves the hex and the picker, and Del then removes the selected mark.
        TrayDesktop.Run(() =>
        {
            var hex = (TextBox)Named(Picker(editor)!, "Hex")!;
            ShownWindow.Press(hex, Key.Escape);
            Assert.IsNull(Picker(editor), "the picker closed");
            Assert.IsTrue(editor.Session!.IsOpen, "and the edit stays open");
            ShownWindow.Press(editor.Canvas, Key.Delete);
            Assert.IsTrue(editor.Session.Layers.IsEmpty, "the selected mark removed");
        });
    }

    [TestMethod]
    public async Task TheHexBoxKeepsItsDraftAndStashWhenTheReadReturnsFilesAnImageOrNothingOrFails()
    {
        var clipboard = new PresetDrags();
        using var shown = ShownWindow.Open(drags: clipboard);
        using var editor = new ShownEditor(shown);
        var item = (await shown.OpenStash(shown.Picture("grey.png", 200, 150, (_, _) => TextFieldTests.Grey))).Single();
        var other = shown.Fixture("transparent.png", "other.png");
        var png = File.ReadAllBytes(other);
        const string noText = "Paste failed: the clipboard, it holds no text to paste";
        var cases = new (Outcome<ClipboardReading> Read, string Notice)[]
        {
            (Outcome<ClipboardReading>.Of(new ClipboardReading([], png, null, false, "ff0000")), noText),
            (Outcome<ClipboardReading>.Of(new ClipboardReading([new DroppedItem("other.png", other, null, false)], null, null, false, "ff0000")), noText),
            (Outcome<ClipboardReading>.Of(new ClipboardReading([], null, new MediaFrame(1, 1, 0, new byte[4]), false)), noText),
            (Words(""), noText),
            (Outcome<ClipboardReading>.Of(new ClipboardReading([], null, null, false)), noText),
            (new Failure(ClipboardPaste.Action, "the clipboard", HeldClipboard.Reason), $"Paste failed: the clipboard, {HeldClipboard.Reason}"),
        };

        await OpenLabel(shown, editor, item);
        var hex = TrayDesktop.Get(() =>
        {
            var field = FocusHex(shown, editor);
            Assert.IsTrue(editor.Session!.Typing, "the empty label's text is still open with focus in the picker");
            return field;
        });
        foreach (var (read, notice) in cases)
        {
            var draft = TrayDesktop.Get(() => hex.Text);
            TrayDesktop.Run(() =>
            {
                clipboard.ReadClipboard = () => Task.FromResult(read);
                hex.SelectAll();
                ShownWindow.Tap(hex, Key.V, KeyModifiers.Control);
            });
            await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Import.Busy is null));
            Assert.HasCount(1, (await shown.Listing.Store!.All()).Value!, "pasting with focus in the hex adds nothing (G13)");
            TrayDesktop.Run(() =>
            {
                Assert.AreEqual(draft, hex.Text);
                Assert.AreEqual("", editor.Field!.Text, "the open layer's text is kept");
                Assert.AreEqual(notice, shown.Said());
                shown.Notices.Close(shown.Notices.Shown.Single(visible => visible.Text == notice));
            });
        }

        TrayDesktop.Run(() =>
        {
            clipboard.ReadClipboard = () => Task.FromResult(Words("ff0000"));
            hex.SelectAll();
            ShownWindow.Tap(hex, Key.V, KeyModifiers.Control);
            Assert.AreEqual("ff0000", hex.Text, "the hex draft takes the text while the layer stays open");
            Assert.AreEqual("", editor.Field!.Text);
            ShownWindow.Tap(hex, Key.Enter);
            Assert.AreEqual(new Rgba(255, 0, 0, 255), Text(editor).Fill);
        });
        Assert.AreEqual(cases.Length + 1, clipboard.Reads);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("Outline color")]
    [DataRow("More colors")]
    public async Task ADelayedHexPasteDoesNotGoIntoTheCaptionOrIntoADifferentOpeningOfThePicker(string? nextPicker)
    {
        var read = new TaskCompletionSource<Outcome<ClipboardReading>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var clipboard = new PresetDrags { ReadClipboard = () => read.Task };
        using var shown = ShownWindow.Open(drags: clipboard);
        using var editor = new ShownEditor(shown);
        var item = (await shown.OpenStash(shown.Picture("grey.png", 200, 150, (_, _) => TextFieldTests.Grey))).Single();
        await OpenLabel(shown, editor, item);

        var (original, target, draft) = TrayDesktop.Get(() =>
        {
            var hex = FocusHex(shown, editor);
            hex.SelectAll();
            ShownWindow.Tap(hex, Key.V, KeyModifiers.Control);
            Assert.AreEqual(1, clipboard.Reads, "the read began before focus moved");
            ShownWindow.Tap(hex, Key.Escape);
            Assert.IsNull(Picker(editor), "Esc closed the first opening");
            Assert.IsTrue(editor.Field!.Focus());
            Assert.AreSame(editor.Field, shown.Focused);
            if (nextPicker is null)
            {
                return (hex, editor.Field!, hex.Text);
            }

            editor.Choose(nextPicker);
            var next = FocusHex(shown, editor);
            next.SelectAll();
            return (hex, next, hex.Text);
        });
        var targetText = TrayDesktop.Get(() => target.Text);
        read.SetResult(Words("ff0000"));
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            Assert.AreEqual(draft, original.Text, "the original opening is gone");
            Assert.AreEqual(targetText, target.Text, "the new target does not take the paste");
            Assert.AreEqual("", editor.Field!.Text);
            Assert.AreEqual("", shown.Said());
        }, DispatcherPriority.Background);
        Assert.HasCount(1, (await shown.Listing.Store!.All()).Value!);
    }

    [TestMethod]
    public async Task CtrlVWithFocusOnTheEyedropperUsesTheOrdinaryImagePaste()
    {
        var clipboard = new PresetDrags();
        using var shown = ShownWindow.Open(drags: clipboard);
        using var editor = new ShownEditor(shown);
        var item = (await shown.OpenStash(shown.Picture("grey.png", 200, 150, (_, _) => TextFieldTests.Grey))).Single();
        var png = File.ReadAllBytes(shown.Fixture("transparent.png", "other.png"));
        clipboard.ReadClipboard = () => Task.FromResult(Outcome<ClipboardReading>.Of(new ClipboardReading([], png, null, false)));
        await OpenLabel(shown, editor, item);

        TrayDesktop.Run(() =>
        {
            shown.Window.UpdateLayout();
            var pick = Named(Picker(editor)!, ColorPicker.PickFromThePicture)!;
            Assert.IsTrue(pick.Focus());
            Assert.AreSame(pick, shown.Focused);
            ShownWindow.Tap(pick, Key.V, KeyModifiers.Control);
        });
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Import.Busy is null));
        Assert.HasCount(2, (await shown.Listing.Store!.All()).Value!, "focus outside the hex keeps the ordinary paste while the picker is open");
        Assert.AreEqual(1, clipboard.Reads);
    }

    private static Outcome<ClipboardReading> Words(string words) => Outcome<ClipboardReading>.Of(new ClipboardReading([], null, null, false, words));

    private static async Task OpenLabel(ShownWindow shown, ShownEditor editor, StashItem item)
    {
        await TrayDesktop.Get(() => shown.Editing.Open(item, EditOrigin.Grid));
        TrayDesktop.Run(() =>
        {
            shown.Window.UpdateLayout();
            editor.Choose("Label");
            editor.Click(0.5, 0.25);
            editor.Choose("More colors");
            Assert.IsTrue(editor.Session!.Typing, "opening the picker leaves the layer text open");
        });
    }

    private static TextBox FocusHex(ShownWindow shown, ShownEditor editor)
    {
        shown.Window.UpdateLayout();
        var hex = (TextBox)Named(Picker(editor)!, "Hex")!;
        Assert.IsTrue(hex.Focus());
        Assert.AreSame(hex, shown.Focused);
        return hex;
    }
}
