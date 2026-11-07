using System.Runtime.Versioning;
using Avalonia;
using ImageStash.Capture;
using ImageStash.Media;
using ImageStash.Seams;
using ImageStash.Store;
using ImageStash.Tests.Support;

namespace ImageStash.Tests.Capture;

/// <summary>
/// A recording's span in the capture session (G15, G3, F4, F12, G32, G35, E3, E5, G28, R2), over the Screen seam's
/// double, a preset selection, the frame and bar's stand-in, Media's GIF encoder and a clock the check steps: the record
/// overlay first, refusing a second capture while it is up; from the release the recording in progress, its region
/// waiting framed with nothing written, a screenshot allowed beside it and a second recording refused; Record starting
/// it, the clip named and its cap counted from Record; Stop naming the saving at once and the frame closed only after
/// the commit; the cap naming the saving too; Cancel while waiting or recording, a refused grab, a refused open and a
/// frame refusing either look adding nothing and leaving no clip, a refused discard named; Record, Stop and Cancel doing
/// nothing once it has ended.
/// </summary>
[TestClass]
public sealed class RecordingSessionTests
{
    private static readonly PixelRect Region = new(100, 50, 32, 24);

    private static readonly RecordingOptions ThirtySeconds = new(TimeSpan.FromSeconds(30), Pointer: true);

