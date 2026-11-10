using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.VisualTree;
using ImageStash.Editor;
using ImageStash.Layers;
using ImageStash.Media;
using ImageStash.Store;
using ImageStash.Windows.Seams;
using ImageStash.Windows.Tests.Support;

namespace ImageStash.Windows.Tests.Editor;

/// <summary>
/// Text's option row on the real window (G20, G21, F8, F4, F13, G25): with nothing selected the presets and "Click the
/// picture to add text"; with a text selected also the font button, the five fill swatches, More and the outline swatch,
/// and what the mouse does; with a hide selected, Hide's row instead; the font menu's ten in F8's order, each in its
/// face, then All fonts… listing the Fonts seam's families in a list that scrolls inside the window, stays open as fonts
/// are chosen from it and checks the chosen one, which the menu then shows checked after the ten; the editor's keys
/// with the menu open; the row wrapping at the flyout's width with nothing clipped; the window asked smaller than its
/// minimum staying at it, the wrapped row, the line naming a missing font, the canvas, the footer and the row's
/// popovers inside it.
/// </summary>
[TestClass]
public sealed class TextOptionRowTests
{
    private static readonly string[] F8 = ["Impact", "Arial Black", "Segoe UI", "Comic Sans MS", "Georgia", "Consolas", "Bahnschrift", "Segoe Script", "Ink Free", "Verdana"];

    [TestMethod]
    public async Task NothingSelectedShowsThePresetsAndTheHintATextSelectedAlsoItsFontSwatchesAndOutlineAndAHideSelectedHidesRow()
    {
        using var shown = ShownWindow.Open();
        using var editor = new ShownEditor(shown);
        var item = (await shown.OpenStash(shown.Picture("grey.png", 200, 150, (_, _) => TextFieldTests.Grey))).Single();
        await EditorOverlayTests.Open(shown, item);
        TrayDesktop.Run(() =>
        {
            CollectionAssert.AreEqual(new[] { "Text", "Crop", "Hide", "Draw" }, editor.Tools(), "Text first on the tool row (G20)");
            Assert.IsTrue(Checked(editor.Button("Text")), "and current at open");
            CollectionAssert.IsSubsetOf(new[] { "Meme", "Caption bar", "Label", "Click the picture to add text" }, editor.Texts(), "the presets and the hint (G21)");
            Assert.IsTrue(Checked(editor.Button("Meme")), "Meme for the next layer");
            Assert.IsFalse(OfTheSelected().Any(editor.Shows), "no font, swatch or outline with nothing selected (G21)");

            editor.Click(0.5, 0.25);
            Assert.IsTrue(editor.Session!.SelectedLayer is TextLayer { Preset: TextPreset.Meme }, "a click adds a Meme");
            Assert.IsTrue(Checked(editor.Button("Meme")));
            Assert.AreEqual("Impact", FontWords(editor), "the font button reads the layer's font (F8)");
            Assert.IsTrue(OfTheSelected().All(editor.Shows), "the font, five swatches, More and the outline swatch (G21)");
            Assert.Contains(editor.Session.Tool.Hint, editor.Texts(), "and what the mouse does (G20)");
            Assert.AreNotEqual("Click the picture to add text", editor.Session.Tool.Hint);

            ShownWindow.Type(editor.Field!, "hi");
            editor.Choose("Hide");
            editor.Drag((0.1, 0.6), (0.4, 0.9));
            Assert.IsTrue(editor.Session.SelectedLayer is HideLayer, "a hide selected");
            Assert.IsTrue(editor.Shows("Blur") && editor.Shows("Black box"), "its own row");
            Assert.IsFalse(editor.Shows("Meme") || OfTheSelected().Any(editor.Shows), "and nothing of Text's (G20: the row is the current tool's)");

            editor.Click(0.5, 0.1);
            Assert.IsTrue(editor.Session.SelectedLayer is TextLayer && Checked(editor.Button("Text")), "a click on the Meme selects it with Text current");
            Assert.IsTrue(OfTheSelected().All(editor.Shows), "and its row back");
            ShownWindow.Press(editor.Canvas, Key.Escape);
            Assert.IsFalse(OfTheSelected().Any(editor.Shows), "deselected, the presets and the hint again");
            Assert.Contains("Click the picture to add text", editor.Texts());
        });
    }

