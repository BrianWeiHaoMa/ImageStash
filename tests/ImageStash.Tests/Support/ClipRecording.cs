using Avalonia;
using ImageStash.Capture;
using ImageStash.Media;

namespace ImageStash.Tests.Support;

/// <summary>The window's recording checks' stash and their one recording through the act, shared by every file that records a clip.</summary>
public static class ClipRecording
{
    /// <summary>The region the check drags.</summary>
    public static readonly PixelRect Region = new(100, 50, 32, 24);

    /// <summary>
    /// An open stash whose captures run on a clock the check steps, reading local time five and a half hours ahead; the
    /// region the check drags; the display's picture different at every grab, as a live screen's is.
    /// </summary>
    public static async Task<ListedStash> Open(ClipOpener? opener = null, TimeSpan? storeRetry = null)
    {
        var recording = await ListedStash.OpenRecording(opener, storeRetry);
        recording.Selection.Answer = new SelectedRegion(RecordingScreen.Primary, Region);
        MediaFrame[] pictures = [recording.Screen.Paint(RecordingScreen.Primary, seed: 1), recording.Screen.Paint(RecordingScreen.Primary, seed: 2)];
        recording.Screen.EachGrab = index => Outcome<MediaFrame>.Of(pictures[index % 2]);
        return recording;
    }

    /// <summary>One recording through the act: Record pressed on the waiting region, a sample at each tick up to <paramref name="untilMs"/>, then Stop 70 ms after the last.</summary>
    public static async Task Record(ListedStash recording, long untilMs = 210)
    {
        var acting = recording.RecordClip.StartOrStop();
        await recording.RecordFromTheWait();
        recording.StepRecording(untilMs);
        recording.Stepped!.Pass(TimeSpan.FromMilliseconds(70));
        recording.Frame.PressStop();
        await acting.WaitAsync(TimeSpan.FromSeconds(10));
    }
}
