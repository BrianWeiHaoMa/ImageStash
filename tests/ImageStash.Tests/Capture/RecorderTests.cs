using ImageStash.Capture;
using ImageStash.Media;
using ImageStash.Tests.Support;

namespace ImageStash.Tests.Capture;

/// <summary>
/// The recording loop's pace and budget over the Screen seam's double, a clock the check steps, and the encoder's double
/// or Media's real GIF encoder: samples every 70 ms anchored to the first and never before they are due (F3, G15), with
/// the pointer when asked; a bounded queue to an encoder thread of its own, a sample due while it is full skipped before
/// any grab so the frame before lasts longer (T3), and a fixed few full frames held however long the clip (R7). How a
/// recording ends is <see cref="RecorderEndingTests"/>'s.
/// </summary>
[TestClass]
// One clock, one encoder thread and the heap read by each check: side by side the GC counts and the real encoder's
// pool-thread quantizing would measure each other.
[DoNotParallelize]
public sealed class RecorderTests
{
    [TestMethod]
    public async Task TheFirstSampleIsTheTimelinesStartWhateverTimePassesBetweenReadingsOfTheClock()
    {
        var recording = new SteppedRecorder();
        recording.Clock.PassesOnEachRead = TimeSpan.FromMilliseconds(3);
        recording.Start();

        recording.Step(140);
        recording.Recorder.Stop();
        await recording.Ended();

        var appended = recording.Encoder.Appends;
        Assert.AreEqual(0, appended[0].AtMs, "the first grab's start is the timeline's 0, so the clip lasts from it until Stop (G15)");
        Assert.AreEqual(recording.Recorder.ElapsedMs, recording.Encoder.StoppedAt, "Stop's time is the time the recorder reads out");
    }

    [TestMethod]
    public async Task TheTicksAreAnchoredToTheFirstSoAGrabsOwnTimeNeverSlowsThePace()
    {
        var recording = new SteppedRecorder();
        recording.WhileGrabbing = _ => recording.Clock.Pass(TimeSpan.FromMilliseconds(20));
        recording.Start();

        recording.Step(150);
        recording.Recorder.Stop();
        await recording.Ended();

        CollectionAssert.AreEqual(new long[] { 0, 70, 140 }, recording.Encoder.Appends.Select(a => a.AtMs).ToList(), "each grab took 20 ms, and still they start at 0, 70 and 140, not 0, 90 and 180");
        Assert.AreEqual(160L, recording.Encoder.StoppedAt);
    }

    [TestMethod]
    public async Task ALateTickRunsAtOnceAndTheNextWaitsForTheNextMultiple()
    {
        var recording = new SteppedRecorder();
        recording.WhileGrabbing = index =>
        {
            if (index == 0)
            {
                recording.Clock.Pass(TimeSpan.FromMilliseconds(100));
            }
        };
        recording.Start();

        recording.Step(150);
        recording.Recorder.Stop();
        await recording.Ended();

        Assert.HasCount(3, recording.Screen.Grabs, "the tick due at 70 runs when the slow grab ends, not skipped, and the next at 140");
        CollectionAssert.AreEqual(new long[] { 0, 70, 140 }, recording.Encoder.Appends.Select(a => a.AtMs).ToList(), "the late tick's frame keeps its place at 70, so no frame lasts 10 cs and the next 4 (F3)");
    }

    [TestMethod]
    public async Task ATimerFiringEarlyIsWaitedOutSoNoTickRunsBeforeItIsDue()
    {
        var recording = new SteppedRecorder();
        recording.Clock.FiresEarlyBy = TimeSpan.FromMilliseconds(15);
        recording.Start();

        recording.Step(150);
        recording.Recorder.Stop();
        await recording.Ended();

        CollectionAssert.AreEqual(new long[] { 0, 70, 140 }, recording.Encoder.Appends.Select(a => a.AtMs).ToList(), "woken at 55 and 125, the ticks still run at 70 and 140, and no extra sample comes between");
    }

    [TestMethod]
    public async Task ATimerWakingOnItsClocksTickLeavesEveryFrameAtTheFixedRate()
    {
        var recording = new SteppedRecorder();
        // Windows' system timers wake on its clock tick, 15.625 ms by default: a tick due at 70 runs at 78.125.
        recording.Clock.FiresOnTicksOf = TimeSpan.FromTicks(156_250);
        recording.Start();

        recording.StepKeepingUp(350);
        recording.Step(10);
        recording.Recorder.Stop();
        await recording.Ended();

        CollectionAssert.AreEqual(new long[] { 0, 70, 140, 210, 280, 350 }, recording.Encoder.Appends.Select(a => a.AtMs).ToList(), "woken at 78, 141, 219, 281 and 359, each frame keeps its place on the 70 ms timeline, so every one lasts 7 cs, not 8 and 6 by turns (F3)");
        Assert.AreEqual(360L, recording.Encoder.StoppedAt, "the last frame closed at Stop's own moment");
    }

