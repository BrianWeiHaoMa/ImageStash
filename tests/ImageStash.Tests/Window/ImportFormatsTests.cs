using System.Security.Cryptography;
using ImageStash.Files;
using ImageStash.Media;
using ImageStash.Seams;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// Which files an import takes as pictures (F3), over a real stash with real pictures: a JPEG named <c>.jfif</c>,
/// <c>.jpe</c> or <c>.pjpeg</c> is added and kept under <c>.jpg</c>; a file whose name has no extension is looked at,
/// before anything of it is copied in, and added under its format's extension when its content begins as one of the
/// four formats, or else skipped and named as a file without an extension (G12), leaving nothing behind (R2); a file cut
/// short is unreadable (G35); every other name is judged by its name alone, its content never read (N4). Each comes in
/// as it came, named after its file (F12), from a path, a folder, a browser's stream or the clipboard (G11, G13).
/// </summary>
[TestClass]
public sealed class ImportFormatsTests
{
    private const string Formats = "ImageStash keeps GIF, PNG, JPG and WebP";

    [TestMethod]
    public async Task AJpegNamedJfifJpeOrPjpegIsAddedAsItCameAndKeptUnderJpg()
    {
        using var importing = await ListedStash.Open();
        var sources = new List<string>
        {
            importing.Stash.Source("photo.jfif", MediaFixtures.Bytes("photo.jpg")),
            importing.Stash.Source("old camera.JPE", MediaFixtures.Bytes("progressive.jpg")),
            importing.Stash.Source("upload.pjpeg", MediaFixtures.Bytes("oriented-6.jpg")),
            importing.Stash.Source("kept.jpeg", MediaFixtures.Bytes("oriented-1.jpg")),
        };

        var results = await importing.Import.Drop(ListedStash.Contents(sources));

        Assert.IsTrue(results.All(file => file.Result == ImportResultKind.Added), string.Join(", ", results.Select(file => $"{file.Name}: {file.Result} {file.Failure?.Sentence}")));
        var added = results.Select(file => file.Item!).ToList();
        CollectionAssert.AreEqual(new[] { "photo", "old camera", "upload", "kept" }, added.Select(item => item.Name).ToList(), "named after the file (F12)");
        CollectionAssert.AreEqual(new[] { "photo.jpg", "old camera.jpg", "upload.jpg", "kept.jpeg" }, added.Select(item => item.FileName).ToList(),
            "kept under .jpg, the name a handoff carries, and a .jpeg as it was (F3)");
        for (var i = 0; i < sources.Count; i++)
        {
            CollectionAssert.AreEqual(File.ReadAllBytes(sources[i]), File.ReadAllBytes(added[i].FilePath), $"{results[i].Name} is stored as it came (F3)");
        }

        Assert.IsEmpty(importing.Stash.WorkFiles());

        var again = await importing.Import.Drop(ListedStash.Contents([MediaFixtures.Path("photo.jpg")]));

        Assert.AreEqual((ImportResultKind.AlreadyInStash, added[0].Id), (again.Single().Result, again.Single().Item?.Id), "the same picture under its other name is already in the stash (G12)");
    }

    [TestMethod]
    public async Task AFileWithoutAnExtensionIsAddedUnderTheExtensionOfTheFormatItsContentBeginsAs()
    {
        using var importing = await ListedStash.Open();
        var sources = new List<string>
        {
            importing.Stash.Source("LOLCAT", MediaFixtures.Bytes("still.png")),
            importing.Stash.Source("selfie", MediaFixtures.Bytes("photo.jpg")),
            importing.Stash.Source("dance", MediaFixtures.Bytes("animated.gif")),
            importing.Stash.Source("sticker", MediaFixtures.Bytes("animated.webp")),
        };
        var before = sources.Select(Untouched).ToList();

        var results = await importing.Import.Drop(ListedStash.Contents(sources));

        Assert.IsTrue(results.All(file => file.Result == ImportResultKind.Added), string.Join(", ", results.Select(file => $"{file.Name}: {file.Result} {file.Failure?.Sentence}")));
        var added = results.Select(file => file.Item!).ToList();
        CollectionAssert.AreEqual(new[] { "LOLCAT", "selfie", "dance", "sticker" }, added.Select(item => item.Name).ToList(), "named by the whole file name (F12)");
        CollectionAssert.AreEqual(new[] { "LOLCAT.png", "selfie.jpg", "dance.gif", "sticker.webp" }, added.Select(item => item.FileName).ToList(), "kept under its format's extension, .jpg for a JPEG (F3)");
        for (var i = 0; i < sources.Count; i++)
        {
            CollectionAssert.AreEqual(File.ReadAllBytes(sources[i]), File.ReadAllBytes(added[i].FilePath), $"{results[i].Name} is stored as it came (F3)");
        }

        CollectionAssert.AreEqual(before, sources.Select(Untouched).ToList(), "the sources stay untouched (R3, G11)");
        Assert.IsEmpty(importing.Stash.WorkFiles());
        Assert.AreEqual(("Added 4 to your stash", ""), (importing.Notices.Shown.Single().Heading, importing.Notices.Shown.Single().Text));

        var again = await importing.Import.Drop(ListedStash.Contents([MediaFixtures.Path("still.png")]));

        Assert.AreEqual((ImportResultKind.AlreadyInStash, added[0].Id), (again.Single().Result, again.Single().Item?.Id), "the same picture with its extension is already in the stash (G12)");
    }

