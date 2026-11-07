using ImageStash.Capture;

namespace ImageStash.Tests.Support;

/// <summary>
/// The frame and the bar's stand-in for the portable checks: every call recorded in order, from whichever thread made
/// it, with the region it showed, and the readings, Record, Stop and Cancel it was handed kept, so a check can read the
/// bar's readings and press its buttons as a person would.
/// </summary>
public sealed class PresetRecordingFrame : IRecordingFrame
{
    private readonly List<string> calls = [];

    /// <summary>Every call so far, in order: "show" (the wait), "record" (the recording look), "saving" and "close".</summary>
    public IReadOnlyList<string> Calls
    {
        get
        {
            lock (calls)
            {
                return calls.ToList();
            }
        }
    }

    /// <summary>The region, cap and buttons of the last showing; null before the first.</summary>
    public (SelectedRegion Region, TimeSpan Cap, Action Record, Action Stop, Action Cancel)? Shown { get; private set; }

    /// <summary>The readings the recording look was last handed; null before the first.</summary>
    public IRecordingReadings? Readings { get; private set; }

    /// <summary>Run inside each call, with its name, so a check can look at the session at that moment, or throw what the windows could throw.</summary>
    public Action<string>? WhileCalled { get; set; }

    /// <summary>What a showing answers: done unless a check sets the platform's refusal.</summary>
    public Outcome ShowAnswer { get; set; } = Outcome.Done;

    /// <summary>What the recording look answers: done unless a check sets the platform's refusal.</summary>
    public Outcome RecordingAnswer { get; set; } = Outcome.Done;

    public Outcome Show(SelectedRegion region, TimeSpan cap, Action record, Action stop, Action cancel)
    {
        Shown = (region, cap, record, stop, cancel);
        Call("show");
        return ShowAnswer;
    }

    public Outcome Recording(IRecordingReadings readings)
    {
        Readings = readings;
        Call("record");
        return RecordingAnswer;
    }

    public void Saving() => Call("saving");

    public void Close() => Call("close");

    /// <summary>Presses the bar's Record.</summary>
    public void PressRecord() => Shown!.Value.Record();

    /// <summary>Presses the bar's Stop.</summary>
    public void PressStop() => Shown!.Value.Stop();

    /// <summary>Presses the bar's Cancel.</summary>
    public void PressCancel() => Shown!.Value.Cancel();

    private void Call(string name)
    {
        WhileCalled?.Invoke(name);
        lock (calls)
        {
            calls.Add(name);
        }
    }
}
