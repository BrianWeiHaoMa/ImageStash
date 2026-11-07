using Avalonia;
using ImageStash.Capture;
using ImageStash.Media;
using ImageStash.Seams;
using ImageStash.Store;
using ImageStash.Tests.Support;

namespace ImageStash.Tests.Capture;

/// <summary>
/// The screenshot's steps over the Screen seam's double and a preset selection (G14): every display grabbed once before
/// the selection, the region's pixels cut from its own display's frozen grab and written into <c>.work\</c> as a PNG
/// (F3, R2) named at the release in local time (F12), the caller's commit run while the capture is in progress and the
/// grabs let go before it; a cancel silent and writing nothing (E3); a refused grab named unchanged before any overlay
/// (E5, G35); a refused write named as the screenshot with its name, leaving nothing; one capture at a time (Approach).
/// </summary>
[TestClass]
public sealed class CaptureSessionTests
{
    private static readonly Display Right150 = new(new PixelRect(1280, 0, 1920, 1200), new PixelRect(1280, 0, 1920, 1152), 1.5, IsPrimary: false);

    [TestMethod]
    public async Task TheRegionIsCutFromItsDisplaysFrozenGrabWrittenAsAPngAndNamedAtTheRelease()
    {
        using var capturing = await Capturing.Open();
        capturing.Screen.Paint(RecordingScreen.Primary, seed: 1);
        var second = capturing.Screen.Paint(Right150, seed: 2);
        var region = new PixelRect(1500, 300, 300, 200);
        capturing.Selection.Answer = new SelectedRegion(Right150, region);
        // The press at 12:00:00 UTC, the release seven seconds later.
        capturing.Selection.AtTheRelease = () => capturing.Clock.Now = capturing.Clock.Now.AddSeconds(7);
        var finished = new List<(CapturedFile File, bool InProgress, bool Written, bool GrabsLetGo)>();

        var outcome = await capturing.Session.TakeScreenshot(capturing.Store, async file =>
        {
            var (inProgress, existed) = (capturing.Session.InProgress, File.Exists(file.WorkPath));
            finished.Add((file, inProgress, existed, await capturing.GrabsLetGo()));
        });

        Assert.IsTrue(outcome.Succeeded, outcome.Failure?.Sentence);
        var (written, inProgress, existed, letGo) = finished.Single();
        Assert.AreEqual(written, outcome.Value, "the file the commit was given");
        Assert.AreEqual("Screenshot 2026-10-16 17.30.07", written.Name, "the release's moment in local time, not the press's (F12)");
        Assert.IsTrue(inProgress, "the commit runs while the capture is in progress");
        Assert.IsTrue(existed, "written whole before the commit");
        Assert.IsTrue(letGo, "no display's pixels kept past the selection");
        Assert.IsTrue(capturing.Stash.Folder.IsWorkFile(written.WorkPath));
        Assert.AreEqual(".png", Path.GetExtension(written.WorkPath));
        var saved = MediaFixtures.FirstFrame(written.WorkPath);
        Assert.AreEqual((300, 200), (saved.Width, saved.Height));
        CollectionAssert.AreEqual(RecordingScreen.Cut(second, new PixelRect(220, 300, 300, 200)).Pixels, saved.Pixels, "exactly the second display's frozen pixels inside the rectangle (G14, F3)");
        Assert.IsFalse(capturing.Session.InProgress);
        Assert.AreEqual(1, capturing.Screen.DisplayReads, "the displays read once");
        CollectionAssert.AreEqual(new[] { RecordingScreen.Primary.Bounds, Right150.Bounds }, capturing.Screen.Grabs, "each display grabbed once, whole, and never again at the release");
        CollectionAssert.AreEqual(new[] { false, false }, capturing.Screen.PointerAsked, "a screenshot draws no pointer in (G14)");
        CollectionAssert.AreEqual(new[] { (RecordingScreen.Primary, 1280, 800), (Right150, 1920, 1200) }, capturing.Selection.Handed.Single().ToList(), "the selection shown over every display's grab");
        Assert.IsEmpty(capturing.Stash.RootFiles(), "nothing written into the root (R2)");
        Assert.IsEmpty(capturing.Named);
    }

