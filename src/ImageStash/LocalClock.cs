namespace ImageStash;

/// <summary>
/// The product's one clock: the system's, except that its local time zone is read again at every use, so the local
/// times ImageStash names and shows (F12's auto names, the preview's added and used times; What crosses every boundary)
/// are in the PC's zone at that moment, a zone changed while ImageStash runs included. .NET keeps the local zone it first
/// read, <see cref="TimeProvider.System"/>'s too, until <see cref="TimeZoneInfo.ClearCachedData"/>, and nothing else
/// clears it.
/// </summary>
public sealed class LocalClock : TimeProvider
{
    private LocalClock()
    {
    }

    public static LocalClock Instance { get; } = new();

    public override TimeZoneInfo LocalTimeZone
    {
        get
        {
            TimeZoneInfo.ClearCachedData();
            return TimeZoneInfo.Local;
        }
    }
}