    [TestMethod]
    public async Task TheFontMenuListsF8sTenInTheirFacesThenAllFontsListingTheSeamsFamiliesWhichStaysOpenAndChecksTheOneChosen()
    {
        var fonts = new PresetFonts();
        using var shown = ShownWindow.Open(fonts: fonts);
        using var editor = new ShownEditor(shown);
        var item = (await shown.OpenStash(shown.Picture("grey.png", 200, 150, (_, _) => TextFieldTests.Grey))).Single();
        await EditorOverlayTests.Open(shown, item);
        var windowsBefore = ProcessWindows.Visible();
        TrayDesktop.Run(() =>
        {
            editor.Click(0.5, 0.25);
            ShownWindow.Type(editor.Field!, "hi");
            ShownWindow.Press(editor.Field!, Key.Enter, KeyModifiers.Control);
            CollectionAssert.AreEqual(F8.Append("—").Append("All fonts…").ToList(), editor.ClickMenuButton("Font"), "F8's ten in order, then All fonts…");
            foreach (var entry in editor.OpenMenu!.Items.OfType<MenuItem>().Where(entry => F8.Contains((string)entry.Header!)))
            {
                var face = Typefaces.Match((string)entry.Header!)!;
                Assert.AreEqual(face.FamilyName, entry.FontFamily.Name, $"{entry.Header} in its own face");
                Assert.AreEqual((FontWeight)face.FontWeight, entry.FontWeight, $"{entry.Header} at its own weight");
            }

            var arialBlack = editor.OpenMenu.Items.OfType<MenuItem>().Single(entry => (string)entry.Header! == "Arial Black");
            Assert.IsTrue(arialBlack.FontFamily.Name == "Arial" && arialBlack.FontWeight == FontWeight.Black, "Arial Black as Arial's Black style, not a fallback");
            editor.ClickMenuEntry("Comic Sans MS");
            Assert.AreEqual("Comic Sans MS", ((TextLayer)editor.Session!.Layers.Layers.Single()).Font, "the selected layer's font (F8)");
            Assert.AreEqual("Comic Sans MS", FontWords(editor));

            // The menu hands the editor its keys: Ctrl+Z undoes the font with it open (G25).
            editor.ClickMenuButton("Font");
            ShownWindow.Press(editor.OpenMenu!.Items.OfType<MenuItem>().First(), Key.Z, KeyModifiers.Control);
            Assert.AreEqual("Impact", ((TextLayer)editor.Session.Layers.Layers.Single()).Font, "Ctrl+Z with the menu open");
            Assert.IsFalse(editor.OpenMenu?.IsOpen ?? false, "and the menu steps aside");

            editor.ClickMenuButton("Font");
            editor.ClickMenuEntry("All fonts…");
        });
        await Task.Delay(200);
        TrayDesktop.Run(() =>
        {
            var list = (ScrollViewer)editor.Overlay.GetVisualDescendants().OfType<Control>().Single(control => control.IsEffectivelyVisible && AutomationProperties.GetName(control) == "All fonts");
            Assert.AreSame(shown.Window, TopLevel.GetTopLevel(list), "inside the stash window (F4)");
            Assert.AreEqual(windowsBefore, ProcessWindows.Visible(), "never a window of its own");
            var families = (IReadOnlyList<string>)list.GetVisualDescendants().OfType<ItemsControl>().Single().ItemsSource!;
            CollectionAssert.AreEqual(new WindowsFonts().Families().Append(fonts.Listed).ToList(), families.ToList(), "every family the Fonts seam names, in its order, read through the seam (F8, F13)");
            Assert.IsGreaterThan(list.Viewport.Height, list.Extent.Height, "a list taller than its place scrolls");
            var listed = list.TranslatePoint(default, shown.Window)!.Value;
            Assert.IsTrue(listed.Y >= 0 && listed.Y + list.Bounds.Height <= shown.Window.Bounds.Height, "within the window");

            var entries = list.GetVisualDescendants().OfType<ToggleButton>().Where(entry => entry.IsEffectivelyVisible && !F8.Contains(AutomationProperties.GetName(entry))).Take(2).ToList();
            Assert.HasCount(2, entries, "two families in view beside F8's ten");
            foreach (var chosen in entries)
            {
                var family = AutomationProperties.GetName(chosen);
                ShownWindow.RaiseClick(chosen);
                Assert.AreEqual(family, ((TextLayer)editor.Session!.Layers.Layers.Single()).Font, "a family chosen from All fonts…");
                Assert.IsTrue(editor.Overlay.GetVisualDescendants().Contains(list) && list.IsEffectivelyVisible, "and the list stays open, so the next can be tried");
                var shownChecked = list.GetVisualDescendants().OfType<ToggleButton>().Where(entry => entry.IsChecked == true).Select(AutomationProperties.GetName).ToList();
                CollectionAssert.AreEqual(new[] { family }, shownChecked, "the family chosen is the one checked");
                shown.Window.UpdateLayout();
                Assert.AreNotEqual(Ground(entries.First(entry => !ReferenceEquals(entry, chosen))), Ground(chosen), "and its check shows: its ground is not a flat entry's");
            }

            var (last, face) = (AutomationProperties.GetName(entries[^1])!, entries[^1].FontFamily.Name);
            ShownWindow.Press(editor.Canvas, Key.Escape);
            Assert.IsFalse(editor.Overlay.GetVisualDescendants().Contains(list), "Esc closes it");
            CollectionAssert.AreEqual(F8.Append(last).Append("—").Append("All fonts…").ToList(), editor.ClickMenuButton("Font"), "a family from All fonts… listed after the ten");
            var items = editor.OpenMenu!.Items.OfType<MenuItem>().ToList();
            CollectionAssert.AreEqual(new[] { last }, items.Where(entry => entry.IsChecked).Select(entry => (string)entry.Header!).ToList(), "and the one checked");
            Assert.AreEqual(face, items.Single(entry => (string)entry.Header! == last).FontFamily.Name, "in the face the list shows it in");
            editor.ClickMenuEntry("Georgia");
            CollectionAssert.AreEqual(F8.Append("—").Append("All fonts…").ToList(), editor.ClickMenuButton("Font"), "one of the ten chosen, the menu is F8's again");
            CollectionAssert.AreEqual(new[] { "Georgia" }, editor.OpenMenu!.Items.OfType<MenuItem>().Where(entry => entry.IsChecked).Select(entry => (string)entry.Header!).ToList());
        });
    }

