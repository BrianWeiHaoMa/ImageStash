using ImageStash.Editor;
using ImageStash.Layers;
using ImageStash.Media;
using ImageStash.Store;
using ImageStash.Tests.Support;
using static ImageStash.Tests.Support.EditorSessions;

namespace ImageStash.Tests.Editor;

/// <summary>
/// Trim and the time ranges (G20, G22, G24, G25, F3, N7, G29; What crosses every boundary): Trim third on the tool row of
/// an animation only; its handles setting the kept frames on the original's timeline, never crossing, each drag one step,
/// a trim keeping the whole clip never stored and Reset removing one; the row's position and kept range from the frames'
/// own durations, stated only once every one is known; the selected text, hide or draw layer's range as a bar whose ends
/// drag, the whole clip while it has none, kept as written when the trim moves; a trimmed, ranged clip saved as exactly
/// its kept frames at their own timing, an animated WebP as a GIF, the original untouched.
/// </summary>
[TestClass]
public sealed class TrimToolTests
{
    // animated.gif's three 8 × 6 frames: red for 100 ms, green for 200, blue for 300.
    private static readonly Rgba Red = new(255, 0, 0, 255), Green = new(0, 255, 0, 255), Blue = new(0, 0, 255, 255);

    [TestMethod]
    public async Task TrimIsThirdOnTheToolRowOfAnAnimationAndAbsentFromAStillAndFromAClipOfOneImage()
    {
        using var clip = await AnimationInTheEditor.Open("animated.gif");
        using var still = await StillInTheEditor.Open();
        using var oneImage = await AnimationInTheEditor.Open(SyntheticClips.Gif(1, 16, 12));

        CollectionAssert.AreEqual(new[] { "Text", "Crop", "Trim", "Hide", "Draw" }, clip.Session.Tools.Select(tool => tool.Name).ToArray());
        Assert.AreEqual("Text", clip.Session.Tool.Name, "Text current at the opening");
        Assert.AreEqual("Drag the handles under the picture", clip.Session.Trim().Hint, "what the mouse does (G20)");
        CollectionAssert.AreEqual(new[] { "Text", "Crop", "Hide", "Draw" }, still.Session.Tools.Select(tool => tool.Name).ToArray());
        Assert.IsFalse(oneImage.Clip.Facts.Animated, "a clip of one image is a still by its facts");
        CollectionAssert.AreEqual(new[] { "Text", "Crop", "Hide", "Draw" }, oneImage.Session.Tools.Select(tool => tool.Name).ToArray());
    }

    [TestMethod]
    public async Task TheLeftHandleDraggedToTheSecondFrameKeepsTwoFramesTheRowSaysSoAndOneUndoGivesTheWholeClipBack()
    {
        using var clip = await AnimationInTheEditor.Open("animated.gif");
        var session = clip.Session;
        await Until(() => session.TimelineComplete, "the frames are never all read");
        session.Tool = session.Trim();
        var trim = session.Trim();
        Assert.AreEqual(new FrameRange(0, 2), trim.Kept, "every frame kept without a trim");
        Assert.AreEqual("Keeps 0:00.0 – 0:00.6 · 0.6 s of 0.6 s", trim.KeptWords);

        trim.DragHandle(RangeEnd.First, 1);

        Assert.AreEqual(List(new TrimLayer(new FrameRange(1, 2))), session.Layers, "the trim on the original's frame indices");
        Assert.AreEqual(new FrameRange(1, 2), trim.Kept);
        Assert.AreEqual("Keeps 0:00.1 – 0:00.6 · 0.5 s of 0.6 s", trim.KeptWords, "the second and third frames' own 200 and 300 ms");
        Assert.AreEqual("0:00.0 / 0:00.6", trim.Position, "the playhead still on the first frame, over the whole clip");
        Assert.AreEqual(new PixelSize(8, 6), session.OutputSize);
        Assert.IsNull(session.Selected, "the trim is never selected");
        session.Undo();
        Assert.IsTrue(session.Layers.IsEmpty, "the drag was one step (G25)");
        Assert.IsFalse(session.CanUndo, "and only one");
    }

