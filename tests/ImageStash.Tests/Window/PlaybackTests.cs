using ImageStash.Media;
using ImageStash.Store;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// The grid's one playing animation through Media's real decoder (G6, R7): frames one at a time at the thumbnail's
/// bound, paced by their durations and looping, the file held open while it plays and closed before the next play
/// opens one; a still never plays; a picture that cannot be read stops its play, is marked, and is named only when the
/// reason is other than its being unreadable (G35); an item an Undo or a Delete recycles while it plays goes to the
/// Recycle Bin, and nothing is named (G29), and a deleted item brought back by Undo plays again from its file (G6); a
/// paused play keeps its frame, hands over nothing more, holds back a frame already on its way, and resumes from the
/// frame after the one it paused on (G19).
/// </summary>
[TestClass]
public sealed class PlaybackTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private static readonly byte[] Red = [0, 0, 255, 255];
    private static readonly byte[] Green = [0, 255, 0, 255];
    private static readonly byte[] Blue = [255, 0, 0, 255];

    private readonly FastForwardClock clock = new();
    private readonly List<MediaFrame> handed = [];
    private readonly List<Failure> named = [];
    private TaskCompletionSource<bool>? enough;
    private int wanted;

    [TestMethod]
    public async Task AnAnimationPlaysFrameByFrameAtItsBoundPacedByItsDurationsAndLoops()
    {
        var thumbnails = NewThumbnails();
        var playback = NewPlayback(thumbnails);

        await Until(7, () => playback.Play(Request("animated.gif", MediaFixtures.Path("animated.gif"), bound: 4)));
        playback.Play(null);
        await playback.Idle();

        var frames = Handed().Take(7).ToList();
        CollectionAssert.AreEqual(new[] { Red, Green, Blue, Red, Green, Blue, Red }.Select(Hex).ToList(), frames.Select(frame => Hex(frame.Pixels[..4])).ToList(), "the frames in order, then again from the first");
        Assert.IsTrue(frames.All(frame => (frame.Width, frame.Height) == (4, 3)), "each frame at the bound, never full size");
        CollectionAssert.AreEqual(new[] { 100, 200, 300, 100, 200, 300 }, clock.Waits.Take(6).Select(wait => (int)wait.TotalMilliseconds).ToList(), "each frame shows for its duration");
        Assert.IsEmpty(named);
    }

    [TestMethod]
    public async Task ABusyUiThreadHoldsOneFrameOfThePlayNotAQueue()
    {
        var waiting = new System.Collections.Concurrent.ConcurrentQueue<Action>();
        var playback = new Playback<MediaFrame>(clock, TestStash.ShortRetry, Hand, waiting.Enqueue, _ => { }, NewThumbnails().ReadFailed);

        playback.Play(Request("animated.gif", MediaFixtures.Path("animated.gif")));
        for (var turn = 0; turn < 5; turn++)
        {
            var waited = System.Diagnostics.Stopwatch.StartNew();
            while (waiting.IsEmpty && waited.Elapsed < Patience)
            {
                await Task.Delay(5);
            }

            // The UI thread stays busy a while longer: the play goes on, but hands over nothing more.
            await Task.Delay(100);
            Assert.HasCount(1, waiting, "one frame waits for the UI thread");
            Assert.IsTrue(waiting.TryDequeue(out var show));
            show();
        }

        playback.Play(null);
        await playback.Idle().WaitAsync(Patience);
        Assert.HasCount(5, Handed());
    }

    [TestMethod]
    public async Task APausedPlayKeepsItsFrameHandsOverNothingMoreAndResumesFromTheNextFrameNotTheFirst()
    {
        var playback = NewPlayback(NewThumbnails());
        var request = Request("long.gif", MediaFixtures.Path("long.gif"));

        await Until(5, () => playback.Play(request));
        playback.Pause();
        var handedAtPause = Handed();
        var showing = playback.Frame(request.Key);
        await Task.Delay(300);

        Assert.IsTrue(playback.Paused);
        Assert.AreSame(handedAtPause[^1], showing, "the frame showing at the pause is the last one handed over");
        Assert.AreSame(showing, playback.Frame(request.Key), "the paused frame stays, never a blank picture");
        Assert.HasCount(handedAtPause.Count, Handed(), "nothing more is handed over while paused, though the clock runs free");

        await Until(handedAtPause.Count + 2, playback.Resume);
        playback.Play(null);
        await playback.Idle();

        // long.gif's sixty frames each carry their index in their red channel at (0,0), four apart.
        var pausedOn = showing!.Pixels[2] / 4;
        var resumedWith = Handed()[handedAtPause.Count].Pixels[2] / 4;
        Assert.AreEqual((pausedOn + 1) % 60, resumedWith, "the play goes on from the frame after the paused one, not from the first");
        Assert.IsFalse(playback.Paused, "a play asked for afresh is not paused");
        Assert.IsEmpty(named);
    }

    [TestMethod]
    public async Task AFrameReachingTheUiThreadAfterThePauseDoesNotAdvanceThePausedPictureAndShowsFirstOnResume()
    {
        var waiting = new System.Collections.Concurrent.ConcurrentQueue<Action>();
        var playback = new Playback<MediaFrame>(clock, TestStash.ShortRetry, Hand, waiting.Enqueue, _ => { }, NewThumbnails().ReadFailed);
        var request = Request("animated.gif", MediaFixtures.Path("animated.gif"));
        playback.Play(request);

        await Next(waiting);
        var first = playback.Frame(request.Key);
        var late = await Queued(waiting);
        playback.Pause();
        late();

        Assert.AreSame(first, playback.Frame(request.Key), "a frame arriving after the pause does not replace the paused one");
        Assert.HasCount(1, Handed());
        Assert.IsTrue(waiting.IsEmpty, "and the play waits, handing over nothing more");

        playback.Resume();
        Assert.HasCount(2, Handed(), "the frame held back shows at the resume");
        Assert.AreSame(Handed()[1], playback.Frame(request.Key));
        CollectionAssert.AreEqual(new[] { Red, Green }.Select(Hex).ToList(), Handed().Select(frame => Hex(frame.Pixels[..4])).ToList());

        await Next(waiting);
        Assert.AreEqual(Hex(Blue), Hex(playback.Frame(request.Key)!.Pixels[..4]), "then the play goes on in order");
        playback.Play(null);
        await playback.Idle().WaitAsync(Patience);
    }

    [TestMethod]
    public async Task ABusyUiThreadOrAPauseLeavesTheFileUnreadPastTheFrameOnItsWaySoNoMoreThanTwoFramesAreHeld()
    {
        using var scratch = new ScratchFolder();
        var path = ThirdFrameSpoiled(scratch);
        var waiting = new System.Collections.Concurrent.ConcurrentQueue<Action>();
        var thumbnails = NewThumbnails();
        var playback = new Playback<MediaFrame>(clock, TestStash.ShortRetry, Hand, waiting.Enqueue, _ => { }, thumbnails.ReadFailed);
        var request = Request("spoiled", path);
        playback.Play(request);

        await Next(waiting);
        var second = await Queued(waiting);
        // Reading the third frame would find it unreadable and say so on the UI thread: until the second is shown, nothing.
        await Task.Delay(300);
        Assert.IsTrue(waiting.IsEmpty, "with the first frame showing and the second on its way, the third is not read yet (R7)");

        playback.Pause();
        second();
        await Task.Delay(300);
        Assert.IsTrue(waiting.IsEmpty, "paused, with the first frame showing and the second held, the file is read no further (R7, G19)");
        Assert.IsFalse(thumbnails.CouldNotRead(request.Key));

        playback.Resume();
        await Next(waiting);
        Assert.IsTrue(thumbnails.CouldNotRead(request.Key), "resumed, the play reads the third frame, which is marked");
        Assert.IsEmpty(named);
        await playback.Idle().WaitAsync(Patience);
    }

    [TestMethod]
    public async Task AFrameAskingForTenMillisecondsOrLessShowsForAHundredAsInABrowser()
    {
        var playback = NewPlayback(NewThumbnails());

        // long.gif's first frames ask for 10, 20, 30, 40 and 50 ms.
        await Until(6, () => playback.Play(Request("long.gif", MediaFixtures.Path("long.gif"))));
        playback.Play(null);
        await playback.Idle();

        CollectionAssert.AreEqual(new[] { 100, 20, 30, 40, 50 }, clock.Waits.Take(5).Select(wait => (int)wait.TotalMilliseconds).ToList(), "the grid shows a frame as ShownDuration says");
    }

    [TestMethod]
    public async Task ThePlayingFileStaysOpenAndClosesBeforeTheNextPlayOpensItsOwn()
    {
        using var scratch = new ScratchFolder();
        var first = Copy(scratch, "animated.gif", "first.gif");
        var second = Copy(scratch, "long.gif", "second.gif");
        var thumbnails = NewThumbnails();
        var playback = NewPlayback(thumbnails);
        var firstRequest = Request("first", first);

        await Until(1, () => playback.Play(firstRequest));
        Assert.IsFalse(OpensAlone(first), "the play holds its file open between frames and loops");

        var secondRequest = Request("second", second);
        playback.Play(secondRequest);
        await Shown(playback, secondRequest.Key);
        Assert.IsTrue(OpensAlone(first), "the first play closed its file");
        Assert.IsFalse(OpensAlone(second));
        Assert.IsNull(playback.Frame(firstRequest.Key), "the first item shows its thumbnail again");

        playback.Play(null);
        await playback.Idle();
        Assert.IsTrue(OpensAlone(second));
    }

    [TestMethod]
    public async Task AStillDoesNotPlay()
    {
        using var scratch = new ScratchFolder();
        var still = Copy(scratch, "still.gif", "still.gif");
        var playback = NewPlayback(NewThumbnails());

        playback.Play(Request("still", still));
        await playback.Idle().WaitAsync(Patience);

        Assert.IsEmpty(Handed());
        Assert.IsTrue(OpensAlone(still));
    }

    [TestMethod]
    public async Task APictureChangedToRandomBytesBehindTheStashStopsItsPlayIsMarkedAndIsNotNamed()
    {
        using var scratch = new ScratchFolder();
        var path = Copy(scratch, "animated.gif", "changed.gif");
        var thumbnails = NewThumbnails();
        var request = Request("changed", path);
        thumbnails.Want([request]);
        await thumbnails.Idle();
        Assert.IsNotNull(thumbnails.Get(request.Key));
        File.WriteAllBytes(path, TestStash.Bytes(5, 400));
        var playback = NewPlayback(thumbnails);

        playback.Play(request);
        await playback.Idle().WaitAsync(Patience);

        Assert.IsTrue(thumbnails.CouldNotRead(request.Key), "the tile shows the mark");
        Assert.IsNull(thumbnails.Get(request.Key));
        Assert.IsEmpty(Handed());
        Assert.IsEmpty(named, "a change behind ImageStash's back is marked, not announced");
    }

    [TestMethod]
    public async Task APictureChangedWhileItPlaysThrowsNothingAndItsNextPlayIsRefusedAndMarked()
    {
        using var scratch = new ScratchFolder();
        var path = Copy(scratch, "long.gif", "changing.gif");
        var thumbnails = NewThumbnails();
        var playback = NewPlayback(thumbnails);
        var request = Request("changing", path);

        await Until(2, () => playback.Play(request));
        using (var changing = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
        {
            changing.Write(TestStash.Bytes(6, (int)changing.Length));
        }

        // The codec may still hold the bytes it read, and plays on from them, or stops at the bytes it now reads.
        await Task.WhenAny(Until(Handed().Count + 3, () => { }), playback.Idle()).WaitAsync(Patience);
        playback.Play(null);
        await playback.Idle();
        playback.Play(request);
        await playback.Idle().WaitAsync(Patience);

        Assert.IsTrue(thumbnails.CouldNotRead(request.Key));
        Assert.IsEmpty(named);
        Assert.IsTrue(OpensAlone(path), "the stopped play closed the file");
    }

    [TestMethod]
    public async Task APictureStillHeldAfterTheRetryIsNamedOnceAndMarkedUntilTriedAgain()
    {
        using var scratch = new ScratchFolder();
        var path = Copy(scratch, "animated.gif", "held.gif");
        var thumbnails = NewThumbnails();
        var playback = NewPlayback(thumbnails);
        var request = Request("held", path);

        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            playback.Play(request);
            await playback.Idle().WaitAsync(Patience);
        }

        Assert.AreEqual("held", named.Single().Subject);
        Assert.IsFalse(MediaDecoder.IsUnreadable(named.Single()));
        Assert.IsTrue(thumbnails.CouldNotRead(request.Key));
        thumbnails.RetryFailed();
        Assert.IsFalse(thumbnails.CouldNotRead(request.Key), "the next summon tries it again");
    }

    [TestMethod]
    public async Task AnItemTheBatchsUndoRecyclesWhileItPlaysGoesToTheRecycleBinAndNothingIsNamed()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var notices = new Notices(stash.Clock);
        var listing = new Listing(named.Add);
        await listing.StashOpened(stash.Store!);
        var import = new Import(listing, notices, TestStash.ShortRetry);
        var source = stash.Source("dance.gif", MediaFixtures.Bytes("animated.gif"));
        var item = (await import.Pick(() => Task.FromResult<IReadOnlyList<string>?>([source]), _ => { })).Single().Item!;
        var playback = NewPlayback(NewThumbnails());

        await Until(2, () => playback.Play(Request(item.Name, item.FilePath)));
        await notices.Undo(notices.Shown.Single());
        await Until(Handed().Count + 2, () => { });
        playback.Play(null);
        await playback.Idle();

        Assert.IsFalse(File.Exists(item.FilePath), "the recycle went through the open play");
        Assert.AreEqual(item.FilePath, stash.RecycleBin.Asked.Single());
        Assert.HasCount(1, Directory.GetFiles(stash.RecycleBin.Folder));
        Assert.IsEmpty(await stash.All());
        Assert.IsEmpty(notices.Shown, "the Undo succeeded and nothing failed");
        Assert.IsEmpty(named);
    }

    [TestMethod]
    public async Task ADeletedItemsPlayStopsWithNothingNamedAndAfterUndoItIsSelectedAndPlaysFromItsRestoredFile()
    {
        using var listed = await ListedStash.Open();
        await listed.Import.Drop(ListedStash.Contents([MediaFixtures.Path("animated.gif")]));
        listed.Notices.Close(listed.Notices.Shown.Single());
        var item = listed.Listing.SelectedItems.Single();
        var playback = NewPlayback(NewThumbnails());

        await Until(2, () => playback.Play(Request(item.Name, item.FilePath)));
        await listed.Deletion.Delete(listed.Listing.SelectedItems);
        await Until(Handed().Count + 2, () => { });
        playback.Play(null);
        await playback.Idle();

        Assert.IsFalse(File.Exists(item.FilePath), "the recycle went through the open play");
        Assert.IsEmpty(listed.Listing.Items);
        Assert.IsEmpty(named, "nothing is named for the play the Delete ended");

        await listed.Notices.Undo(listed.Notices.Shown.Single());
        CollectionAssert.AreEqual(new[] { item.Id }, listed.Listing.Selection.ToList(), "alone in the stash, it is selected again");
        var before = Handed().Count;
        await Until(before + 3, () => playback.Play(Request(item.Name, item.FilePath)));
        playback.Play(null);
        await playback.Idle();

        CollectionAssert.AreEqual(MediaFixtures.Bytes("animated.gif"), File.ReadAllBytes(item.FilePath));
        CollectionAssert.AreEqual(new[] { Red, Green, Blue }.Select(Hex).ToList(), Handed().Skip(before).Take(3).Select(frame => Hex(frame.Pixels[..4])).ToList(), "it plays from its first frame again");
        Assert.IsEmpty(named);
        Assert.IsEmpty(listed.Notices.Shown);
    }

    private Thumbnails<MediaFrame> NewThumbnails() =>
        new(1 << 30, 4, TestStash.ShortRetry, frame => frame, action => action(), _ => { }, named.Add);

    private Playback<MediaFrame> NewPlayback(Thumbnails<MediaFrame> thumbnails) =>
        new(clock, TestStash.ShortRetry, Hand, action => action(), _ => { }, thumbnails.ReadFailed);

    private static ThumbnailRequest Request(string name, string path, int bound = 8) =>
        new(new ThumbnailKey(name.GetHashCode(), 0, ThumbnailSize.Medium), path, name, bound);

    private MediaFrame Hand(MediaFrame frame)
    {
        lock (handed)
        {
            handed.Add(frame);
            if (handed.Count >= wanted)
            {
                enough?.TrySetResult(true);
            }
        }

        return frame;
    }

    private List<MediaFrame> Handed()
    {
        lock (handed)
        {
            return handed.ToList();
        }
    }

    /// <summary>Does <paramref name="act"/>, then waits until <paramref name="count"/> frames in all have been handed over.</summary>
    private async Task Until(int count, Action act)
    {
        lock (handed)
        {
            wanted = count;
            enough = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (handed.Count >= count)
            {
                enough.TrySetResult(true);
            }
        }

        act();
        await enough.Task.WaitAsync(Patience);
    }

    /// <summary>Waits until the play has posted a frame to the UI thread, and takes it without running it.</summary>
    private static async Task<Action> Queued(System.Collections.Concurrent.ConcurrentQueue<Action> waiting)
    {
        var waited = System.Diagnostics.Stopwatch.StartNew();
        Action? show;
        while (!waiting.TryDequeue(out show))
        {
            Assert.IsLessThan(Patience, waited.Elapsed, "no frame was posted");
            await Task.Delay(5);
        }

        return show;
    }

    /// <summary>Runs the next frame the play posts to the UI thread.</summary>
    private static async Task Next(System.Collections.Concurrent.ConcurrentQueue<Action> waiting) => (await Queued(waiting))();

    /// <summary>Waits until a frame of <paramref name="key"/>'s own play shows.</summary>
    private static async Task Shown(Playback<MediaFrame> playback, ThumbnailKey key)
    {
        var waited = System.Diagnostics.Stopwatch.StartNew();
        while (playback.Frame(key) is null)
        {
            Assert.IsLessThan(Patience, waited.Elapsed, "no frame of the play arrived");
            await Task.Delay(5);
        }
    }

    private static string Copy(ScratchFolder scratch, string fixture, string name)
    {
        var path = scratch.File(name);
        File.Copy(MediaFixtures.Path(fixture), path);
        return path;
    }

    /// <summary>
    /// animated.gif with its third frame's picture data overwritten, its blocks and trailer kept: the file opens and its
    /// first two frames decode; the third does not.
    /// </summary>
    private static string ThirdFrameSpoiled(ScratchFolder scratch)
    {
        var bytes = MediaFixtures.Bytes("animated.gif");
        var at = 13 + ColorTable(bytes[10]);
        for (var frame = 0; bytes[at] != 0x3B; )
        {
            if (bytes[at] == 0x21)
            {
                at = PastSubBlocks(bytes, at + 2, spoil: false);
                continue;
            }

            // An image descriptor: ten bytes with its separator, its colour table, the LZW code size, its data.
            at += 10 + ColorTable(bytes[at + 9]) + 1;
            at = PastSubBlocks(bytes, at, spoil: frame++ == 2);
        }

        var path = scratch.File("spoiled.gif");
        File.WriteAllBytes(path, bytes);
        return path;

        static int ColorTable(byte packed) => (packed & 0x80) == 0 ? 0 : 3 << ((packed & 7) + 1);
    }

    private static int PastSubBlocks(byte[] bytes, int at, bool spoil)
    {
        while (bytes[at] != 0)
        {
            if (spoil)
            {
                Array.Fill(bytes, (byte)0xFF, at + 1, bytes[at]);
            }

            at += bytes[at] + 1;
        }

        return at + 1;
    }

    /// <summary>Whether the file opens with no sharing, which it does only while nothing else holds it open.</summary>
    private static bool OpensAlone(string path)
    {
        try
        {
            using var alone = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static string Hex(byte[] pixel) => Convert.ToHexString(pixel);
}
