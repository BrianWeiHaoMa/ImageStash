using ImageStash.Seams;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// G11's drop card before release: supported and unsupported files counted by their names alone, a folder by its own
/// files, a name with no extension counted apart as a file to check (F3); the lit chip's tag every picture dropped will
/// get, an item already in the stash included; a link is not taken (N2); nothing is opened to count.
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
    public void AFileWhoseNameHasNoExtensionIsCountedApartAsOneToCheckWithoutReadingIt()
    {
        var unopened = new DroppedItem("image", null, () => throw new AssertFailedException("the card opened a stream"), IsFolder: false);
        var contents = new DropContents(
        [
            Named("photo.jfif"), Named("old camera.JPE"), Named("still.png"), unopened, Named("LICENSE"), Named("clip.mp4"),
        ], false, false, []);

        var card = DropCard.Of(contents);

        Assert.AreEqual((3, 2, 1), (card.Supported, card.ToCheck, card.Skipped), "a JPEG's other names supported; the names with no extension to check");
        Assert.AreEqual("3 supported files · 2 to check · 1 will be skipped as unsupported", card.Lines[0]);
        Assert.AreEqual("0 supported files · 1 to check", DropCard.Of(new DropContents([unopened], false, false, [])).Lines[0], "how many are supported, said when none is");
        Assert.AreEqual("0 supported files · 2 to check · 1 will be skipped as unsupported", DropCard.Of(new DropContents([unopened, Named("LICENSE"), Named("notes.txt")], false, false, [])).Lines[0]);
    }

    [TestMethod]
    public void AFolderCountsItsOwnFilesAndNotItsSubfolders()
    {
        using var scratch = new ScratchFolder();
        Directory.CreateDirectory(scratch.File("inner"));
        File.WriteAllBytes(scratch.File("still.png"), MediaFixtures.Bytes("still.png"));
        File.WriteAllBytes(Path.Combine(scratch.File("inner"), "huge.png"), []);
        File.WriteAllText(scratch.File("notes.txt"), "notes");
        File.WriteAllBytes(scratch.File("nameless"), MediaFixtures.Bytes("still.gif"));

        // Held so no other program may read it: a card that opened it to count it would throw.
        using var held = new FileStream(scratch.File("nameless"), FileMode.Open, FileAccess.Read, FileShare.None);
        var card = DropCard.Of(new DropContents([new DroppedItem("folder", scratch.Path, null, IsFolder: true)], false, false, []));

        Assert.AreEqual((1, 1, 1), (card.Supported, card.ToCheck, card.Skipped));
        Assert.AreEqual("1 supported file · 1 to check · 1 will be skipped as unsupported", card.Lines[0], "the folder's own file with no extension is one to check");
    }

    [TestMethod]
    public void WhileATagChipIsLitTheCardSaysEveryPictureGetsItsTagThoseAlreadyInTheStashToo()
    {
        var contents = new DropContents([Named("photo.jpg")], false, false, []);

        var lit = DropCard.Of(contents, ["dog"]);
        var both = DropCard.Of(contents, ["dog", "cat"]);
        var unlit = DropCard.Of(contents, []);

        CollectionAssert.AreEqual(new[] { "1 supported file", "Each picture gets the tag #dog, those already in your stash too.", "Copies are made; the originals stay where they are." }, lit.Lines.ToList());
        Assert.AreEqual("Each picture gets the tags #dog #cat, those already in your stash too.", both.Lines[1], "every lit chip's, a duplicate's item getting them too (G11)");
        Assert.IsFalse(unlit.Lines.Any(line => line.Contains("tag")), "no tag lit, no such line");
        Assert.IsFalse(DropCard.Of(new DropContents([Named("clip.mp4")], false, false, []), ["dog"]).Lines.Any(line => line.Contains("tag")), "no picture that may come in, no tag to give");
        Assert.AreEqual("Each picture gets the tag #dog, those already in your stash too.", DropCard.Of(new DropContents([Named("image"), Named("clip.mp4")], false, false, []), ["dog"]).Lines[1],
            "a file to check may come in, and is tagged when it does");
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
