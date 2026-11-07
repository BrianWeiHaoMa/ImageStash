using ImageStash.Files;
using ImageStash.Media;
using ImageStash.Store;
using ImageStash.Tests.Support;
using ImageStash.Window;
using SkiaSharp;

namespace ImageStash.Tests.Window;

/// <summary>
/// The grid's thumbnails through Media's real decoder: only the tiles in view are decoded, each scaled to a bound and
/// never full size (R7), with straight alpha kept (G6); the cache stays within its bound in memory (R8); a picture
/// that cannot be read is marked, never thrown, and a failure other than its being unreadable is named and tried
/// again (G35); a tile shows its own item's picture, never one that renames put at the path its read waited on
/// (Product: the finished picture "is what the grid shows"), and from the next thumbnail size on, the size stepped back
/// to included, whatever a rename round trip under its read left (G6).
/// </summary>
[TestClass]
public sealed class ThumbnailsTests
{
    private static readonly long FixtureBytes = 8 * 6 * 4;

    private readonly List<MediaFrame> released = [];

    private readonly List<Failure> named = [];

    private Thumbnails<MediaFrame> NewThumbnails(long byteBound = 1 << 30, int decoders = 4) =>
        new(byteBound, decoders, TestStash.ShortRetry, frame => frame, action => action(), released.Add, named.Add);

    private static ThumbnailRequest Request(StashItem item) => Request(item, ThumbnailSize.Medium);

    private static ThumbnailRequest Request(StashItem item, ThumbnailSize size) =>
        new(ThumbnailKey.Of(item, size), item.FilePath, item.Name, Thumbnails<MediaFrame>.Bound(item.Facts, JustifiedRows.RowHeight(size), 1));

    /// <summary>The red of a solid picture's first pixel (BGRA), or -1 for no picture.</summary>
    private static int Red(MediaFrame? frame) => frame is null ? -1 : frame.Pixels[2];

    /// <summary>The green of a solid picture's first pixel (BGRA), or -1 for no picture.</summary>
    private static int Green(MediaFrame? frame) => frame is null ? -1 : frame.Pixels[1];

    /// <summary>A solid picture's first pixel and size, or none.</summary>
    private static string Shown(MediaFrame? frame) =>
        frame is null ? "none" : $"red {Red(frame):X2} green {Green(frame):X2} ({frame.Width}x{frame.Height})";

    /// <summary>The bytes of the frames held under <paramref name="keys"/>.</summary>
    private static long Held(Thumbnails<MediaFrame> thumbnails, params ThumbnailKey[] keys) =>
        keys.Select(thumbnails.Get).Sum(frame => frame is null ? 0L : (long)frame.Width * frame.Height * 4);

    [TestMethod]
    public async Task OnlyTheTilesInViewAreDecoded()
    {
        var items = TestItems.Many(5000);
        var rows = JustifiedRows.Layout(items, 440, 96);
        var (first, last) = JustifiedRows.InView(rows, 0, 600, 600);
        var inView = rows.Skip(first).Take(last - first + 1).SelectMany(r => r.Tiles).Select(t => items[t.Index]).ToList();
        var thumbnails = NewThumbnails();

        thumbnails.Want(inView.Select(Request).ToList());
        await thumbnails.Idle();

        Assert.IsLessThan(100, inView.Count);
        Assert.IsTrue(inView.All(item => thumbnails.Get(ThumbnailKey.Of(item, ThumbnailSize.Medium)) is not null));
        var outOfView = items.Except(inView).ToList();
        Assert.IsTrue(outOfView.All(item => thumbnails.Get(ThumbnailKey.Of(item, ThumbnailSize.Medium)) is null));
        Assert.AreEqual(inView.Count * FixtureBytes, thumbnails.Bytes);
    }

    [TestMethod]
    public async Task ATransparentPictureKeepsItsStraightAlpha()
    {
        var item = TestItems.Sized(1, 8, 6, fixture: "transparent.png");
        var thumbnails = NewThumbnails();

        thumbnails.Want([Request(item)]);
        await thumbnails.Idle();

        var frame = thumbnails.Get(ThumbnailKey.Of(item, ThumbnailSize.Medium))!;
        var at = (1 * frame.Width + 1) * 4;
        CollectionAssert.AreEqual(new byte[] { 0, 0, 255, 128 }, frame.Pixels[at..(at + 4)], "red at half alpha, not premultiplied");
        Assert.AreEqual(255, frame.Pixels[3]);
    }

