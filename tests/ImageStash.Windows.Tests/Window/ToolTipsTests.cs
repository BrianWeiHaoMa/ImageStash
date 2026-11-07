using Avalonia;
using Avalonia.Controls;
using Avalonia.Automation;
using Avalonia.Controls.Diagnostics;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.VisualTree;
using ImageStash.Editor;
using ImageStash.Look;
using ImageStash.Window;
using ImageStash.Windows.Tests.Editor;
using ImageStash.Windows.Tests.Support;
using SkiaSharp;

namespace ImageStash.Windows.Tests.Window;

/// <summary>
/// Every tooltip's placement, the mechanism behind a tip whole on a display of another scaling (E1), and each tip whole on
/// the display these checks have. One display cannot show the other scaling: the person's two displays are the acceptance.
/// And the rule, on the product's own controls: a control shown as a glyph alone names its action in its tip, a control
/// whose words are its action carries none, and a tip that adds a key or a role stays.
/// </summary>
[TestClass]
public sealed class ToolTipsTests
{
    [TestInitialize]
    public void UseProductTheme() => TrayDesktop.UseProductTheme();

    /// <summary>The popup window of the control's tip, once it has one.</summary>
    private static WindowBase? Root(Control control) => control.GetValue(ToolTipDiagnostics.ToolTipProperty) is Visual tip ? TopLevel.GetTopLevel(tip) as WindowBase : null;

    [TestMethod]
    public void EveryControlsTipIsPlacedAgainstItNeverAtThePointerWhichAvaloniaNeverSizesAgain() => TrayDesktop.Run(() =>
    {
        foreach (var control in new Control[] { new Button(), new TextBlock(), new ToggleButton() })
        {
            if (control is TextBlock)
            {
                ToolTipRule.ShowWhole(control, "Words");
            }
            else
            {
                ToolTipRule.Set(control, "Words");
            }

            Assert.AreEqual("Words", ToolTip.GetTip(control));
            Assert.AreEqual(PlacementMode.Bottom, ToolTip.GetPlacement(control));
            Assert.AreEqual(4, ToolTip.GetVerticalOffset(control));
        }
    });

