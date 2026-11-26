using ImageStash.Editor;
using ImageStash.Layers;
using ImageStash.Media;
using ImageStash.Tests.Support;
using static ImageStash.Tests.Support.EditorSessions;

namespace ImageStash.Tests.Editor;

/// <summary>
/// The editor playing an animated original (G22, G25, G6, R7, R4): from the opening, each frame for the time a browser
/// shows it, round again; Space's pause and play, Left and Right stepping and a seek each pausing on their frame, a frame
/// on its way never showing once another is asked for, the frame and its index always together; the canvas's routine
/// and the eyedropper composing the frame showing; a Save or Save as new item while it plays; two full-size frames at
/// most whatever the clip's length; a still playing nothing.
/// </summary>
[TestClass]
public sealed class FramePlayerTests
{
    // animated.gif's frames, 100, 200 and 300 ms long, as their first pixel's BGRA.
    private static readonly byte[] Red = [0, 0, 255, 255];
    private static readonly byte[] Green = [0, 255, 0, 255];
    private static readonly byte[] Blue = [255, 0, 0, 255];

    [TestMethod]
    public async Task AnAnimationPlaysFromTheOpeningEachFrameForItsTimeAndRoundAgainComposedAsTheCanvasDrawsIt()
    {
        using var clip = await AnimationInTheEditor.Open("animated.gif");
        var session = clip.Session;
        Assert.IsTrue(session.Animated);
        Assert.IsTrue(session.Playing, "it plays from the opening");
        Assert.AreEqual(3, session.FrameCount);
        AssertShowing(session, 0, Red);

        await clip.NextFrameRead();
        clip.Clock.Advance(TimeSpan.FromMilliseconds(99));
        await Task.Delay(50);
        AssertShowing(session, 0, Red);
        await clip.Advance(1, 1);
        AssertShowing(session, 1, Green);

        await clip.NextFrameRead();
        clip.Clock.Advance(TimeSpan.FromMilliseconds(199));
        await Task.Delay(50);
        Assert.AreEqual(1, session.FrameIndex, "frame 1 lasts 200 ms");
        await clip.Advance(1, 2);
        AssertShowing(session, 2, Blue);

        await clip.NextFrameRead();
        await clip.Advance(300, 0);
        AssertShowing(session, 0, Red);
        Assert.IsTrue(session.Playing);
    }

    [TestMethod]
    public async Task AFrameAskingForTenMillisecondsShowsForAHundredAsThePreviewShowsIt()
    {
        // long.gif's frames ask for 10, 20, 30, 40 and 50 ms in turn.
        using var clip = await AnimationInTheEditor.Open("long.gif");

        await clip.NextFrameRead();
        clip.Clock.Advance(TimeSpan.FromMilliseconds(99));
        await Task.Delay(50);
        Assert.AreEqual(0, clip.Session.FrameIndex, "a frame of 10 ms shows for 100");
        await clip.Advance(1, 1);
        await clip.NextFrameRead();
        clip.Clock.Advance(TimeSpan.FromMilliseconds(19));
        await Task.Delay(50);
        Assert.AreEqual(1, clip.Session.FrameIndex, "a frame of 20 ms shows for 20");
        await clip.Advance(1, 2);
    }

    [TestMethod]
    public async Task SpacePausesOnTheFrameShowingAndPlaysOnFromItAndStepsAndSeeksPauseOnTheirFrameWithinTheClip()
    {
        using var clip = await AnimationInTheEditor.Open("animated.gif");
        var session = clip.Session;
        await clip.NextFrameRead();

        session.TogglePlaying();
        Assert.IsFalse(session.Playing);
        clip.Clock.Advance(TimeSpan.FromSeconds(5));
        await Task.Delay(50);
        AssertShowing(session, 0, Red);

        session.TogglePlaying();
        Assert.IsTrue(session.Playing);
        await clip.Advance(100, 1);

        session.Step(1);
        Assert.IsFalse(session.Playing, "a step pauses");
        await Until(() => session.FrameIndex == 2, "Right shows the next frame");
        AssertShowing(session, 2, Blue);
        clip.Clock.Advance(TimeSpan.FromSeconds(5));
        session.Step(1);
        await Task.Delay(50);
        AssertShowing(session, 2, Blue);

        session.Seek(0);
        await Until(() => session.FrameIndex == 0, "a seek shows its frame");
        session.Step(-1);
        await Task.Delay(50);
        AssertShowing(session, 0, Red);
        Assert.IsFalse(session.Playing);

        session.Seek(2);
        await Until(() => session.FrameIndex == 2, "a seek shows its frame");
        session.Step(-2);
        await Until(() => session.FrameIndex == 0, "a step back from the last frame");
        Assert.IsFalse(session.Playing);
    }