    [TestMethod]
    public async Task ALargePictureArrivesScaledToItsBoundNotAtFullSize()
    {
        using var scratch = new ScratchFolder();
        var path = scratch.File("large.png");
        using (var bitmap = new SKBitmap(new SKImageInfo(400, 300, SKColorType.Bgra8888, SKAlphaType.Unpremul)))
        {
            bitmap.Erase(SKColors.Teal);
            File.WriteAllBytes(path, bitmap.Encode(SKEncodedImageFormat.Png, 100).ToArray());
        }

        var item = TestItems.Sized(1, 400, 300) with { FilePath = path };
        var bound = Thumbnails<MediaFrame>.Bound(item.Facts, 96, 1.5);
        var thumbnails = NewThumbnails();

        thumbnails.Want([new ThumbnailRequest(ThumbnailKey.Of(item, ThumbnailSize.Medium), item.FilePath, item.Name, bound)]);
        await thumbnails.Idle();

        var frame = thumbnails.Get(ThumbnailKey.Of(item, ThumbnailSize.Medium))!;
        Assert.AreEqual(192, bound, "1.33 at 96 units high on a display at 150 %: 144 pixels high, 192 wide");
        Assert.AreEqual((192, 144), (frame.Width, frame.Height));
    }

    [TestMethod]
    public void TheBoundCoversTheTileOnceCroppedToItsRatio()
    {
        Assert.AreEqual(600, Thumbnails<MediaFrame>.Bound(TestItems.Sized(1, 400, 100).Facts, 100, 1.5));
        Assert.AreEqual(96, Thumbnails<MediaFrame>.Bound(TestItems.Sized(2, 1000, 1000).Facts, 96, 1));
        Assert.AreEqual(496, Thumbnails<MediaFrame>.Bound(TestItems.Sized(3, 100, 400).Facts, 100, 2));
    }

    [TestMethod]
    public async Task TheCacheStaysWithinItsBoundAsTheViewMovesOverEveryTile()
    {
        var items = TestItems.Many(60);
        var thumbnails = NewThumbnails(byteBound: 10 * FixtureBytes);

        foreach (var view in items.Chunk(8))
        {
            thumbnails.Want(view.Select(Request).ToList());
            await thumbnails.Idle();
            Assert.IsLessThanOrEqualTo(10 * FixtureBytes, thumbnails.Bytes);
            Assert.IsTrue(view.All(item => thumbnails.Get(ThumbnailKey.Of(item, ThumbnailSize.Medium)) is not null), "what is in view is kept");
        }

        Assert.IsNull(thumbnails.Get(ThumbnailKey.Of(items[0], ThumbnailSize.Medium)), "the least recently shown left first");
        Assert.HasCount(60 - 10, released);
    }

    [TestMethod]
    public async Task AChangedPictureOrAnotherSizeIsAnotherThumbnail()
    {
        var item = TestItems.Sized(1, 8, 6);
        var thumbnails = NewThumbnails();
        thumbnails.Want([Request(item)]);
        await thumbnails.Idle();

        Assert.IsNull(thumbnails.Get(ThumbnailKey.Of(item with { RenderedRevision = 1 }, ThumbnailSize.Medium)));
        Assert.IsNull(thumbnails.Get(ThumbnailKey.Of(item, ThumbnailSize.Large)));
    }

    [TestMethod]
    public async Task AnUnreadablePictureIsMarkedForGoodWithoutANoticeAndNothingThrows()
    {
        using var scratch = new ScratchFolder();
        var garbage = scratch.File("garbage.png");
        File.WriteAllBytes(garbage, TestStash.Bytes(7));
        var unreadable = TestItems.Sized(1, 8, 6) with { FilePath = garbage };
        var thumbnails = NewThumbnails();

        thumbnails.Want([Request(unreadable)]);
        await thumbnails.Idle();
        File.Copy(MediaFixtures.Path("still.png"), garbage, overwrite: true);
        thumbnails.RetryFailed();
        thumbnails.Want([Request(unreadable)]);
        await thumbnails.Idle();

        Assert.IsTrue(thumbnails.CouldNotRead(ThumbnailKey.Of(unreadable, ThumbnailSize.Medium)));
        Assert.IsNull(thumbnails.Get(ThumbnailKey.Of(unreadable, ThumbnailSize.Medium)));
        Assert.IsEmpty(named, "a change behind ImageStash's back is marked, not announced");
        Assert.AreEqual(0, thumbnails.Bytes);
    }

