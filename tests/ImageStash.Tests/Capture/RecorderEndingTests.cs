using ImageStash.Capture;
using ImageStash.Media;
using ImageStash.Store;
using ImageStash.Tests.Support;
using static ImageStash.Tests.Support.SyntheticFrames;

namespace ImageStash.Tests.Capture;

/// <summary>
/// Every way a recording ends, over the Screen seam's double, a clock the check steps, and the encoder's double or Media's
/// real GIF encoder: Stop draining what was sampled before it and closing the clip at its moment, a grab started at that
/// moment dropped, no frame meaning nothing kept; the cap ending the clip at the cap itself (G15, G32); Cancel discarding
/// the queue and naming nothing; a refused grab, an encoder failure or a refused open named, never thrown (E5, G35); the
/// file left closed at the path it was given and never deleted by the recorder (R2). The pace is <see cref="RecorderTests"/>'s.
/// </summary>
[TestClass]
// The real encoder quantizes on the thread pool and waits for it, as GifEncoderTests found: one check at a time.
[DoNotParallelize]
public sealed class RecorderEndingTests
{
    [TestMethod]
    public async Task TheCapEndsTheClipAtTheCapItselfWithEverySampleAnImageLastingUntilTheNext()
    {
        using var stash = new TestStash();
        var path = (await stash.OpenCleanly()).AllocateWorkPath(".gif").Value!;
        var recording = new SteppedRecorder(GifEncoder.Opener(TestStash.ShortRetry));

        Assert.IsTrue(recording.Start(path, TimeSpan.FromMilliseconds(500)).Succeeded);
        Assert.AreEqual(0, recording.Recorder.ElapsedMs, "nothing sampled yet");
        recording.StepKeepingUp(490);
        recording.Step(20);
        Assert.AreEqual(500, recording.Recorder.ElapsedMs, "the time against the cap never passes it");
        Assert.IsTrue(recording.Recorder.Sampling, "the cap is noticed at the next tick, which is due at 560");
        recording.Step(50);
        var ended = await recording.Ended();

        Assert.IsTrue(ended.Succeeded, ended.Failure?.Sentence);
        Assert.AreEqual(new GifClip(path, 8, 500), ended.Value, "samples at 0, 70, … 490, the clip ending at the cap (G15, G32)");
        Assert.IsFalse(recording.Recorder.Sampling);
        Assert.AreEqual(500, recording.Recorder.ElapsedMs, "frozen at the end");
        Assert.HasCount(8, recording.Screen.Grabs, "no grab at or after the cap");
        var frames = MediaFixtures.Frames(path);
        CollectionAssert.AreEqual(new[] { 70, 70, 70, 70, 70, 70, 70, 10 }, frames.Select(f => f.DurationMs).ToList(), "each sample lasts until the next, the last until the cap (F3)");
        CollectionAssert.AreEqual(Enumerable.Range(0, 8).Select(SteppedRecorder.Colour).ToList(), frames.Select(f => Pixel(f, 32, 24)).ToList(), "every sample its own image, in order");
        Assert.AreEqual(new FileInfo(path).Length, recording.Recorder.BytesWritten, "the encoder's bytes, the file's length once done");
        CollectionAssert.AreEqual(new[] { Path.GetFileName(path) }, stash.WorkFiles(), "the clip at the path it was given and nowhere else (R2)");
        Assert.IsEmpty(stash.RootFiles());
    }

    [TestMethod]
    public async Task AStopAfterTheCapsMomentButBeforeItsTickEndsTheClipAtTheCap()
    {
        var recording = new SteppedRecorder();
        recording.Start(cap: TimeSpan.FromMilliseconds(500));

        recording.Step(530);
        recording.Recorder.Stop();
        var ended = await recording.Ended();

        Assert.IsTrue(ended.Succeeded, ended.Failure?.Sentence);
        Assert.AreEqual(500L, recording.Encoder.StoppedAt);
        Assert.AreEqual(500, ended.Value!.DurationMs);
    }