    [TestMethod]
    public async Task ATickWokenPastLaterPlacesSkipsThemAndItsFrameTakesTheLatestPlaceReached()
    {
        var recording = new SteppedRecorder();
        // The sampler held up until 300 ms: the samples due at 70 to 280 are missed.
        recording.Clock.FiresOnTicksOf = TimeSpan.FromMilliseconds(300);
        recording.Start();

        recording.Step(0);
        recording.Step(300);
        recording.Step(40);
        recording.Recorder.Stop();
        await recording.Ended();

        Assert.HasCount(2, recording.Screen.Grabs, "nothing grabbed for the missed samples");
        CollectionAssert.AreEqual(new long[] { 0, 280 }, recording.Encoder.Appends.Select(a => a.AtMs).ToList(), "the frame before lasts until 280 and the one grabbed at 300 shows from there, not from 70 (G15, T3)");
        Assert.AreEqual(340L, recording.Encoder.StoppedAt);
    }

    [TestMethod]
    public async Task ASampleDueWhileTheQueueIsFullIsSkippedBeforeAnyGrabAndTheFrameBeforeLastsLonger()
    {
        var recording = new SteppedRecorder();
        recording.Encoder.Hold();
        recording.Start();

        recording.Step(0);
        recording.Encoder.WaitForAppendsBegun(1);
        recording.Step(70);
        recording.Step(70);
        Assert.AreEqual(Recorder.QueueBound + 1, recording.Recorder.FramesHeld, "0 being appended, 70 and 140 queued");
        recording.Step(70);
        recording.Step(70);

        Assert.HasCount(3, recording.Screen.Grabs, "210 and 280 skipped without a grab");
        Assert.AreEqual(Recorder.QueueBound + 1, recording.Recorder.FramesHeld, "a skipped sample holds nothing");
        recording.Encoder.LetAllThrough();
        recording.Encoder.WaitForAppends(3);
        recording.Step(70);
        recording.Encoder.WaitForAppends(4);
        recording.Step(10);
        recording.Recorder.Stop();
        var ended = await recording.Ended();

        Assert.IsTrue(ended.Succeeded, ended.Failure?.Sentence);
        CollectionAssert.AreEqual(new long[] { 0, 70, 140, 350 }, recording.Encoder.Appends.Select(a => a.AtMs).ToList(), "the next kept sample's time follows 140, so that frame lasts 210 ms (T3)");
        CollectionAssert.AreEqual(new[] { 0, 1, 2, 3 }, recording.Encoder.Appends.Select(a => a.Mark).ToList(), "each frame with its own grab's time");
        Assert.AreEqual(360L, recording.Encoder.StoppedAt);
    }