    [TestMethod]
    public async Task AFileWithoutAnExtensionThatHoldsNoPictureIsSkippedAndNamedAsSuchLeavingNothingBehind()
    {
        using var importing = await ListedStash.Open();
        var sources = new List<string>
        {
            importing.Stash.Source("LICENSE", "Permission is hereby granted, free of charge, to any person"u8.ToArray()),
            importing.Stash.Source("tiny", [0x89, 0x50, 0x4E]),
            importing.Stash.Source("empty", []),
        };

        var results = await importing.Import.Drop(ListedStash.Contents(sources));

        CollectionAssert.AreEqual(Enumerable.Repeat((ImportResultKind.Skipped, "a file without an extension"), 3).ToList(), results.Select(file => (file.Result, file.Type)).ToList(),
            "skipped and named with its type, not sent to be decoded (G12)");
        var notice = importing.Notices.Shown.Single();
        Assert.AreEqual(("Nothing was added · 3 skipped", $"Skipped LICENSE (a file without an extension), tiny (a file without an extension) and empty (a file without an extension): {Formats}."),
            (notice.Heading, notice.Text));
        Assert.IsEmpty(await importing.Stash.All());
        Assert.IsEmpty(importing.Stash.WorkFiles(), "nothing of it was copied in (R2)");
    }

    [TestMethod]
    public async Task AFileWithoutAnExtensionThatBeginsAsAPictureAndIsCutShortIsUnreadable()
    {
        using var importing = await ListedStash.Open();
        var cut = MediaFixtures.Bytes("still.png")[..^12];
        var opened = 0;
        var streamed = new DroppedItem("cut from a browser", null, () =>
        {
            opened++;
            return Task.FromResult<Stream>(new MemoryStream(cut));
        }, IsFolder: false);

        var results = await importing.Import.Drop(new DropContents([ListedStash.File(importing.Stash.Source("cut", cut)), streamed], false, false, []));

        CollectionAssert.AreEqual(new[] { ImportResultKind.Unreadable, ImportResultKind.Unreadable }, results.Select(file => file.Result).ToList(), "unreadable, as a named one is (G35)");
        Assert.AreEqual(("Nothing was added · 2 unreadable", "cut and cut from a browser are unreadable."), (importing.Notices.Shown.Single().Heading, importing.Notices.Shown.Single().Text));
        Assert.AreEqual(1, opened);
        Assert.IsEmpty(importing.Stash.WorkFiles());
    }

    [TestMethod]
    public async Task ABrowsersFileWithoutAnExtensionIsOpenedOnceAndAddedOrSkippedByWhatItBeginsAs()
    {
        using var importing = await ListedStash.Open();
        var picture = new CountedStream(MediaFixtures.Bytes("transparent.webp"));
        var page = new CountedStream("<!doctype html><title>cat</title>"u8.ToArray());

        var results = await importing.Import.Drop(new DropContents([picture.Item("image"), page.Item("download")], false, false, []));

        Assert.AreEqual((ImportResultKind.Added, "image.webp"), (results[0].Result, results[0].Item?.FileName), results[0].Failure?.Sentence);
        CollectionAssert.AreEqual(MediaFixtures.Bytes("transparent.webp"), File.ReadAllBytes(results[0].Item!.FilePath), "every byte, those read to look at it first (F3)");
        Assert.AreEqual((ImportResultKind.Skipped, "a file without an extension"), (results[1].Result, results[1].Type));
        Assert.AreEqual((1, 1), (picture.Opened, page.Opened), "its contents open once, never a second time to copy them in");
        Assert.IsTrue(picture.Disposed && page.Disposed, "neither left open");
        Assert.IsEmpty(importing.Stash.WorkFiles(), "the page left nothing behind (R2)");
    }

