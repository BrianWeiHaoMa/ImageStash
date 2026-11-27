using Avalonia;
using ImageStash.Capture;
using ImageStash.Seams;
using ImageStash.Window;

namespace ImageStash.Tests.Support;

/// <summary>
/// A check's stash on two displays, the primary and one to its right, with every stack the notifications' home opens
/// recorded with its display as the app draws it; and captures of a region on either, through the acts' own entries.
/// </summary>
public sealed class TwoDisplays
{
    /// <summary>The display to the primary's right, at another scaling.</summary>
    public static readonly Display Second = new(new PixelRect(1280, 0, 1920, 1080), new PixelRect(1280, 0, 1920, 1040), 1.5, IsPrimary: false);

    private TwoDisplays(ListedStash listed)
    {
        Listed = listed;
        listed.Screen.Shown = [RecordingScreen.Primary, Second];
        // As the app draws and follows each stack opened (App.cs), before its first notification shows.
        listed.Stacks.Opened += (stack, display) => Opened.Add((stack, display, stack.Shown.Count));
    }

    public ListedStash Listed { get; }

    /// <summary>Each stack opened for another display, its display, and how many notifications it held as it opened.</summary>
    public List<(Notices Stack, Display Display, int HeldAtOpening)> Opened { get; } = [];

    /// <summary>The second display's stack, which must have been opened.</summary>
    public Notices OnSecond => Opened.Single(opened => opened.Display == Second).Stack;

    public static async Task<TwoDisplays> Open() => new(await ListedStash.Open());

    /// <summary>A stash whose recordings run on a clock the check steps.</summary>
    public static async Task<TwoDisplays> OpenRecording() => new(await ListedStash.OpenRecording());

    /// <summary>A region of 30 × 20 pixels on <paramref name="display"/>, 100 by 50 pixels into it.</summary>
    public static SelectedRegion RegionOn(Display display) =>
        new(display, new PixelRect(display.Bounds.X + 100, display.Bounds.Y + 50, 30, 20));

    /// <summary>A screenshot of a region on <paramref name="display"/>, through the act.</summary>
    public Task Screenshot(Display display)
    {
        Listed.Selection.Answer = RegionOn(display);
        return Listed.Screenshot.Take();
    }

    /// <summary>A recording of a region on <paramref name="display"/> begun through the act, answered once its region waits.</summary>
    public async Task<Task> RecordingWaitingOn(Display display)
    {
        Listed.Selection.Answer = RegionOn(display);
        var acting = Listed.RecordClip.StartOrStop();
        await Listed.WhileWaiting();
        return acting;
    }

    /// <summary>The waiting recording of <paramref name="acting"/> recorded for three samples and stopped, through its bar.</summary>
    public async Task FinishRecording(Task acting)
    {
        await Listed.RecordFromTheWait();
        Listed.StepRecording(140);
        Listed.Stepped!.Pass(TimeSpan.FromMilliseconds(70));
        Listed.Frame.PressStop();
        await acting.WaitAsync(TimeSpan.FromSeconds(10));
    }
}
