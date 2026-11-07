using ImageStash.Media;
using ImageStash.Store;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// The one home of a clipboard picture's steps to an item (G13, F3, R4): the PNG bytes kept as they are, a frame
/// encoded by Media, bytes labelled PNG that are another format made a still of their first frame; saved without G12's
/// question and with the tag it is given in its own insert (G11, G28); the commit's outcomes answered as an import's,
/// refused before it leaving nothing and failed after it leaving the item as committed (R9); unreadable bytes named; the
/// paste's batch and Stash clipboard image its only callers; and a capture's written PNG or GIF saved from its facts
/// on, with no tag, by Take a screenshot and Record a clip alone.
/// </summary>
[TestClass]
public sealed class PictureImportTests
{
    [TestMethod]
    public async Task ThePngBytesAreKeptAsTheyAreSavedTwiceAndTaggedInTheirInsert()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var named = new List<Failure>();
        var pictures = new PictureImport(TestStash.ShortRetry, named.Add);
        var png = MediaFixtures.Bytes("transparent.png");

        var first = await pictures.Save(store, new PastedPicture("cat", png, null), ["reaction"]);
        var second = await pictures.Save(store, new PastedPicture("cat", png, null), []);

        Assert.AreEqual((ImportResultKind.Added, ImportResultKind.Added), (first.Result, second.Result), "never already in the stash (G12)");
        Assert.AreEqual(("cat.png", "cat 2.png"), (first.Item!.FileName, second.Item!.FileName));
        CollectionAssert.AreEqual(png, File.ReadAllBytes(first.Item.FilePath), "the bytes as they are (F3)");
        var all = await stash.All();
        CollectionAssert.AreEqual(new[] { "reaction" }, all.Single(item => item.Id == first.Item.Id).Tags.ToList(), "the tag in its own insert (G11)");
        Assert.IsEmpty(all.Single(item => item.Id == second.Item.Id).Tags);
        Assert.IsEmpty(stash.WorkFiles());
        Assert.IsEmpty(named);
    }

    [TestMethod]
    public async Task AFrameIsEncodedAsAPngAndGifBytesLabelledPngBecomeTheirFirstFrame()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var pictures = new PictureImport(TestStash.ShortRetry, _ => Assert.Fail("nothing to name"));
        byte[] pixels = [0, 0, 255, 255, 255, 0, 0, 128];

        var bitmap = await pictures.Save(store, new PastedPicture("bitmap", null, new MediaFrame(2, 1, 0, pixels)), []);
        var gif = await pictures.Save(store, new PastedPicture("gif", MediaFixtures.Bytes("animated.gif"), null), []);

        Assert.AreEqual(new MediaFacts(ImageFormat.Png, 2, 1, false, 1, 0), bitmap.Item!.Facts);
        CollectionAssert.AreEqual(pixels, MediaFixtures.FirstFrame(bitmap.Item.FilePath).Pixels, "Media's one PNG encoder, alpha kept (R4)");
        Assert.AreEqual("gif.png", gif.Item!.FileName, "a still PNG whatever the label (G13)");
        Assert.IsFalse(gif.Item.Facts.Animated);
        CollectionAssert.AreEqual(MediaFixtures.FirstFrame(MediaFixtures.Path("animated.gif")).Pixels, MediaFixtures.FirstFrame(gif.Item.FilePath).Pixels);
        Assert.IsEmpty(stash.WorkFiles(), "the first staged copy discarded");
    }

    [TestMethod]
    public async Task BytesThatAreNoPictureAreUnreadableAndLeaveNothing()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var pictures = new PictureImport(TestStash.ShortRetry, _ => Assert.Fail("nothing to name"));

        var saved = await pictures.Save(store, new PastedPicture("noise", [0x89, 0x50, 0x4E, 0x47, 1, 2, 3, 4], null), []);

        Assert.AreEqual(new ImportedFile("noise", ImportResultKind.Unreadable), saved);
        Assert.IsEmpty(stash.WorkFiles());
        Assert.IsEmpty(await stash.All());
    }

    [TestMethod]
    public async Task AnInsertRefusedIsAFailureWithNoItemAndLeavesNoCopy()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        stash.SqlWhileOpen("CREATE TRIGGER refuse_insert BEFORE INSERT ON items BEGIN SELECT RAISE(ABORT, 'the write was refused'); END");
        var pictures = new PictureImport(TestStash.ShortRetry, _ => Assert.Fail("nothing to name"));

        var saved = await pictures.Save(store, new PastedPicture("cat", MediaFixtures.Bytes("transparent.png"), null), []);

        Assert.AreEqual(ImportResultKind.Failed, saved.Result);
        Assert.IsNull(saved.Item);
        Assert.IsNotNull(saved.Failure);
        Assert.IsEmpty(stash.WorkFiles(), "refused before the commit: nothing left (State: the import bullet)");
    }

    [TestMethod]
    public async Task AClearingRefusedIsAFailureCarryingTheListedItem()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        stash.SqlWhileOpen(
            "CREATE TRIGGER refuse_clearing BEFORE UPDATE OF pending_steps ON items WHEN NEW.pending_steps IS NULL BEGIN SELECT RAISE(ABORT, 'the write was refused'); END");
        var pictures = new PictureImport(TestStash.ShortRetry, _ => Assert.Fail("nothing to name"));

        var saved = await pictures.Save(store, new PastedPicture("cat", MediaFixtures.Bytes("transparent.png"), null), []);

        Assert.AreEqual(ImportResultKind.Failed, saved.Result, "not added before its last step (R9)");
        Assert.AreEqual((await stash.All()).Single().Id, saved.Item!.Id, "the item as committed, in place and listed");
        Assert.IsTrue(File.Exists(saved.Item.FilePath), "nothing discarded after the commit");
    }

    [TestMethod]
    public async Task AStreamFailingPartWayWhosePartialCopyCannotBeDeletedNamesThatToo()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var named = new List<Failure>();
        var pictures = new PictureImport(TestStash.ShortRetry, named.Add);
        // Read-only, set while the copy is being written, so the delete that would clear it away is refused.
        var stream = new BreakingStream(() => File.SetAttributes(Directory.GetFiles(stash.Folder.Work).Single(), FileAttributes.ReadOnly));

        var staged = await pictures.StageStream(store, "broken.png", ".png", () => Task.FromResult<Stream>(stream));

        try
        {
            Assert.AreEqual(new Failure("Copy into the stash", "broken.png", BreakingStream.Reason), staged.Failure, "the copy's own failure, as before");
            Assert.AreEqual("Discard a copy", named.Single().Action, "the copy left behind is named too (G35)");
            Assert.AreEqual(stash.WorkFiles().Single(), named.Single().Subject);
        }
        finally
        {
            foreach (var left in Directory.GetFiles(stash.Folder.Work))
            {
                File.SetAttributes(left, FileAttributes.Normal);
            }
        }
    }

    [TestMethod]
    public void ThePastesBatchAndStashClipboardImageAreItsOnlyCallers()
    {
        var callers = ProductCalls.Methods()
            .Where(method => ProductCalls.CalledMethods(method).Any(called => called.DeclaringType == typeof(PictureImport) && called.Name == nameof(PictureImport.Save)))
            .Select(method => ProductCalls.Outermost(method.DeclaringType!).FullName)
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToList();

        CollectionAssert.AreEqual(new[] { "ImageStash.Window.Import", "ImageStash.Window.StashClipboardImage" }, callers);
    }

    [TestMethod]
    public async Task ACapturesWrittenPngIsSavedAsItIsEvenWhenItsBytesAreInTheStashWithNoTag()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var pictures = new PictureImport(TestStash.ShortRetry, _ => Assert.Fail("nothing to name"));
        var png = MediaFixtures.Bytes("transparent.png");
        var earlier = await stash.Import("earlier", png);

        var saved = await pictures.SaveStaged(store, "Screenshot 2026-10-16 17.30.00", await stash.Stage("written.png", png));

        Assert.AreEqual(ImportResultKind.Added, saved.Result, "never already in the stash (G12)");
        Assert.AreNotEqual(earlier.Id, saved.Item!.Id);
        Assert.AreEqual(("Screenshot 2026-10-16 17.30.00", "Screenshot 2026-10-16 17.30.00.png"), (saved.Item.Name, saved.Item.FileName));
        Assert.AreEqual(TestStash.StillPng, saved.Item.Facts);
        CollectionAssert.AreEqual(png, File.ReadAllBytes(saved.Item.FilePath), "the file as written");
        Assert.IsEmpty((await stash.All()).Single(item => item.Id == saved.Item.Id).Tags, "no tag (G11)");
        Assert.IsEmpty(stash.WorkFiles());
    }

    [TestMethod]
    public async Task ACapturesFileThatIsNoPictureIsUnreadableAndDiscarded()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var pictures = new PictureImport(TestStash.ShortRetry, _ => Assert.Fail("nothing to name"));

        var saved = await pictures.SaveStaged(store, "Screenshot", await stash.Stage("written.png", [0x89, 0x50, 0x4E, 0x47, 1, 2, 3, 4]));

        Assert.AreEqual(new ImportedFile("Screenshot", ImportResultKind.Unreadable), saved);
        Assert.IsEmpty(stash.WorkFiles());
        Assert.IsEmpty(await stash.All());
    }

    [TestMethod]
    public void TheCapturesThatWriteMediasOwnFileAreTheOnlyCallersOfTheWrittenPicturesSave()
    {
        var callers = ProductCalls.Methods()
            .Where(method => ProductCalls.CalledMethods(method).Any(called => called.DeclaringType == typeof(PictureImport) && called.Name == nameof(PictureImport.SaveStaged)))
            .Select(method => ProductCalls.Outermost(method.DeclaringType!).FullName)
            .Distinct()
            .ToList();

        // Nothing is re-encoded there, so only a file Media wrote itself, a screenshot's PNG or a recording's GIF, may come in that way.
        CollectionAssert.AreEquivalent(new[] { "ImageStash.Window.Screenshot", "ImageStash.Window.RecordClip" }, callers);
    }

    /// <summary>A stream that hands over its first bytes, then runs <paramref name="beforeBreaking"/> and breaks.</summary>
    private sealed class BreakingStream(Action beforeBreaking) : MemoryStream(MediaFixtures.Bytes("transparent.png"))
    {
        public const string Reason = "the source stopped sending it";

        public override int Read(byte[] buffer, int offset, int count) =>
            Position > 0 ? Break() : base.Read(buffer, offset, Math.Min(count, 16));

        public override int Read(Span<byte> buffer) =>
            Position > 0 ? Break() : base.Read(buffer[..Math.Min(buffer.Length, 16)]);

        private int Break()
        {
            beforeBreaking();
            throw new IOException(Reason);
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Read(buffer.Span));

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            Task.FromResult(Read(buffer, offset, count));
    }
}
