using System.Collections.Concurrent;

namespace ImageStash.Tests.Support;

/// <summary>
/// The one thread the portable checks build Avalonia controls on: Avalonia binds its dispatcher to the first thread that
/// builds a control, and checks run in parallel.
/// </summary>
public static class ControlThread
{
    private static readonly BlockingCollection<(Action Check, TaskCompletionSource Done)> Checks = Start();

    /// <summary>Runs <paramref name="check"/> on that thread, failing as it fails.</summary>
    public static void Run(Action check)
    {
        var done = new TaskCompletionSource();
        Checks.Add((check, done));
        done.Task.GetAwaiter().GetResult();
    }

    private static BlockingCollection<(Action Check, TaskCompletionSource Done)> Start()
    {
        var checks = new BlockingCollection<(Action Check, TaskCompletionSource Done)>();
        new Thread(() =>
        {
            foreach (var (check, done) in checks.GetConsumingEnumerable())
            {
                try
                {
                    check();
                    done.SetResult();
                }
                catch (Exception failed)
                {
                    done.SetException(failed);
                }
            }
        }) { IsBackground = true, Name = "Control checks" }.Start();
        return checks;
    }
}
