using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using ImageStash.Seams;
using ImageStash.Window;
using ImageStash.Windows.Tests.Support;
using static ImageStash.Windows.Tests.Support.ShownWindow;

namespace ImageStash.Windows.Tests.Window;

/// <summary>
/// An import's notice on the real window (G12, G16): its card shows the counts semi-bold above the files that were not
/// added, and its Undo names the key that runs it. These checks show the stash window.
/// </summary>
[TestClass]
public sealed class ImportNoticeCardTests
{
    [TestInitialize]
    public void UseProductTheme() => TrayDesktop.UseProductTheme();

    [TestMethod]
    public async Task TheCardShowsTheCountsSemiBoldAboveTheFilesNotAddedAndItsUndoNamesCtrlZ()
    {
        using var shown = ShownWindow.Open();
        await shown.OpenStash();
        var picture = shown.Picture("counted.png");
        var clip = Path.Combine(Path.GetDirectoryName(picture)!, "clip.mp4");
        File.WriteAllBytes(clip, [1, 2, 3]);

        await TrayDesktop.Get(() => shown.Import.Drop(new DropContents(
            [new DroppedItem("counted.png", picture, null, IsFolder: false), new DroppedItem("clip.mp4", clip, null, IsFolder: false)], false, false, [])));
        await Eventually(() => TrayDesktop.Get(() => NoticeCards(shown).Count == 1));

        TrayDesktop.Run(() =>
        {
            shown.Window.UpdateLayout();
            var card = NoticeCards(shown).Single();
            var texts = card.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible && text.Text is { Length: > 0 }
                && !text.GetVisualAncestors().OfType<Button>().Any()).ToList();
            var heading = texts.Single(text => text.Text == "Added 1 · 1 skipped");
            var details = texts.Single(text => text.Text!.StartsWith("Skipped clip.mp4 (a video)"));
            Assert.AreEqual(FontWeight.SemiBold, heading.FontWeight, "the counts in semi-bold");
            Assert.AreNotEqual(FontWeight.SemiBold, details.FontWeight);
            Assert.IsLessThan(Top(details, card), Top(heading, card), "the counts above the files not added");
            Assert.AreEqual("Undo (Ctrl+Z)", ToolTip.GetTip(UndoButton(card)));
        });
    }

    private static double Top(Visual text, Visual card) => text.TranslatePoint(default, card)!.Value.Y;

    /// <summary>The cards of the notices showing in the stash window.</summary>
    private static List<Border> NoticeCards(ShownWindow shown) =>
        shown.Window.GetVisualDescendants().OfType<Border>().Where(border => border.Child is StackPanel row
            && row.Children.OfType<Button>().Any(button => Avalonia.Automation.AutomationProperties.GetName(button) == "Close")).ToList();

    private static Button UndoButton(Control card) => card.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Undo"));
}
