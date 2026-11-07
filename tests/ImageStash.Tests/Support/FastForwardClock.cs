namespace ImageStash.Tests.Support;

/// <summary>
/// A clock whose every timer fires at once, the time moving on by what the timer was to wait, which it records; so a
/// check of something paced by a clock runs at full speed and reads the waits it asked for.
/// </summary>
public sealed class FastForwardClock : TimeProvider
{
    private readonly List<TimeSpan> waits = [];
    private long ticks;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    /// <summary>Every wait asked for, in order.</summary>
    public IReadOnlyList<TimeSpan> Waits
    {
        get
        {
            lock (waits)
            {
                return waits.ToList();
            }
        }
    }

    public override long GetTimestamp() => Interlocked.Read(ref ticks);

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        lock (waits)
        {
            waits.Add(dueTime);
        }

        Interlocked.Add(ref ticks, dueTime.Ticks);
        ThreadPool.QueueUserWorkItem(_ => callback(state));
        return new Fired();
    }

    private sealed class Fired : ITimer
    {
        public bool Change(TimeSpan dueTime, TimeSpan period) => false;

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