    [TestMethod]
    public async Task AHandleDraggedBackToTheEdgeStoresNoTrimAndResetRemovesOneAsOneStep()
    {
        using var clip = await AnimationInTheEditor.Open("animated.gif");
        var session = clip.Session;
        session.Tool = session.Trim();
        var trim = session.Trim();

        trim.DragHandle(RangeEnd.First, 1);
        trim.DragHandle(RangeEnd.First, 0);
        Assert.IsTrue(session.Layers.IsEmpty, "a trim keeping the whole clip is no trim");
        trim.DragHandle(RangeEnd.Last, 2);
        Assert.IsTrue(session.Layers.IsEmpty);
        session.Undo();
        Assert.AreEqual(List(new TrimLayer(new FrameRange(1, 2))), session.Layers, "the drag back was a step of its own");

        trim.Reset();
        Assert.IsTrue(session.Layers.IsEmpty, "Reset keeps every frame again");
        session.Undo();
        Assert.AreEqual(List(new TrimLayer(new FrameRange(1, 2))), session.Layers, "as one step");
        session.Redo();
        trim.Reset();
        Assert.IsTrue(session.Layers.IsEmpty);
        session.Undo();
        Assert.AreEqual(List(new TrimLayer(new FrameRange(1, 2))), session.Layers, "Reset with no trim made no step of its own");
    }

    [TestMethod]
    public async Task TheHandlesNeverCrossSoAtLeastOneFrameIsKept()
    {
        using var clip = await AnimationInTheEditor.Open("animated.gif");
        var session = clip.Session;
        session.Tool = session.Trim();
        var trim = session.Trim();

        trim.DragHandle(RangeEnd.Last, 1);
        Assert.AreEqual(new FrameRange(0, 1), trim.Kept);
        trim.DragHandle(RangeEnd.First, 2);
        Assert.AreEqual(new FrameRange(1, 1), trim.Kept, "the left handle stops at the right");
        trim.DragHandle(RangeEnd.Last, 0);
        Assert.AreEqual(new FrameRange(1, 1), trim.Kept, "and the right at the left");
        trim.DragHandle(RangeEnd.Last, 9);
        Assert.AreEqual(new FrameRange(1, 2), trim.Kept, "past the clip's end it stops on the last frame");
        Assert.AreEqual(List(new TrimLayer(new FrameRange(1, 2))), session.Layers);
    }

    [TestMethod]
    public async Task AHandleOrABarEndPressedAndLetGoWithoutMovingChangesNothing()
    {
        var box = Box(1, 1, 3, 3) with { Range = new FrameRange(5, 9) };
        using var clip = await AnimationInTheEditor.Open(stash => stash.ImportFixture("clip", "animated.gif"), List(new TrimLayer(new FrameRange(1, 9)), box));
        var session = clip.Session;
        session.Tool = session.Trim();

        session.Trim().PressHandle(RangeEnd.Last);
        session.Trim().ReleaseHandle();
        session.Tool = session.Draw();
        session.Click(2, 2);
        Assert.AreEqual(1, session.Selected);
        Assert.AreEqual(new FrameRange(2, 2), session.SelectedRange, "a range beyond the frames drawn on the last");
        session.PressRangeEnd(RangeEnd.First);
        session.ReleaseRangeEnd();

        Assert.AreEqual(List(new TrimLayer(new FrameRange(1, 9)), box), session.Layers, "both kept as written");
        Assert.IsFalse(session.CanUndo);
    }

