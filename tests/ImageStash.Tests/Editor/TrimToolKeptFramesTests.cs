using ImageStash.Editor;
using ImageStash.Layers;
using ImageStash.Media;
using ImageStash.Store;
using ImageStash.Tests.Support;
using static ImageStash.Tests.Support.EditorSessions;

namespace ImageStash.Tests.Editor;

/// <summary>
/// Trim's kept frames as a person finds them (G22, G25, G26, F3): a handle or a layer's bar end pausing on the frame it
/// is on while it drags and resting there once let go, the end the range took and not the frame asked for; the frames
/// outside the handles shown by a seek, the arrows and a handle dragged outward; <c>[</c> and <c>]</c> setting an end at
/// the frame showing as one step, none when nothing changes or the frame lies past the other end, nothing while a
/// Save runs; a clip saved after a dragged start and a key-set end holding exactly its kept frames.
/// </summary>
[TestClass]
public sealed class TrimToolKeptFramesTests
{
    [TestMethod]
    public async Task AHandlePausesOnTheFrameItsEndIsOnWhileItDragsAndRestsThereOnceLetGo()
    {
        using var clip = await AnimationInTheEditor.Open("animated.gif");
        var session = clip.Session;
        session.Tool = session.Trim();
        var trim = session.Trim();
        await clip.Advance(100, 1);

        trim.PressHandle(RangeEnd.First);
        Assert.IsFalse(session.Playing, "the press pauses");
        await Until(() => session.FrameIndex == 0, "the start's frame never shows at the press");
        trim.DragHandle(1);
        await Until(() => session.FrameIndex == 1, "the frame the start is dragged to never shows before release");
        trim.ReleaseHandle();
        clip.Clock.Advance(TimeSpan.FromSeconds(5));
        await Task.Delay(50);
        Assert.AreEqual((1, false), (session.FrameIndex, session.Playing), "after release it rests, paused, on the first kept frame");

        trim.PressHandle(RangeEnd.Last);
        await Until(() => session.FrameIndex == 2, "the end's frame, the last kept one, never shows");
        trim.DragHandle(0);
        await Until(() => session.FrameIndex == 1, "an end dragged past the start shows the frame it stopped at");
        trim.ReleaseHandle();
        Assert.AreEqual(List(new TrimLayer(new FrameRange(1, 1))), session.Layers);
        Assert.AreEqual(1, session.FrameIndex);
    }

    [TestMethod]
    public async Task ADragThatLeavesTheRangeAsItWasStillPausesOnItsEndAndAFastDragSettlesOnTheEndLetGo()
    {
        using var clip = await AnimationInTheEditor.Open("animated.gif");
        var session = clip.Session;
        session.Tool = session.Trim();
        var trim = session.Trim();

        trim.PressHandle(RangeEnd.Last);
        trim.DragHandle(2);
        trim.ReleaseHandle();
        await Until(() => session.FrameIndex == 2, "a drag changing nothing never pauses on its end");
        Assert.IsFalse(session.Playing);
        Assert.IsFalse(session.CanUndo, "and makes no step");

        trim.PressHandle(RangeEnd.First);
        trim.DragHandle(1);
        trim.DragHandle(2);
        trim.DragHandle(1);
        trim.DragHandle(0);
        trim.DragHandle(1);
        trim.ReleaseHandle();
        await Until(() => session.FrameIndex == 1, "the end let go never shows");
        await Task.Delay(100);
        Assert.AreEqual(1, session.FrameIndex, "no frame passed on the way shows after it");
        Assert.AreEqual(List(new TrimLayer(new FrameRange(1, 2))), session.Layers);
    }

    [TestMethod]
    public async Task ALayersBarEndPausesOnItsFrameAsTheTrimsHandlesDo()
    {
        using var clip = await AnimationInTheEditor.Open(stash => stash.ImportFixture("clip", "animated.gif"), List(Box(1, 1, 3, 3)));
        var session = clip.Session;
        session.Tool = session.Draw();
        session.Click(2, 2);
        Assert.AreEqual(0, session.Selected);

        session.PressRangeEnd(RangeEnd.Last);
        Assert.IsFalse(session.Playing);
        await Until(() => session.FrameIndex == 2, "the bar's end frame never shows");
        session.DragRangeEnd(1);
        await Until(() => session.FrameIndex == 1, "the bar's end dragged never shows its frame");
        session.ReleaseRangeEnd();

        Assert.AreEqual(new FrameRange(0, 1), ((DrawMarkLayer)session.Layers.Layers[0]).Range);
        Assert.AreEqual((1, false), (session.FrameIndex, session.Playing));
    }

    [TestMethod]
    public async Task FramesOutsideTheHandlesAreShownByASeekTheArrowsAndAHandleDraggedOutward()
    {
        using var clip = await AnimationInTheEditor.Open(stash => stash.ImportFixture("clip", "animated.gif"), List(new TrimLayer(new FrameRange(1, 1))));
        var session = clip.Session;
        session.Tool = session.Trim();

        session.Seek(2);
        await Until(() => session.FrameIndex == 2, "a seek past the kept frame never shows its frame");
        Assert.IsFalse(session.Playing);
        session.Step(-1);
        session.Step(-1);
        await Until(() => session.FrameIndex == 0, "Left never reaches the first frame, cut away");
        session.Step(2);
        await Until(() => session.FrameIndex == 2, "Right never reaches the last frame, cut away");

        session.Trim().DragHandle(RangeEnd.First, 0);
        await Until(() => session.FrameIndex == 0, "the start dragged outward never shows the frame it widens to");
        Assert.AreEqual(List(new TrimLayer(new FrameRange(0, 1))), session.Layers, "the trim widened by eye (G22)");
    }

