using System.Runtime.Versioning;

namespace ImageStash.Tests.Support;

/// <summary>
/// A file another program holds, as a sync client or scanner may (E3): another handle locks every byte of it, so the
/// file system refuses reads and writes of those bytes through every other handle until the lock goes.
/// </summary>
[SupportedOSPlatform("windows")]
public static class HeldFiles
{
    /// <summary>Runs <paramref name="call"/> while the file at <paramref name="path"/> is held; the hold is gone before the result is returned.</summary>
    public static T WhileLocked<T>(string path, Func<T> call)
    {
        using var other = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        other.Lock(0, long.MaxValue);
        return call();
    }

    /// <summary>Holds the file at <paramref name="path"/> now and lets go after <paramref name="hold"/>, when the task ends.</summary>
    public static Task HeldFor(string path, TimeSpan hold)
    {
        var other = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        other.Lock(0, long.MaxValue);
        return Task.Run(async () =>
        {
            await Task.Delay(hold);
            other.Unlock(0, long.MaxValue);
            await other.DisposeAsync();
        });
    }
}
