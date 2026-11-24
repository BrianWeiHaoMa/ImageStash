using ImageStash.Seams;

namespace ImageStash.Tests.Support;

/// <summary>A Start with Windows seam that records what it was asked to do, and refuses it while the check sets a refusal.</summary>
public sealed class RecordingStartWithWindows : IStartWithWindows
{
    public List<string> Calls { get; } = [];

    /// <summary>What every call answers while set, as Windows refusing the Run entry's change; done while null.</summary>
    public Failure? Refusal { get; set; }

    /// <summary>The argument the last set registered beside the executable, as a start at sign-in would then be given it.</summary>
    public string? Argument { get; private set; }

    public Outcome Set(string executablePath, string argument)
    {
        Calls.Add($"set {executablePath} {argument}");
        Argument = argument;
        return Refusal is { } refused ? refused : Outcome.Done;
    }

    public Outcome Clear()
    {
        Calls.Add("clear");
        return Refusal is { } refused ? refused : Outcome.Done;
    }
}
