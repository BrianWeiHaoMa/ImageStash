using ImageStash.Media;
using ImageStash.Tests.Support;
using static ImageStash.Tests.Support.SyntheticFrames;

namespace ImageStash.Tests.Capture;

/// <summary>
/// The facts and the content hash a recording's clip is handed on with, from Media's GIF encoder as it wrote the file,
/// against Media's own reading of that file (G12, G15, G19, F3), over the Screen seam's double and a clock the check
/// steps: samples skipped, frames that change nothing, and a Stop between two centiseconds. The cap and a clip of one
/// image are <see cref="RecorderEndingTests"/>'s, read the same way.
/// </summary>
[TestClass]
// The real encoder quantizes on the thread pool and waits for it, as GifEncoderTests found: one check at a time.
[DoNotParallelize]
public sealed class RecordedClipFactsTests
{
    [TestMethod]
    public async Task SamplesSkippedAreNoFramesOfTheClipItsFactsSay()
    {
        using var stash = new TestStash();
        var path = (await stash.OpenCleanly()).AllocateWorkPath(".gif").Value!;
        var recording = new SteppedRecorder(GifEncoder.Opener(TestStash.ShortRetry));
        // The sampler held up until 300 ms: the samples due at 70 to 280 are missed (G15).
        recording.Clock.FiresOnTicksOf = TimeSpan.FromMilliseconds(300);
        recording.Start(path);

        recording.Step(0);
        recording.Step(300);
        recording.Step(40);
        recording.Recorder.Stop();
        var ended = await recording.Ended();

        Assert.IsTrue(ended.Succeeded, ended.Failure?.Sentence);
        WrittenClips.AreAsRead(new GifClip(path, 2, 340, null), ended.Value, "two frames, the first lasting until 280, not one per sample due");
    }

    [TestMethod]
    public async Task FramesThatChangeNothingAreFramesOfTheClipItsFactsSay()
    {
        using var stash = new TestStash();
        var path = (await stash.OpenCleanly()).AllocateWorkPath(".gif").Value!;
        var recording = new SteppedRecorder(GifEncoder.Opener(TestStash.ShortRetry));
        var still = Frame(SteppedRecorder.Display.Bounds.Width, SteppedRecorder.Display.Bounds.Height, (_, _) => (10, 20, 30));
        recording.Screen.EachGrab = _ => Outcome<MediaFrame>.Of(still);
        recording.Start(path);

        recording.StepKeepingUp(210);
        recording.Step(60);
        recording.Recorder.Stop();
        var ended = await recording.Ended();

        Assert.IsTrue(ended.Succeeded, ended.Failure?.Sentence);
        WrittenClips.AreAsRead(new GifClip(path, 4, 270, null), ended.Value, "every sample an image, though it changes no pixel");
    }

    [TestMethod]
    public async Task AStopBetweenTwoCentisecondsGivesTheLengthOfTheDelaysAsWritten()
    {
        using var stash = new TestStash();
        var path = (await stash.OpenCleanly()).AllocateWorkPath(".gif").Value!;
        var recording = new SteppedRecorder(GifEncoder.Opener(TestStash.ShortRetry));
        recording.Start(path);

        recording.StepKeepingUp(140);
        recording.Step(65);
        recording.Recorder.Stop();
        var ended = await recording.Ended();

        Assert.IsTrue(ended.Succeeded, ended.Failure?.Sentence);
        WrittenClips.AreAsRead(new GifClip(path, 3, 210, null), ended.Value, "Stop at 205 ms rounds to 21 cs, halves up, so the clip lasts 210 ms, not 205");
    }
}