    [TestMethod]
    public async Task FromTheReleaseTheRegionWaitsFramedWithNothingWrittenAndRecordStartsTheClipNamedAtRecord()
    {
        using var recording = await Recording.Open();
        var releasing = new TaskCompletionSource();
        recording.Selection.Gate = releasing.Task;
        var finished = new List<(RecordedClip Clip, bool InProgress, bool Sampling, IReadOnlyList<string> Calls, bool GrabsLetGo)>();

        var outcome = recording.Session.Record(recording.Store, ThirtySeconds, async clip =>
            finished.Add((clip, recording.Session.RecordingInProgress, recording.Session.RecordingSampling, recording.Frame.Calls, await recording.DisplayGrabsLetGo())));
        await recording.WhileSelecting();

        Assert.IsTrue(recording.Session.InProgress && recording.Session.Selecting && !recording.Session.RecordingInProgress, "the overlay is the capture in progress");
        Assert.AreEqual(new Failure("Record a clip", "the screen", "a capture is already in progress"), (await recording.Session.Record(recording.Store, ThirtySeconds, _ => Task.CompletedTask)).Failure);
        Assert.AreEqual(new Failure("Take a screenshot", "the screen", "a capture is already in progress"), (await recording.Session.TakeScreenshot(recording.Store, _ => Task.CompletedTask)).Failure, "no second overlay over the first");
        // The release three seconds after the press.
        recording.Clock.Pass(TimeSpan.FromSeconds(3));
        releasing.SetResult();
        await recording.WhileWaiting();

        Assert.IsFalse(recording.Session.InProgress || recording.Session.Selecting, "the overlay gone, so a screenshot may start");
        Assert.IsTrue(recording.Session.RecordingInProgress && recording.Session.AnyInProgress, "the wait is the recording in progress (G3)");
        Assert.IsTrue(recording.Session.RecordingWaiting && !recording.Session.RecordingSampling, "waiting, not recording (G15)");
        Assert.AreEqual(RecordingStage.Waiting, recording.Session.Stage);
        CollectionAssert.AreEqual(new[] { CaptureMode.Recording }, recording.Selection.Modes, "the overlay told the release selects a recording's region");
        var (region, cap, _, _, _) = recording.Frame.Shown!.Value;
        Assert.AreEqual(new SelectedRegion(RecordingScreen.Primary, Region), region, "the frame and the bar around the released region");
        Assert.AreEqual(TimeSpan.FromSeconds(30), cap);
        CollectionAssert.AreEqual(new[] { "show" }, recording.Frame.Calls.ToList(), "the wait's look, Record and Cancel; nothing recording yet");
        Assert.IsNull(recording.Frame.Readings, "no readings before Record");
        Assert.IsEmpty(recording.Stash.WorkFiles(), "nothing written while the region waits, so a kill now leaves nothing (G28, R2)");
        CollectionAssert.AreEqual(new[] { false }, recording.Screen.PointerAsked, "nothing sampled while the region waits: the overlay's grab alone");
        Assert.AreEqual(new Failure("Record a clip", "the screen", "a capture is already in progress"), (await recording.Session.Record(recording.Store, ThirtySeconds, _ => Task.CompletedTask)).Failure, "one recording at a time");
        recording.Selection.Answer = null;
        var screenshot = await recording.Session.TakeScreenshot(recording.Store, _ => Task.CompletedTask);
        Assert.IsTrue(screenshot.Succeeded && screenshot.Value is null, $"a screenshot may start while the region waits, here cancelled: {screenshot.Failure?.Sentence}");
        recording.Session.StopRecording();
        Assert.IsTrue(recording.Session.RecordingWaiting, "Stop does nothing to a region waiting");
        // Record pressed a minute after the release.
        recording.Clock.Pass(TimeSpan.FromMinutes(1));
        recording.Frame.PressRecord();
        await recording.WhileSampling();

        CollectionAssert.AreEqual(new[] { "show", "record" }, recording.Frame.Calls.ToList(), "the same frame and bar take the recording's look (F4)");
        Assert.IsTrue(!recording.Session.RecordingWaiting && recording.Session.RecordingSampling && recording.Session.Stage == RecordingStage.Sampling);
        recording.Session.StartRecording();
        recording.FirstFrame();
        recording.Clock.Pass(TimeSpan.FromSeconds(7));
        recording.Frame.PressStop();
        var saved = await outcome.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.IsTrue(saved.Succeeded, saved.Failure?.Sentence);
        var (clip, inProgress, sampling, calls, letGo) = finished.Single();
        Assert.AreEqual(clip, saved.Value, "the clip the commit was given");
        Assert.AreEqual("Recording 2026-10-20 17.31.03", clip.Name, "Record's moment in local time, not the press's, the release's nor Stop's (F12)");
        Assert.IsTrue(recording.Stash.Folder.IsWorkFile(clip.WorkPath) && clip.WorkPath.EndsWith(".gif", StringComparison.Ordinal), clip.WorkPath);
        Assert.AreEqual(new GifClip(clip.WorkPath, 1, 7000), clip.Clip, "one frame lasting from Record until Stop, the wait not counted in");
        Assert.IsTrue(File.Exists(clip.WorkPath), "written whole before the commit");
        Assert.IsTrue(inProgress && !sampling, "the commit runs while the recording is in progress, its sampling over");
        CollectionAssert.AreEqual(new[] { "show", "record", "saving" }, calls.ToList(), "the bar names the saving from Stop until the commit has run (G35)");
        Assert.IsTrue(letGo, "no display's frozen pixels kept past the selection");
        CollectionAssert.AreEqual(new[] { "show", "record", "saving", "close" }, recording.Frame.Calls.ToList(), "the frame and the bar gone once the item is placed (F4)");
        Assert.IsFalse(recording.Session.RecordingInProgress || recording.Session.AnyInProgress);
        CollectionAssert.AreEqual(new[] { RecordingStage.Waiting, RecordingStage.Sampling, RecordingStage.None }, recording.Changes, "said as the wait starts, as Record starts the sampling and as Stop ends it");
        CollectionAssert.AreEqual(new[] { false, false, true }, recording.Screen.PointerAsked, "the overlays' grabs of the display without the pointer, the region's one sample with it");
        Assert.IsEmpty(recording.Named);
    }

