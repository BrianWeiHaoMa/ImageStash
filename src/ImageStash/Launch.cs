using ImageStash.Seams;

namespace ImageStash;

/// <summary>
/// The kind of start (G3): the start Windows makes at sign-in (F1) carries the one argument the Run entry registers after
/// the executable's path (State: Settings), and every other start is a launch by hand. The Run entry's rule writes that
/// argument from here and the entry point recognises it from here, so the two cannot drift apart.
/// </summary>
public static class Launch
{
    /// <summary>The argument the Run entry carries, which marks the start at sign-in.</summary>
    public const string SignInArgument = "--at-sign-in";

    /// <summary>
    /// Whether a start's arguments carry <see cref="SignInArgument"/>, exactly as written, alone or beside others; a start
    /// from an earlier build's Run entry carries none and is a launch by hand (G3).
    /// </summary>
    public static bool IsAtSignIn(IReadOnlyList<string> args) => args.Contains(SignInArgument, StringComparer.Ordinal);

    /// <summary>
    /// Takes the session's instance, before anything else (G3, E1): true when this start now holds it. While another process
    /// holds it, a launch by hand asks that one to show its window as a tray click would, and the start at sign-in exits
    /// without signalling, so it opens nothing there (G3. One instance).
    /// </summary>
    public static bool HoldInstance(IOneInstance instance, IReadOnlyList<string> args)
    {
        if (instance.TryHold())
        {
            return true;
        }

        if (!IsAtSignIn(args))
        {
            instance.SignalRunning();
        }

        return false;
    }
}