    [TestMethod]
    public async Task AFrameOnItsWayWhenAnotherIsSoughtNeverShowsAndTheSoughtOneStays()
    {
        using var clip = await AnimationInTheEditor.Open("animated.gif");
        var session = clip.Session;
        // Frame 1 is read and waits for its time, 100 ms on.
        await clip.NextFrameRead();

        session.Seek(2);
        await Until(() => session.FrameIndex == 2, "the sought frame shows");
        clip.Clock.Advance(TimeSpan.FromSeconds(2));
        await Task.Delay(100);

        AssertShowing(session, 2, Blue);
        Assert.IsFalse(session.Playing);
        Assert.AreEqual(1, session.FullSizeFramesHeld, "the late frame was let go");
    }

    [TestMethod]
    public async Task TheEyedropperSamplesTheFrameShowingWithTheLayersAtIt()
    {
        // A black box ranged to the last frame alone, over its top-left corner.
        var ranged = new HideLayer(new PixelRect(0, 0, 2, 2), HideKind.Black, new FrameRange(2, 2));
        using var clip = await AnimationInTheEditor.Open(stash => stash.ImportFixture("clip", "animated.gif"), List(ranged));
        var session = clip.Session;

        Assert.AreEqual(new Rgba(255, 0, 0, 255), session.SampleColor(new PixelPoint(0, 0)), "frame 0's own red, the box not drawn on it");
        session.Seek(2);
        await Until(() => session.FrameIndex == 2, "frame 2 shows");

        Assert.AreEqual(new Rgba(0, 0, 255, 255), session.SampleColor(new PixelPoint(4, 3)), "frame 2's blue, not frame 0's red");
        Assert.AreEqual(new Rgba(0, 0, 0, 255), session.SampleColor(new PixelPoint(0, 0)), "the box drawn in its range");
    }

    [TestMethod]
    public async Task ASaveWhilePlayingMovesTheOriginalItsPlayerHoldsOpenAndEndsThePlay()
    {
        using var clip = await AnimationInTheEditor.Open(stash => stash.ImportFixture("clip", "animated.gif"), List(Box(1, 1, 3, 3)));
        var session = clip.Session;
        await clip.NextFrameRead();
        var root = clip.Clip.FilePath;

        var saved = await session.Save();

        Assert.AreEqual(EditEnding.Saved, saved.Ending, saved.Failure?.Sentence);
        var edit = await EditedItems.ForEdit(clip.Stash.Store!, clip.Clip.Id);
        Assert.AreNotEqual(root, edit.OriginalPath, "the original moved into originals\\ while the player held it open");
        Assert.HasCount(3, MediaFixtures.Frames(saved.Item!.FilePath), "the finished GIF holds every frame");
        // Opened with no sharing once no decoder of the session's holds the original any more.
        await Until(() => !Held(edit.OriginalPath), "the session's decoders never let the original go");

        Assert.IsFalse(session.Playing);
    }

    [TestMethod]
    public async Task SaveAsNewItemWhilePlayingGoesOnPlayingTheNewItemAndStepsStillWork()
    {
        using var clip = await AnimationInTheEditor.Open(stash => stash.ImportFixture("clip", "animated.gif"), List(Box(1, 1, 3, 3)));
        var session = clip.Session;
        await clip.NextFrameRead();

        var continued = await session.SaveAsNewItem();

        Assert.AreEqual(EditEnding.Continued, continued.Ending, continued.Failure?.Sentence);
        Assert.AreNotEqual(clip.Clip.Id, session.Item.Id);
        Assert.IsTrue(session.Playing);
        await clip.Advance(100, 1);
        session.Step(1);
        await Until(() => session.FrameIndex == 2, "Right still steps");
    }

    // Alone, since the process's memory it measures is every parallel check's too.
    [TestMethod]
    [DoNotParallelize]
    public async Task ALongClipHoldsAtMostTwoFullSizeFramesWhereverItPlaysStepsOrSeeks()
    {
        using var clip = await AnimationInTheEditor.Open(SyntheticClips.Long(60, 200, 150));
        var session = clip.Session;
        await Until(() => session.TimelineComplete, "the filmstrip's frames are never all read");
        var before = GC.GetTotalMemory(forceFullCollection: true);
        var most = session.FullSizeFramesHeld;

        for (var frame = 1; frame < 60; frame++)
        {
            await clip.NextFrameRead();
            most = Math.Max(most, session.FullSizeFramesHeld);
            await clip.Advance(20, frame);
            most = Math.Max(most, session.FullSizeFramesHeld);
        }

        session.Seek(5);
        await Until(() => session.FrameIndex == 5, "a seek back to frame 5");
        most = Math.Max(most, session.FullSizeFramesHeld);
        session.Step(-1);
        await Until(() => session.FrameIndex == 4, "a step back");

        Assert.IsLessThanOrEqualTo(2, Math.Max(most, session.FullSizeFramesHeld), "the frame showing and the one on its way, never more (R7)");
        // Every frame kept would be 60 × 120,000 bytes; two are a fraction of a megabyte.
        Assert.IsLessThan(1_500_000, GC.GetTotalMemory(forceFullCollection: true) - before, "nothing kept per frame played, stepped or sought");
        Assert.AreEqual((200, 150), (session.Frame.Width, session.Frame.Height), "the frame showing is full size");
    }

