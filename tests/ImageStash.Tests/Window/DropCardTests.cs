using ImageStash.Seams;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// G11's drop card before release: supported and unsupported files counted by their names alone, a folder by its own
/// files; the lit chip's tag the new items will get; a link is not taken (N2); nothing is opened to count.
/// </summary>
[TestClass]
public sealed class DropCardTests
{
    [TestMethod]
    public void TheCardCountsSupportedAndSkippedFilesByNameWithoutReadingAny()
    {
        var unopened = new DroppedItem("from a browser.webp", null, () => throw new AssertFailedException("the card opened a stream"), IsFolder: false);
        var contents = new DropContents(
        [
            Named("photo.jpg"), Named("transparent.png"), Named("animated.gif"), unopened, Named("clip.mp4"), Named("cat pictures.url"),
        ], false, false, []);

        var card = DropCard.Of(contents);

        Assert.AreEqual("Add to your stash", card.Title);
        Assert.AreEqual(4, card.Supported);
        Assert.AreEqual(2, card.Skipped);
        Assert.AreEqual("4 supported files · 2 will be skipped as unsupported", card.Lines[0]);
    }

    [TestMethod]
    public void AFolderCountsItsOwnFilesAndNotItsSubfolders()
    {
        using var scratch = new ScratchFolder();
        Directory.CreateDirectory(scratch.File("inner"));
        File.WriteAllBytes(scratch.File("still.png"), MediaFixtures.Bytes("still.png"));
        File.WriteAllBytes(Path.Combine(scratch.File("inner"), "huge.png"), []);
        File.WriteAllText(scratch.File("notes.txt"), "notes");

        var card = DropCard.Of(new DropContents([new DroppedItem("folder", scratch.Path, null, IsFolder: true)], false, false, []));

        Assert.AreEqual((1, 1), (card.Supported, card.Skipped));
        Assert.AreEqual("1 supported file · 1 will be skipped as unsupported", card.Lines[0]);
    }

    [TestMethod]
    public void WhileATagChipIsLitTheCardSaysTheNewItemsGetItsTag()
    {
        var contents = new DropContents([Named("photo.jpg")], false, false, []);

        var lit = DropCard.Of(contents, ["dog"]);
        var both = DropCard.Of(contents, ["dog", "cat"]);
        var unlit = DropCard.Of(contents, []);

        CollectionAssert.AreEqual(new[] { "1 supported file", "New items get the tag #dog.", "Copies are made; the originals stay where they are." }, lit.Lines.ToList());
        Assert.AreEqual("New items get the tags #dog #cat.", both.Lines[1], "every lit chip's (G11)");
        Assert.IsFalse(unlit.Lines.Any(line => line.Contains("tag")), "no tag lit, no such line");
        Assert.AreEqual("Links are not taken", DropCard.Of(new DropContents([], onlyLink: true, filesUnavailable: false, []), ["dog"]).Title, "nothing is said of a tag for what cannot be added");
    }

    [TestMethod]
    public void ALinkIsNotTakenAndADragWithoutFilesSaysSo()
    {
        Assert.AreEqual("Links are not taken", DropCard.Of(new DropContents([], onlyLink: true, filesUnavailable: false, [])).Title);
        Assert.AreEqual("Nothing here can be added", DropCard.Of(new DropContents([], onlyLink: false, filesUnavailable: false, [])).Title);
        Assert.AreEqual("The files being dragged cannot be read.", DropCard.Of(new DropContents([], onlyLink: false, filesUnavailable: true, [])).Lines.Single());
    }

    /// <summary>A file by name, at a path that does not exist, so a card that read it would fail.</summary>
    private static DroppedItem Named(string name) => new(name, Path.Combine(Path.GetTempPath(), "ImageStash.Tests", "absent", name), null, IsFolder: false);
}