    [TestMethod]
    public async Task APictureStillHeldAfterTheRetryIsNamedAndShownOnceItIsTriedAgain()
    {
        using var scratch = new ScratchFolder();
        var path = scratch.File("held.png");
        File.Copy(MediaFixtures.Path("still.png"), path);
        var item = TestItems.Sized(1, 8, 6) with { FilePath = path, Name = "held" };
        var key = ThumbnailKey.Of(item, ThumbnailSize.Medium);
        var thumbnails = NewThumbnails();

        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            thumbnails.Want([Request(item)]);
            await thumbnails.Idle();
        }

        Assert.IsTrue(thumbnails.CouldNotRead(key));
        var failure = named.Single();
        Assert.AreEqual("held", failure.Subject);
        Assert.IsFalse(MediaDecoder.IsUnreadable(failure), "held is not unreadable");

        thumbnails.Want([Request(item)]);
        await thumbnails.Idle();
        Assert.IsNull(thumbnails.Get(key), "not tried again until asked to");

        thumbnails.RetryFailed();
        thumbnails.Want([Request(item)]);
        await thumbnails.Idle();

        Assert.IsNotNull(thumbnails.Get(key));
        Assert.IsFalse(thumbnails.CouldNotRead(key));
        Assert.HasCount(1, named);
    }

    [TestMethod]
    [DataRow(true, DisplayName = "the listing read again before the hold ends")]
    [DataRow(false, DisplayName = "the listing read again after the hold ends")]
    public async Task ATileKeepsItsOwnPictureWhenRenamesPutAnotherAtThePathItsReadWaitedOn(bool listedFirst)
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        // Three rounds: a retry that falls between the two renames finds no file there, and is refused for that instead.
        for (var round = 0; round < 3; round++)
        {
            var cat = await stash.ImportPicture($"cat {round}", $"cat {round}.png", MediaFixtures.DistinctPng(0xA0 + round, 8, 6));
            var other = await stash.ImportPicture($"other {round}", $"other {round}.png", MediaFixtures.DistinctPng(0x5000 + (round << 8), 12, 10));
            var thumbnails = new Thumbnails<MediaFrame>(1 << 30, 4, HeldFileRetry.Duration, frame => frame, action => action(), released.Add, named.Add);
            named.Clear();
            StashItem[] renamed;

            // Held as a scanner holds it (E3), reading barred and renaming allowed: the tile's read waits in its retry while
            // two renames, as the cards' fields ask them, put the other picture at the path the read was given.
            using (new FileStream(cat.FilePath, FileMode.Open, FileAccess.Read, FileShare.Delete))
            {
                thumbnails.Want([Request(cat)]);
                var renames = await Task.WhenAll(store.Rename(cat, $"moved {round}"), store.Rename(other, $"cat {round}"));
                Assert.IsTrue(renames.All(rename => rename.Succeeded), string.Join("; ", renames.Select(rename => rename.Failure)));
                renamed = [.. renames.Select(rename => rename.Value!)];
                Assert.AreEqual(cat.FilePath, renamed[1].FilePath, "the other picture is at the path the read was given");
                if (listedFirst)
                {
                    thumbnails.Want(renamed.Select(Request).ToList());
                }
            }

            await thumbnails.Idle();
            if (!listedFirst)
            {
                thumbnails.Want(renamed.Select(Request).ToList());
                await thumbnails.Idle();
            }

            var (catKey, otherKey) = (ThumbnailKey.Of(cat, ThumbnailSize.Medium), ThumbnailKey.Of(other, ThumbnailSize.Medium));
            Assert.AreEqual(catKey, ThumbnailKey.Of(renamed[0], ThumbnailSize.Medium), "a rename keeps the tile's key");
            var shown = thumbnails.Get(catKey);
            Assert.AreNotEqual(0x50 + round, Green(shown), "cat's tile never shows the other's picture (Product)");
            Assert.IsTrue(Red(shown) == 0xA0 + round || (shown is null && named.Any(failure => failure.Subject == $"cat {round}")), "cat's own picture, or none with the failure named (G35)");
            Assert.AreEqual(0x50 + round, Green(thumbnails.Get(otherKey)), "the other's tile shows the other's");
            Assert.AreEqual(Held(thumbnails, catKey, otherKey), thumbnails.Bytes, "only the frames held are counted");
        }
    }

    [TestMethod]
    public async Task ATileDropsAReadMadeWhileTheGridGaveItAnotherPathEvenOnceThePathIsBack()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var cat = await stash.ImportPicture("cat", "cat.png", MediaFixtures.DistinctPng(0xA0, 8, 6));
        var other = await stash.ImportPicture("other", "other.png", MediaFixtures.DistinctPng(0x5000, 12, 10));
        using var reads = new SemaphoreSlim(0);
        using var arrive = new ManualResetEventSlim();
        // Each read, once made, waits to arrive until the check lets it, so the renames back land while it is in flight.
        var thumbnails = new Thumbnails<MediaFrame>(1 << 30, 4, HeldFileRetry.Duration, frame =>
        {
            reads.Release();
            arrive.Wait(TimeSpan.FromSeconds(10));
            return frame;
        }, action => action(), released.Add, named.Add);

        var apart = await Task.WhenAll(store.Rename(cat, "moved"), store.Rename(other, "cat"));
        // Asked from the listing as it was before the renames: cat's read finds the other's picture at cat.png.
        thumbnails.Want([Request(cat)]);
        Assert.IsTrue(await reads.WaitAsync(TimeSpan.FromSeconds(10)), "cat's read made");
        thumbnails.Want(apart.Select(rename => Request(rename.Value!)).ToList());
        var back = await Task.WhenAll(store.Rename(apart[1].Value!, "other"), store.Rename(apart[0].Value!, "cat"));
        Assert.AreEqual(cat.FilePath, back[1].Value!.FilePath, "cat's path is the one its read was given again");
        thumbnails.Want(back.Select(rename => Request(rename.Value!)).ToList());
        arrive.Set();
        await thumbnails.Idle();

        var (catKey, otherKey) = (ThumbnailKey.Of(cat, ThumbnailSize.Medium), ThumbnailKey.Of(other, ThumbnailSize.Medium));
        Assert.AreEqual(0xA0, Red(thumbnails.Get(catKey)), "cat's tile shows cat's picture, never the other's read meanwhile (Product)");
        Assert.AreEqual(0x50, Green(thumbnails.Get(otherKey)), "the other's tile shows the other's");
        Assert.IsEmpty(named, string.Join("; ", named));
        Assert.AreEqual(Held(thumbnails, catKey, otherKey), thumbnails.Bytes, "only the frames held are counted");
    }

    [TestMethod]
    [DataRow(true, DisplayName = "the tile in view at the next size")]
    [DataRow(false, DisplayName = "the tile out of view at the next size")]
    public async Task ATileShowsItsOwnPictureFromTheNextSizeOnAfterARenameRoundTripUnderItsRead(bool inViewAtTheNextSize)
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var seen = new List<string>();
        for (var round = 0; round < 5; round++)
        {
            var cat = await stash.ImportPicture($"cat {round}", $"cat {round}.png", MediaFixtures.DistinctPng(0xA0 + round, 8, 6));
            var other = await stash.ImportPicture($"other {round}", $"other {round}.png", MediaFixtures.DistinctPng(0x5000 + (round << 8), 12, 10));
            // The grid's own bound (96 MB): two small pictures at two sizes never reach it, so nothing is let go for room.
            var thumbnails = new Thumbnails<MediaFrame>(96L * 1024 * 1024, 4, HeldFileRetry.Duration, frame => frame, action => action(), released.Add, named.Add);

            // Held as a scanner holds it (E3): the read, asked from the listing before the renames, waits in its retry
            // while they put the other picture at the path it was given.
            using (new FileStream(cat.FilePath, FileMode.Open, FileAccess.Read, FileShare.Delete))
            {
                thumbnails.Want([Request(cat)]);
                var renames = await Task.WhenAll(store.Rename(cat, $"moved {round}"), store.Rename(other, $"cat {round}"));
                Assert.IsTrue(renames.All(rename => rename.Succeeded), string.Join("; ", renames.Select(rename => rename.Failure)));
                (cat, other) = (renames[0].Value!, renames[1].Value!);
                await thumbnails.Idle();
            }

            // Renamed back before the stash is next listed, so the path the grid gives is cat's own file again.
            other = (await store.Rename(other, $"other back {round}")).Value!;
            var back = (await store.Rename(cat, $"cat {round}")).Value!;
            thumbnails.Want([Request(back)]);
            await thumbnails.Idle();
            var listed = Shown(thumbnails.Get(ThumbnailKey.Of(back, ThumbnailSize.Medium)));

            // Ctrl+wheel: the next thumbnail size, then back to the size before.
            thumbnails.Want([Request(inViewAtTheNextSize ? back : other, ThumbnailSize.Large)]);
            await thumbnails.Idle();
            var stepped = inViewAtTheNextSize ? Shown(thumbnails.Get(ThumbnailKey.Of(back, ThumbnailSize.Large))) : "out of view";
            thumbnails.Want([Request(back)]);
            await thumbnails.Idle();
            var steppedBack = Shown(thumbnails.Get(ThumbnailKey.Of(back, ThumbnailSize.Medium)));
            seen.Add($"round {round}: once listed {listed}; at the next size {stepped}; stepped back {steppedBack}");
        }

        Assert.IsFalse(
            seen.Any(line => line.Split("at the next size")[1].Contains("green 5")),
            "cat's tile held the other's picture after the size stepped and stepped back (G6):\n" + string.Join("\n", seen));
    }

    [TestMethod]
    public async Task AReadUnderWayAsTheSizeStepsAndStepsBackIsReadAgainSoTheTileShowsItsOwnPicture()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var cat = await stash.ImportPicture("cat", "cat.png", MediaFixtures.DistinctPng(0xA0, 8, 6));
        var other = await stash.ImportPicture("other", "other.png", MediaFixtures.DistinctPng(0x5000, 12, 10));
        using var firstRead = new SemaphoreSlim(0);
        using var arrive = new ManualResetEventSlim();
        var reads = 0;
        // The first read, once made, waits to arrive until the check lets it, so the size steps and steps back meanwhile.
        var thumbnails = new Thumbnails<MediaFrame>(1 << 30, 4, HeldFileRetry.Duration, frame =>
        {
            if (Interlocked.Increment(ref reads) == 1)
            {
                firstRead.Release();
                arrive.Wait(TimeSpan.FromSeconds(10));
            }

            return frame;
        }, action => action(), released.Add, named.Add);

        var apart = await Task.WhenAll(store.Rename(cat, "moved"), store.Rename(other, "cat"));
        // Asked from the listing as it was before the renames: cat's read finds the other's picture at cat.png.
        thumbnails.Want([Request(cat)]);
        Assert.IsTrue(await firstRead.WaitAsync(TimeSpan.FromSeconds(10)), "cat's read made");
        var back = await Task.WhenAll(store.Rename(apart[1].Value!, "other"), store.Rename(apart[0].Value!, "cat"));
        Assert.AreEqual(cat.FilePath, back[1].Value!.FilePath, "cat's path is the one its read was given again");
        thumbnails.Want([Request(cat, ThumbnailSize.Large)]);
        thumbnails.Want([Request(cat)]);
        arrive.Set();
        await thumbnails.Idle();

        var (medium, large) = (ThumbnailKey.Of(cat, ThumbnailSize.Medium), ThumbnailKey.Of(cat, ThumbnailSize.Large));
        Assert.AreEqual(0xA0, Red(thumbnails.Get(medium)), "cat's tile shows cat's picture, never the other's read before the size stepped (G6)");
        Assert.IsEmpty(named, string.Join("; ", named));
        Assert.AreEqual(Held(thumbnails, medium, large), thumbnails.Bytes, "only the frames held are counted");
    }

    [TestMethod]
    public async Task AMissingFileIsNamedAndMarked()
    {
        using var scratch = new ScratchFolder();
        var missing = TestItems.Sized(2, 8, 6) with { FilePath = scratch.File("gone.png"), Name = "gone" };
        var thumbnails = NewThumbnails();

        thumbnails.Want([Request(missing)]);
        await thumbnails.Idle();

        Assert.IsTrue(thumbnails.CouldNotRead(ThumbnailKey.Of(missing, ThumbnailSize.Medium)));
        Assert.AreEqual("gone", named.Single().Subject);
    }

    [TestMethod]
    public async Task IdleCompletesOnlyOnceAFailureHasBeenNamedAndItsTileTold()
    {
        using var scratch = new ScratchFolder();
        var missing = TestItems.Sized(2, 8, 6) with { FilePath = scratch.File("gone.png"), Name = "gone" };
        var idleWhenNamed = new List<bool>();
        var idleWhenArrived = new List<bool>();
        Thumbnails<MediaFrame>? thumbnails = null;
        thumbnails = new(1 << 30, 4, TestStash.ShortRetry, frame => frame, action => action(), released.Add,
            _ => idleWhenNamed.Add(thumbnails!.Idle().IsCompleted));
        thumbnails.Arrived += () => idleWhenArrived.Add(thumbnails.Idle().IsCompleted);

        thumbnails.Want([Request(missing)]);
        await thumbnails.Idle();

        CollectionAssert.AreEqual(new[] { false }, idleWhenNamed, "a check that reads what was named once Idle() completes finds it");
        CollectionAssert.AreEqual(new[] { false }, idleWhenArrived);
    }

    [TestMethod]
    [DataRow(false, DisplayName = "the replaced picture shown")]
    [DataRow(true, DisplayName = "the replaced picture marked unreadable")]
    public async Task OnceLetGoForAnImportATileShowsTheImportedPictureThatTakesTheReplacedOnesKeyAndPath(bool unreadable)
    {
        using var scratch = new ScratchFolder();
        var path = scratch.File("image.png");
        File.WriteAllBytes(path, unreadable ? TestStash.Bytes(7) : MediaFixtures.DistinctPng(0xA0, 8, 6));
        // The replaced stash's item 12 and the imported stash's item 12: one name, one file name, one revision.
        var item = TestItems.Sized(12, 8, 6) with { FilePath = path, Name = "image" };
        var key = ThumbnailKey.Of(item, ThumbnailSize.Medium);
        var thumbnails = NewThumbnails();
        thumbnails.Want([Request(item)]);
        await thumbnails.Idle();
        Assert.AreEqual(unreadable, thumbnails.CouldNotRead(key));

        thumbnails.LetGo();
        await thumbnails.Idle();
        Assert.IsNull(thumbnails.Get(key), "nothing of the replaced stash kept");
        Assert.IsFalse(thumbnails.CouldNotRead(key), "nor marked");
        File.WriteAllBytes(path, MediaFixtures.DistinctPng(0x30, 8, 6));
        thumbnails.Want([Request(item)]);
        await thumbnails.Idle();

        Assert.AreEqual(0x30, Red(thumbnails.Get(key)), "the tile shows the imported item's picture (G6, G36)");
        Assert.IsFalse(thumbnails.CouldNotRead(key));
    }

    [TestMethod]
    public async Task AReadUnderWayWhenTheThumbnailsAreLetGoForAnImportDeliversNothingAndIdleWaitsForIt()
    {
        using var scratch = new ScratchFolder();
        var path = scratch.File("image.png");
        File.WriteAllBytes(path, MediaFixtures.DistinctPng(0xA0, 8, 6));
        var item = TestItems.Sized(12, 8, 6) with { FilePath = path, Name = "image" };
        using var reads = new SemaphoreSlim(0);
        using var arrive = new ManualResetEventSlim();
        var arrived = 0;
        var thumbnails = new Thumbnails<MediaFrame>(1 << 30, 4, TestStash.ShortRetry, frame =>
        {
            reads.Release();
            arrive.Wait(TimeSpan.FromSeconds(10));
            return frame;
        }, action => action(), released.Add, named.Add);
        thumbnails.Arrived += () => arrived++;
        thumbnails.Want([Request(item)]);
        Assert.IsTrue(await reads.WaitAsync(TimeSpan.FromSeconds(10)), "the read made");

        thumbnails.LetGo();
        var idle = thumbnails.Idle();
        Assert.IsFalse(idle.IsCompleted, "the read under way is waited for");
        arrive.Set();
        await idle;

        Assert.IsNull(thumbnails.Get(ThumbnailKey.Of(item, ThumbnailSize.Medium)), "it delivers nothing afterwards (G6, G36)");
        Assert.IsFalse(thumbnails.CouldNotRead(ThumbnailKey.Of(item, ThumbnailSize.Medium)));
        Assert.AreEqual(0, arrived, "and tells no tile");
        Assert.HasCount(1, released, "its picture let go");
        Assert.AreEqual(0, thumbnails.Bytes);
    }

    [TestMethod]
    public async Task ATileThatLeftViewBeforeItsTurnIsNotDecoded()
    {
        var before = TestItems.Many(10);
        var now = TestItems.Sized(99, 8, 6);
        var thumbnails = NewThumbnails(decoders: 1);

        thumbnails.Want(before.Select(Request).ToList());
        thumbnails.Want([Request(now)]);
        await thumbnails.Idle();

        Assert.IsNotNull(thumbnails.Get(ThumbnailKey.Of(now, ThumbnailSize.Medium)));
        Assert.IsLessThanOrEqualTo(1, before.Count(item => thumbnails.Get(ThumbnailKey.Of(item, ThumbnailSize.Medium)) is not null), "only the decode already running finishes");
    }
}