    [TestMethod]
    public async Task ABrowsersFileWithoutAnExtensionWhoseStreamFailsIsNamedAndLeavesNothing()
    {
        using var importing = await ListedStash.Open();
        var breaking = new DroppedItem("image", null, () => Task.FromResult<Stream>(new FailingStream(MediaFixtures.Bytes("animated.gif"))), IsFolder: false);
        var refusing = new DroppedItem("other", null, () => throw new IOException(FailingStream.Reason), IsFolder: false);

        var results = await importing.Import.Drop(new DropContents([breaking, refusing], false, false, []));

        Assert.AreEqual(new Failure("Copy into the stash", "image", $"Windows could not read or write it ({FailingStream.Reason})"), results[0].Failure, "broken after its first bytes were looked at");
        Assert.AreEqual(new Failure("Copy into the stash", "other", $"Windows could not read or write it ({FailingStream.Reason})"), results[1].Failure, "refused as it opened");
        Assert.IsTrue(results.All(file => file.Result == ImportResultKind.Failed));
        Assert.IsEmpty(await importing.Stash.All());
        Assert.IsEmpty(importing.Stash.WorkFiles());
    }

    [TestMethod]
    public async Task ABrowsersFileWithoutAnExtensionWhoseStreamFailsAsItClosesIsNamedAndTheRestAreAdded()
    {
        using var importing = await ListedStash.Open();
        var picture = new DroppedItem("image", null, () => Task.FromResult<Stream>(new FailingClose(MediaFixtures.Bytes("transparent.webp"))), IsFolder: false);
        var page = new DroppedItem("download", null, () => Task.FromResult<Stream>(new FailingClose("<!doctype html>"u8.ToArray())), IsFolder: false);

        var results = await importing.Import.Drop(new DropContents([picture, page, ListedStash.File(MediaFixtures.Path("still.png"))], false, false, []));

        CollectionAssert.AreEqual(new[] { ImportResultKind.Failed, ImportResultKind.Failed, ImportResultKind.Added }, results.Select(file => file.Result).ToList(),
            "each named, the file after them added (G35, G12)");
        Assert.AreEqual(new Failure("Copy into the stash", "image", $"Windows could not read or write it ({FailingClose.Reason})"), results[0].Failure);
        Assert.AreEqual(new Failure("Copy into the stash", "download", $"Windows could not read or write it ({FailingClose.Reason})"), results[1].Failure);
        Assert.AreEqual("still", (await importing.Stash.All()).Single().Name);
        Assert.IsEmpty(importing.Stash.WorkFiles(), "the failed copy discarded (R2)");
    }

    [TestMethod]
    public async Task OtherNamesAreJudgedByTheirNamesAloneWhateverTheyHold()
    {
        using var importing = await ListedStash.Open();
        var png = MediaFixtures.Bytes("still.png");
        var paths = new[] { "clip.mp4", "song.mp3", "notes.txt" }.Select(name => importing.Stash.Source(name, png)).ToList();
        var streamed = new CountedStream(png);
        // Held so that no other program may open them: a look inside would be refused, and would fail them as held.
        var holds = paths.Select(path => new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None)).ToList();
        IReadOnlyList<ImportedFile> results;
        try
        {
            results = await importing.Import.Drop(new DropContents([.. paths.Select(ListedStash.File), streamed.Item("cat.url")], false, false, []));
        }
        finally
        {
            holds.ForEach(hold => hold.Dispose());
        }

