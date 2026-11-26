using Avalonia.Input;
using Avalonia.Threading;
using ImageStash.Layers;
using ImageStash.Seams;
using ImageStash.Store;
using ImageStash.Windows.Tests.Support;
using SkiaSharp;
using static ImageStash.Windows.Tests.Support.ShownWindow;

namespace ImageStash.Windows.Tests.Editor;

/// <summary>Clipboard keys in an open Meme field in the real editor (G13, G21, G25, N9, R5).</summary>
[TestClass]
public sealed class LayerTextPasteTests
{
    [TestMethod]
    public async Task CtrlVPastesWordsOnceAsUppercaseIntoTheFieldAndLayer()
    {
        using var shown = Open();
        using var editor = new ShownEditor(shown);
        await OpenMeme(shown, editor);
        TestClipboard.PutText("so true");

        TrayDesktop.Run(() => Tap(editor.Field!, Key.V, KeyModifiers.Control));

        await Eventually(() => TrayDesktop.Get(() => editor.Field?.Text is { Length: > 0 } || shown.Notices.Shown.Count > 0));
        Assert.HasCount(1, (await shown.Listing.Store!.All()).Value!, "the stash still holds only the original item (G13)");
        TrayDesktop.Run(() =>
        {
            Assert.AreEqual("SO TRUE", editor.Field?.Text, "one insertion in the field (G13, G21)");
            Assert.AreEqual("SO TRUE", ((TextLayer)editor.Session!.Layers.Layers.Single()).Text, "one insertion in the layer (G25)");
            Assert.IsEmpty(shown.Notices.Shown, "no paste notice for words alone");
        });
    }

    [TestMethod]
    [DataRow(false, false, DisplayName = "a picture on the clipboard")]
    [DataRow(true, false, DisplayName = "a file list on the clipboard")]
    [DataRow(false, true, DisplayName = "a picture with words on the clipboard")]
    [DataRow(true, true, DisplayName = "a file list with words on the clipboard")]
    public async Task CtrlVWithAPictureOrFilesLeavesTheTextAndStashAloneAndShowsTheNotice(bool files, bool withWords)
    {
        using var shown = Open();
        using var editor = new ShownEditor(shown);
        await OpenMeme(shown, editor);
        var picture = shown.Picture("clipboard picture.png");
        var media = files ? TestClipboard.FileList(picture) : (TestClipboard.Png, File.ReadAllBytes(picture));
        TestClipboard.Put(withWords ? [media, TestClipboard.Text("so true")] : [media]);

        TrayDesktop.Run(() =>
        {
            Type(editor.Field!, "keep");
            Tap(editor.Field!, Key.V, KeyModifiers.Control);
        });

        await Eventually(() => TrayDesktop.Get(() => shown.Notices.Shown.Count > 0));
        Assert.HasCount(1, (await shown.Listing.Store!.All()).Value!, "the stash still holds only the original item (G13)");
        TrayDesktop.Run(() =>
        {
            Assert.AreEqual("KEEP", editor.Field?.Text, "the field keeps its text (G13)");
            Assert.AreEqual("KEEP", ((TextLayer)editor.Session!.Layers.Layers.Single()).Text, "the layer keeps its text (G25)");
            Assert.AreEqual("Can't paste: the clipboard holds no text to paste.", shown.Said());
        });
    }

    [TestMethod]
    public async Task ShiftInsertWithWordsLeavesTheFieldAndLayerAlone()
    {
        using var shown = Open();
        using var editor = new ShownEditor(shown);
        await OpenMeme(shown, editor);
        TestClipboard.PutText("so true");

        TrayDesktop.Run(() =>
        {
            Type(editor.Field!, "keep");
            Tap(editor.Field!, Key.Insert, KeyModifiers.Shift);
        });

        Assert.HasCount(1, (await shown.Listing.Store!.All()).Value!);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            Assert.AreEqual("KEEP", editor.Field?.Text, "Shift+Insert leaves the field alone (N9, R5)");
            Assert.AreEqual("KEEP", ((TextLayer)editor.Session!.Layers.Layers.Single()).Text);
            Assert.IsEmpty(shown.Notices.Shown);
        }, DispatcherPriority.Background);
    }

    [TestMethod]
    public async Task ADelayedPasteAfterCtrlEnterLeavesTheReopenedMemeAloneAndNamesTheDrop()
    {
        var read = new TaskCompletionSource<Outcome<ClipboardReading>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var clipboard = new PresetDrags { ReadClipboard = () => read.Task };
        using var shown = Open(drags: clipboard);
        using var editor = new ShownEditor(shown);
        await OpenMeme(shown, editor);

        TrayDesktop.Run(() =>
        {
            var field = editor.Field!;
            Type(field, "keep");
            shown.Window.UpdateLayout();
            Tap(field, Key.V, KeyModifiers.Control);
            Assert.AreEqual(1, clipboard.Reads);
            Assert.AreEqual("KEEP", field.Text, "the read has not answered yet");
            Tap(field, Key.Enter, KeyModifiers.Control);
            Assert.IsNull(editor.Field);

            var box = ((TextLayer)editor.Session!.SelectedLayer!).Box;
            editor.DoubleClick(editor.OnCanvas(box.X + box.Width / 2d, box.Y + box.Height / 2d));
            Assert.IsNotNull(editor.Field);
            Assert.AreSame(editor.Field, shown.Focused);
            editor.Field.SelectAll();
        });

        read.SetResult(Outcome<ClipboardReading>.Of(new ClipboardReading([], null, null, false, "dropped words")));
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            Assert.AreEqual("KEEP", editor.Field!.Text);
            Assert.AreEqual("KEEP", ((TextLayer)editor.Session!.SelectedLayer!).Text);
            var notice = shown.Notices.Shown.Single();
            Assert.AreEqual("Paste failed: the editor field, it closed before the paste could complete", notice.Text);
            Assert.IsTrue(notice.NamesAProblem);
        }, DispatcherPriority.Background);
        Assert.HasCount(1, (await shown.Listing.Store!.All()).Value!);
    }

    private static async Task OpenMeme(ShownWindow shown, ShownEditor editor)
    {
        var item = (await shown.OpenStash(shown.Picture("editor original.png", 200, 150, (_, _) => SKColors.Gray))).Single();
        TransferredStash.CloseNotices(shown);
        await EditorOverlayTests.Open(shown, item);
        TrayDesktop.Run(() =>
        {
            shown.Window.UpdateLayout();
            editor.Click(0.5, 0.25);
            Assert.IsNotNull(editor.Field);
            Assert.AreSame(editor.Field, shown.Focused);
            Assert.AreEqual(TextPreset.Meme, ((TextLayer)editor.Session!.Layers.Layers.Single()).Preset);
        });
    }
}
