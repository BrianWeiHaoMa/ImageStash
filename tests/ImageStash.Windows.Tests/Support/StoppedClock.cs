namespace ImageStash.Windows.Tests.Support;

/// <summary>A clock that stays at the moment it was made, so no notice expires during a check, until a check moves it on.</summary>
public sealed class StoppedClock : TimeProvider
{
    private DateTimeOffset now = DateTimeOffset.UtcNow;

    public override DateTimeOffset GetUtcNow() => now;

    public void Advance(TimeSpan by) => now += by;
}