    [TestMethod]
    public async Task TheCapCountedFromRecordEndsTheSamplingAndTheBarNamesTheSavingThen()
    {
        using var recording = await Recording.Open();

        var outcome = recording.Session.Record(recording.Store, new RecordingOptions(TimeSpan.FromMilliseconds(280), Pointer: false), _ => Task.CompletedTask);
        await recording.WhileWaiting();
        // The region waits five seconds, longer than the cap, before Record.
        recording.Clock.Pass(TimeSpan.FromSeconds(5));
        Assert.IsTrue(recording.Session.RecordingWaiting, "the cap does not run while the region waits");
        recording.Frame.PressRecord();
        await recording.WhileSampling();
        recording.StepKeepingUp(280);
        var saved = await outcome.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.IsTrue(saved.Succeeded, saved.Failure?.Sentence);
        Assert.AreEqual(280, saved.Value!.Clip.DurationMs, "the clip ends at the cap itself, counted from Record (G32, G15)");
        Assert.AreEqual("Recording 2026-10-20 17.30.05", saved.Value.Name);
        CollectionAssert.AreEqual(new[] { "show", "record", "saving", "close" }, recording.Frame.Calls.ToList(), "no Stop or Cancel offered once the cap ended the sampling");
        CollectionAssert.AreEqual(new[] { RecordingStage.Waiting, RecordingStage.Sampling, RecordingStage.None }, recording.Changes);
        CollectionAssert.AreEqual(new[] { false, false, false, false, false }, recording.Screen.PointerAsked, "no pointer asked for while Settings says so (G15)");
    }

    [TestMethod]
    public async Task ACancelTheBarStillOfferedWhenTheCapEndedTheSamplingDiscardsTheClip()
    {
        using var recording = await Recording.Open();
        // Cancel pressed as the cap's end reaches the session: the recorder has finished the clip, the bar still offers Cancel.
        recording.Frame.WhileCalled = call =>
        {
            if (call == "saving")
            {
                recording.Frame.PressCancel();
            }
        };

        var outcome = recording.Session.Record(recording.Store, new RecordingOptions(TimeSpan.FromMilliseconds(140), Pointer: true), _ => throw new AssertFailedException("a cancelled clip is not committed"));
        await recording.Started();
        recording.StepKeepingUp(140);
        var cancelled = await outcome.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.IsTrue(cancelled.Succeeded && cancelled.Value is null, $"Cancel discards it (G15, E3): {cancelled.Failure?.Sentence}");
        Assert.IsEmpty(recording.Stash.WorkFiles(), "the finished clip discarded through the store");
        CollectionAssert.AreEqual(new[] { RecordingStage.Waiting, RecordingStage.Sampling, RecordingStage.None }, recording.Changes);
        Assert.IsEmpty(recording.Named);
    }

    [TestMethod]
    public async Task AFrameThePlatformRefusesOrCannotMakeForTheWaitEndsTheRecordingNamedOnTheScreen()
    {
        using var refused = await Recording.Open();
        refused.Frame.ShowAnswer = new Failure("Record a clip", "ImageStash recording", "Windows would not show the frame around the region");

        var unmarked = await refused.Session.Record(refused.Store, ThirtySeconds, _ => throw new AssertFailedException("nothing to commit"));

        Assert.AreEqual(new Failure("Record a clip", "the screen", "Windows would not show the frame around the region"), unmarked.Failure, "no auto name before Record: the screen is the subject (G35)");
        CollectionAssert.AreEqual(new[] { "show", "close" }, refused.Frame.Calls.ToList());
        Assert.IsEmpty(refused.Changes, "it never waited for Record nor sampled");
        Assert.IsEmpty(refused.Stash.WorkFiles(), "nothing written");
        Assert.IsFalse(refused.Session.AnyInProgress);

        using var throwing = await Recording.Open();
        throwing.Frame.WhileCalled = call =>
        {
            if (call == "show")
            {
                throw new InvalidOperationException("the window could not be made");
            }
        };

        var failed = await throwing.Session.Record(throwing.Store, ThirtySeconds, _ => throw new AssertFailedException("nothing to commit"));

        Assert.AreEqual(new Failure("Record a clip", "the screen", "the window could not be made"), failed.Failure, "named, never thrown (G35)");
        CollectionAssert.AreEqual(new[] { "close" }, throwing.Frame.Calls.ToList(), "whatever was shown closed");
        Assert.IsEmpty(throwing.Changes);
        Assert.IsEmpty(throwing.Stash.WorkFiles());
        Assert.IsFalse(throwing.Session.AnyInProgress, "the next recording can start");
        Assert.IsEmpty(throwing.Named);
    }

