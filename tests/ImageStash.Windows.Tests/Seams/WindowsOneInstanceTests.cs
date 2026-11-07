using ImageStash.Windows.Seams;

namespace ImageStash.Windows.Tests.Seams;

/// <summary>G3, S7: one holder per name; a second launch is refused and its signal reaches the holder; a new holder after the first ends.</summary>
[TestClass]
public sealed class WindowsOneInstanceTests
{
    private readonly string name = "ImageStash.Tests." + Guid.NewGuid().ToString("N");

    [TestMethod]
    public void ASecondLaunchIsRefusedAndItsSignalReachesTheRunningInstance()
    {
        using var running = new WindowsOneInstance(name);
        Assert.IsTrue(running.TryHold());
        using var asked = new ManualResetEventSlim();
        running.ListenForShowRequests(asked.Set);

        // A mutex is re-entrant on its owning thread; the second launch is another process, so another thread here.
        var secondHeld = OnOtherThread(() =>
        {
            using var second = new WindowsOneInstance(name);
            var held = second.TryHold();
            second.SignalRunning();
            return held;
        });

        Assert.IsFalse(secondHeld);
        Assert.IsTrue(asked.Wait(TimeSpan.FromSeconds(5)), "the running instance was not asked to show its window");
    }

    [TestMethod]
    public void QuittingWhileAShowRequestIsHandledWaitsForItAndEndsTheListener()
    {
        var running = new WindowsOneInstance(name);
        Assert.IsTrue(running.TryHold());
        using var entered = new ManualResetEventSlim();
        using var finished = new ManualResetEventSlim();
        running.ListenForShowRequests(() =>
        {
            entered.Set();
            Thread.Sleep(300);
            finished.Set();
        });
        running.SignalRunning();
        Assert.IsTrue(entered.Wait(TimeSpan.FromSeconds(5)));

        running.Dispose();

        Assert.IsTrue(finished.IsSet, "Dispose returned while the show request was still being handled");
    }

    [TestMethod]
    public void AfterTheRunningInstanceEndsANewLaunchHoldsTheInstance()
    {
        var first = new WindowsOneInstance(name);
        Assert.IsTrue(first.TryHold());
        first.Dispose();

        Assert.IsTrue(OnOtherThread(() =>
        {
            using var next = new WindowsOneInstance(name);
            return next.TryHold();
        }));
    }

    [TestMethod]
    public void AnInstanceKilledWhileHoldingLeavesItToTheNextLaunch()
    {
        // A thread that ends while holding stands in for a killed process: the mutex is abandoned.
        using var killed = new WindowsOneInstance(name);
        Assert.IsTrue(OnOtherThread(killed.TryHold));

        Assert.IsTrue(OnOtherThread(() =>
        {
            using var next = new WindowsOneInstance(name);
            return next.TryHold();
        }));
    }

    private static T OnOtherThread<T>(Func<T> work)
    {
        T result = default!;
        var thread = new Thread(() => result = work());
        thread.Start();
        thread.Join();
        return result;
    }
}
