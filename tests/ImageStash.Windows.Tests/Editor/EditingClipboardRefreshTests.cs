using Avalonia.Input;
using ImageStash.Store;
using ImageStash.Windows.Tests.Support;
using SkiaSharp;

namespace ImageStash.Windows.Tests.Editor;

/// <summary>
/// The clipboard after a Save, end to end on the real desktop and the signed-in user's real clipboard (G26, G10, How
/// G26 holds): ImageStash's own copy of the item replaced by the finished picture as saved, its PNG and its bitmap, with
/// no use recorded; text another program put there since, or a copy of another item since, left as it is.
/// </summary>
[TestClass]
public sealed class EditingClipboardRefreshTests
{
    [TestMethod]
    public async Task SaveReplacesImageStashsOwnCopyOfTheItemWithTheFinishedPictureAndRecordsNoUse()
    {
        using var shown = ShownWindow.Open();
        using var editor = new ShownEditor(shown);
        var item = (await shown.OpenStash(Grey(shown, "grey.png"))).Single();
        await Copy(shown, item);
        var copied = TestClipboard.Read();
        Assert.AreEqual((byte)200, Centre(copied.Png!).Red, "the copy is the grey original");

        await SaveABlackBox(shown, editor, item);

        var read = TestClipboard.Read();
        var saved = TrayDesktop.Get(() => shown.Listing.Items.Single());
        CollectionAssert.AreEqual(new[] { saved.FilePath }, read.Files.ToList(), "the finished picture's file (G8)");
        Assert.AreEqual(1, saved.RenderedRevision);
        Assert.AreEqual(SKColors.Black, Centre(read.Png!), "G26: the PNG is the saved picture, black inside the hide");
        Assert.AreEqual(new SKColor(200, 200, 200), Corner(read.Png!), "and grey outside it");
        Assert.IsNotNull(read.DibV5, "with its bitmap, as a copy puts it there");
        Assert.AreEqual(1, shown.Uses(), "G10: the copy's use alone; the refresh records none");
    }

    [TestMethod]
    public async Task TextAnotherProgramPutThereSinceStandsAfterTheSave()
    {
        using var shown = ShownWindow.Open();
        using var editor = new ShownEditor(shown);
        var item = (await shown.OpenStash(Grey(shown, "grey.png"))).Single();
        await Copy(shown, item);
        TestClipboard.PutText("elsewhere");

        await SaveABlackBox(shown, editor, item);

        Assert.AreEqual("elsewhere", TestClipboard.Text(), "G26: only what nothing has changed since ImageStash put it there is replaced");
        Assert.IsEmpty(TestClipboard.Read().Files);
    }

    [TestMethod]
    public async Task ACopyOfAnotherItemSinceStandsAfterTheSave()
    {
        using var shown = ShownWindow.Open();
        using var editor = new ShownEditor(shown);
        var items = await shown.OpenStash(Grey(shown, "grey.png"), shown.Fixture("transparent.png", "other.png"));
        var (grey, other) = (items.Single(item => item.Name == "grey"), items.Single(item => item.Name == "other"));
        await Copy(shown, grey);
        await Copy(shown, other);
        var before = TestClipboard.Read();

        await SaveABlackBox(shown, editor, grey);

        var after = TestClipboard.Read();
        CollectionAssert.AreEqual(new[] { other.FilePath }, after.Files.ToList(), "the other item's copy stands");
        CollectionAssert.AreEqual(before.Png, after.Png);
    }

    private static string Grey(ShownWindow shown, string name) => shown.Picture(name, 64, 48, (_, _) => new SKColor(200, 200, 200));

    /// <summary><c>Ctrl+C</c> on the item in the grid, until its use is recorded.</summary>
    private static async Task Copy(ShownWindow shown, StashItem item)
    {
        var uses = shown.Uses();
        TrayDesktop.Run(() =>
        {
            shown.Listing.Focus(item.Id);
            shown.Grid.Focus();
            ShownWindow.Press(shown.Grid, Key.C, KeyModifiers.Control);
        });
        await ShownWindow.Eventually(() => shown.Uses() == uses + 1);
    }

    /// <summary>The editor on the item, a black box over its middle, and Save, awaited through the refresh it runs.</summary>
    private static async Task SaveABlackBox(ShownWindow shown, ShownEditor editor, StashItem item)
    {
        await EditorOverlayTests.Open(shown, item);
        TrayDesktop.Run(() =>
        {
            editor.Choose("Hide");
            editor.Choose("Black box");
            editor.Drag((0.25, 0.25), (0.75, 0.75));
        });
        var saved = await TrayDesktop.Get(() => shown.Editing.Save());
        Assert.IsTrue(saved.Succeeded, saved.Failure?.Sentence);
    }

    private static SKColor Centre(byte[] png)
    {
        using var bitmap = SKBitmap.Decode(png);
        return bitmap.GetPixel(bitmap.Width / 2, bitmap.Height / 2);
    }

    private static SKColor Corner(byte[] png)
    {
        using var bitmap = SKBitmap.Decode(png);
        return bitmap.GetPixel(1, 1);
    }
}