    [TestMethod]
    public async Task AFrameThatRefusesItsRecordingLookAtRecordEndsTheRecordingAtItsStartNamedOnTheClip()
    {
        using var refused = await Recording.Open();
        refused.Frame.RecordingAnswer = new Failure("Record a clip", "ImageStash recording", "Windows would not show the frame around the region");

        var outcome = refused.Session.Record(refused.Store, ThirtySeconds, _ => throw new AssertFailedException("nothing to commit"));
        await refused.WhileWaiting();
        refused.Frame.PressRecord();
        var unmarked = await outcome.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.AreEqual(new Failure("Record a clip", "Recording 2026-10-20 17.30.00", "Windows would not show the frame around the region"), unmarked.Failure, "nothing recorded unmarked (G15), the failure named on the clip (G35)");
        CollectionAssert.AreEqual(new[] { "show", "record", "close" }, refused.Frame.Calls.ToList(), "the frame and the bar gone (F4)");
        CollectionAssert.AreEqual(new[] { RecordingStage.Waiting, RecordingStage.None }, refused.Changes, "it never sampled, and the tray is told the wait is over");
        Assert.IsEmpty(refused.Stash.WorkFiles(), "its clip discarded");
        Assert.IsFalse(refused.Session.AnyInProgress);

        using var throwing = await Recording.Open();
        throwing.Frame.WhileCalled = call =>
        {
            if (call == "record")
            {
                throw new InvalidOperationException("the window could not be made");
            }
        };

        var started = throwing.Session.Record(throwing.Store, ThirtySeconds, _ => throw new AssertFailedException("nothing to commit"));
        await throwing.WhileWaiting();
        throwing.Frame.PressRecord();
        var failed = await started.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.AreEqual(new Failure("Record a clip", "Recording 2026-10-20 17.30.00", "the window could not be made"), failed.Failure, "named, never thrown (G35)");
        CollectionAssert.AreEqual(new[] { "show", "close" }, throwing.Frame.Calls.ToList(), "whatever was shown closed");
        Assert.IsEmpty(throwing.Stash.WorkFiles());
        Assert.IsFalse(throwing.Session.AnyInProgress, "the next recording can start");
        Assert.IsEmpty(throwing.Named);
    }

    [TestMethod]
    public async Task CancelWhileTheRegionWaitsEndsItWithNothingWrittenAndSaysNothing()
    {
        using var recording = await Recording.Open();

        var outcome = recording.Session.Record(recording.Store, ThirtySeconds, _ => throw new AssertFailedException("nothing to commit"));
        await recording.WhileWaiting();
        recording.Frame.PressCancel();
        var cancelled = await outcome.WaitAsync(TimeSpan.FromSeconds(10));
        recording.Frame.PressRecord();
        recording.Session.StartRecording();
        recording.Frame.PressCancel();

        Assert.IsTrue(cancelled.Succeeded && cancelled.Value is null, $"Cancel discards the wait (G15, E3): {cancelled.Failure?.Sentence}");
        CollectionAssert.AreEqual(new[] { "show", "close" }, recording.Frame.Calls.ToList(), "the frame and the bar gone, nothing recorded or saved (F4)");
        CollectionAssert.AreEqual(new[] { RecordingStage.Waiting, RecordingStage.None }, recording.Changes, "the tray told the wait is over");
        Assert.IsEmpty(recording.Stash.WorkFiles(), "nothing written");
        CollectionAssert.AreEqual(new[] { false }, recording.Screen.PointerAsked, "nothing sampled");
        Assert.IsFalse(recording.Session.AnyInProgress, "Record and Cancel do nothing once it has ended");
        Assert.IsEmpty(recording.Named, "a cancel says nothing (E3)");
    }

