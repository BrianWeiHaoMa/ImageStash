using System.Security.Principal;
using System.Text;

namespace ImageStash.Windows.Tests.Support;

/// <summary>
/// The signed-in user's real Recycle Bin, read from its folder on a file's drive: each entry is a <c>$R</c> file, the
/// recycled file itself, beside a <c>$I</c> record naming where it came from.
/// </summary>
public static class RecycleBinEntries
{
    /// <summary>An entry: the recycled file and its record.</summary>
    public sealed record Entry(string Recycled, string Record);

    /// <summary>Every entry recycled from <paramref name="originalPath"/>, compared as Windows compares paths.</summary>
    public static IReadOnlyList<Entry> Of(string originalPath) =>
        Where(originalPath, path => string.Equals(path, originalPath, StringComparison.OrdinalIgnoreCase));

    /// <summary>Every entry recycled from inside <paramref name="folder"/>, at any depth, compared as Windows compares paths.</summary>
    public static IReadOnlyList<Entry> Within(string folder)
    {
        var inside = Path.TrimEndingDirectorySeparator(folder) + Path.DirectorySeparatorChar;
        return Where(folder, path => path?.StartsWith(inside, StringComparison.OrdinalIgnoreCase) == true);
    }

    /// <summary>The entries in the bin on <paramref name="onDrive"/>'s drive whose record names a path <paramref name="came"/> accepts.</summary>
    private static List<Entry> Where(string onDrive, Func<string?, bool> came)
    {
        var bin = Path.Combine(Path.GetPathRoot(onDrive)!, "$Recycle.Bin", WindowsIdentity.GetCurrent().User!.Value);
        return Directory.GetFiles(bin, "$I*")
            .Where(record => came(OriginalPath(record)))
            .Select(record => new Entry(Path.Combine(bin, "$R" + Path.GetFileName(record)[2..]), record))
            .ToList();
    }

    /// <summary>
    /// The path a <c>$I</c> record names: in version 2, the one Windows 10 and 11 write, a character count at byte 24
    /// and the path after it; in version 1, a fixed field of 260 characters at byte 24.
    /// </summary>
    private static string? OriginalPath(string record)
    {
        try
        {
            var bytes = File.ReadAllBytes(record);
            return BitConverter.ToInt64(bytes, 0) == 2
                ? Encoding.Unicode.GetString(bytes, 28, (BitConverter.ToInt32(bytes, 24) - 1) * 2)
                : Encoding.Unicode.GetString(bytes, 24, 520).TrimEnd('\0');
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // A record another program is writing or removing right now, or one of another shape, is no entry of the check's.
            return null;
        }
    }
}
