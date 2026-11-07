using System.IO.Compression;
using System.Text.Json;
using ImageStash.Files;

namespace ImageStash.Store;

/// <summary>The words of every refusal an export or an import gives about the file it is handed (G35, G36).</summary>
public static class TransferReasons
{
    public const string NotAnExport = "it is not an ImageStash export";

    /// <summary>The words <see cref="StoreOpen"/> gives a newer store, given the archive that carries one.</summary>
    public const string NewerBuild = "it was written by a newer ImageStash, which this one cannot read";

    public const string Unfinished = "its import could not be finished; ImageStash finishes it when it next starts";
}

/// <summary>What an export or an import is doing: writing the archive, extracting it, replacing the stash folder, or writing the settings.</summary>
public enum TransferPhase
{
    Writing,
    Extracting,
    Replacing,
    Finishing,
}

/// <summary>An export's or an import's progress, reported on the store's thread: <paramref name="Done"/> of <paramref name="Total"/> in <paramref name="Phase"/>.</summary>
public sealed record TransferProgress(TransferPhase Phase, int Done, int Total);

/// <summary>What an archive's manifest says: how many items it holds, its store's version and the build that wrote it.</summary>
public sealed record ExportSummary(int Items, long StoreVersion, string Build);

/// <summary>
/// The export archive's format (G36, State: Export stash…), one ZIP file: <c>manifest.json</c> naming the format, its
/// version, the store's version, the build and the item count; <c>stash.db</c>; <c>settings.json</c> holding the
/// travelling values under <c>settings.json</c>'s own names; every root file under its name and every file of
/// <c>originals\</c> as <c>originals/&lt;name&gt;</c>. Read before anything is written: the manifest first, so a newer
/// build's archive is refused as that whatever else it holds, then every entry's name, so none can land outside the
/// folder it is extracted into.
/// </summary>
internal static class StashArchive
{
    public const string Format = "imagestash-export";

    public const int FormatVersion = 1;

    public const string ManifestEntry = "manifest.json";

    public const string StoreEntry = StashFolder.StoreFileName;

    public const string SettingsEntry = Settings.FileName;

    public const string OriginalsFolder = "originals";


    /// <summary>The manifest as the archive carries it, camelCase as <c>settings.json</c> is.</summary>
    public sealed record Manifest(string Format, int FormatVersion, long StoreVersion, string Build, int Items);

    /// <summary>This build, as a manifest names it.</summary>
    public static string Build => typeof(StashArchive).Assembly.GetName().Version?.ToString() ?? "unknown";

    /// <summary>An archive is named by its file name, or by the path as given when it has none.</summary>
    public static string Subject(string path)
    {
        try
        {
            return Path.GetFileName(path) is { Length: > 0 } name ? name : path;
        }
        catch (ArgumentException)
        {
            return path;
        }
    }

    /// <summary>
    /// The archive <paramref name="stream"/> holds, checked: its manifest, its travelling values (a value an earlier build's
    /// archive lacks taking its default, G31), and the entries to extract with the path each takes inside the folder; or
    /// the refusal, naming <paramref name="action"/> and <paramref name="subject"/>. The stream is the archive's, and is
    /// closed with it.
    /// </summary>
    public static Outcome<ReadArchive> Read(Stream stream, string action, string subject)
    {
        Failure Refused(string reason) => new(action, subject, reason);
        ZipArchive zip;
        try
        {
            zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        }
        catch (Exception e) when (e is InvalidDataException or IOException)
        {
            // A file another program holds is held, not "not an export": its reader retries it (G35).
            stream.Dispose();
            return Refused(HeldFileRetry.IsHeldRefusal(e) ? HeldFileRetry.HeldReason : e is IOException io ? HeldFileRetry.ReasonFor(io) : TransferReasons.NotAnExport);
        }

        try
        {
            var manifest = ReadEntry<Manifest>(zip, ManifestEntry);
            if (manifest is null || manifest.Format != Format || manifest.FormatVersion < 1 || manifest.Build is null)
            {
                zip.Dispose();
                return Refused(TransferReasons.NotAnExport);
            }

            if (manifest.FormatVersion > FormatVersion || manifest.StoreVersion > StoreSchema.CurrentVersion)
            {
                zip.Dispose();
                return Refused(TransferReasons.NewerBuild);
            }

            var settings = ReadEntry<Settings>(zip, SettingsEntry);
            var files = Files(zip);
            if (settings?.Shortcuts is null || files is null || !files.Any(file => file.Path == StoreEntry))
            {
                zip.Dispose();
                return Refused(TransferReasons.NotAnExport);
            }

            return Outcome<ReadArchive>.Of(new ReadArchive(zip, manifest, TravellingSettings.Of(settings), files));
        }
        catch (Exception e) when (e is InvalidDataException or JsonException or IOException)
        {
            // An entry the archive cannot give back (damaged or truncated) is the file's refusal, never a crash (G35).
            zip.Dispose();
            return Refused(HeldFileRetry.IsHeldRefusal(e) ? HeldFileRetry.HeldReason : e is IOException io ? HeldFileRetry.ReasonFor(io) : TransferReasons.NotAnExport);
        }
    }

