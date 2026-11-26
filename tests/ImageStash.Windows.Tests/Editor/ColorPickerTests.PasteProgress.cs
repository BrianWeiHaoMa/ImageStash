using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ImageStash.Editor;
using ImageStash.Seams;
using ImageStash.Store;
using ImageStash.Window;
using ImageStash.Windows.Tests.Support;

namespace ImageStash.Windows.Tests.Editor;

public sealed partial class ColorPickerTests
{
    [TestMethod]
    [DataRow(false, true, true)]
    [DataRow(true, true, true)]
    [DataRow(false, false, true)]
    [DataRow(true, false, true)]
    [DataRow(false, true, false)]
    [DataRow(true, true, false)]
    [DataRow(false, false, false)]
    [DataRow(true, false, false)]
    public async Task EditorNamesPasteUntilTheClipboardReadAnswers(bool pinned, bool inHex, bool succeeds)
    {
        const string progress = "Pasting from the clipboard…";
        var read = new TaskCompletionSource<Outcome<ClipboardReading>>();
        var clipboard = new PresetDrags { ReadClipboard = () => read.Task };
        using var shown = ShownWindow.Open(drags: clipboard);
        using var editor = new ShownEditor(shown);
        var item = (await shown.OpenStash(shown.Picture("grey.png", 200, 150, (_, _) => TextFieldTests.Grey))).Single();
        TrayDesktop.Run(() =>
        {
            TransferredStash.CloseNotices(shown);
            if (pinned)
            {
                shown.Window.SummonPinned();
            }
        });
        await OpenTextFill(shown, editor, item, "Meme");

        var (field, before) = TrayDesktop.Get(() =>
        {
            var hex = FocusHex(shown, editor);
            if (!inHex)
            {
                ShownWindow.Tap(hex, Key.Escape);
                Assert.IsTrue(editor.Field!.Focus());
            }

            var target = inHex ? hex : editor.Field!;
            Assert.AreSame(target, shown.Focused);
            target.SelectAll();
            var draft = target.Text;
            ShownWindow.Tap(target, Key.V, KeyModifiers.Control);
            Assert.AreEqual(1, clipboard.Reads);
            Assert.AreEqual(draft, target.Text, "the clipboard is still held");
            shown.Window.UpdateLayout();
            var label = editor.Overlay.GetVisualDescendants().OfType<TextBlock>().SingleOrDefault(text => text.IsEffectivelyVisible && text.Text == progress);
            Assert.IsNotNull(label, "the active editor visibly names the running paste (G35)");
            TextOptionRowTests.AssertInside(shown, label, "clipboard paste progress");
            return (target, draft);
        });

        var words = inHex ? "ff0000" : "STILL TYPING";
        var refusal = new Failure(ClipboardPaste.Action, "the clipboard", HeldClipboard.Reason);
        await Dispatcher.UIThread.InvokeAsync(() => read.SetResult(succeeds ? Words(words) : refusal));
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            shown.Window.UpdateLayout();
            Assert.DoesNotContain(progress, editor.Texts(), "the read has ended");
            Assert.AreEqual(succeeds ? words : before, field.Text);
            Assert.AreSame(field, shown.Focused);
            if (succeeds)
            {
                Assert.AreEqual("", shown.Said());
            }
            else
            {
                var label = shown.Window.GetVisualDescendants().OfType<TextBlock>()
                    .Single(text => text.IsEffectivelyVisible && text.Text == $"Paste failed: the clipboard, {HeldClipboard.Reason}");
                TextOptionRowTests.AssertInside(shown, label, "clipboard paste refusal");
            }
        }, DispatcherPriority.Background);
    }

    [TestMethod]
    public async Task AFullNoticeStackLeavesEditorPasteProgressVisible()
    {
        const string progress = "Pasting from the clipboard…";
        const string failure = "Import failed: \"a picture with a long name.png\" is a video. Use a PNG, JPEG, WebP, or GIF picture.";
        var read = new TaskCompletionSource<Outcome<ClipboardReading>>();
        using var shown = ShownWindow.Open(drags: new PresetDrags { ReadClipboard = () => read.Task });
        using var editor = new ShownEditor(shown);
        var item = (await shown.OpenStash(shown.Picture("grey.png", 200, 150, (_, _) => TextFieldTests.Grey))).Single();
        TrayDesktop.Run(() =>
        {
            shown.Window.Width = WindowSize.Minimum.Width;
            shown.Window.Height = WindowSize.Minimum.Height;
        });
        await OpenTextFill(shown, editor, item, "Meme");

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var hex = FocusHex(shown, editor);
            for (var index = 0; index < 20; index++)
            {
                shown.Notices.Raise(failure, namesAProblem: true);
            }

            ShownWindow.Tap(hex, Key.V, KeyModifiers.Control);
            shown.Window.UpdateLayout();
            var label = editor.Overlay.GetVisualDescendants().OfType<TextBlock>().Single(text => text.IsEffectivelyVisible && text.Text == progress);
            var cardText = shown.Window.GetVisualDescendants().OfType<TextBlock>().First(text => text.Text == failure);
            var stack = cardText.GetVisualAncestors().OfType<ScrollViewer>().Last();
            var stackPlace = Place(stack, shown);
            var labelPlace = Place(label, shown);
            Assert.IsFalse(stackPlace.Intersects(labelPlace), "the notice stack cannot cover the paste label");
            Assert.IsFalse(Place((Control)Picker(editor)!.GetVisualParent()!, shown).Intersects(labelPlace), "the floating picker cannot cover the paste label");
            foreach (var button in new[] { "Cancel", EditorOverlay.OutputSize, "Save" })
            {
                Assert.IsFalse(stackPlace.Intersects(Place(editor.Button(button), shown)), $"the notice stack cannot cover {button}");
            }

            read.SetResult(Words("ff0000"));
        });
    }

    [TestMethod]
    public async Task PasteCompletionDoesNotMoveTheLayerUnderAStationaryPointer()
    {
        var read = new TaskCompletionSource<Outcome<ClipboardReading>>();
        using var shown = ShownWindow.Open(drags: new PresetDrags { ReadClipboard = () => read.Task });
        using var editor = new ShownEditor(shown);
        var item = (await shown.OpenStash(shown.Picture("grey.png", 200, 150, (_, _) => TextFieldTests.Grey))).Single();
        await OpenTextFill(shown, editor, item, "Label");

        var (box, pointer, picture) = TrayDesktop.Get(() =>
        {
            ShownWindow.Tap(FocusHex(shown, editor), Key.Escape);
            var field = editor.Field!;
            Assert.IsTrue(field.Focus());
            ShownWindow.Type(field, "Already typed");
            ShownWindow.Tap(field, Key.V, KeyModifiers.Control);
            ShownWindow.Tap(field, Key.Enter, KeyModifiers.Control);
            Assert.IsNull(editor.Field);
            shown.Window.UpdateLayout();
            var place = Text(editor).Box;
            var at = editor.OnCanvas(place.X + place.Width / 2d, place.Y + place.Height / 2d);
            editor.Press(at);
            return (place, editor.Canvas.TranslatePoint(at, shown.Window)!.Value, PicturePlace(editor, shown));
        });

        await Dispatcher.UIThread.InvokeAsync(() => read.SetResult(Words("Too late")));
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            shown.Window.UpdateLayout();
            editor.Release(shown.Window.TranslatePoint(pointer, editor.Canvas)!.Value);
            Assert.AreEqual(box, Text(editor).Box, "a stationary pointer selects the text without moving it");
            Assert.AreEqual(picture, PicturePlace(editor, shown), "the fitted picture stays in place when the paste answers");
        }, DispatcherPriority.Background);
    }

    private static Rect PicturePlace(ShownEditor editor, ShownWindow shown) =>
        editor.Canvas.Mapping()!.Box.Translate(Place(editor.Canvas, shown).Position - default(Point));

    private static Rect Place(Control control, ShownWindow shown) =>
        new(control.TranslatePoint(default, shown.Window)!.Value, control.Bounds.Size);
}