        CollectionAssert.AreEqual(
            new[] { (ImportResultKind.Skipped, "a video"), (ImportResultKind.Skipped, "a sound file"), (ImportResultKind.Skipped, "a .txt file"), (ImportResultKind.Skipped, "a link") },
            results.Select(file => (file.Result, file.Type)).ToList(), "refused by name, never looked inside (N4, N2)");
        Assert.AreEqual(0, streamed.Opened);
        Assert.IsEmpty(importing.Stash.WorkFiles());
    }

    [TestMethod]
    public async Task AFileWithoutAnExtensionIsLookedAtWhereItLiesReadOnlyAndAHeldOneIsRetriedThenNamedAsHeld()
    {
        using var importing = await ListedStash.Open();
        var shared = importing.Stash.Source("shared", MediaFixtures.Bytes("still.png"));
        var briefly = importing.Stash.Source("briefly", MediaFixtures.Bytes("still.gif"));
        var held = importing.Stash.Source("held", MediaFixtures.Bytes("still.webp"));
        // Another program reading it lets others read and nothing more: a look that opened it to write would be refused.
        using var reader = new FileStream(shared, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var holder = new FileStream(briefly, FileMode.Open, FileAccess.Read, FileShare.None);
        // First in the drop, so its look comes well inside the hold, as the existing check of a briefly held source does.
        _ = Task.Delay(100).ContinueWith(_ => holder.Dispose(), TaskScheduler.Default);

        IReadOnlyList<ImportedFile> results;
        using (new FileStream(held, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            results = await importing.Import.Drop(ListedStash.Contents([briefly, shared, held]));
        }

        Assert.AreEqual((ImportResultKind.Added, "briefly.gif"), (results[0].Result, results[0].Item?.FileName), "retried while briefly held (G35)");
        Assert.AreEqual((ImportResultKind.Added, "shared.png"), (results[1].Result, results[1].Item?.FileName), results[1].Failure?.Sentence);
        Assert.AreEqual((ImportResultKind.Failed, new Failure("Copy into the stash", "held", HeldFileRetry.HeldReason)), (results[2].Result, results[2].Failure),
            "held past the retry: named as held, neither skipped nor unreadable (G35)");
        Assert.IsEmpty(importing.Stash.WorkFiles());
    }

    [TestMethod]
    public async Task AFoldersOwnFilesWithAJpegsOtherNameOrNoExtensionAreAdded()
    {
        using var importing = await ListedStash.Open();
        var folder = importing.Stash.Outside("dropped");
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, "photo.jfif"), MediaFixtures.Bytes("photo.jpg"));
        File.WriteAllBytes(Path.Combine(folder, "nameless"), MediaFixtures.Bytes("still.gif"));
        File.WriteAllText(Path.Combine(folder, "LICENSE"), "MIT");

        var results = await importing.Import.Drop(new DropContents([new DroppedItem("dropped", folder, null, IsFolder: true)], false, false, []));

        CollectionAssert.AreEqual(
            new[] { ("LICENSE", ImportResultKind.Skipped, (string?)null), ("nameless", ImportResultKind.Added, "nameless.gif"), ("photo.jfif", ImportResultKind.Added, "photo.jpg") },
            results.Select(file => (file.Name, file.Result, file.Item?.FileName)).ToList());
    }

    [TestMethod]
    public async Task FilesOnTheClipboardWithAJpegsOtherNameOrNoExtensionAreImportedAsADropsWould()
    {
        using var pasting = await ListedStash.Open(SetClock.Ahead);
        var files = new[]
        {
            pasting.Stash.Source("photo.jfif", MediaFixtures.Bytes("photo.jpg")),
            pasting.Stash.Source("LOLCAT", MediaFixtures.Bytes("transparent.png")),
            pasting.Stash.Source("LICENSE", "MIT"u8.ToArray()),
        };
        pasting.Clipboard.Reading = Outcome<ClipboardReading>.Of(new ClipboardReading(files.Select(ListedStash.File).ToList(), null, null, false));

        await pasting.Paste.Paste();

        CollectionAssert.AreEquivalent(new[] { "photo.jpg", "LOLCAT.png" }, (await pasting.Stash.All()).Select(item => item.FileName).ToList(), "as a drop's would (G13)");
        Assert.AreEqual(
            ("Added 2 · 1 skipped", $"Skipped LICENSE (a file without an extension): {Formats}. Added “photo” and “LOLCAT”."),
            (pasting.Notices.Shown.Single().Heading, pasting.Notices.Shown.Single().Text));
        Assert.IsEmpty(pasting.Stash.WorkFiles());
    }

    /// <summary>What a source holds and when it was last written, which an import leaves as it was.</summary>
    private static (string Sha256, DateTime Written) Untouched(string path) => (Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))), File.GetLastWriteTimeUtc(path));

    /// <summary>A browser's stream that hands over every byte and then fails as it is closed.</summary>
    private sealed class FailingClose(byte[] bytes) : MemoryStream(bytes, writable: false)
    {
        public const string Reason = "the browser failed to let go of it";

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            throw new IOException(Reason);
        }
    }

    /// <summary>A browser's file whose contents open once, counting its openings and whether it was let go.</summary>
    private sealed class CountedStream(byte[] bytes)
    {
        public int Opened { get; private set; }

        public bool Disposed { get; private set; }

        public DroppedItem Item(string name) => new(name, null, () =>
        {
            Opened++;
            return Task.FromResult<Stream>(new Watched(bytes, () => Disposed = true));
        }, IsFolder: false);

        private sealed class Watched(byte[] bytes, Action disposed) : MemoryStream(bytes, writable: false)
        {
            protected override void Dispose(bool disposing)
            {
                disposed();
                base.Dispose(disposing);
            }
        }
    }
}