    [TestMethod]
    [DataRow("Label")]
    [DataRow("Thin")]
    [DataRow("Medium")]
    [DataRow("Thick")]
    [DataRow("Add to your stash")]
    public async Task ATipShowsItsWholeWordOnOneLineInAPopupAsWideAsItsWords(string words)
    {
        var (window, button) = TrayDesktop.Get(() =>
        {
            var tipped = new Button { Content = "Tipped" };
            ToolTipRule.Set(tipped, words);
            var shown = new Avalonia.Controls.Window { Width = 300, Height = 200, ShowActivated = false, Position = new PixelPoint(-8000, 500), Content = tipped };
            shown.Show();
            return (shown, tipped);
        });
        try
        {
            TrayDesktop.Run(() => ToolTip.SetIsOpen(button, true));
            await ShownWindow.Eventually(() => TrayDesktop.Get(() => Root(button) is { IsVisible: true } root && root.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == words)));

            TrayDesktop.Run(() =>
            {
                var root = Root(button)!;
                root.UpdateLayout();
                var text = root.GetVisualDescendants().OfType<TextBlock>().Single(each => each.Text == words);
                Assert.HasCount(1, text.TextLayout.TextLines, $"{words} on one line, nothing wrapped out of sight");
                Assert.IsGreaterThanOrEqualTo(text.TextLayout.WidthIncludingTrailingWhitespace, text.Bounds.Width, $"{words}'s text box holds it");
                Assert.IsGreaterThanOrEqualTo(text.Bounds.Width, root.ClientSize.Width, $"{words}'s popup holds its text box at the popup's own scaling");
                ToolTip.SetIsOpen(button, false);
            });
        }
        finally
        {
            TrayDesktop.Run(window.Close);
        }
    }

    [TestMethod]
    public async Task AGlyphOnlyControlNamesItsActionInItsTipAndAControlShowingItsActionInWordsCarriesNone()
    {
        using var pointer = TestMouse.PutBackAfterwards();
        TestMouse.MoveTo(TestMouse.Away);
        using var shown = ShownWindow.Open();
        using var editor = new ShownEditor(shown);
        var items = await shown.OpenStash(shown.Picture("tips.png", 200, 150, (_, _) => SKColors.Gray));

        TrayDesktop.Run(() =>
        {
            Assert.AreEqual("Settings", Tip(shown.SettingsButton), "the gear");
            Assert.AreEqual("Add to your stash", Tip(shown.AddButton), "+");
            Assert.AreEqual("Keep open", Tip(Named(shown.Window, "Keep open")), "the pin");
            Assert.AreEqual("Every tag", Tip(shown.ChipRow.Children.OfType<Button>().Single(chip => AutomationProperties.GetName(chip) == "Every tag")), "the chip row's …");
            var closes = shown.Window.GetVisualDescendants().OfType<Button>().Where(button => AutomationProperties.GetName(button) == "Close").ToList();
            Assert.IsNotEmpty(closes, "the import's notice and its ×");
            Assert.IsTrue(closes.All(close => Tip(close) == "Close"), "a notice's ×");
            foreach (var scope in new[] { "Recent", "Favorites", "All" })
            {
                Assert.IsNull(Tip(shown.Chip(scope)), $"the scope chip {scope}, its words its action");
            }

            shown.OpenPreview(items[0]);
        });
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Preview.IsOpen));
        TrayDesktop.Run(() =>
        {
            Assert.AreEqual("Back (Esc)", Tip(shown.BackArrow), "the preview's back arrow, with its key");
            Assert.AreEqual("More options", Tip(shown.MoreButton), "the preview's ⋯");
            Assert.AreEqual("Rename (F2)", Tip(shown.NameButton), "the name, its act and key not in its words");
            Assert.IsNull(Tip(shown.HandoffButton), "the handoff button, named as the footer names it (G19)");
            Assert.IsNull(Tip(shown.CopyButton), "Copy");
            Assert.IsNull(Tip(shown.EditButton), "Edit");
            ShownWindow.Press(shown.PreviewPicture, Key.Escape);
        });
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => !shown.Preview.IsOpen));

        await EditorOverlayTests.Open(shown, items[0]);
        TrayDesktop.Run(() =>
        {
            Assert.AreEqual("Back (Esc)", Tip(editor.Button("Back")), "the editor's back arrow");
            Assert.AreEqual("More options", Tip(editor.Button("More options")), "the editor's ⋯");
            Assert.AreEqual("Hold to see the original (C)", Tip(editor.Button("Compare")), "Compare, held, with its key");
            Assert.AreEqual("The saved picture's size; Shrink it here", Tip(editor.Button(EditorOverlay.OutputSize)), "the size chip's role");
            foreach (var words in editor.Tools().Concat(["Meme", "Caption bar", "Label"]))
            {
                Assert.IsNull(Tip(editor.Button(words)), $"{words}, its words its action");
            }

            editor.Click(0.5, 0.25);
            ShownWindow.Type(editor.Field!, "hi");
            ShownWindow.Press(editor.Field!, Key.Enter, KeyModifiers.Control);
            foreach (var hex in ColorChoices.Swatches.Select(ColorChoices.Hex))
            {
                Assert.AreEqual(hex, Tip(editor.Button(hex)), "a swatch, a square named by its hex");
            }

            Assert.AreEqual("More colors", Tip(editor.Button("More colors")), "More, a rainbow square");
            Assert.AreEqual("Outline", Tip(editor.Button("Outline color")), "the outline swatch, a ring");
            Assert.AreEqual("Font", Tip(editor.Button("Font")), "the font button shows the family, its tip the role");
        });
        await EditorOverlayTests.ClickReaching(shown, editor, "Outline color");
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => editor.Overlay.GetVisualDescendants().OfType<Control>().Any(control => control.IsEffectivelyVisible && AutomationProperties.GetName(control) == "Auto")));
        TrayDesktop.Run(() =>
        {
            Assert.IsNull(Tip(Named(editor.Overlay, "Auto")), "the outline picker's Auto, its name beside its mark");
            Assert.IsNull(Tip(Named(editor.Overlay, "None")), "and None");
            ShownWindow.Press(Named(editor.Overlay, "Hex"), Key.Escape);
        });
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => !editor.Overlay.GetVisualDescendants().OfType<Control>().Any(control => control.IsEffectivelyVisible && AutomationProperties.GetName(control) == "Auto")));

        foreach (var (tool, plain, glyphs) in new[] { ("Draw", new[] { "Pen", "Marker", "Arrow", "Box" }, new[] { "Thin", "Medium", "Thick" }), ("Hide", ["Blur", "Black box"], []), ("Crop", ["Reset"], []) })
        {
            await EditorOverlayTests.ClickReaching(shown, editor, tool);
            TrayDesktop.Run(() =>
            {
                foreach (var words in plain)
                {
                    Assert.IsNull(Tip(editor.Button(words)), $"{tool}: {words}, its words its action");
                }

                foreach (var words in glyphs)
                {
                    Assert.AreEqual(words, Tip(editor.Button(words)), $"{tool}: {words}, a dot named in its tip");
                }
            });
        }

        // The person asked for it: "1 : 1" is a symbol whose action its two digits do not say.
        TrayDesktop.Run(() => Assert.AreEqual("Square crop", Tip(editor.Button("1:1")), "Crop: 1:1, its action named in its tip"));
    }

    private static string? Tip(Control control) => ToolTip.GetTip(control) as string;

    private static Control Named(Control root, string words) =>
        root.GetVisualDescendants().OfType<Control>().First(control => control.IsEffectivelyVisible && AutomationProperties.GetName(control) == words);
}
