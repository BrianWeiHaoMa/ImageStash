using System.Globalization;

namespace ImageStash.Store;

/// <summary>
/// F12's automatic names, in local time: <c>Screenshot YYYY-MM-DD HH.MM.SS</c>, which a stashed clipboard image is named
/// like too, and <c>Recording YYYY-MM-DD HH.MM.SS</c>. Two made in the same second share the name; only their files differ (P1).
/// </summary>
public static class AutoName
{
    public static string Screenshot(DateTimeOffset localNow) => Named("Screenshot", localNow);

    public static string Recording(DateTimeOffset localNow) => Named("Recording", localNow);

    private static string Named(string kind, DateTimeOffset localNow) =>
        kind + " " + localNow.ToString("yyyy-MM-dd HH.mm.ss", CultureInfo.InvariantCulture);
}
