using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using ImageStash.Store;
using ImageStash.Window;
using ImageStash.Windows.Tests.Support;
using static ImageStash.Windows.Tests.Support.ShownWindow;

namespace ImageStash.Windows.Tests.Window;

/// <summary>
/// The drop card on the real window (G11, L2): another program's drag of a JPEG named <c>.jfif</c>, a picture whose name
/// has no extension and a text file whose name has none, while a tag chip is lit, shows its counts with the two files to
/// check apart and its line about the tag, each line whole inside the window at its minimum size. These checks show the
/// stash window.
/// </summary>
[TestClass]
public sealed class DropCardTests
{
    [TestInitialize]
    public void UseProductTheme() => TrayDesktop.UseProductTheme();

    [TestMethod]
    public async Task ADragHoldingFilesToCheckShowsItsCountsWholeInsideTheWindowAtItsMinimumSize()
    {
        using var shown = ShownWindow.Open();
        var carrying = (await shown.OpenStash(shown.Picture("carrying.png"))).Single();
        await shown.Tag([carrying], "cat");
        TransferredStash.CloseNotices(shown);
        TrayDesktop.Run(() =>
        {
            shown.Search.Text = "cat";
            shown.Window.Width = WindowSize.Minimum.Width;
            shown.Window.Height = WindowSize.Minimum.Height;
        });
        await Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled && shown.Listing.LitTags.Contains("cat")
            && Math.Abs(shown.Window.ClientSize.Width - WindowSize.Minimum.Width) <= 0.5 && Math.Abs(shown.Window.ClientSize.Height - WindowSize.Minimum.Height) <= 0.5));
        var jfif = shown.Picture("from a browser.jfif");
        var license = Path.Combine(Path.GetDirectoryName(jfif)!, "LICENSE");
        File.WriteAllText(license, "MIT License");
        var data = await shown.Files(jfif, shown.Picture("nameless"), license);

        TrayDesktop.Run(() => RaiseDrag(shown.Window, DragDrop.DragEnterEvent, data));

        TrayDesktop.Run(() =>
        {
            shown.Window.UpdateLayout();
            WindowMinimum.AssertAt(shown.Window.ClientSize, "the window at its minimum");
            var window = new Rect(shown.Window.ClientSize);
            foreach (var line in new[] { "1 supported file · 2 to check", "Each picture gets the tag #cat, those already in your stash too.", "Copies are made; the originals stay where they are." })
            {
                var shownText = shown.Window.GetVisualDescendants().OfType<TextBlock>().Single(text => text.IsEffectivelyVisible && text.Text == line);
                var bounds = new Rect(shownText.TranslatePoint(default, shown.Window)!.Value, shownText.Bounds.Size);
                Assert.IsTrue(window.Contains(bounds), $"'{line}' lies whole inside the window: {bounds} in {window}");
                Assert.IsGreaterThanOrEqualTo(shownText.DesiredSize.Height - 0.5, shownText.Bounds.Height, $"'{line}' wraps rather than being clipped");
            }
        });
        TrayDesktop.Run(() => RaiseDrag(shown.Window, DragDrop.DragLeaveEvent, data));
    }
}