    [TestMethod]
    public async Task ACancelWritesNothingCommitsNothingAndSaysNothing()
    {
        using var capturing = await Capturing.Open();
        capturing.Selection.Answer = null;
        var finished = 0;

        var outcome = await capturing.Session.TakeScreenshot(capturing.Store, _ => { finished++; return Task.CompletedTask; });

        Assert.IsTrue(outcome.Succeeded, outcome.Failure?.Sentence);
        Assert.IsNull(outcome.Value, "cancelled, with nothing to name (E3)");
        Assert.AreEqual(0, finished);
        Assert.IsEmpty(capturing.Stash.WorkFiles());
        Assert.IsEmpty(capturing.Stash.RootFiles());
        Assert.IsFalse(capturing.Session.InProgress);
        Assert.IsTrue(await capturing.GrabsLetGo());
        Assert.IsEmpty(capturing.Named);
    }

    [TestMethod]
    public async Task AGrabTheScreenRefusesEndsTheCaptureWithThatFailureBeforeAnyOverlay()
    {
        using var capturing = await Capturing.Open();
        capturing.Screen.Shown = [RecordingScreen.Primary, Right150];
        var refused = new Failure("Grab the screen", "the display at 1280, 0 (1920 × 1200)", "access is denied");
        capturing.Screen.Refuse(Right150, refused);
        capturing.Selection.Answer = new SelectedRegion(RecordingScreen.Primary, new PixelRect(10, 10, 50, 50));
        var finished = 0;

        var outcome = await capturing.Session.TakeScreenshot(capturing.Store, _ => { finished++; return Task.CompletedTask; });

        Assert.AreEqual(refused, outcome.Failure, "the seam's failure, unchanged (E5, G35)");
        Assert.IsEmpty(capturing.Selection.Handed, "no overlay shown");
        Assert.AreEqual(0, finished);
        Assert.IsEmpty(capturing.Stash.WorkFiles());
        Assert.IsFalse(capturing.Session.InProgress);
    }

    [TestMethod]
    public async Task ASecondScreenshotWhileTheFirstAwaitsTheSelectionIsRefusedAtOnce()
    {
        using var capturing = await Capturing.Open();
        var releasing = new TaskCompletionSource();
        capturing.Selection.Gate = releasing.Task;
        capturing.Selection.Answer = new SelectedRegion(RecordingScreen.Primary, new PixelRect(10, 20, 30, 40));
        var finished = new List<CapturedFile>();

        var first = capturing.Session.TakeScreenshot(capturing.Store, file => { finished.Add(file); return Task.CompletedTask; });
        await capturing.WhileSelecting();
        var second = capturing.Session.TakeScreenshot(capturing.Store, file => { finished.Add(file); return Task.CompletedTask; });

        Assert.IsTrue(second.IsCompleted, "refused at once, without waiting on the first");
        Assert.AreEqual(new Failure("Take a screenshot", "the screen", "a capture is already in progress"), (await second).Failure);
        Assert.IsTrue(capturing.Session.InProgress, "the first goes on");
        releasing.SetResult();
        var completed = await first;
        Assert.IsTrue(completed.Succeeded, completed.Failure?.Sentence);
        Assert.AreEqual(completed.Value, finished.Single(), "only the first was committed");
        Assert.HasCount(1, capturing.Selection.Handed, "one overlay shown");
        Assert.AreEqual(1, capturing.Screen.DisplayReads, "nothing grabbed for the second");
        Assert.IsFalse(capturing.Session.InProgress);
    }