    [TestMethod]
    public async Task TheBracketKeysSetAnEndAtTheFrameShowingEachOneStepAndNoneWhenNothingChanges()
    {
        using var clip = await AnimationInTheEditor.Open("animated.gif");
        var session = clip.Session;
        session.Tool = session.Trim();
        var trim = session.Trim();
        await ShowPaused(session, 1);

        trim.EndAtPlayhead(RangeEnd.First);
        Assert.AreEqual(List(new TrimLayer(new FrameRange(1, 2))), session.Layers, "[ starts the kept frames at the frame showing");
        trim.EndAtPlayhead(RangeEnd.First);
        session.Undo();
        Assert.IsTrue(session.Layers.IsEmpty, "the second press, changing nothing, made no step");
        Assert.IsFalse(session.CanUndo, "the first was one step");
        session.Redo();

        trim.EndAtPlayhead(RangeEnd.Last);
        Assert.AreEqual(List(new TrimLayer(new FrameRange(1, 1))), session.Layers, "] ends them there");
        await ShowPaused(session, 2);
        trim.EndAtPlayhead(RangeEnd.First);
        Assert.AreEqual(List(new TrimLayer(new FrameRange(1, 1))), session.Layers, "past the end, [ changes nothing (G25)");
        await ShowPaused(session, 0);
        trim.EndAtPlayhead(RangeEnd.Last);
        Assert.AreEqual(List(new TrimLayer(new FrameRange(1, 1))), session.Layers, "before the start, ] changes nothing");
        trim.EndAtPlayhead(RangeEnd.First);
        Assert.AreEqual(List(new TrimLayer(new FrameRange(0, 1))), session.Layers);
        await ShowPaused(session, 2);
        trim.EndAtPlayhead(RangeEnd.First);
        Assert.AreEqual(List(new TrimLayer(new FrameRange(0, 1))), session.Layers, "past the end, [ moves the start nowhere, not even up to the end");
        trim.EndAtPlayhead(RangeEnd.Last);
        Assert.IsTrue(session.Layers.IsEmpty, "a range come to cover the whole clip is no trim");

        session.Undo();
        Assert.AreEqual(List(new TrimLayer(new FrameRange(0, 1))), session.Layers, "each press one step");
        session.Undo();
        Assert.AreEqual(List(new TrimLayer(new FrameRange(1, 1))), session.Layers, "and the presses past an end none");
    }

    [TestMethod]
    public async Task WhileASaveRunsTheBracketKeysChangeNothing()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var clip = await stash.ImportFixture("clip", "animated.gif");
        using var rendering = new SemaphoreSlim(0);
        using var go = new SemaphoreSlim(0);
        FinishedPictureRenderer waiting = (original, name, layers, format, work, retry) =>
        {
            rendering.Release();
            go.Wait();
            return FinishedPicture.Render(original, name, layers, format, work, retry);
        };
        using var session = await Open(stash, clip, waiting, List(new TrimLayer(new FrameRange(0, 1))));
        session.Tool = session.Trim();
        await ShowPaused(session, 1);

        var saving = session.Save();
        Assert.IsTrue(await rendering.WaitAsync(TimeSpan.FromSeconds(10)));
        session.Trim().EndAtPlayhead(RangeEnd.First);
        Assert.AreEqual(List(new TrimLayer(new FrameRange(0, 1))), session.Layers, "the busy Save's layers stand");
        go.Release();

        Assert.AreEqual(EditEnding.Saved, (await saving).Ending);
    }

    [TestMethod]
    public async Task AClipSavedAfterADraggedStartAndAKeySetEndHoldsExactlyTheKeptFramesAndReopensWithThem()
    {
        // long.gif's sixty frames ask for 10, 20, 30, 40 and 50 ms in turn.
        using var clip = await AnimationInTheEditor.Open("long.gif");
        var session = clip.Session;
        session.Tool = session.Trim();
        session.Trim().DragHandle(RangeEnd.First, 1);
        await ShowPaused(session, 3);
        session.Trim().EndAtPlayhead(RangeEnd.Last);
        Assert.AreEqual(List(new TrimLayer(new FrameRange(1, 3))), session.Layers);

        var saved = await session.Save();

        Assert.AreEqual(EditEnding.Saved, saved.Ending, saved.Failure?.Sentence);
        CollectionAssert.AreEqual(new[] { 2, 3, 4 }, GifImages.Read(saved.Item!.FilePath).Images.Select(image => image.DelayCs).ToList(), "the second to fourth frames at their own timing (F3)");
        var original = (await EditedItems.ForEdit(clip.Stash.Store!, saved.Item.Id)).OriginalPath;
        Assert.HasCount(60, MediaFixtures.Frames(original), "the frames outside still in the original (G22)");
        using var reopened = await Open(clip.Stash, saved.Item);
        Assert.AreEqual(List(new TrimLayer(new FrameRange(1, 3))), reopened.Layers, "the next Edit brings the trim back live (G26)");
    }

    /// <summary>Pauses on frame <paramref name="index"/>, as a click on its thumbnail does.</summary>
    private static async Task ShowPaused(EditorSession session, int index)
    {
        session.Seek(index);
        await Until(() => session.FrameIndex == index, $"frame {index} never shows");
    }
}
