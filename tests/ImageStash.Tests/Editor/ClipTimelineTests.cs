using ImageStash.Editor;
using ImageStash.Media;
using ImageStash.Tests.Support;
using static ImageStash.Tests.Support.EditorSessions;

namespace ImageStash.Tests.Editor;

/// <summary>
/// The animated original's timeline the filmstrip shows (G22, R7, R8, G35): every frame read once into a small thumbnail
/// with its own duration and start, the item named while it is read; a frame that cannot be read named once, the editor
/// staying open on the frames before it, which the play, a seek and the strip keep to.
/// </summary>
[TestClass]
public sealed class ClipTimelineTests
{
    [TestMethod]
    public async Task EveryFrameIsReadOnceWithItsOwnDurationAndStart()
    {
        using var clip = await AnimationInTheEditor.Open("animated.gif");
        var session = clip.Session;

        await Until(() => session.TimelineComplete, "the frames are never all read");

        CollectionAssert.AreEqual(new[] { new FrameTime(100, 0), new FrameTime(200, 100), new FrameTime(300, 300) }, session.FrameTimes.ToArray());
        CollectionAssert.AreEqual(
            new[] { "0000FFFF", "00FF00FF", "FF0000FF" },
            session.Thumbnails.Select(thumbnail => Convert.ToHexString(thumbnail.Pixels[..4])).ToArray(),
            "each frame's own thumbnail, in order");
        Assert.IsNull(session.FramesBusy);
        Assert.IsNull(session.FramesFailure);
    }

    [TestMethod]
    public async Task EveryFrameOfALongClipHasAThumbnailNoLargerThanTheBoundAndOnlyTwoFullSizeFramesAreHeld()
    {
        using var clip = await AnimationInTheEditor.Open(SyntheticClips.Long(60, 200, 150));
        var session = clip.Session;

        await Until(() => session.TimelineComplete, "the frames are never all read");

        Assert.HasCount(60, session.Thumbnails);
        Assert.IsTrue(session.Thumbnails.All(thumbnail => (thumbnail.Width, thumbnail.Height) == (ClipTimeline.ThumbnailBound, 72)), "each scaled to the bound on its longer edge, never kept whole");
        CollectionAssert.AreEqual(Enumerable.Range(0, 60).Select(i => new FrameTime(20, 20L * i)).ToArray(), session.FrameTimes.ToArray());
        Assert.IsLessThanOrEqualTo(2, session.FullSizeFramesHeld, "the frame showing and the one on its way (R7)");
        Assert.AreEqual(60, session.FrameCount);
    }

    [TestMethod]
    public async Task AClipNoLargerThanTheBoundKeepsNoFrameAtFullSizeAsItsThumbnail()
    {
        using var clip = await AnimationInTheEditor.Open(SyntheticClips.Long(60, 64, 48));
        var session = clip.Session;

        await Until(() => session.TimelineComplete, "the frames are never all read");

        Assert.HasCount(60, session.Thumbnails);
        Assert.IsTrue(session.Thumbnails.All(thumbnail => (thumbnail.Width, thumbnail.Height) == (32, 24)), "each smaller than its frame: the strip holds no frame whole (R7)");
    }

    [TestMethod]
    public async Task WhileTheFramesAreReadTheFilmstripNamesTheItemAndOnceReadItDoesNot()
    {
        // Long enough to take a while to read: a thousand frames.
        using var clip = await AnimationInTheEditor.Open(SyntheticClips.Long(1000, 200, 150));
        var session = clip.Session;

        Assert.IsFalse(session.TimelineComplete);
        Assert.AreEqual("Reading the frames of “clip”…", session.FramesBusy);

        await Until(() => session.TimelineComplete, "the frames are never all read");
        Assert.IsNull(session.FramesBusy);
        Assert.HasCount(1000, session.Thumbnails);
    }

    [TestMethod]
    public async Task AFrameThatCannotBeReadIsNamedOnceAndTheEditorStaysOpenOnTheFramesBeforeIt()
    {
        var gif = SyntheticClips.WithFrameUnreadable(SyntheticClips.Gif(6, 16, 12), 3);
        using var scratch = new ScratchFolder();
        File.WriteAllBytes(scratch.File("clip.gif"), gif);
        using (var decoder = MediaDecoder.Open(scratch.File("clip.gif"), "clip", TestStash.ShortRetry).Value!)
        {
            for (var frame = 0; frame < 3; frame++)
            {
                Assert.IsTrue(decoder.NextFrame(16).Succeeded, $"frame {frame} reads");
            }

            Assert.IsTrue(MediaDecoder.IsUnreadable(decoder.NextFrame(16).Failure!), "frame 3 does not");
        }

        using var stash = new TestStash();
        await stash.OpenCleanly();
        // As an earlier build or a later change to the file might have left it: Media's import would refuse it.
        var staged = await stash.Stage("clip.gif", gif);
        var committed = await stash.Store!.CommitImport(TestStash.Candidate(staged, "clip", gif, new MediaFacts(ImageFormat.Gif, 16, 12, true, 6, 600)));
        Assert.IsTrue(committed.Succeeded, committed.Failure?.ToString());
        var clock = new SteppedClock();
        using var session = await Open(stash, committed.Value!.Item, time: clock);

        await Until(() => session.FramesFailure is not null, "the frame that cannot be read is never named");

        Assert.AreEqual(new Failure("Read the picture's frames", "clip", MediaDecoder.UnreadableReason), session.FramesFailure);
        Assert.IsTrue(session.IsOpen, "the edit stays open");
        Assert.AreEqual(3, session.FrameCount, "the frames before it");
        Assert.HasCount(3, session.Thumbnails);
        Assert.HasCount(3, session.FrameTimes);
        Assert.IsTrue(session.TimelineComplete);
        Assert.IsNull(session.FramesBusy);

        session.Seek(5);
        await Until(() => session.FrameIndex == 2, "a seek beyond them lands on the last one read");
        session.Play();
        // Once the player waits for the next frame's time, which it arms on another thread.
        await Until(() => clock.NextDue is not null, "the player never waits for the next frame's time");
        clock.Advance(TimeSpan.FromMilliseconds(100));
        await Until(() => session.FrameIndex == 0, "the play goes round over the frames read");
    }
}
