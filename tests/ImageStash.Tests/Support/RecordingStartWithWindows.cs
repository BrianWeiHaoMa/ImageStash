using ImageStash.Seams;

namespace ImageStash.Tests.Support;

/// <summary>A Start with Windows seam that records what it was asked to do, and refuses it while the check sets a refusal.</summary>
public sealed class RecordingStartWithWindows : IStartWithWindows
{
    public List<string> Calls { get; } = [];

    /// <summary>What every call answers while set, as Windows refusing the Run entry's change; done while null.</summary>
    public Failure? Refusal { get; set; }

    public Outcome Set(string executablePath)
    {
        Calls.Add($"set {executablePath}");
        return Refusal is { } refused ? refused : Outcome.Done;
    }

    public Outcome Clear()
    {
        Calls.Add("clear");
        return Refusal is { } refused ? refused : Outcome.Done;
    }
}
