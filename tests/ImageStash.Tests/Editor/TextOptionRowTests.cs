using Avalonia.Automation;
using Avalonia.Controls.Primitives;
using ImageStash.Editor;
using ImageStash.Layers;
using ImageStash.Tests.Support;

namespace ImageStash.Tests.Editor;

/// <summary>
/// Text's option row as the editor shows it (G21, F8): with no layer selected, the font button, the five fill swatches,
/// More and the outline swatch able, showing what the next layer of the current preset takes, and a choice there what
/// the next click on the picture gives; another preset showing its own next values; and the row still saying "Click the
/// picture to add text".
/// </summary>
[TestClass]
public sealed class TextOptionRowTests
{
    private static readonly string[] Colors = [.. ColorChoices.Swatches.Select(ColorChoices.Hex), DrawOptionRow.MoreColors, OutlineSwatch.Called];

    [TestMethod]
    public async Task WithNoneSelectedTheFontAndColoursAreAbleAndShowTheNextMemesValues()
    {
        using var editing = await StillInTheEditor.Open();

        ControlThread.Run(() =>
        {
            var shown = EditorShown.Over(editing.Session);

            foreach (var name in Colors.Prepend(TextOptionRow.FontButton))
            {
                Assert.AreEqual((true, true), shown.State(name), name);
            }

            Assert.AreEqual("Impact", AutomationProperties.GetHelpText(shown.Button(TextOptionRow.FontButton)), "the font the menu marks");
            Assert.AreEqual(ColorChoices.Hex(ColorChoices.Swatches[0]), Lit(shown), "white, the Meme's own fill");
            Assert.AreEqual("Click the picture to add text", editing.Session.Text().Hint);
        });
    }

    [TestMethod]
    public async Task ASwatchClickedWithNoneSelectedIsTheNextTextsFillAndAnotherPresetShowsItsOwn()
    {
        using var editing = await StillInTheEditor.Open();
        var session = editing.Session;
        var blue = ColorChoices.Swatches[4];

        ControlThread.Run(() =>
        {
            var shown = EditorShown.Over(session);
            shown.Click(ColorChoices.Hex(blue));
            Assert.AreEqual(ColorChoices.Hex(blue), Lit(shown), "the row shows the choice");

            shown.Click("Caption bar");
            Assert.AreEqual(ColorChoices.Hex(ColorChoices.Swatches[1]), Lit(shown), "the Caption bar's own near-black, not the Meme's blue");
            Assert.AreEqual("Segoe UI", AutomationProperties.GetHelpText(shown.Button(TextOptionRow.FontButton)));

            shown.Click("Meme");
            Assert.AreEqual(ColorChoices.Hex(blue), Lit(shown));
            EditorSessions.Click(session, 10, 5);
            Assert.AreEqual(blue, ((TextLayer)session.SelectedLayer!).Fill, "the next click gives what the row showed");
        });
    }

    /// <summary>The one fill swatch lit.</summary>
    private static string? Lit(EditorShown shown) =>
        ColorChoices.Swatches.Select(ColorChoices.Hex).Single(name => shown.Control<ToggleButton>(name).IsChecked == true);
}
