namespace ImageStash.Seams;

/// <summary>One running ImageStash per user session (G3).</summary>
public interface IOneInstance
{
    /// <summary>Takes the session's instance for as long as this process runs; false when another process holds it.</summary>
    bool TryHold();

    /// <summary>Asks the running instance to show its window.</summary>
    void SignalRunning();

    /// <summary>
    /// Calls <paramref name="showRequested"/> each time another launch asks this instance to show its window,
    /// on a thread of the seam's own; the caller moves it to the UI thread.
    /// </summary>
    void ListenForShowRequests(Action showRequested);
}