    [TestMethod]
    public async Task TheFontMenusTenNamesAreDrawnEachInTheFaceALayerInThatFontTakes()
    {
        var fonts = new PresetFonts();
        using var shown = ShownWindow.Open(fonts: fonts);
        using var editor = new ShownEditor(shown);
        var item = (await shown.OpenStash(shown.Picture("grey.png", 200, 150, (_, _) => TextFieldTests.Grey))).Single();
        await EditorOverlayTests.Open(shown, item);
        TrayDesktop.Run(() =>
        {
            editor.Click(0.5, 0.25);
            editor.ClickMenuButton("Font");
        });
        await Task.Delay(200);
        TrayDesktop.Run(() =>
        {
            var listed = editor.OpenMenu!.Items.OfType<MenuItem>().Where(entry => F8.Contains((string)entry.Header!)).ToList();
            Assert.HasCount(F8.Length, listed);
            foreach (var entry in listed.Where(entry => fonts.IsInstalled((string)entry.Header!)))
            {
                AssertDrawnInItsOwnFace((string)entry.Header!, entry);
            }
        });
    }

    /// <summary>
    /// Checks that <paramref name="family"/>'s words in <paramref name="entry"/> are drawn in the face Media matches the
    /// name to, the one a layer in that font renders in (F10, G21), read from the glyph typefaces they were laid out with.
    /// </summary>
    private static void AssertDrawnInItsOwnFace(string family, Control entry)
    {
        var words = entry.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text == family);
        var face = Typefaces.Match(family)!;
        var drawn = words.TextLayout.TextLines.SelectMany(line => line.TextRuns).OfType<ShapedTextRun>().Select(run => run.GlyphRun.GlyphTypeface).ToList();
        // DirectWrite names a style's face by its own family where it has one: "Arial Black" is Arial's Black style.
        Assert.IsTrue(drawn.Count > 0 && drawn.All(glyphs => (glyphs.FamilyName == face.FamilyName || glyphs.FamilyName == family) && glyphs.Weight == (FontWeight)face.FontWeight),
            $"{family} drawn in {string.Join(", ", drawn.Select(glyphs => $"{glyphs.FamilyName} {glyphs.Weight}"))}, not its own face, {face.FamilyName} {(FontWeight)face.FontWeight}");
    }

    [TestMethod]
    public async Task AtTheFlyoutsWidthTheRowWrapsAndEveryControlShowsInsideTheWindow()
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
        await Task.Delay(200);
        TrayDesktop.Run(() => Assert.AreEqual(WindowSize.FlyoutDefault.Width, shown.Window.Bounds.Width, 1, "the flyout's default width"));
        AssertRowInside(shown, editor);
    }

    [TestMethod]
    public async Task AskedSmallerThanItsMinimumTheWindowStaysThereWithTheWrappedRowTheMissingFontLineTheCanvasAndTheFooterInside()
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
            // A family this PC lacks, as a list saved on another PC carries it, so the line under the header shows (E3).
            ((TextTool)editor.Session!.Tool).Choose("No Such Font");
        });
        await RenderedWindow.Of(shown);
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => editor.Texts().Any(text => text.Contains("No Such Font"))));

        // The editor's tallest state with Text: a Meme selected, its row wrapped, the line under the header shown.
        foreach (var (width, height) in new[] { (200.0, 360.0), (1.0, 1.0), (460.0, 240.0), (200.0, 640.0) })
        {
            TrayDesktop.Run(() =>
            {
                shown.Window.Width = width;
                shown.Window.Height = height;
            });
            await Task.Delay(200);
            TrayDesktop.Run(() =>
            {
                shown.Window.UpdateLayout();
                Assert.AreEqual(Math.Max(width, WindowSize.Minimum.Width), shown.Window.Bounds.Width, 0.5, $"asked {width} × {height}, no narrower than the minimum");
                Assert.AreEqual(Math.Max(height, WindowSize.Minimum.Height), shown.Window.Bounds.Height, 0.5, $"asked {width} × {height}, no shorter than the minimum");
                var note = editor.Overlay.GetVisualDescendants().OfType<TextBlock>().Single(text => text.IsEffectivelyVisible && (text.Text ?? "").Contains("No Such Font is not installed"));
                foreach (var control in new Control[] { note, editor.Button("Cancel"), editor.Button(EditorOverlay.OutputSize), editor.Button("Save") })
                {
                    AssertInside(shown, control, $"asked {width} × {height}: {AutomationProperties.GetName(control) ?? "the line naming the missing font"} (G20, G21)");
                }

                Assert.IsGreaterThanOrEqualTo(100.0, editor.Canvas.Bounds.Height, $"asked {width} × {height}: room for the picture (G20)");
            });
            AssertRowInside(shown, editor);
        }

        // At the minimum, each of the row's popovers lies inside the window too, the outline's picker the tallest.
        TrayDesktop.Run(() =>
        {
            shown.Window.Width = 1;
            shown.Window.Height = 1;
        });
        await Task.Delay(200);
        TrayDesktop.Run(() => WindowMinimum.AssertAt(shown.Window.Bounds.Size, "the popovers opened at the minimum"));
        foreach (var opener in new[] { "More colors", "Outline color" })
        {
            TrayDesktop.Run(() =>
            {
                ShownWindow.RaiseClick(editor.Button(opener));
                shown.Window.UpdateLayout();
                var picker = editor.Overlay.GetVisualDescendants().OfType<Control>().Single(control => control.IsEffectivelyVisible && AutomationProperties.GetName(control) == "Color picker");
                AssertInside(shown, (Control)picker.GetVisualParent()!, $"the colour picker from {opener} at the minimum (G21)");
                ShownWindow.Press(editor.Canvas, Key.Escape);
            });
        }

        TrayDesktop.Run(() =>
        {
            editor.ClickMenuButton("Font");
            editor.ClickMenuEntry("All fonts…");
        });
        await Task.Delay(300);
        TrayDesktop.Run(() =>
        {
            shown.Window.UpdateLayout();
            var list = editor.Overlay.GetVisualDescendants().OfType<ScrollViewer>().Single(control => control.IsEffectivelyVisible && AutomationProperties.GetName(control) == "All fonts");
            AssertInside(shown, (Control)list.GetVisualParent()!, "the All fonts… list at the minimum (G21)");
        });
    }

    /// <summary>Checks that <paramref name="control"/> shows with its whole rectangle inside the window.</summary>
    internal static void AssertInside(ShownWindow shown, Control control, string because)
    {
        var at = control.TranslatePoint(default, shown.Window)!.Value;
        Assert.IsTrue(control.IsEffectivelyVisible && at.X >= 0 && at.Y >= 0 && at.X + control.Bounds.Width <= shown.Window.Bounds.Width + 0.5 && at.Y + control.Bounds.Height <= shown.Window.Bounds.Height + 0.5,
            $"{because}: at {new Rect(at, control.Bounds.Size)} inside the {shown.Window.Bounds.Size} window");
    }

    /// <summary>Checks that every control of Text's row shows inside the window and that the row takes more than one line.</summary>
    private static void AssertRowInside(ShownWindow shown, ShownEditor editor) => TrayDesktop.Run(() =>
    {
        shown.Window.UpdateLayout();
        // The row itself is the option row's content, the groups inside it its children.
        var row = editor.Button("Meme").GetVisualAncestors().OfType<WrapPanel>().First(panel => panel.Parent is ContentControl);
        var controls = row.GetVisualDescendants().OfType<Button>().Cast<Control>().Concat(row.Children.OfType<TextBlock>()).ToList();
        Assert.IsGreaterThan(10, controls.Count);
        foreach (var control in controls)
        {
            AssertInside(shown, control, $"{AutomationProperties.GetName(control) ?? (control as TextBlock)?.Text} (G21)");
        }

        Assert.IsGreaterThan(40, row.Bounds.Height, "the row wraps onto more than one line");
    });

    private static bool Checked(Button button) => ((ToggleButton)button).IsChecked == true;

    /// <summary>The colour an entry's ground is drawn in, as its template's presenter has it, transparent for none.</summary>
    private static Color Ground(ToggleButton entry) =>
        entry.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.ContentPresenter>().First().Background is ISolidColorBrush { } ground ? ground.Color : Colors.Transparent;

    /// <summary>The words of the controls the row shows only with a text or caption-bar layer selected.</summary>
    private static IEnumerable<string> OfTheSelected() =>
        ColorChoices.Swatches.Select(ColorChoices.Hex).Append("Font").Append("More colors").Append("Outline color");

    /// <summary>The font button's words.</summary>
    internal static string FontWords(ShownEditor editor) =>
        ((Panel)editor.Button("Font").Content!).Children.OfType<TextBlock>().First().Text!;
}