    [TestMethod]
    public async Task ARecordingIsRefusedWhileAScreenshotIsBeingSaved()
    {
        using var recording = await Recording.Open();
        Outcome<RecordedClip?>? refused = null;

        var screenshot = await recording.Session.TakeScreenshot(recording.Store, async _ =>
            refused = await recording.Session.Record(recording.Store, ThirtySeconds, _ => throw new AssertFailedException("nothing to commit")));

        Assert.IsTrue(screenshot.Succeeded, screenshot.Failure?.Sentence);
        Assert.AreEqual(new Failure("Record a clip", "the screen", "a capture is already in progress"), refused?.Failure, "one capture at a time (Approach)");
        Assert.HasCount(1, recording.Selection.Handed, "no second overlay");
        Assert.IsEmpty(recording.Frame.Calls);
    }

    [TestMethod]
    public async Task ACancelledSelectionShowsNothingWritesNothingAndSaysNothing()
    {
        using var recording = await Recording.Open();
        recording.Selection.Answer = null;

        var outcome = await recording.Session.Record(recording.Store, ThirtySeconds, _ => throw new AssertFailedException("nothing to commit"));

        Assert.IsTrue(outcome.Succeeded && outcome.Value is null, outcome.Failure?.Sentence);
        Assert.IsEmpty(recording.Frame.Calls, "the frame never shown");
        Assert.IsEmpty(recording.Changes, "the recording never sampled");
        Assert.IsEmpty(recording.Stash.WorkFiles());
        Assert.IsFalse(recording.Session.AnyInProgress);
        Assert.IsEmpty(recording.Named);
    }

    [TestMethod]
    public async Task CancelWhileSamplingDiscardsTheClipAndSaysNothing()
    {
        using var recording = await Recording.Open();

        var outcome = recording.Session.Record(recording.Store, ThirtySeconds, _ => throw new AssertFailedException("nothing to commit"));
        await recording.Started();
        recording.FirstFrame();
        Assert.HasCount(1, recording.Stash.WorkFiles(), "the clip being written");
        recording.Frame.PressCancel();
        var cancelled = await outcome.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.IsTrue(cancelled.Succeeded && cancelled.Value is null, cancelled.Failure?.Sentence);
        CollectionAssert.AreEqual(new[] { "show", "record", "close" }, recording.Frame.Calls.ToList(), "no saving named for a discarded clip");
        Assert.IsEmpty(recording.Stash.WorkFiles(), "the clip discarded through the store (R2)");
        CollectionAssert.AreEqual(new[] { RecordingStage.Waiting, RecordingStage.Sampling, RecordingStage.None }, recording.Changes);
        Assert.IsEmpty(recording.Named, "a cancel says nothing (E3)");
    }

    [TestMethod]
    [SupportedOSPlatform("windows")]
    public async Task AClipHeldPastTheRetriesWhenCancelledIsNamedAndLeftForTheNextStart()
    {
        using var recording = await Recording.Open();

        var outcome = recording.Session.Record(recording.Store, ThirtySeconds, _ => throw new AssertFailedException("nothing to commit"));
        await recording.Started();
        recording.FirstFrame();
        var clip = Path.Combine(recording.Stash.Folder.Work, recording.Stash.WorkFiles().Single());
        var held = HeldFiles.HeldFor(clip, TimeSpan.FromSeconds(2));
        recording.Session.CancelRecording();
        var cancelled = await outcome.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.IsTrue(cancelled.Succeeded && cancelled.Value is null, "the cancel stands");
        Assert.IsTrue(File.Exists(clip), "left for the next start's clearing of .work");
        Assert.AreEqual(new Failure("Discard a copy", Path.GetFileName(clip), "another program is holding it open"), recording.Named.Single(), "the discard's failure named (G35)");
        CollectionAssert.AreEqual(new[] { "show", "record", "close" }, recording.Frame.Calls.ToList());
        await held;
    }

