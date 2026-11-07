namespace ImageStash.Tests.Support;

/// <summary>
/// A clock that moves only when the check moves it, so something paced by its timers runs with exact times and the
/// check's own calls fall between its steps. <see cref="Advance"/> fires every timer due on the way, in order of their
/// due moments, each with the time at its moment, on the check's thread; after one fires, the next step waits until its
/// owner has armed it again or let it go, so the owner's work for that moment is done before time moves on.
/// <see cref="Pass"/> moves the time without firing anything, as work that takes time does. One-shot timers only.
/// </summary>
public sealed class SteppedClock : TimeProvider
{
    private static readonly TimeSpan OwnerTimeout = TimeSpan.FromSeconds(10);

    private readonly object gate = new();
    private readonly List<SteppedTimer> timers = [];
    private long ticks;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    /// <summary>The time since the clock was made.</summary>
    public TimeSpan Elapsed
    {
        get
        {
            lock (gate)
            {
                return TimeSpan.FromTicks(ticks);
            }
        }
    }

    /// <summary>When the earliest armed timer is due, as time since the clock was made; null while none is armed.</summary>
    public TimeSpan? NextDue
    {
        get
        {
            lock (gate)
            {
                return timers.Where(t => t.DueTicks is not null).Select(t => (TimeSpan?)TimeSpan.FromTicks(t.DueTicks!.Value)).Min();
            }
        }
    }

    /// <summary>A timer armed for longer than this fires this much before its due moment, as a system timer may by its clock's granularity.</summary>
    public TimeSpan FiresEarlyBy { get; set; }

    /// <summary>
    /// When set, a timer fires at the first multiple of this at or after its due moment, as a system timer does on its
    /// clock's tick (15.625 ms on Windows by default).
    /// </summary>
    public TimeSpan FiresOnTicksOf { get; set; }

    /// <summary>Each read of the time moves it on by this much afterwards, as time passes between a thread's readings.</summary>
    public TimeSpan PassesOnEachRead { get; set; }

    /// <summary>The time of day the clock reads when it is made; it moves on with <see cref="Elapsed"/>.</summary>
    public DateTimeOffset Start { get; set; } = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The local time zone it reads in, UTC unless a check sets another.</summary>
    public TimeZoneInfo Zone { get; set; } = TimeZoneInfo.Utc;

    public override DateTimeOffset GetUtcNow() => Start + Elapsed;

    public override TimeZoneInfo LocalTimeZone => Zone;

    public override long GetTimestamp()
    {
        lock (gate)
        {
            var now = ticks;
            ticks += PassesOnEachRead.Ticks;
            return now;
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new SteppedTimer(this, callback, state);
        lock (gate)
        {
            timers.Add(timer);
        }

        timer.Change(dueTime, period);
        return timer;
    }

    /// <summary>Moves the time on by <paramref name="by"/>, firing nothing: work inside a timer's owner taking that long.</summary>
    public void Pass(TimeSpan by)
    {
        lock (gate)
        {
            ticks += by.Ticks;
        }
    }

    /// <summary>
    /// Moves the time on by <paramref name="by"/>, firing each timer due on the way at its due moment, and, before each
    /// step, waiting for the owner of the timer fired last to arm it again or let it go.
    /// </summary>
    public void Advance(TimeSpan by)
    {
        long target;
        lock (gate)
        {
            target = ticks + by.Ticks;
        }

        while (true)
        {
            SteppedTimer? due;
            lock (gate)
            {
                WaitForOwners();
                due = timers.Where(t => t.DueTicks <= target).MinBy(t => t.DueTicks);
                if (due is null)
                {
                    ticks = Math.Max(ticks, target);
                    return;
                }

                ticks = Math.Max(ticks, due.DueTicks!.Value);
                due.DueTicks = null;
                due.Owed = true;
            }

            due.Fire();
        }
    }

    private void WaitForOwners()
    {
        var deadline = DateTime.UtcNow + OwnerTimeout;
        while (timers.Any(t => t.Owed))
        {
            var left = deadline - DateTime.UtcNow;
            if (left <= TimeSpan.Zero || !Monitor.Wait(gate, left))
            {
                throw new TimeoutException($"a timer fired at {Elapsed.TotalMilliseconds} ms was neither armed again nor let go within {OwnerTimeout.TotalSeconds} s");
            }
        }
    }

    private sealed class SteppedTimer(SteppedClock clock, TimerCallback callback, object? state) : ITimer
    {
        /// <summary>When it fires next, in the clock's ticks; null while not armed.</summary>
        public long? DueTicks { get; set; }

        /// <summary>True from its firing until its owner arms it again or lets it go.</summary>
        public bool Owed { get; set; }

        public void Fire() => callback(state);

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            if (period != Timeout.InfiniteTimeSpan)
            {
                throw new NotSupportedException("The stepped clock fires one-shot timers only.");
            }

            lock (clock.gate)
            {
                var early = dueTime > clock.FiresEarlyBy ? clock.FiresEarlyBy.Ticks : 0;
                var at = clock.ticks + Math.Max(0, dueTime.Ticks - early);
                var tick = clock.FiresOnTicksOf.Ticks;
                DueTicks = dueTime == Timeout.InfiniteTimeSpan ? null : tick > 0 ? (at + tick - 1) / tick * tick : at;
                Owed = false;
                Monitor.PulseAll(clock.gate);
            }

            return true;
        }

        public void Dispose()
        {
            lock (clock.gate)
            {
                clock.timers.Remove(this);
                DueTicks = null;
                Owed = false;
                Monitor.PulseAll(clock.gate);
            }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
