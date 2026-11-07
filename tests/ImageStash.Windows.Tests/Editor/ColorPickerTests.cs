using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.VisualTree;
using ImageStash.Editor;
using ImageStash.Layers;
using ImageStash.Media;
using ImageStash.Store;
using ImageStash.Windows.Tests.Support;
using SkiaSharp;

namespace ImageStash.Windows.Tests.Editor;

/// <summary>
/// The colour picker the More swatch opens (G21, F4, N9, R5): over the editor inside the stash window, never a window of
/// its own, with the square, the hue strip, the hex, Pick from the picture and Recent; a hex typed and entered colouring
/// the selected mark and showing inside the More swatch and in Recent; the hex with no paste of its own, Ctrl+V in it
/// being the window's paste; Esc closing it; the eyedropper sampling the picture as composed, not the screen; a hue chosen
/// on the strip from a white kept for the square, a hex then naming its own hue; at the window's minimum, every recent
/// colour kept, Auto, None, the hex and Pick from the picture whole and under the pointer inside the card, and the card
/// inside the window as Recent fills while it shows.
/// </summary>
[TestClass]
public sealed class ColorPickerTests
{
    [TestMethod]
    public async Task AHexTypedAndEnteredColoursTheSelectedMarkShowsInsideTheMoreSwatchAndInRecentAndThePasteIsTheWindowsNotTheHexs()
    {
        using var shown = ShownWindow.Open();
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
        await OpenThePicker(shown, editor);
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => Picker(editor) is not null));

        TrayDesktop.Run(() =>
        {
            var content = Picker(editor)!;
            Assert.AreSame(shown.Window, TopLevel.GetTopLevel(content), "inside the stash window (F4)");
            Assert.AreEqual(windowsBefore, ProcessWindows.Visible(), "never a window of its own (F4)");
            Assert.IsTrue(Named(content, "Pick from the picture") is Button, "the eyedropper");
            Assert.Contains("Recent", content.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text).ToList());
            var hex = (TextBox)Named(content, "Hex")!;
            hex.Focus();
            hex.SelectAll();
            ShownWindow.Type(hex, "#FF7A1A");
            ShownWindow.Press(hex, Key.Enter);

            var orange = new Rgba(255, 122, 26, 255);
            Assert.AreEqual(orange, ((DrawMarkLayer)editor.Session!.Layers.Layers.Single()).Color, "the selected box takes the colour (G21)");
            Assert.AreEqual(Color.FromRgb(255, 122, 26), MoreSwatchFill(editor), "the More swatch shows it");
            Assert.Contains("#FF7A1A", content.GetVisualDescendants().OfType<Button>().Select(AutomationProperties.GetName).ToList(), "Recent holds it");
        });

        // N9, R5: the hex has no paste of its own; Ctrl+V in it is the window's paste, which reads the clipboard's text as nothing to add.
        TestClipboard.PutText("#123456");
        TrayDesktop.Run(() =>
        {
            var hex = (TextBox)Named(Picker(editor)!, "Hex")!;
            hex.Focus();
            ShownWindow.Press(hex, Key.V, KeyModifiers.Control);
            Assert.AreEqual("#FF7A1A", hex.Text, "nothing pasted into the hex");
        });
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Said().Contains("it holds no picture or file")));

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
    public async Task PickFromThePictureSamplesThePictureAsComposedUnderTheClick()
    {
        using var shown = ShownWindow.Open();
        using var editor = new ShownEditor(shown);
        var items = await shown.OpenStash(shown.Picture("checker.png", 64, 48, EditorCanvasTests.Checker));
        await EditorOverlayTests.Open(shown, items[0]);
        TrayDesktop.Run(() =>
        {
            editor.Choose("Hide");
            editor.Choose("Black box");
            editor.Drag((0.25, 0.25), (0.75, 0.75));
            ShownWindow.Press(editor.Canvas, Key.Escape);
            editor.Choose("Draw");
        });
        await OpenThePicker(shown, editor);
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => Picker(editor) is not null));

        TrayDesktop.Run(() =>
        {
            ShownWindow.RaiseClick((Button)Named(Picker(editor)!, "Pick from the picture")!);
            Assert.IsNull(Picker(editor), "the picker steps aside for the click on the picture");
            Assert.IsTrue(editor.Canvas.Picking);
            editor.Click(0.5, 0.5);
            Assert.IsFalse(editor.Canvas.Picking);
            Assert.IsEmpty(editor.Session!.Layers.Layers.OfType<DrawMarkLayer>(), "the click sampled, and drew nothing");
            Assert.AreEqual(Color.FromRgb(0, 0, 0), MoreSwatchFill(editor), "the black the hide covers there, not the checker beneath it (G21)");
            editor.Drag((0.05, 0.05), (0.2, 0.05));
            Assert.AreEqual(new Rgba(0, 0, 0, 255), editor.Session.Layers.Layers.OfType<DrawMarkLayer>().Single().Color, "the pointer is the tools' again, drawing in the sampled colour");
        });
    }

    [TestMethod]
    public async Task OnATextTheSwatchesAndMoreColourItsLettersAndTheOutlineSwatchOpensThePickerWithAutoAndNone()
    {
        using var shown = ShownWindow.Open();
        using var editor = new ShownEditor(shown);
        var items = await shown.OpenStash(shown.Picture("grey.png", 200, 150, (_, _) => TextFieldTests.Grey));
        await EditorOverlayTests.Open(shown, items[0]);
        var blue = ColorChoices.Swatches[4];
        TrayDesktop.Run(() =>
        {
            editor.Click(0.5, 0.25);
            ShownWindow.Type(editor.Field!, "hi");
            ShownWindow.Press(editor.Field!, Key.Enter, KeyModifiers.Control);
            editor.Choose(ColorChoices.Hex(blue));
            Assert.AreEqual(blue, Text(editor).Fill, "the swatch colours the text (G21)");
            Assert.IsTrue(((ToggleButton)editor.Button(ColorChoices.Hex(blue))).IsChecked, "and is checked");
        });
        await OpenThePicker(shown, editor);
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => Picker(editor) is not null));
        TrayDesktop.Run(() =>
        {
            Assert.IsTrue(Named(Picker(editor)!, "Auto") is null && Named(Picker(editor)!, "None") is null, "the fill's picker offers no Auto or None");
            var hex = (TextBox)Named(Picker(editor)!, "Hex")!;
            hex.Focus();
            hex.SelectAll();
            ShownWindow.Type(hex, "#FF7A1A");
            ShownWindow.Press(hex, Key.Enter);
            Assert.AreEqual(new Rgba(255, 122, 26, 255), Text(editor).Fill, "a hex colours the text");
            Assert.AreEqual(Color.FromRgb(255, 122, 26), MoreSwatchFill(editor), "shown inside More");
            ShownWindow.Press(hex, Key.Escape);
            Assert.IsNull(Picker(editor));
        });

        await Task.Delay(200);
        TrayDesktop.Run(() => shown.Click(editor.Button("Outline color")));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => Picker(editor) is not null));
        TrayDesktop.Run(() =>
        {
            Assert.IsTrue(Named(Picker(editor)!, "Auto") is ToggleButton { IsChecked: true }, "the outline's picker offers Auto, the Meme's own (G21)");
            ShownWindow.RaiseClick((Button)Named(Picker(editor)!, "None")!);
            Assert.AreEqual(Outline.None, Text(editor).Outline, "None");
            Assert.IsTrue(OutlineSwatchShows(editor).Struck, "the swatch struck through");
            ShownWindow.RaiseClick((Button)Named(Picker(editor)!, "Auto")!);
            Assert.AreEqual(Outline.Auto, Text(editor).Outline, "Auto");
            var auto = LayerComposer.AutoOutline(Text(editor).Fill);
            Assert.AreEqual(ToColor(auto), OutlineSwatchShows(editor).Ring, "the swatch's ring in Auto's colour for the fill");
            Assert.IsFalse(OutlineSwatchShows(editor).Struck);
            var hex = (TextBox)Named(Picker(editor)!, "Hex")!;
            hex.Focus();
            hex.SelectAll();
            ShownWindow.Type(hex, "#00C000");
            ShownWindow.Press(hex, Key.Enter);
            Assert.AreEqual(Outline.Of(new Rgba(0, 0xC0, 0, 255)), Text(editor).Outline, "a colour of its own");
            Assert.IsFalse(Named(Picker(editor)!, "Auto") is ToggleButton { IsChecked: true } || Named(Picker(editor)!, "None") is ToggleButton { IsChecked: true }, "neither Auto nor None then");
            Assert.AreEqual(Color.FromRgb(0, 0xC0, 0), OutlineSwatchShows(editor).Ring);
        });
    }

    [TestMethod]
    public async Task TheEyedropperColoursWhatThePickerWasOpenedForTheOutlineOrTheFill()
    {
        using var shown = ShownWindow.Open();
        using var editor = new ShownEditor(shown);
        var red = new Rgba(220, 30, 30, 255);
        var items = await shown.OpenStash(shown.Picture("patch.png", 200, 150, (x, _) => x >= 150 ? new SKColor(red.R, red.G, red.B) : TextFieldTests.Grey));
        await EditorOverlayTests.Open(shown, items[0]);
        var yellow = ColorChoices.Swatches[2];
        TrayDesktop.Run(() =>
        {
            editor.Choose("Label");
            editor.Click(0.05, 0.1);
            ShownWindow.Type(editor.Field!, "hi");
            ShownWindow.Press(editor.Field!, Key.Enter, KeyModifiers.Control);
            editor.Choose(ColorChoices.Hex(yellow));
        });

        await Task.Delay(200);
        TrayDesktop.Run(() => shown.Click(editor.Button("Outline color")));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => Picker(editor) is not null));
        TrayDesktop.Run(() =>
        {
            ShownWindow.RaiseClick((Button)Named(Picker(editor)!, "Pick from the picture")!);
            editor.Click(0.9, 0.5);
            var label = Text(editor);
            Assert.IsTrue(label.Outline == Outline.Of(red) && label.Fill == yellow, $"the outline sampled, the fill as it was: {label.Outline}, {label.Fill} (G21)");
            Assert.IsTrue(editor.Session!.Layers.Layers.Count == 1 && editor.Session.Selected == 0, "no layer added, the label still selected");
            Assert.IsNull(Picker(editor), "the picker closed");
        });

        await OpenThePicker(shown, editor);
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => Picker(editor) is not null));
        TrayDesktop.Run(() =>
        {
            ShownWindow.RaiseClick((Button)Named(Picker(editor)!, "Pick from the picture")!);
            editor.Click(0.9, 0.8);
            var label = Text(editor);
            Assert.IsTrue(label.Fill == red && label.Outline == Outline.Of(red), "from More, the fill sampled and the outline as it was");
            Assert.HasCount(1, editor.Session!.Layers.Layers);
        });
    }

    [TestMethod]
    public async Task AHueChosenOnTheStripFromAWhiteFillSurvivesTheReleaseAndTheSquareThenGivesThatHueAHexGivesItsOwn()
    {
        using var shown = ShownWindow.Open();
        using var editor = new ShownEditor(shown);
        var items = await shown.OpenStash(shown.Picture("grey.png", 200, 150, (_, _) => TextFieldTests.Grey));
        await EditorOverlayTests.Open(shown, items[0]);
        TrayDesktop.Run(() =>
        {
            editor.Click(0.5, 0.25);
            ShownWindow.Type(editor.Field!, "hi");
            ShownWindow.Press(editor.Field!, Key.Enter, KeyModifiers.Control);
            Assert.AreEqual(new Rgba(255, 255, 255, 255), Text(editor).Fill, "a Meme starts white, which carries no hue");
        });
        await OpenThePicker(shown, editor);
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => Picker(editor) is not null));
        TrayDesktop.Run(() =>
        {
            var picker = Picker(editor)!;
            var strip = picker.GetVisualDescendants().OfType<Control>().Single(control => control.GetType().Name == "HueStrip");
            var square = picker.GetVisualDescendants().OfType<Control>().Single(control => control.GetType().Name == "ValueSquare");
            // The strip's middle is cyan, hue 180; the square's middle half saturation and value.
            shown.Click(strip);
            Assert.AreEqual(new Rgba(255, 255, 255, 255), Text(editor).Fill, "a hue alone leaves a white white");
            shown.Click(square);
            var fill = Text(editor).Fill;
            var hue = ColorChoices.ToHsv(fill).Hue;
            Assert.IsTrue(Math.Abs(hue - 180) <= 1 && fill.G > fill.R && fill.B > fill.R, $"the square gives the strip's hue: {fill}, hue {hue:F1} (G21)");

            var hex = (TextBox)Named(picker, "Hex")!;
            hex.Focus();
            hex.SelectAll();
            ShownWindow.Type(hex, "#FF0000");
            ShownWindow.Press(hex, Key.Enter);
            Assert.AreEqual(new Rgba(255, 0, 0, 255), Text(editor).Fill, "a hex names a colour");
            shown.Click(square);
            Assert.AreEqual(new Rgba(128, 64, 64, 255), Text(editor).Fill, "and its own hue, red's, which the square then gives");
        });
    }

    [TestMethod]
    public async Task AtTheMinimumEveryRecentColourAutoNoneTheHexAndPickFromThePictureShowWholeInsideThePickersCardInsideTheWindow()
    {
        using var shown = ShownWindow.Open();
        using var editor = new ShownEditor(shown);
        var items = await shown.OpenStash(shown.Picture("grey.png", 200, 150, (_, _) => TextFieldTests.Grey));
        await EditorOverlayTests.Open(shown, items[0]);
        TrayDesktop.Run(() =>
        {
            shown.Window.Width = 1;
            shown.Window.Height = 1;
            editor.Click(0.5, 0.25);
            ShownWindow.Type(editor.Field!, "hi");
            ShownWindow.Press(editor.Field!, Key.Enter, KeyModifiers.Control);
            // The import's notice would lie over the picker's foot for its six seconds; the user's × closes it.
            foreach (var close in shown.Window.GetVisualDescendants().OfType<Button>().Where(button => button.IsEffectivelyVisible && AutomationProperties.GetName(button) == "Close" && button.GetVisualAncestors().Any(parent => parent.GetType().Name == "NoticeCard")).ToList())
            {
                ShownWindow.RaiseClick(close);
            }
        });

        // Opened with no recent colour yet; every colour the editor keeps, and one more, then entered in its hex as a
        // person would, the card growing as Recent fills.
        var clipped = new List<string>();
        await Task.Delay(200);
        TrayDesktop.Run(() => shown.Click(editor.Button("Outline color")));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => Picker(editor) is not null));
        TrayDesktop.Run(() =>
        {
            Assert.AreEqual(WindowSize.Minimum, new WindowSize(shown.Window.Bounds.Width, shown.Window.Bounds.Height), "the window at its minimum");
            var hex = (TextBox)Named(Picker(editor)!, "Hex")!;
            foreach (var step in Enumerable.Range(1, ColorChoices.RecentCount + 1))
            {
                hex.Focus();
                hex.SelectAll();
                ShownWindow.Type(hex, ColorChoices.Hex(new Rgba((byte)(step * 30), 0x50, 0x90, 0xFF)));
                ShownWindow.Press(hex, Key.Enter);
                shown.Window.UpdateLayout();
                var card = (Control)Picker(editor)!.GetVisualParent()!;
                clipped.AddRange(Outside(shown.Window, card, $"with {step} recent colours entered while it shows: the card"));
            }

            Assert.HasCount(ColorChoices.RecentCount, editor.Session!.Colors.Recent);
            ShownWindow.Press(hex, Key.Escape);
        });

        foreach (var (tool, opener) in new[] { ("Text", "Outline color"), ("Text", "More colors"), ("Draw", "More colors") })
        {
            TrayDesktop.Run(() =>
            {
                editor.Choose(tool);
                shown.Window.UpdateLayout();
                ShownWindow.RaiseClick(editor.Button(opener));
            });

            // A hit lands on what was last rendered.
            await Task.Delay(200);
            TrayDesktop.Run(() =>
            {
                shown.Window.UpdateLayout();
                var picker = Picker(editor)!;
                var card = (Control)picker.GetVisualParent()!;
                var what = $"{tool}'s {opener}";
                clipped.AddRange(Outside(shown.Window, card, $"from {what}: the card"));
                var swatches = picker.GetVisualDescendants().OfType<Button>().Where(button => (AutomationProperties.GetName(button) ?? "").StartsWith('#')).ToList();
                Assert.HasCount(ColorChoices.RecentCount, swatches, $"from {what}: every kept recent colour offered (G21)");
                var offered = swatches.Cast<Control>().Append(Named(picker, "Hex")!).Append(Named(picker, "Pick from the picture")!);
                if (opener == "Outline color")
                {
                    offered = offered.Append(Named(picker, "Auto")!).Append(Named(picker, "None")!);
                }

                foreach (var control in offered)
                {
                    clipped.AddRange(Outside(card, control, $"from {what}: {AutomationProperties.GetName(control)}"));
                    var centre = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), shown.Window)!.Value;
                    if (shown.Window.InputHitTest(centre) is not Visual hit || !(ReferenceEquals(hit, control) || control.IsVisualAncestorOf(hit)))
                    {
                        clipped.Add($"from {what}: a click at {AutomationProperties.GetName(control)}'s centre lands on {shown.Window.InputHitTest(centre)?.GetType().Name ?? "nothing"}");
                    }
                }

                foreach (var words in picker.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible && !string.IsNullOrEmpty(text.Text) && !text.GetVisualAncestors().OfType<TextBox>().Any()))
                {
                    var holder = words.GetVisualAncestors().OfType<Button>().FirstOrDefault() as Control ?? card;
                    clipped.AddRange(Outside(holder, words, $"from {what}: the words \"{words.Text}\""));
                    clipped.AddRange(Outside(card, words, $"from {what}: the words \"{words.Text}\""));
                    if (!Whole(words))
                    {
                        clipped.Add($"from {what}: \"{words.Text}\" cut, {words.Bounds.Width:F1} wide for {Unconstrained(words):F1}");
                    }
                }

                ShownWindow.Press(editor.Canvas, Key.Escape);
                Assert.IsNull(Picker(editor));
            });
        }

        Assert.IsEmpty(clipped, "whole inside the card inside the window at the minimum (G20, G21): " + string.Join("; ", clipped));
    }

    /// <summary>What says <paramref name="control"/> does not show with its whole rectangle inside <paramref name="holder"/>; nothing when it does.</summary>
    private static IEnumerable<string> Outside(Control holder, Control control, string what)
    {
        var at = control.TranslatePoint(default, holder)!.Value;
        if (!(control.IsEffectivelyVisible && at.X >= -0.5 && at.Y >= -0.5 && at.X + control.Bounds.Width <= holder.Bounds.Width + 0.5 && at.Y + control.Bounds.Height <= holder.Bounds.Height + 0.5))
        {
            yield return $"{what} at {new Rect(at, control.Bounds.Size)} outside {holder.GetType().Name} {holder.Bounds.Size}";
        }
    }

    /// <summary>Whether <paramref name="words"/> has room for every line it lays out, none trimmed, and on one line for all of its text when it does not wrap.</summary>
    private static bool Whole(TextBlock words) =>
        !words.TextLayout.TextLines.Any(line => line.HasCollapsed)
        && words.TextLayout.Width <= words.Bounds.Width + 0.5
        && words.TextLayout.Height <= words.Bounds.Height + 0.5
        && (words.TextWrapping != TextWrapping.NoWrap || Unconstrained(words) <= words.Bounds.Width + 0.5);

    /// <summary>How wide <paramref name="words"/> lays out on one line with no limit.</summary>
    private static double Unconstrained(TextBlock words)
    {
        using var layout = new TextLayout(words.Text, new Typeface(words.FontFamily, words.FontStyle, words.FontWeight), words.FontSize, null);
        return layout.Width;
    }

    /// <summary>A click on the More swatch, once the row the tool brought in has been drawn, so a click can land on it.</summary>
    private static async Task OpenThePicker(ShownWindow shown, ShownEditor editor)
    {
        await Task.Delay(200);
        TrayDesktop.Run(() => shown.Click(editor.Button("More colors")));
    }

    /// <summary>The one text layer.</summary>
    private static TextLayer Text(ShownEditor editor) => editor.Session!.Layers.Layers.OfType<TextLayer>().Single();

    /// <summary>What the outline swatch shows: its ring's colour, and whether None's stroke crosses it.</summary>
    private static (Color? Ring, bool Struck) OutlineSwatchShows(ShownEditor editor)
    {
        var swatch = editor.Button("Outline color");
        var ring = swatch.GetVisualDescendants().OfType<Ellipse>().Single();
        return ((ring.Stroke as ISolidColorBrush)?.Color, swatch.GetVisualDescendants().OfType<Line>().Single().IsEffectivelyVisible);
    }

    private static Color ToColor(Rgba color) => Color.FromArgb(color.A, color.R, color.G, color.B);

    /// <summary>The colour picker while it shows.</summary>
    private static Control? Picker(ShownEditor editor) =>
        editor.Overlay.GetVisualDescendants().OfType<Control>().FirstOrDefault(control => control.IsEffectivelyVisible && AutomationProperties.GetName(control) == "Color picker");

    private static Control? Named(Control root, string words) =>
        root.GetVisualDescendants().OfType<Control>().FirstOrDefault(control => AutomationProperties.GetName(control) == words);

    private static Color MoreSwatchFill(ShownEditor editor) =>
        ((ISolidColorBrush)((Border)((ContentControl)editor.Button("More colors")).Content!).Background!).Color;
}