    [TestMethod]
    public async Task AGrabRefusedMidClipEndsItWithThatFailureAndLeavesNoClip()
    {
        using var recording = await Recording.Open();
        var refused = new Failure("Grab the screen", "the display at 0, 0 (1280 × 800)", "access is denied");
        var picture = recording.Screen.Paint(RecordingScreen.Primary, seed: 3);
        // The displays' grab, then three samples, then the refusal.
        recording.Screen.EachGrab = index => index <= 3 ? Outcome<MediaFrame>.Of(picture) : refused;

        var outcome = recording.Session.Record(recording.Store, ThirtySeconds, _ => throw new AssertFailedException("nothing to commit"));
        await recording.Started();
        recording.StepKeepingUp(280);
        var failed = await outcome.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.AreEqual(refused, failed.Failure, "the screen's failure, unchanged (E5, G35)");
        CollectionAssert.AreEqual(new[] { "show", "record", "close" }, recording.Frame.Calls.ToList(), "the frame and the bar gone, no saving named");
        Assert.IsEmpty(recording.Stash.WorkFiles(), "no clip left");
        CollectionAssert.AreEqual(new[] { RecordingStage.Waiting, RecordingStage.Sampling, RecordingStage.None }, recording.Changes);
        Assert.IsFalse(recording.Session.AnyInProgress);
    }

    [TestMethod]
    public async Task ARefusedOpenAtRecordShowsNoRecordingAndIsTheFailure()
    {
        using var refusing = await Recording.Open((_, _, _, name) => new Failure("Record a clip", name, "the disk is full"));

        var outcome = refusing.Session.Record(refusing.Store, ThirtySeconds, _ => throw new AssertFailedException("nothing to commit"));
        await refusing.WhileWaiting();
        refusing.Clock.Pass(TimeSpan.FromSeconds(2));
        refusing.Frame.PressRecord();
        var refused = await outcome.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.AreEqual(new Failure("Record a clip", "Recording 2026-10-20 17.30.02", "the disk is full"), refused.Failure, "named on the clip Record began (G35, F12)");
        CollectionAssert.AreEqual(new[] { "show", "close" }, refusing.Frame.Calls.ToList(), "no recording look, the frame and the bar gone (F4)");
        CollectionAssert.AreEqual(new[] { RecordingStage.Waiting, RecordingStage.None }, refusing.Changes);
        Assert.IsFalse(refusing.Session.AnyInProgress);

        using var missing = await Recording.Open();
        // The work folder gone under the store between the press and Record.
        missing.Selection.AtTheRelease = () => Directory.Delete(missing.Stash.Folder.Work, recursive: true);

        var started = missing.Session.Record(missing.Store, ThirtySeconds, _ => throw new AssertFailedException("nothing to commit"));
        await missing.WhileWaiting();
        missing.Frame.PressRecord();
        var failed = await started.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.AreEqual(("Record a clip", "Recording 2026-10-20 17.30.00"), (failed.Failure?.Action, failed.Failure?.Subject), failed.Failure?.Sentence);
        CollectionAssert.AreEqual(new[] { "show", "close" }, missing.Frame.Calls.ToList(), "no recording look");
        Assert.IsFalse(Directory.Exists(missing.Stash.Folder.Work), "nothing written in its place");
        Assert.IsEmpty(missing.Named);
    }

    [TestMethod]
    public async Task AStopBeforeAnyFrameSavesNothingAndStopAndCancelDoNothingOnceItEnded()
    {
        using var recording = await Recording.Open();

        var outcome = recording.Session.Record(recording.Store, ThirtySeconds, _ => throw new AssertFailedException("nothing to commit"));
        await recording.Started();
        recording.Session.StopRecording();
        var stopped = await outcome.WaitAsync(TimeSpan.FromSeconds(10));
        recording.Session.StopRecording();
        recording.Session.CancelRecording();
        recording.Session.StartRecording();
        recording.Frame.PressStop();
        recording.Frame.PressCancel();
        recording.Frame.PressRecord();

        Assert.IsTrue(stopped.Succeeded && stopped.Value is null, $"a recording with no frame is cancelled: {stopped.Failure?.Sentence}");
        CollectionAssert.AreEqual(new[] { "show", "record", "saving", "close" }, recording.Frame.Calls.ToList(), "nothing after the end");
        CollectionAssert.AreEqual(new[] { RecordingStage.Waiting, RecordingStage.Sampling, RecordingStage.None }, recording.Changes);
        Assert.IsEmpty(recording.Stash.WorkFiles());
        Assert.IsEmpty(recording.Named);
    }

