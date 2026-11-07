using System.Globalization;

namespace ImageStash.Media;

/// <summary>
/// The one home of an animation's time in words, from the frames' own durations: its length in seconds, as the preview,
/// the capture notification and Trim's kept range say it (G19, G16, G22), and a moment on the clip as Trim's clock reads
/// it. Both round to the nearest tenth of a second, halves up, so a kept range's ends and its length agree.
/// </summary>
public static class ClipLength
{
    private static readonly CultureInfo English = CultureInfo.InvariantCulture;

    /// <summary>A length in seconds with one decimal: "2.4 s".</summary>
    public static string Words(long milliseconds)
    {
        var tenths = Tenths(milliseconds);
        return $"{(tenths / 10).ToString(English)}.{(tenths % 10).ToString(English)} s";
    }

    /// <summary>A moment on the clip in minutes, seconds and tenths, as a player's clock reads it: "0:01.3".</summary>
    public static string Clock(long milliseconds)
    {
        var tenths = Tenths(milliseconds);
        return $"{(tenths / 600).ToString(English)}:{(tenths / 10 % 60).ToString("00", English)}.{(tenths % 10).ToString(English)}";
    }

    private static long Tenths(long milliseconds) => (Math.Max(0, milliseconds) + 50) / 100;
}