    [TestMethod]
    public async Task AnEditOpenedWithASavedTrimPlaysRoundItsKeptFramesFromTheFrameOutsideThem()
    {
        using var clip = await AnimationInTheEditor.Open(stash => stash.ImportFixture("clip", "animated.gif"), List(new TrimLayer(new FrameRange(1, 2))));
        var session = clip.Session;
        var shown = Shown(session);
        AssertShowing(session, 0, Red);

        await clip.Advance(100, 1);
        await clip.Advance(200, 2);
        await clip.Advance(300, 1);
        await clip.Advance(200, 2);
        await clip.Advance(300, 1);

        Assert.IsTrue(session.Playing);
        CollectionAssert.AreEqual(new[] { 1, 2, 1, 2, 1 }, shown.ToArray(), "after the first frame, the kept frames alone, the first kept following the last (G22)");
    }

    [TestMethod]
    public async Task ATrimReachingBeyondTheFramesPlaysTheKeptFramesTheEditorCanShow()
    {
        using var clip = await AnimationInTheEditor.Open(stash => stash.ImportFixture("clip", "animated.gif"), List(new TrimLayer(new FrameRange(1, 9))));
        var shown = Shown(clip.Session);

        await clip.Advance(100, 1);
        await clip.Advance(200, 2);
        await clip.Advance(300, 1);

        CollectionAssert.AreEqual(new[] { 1, 2, 1 }, shown.ToArray());
    }

    [TestMethod]
    public async Task ThePlayFollowsTheTrimAsAKeyUndoRedoAndResetChangeItAndWithoutOneWrapsTheWholeClip()
    {
        using var clip = await AnimationInTheEditor.Open("animated.gif");
        var session = clip.Session;
        session.Tool = session.Trim();
        await clip.Advance(100, 1);
        // Frame 2 is read and waits for its time when the end comes in under it.
        await clip.NextFrameRead();
        var shown = Shown(session);

        session.Trim().EndAtPlayhead(RangeEnd.Last);
        Assert.AreEqual(List(new TrimLayer(new FrameRange(0, 1))), session.Layers);
        Assert.IsTrue(session.Playing, "a key sets the end without pausing");
        await clip.Advance(200, 0);
        await clip.Advance(100, 1);
        await clip.Advance(200, 0);

        session.Undo();
        await clip.Advance(100, 1);
        await clip.Advance(200, 2);
        await clip.Advance(300, 0);

        session.Redo();
        await clip.Advance(100, 1);
        await clip.Advance(200, 0);

        session.Trim().Reset();
        await clip.Advance(100, 1);
        await clip.Advance(200, 2);

        CollectionAssert.AreEqual(new[] { 0, 1, 0, 1, 2, 0, 1, 0, 1, 2 }, shown.ToArray(), "frame 2 only while no trim cuts it away");
    }

    [TestMethod]
    public async Task PlayAskedWhileAFrameOutsideTheTrimIsSoughtShowsOnlyKeptFramesAfterIt()
    {
        using var clip = await AnimationInTheEditor.Open(stash => stash.ImportFixture("clip", "animated.gif"), List(new TrimLayer(new FrameRange(1, 2))));
        var session = clip.Session;
        await clip.Advance(100, 1);
        session.Pause();

        session.Seek(0);
        session.Play();
        var asked = session.FrameIndex;
        var shown = Shown(session);
        // The sought frame may have shown before play was asked, or been forgotten: either way four more frames show.
        while (shown.Count < 4)
        {
            await Until(() => clip.Clock.NextDue is not null, "the play never waits for a frame's time");
            var count = shown.Count;
            clip.Clock.Advance(TimeSpan.FromSeconds(1));
            await Until(() => shown.Count > count, "no frame follows");
        }

        Assert.IsTrue(shown.All(index => index is 1 or 2), $"from frame {asked}, only kept frames: {string.Join(", ", shown)}");
    }

    [TestMethod]
    public async Task ASingleKeptFrameReachedFromOutsideHoldsStill()
    {
        using var clip = await AnimationInTheEditor.Open(stash => stash.ImportFixture("clip", "animated.gif"), List(new TrimLayer(new FrameRange(2, 2))));
        var session = clip.Session;

        await clip.Advance(100, 2);
        clip.Clock.Advance(TimeSpan.FromSeconds(5));
        await Task.Delay(50);

        AssertShowing(session, 2, Blue);
        Assert.IsFalse(session.Playing, "nothing follows the one kept frame");
        session.Play();
        Assert.IsFalse(session.Playing, "and play has nothing to go round");
    }