    [TestMethod]
    public async Task ALabelSpansTheWholeClipAndItsBarsStartDraggedToTheLastFrameRangesItAsOneStep()
    {
        using var clip = await AnimationInTheEditor.Open("animated.gif");
        var session = clip.Session;
        session.Text().Choose(TextKind.Label);
        session.Click(1, 1);
        session.SetTypedText("oh");
        session.EndTyping();
        Assert.IsNull(((TextLayer)session.SelectedLayer!).Range, "the whole clip by default");
        Assert.AreEqual(new FrameRange(0, 2), session.SelectedRange, "its bar spans the filmstrip");
        var added = session.Layers;

        session.DragBarEnd(RangeEnd.First, 2);

        Assert.AreEqual(new FrameRange(2, 2), ((TextLayer)session.SelectedLayer!).Range);
        Assert.AreEqual(new FrameRange(2, 2), session.SelectedRange);
        Assert.AreSame(session.Text(), session.Tool, "the layer's own tool stays current");
        session.Undo();
        Assert.AreEqual(added, session.Layers, "one step");
        session.Redo();
        session.DragBarEnd(RangeEnd.First, 0);
        Assert.IsNull(((TextLayer)session.SelectedLayer!).Range, "dragged back over the whole clip it is none again");
        session.DragBarEnd(RangeEnd.Last, 1);
        Assert.AreEqual(new FrameRange(0, 1), ((TextLayer)session.SelectedLayer!).Range);
        session.DragBarEnd(RangeEnd.First, 2);
        Assert.AreEqual(new FrameRange(1, 1), ((TextLayer)session.SelectedLayer!).Range, "its start stops at its end");
    }

    [TestMethod]
    public async Task ABarShowsForTheSelectedTextHideOrDrawLayerOnlyAndNotForACaptionBarOrWithNothingSelected()
    {
        var hide = new HideLayer(new PixelRect(0, 0, 2, 2), HideKind.Blur, null);
        var bar = new CaptionBarLayer("hi", "Segoe UI", 2, new Rgba(0, 0, 0, 255), Outline.None);
        using var clip = await AnimationInTheEditor.Open(stash => stash.ImportFixture("clip", "animated.gif"), List(bar, hide));
        var session = clip.Session;
        Assert.IsNull(session.SelectedRange, "nothing selected, no bar");

        session.Tool = session.Hide();
        session.Click(1, 1);
        Assert.AreEqual(1, session.Selected);
        Assert.AreEqual(new FrameRange(0, 2), session.SelectedRange, "a hide's");

        session.Drawing(MarkKind.Box);
        session.DragThrough((4, 1), (6, 3), (7, 4));
        Assert.IsInstanceOfType<DrawMarkLayer>(session.SelectedLayer);
        Assert.AreEqual(new FrameRange(0, 2), session.SelectedRange, "a box just drawn, still selected");
        session.DragBarEnd(RangeEnd.First, 1);
        Assert.AreEqual(new FrameRange(1, 2), ((DrawMarkLayer)session.SelectedLayer!).Range);

        session.Select(0);
        Assert.IsInstanceOfType<CaptionBarLayer>(session.SelectedLayer, "the caption bar selected");
        Assert.IsNull(session.SelectedRange, "a caption bar spans the whole clip and has no bar");
        var layers = session.Layers;
        session.DragBarEnd(RangeEnd.First, 2);
        Assert.AreEqual(layers, session.Layers, "and nothing of it drags");
    }

    [TestMethod]
    public async Task AMovedTrimLeavesARangeAsWrittenAndTheBarShowsBeyondTheHandles()
    {
        var label = new TextLayer(TextPreset.Label, "oh", "Segoe UI", 4, new Rgba(255, 255, 255, 255), Outline.Auto, new PixelRect(0, 0, 6, 5), 0, new FrameRange(2, 2));
        using var clip = await AnimationInTheEditor.Open(stash => stash.ImportFixture("clip", "animated.gif"), List(label));
        var session = clip.Session;
        session.Tool = session.Trim();

        session.Trim().DragHandle(RangeEnd.First, 1);
        Assert.AreEqual(new FrameRange(2, 2), ((TextLayer)session.Layers.Layers[0]).Range, "the trim moved, the range as written (G22)");
        session.Trim().DragHandle(RangeEnd.Last, 1);
        session.Trim().DragHandle(RangeEnd.First, 0);
        Assert.AreEqual(new FrameRange(0, 1), session.Trim().Kept);
        Assert.AreEqual(new FrameRange(2, 2), ((TextLayer)session.Layers.Layers[0]).Range, "beyond the handles, still as written");

        session.Tool = session.Text();
        session.Click(1, 1);
        Assert.AreEqual(0, session.Selected);
        Assert.AreEqual(new FrameRange(2, 2), session.SelectedRange, "its bar over the trimmed-away frame");
    }

