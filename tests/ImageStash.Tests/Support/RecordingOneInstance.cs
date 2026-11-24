using ImageStash.Seams;

namespace ImageStash.Tests.Support;

/// <summary>A One-instance seam that records what a start asked of it, the instance free or held by another process as the check sets.</summary>
public sealed class RecordingOneInstance : IOneInstance
{
    public List<string> Calls { get; } = [];

    /// <summary>Whether no other process holds the session's instance, so a hold succeeds.</summary>
    public bool Free { get; init; } = true;

    public bool TryHold()
    {
        Calls.Add("hold");
        return Free;
    }

    public void SignalRunning() => Calls.Add("signal");

    public void ListenForShowRequests(Action showRequested) => Calls.Add("listen");
}