    /// <summary>
    /// A session over a stash of its own, the Screen seam's double showing the primary display, a selection answering
    /// <see cref="Region"/>, the frame's stand-in, Media's GIF encoder unless a check gives another opener, and a stepped
    /// clock five and a half hours ahead of UTC; every change of the recording's stage and every failure named kept.
    /// </summary>
    private sealed class Recording : IDisposable
    {
        private readonly List<RecordingStage> changes = [];

        private Recording(TestStash stash, StashStore store, ClipOpener? opener)
        {
            Stash = stash;
            Store = store;
            Screen.Paint(RecordingScreen.Primary, seed: 1);
            Selection.Answer = new SelectedRegion(RecordingScreen.Primary, Region);
            Session = new CaptureSession(Screen, Selection, Frame, opener ?? GifEncoder.Opener(TestStash.ShortRetry), Clock, Named.Add);
            Session.RecordingChanged += () =>
            {
                lock (changes)
                {
                    changes.Add(Session.Stage);
                }
            };
        }

        public TestStash Stash { get; }

        public StashStore Store { get; }

        public RecordingScreen Screen { get; } = new();

        public PresetRegionSelection Selection { get; } = new();

        public PresetRecordingFrame Frame { get; } = new();

        public SteppedClock Clock { get; } = new() { Zone = SetClock.Ahead };

        public List<Failure> Named { get; } = [];

        public CaptureSession Session { get; }

        /// <summary>The stage as each change said it, in order.</summary>
        public List<RecordingStage> Changes
        {
            get
            {
                lock (changes)
                {
                    return changes.ToList();
                }
            }
        }

        /// <summary>The recorder the session made, which the frame was handed as its readings.</summary>
        private Recorder Recorder => (Recorder)Frame.Readings!;

        public static async Task<Recording> Open(ClipOpener? opener = null)
        {
            var stash = new TestStash();
            return new Recording(stash, await stash.OpenCleanly(), opener);
        }

        /// <summary>Until the overlay has been handed its grabs.</summary>
        public Task WhileSelecting() => Until(() => Selection.Handed.Count > 0, "the overlay was never shown");

        /// <summary>Until the frame shows the released region waiting to be recorded.</summary>
        public Task WhileWaiting() => Until(() => Frame.Calls.Contains("show") && Session.RecordingWaiting, "the region never waited");

        /// <summary>Until the frame shows its recording look and the recording samples.</summary>
        public Task WhileSampling() => Until(() => Frame.Calls.Contains("record") && Session.RecordingSampling, "the recording never sampled");

        /// <summary>Until the region waits, then Record pressed on the bar, then until the recording samples.</summary>
        public async Task Started()
        {
            await WhileWaiting();
            Frame.PressRecord();
            await WhileSampling();
        }

        /// <summary>The first tick, then until its frame is written into the clip.</summary>
        public void FirstFrame() => StepKeepingUp(0);

        /// <summary>Each tick up to <paramref name="untilMs"/>, the first at once, waiting after each until the recorder holds no frame.</summary>
        public void StepKeepingUp(long untilMs) => SteppedRecorder.StepKeepingUp(Clock, Recorder, untilMs);

        /// <summary>Whether the displays' grab handed to the overlay has been let go, after full collections for up to a second.</summary>
        public async Task<bool> DisplayGrabsLetGo()
        {
            var deadline = DateTime.UtcNow.AddSeconds(1);
            while (true)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                if (!Screen.Handed[0].TryGetTarget(out _))
                {
                    return true;
                }

                if (DateTime.UtcNow > deadline)
                {
                    return false;
                }

                await Task.Delay(20);
            }
        }

        public void Dispose() => Stash.Dispose();

        private static async Task Until(Func<bool> condition, string never)
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!condition())
            {
                Assert.IsLessThan(deadline, DateTime.UtcNow, never);
                await Task.Delay(5);
            }
        }
    }
}
