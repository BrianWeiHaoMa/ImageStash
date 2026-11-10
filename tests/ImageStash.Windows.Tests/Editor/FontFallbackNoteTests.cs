using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using ImageStash.Editor;
using ImageStash.Files;
using ImageStash.Layers;
using ImageStash.Media;
using ImageStash.Store;
using ImageStash.Windows.Tests.Support;

namespace ImageStash.Windows.Tests.Editor;

/// <summary>
/// The line under the editor's header naming a font this PC lacks (G21, E3): absent while every font of the picture is
/// installed; on an item saved with a text in a family this PC does not have, there once the canvas has drawn it; gone
/// once the text's font is changed to one this PC has; with forty such families, no taller than with one, so the footer
/// stays inside the window at its minimum size (G20).
/// </summary>
[TestClass]
public sealed class FontFallbackNoteTests
{
    private const string Missing = "No Such Font";

    [TestMethod]
    public async Task AnItemSavedWithAFontThisPCLacksIsNamedUnderTheHeaderUntilItsTextTakesAnInstalledOne()
    {
        using var shown = ShownWindow.Open();
        using var editor = new ShownEditor(shown);
        var item = (await shown.OpenStash(shown.Picture("grey.png", 200, 150, (_, _) => TextFieldTests.Grey))).Single();
        await EditorOverlayTests.Open(shown, item);
        TrayDesktop.Run(() =>
        {
            editor.Click(0.5, 0.25);
            ShownWindow.Type(editor.Field!, "hi");
            ShownWindow.Press(editor.Field!, Key.Enter, KeyModifiers.Control);
        });
        await RenderedWindow.Of(shown);
        await Task.Delay(300);
        TrayDesktop.Run(() =>
        {
            Assert.IsFalse(editor.Texts().Any(text => text.Contains("not installed")), "no line while every font is installed");
            // A family no menu offers on this PC, as a list saved on another PC carries it (E3).
            ((TextTool)editor.Session!.Tool).Choose(Missing);
            ShownWindow.Press(editor.Canvas, Key.S, KeyModifiers.Control);
        });
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => !shown.Editing.IsOpen && shown.Grid.IsFocused));

        await EditorOverlayTests.Open(shown, TrayDesktop.Get(() => shown.Listing.Items.Single()));
        await RenderedWindow.Of(shown);
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => editor.Texts().Any(text => text.Contains(Missing))));
        TrayDesktop.Run(() =>
        {
            Assert.AreEqual(Missing, ((TextLayer)editor.Session!.Layers.Layers.Single()).Font, "saved in that family, whatever this PC has");
            editor.Click(0.5, 0.1);
            editor.ClickMenuButton("Font");
            editor.ClickMenuEntry("Impact");
        });
        await RenderedWindow.Of(shown);
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => !editor.Texts().Any(text => text.Contains(Missing))));
    }

    [TestMethod]
    public async Task FortyMissingFamiliesTakeNoMoreRoomThanOneAndTheFooterStaysInsideTheWindowAtItsMinimum()
    {
        using var shown = ShownWindow.Open();
        using var editor = new ShownEditor(shown);
        var items = await shown.OpenStash(shown.Picture("one.png", 200, 150, (_, _) => TextFieldTests.Grey), shown.Picture("forty.png", 201, 150, (_, _) => TextFieldTests.Grey));
        // As lists saved on another PC carry them (E3): one text in a family this PC lacks, and forty, each in its own.
        await SaveTexts(shown, items[0], [Missing]);
        await SaveTexts(shown, items[1], Enumerable.Range(1, 40).Select(i => $"{Missing} {i}").ToList());
        await TrayDesktop.Get(() => shown.Listing.Refresh());
        TrayDesktop.Run(() =>
        {
            shown.Window.Width = 1;
            shown.Window.Height = 1;
        });

        var one = await NoteAtTheMinimum(shown, editor, items[0].Id, Missing);
        await EditorOverlayTests.Cancelled(shown);
        var forty = await NoteAtTheMinimum(shown, editor, items[1].Id, $"{Missing} 40");

        Assert.IsLessThanOrEqualTo(one.Height + 0.5, forty.Height, $"forty families no taller than one at the minimum width: {forty.Height}, not {one.Height}");
        Assert.AreEqual(2, forty.Lines, "two lines");
        Assert.IsTrue(forty.CutShort, "what does not fit ends in an ellipsis");
        Assert.IsTrue(forty.Text.StartsWith("These fonts are not installed on this PC", StringComparison.Ordinal), $"what is said of them comes first, so it shows: {forty.Text}");
        Assert.IsTrue(forty.Tip?.Contains($"{Missing} 1,") == true && forty.Tip.Contains($"{Missing} 40"), "every family in its tooltip");
    }

    /// <summary>Saves over <paramref name="item"/>, through the store as Save does, a Label in each of <paramref name="families"/>.</summary>
    private static async Task SaveTexts(ShownWindow shown, StashItem item, IReadOnlyList<string> families)
    {
        var layers = new LayerList(families.Select((family, i) => (Layer)new TextLayer(TextPreset.Label, "hi", family, 12, ColorChoices.Swatches[0], Outline.Auto, new ImageStash.Layers.PixelRect(10, 3 * i, 40, 14), 0, null)).ToList());
        var store = TrayDesktop.Get(() => shown.Listing.Store!);
        var edit = (await store.ForEdit(item.Id)).Value!;
        var saved = await SavedEdit.Save(store, item, edit, layers, FinishedPicture.Render, HeldFileRetry.Duration);
        Assert.IsTrue(saved.Succeeded, $"saved: {saved.Failure}");
    }

    /// <summary>
    /// Opens the editor on the item and waits for the line naming <paramref name="named"/> among the missing families;
    /// checks that the window is at its minimum with the line, Cancel, the output size and Save inside it; answers the
    /// line's height, its words, how many lines they take, whether the last is cut short, and its tooltip.
    /// </summary>
    private static async Task<(double Height, string Text, int Lines, bool CutShort, string? Tip)> NoteAtTheMinimum(ShownWindow shown, ShownEditor editor, long id, string named)
    {
        await EditorOverlayTests.Open(shown, TrayDesktop.Get(() => shown.Listing.Items.Single(item => item.Id == id)));
        await RenderedWindow.Of(shown);
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => Note(editor) is { } note && ToolTip.GetTip(note) is string tip && tip.Contains(named)));
        return TrayDesktop.Get(() =>
        {
            shown.Window.UpdateLayout();
            var window = shown.Window.Bounds;
            WindowMinimum.AssertAt(new Size(window.Width, window.Height), "the window asked 1 × 1 at its minimum");
            var note = Note(editor)!;
            foreach (var control in new Control[] { note, editor.Button("Cancel"), editor.Button(EditorOverlay.OutputSize), editor.Button("Save") })
            {
                var at = control.TranslatePoint(default, shown.Window)!.Value;
                Assert.IsTrue(at.Y >= 0 && at.Y + control.Bounds.Height <= window.Height + 0.5, $"{control} at {new Rect(at, control.Bounds.Size)} inside the {window.Size} window (G20)");
            }

            var lines = note.TextLayout.TextLines;
            return (note.Bounds.Height, note.Text!, lines.Count, lines[^1].HasCollapsed, ToolTip.GetTip(note) as string);
        });
    }

    /// <summary>The line under the header naming the missing families, while it shows.</summary>
    private static TextBlock? Note(ShownEditor editor) =>
        editor.Overlay.GetVisualDescendants().OfType<TextBlock>().SingleOrDefault(text => text.IsEffectivelyVisible && (text.Text ?? "").Contains("not installed"));
}