    [TestMethod]
    public async Task AWriteTheStashRefusesIsNamedAsTheScreenshotAndLeavesNothing()
    {
        using var capturing = await Capturing.Open();
        capturing.Selection.Answer = new SelectedRegion(RecordingScreen.Primary, new PixelRect(10, 20, 30, 40));
        // The work folder gone under the store between the press and the write.
        capturing.Selection.AtTheRelease = () => Directory.Delete(capturing.Stash.Folder.Work, recursive: true);
        var finished = 0;

        var outcome = await capturing.Session.TakeScreenshot(capturing.Store, _ => { finished++; return Task.CompletedTask; });

        Assert.IsFalse(outcome.Succeeded);
        Assert.AreEqual(("Take a screenshot", "Screenshot 2026-10-16 17.30.00"), (outcome.Failure.Action, outcome.Failure.Subject), outcome.Failure.Sentence);
        Assert.IsFalse(string.IsNullOrWhiteSpace(outcome.Failure.Reason));
        Assert.AreEqual(0, finished, "nothing to commit");
        Assert.IsFalse(Directory.Exists(capturing.Stash.Folder.Work), "nothing written into .work\\ or anywhere else in its place");
        Assert.IsEmpty(capturing.Stash.RootFiles(), "nothing written into the root (R2)");
        Assert.IsEmpty(capturing.Named, "no partial file to name");
        Assert.IsFalse(capturing.Session.InProgress);
    }

    [TestMethod]
    public async Task NoRoomForTheOverlayIsANamedFailureAndEndsTheCapture()
    {
        using var capturing = await Capturing.Open();
        capturing.Selection.AtTheRelease = () => throw new OutOfMemoryException();
        var finished = 0;

        var outcome = await capturing.Session.TakeScreenshot(capturing.Store, _ => { finished++; return Task.CompletedTask; });

        Assert.AreEqual(new Failure("Take a screenshot", "the screen", "there is not enough memory to hold it"), outcome.Failure, "named, never thrown (G35)");
        Assert.AreEqual(0, finished);
        Assert.IsEmpty(capturing.Stash.WorkFiles());
        Assert.IsFalse(capturing.Session.InProgress, "the next screenshot can start");
    }

    /// <summary>A session over a stash of its own, the Screen seam's double with two displays and a preset selection, its clock ahead of UTC.</summary>
    private sealed class Capturing : IDisposable
    {
        private Capturing(TestStash stash, StashStore store)
        {
            Stash = stash;
            Store = store;
            Screen.Shown = [RecordingScreen.Primary, Right150];
            Session = new CaptureSession(Screen, Selection, new PresetRecordingFrame(), GifEncoder.Opener(TestStash.ShortRetry), Clock, Named.Add);
        }

        public TestStash Stash { get; }

        public StashStore Store { get; }

        public RecordingScreen Screen { get; } = new();

        public PresetRegionSelection Selection { get; } = new();

        public SetClock Clock { get; } = new() { Zone = SetClock.Ahead };

        public List<Failure> Named { get; } = [];

        public CaptureSession Session { get; }

        public static async Task<Capturing> Open()
        {
            var stash = new TestStash();
            return new Capturing(stash, await stash.OpenCleanly());
        }

        /// <summary>
        /// Whether every frame the screen handed out has been let go, after full collections for up to a second: a check
        /// resumed on the pool thread still finishing the grab's own task waits for that thread to let go of it.
        /// </summary>
        public async Task<bool> GrabsLetGo()
        {
            var deadline = DateTime.UtcNow.AddSeconds(1);
            while (true)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                if (Screen.Handed.Count > 0 && Screen.Handed.All(frame => !frame.TryGetTarget(out _)))
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

        /// <summary>Until the selection has been handed its grabs.</summary>
        public async Task WhileSelecting()
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (Selection.Handed.Count == 0)
            {
                Assert.IsLessThan(deadline, DateTime.UtcNow, "the selection was never shown");
                await Task.Delay(10);
            }
        }

        public void Dispose() => Stash.Dispose();
    }
}