    // The entry read as JSON, or null when the archive has none or it holds null.
    private static T? ReadEntry<T>(ZipArchive zip, string name)
        where T : class
    {
        if (zip.GetEntry(name) is not { } entry)
        {
            return null;
        }

        using var read = entry.Open();
        return JsonSerializer.Deserialize<T>(read, Settings.Json);
    }

    // Every entry but the manifest and the settings, with the path it takes inside the extracted folder: the store, a
    // root file, or a file of originals\; null when one is anything else (a path that climbs or nests, a name Windows
    // does not keep as written, a store's journal) or when two take the same path as Windows compares them.
    private static List<(ZipArchiveEntry Entry, string Path)>? Files(ZipArchive zip)
    {
        var files = new List<(ZipArchiveEntry, string)>();
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in zip.Entries)
        {
            var name = entry.FullName;
            if (name is ManifestEntry or SettingsEntry || name == OriginalsFolder + "/")
            {
                continue;
            }

            var parts = name.Split('/');
            var path = parts switch
            {
                [StoreEntry] => StoreEntry,
                [var file] when IsPlainFileName(file) && !IsStoreFileName(file) => file,
                [OriginalsFolder, var file] when IsPlainFileName(file) && !IsStoreFileName(file) => System.IO.Path.Combine(OriginalsFolder, file),
                _ => null,
            };
            if (path is null || !taken.Add(path))
            {
                return null;
            }

            files.Add((entry, path));
        }

        return files;
    }

    /// <summary>
    /// A name that stays inside the folder it is joined to, as Windows keeps it: no separator, drive or other character a
    /// file name may not hold, and no trailing dot or space, which Windows trims; never <c>.</c> or <c>..</c>.
    /// </summary>
    public static bool IsPlainFileName(string? name) =>
        name is { Length: > 0 } && name.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) < 0 && !name.EndsWith('.') && !name.EndsWith(' ');

    /// <summary>The store's name, or that of a file SQLite keeps beside it: never an item's file, carried only as the store's entry.</summary>
    public static bool IsStoreFileName(string name) => StoreFiles.Contains(name);

    private static readonly HashSet<string> StoreFiles = new(
        [StoreEntry, StoreEntry + "-journal", StoreEntry + "-wal", StoreEntry + "-shm"], StringComparer.OrdinalIgnoreCase);
}

/// <summary>An archive <see cref="StashArchive.Read"/> checked, open until disposed.</summary>
internal sealed class ReadArchive(ZipArchive zip, StashArchive.Manifest manifest, TravellingSettings settings, IReadOnlyList<(ZipArchiveEntry Entry, string Path)> files) : IDisposable
{
    public StashArchive.Manifest Manifest { get; } = manifest;

    public TravellingSettings Settings { get; } = settings;

    /// <summary>The entries to extract, each with its path inside the folder it is extracted into.</summary>
    public IReadOnlyList<(ZipArchiveEntry Entry, string Path)> Files { get; } = files;

    public ExportSummary Summary => new(Manifest.Items, Manifest.StoreVersion, Manifest.Build);

    public void Dispose() => zip.Dispose();
}
