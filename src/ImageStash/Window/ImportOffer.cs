using System.Globalization;
using ImageStash.Store;

namespace ImageStash.Window;

/// <summary>
/// An import offered for confirmation inside the Import row (G36): the chosen file, what its manifest says it holds, and
/// how many items the whole stash holds, with the words the row says of it.
/// </summary>
public sealed record ImportOffer(string Path, ExportSummary Summary, int CurrentItems)
{
    /// <summary>What both the offer and the Import row's line say of the replaced stash's files, as G29 qualifies the Recycle Bin.</summary>
    public const string ReplacedFilesWords = "Replaced files go to the Recycle Bin, which may not keep a large stash.";

    /// <summary>
    /// The offer's words (G36): the question naming the whole stash's count and the file's ("Replace your 1,240 items with
    /// the 85 in export.zip?"), that the current pictures, tags and edits leave ImageStash, which settings the file's
    /// replace, and where the replaced files go, as G29 qualifies it.
    /// </summary>
    public string Words
    {
        get
        {
            var file = System.IO.Path.GetFileName(Path);
            var current = CurrentItems switch
            {
                0 => "your empty stash",
                1 => "your 1 item",
                var n => $"your {Counted(n)} items",
            };
            var coming = Summary.Items switch
            {
                0 => $"the empty stash in {file}",
                1 => $"the 1 item in {file}",
                // "the 85" leans on the "items" just said; after an empty stash or one item it says its own noun.
                var n => CurrentItems > 1 ? $"the {Counted(n)} in {file}" : $"the {Counted(n)} items in {file}",
            };
            var leaving = CurrentItems > 0 ? "Your current pictures, tags and edits leave ImageStash. " : "";
            return $"Replace {current} with {coming}? {leaving}"
                + "The file's settings replace yours: the four shortcuts, Also copy, the recording's maximum length and pointer, Start with Windows and Theme. "
                + ReplacedFilesWords;
        }
    }

    /// <summary>A count as the page says it, its thousands grouped, in English (F10).</summary>
    private static string Counted(int count) => count.ToString("N0", CultureInfo.InvariantCulture);
}