    [TestMethod]
    public async Task StopDrainsWhatWasSampledBeforeItAndClosesTheClipAtItsMoment()
    {
        var recording = new SteppedRecorder();
        recording.Encoder.Hold();
        recording.Start();
        recording.Step(0);
        recording.Encoder.WaitForAppendsBegun(1);
        recording.Step(70);
        recording.Step(70);

        recording.Step(10);
        recording.Recorder.Stop();
        Assert.IsFalse(recording.Recorder.Sampling, "Stop ends the sampling at once");
        recording.Step(400);
        Assert.AreEqual(150, recording.Recorder.ElapsedMs, "frozen at Stop's moment");
        recording.Encoder.LetAllThrough();
        var ended = await recording.Ended();

        Assert.IsTrue(ended.Succeeded, ended.Failure?.Sentence);
        CollectionAssert.AreEqual(new long[] { 0, 70, 140 }, recording.Encoder.Appends.Select(a => a.AtMs).ToList(), "the queue drained, not discarded");
        Assert.AreEqual(150L, recording.Encoder.StoppedAt, "Stop's time is its press, not the drain's end");
        Assert.HasCount(3, recording.Screen.Grabs, "nothing sampled after Stop");
        Assert.IsFalse(recording.Encoder.Cancelled);
    }

    [TestMethod]
    public async Task AStopAfterTwoSamplesClosesTheLastFrameAtStopsMoment()
    {
        using var scratch = new ScratchFolder();
        var path = scratch.File("clip.gif");
        var recording = new SteppedRecorder(GifEncoder.Opener(TestStash.ShortRetry));
        recording.Start(path);

        recording.Step(0);
        recording.Step(70);
        recording.Step(30);
        recording.Recorder.Stop();
        var ended = await recording.Ended();

        Assert.IsTrue(ended.Succeeded, ended.Failure?.Sentence);
        Assert.AreEqual(new GifClip(path, 2, 100), ended.Value);
        CollectionAssert.AreEqual(new[] { 70, 30 }, MediaFixtures.Frames(path).Select(f => f.DurationMs).ToList());
    }

    [TestMethod]
    public async Task AGrabThatStartsAtStopsMomentIsDropped()
    {
        var recording = new SteppedRecorder();
        recording.WhileGrabbing = index =>
        {
            if (index == 2)
            {
                recording.Recorder.Stop();
            }
        };
        recording.Start();

        recording.Step(140);
        var ended = await recording.Ended();

        Assert.IsTrue(ended.Succeeded, ended.Failure?.Sentence);
        Assert.HasCount(3, recording.Screen.Grabs, "the grab at 140 was made");
        CollectionAssert.AreEqual(new long[] { 0, 70 }, recording.Encoder.Appends.Select(a => a.AtMs).ToList(), "and its frame, started at Stop's moment, dropped");
        Assert.AreEqual(140L, recording.Encoder.StoppedAt);
    }

    [TestMethod]
    public async Task AStopBeforeTheFirstTickGrabsNothingAndLeavesNoFile()
    {
        using var scratch = new ScratchFolder();
        var path = scratch.File("clip.gif");
        var recording = new SteppedRecorder(GifEncoder.Opener(TestStash.ShortRetry));
        recording.Start(path);

        recording.Recorder.Stop();
        var ended = await recording.Ended();
        recording.Step(200);

        Assert.IsTrue(ended.Succeeded, ended.Failure?.Sentence);
        Assert.IsNull(ended.Value, "a recording with no frame is cancelled");
        Assert.IsEmpty(recording.Screen.Grabs);
        Assert.IsFalse(File.Exists(path));
    }

    [TestMethod]
    public async Task AStopDuringTheFirstGrabKeepsItsFrameLastingUntilStop()
    {
        using var scratch = new ScratchFolder();
        var path = scratch.File("clip.gif");
        var recording = new SteppedRecorder(GifEncoder.Opener(TestStash.ShortRetry));
        recording.WhileGrabbing = _ =>
        {
            recording.Clock.Pass(TimeSpan.FromMilliseconds(10));
            recording.Recorder.Stop();
        };
        recording.Start(path);

        recording.Step(0);
        var ended = await recording.Ended();

        Assert.IsTrue(ended.Succeeded, ended.Failure?.Sentence);
        Assert.AreEqual(new GifClip(path, 1, 10), ended.Value, "the grab started at 0, before Stop at 10");
        Assert.AreEqual(1, GifImages.Read(path).Images.Single().DelayCs);
    }

