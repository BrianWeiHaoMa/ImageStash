using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using ImageStash.Seams;
using ImageStash.Window;
using ImageStash.Windows.Tests.Support;
using static ImageStash.Windows.Tests.Support.ShownWindow;

namespace ImageStash.Windows.Tests.Window;

/// <summary>
/// One path through the real window with real keys (G12, G16, G7, G18, G17): a drop whose batch ends after the flyout
/// hid leaves its notice for the summon, counting what was added and naming the file that was not; Del then <c>Ctrl+Z</c>
/// brings the item back; <c>Ctrl+T</c>, a tag typed and a real click on the grid tag the item. It shows the stash window,
/// clicks its grid with the real pointer and deletes a picture of its own into the Recycle Bin, whose entry the Undo
/// removes.
/// </summary>
[TestClass]
public sealed class StashWindowPathTests
{
    [TestInitialize]
    public void UseProductTheme() => TrayDesktop.UseProductTheme();

    [TestMethod]
    public async Task ADropEndingHiddenThenDelAndCtrlZThenCtrlTTypeAndClickTheGrid()
    {
        using var shown = ShownWindow.Open();
        await shown.OpenStash(shown.Picture("path kept.png"));
        TrayDesktop.Run(() => shown.Notices.Close(shown.Notices.Shown.Single()));
        var pictures = new[] { "path one.png", "path two.png", "path three.png", "path four.png" }.Select(shown.Picture).ToList();
        var clip = Path.Combine(Path.GetDirectoryName(pictures[0])!, "clip.mp4");
        File.WriteAllBytes(clip, [1, 2, 3]);
        var dropped = new DropContents([.. pictures.Select(path => new DroppedItem(Path.GetFileName(path), path, null, IsFolder: false)), new DroppedItem("clip.mp4", clip, null, IsFolder: false)], false, false, []);
        var clock = (StoppedClock)shown.Notices.Clock;

        // The drop starts, and the flyout hides in the same turn, before its batch ends.
        var importing = TrayDesktop.Get(() =>
        {
            var started = shown.Import.Drop(dropped);
            shown.Window.Hide();
            return started;
        });
        var four = (await importing).Where(file => file.Item is not null).Select(file => file.Item!).ToList();
        clock.Advance(TimeSpan.FromHours(1));
        TrayDesktop.Run(shown.Notices.Sweep);
        TrayDesktop.Run(() => shown.Window.SummonFromShortcut(default, new SummonAnchor(null)));

        await Eventually(() => TrayDesktop.Get(() => shown.Window.IsVisible && CardSays(shown, "Added 4 · 1 skipped")));
        TrayDesktop.Run(() => Assert.IsTrue(shown.Window.GetVisualDescendants().OfType<TextBlock>().Any(block => block.IsEffectivelyVisible && block.Text is { } text && text.StartsWith("Skipped clip.mp4 (a video)")), "the file not added, named with its reason"));

        var chosen = four[1];
        TrayDesktop.Run(() =>
        {
            shown.Listing.Focus(chosen.Id);
            shown.Grid.Focus();
            Tap(shown.Grid, Key.Delete);
        });
        await Eventually(() => TrayDesktop.Get(() => shown.Listing.Items.All(item => item.Id != chosen.Id)));
        TrayDesktop.Run(() => Tap(shown.Grid, Key.Z, KeyModifiers.Control));
        await Eventually(() => TrayDesktop.Get(() => shown.Listing.Items.Any(item => item.Id == chosen.Id)));
        Assert.IsTrue(File.Exists(chosen.FilePath), "back with its file");

        TrayDesktop.Run(() =>
        {
            shown.Listing.Focus(chosen.Id);
            shown.Grid.Focus();
            Tap(shown.Grid, Key.T, KeyModifiers.Control);
        });
        await Eventually(() => TrayDesktop.Get(() => shown.TagField.IsVisible && shown.TagBox.IsFocused));
        TrayDesktop.Run(() =>
        {
            Type(shown.TagBox, "cat");
            // The import's notice sits over the grid's foot, where the click goes.
            foreach (var notice in shown.Notices.Shown.ToList())
            {
                shown.Notices.Dismiss(notice);
            }
        });
        // The real pointer lands where the last frame drew the grid.
        await Task.Delay(200);
        using var pointer = TestMouse.PutBackAfterwards();

        TestMouse.Click(TrayDesktop.Get(() => shown.Grid.PointToScreen(new Point(shown.Grid.Bounds.Width / 2, shown.Grid.Bounds.Height - 12))));

        await Eventually(() => shown.TagsOf(chosen.Id).Contains("cat"));
        TrayDesktop.Run(() => Assert.IsFalse(shown.TagField.IsVisible));
    }

    private static bool CardSays(ShownWindow shown, string text) =>
        shown.Window.GetVisualDescendants().OfType<TextBlock>().Any(block => block.Text == text && block.IsEffectivelyVisible);
}
