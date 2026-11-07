using System.Globalization;

namespace ImageStash.Files;

/// <summary>
/// The one home of a byte count in words, as File Explorer counts it: the preview's and the capture notification's
/// file size (G19, G16, G24) and the recording bar's size so far (G15).
/// </summary>
public static class FileSize
{
    private static readonly CultureInfo English = CultureInfo.InvariantCulture;

    /// <summary>
    /// In units of 1,024: whole KB below a megabyte, at least 1 KB for anything written; MB with one decimal from one.
    /// Nothing at all is 0 KB, as a recording reads before its first frame is written.
    /// </summary>
    public static string Words(long bytes) =>
        bytes <= 0 ? "0 KB"
        : bytes >= 1024 * 1024 ? (bytes / (1024.0 * 1024)).ToString("0.0", English) + " MB"
        : Math.Max(1, (long)Math.Round(bytes / 1024.0, MidpointRounding.AwayFromZero)).ToString(English) + " KB";
}