    [TestMethod]
    [DataRow(50)]
    [DataRow(500)]
    public async Task AFixedFewFullFramesAreHeldHoweverLongTheClip(int samples)
    {
        var recording = new SteppedRecorder();
        var held = new List<int>();
        recording.WhileGrabbing = _ => Note(held, recording.Recorder.FramesHeld);
        recording.Encoder.WhileAppending = () => Note(held, recording.Recorder.FramesHeld);
        recording.Encoder.Hold();
        var alive = new List<int>();
        recording.Start();

        for (var i = 0; i < samples; i++)
        {
            recording.Step(i == 0 ? 0 : 70);
            if (i == 0)
            {
                recording.Encoder.WaitForAppendsBegun(1);
            }

            // The encoder lets one frame through every third sample, so the queue fills and drains over and over.
            if (i % 3 == 2)
            {
                recording.Encoder.LetThrough();
            }

            if (i % 25 == 24)
            {
                alive.Add(Alive(recording.Screen));
            }
        }

        recording.Encoder.LetAllThrough();
        recording.Recorder.Stop();
        var ended = await recording.Ended();

        Assert.IsTrue(ended.Succeeded, ended.Failure?.Sentence);
        var most = Note(held);
        Assert.IsLessThanOrEqualTo(Recorder.QueueBound + 2, most, "frames held, read in every grab and every append (R7)");
        Assert.AreEqual(Recorder.QueueBound + 1, most, "a grab with the queue one short of full and a frame being appended, so the bound was reached");
        Assert.IsLessThanOrEqualTo(Recorder.QueueBound + 2, alive.Max(), $"frames alive after a collection: {string.Join(", ", alive)}");
        Assert.IsLessThan(samples, recording.Screen.Grabs.Count, "samples were skipped");
        var appended = recording.Encoder.Appends;
        CollectionAssert.AreEqual(Enumerable.Range(0, recording.Screen.Grabs.Count).ToList(), appended.Select(a => a.Mark).ToList(), "every grabbed frame appended, in order, Stop draining the queue");
        Assert.IsTrue(appended.Zip(appended.Skip(1)).All(pair => pair.First.AtMs < pair.Second.AtMs), "strictly increasing times");
        Assert.AreEqual(0, recording.Recorder.FramesHeld);
        Assert.AreEqual(0, Alive(recording.Screen), "nothing held once ended");
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task EveryGrabIsAskedForThePointerExactlyWhenTheRecordingIs(bool withPointer)
    {
        var recording = new SteppedRecorder();
        recording.Start(withPointer: withPointer);

        // Kept up with tick by tick: a queue the encoder has not drained skips a sample before its grab (G15).
        recording.StepKeepingUp(280);
        recording.Recorder.Stop();
        await recording.Ended();

        Assert.HasCount(5, recording.Screen.PointerAsked);
        Assert.IsTrue(recording.Screen.PointerAsked.All(asked => asked == withPointer), "the pointer recorded unless Settings says otherwise (G15)");
    }

    [TestMethod]
    public async Task FramesAreGrabbedOnTheSamplersThreadAndAppendedInOrderOnAnotherNeverTheCallers()
    {
        var grabThreads = new List<int>();
        var recording = new SteppedRecorder { WhileGrabbing = _ => grabThreads.Add(Environment.CurrentManagedThreadId) };
        recording.Start();

        // Kept up with tick by tick: a queue the encoder has not drained skips a sample before its grab (G15).
        recording.StepKeepingUp(700);
        recording.Recorder.Stop();
        await recording.Ended();

        var appended = recording.Encoder.Appends;
        Assert.HasCount(11, appended);
        var appendThread = appended.Select(a => a.Thread).Distinct().Single();
        var grabThread = grabThreads.Distinct().Single();
        Assert.AreNotEqual(grabThread, appendThread, "the sampler and the encoder each on a thread of its own");
        Assert.IsFalse(new[] { grabThread, appendThread }.Contains(Environment.CurrentManagedThreadId), "neither the caller's thread");
        CollectionAssert.AreEqual(Enumerable.Range(0, 11).Select(i => i * 70L).ToList(), appended.Select(a => a.AtMs).ToList());
        Assert.AreEqual((SteppedRecorder.Display.Bounds.Width, SteppedRecorder.Display.Bounds.Height, SteppedRecorder.Clip), (recording.Encoder.Opened!.Value.Width, recording.Encoder.Opened.Value.Height, recording.Encoder.Opened.Value.Name), "opened with the region's size and the clip's name");
    }

    [TestMethod]
    public async Task OnTheSystemsClockTheClipLastsTheRecordedTime()
    {
        using var scratch = new ScratchFolder();
        var path = scratch.File("clip.gif");
        var recorder = new Recorder(new RecordingScreen { Shown = [SteppedRecorder.Display] }, GifEncoder.Opener(TestStash.ShortRetry), TimeProvider.System);
        var clock = System.Diagnostics.Stopwatch.StartNew();

        Assert.IsTrue(recorder.Start(SteppedRecorder.Display.Bounds, withPointer: true, TimeSpan.FromMilliseconds(700), path, SteppedRecorder.Clip).Succeeded);
        var ended = await recorder.Ended.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.IsTrue(ended.Succeeded, ended.Failure?.Sentence);
        Assert.AreEqual(700, ended.Value!.DurationMs, "the cap ends the clip at the cap");
        Assert.IsGreaterThanOrEqualTo(700, clock.ElapsedMilliseconds);
        Assert.IsTrue(ended.Value.FrameCount is >= 5 and <= 10, $"about one sample per 70 ms: {ended.Value.FrameCount}");
        Assert.AreEqual(700, MediaFixtures.Frames(path).Sum(f => f.DurationMs));
    }

    /// <summary>Adds <paramref name="reading"/> to <paramref name="readings"/>, from any thread; with none, the largest so far.</summary>
    private static int Note(List<int> readings, int? reading = null)
    {
        lock (readings)
        {
            if (reading is { } read)
            {
                readings.Add(read);
            }

            return readings.Max();
        }
    }

    private static int Alive(RecordingScreen screen)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        lock (screen.Grabs)
        {
            return screen.HandedPixels.Count(pixels => pixels.TryGetTarget(out _));
        }
    }
}