    [TestMethod]
    public async Task ABoxRangedFromTheSecondFrameIsAbsentFromTheFirstInTheSavedGif()
    {
        using var clip = await AnimationInTheEditor.Open("animated.gif");
        var session = clip.Session;
        session.Drawing(MarkKind.Box);
        session.DragThrough((1, 1), (4, 3), (6, 4));

        session.DragBarEnd(RangeEnd.First, 1);
        var saved = await session.Save();

        Assert.AreEqual(EditEnding.Saved, saved.Ending, saved.Failure?.Sentence);
        var frames = MediaFixtures.Frames(saved.Item!.FilePath);
        Assert.HasCount(3, frames);
        Assert.IsTrue(Pixels(frames[0]).All(pixel => pixel == Red), "the first frame bare");
        Assert.IsTrue(Pixels(frames[1]).Any(pixel => pixel != Green) && Pixels(frames[2]).Any(pixel => pixel != Blue), "the box on the second and the third");
    }

    [TestMethod]
    public async Task ATrimmedClipWithARangedLabelSavesExactlyTheKeptFramesAtTheirOwnTimingAndTheOriginalKeepsThree()
    {
        var label = new TextLayer(TextPreset.Label, "W", "Segoe UI", 8, new Rgba(255, 255, 255, 255), Outline.Auto, new PixelRect(0, 0, 8, 6), 0, null);
        using var clip = await AnimationInTheEditor.Open(stash => stash.ImportFixture("clip", "animated.gif"), List(label));
        var session = clip.Session;
        var store = clip.Stash.Store!;
        var originalBytes = File.ReadAllBytes(clip.Clip.FilePath);
        session.Tool = session.Text();
        session.Click(4, 3);
        session.DragBarEnd(RangeEnd.First, 2);
        session.Tool = session.Trim();
        session.Trim().DragHandle(RangeEnd.First, 1);
        Assert.AreEqual(List(label with { Range = new FrameRange(2, 2) }, new TrimLayer(new FrameRange(1, 2))), session.Layers);

        var saved = await session.Save();

        Assert.AreEqual(EditEnding.Saved, saved.Ending, saved.Failure?.Sentence);
        Assert.AreEqual(".gif", Path.GetExtension(saved.Item!.FilePath));
        Assert.AreEqual(new MediaFacts(ImageFormat.Gif, 8, 6, true, 2, 500), saved.Item.Facts, "two frames lasting 500 ms (G19)");
        CollectionAssert.AreEqual(new[] { 20, 30 }, GifImages.Read(saved.Item.FilePath).Images.Select(image => image.DelayCs).ToList(), "their own timing, not the recorder's (F3)");
        var frames = MediaFixtures.Frames(saved.Item.FilePath);
        Assert.IsTrue(Pixels(frames[0]).All(pixel => pixel == Green), "the first kept frame, without the label ranged on the next");
        Assert.IsTrue(Pixels(frames[1]).Any(pixel => pixel != Blue), "the second, with the label");
        var original = (await EditedItems.ForEdit(store, clip.Clip.Id)).OriginalPath;
        CollectionAssert.AreEqual(originalBytes, File.ReadAllBytes(original), "the original untouched (G29)");
        Assert.HasCount(3, MediaFixtures.Frames(original), "the trimmed-away frame still in it (G22)");
    }