    [TestMethod]
    public async Task CancelDiscardsTheQueueAndEndsWithNothing()
    {
        var recording = new SteppedRecorder();
        recording.Encoder.Hold();
        recording.Start();
        recording.Step(0);
        recording.Encoder.WaitForAppendsBegun(1);
        recording.Step(70);
        recording.Step(70);

        recording.Recorder.Cancel();
        Assert.AreEqual(1, recording.Recorder.FramesHeld, "the two queued frames let go at once; the one being appended is the encoder's");
        recording.Recorder.Stop();
        recording.Encoder.LetAllThrough();
        var ended = await recording.Ended();

        Assert.IsTrue(ended.Succeeded, ended.Failure?.Sentence);
        Assert.IsNull(ended.Value);
        CollectionAssert.AreEqual(new long[] { 0 }, recording.Encoder.Appends.Select(a => a.AtMs).ToList(), "the queued frames never reached the encoder");
        Assert.IsTrue(recording.Encoder.Cancelled);
        Assert.IsNull(recording.Encoder.StoppedAt, "a Stop after Cancel revives nothing");
    }

    [TestMethod]
    public async Task CancelLeavesTheClipsFileClosedForTheCaller()
    {
        using var scratch = new ScratchFolder();
        var path = scratch.File("clip.gif");
        var recording = new SteppedRecorder(GifEncoder.Opener(TestStash.ShortRetry));
        recording.Start(path);
        recording.StepKeepingUp(140);

        recording.Recorder.Cancel();
        var ended = await recording.Ended();

        Assert.IsTrue(ended.Succeeded, ended.Failure?.Sentence);
        Assert.IsNull(ended.Value);
        AssertClosed(path);
    }

    [TestMethod]
    public async Task ACancelDuringStopsDrainDoesNotDiscardTheClip()
    {
        var recording = new SteppedRecorder();
        recording.Encoder.Hold();
        recording.Start();
        recording.Step(0);
        recording.Encoder.WaitForAppendsBegun(1);
        recording.Step(70);
        recording.Step(10);

        recording.Recorder.Stop();
        recording.Recorder.Cancel();
        recording.Encoder.LetAllThrough();
        var ended = await recording.Ended();

        Assert.IsTrue(ended.Succeeded, ended.Failure?.Sentence);
        Assert.IsNotNull(ended.Value);
        CollectionAssert.AreEqual(new long[] { 0, 70 }, recording.Encoder.Appends.Select(a => a.AtMs).ToList());
        Assert.AreEqual(80L, recording.Encoder.StoppedAt);
        Assert.IsFalse(recording.Encoder.Cancelled);
    }

    [TestMethod]
    public async Task ARefusedGrabEndsTheRecordingWithThatFailureUnchanged()
    {
        Failure refused = new("Grab the screen", "the display at 0, 0 (64 × 48)", "Access is denied.");
        var recording = new SteppedRecorder { RefusedGrab = (3, refused) };
        recording.Start();

        recording.Step(210);
        Assert.IsFalse(recording.Recorder.Sampling, "the sampling ended with the refused grab");
        recording.Step(700);
        var ended = await recording.Ended();

        Assert.AreEqual(refused, ended.Failure, "named as the screen named it (E5, G35)");
        Assert.HasCount(4, recording.Screen.Grabs, "no grab after the refused one");
        Assert.IsTrue(recording.Encoder.Cancelled, "the encoder cancelled");
        Assert.IsNull(recording.Encoder.StoppedAt);
    }

    [TestMethod]
    public async Task ARefusedGrabLeavesTheClipsFileClosedForTheCaller()
    {
        using var scratch = new ScratchFolder();
        var path = scratch.File("clip.gif");
        var recording = new SteppedRecorder(GifEncoder.Opener(TestStash.ShortRetry)) { RefusedGrab = (2, new Failure("Grab the screen", "the display", "Access is denied.")) };
        recording.Start(path);

        recording.StepKeepingUp(140);
        var ended = await recording.Ended();

        Assert.AreEqual("Grab the screen", ended.Failure?.Action);
        AssertClosed(path);
    }

