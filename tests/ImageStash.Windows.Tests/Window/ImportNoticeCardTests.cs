using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using ImageStash.Seams;
using ImageStash.Store;
using ImageStash.Window;
using ImageStash.Windows.Tests.Support;
using static ImageStash.Windows.Tests.Support.ShownWindow;

namespace ImageStash.Windows.Tests.Window;

/// <summary>
/// An import's notice on the real window (G12, G16): its card shows the counts semi-bold above the files that were not
/// added, and its Undo names the key that runs it; with a tag chip lit, the tags it gave show whole inside the window at
/// its minimum size (G11, L2). These checks show the stash window.
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

    [TestMethod]
    public async Task TheCardOfADropWithAChipLitShowsTheTagsItGaveWholeInsideTheWindowAtItsMinimumSize()
    {
        using var shown = ShownWindow.Open();
        var held = (await shown.OpenStash(shown.Picture("held.png"))).Single();
        await shown.Tag(await shown.AddTo("carrying.png"), "cat");
        // The setting up's own import notices, whose time a stopped clock never ends, leave first.
        TransferredStash.CloseNotices(shown);
        TrayDesktop.Run(() =>
        {
            shown.Search.Text = "cat";
            shown.Window.Width = WindowSize.Minimum.Width;
            shown.Window.Height = WindowSize.Minimum.Height;
        });
        await Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled && shown.Listing.LitTags.Contains("cat")
            && Math.Abs(shown.Window.ClientSize.Width - WindowSize.Minimum.Width) <= 0.5 && Math.Abs(shown.Window.ClientSize.Height - WindowSize.Minimum.Height) <= 0.5));

        await TrayDesktop.Get(() => shown.Import.Drop(new DropContents(
            [new DroppedItem("newcomer.png", shown.Picture("newcomer.png"), null, IsFolder: false), new DroppedItem("held.png", shown.Picture("held.png"), null, IsFolder: false)], false, false, [])));
        await Eventually(() => TrayDesktop.Get(() => NoticeCards(shown).Count == 1));

        CollectionAssert.AreEqual(new[] { "cat" }, shown.TagsOf(held.Id), "the picture the stash already held got the tag");
        TrayDesktop.Run(() =>
        {
            shown.Window.UpdateLayout();
            WindowMinimum.AssertAt(shown.Window.ClientSize, "the window at its minimum");
            var card = NoticeCards(shown).Single();
            var texts = card.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible && text.Text is { Length: > 0 }
                && !text.GetVisualAncestors().OfType<Button>().Any()).ToList();
            var heading = texts.Single(text => text.Text == "Added 1, tagged #cat · 1 already in your stash");
            var details = texts.Single(text => text.Text == "held.png was already in your stash as “held”, now tagged #cat. Added “newcomer”.");
            var window = new Rect(shown.Window.ClientSize);
            foreach (var shownText in new[] { heading, details })
            {
                var bounds = new Rect(shownText.TranslatePoint(default, shown.Window)!.Value, shownText.Bounds.Size);
                Assert.IsTrue(window.Contains(bounds), $"'{shownText.Text}' lies whole inside the window: {bounds} in {window}");
                Assert.IsGreaterThanOrEqualTo(shownText.DesiredSize.Height - 0.5, shownText.Bounds.Height, $"'{shownText.Text}' wraps rather than being clipped");
            }

            Assert.AreEqual("Undo (Ctrl+Z)", ToolTip.GetTip(UndoButton(card)), "its Undo takes back the tag too");
        });
    }

    private static double Top(Visual text, Visual card) => text.TranslatePoint(default, card)!.Value.Y;

    /// <summary>The cards of the notices showing in the stash window.</summary>
    private static List<Border> NoticeCards(ShownWindow shown) =>
        shown.Window.GetVisualDescendants().OfType<Border>().Where(border => border.Child is StackPanel row
            && row.Children.OfType<Button>().Any(button => Avalonia.Automation.AutomationProperties.GetName(button) == "Close")).ToList();

    private static Button UndoButton(Control card) => card.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Undo"));
}