    [TestMethod]
    public async Task KeysNarrowingTheTrimToTheFrameShowingStopThePlayAndSaySo()
    {
        using var clip = await AnimationInTheEditor.Open("animated.gif");
        var session = clip.Session;
        session.Tool = session.Trim();
        await clip.Advance(100, 1);
        await clip.NextFrameRead();
        session.Trim().EndAtPlayhead(RangeEnd.First);
        Assert.IsTrue(session.Playing, "frames 1 and 2 still go round");
        var told = 0;
        session.FrameChanged += () => told++;

        session.Trim().EndAtPlayhead(RangeEnd.Last);

        Assert.AreEqual(List(new TrimLayer(new FrameRange(1, 1))), session.Layers);
        Assert.IsFalse(session.Playing, "one kept frame, showing: nothing to go round");
        Assert.IsGreaterThan(0, told, "the play button is told the play stopped");
        clip.Clock.Advance(TimeSpan.FromSeconds(5));
        await Task.Delay(50);
        AssertShowing(session, 1, Green);
    }

    [TestMethod]
    public async Task SpaceWhileAFrameOutsideAOneFrameTrimIsSoughtEndsOnTheKeptFrame()
    {
        using var clip = await AnimationInTheEditor.Open(stash => stash.ImportFixture("clip", "animated.gif"), List(new TrimLayer(new FrameRange(1, 1))));
        var session = clip.Session;
        await clip.Advance(100, 1);
        Assert.IsFalse(session.Playing, "one kept frame holds still");

        session.Seek(2);
        session.TogglePlaying();
        var shown = Shown(session);
        for (var i = 0; i < 20 && (session.Playing || session.FrameIndex != 1); i++)
        {
            clip.Clock.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(20);
        }

        await Task.Delay(100);
        AssertShowing(session, 1, Green);
        Assert.DoesNotContain(2, shown, $"the frame sought shows after Space: {string.Join(", ", shown)}");
    }

    // Alone, as the long clip's check beside it.
    [TestMethod]
    [DoNotParallelize]
    public async Task ATrimmedRangeGoingRoundHoldsAtMostTwoFullSizeFrames()
    {
        using var clip = await AnimationInTheEditor.Open(stash => stash.ImportPicture("clip", "clip.gif", SyntheticClips.Long(60, 200, 150)), List(new TrimLayer(new FrameRange(40, 49))));
        var session = clip.Session;
        var most = session.FullSizeFramesHeld;

        foreach (var frame in Enumerable.Range(0, 25).Select(lap => 40 + lap % 10))
        {
            await clip.NextFrameRead();
            most = Math.Max(most, session.FullSizeFramesHeld);
            await clip.Advance(20, frame);
            most = Math.Max(most, session.FullSizeFramesHeld);
        }

        Assert.IsLessThanOrEqualTo(2, most, "the frame showing and the one on its way, round the kept frames too (R7)");
    }

    [TestMethod]
    public async Task OnAStillNothingPlaysAndNoKeyChangesTheFrame()
    {
        using var cat = await CatInTheEditor.Open();
        var session = cat.Session;
        var frame = session.Frame;

        session.TogglePlaying();
        session.Step(1);
        session.Seek(3);

        Assert.IsFalse(session.Animated);
        Assert.IsFalse(session.Playing);
        Assert.AreEqual(1, session.FrameCount);
        Assert.AreEqual(0, session.FrameIndex);
        Assert.AreSame(frame, session.Frame);
        Assert.IsEmpty(session.Thumbnails);
        Assert.IsTrue(session.TimelineComplete);
        Assert.IsNull(session.FramesBusy);
    }

    /// <summary>The index of every frame shown from now on, as the canvas is told of it.</summary>
    private static List<int> Shown(EditorSession session)
    {
        var shown = new List<int>();
        var last = session.FrameIndex;
        session.FrameChanged += () =>
        {
            lock (shown)
            {
                if (session.FrameIndex != last)
                {
                    last = session.FrameIndex;
                    shown.Add(last);
                }
            }
        };
        return shown;
    }

    private static bool Held(string path)
    {
        try
        {
            using var alone = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
    }

    private static void AssertShowing(EditorSession session, int index, byte[] colour)
    {
        var showing = session.Showing;
        Assert.AreEqual(index, showing.Index);
        CollectionAssert.AreEqual(colour, showing.Frame.Pixels[..4], $"frame {index}'s own pixels show with its index");
        var composed = session.SampleColor(new PixelPoint(4, 3));
        Assert.AreEqual(new Rgba(colour[2], colour[1], colour[0], colour[3]), composed, "the routine the canvas draws through composes that frame");
    }
}