    [TestMethod]
    public async Task AnAnimatedWebPWithALabelSavesAsAGif()
    {
        var label = new TextLayer(TextPreset.Label, "W", "Segoe UI", 8, new Rgba(255, 255, 255, 255), Outline.Auto, new PixelRect(0, 0, 8, 6), 0, null);
        using var clip = await AnimationInTheEditor.Open(stash => stash.ImportFixture("clip", "animated.webp"), List(label));
        var originalBytes = File.ReadAllBytes(clip.Clip.FilePath);

        var saved = await clip.Session.Save();

        Assert.AreEqual(EditEnding.Saved, saved.Ending, saved.Failure?.Sentence);
        Assert.AreEqual(".gif", Path.GetExtension(saved.Item!.FilePath), "N7: a WebP animation is finished as a GIF");
        Assert.AreEqual(new MediaFacts(ImageFormat.Gif, 8, 6, true, 3, 600), saved.Item.Facts);
        var original = (await EditedItems.ForEdit(clip.Stash.Store!, clip.Clip.Id)).OriginalPath;
        Assert.AreEqual(".webp", Path.GetExtension(original));
        CollectionAssert.AreEqual(originalBytes, File.ReadAllBytes(original), "the original untouched (G29)");
    }

    [TestMethod]
    public async Task UnderTrimThePlayAndTheStepsWorkAndDelRemovesNeitherTheTrimNorAnythingElse()
    {
        using var clip = await AnimationInTheEditor.Open("animated.gif");
        var session = clip.Session;
        session.Tool = session.Trim();
        session.Trim().DragHandle(RangeEnd.First, 1);
        Assert.IsTrue(session.Playing, "it plays from the opening");

        session.TogglePlaying();
        Assert.IsFalse(session.Playing);
        session.Step(1);
        await Until(() => session.FrameIndex == 1, "Right never steps on");
        session.Step(-1);
        await Until(() => session.FrameIndex == 0, "Left never steps back");
        Assert.AreEqual(EditorKeyAction.DeleteSelected, EditorKeys.Route(Avalonia.Input.Key.Delete, Avalonia.Input.KeyModifiers.None, false, false));
        session.DeleteSelected();

        Assert.AreEqual(List(new TrimLayer(new FrameRange(1, 2))), session.Layers, "Del removes a selected layer, and the trim is never one");
        Assert.AreSame(session.Trim(), session.Tool);
    }

    [TestMethod]
    public async Task WithThePlayheadOnAFrameTheTrimCutsAwayThatFrameIsComposedAndThePositionReadsItsStart()
    {
        using var clip = await AnimationInTheEditor.Open(stash => stash.ImportFixture("clip", "animated.gif"), List(new TrimLayer(new FrameRange(1, 2))));
        var session = clip.Session;
        await Until(() => session.TimelineComplete, "the frames are never all read");
        session.Tool = session.Trim();
        session.Seek(2);
        await Until(() => session.FrameIndex == 2, "frame 2 never shows");
        Assert.AreEqual("0:00.3 / 0:00.6", session.Trim().Position);

        session.Seek(0);
        await Until(() => session.FrameIndex == 0, "frame 0 never shows");

        Assert.AreEqual(Red, session.SampleColor(new PixelPoint(4, 3)), "the canvas composes the frame at the playhead, kept or not");
        Assert.AreEqual("0:00.0 / 0:00.6", session.Trim().Position);
    }

    [TestMethod]
    public async Task BeforeEveryFramesDurationIsKnownTheRowStatesNoTimeAndAfterItStatesTheKeptFrames()
    {
        // Long enough to take a while to read: a thousand frames of 20 ms.
        using var clip = await AnimationInTheEditor.Open(SyntheticClips.Long(1000, 200, 150));
        var session = clip.Session;
        var trim = session.Trim();

        Assert.IsFalse(session.TimelineComplete);
        Assert.IsNull(trim.KeptWords, "no kept range as a measurement before the durations are known (G35)");
        Assert.IsNull(trim.Position);
        Assert.AreEqual("Reading the frames of “clip”…", session.FramesBusy, "the busy state the row names instead");

        await Until(() => session.TimelineComplete, "the frames are never all read");
        Assert.AreEqual("Keeps 0:00.0 – 0:20.0 · 20.0 s of 20.0 s", trim.KeptWords);
        session.Tool = trim;
        trim.DragHandle(RangeEnd.Last, 499);
        Assert.AreEqual("Keeps 0:00.0 – 0:10.0 · 10.0 s of 20.0 s", trim.KeptWords);
    }

