using System.Collections.Concurrent;
using Avalonia.Threading;

namespace ImageStash.Tests.Support;

/// <summary>
/// Binds Avalonia's dispatcher to <see cref="ControlThread"/> before any check runs: Avalonia binds it to the first thread
/// that makes one of its objects (a brush included), so a check making one on its own thread first would leave every
/// control check after it on the wrong thread, whatever order the checks run in.
/// </summary>
[TestClass]
public sealed class ControlThreadFirst
{
    [AssemblyInitialize]
    public static void BindTheDispatcher(TestContext context) => ControlThread.Run(() => _ = Dispatcher.UIThread);
}

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
