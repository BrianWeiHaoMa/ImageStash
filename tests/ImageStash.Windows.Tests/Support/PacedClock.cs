namespace ImageStash.Windows.Tests.Support;

/// <summary>
/// A clock each of whose readings of the time stamp is a second past the last, so no click the recording's bar reads on
/// it falls within a double-click's time of an earlier one: a check clicking Record and later Stop sees Stop act however
/// quickly its clicks come.
/// </summary>
public sealed class PacedClock : TimeProvider
{
    private long ticks;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => Interlocked.Add(ref ticks, TimeSpan.TicksPerSecond);
}