    [TestMethod]
    public async Task TheTimesAreTheFramesOwnDurationsWhereAQuickFrameShowsLonger()
    {
        // long.gif: sixty frames of 10, 20, 30, 40 and 50 ms in turn, the 10 ms ones shown for 100 ms as a browser shows them.
        using var clip = await AnimationInTheEditor.Open("long.gif");
        var session = clip.Session;
        await Until(() => session.TimelineComplete, "the frames are never all read");
        session.Tool = session.Trim();

        session.Trim().DragHandle(RangeEnd.First, 5);

        Assert.AreEqual("Keeps 0:00.2 – 0:01.8 · 1.7 s of 1.8 s", session.Trim().KeptWords, "what the saved clip lasts, not how long it shows here (F3)");
        Assert.AreEqual("0:00.0 / 0:01.8", session.Trim().Position);
    }

    [TestMethod]
    public async Task OnAClipWhoseLaterFramesCannotBeReadTheRowStatesNoLengthAndALeftHandleDragLeavesTheRightEndWhereItWas()
    {
        var gif = SyntheticClips.WithFrameUnreadable(SyntheticClips.Gif(6, 16, 12), 3);
        using var clip = await AnimationInTheEditor.Open(async stash =>
        {
            // As an earlier build or a later change to the file might have left it: Media's import would refuse it.
            var staged = await stash.Stage("clip.gif", gif);
            var committed = await stash.Store!.CommitImport(TestStash.Candidate(staged, "clip", gif, new MediaFacts(ImageFormat.Gif, 16, 12, true, 6, 600)));
            Assert.IsTrue(committed.Succeeded, committed.Failure?.ToString());
            return committed.Value!.Item;
        });
        var session = clip.Session;
        await Until(() => session.FramesFailure is not null && session.TimelineComplete, "the frame that cannot be read is never named");
        Assert.AreEqual(3, session.FrameCount, "the frames before it");
        session.Tool = session.Trim();
        var trim = session.Trim();

        Assert.IsNull(trim.KeptWords, "three frames' 300 ms are not the clip's length (G35)");
        Assert.IsNull(trim.Position);
        Assert.AreEqual(new FrameRange(0, 2), trim.Kept, "the handles over the frames read");

        trim.DragHandle(RangeEnd.First, 1);

        Assert.AreEqual(List(new TrimLayer(new FrameRange(1, 5))), session.Layers, "the right end, never held, still at the original's last frame (G22)");
        Assert.AreEqual(new FrameRange(1, 2), trim.Kept);
        var saved = await session.Save();
        Assert.AreEqual(EditEnding.Refused, saved.Ending, "the frames the trim still keeps cannot be read: refused by name, none dropped unasked (G35)");
        Assert.AreEqual(MediaDecoder.UnreadableReason, saved.Failure?.Reason);
    }

    [TestMethod]
    public async Task TheFootersEstimateFollowsTheKeptFrames()
    {
        using var clip = await AnimationInTheEditor.Open("animated.gif");
        var session = clip.Session;
        await Until(() => session.EstimatedBytes is not null, "the estimate never comes");
        var whole = session.EstimatedBytes;
        session.Tool = session.Trim();

        session.Trim().DragHandle(RangeEnd.Last, 0);
        await Until(() => session.EstimatedBytes is not null, "the estimate for the trim never comes");

        var edit = await EditedItems.ForEdit(clip.Stash.Store!, clip.Clip.Id);
        var oneFrame = FinishedPicture.Estimate(edit.OriginalPath, edit.OriginalSha256, "clip", List(new TrimLayer(new FrameRange(0, 0))), ImageFormat.Gif, TestStash.ShortRetry);
        Assert.AreEqual(oneFrame.Value, session.EstimatedBytes, "the size of the one frame kept (G24)");
        Assert.IsLessThan(whole!.Value, session.EstimatedBytes!.Value);
    }

    private static IEnumerable<Rgba> Pixels(MediaFrame frame) =>
        Enumerable.Range(0, frame.Width * frame.Height).Select(i => new Rgba(frame.Pixels[i * 4 + 2], frame.Pixels[i * 4 + 1], frame.Pixels[i * 4], frame.Pixels[i * 4 + 3]));
}
