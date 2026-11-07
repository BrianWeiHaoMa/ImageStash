namespace ImageStash.Tests.Support;

/// <summary>A clock that reads whatever time, and local time zone, the check sets.</summary>
public sealed class SetClock : TimeProvider
{
    /// <summary>Five and a half hours ahead of UTC, so a name in UTC could not pass for one in local time.</summary>
    public static readonly TimeZoneInfo Ahead = TimeZoneInfo.CreateCustomTimeZone("five and a half ahead", TimeSpan.FromHours(5.5), "ahead", "ahead");

    public DateTimeOffset Now { get; set; } = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The zone local time is read in: this PC's until a check sets another.</summary>
    public TimeZoneInfo Zone { get; set; } = TimeZoneInfo.Local;

    public override DateTimeOffset GetUtcNow() => Now;

    public override TimeZoneInfo LocalTimeZone => Zone;
}
