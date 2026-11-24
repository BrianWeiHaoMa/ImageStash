using ImageStash.Seams;
using ImageStash.Windows.Seams;
using ImageStash.Windows.Tests.Support;
using static ImageStash.Windows.Tests.Support.ShownWindow;

namespace ImageStash.Windows.Tests.Seams;

/// <summary>
/// The Target seam's held read (G8): the window before one window of ImageStash's, answered for it while another of
/// ImageStash's windows comes in front and after it has closed, and none until a report the hook delivers late comes.
/// </summary>
[TestClass]
public sealed class WindowsTargetHeldTests
{
    [TestMethod]
    public void HeldBeforeAnswersTheAppAfterAnotherOwnWindowComesForwardAndTheHeldWindowCloses()
    {
        using var pointer = TestMouse.PutBackAfterwards();
        using var seam = new WindowsTarget();
        var display = DisplayPlaces.Primary();
        using var app = TargetApp.Start("ImageStash check before held window", DisplayPlaces.At(display, 100, 100));
        using var later = FrontWindow.Show(DisplayPlaces.At(display, 600, 350));
        var expected = new PasteTarget(app.Window, app.AppName);
        HeldBefore held;
        using (var first = FrontWindow.Show(DisplayPlaces.At(display, 600, 100)))
        {
            app.ClickTitleBar();
            first.ClickToFront();
            held = seam.HoldBeforeMine();
            Assert.AreEqual(expected, held.Read(), "the app in front just before the held window (G8)");

            later.ClickToFront();
            Assert.AreEqual(expected, held.Read(), "still the app while another window of our own is in front (G8)");
        }

        Assert.AreEqual(later.Handle, FrontWindow.GetForegroundWindow(), "the later window is still in front after the held window closed");
        Assert.AreEqual(expected, held.Read(), "still the app after the held window closed (G8)");
    }

    [TestMethod]
    public async Task HeldBeforeAnswersNoneUntilReportedLateThenAnswersTheApp()
    {
        using var pointer = TestMouse.PutBackAfterwards();
        using var seam = new WindowsTarget();
        var display = DisplayPlaces.Primary();
        using var app = TargetApp.Start("ImageStash check before a late report", DisplayPlaces.At(display, 100, 100));
        using var other = TargetApp.Start("ImageStash check before the held activation", DisplayPlaces.At(display, 100, 400));
        using var own = FrontWindow.Show(DisplayPlaces.At(display, 600, 100));
        app.ClickTitleBar();
        own.ClickToFront();
        await Eventually(() => seam.LastBeforeMine() == new PasteTarget(app.Window, app.AppName));
        // The held activation comes from another app than the earlier one, so an answer from that earlier one shows.
        var expected = new PasteTarget(other.Window, other.AppName);

        var reportedLate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        seam.ReportedLate += () => reportedLate.TrySetResult();
        HeldBefore held;
        using (HeldThread.Suspended(seam.HookThread))
        {
            other.ClickTitleBar();
            own.ClickToFront();
            held = seam.HoldBeforeMine();
            // G8: “while ImageStash has not yet heard from Windows which window had focus just before it, it is none until it hears, and then that window”.
            Assert.IsNull(held.Read(), "none while the hook cannot report the activation (G8)");
            Assert.IsFalse(reportedLate.Task.IsCompleted, "ReportedLate has not been raised while the hook is held");
        }

        await reportedLate.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await Eventually(() => held.Read() == expected);
    }
}