    [TestMethod]
    public async Task AnEncoderFailureFromAppendEndsTheRecordingNamedAsRecordingTheClip()
    {
        var recording = new SteppedRecorder();
        recording.Encoder.AppendAnswer = index => index == 1 ? new Failure("Write the clip as GIF", SteppedRecorder.Clip, "the disk is full") : Outcome.Done;
        recording.Start();

        recording.Step(70);
        recording.Encoder.WaitForAppends(2);
        recording.Step(700);
        var ended = await recording.Ended();

        Assert.AreEqual(new Failure("Record a clip", SteppedRecorder.Clip, "the disk is full"), ended.Failure);
        Assert.IsFalse(recording.Recorder.Sampling);
        Assert.HasCount(2, recording.Encoder.Appends, "nothing more reached the encoder");
        Assert.IsTrue(recording.Encoder.Cancelled, "the clip closed for the caller");
        Assert.IsNull(recording.Encoder.StoppedAt);
    }

    [TestMethod]
    public async Task AnEncoderFailureFromStopIsNamedAsRecordingTheClip()
    {
        var recording = new SteppedRecorder();
        recording.Encoder.StopAnswer = new Failure("Write the clip as GIF", SteppedRecorder.Clip, "another program is holding it open");
        recording.Start();

        recording.Step(70);
        recording.Recorder.Stop();
        var ended = await recording.Ended();

        Assert.AreEqual(new Failure("Record a clip", SteppedRecorder.Clip, "another program is holding it open"), ended.Failure);
        Assert.IsTrue(recording.Encoder.Cancelled, "a refused Stop leaves the clip open, so it is closed");
    }

    [TestMethod]
    public async Task ARefusedOpenIsNamedAndNothingSamples()
    {
        using var scratch = new ScratchFolder();
        var path = Path.Combine(scratch.Path, "missing", "clip.gif");
        var recording = new SteppedRecorder(GifEncoder.Opener(TestStash.ShortRetry));

        var started = recording.Start(path);
        recording.Step(500);
        var ended = await recording.Ended();

        Assert.IsFalse(started.Succeeded);
        Assert.AreEqual(("Record a clip", SteppedRecorder.Clip), (started.Failure.Action, started.Failure.Subject));
        Assert.AreEqual(started.Failure, ended.Failure, "Ended carries the same failure");
        Assert.IsFalse(recording.Recorder.Sampling);
        Assert.IsEmpty(recording.Screen.Grabs);
        Assert.IsFalse(File.Exists(path));
    }

    [TestMethod]
    public async Task ACancelWhileTheClipOpensEndsWithNothingThoughTheOpenIsThenRefused()
    {
        using var opening = new ManualResetEventSlim();
        using var proceed = new ManualResetEventSlim();
        var recording = new SteppedRecorder((_, _, _, name) =>
        {
            opening.Set();
            proceed.Wait(TimeSpan.FromSeconds(10));
            return new Failure("Write the clip as GIF", name, "its folder does not exist");
        });

        var started = Task.Run(() => recording.Start());
        Assert.IsTrue(opening.Wait(TimeSpan.FromSeconds(10)));
        recording.Recorder.Cancel();
        proceed.Set();
        var outcome = await started;
        var ended = await recording.Ended();

        Assert.AreEqual(new Failure("Record a clip", SteppedRecorder.Clip, "its folder does not exist"), outcome.Failure, "Start answers what the open answered");
        Assert.IsTrue(ended.Succeeded, ended.Failure?.Sentence);
        Assert.IsNull(ended.Value, "Cancel came first, and a cancelled recording names nothing (E3)");
    }

    [TestMethod]
    public async Task ACancelWhileTheClipOpensStartsNoSampling()
    {
        using var opening = new ManualResetEventSlim();
        using var proceed = new ManualResetEventSlim();
        SteppedRecorder? recording = null;
        recording = new SteppedRecorder((path, width, height, name) =>
        {
            opening.Set();
            proceed.Wait(TimeSpan.FromSeconds(10));
            return recording!.Encoder.Opener(path, width, height, name);
        });

        var started = Task.Run(() => recording.Start());
        Assert.IsTrue(opening.Wait(TimeSpan.FromSeconds(10)));
        recording.Recorder.Cancel();
        proceed.Set();
        Assert.IsTrue((await started).Succeeded);
        recording.Step(300);
        var ended = await recording.Ended();

        Assert.IsTrue(ended.Succeeded, ended.Failure?.Sentence);
        Assert.IsNull(ended.Value);
        Assert.IsEmpty(recording.Screen.Grabs, "nothing sampled after the Cancel");
    }

    private static void AssertClosed(string path)
    {
        Assert.IsTrue(File.Exists(path), "the file left for the caller to discard through Store");
        using var alone = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }
}
