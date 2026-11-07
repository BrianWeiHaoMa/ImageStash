using System.Text.Json;
using System.Text.Json.Serialization;
using ImageStash.Files;

namespace ImageStash.Store;

/// <summary>
/// The app's settings as <c>settings.json</c> records them, each with G32's default. A file from an earlier build is
/// read by its version, and a value it lacks takes its default (G31).
/// </summary>
public sealed record Settings
{
    public const int CurrentVersion = 1;
    public const string FileName = "settings.json";

    public int Version { get; init; } = CurrentVersion;
    public Shortcuts Shortcuts { get; init; } = new();
    public bool AlsoCopyCapture { get; init; } = true;
    public int RecordingMaxSeconds { get; init; } = 30;
    public bool RecordPointer { get; init; } = true;

    /// <summary>The stash folder Settings chose; null while the stash is at its default location.</summary>
    public string? StashFolder { get; init; }

    /// <summary>
    /// The folder a Move is copying the stash into, from the moment it records it until the switch or the copy's failure;
    /// a start that finds it deletes that partial copy (State: Move). Null, and absent from an earlier build's file, otherwise.
    /// </summary>
    public string? MoveCopyInProgress { get; init; }

    /// <summary>The folder a Move switched away from, from the switch until it is removed; a start that finds it finishes the removal (State: Move).</summary>
    public string? PreviousStashFolderPendingRemoval { get; init; }

    /// <summary>
    /// An import past its point of no return (State: Import stash…): from the record until its last step, a start finishes
    /// it before the stash opens, and nothing opens the stash folder meanwhile. Null, and absent from an earlier build's file, otherwise.
    /// </summary>
    public ImportPending? ImportPending { get; init; }

    public bool StartWithWindows { get; init; } = true;
    // An earlier build's "look" (its take of the look) is no longer kept: the reader skips it as it skips every member
    // this build lacks, so that file's other values stay (G31).
    public Theme Theme { get; init; } = Theme.System;

    /// <summary>The step of the grid's thumbnail size (G6).</summary>
    public ThumbnailSize ThumbnailSize { get; init; } = ThumbnailSize.Medium;

    /// <summary>The flyout's size in device-independent units, remembered apart from the pinned window's (G2).</summary>
    public WindowSize FlyoutSize { get; init; } = WindowSize.FlyoutDefault;

    /// <summary>The pinned window's size in device-independent units (G2).</summary>
    public WindowSize PinnedSize { get; init; } = WindowSize.PinnedDefault;

    /// <summary>The pinned window's top-left corner on the desktop in physical pixels; null until the user has placed it.</summary>
    public WindowPosition? PinnedPosition { get; init; }

    // Also the export archive's settings.json, which carries the travelling values under these names (G36).
    internal static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        RespectNullableAnnotations = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };

    /// <summary>
    /// Reads the settings file in <paramref name="folder"/>. When there is none (first run), the defaults are written
    /// at once. A file that cannot be read or parsed gives the defaults and a failure naming it, and is left as it is.
    /// </summary>
    public static SettingsLoad Load(string folder, TimeSpan heldFileRetry)
    {
        var path = Path.Combine(folder, FileName);
        var read = HeldFileRetry.Run("Read settings", path, heldFileRetry, () => ReadText(path));
        if (!read.Succeeded)
        {
            return new SettingsLoad(new Settings(), read.Failure, AsSaved: false);
        }

        if (read.Value is null)
        {
            var defaults = new Settings();
            return new SettingsLoad(defaults, Write(folder, defaults, heldFileRetry).Failure, AsSaved: true, FirstRun: true);
        }

        try
        {
            var settings = JsonSerializer.Deserialize<Settings>(read.Value, Json)
                ?? throw new JsonException("The settings file holds null.");
            return new SettingsLoad(settings, null, AsSaved: true);
        }
        catch (JsonException)
        {
            return new SettingsLoad(new Settings(), new Failure("Read settings", FileName, "the file is not valid settings, so the defaults are in use"), AsSaved: false);
        }
    }

    /// <summary>
    /// Writes <paramref name="settings"/> as a new file beside the old one and renames it over the old, so a kill
    /// leaves the old file or the new one, never a partial one (State: Settings).
    /// </summary>
    public static Outcome Write(string folder, Settings settings, TimeSpan heldFileRetry)
    {
        var path = Path.Combine(folder, FileName);
        var fresh = path + ".new";
        var text = JsonSerializer.Serialize(settings with { Version = CurrentVersion }, Json);
        var written = HeldFileRetry.Run("Save settings", path, heldFileRetry, () =>
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(fresh, text);
        });
        if (!written.Succeeded)
        {
            return written;
        }

        return HeldFileRetry.Run("Save settings", path, heldFileRetry, () => File.Move(fresh, path, overwrite: true), replaced: path);
    }

    /// <summary>The file's text, or null when there is no settings file yet.</summary>
    private static string? ReadText(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
    }
}

/// <summary>
/// The settings that are not about this PC, which an export carries and an import replaces (G36): the four shortcuts,
/// Also copy, the recording's maximum length and whether the pointer is recorded, Start with Windows and Theme; never
/// the stash location, the thumbnail size or the windows' sizes and positions.
/// </summary>
public sealed record TravellingSettings(Shortcuts Shortcuts, bool AlsoCopyCapture, int RecordingMaxSeconds, bool RecordPointer, bool StartWithWindows, Theme Theme)
{
    public static TravellingSettings Of(Settings settings) =>
        new(settings.Shortcuts, settings.AlsoCopyCapture, settings.RecordingMaxSeconds, settings.RecordPointer, settings.StartWithWindows, settings.Theme);

    /// <summary><paramref name="settings"/> with these six values, every other value kept.</summary>
    public Settings AppliedTo(Settings settings) => settings with
    {
        Shortcuts = Shortcuts,
        AlsoCopyCapture = AlsoCopyCapture,
        RecordingMaxSeconds = RecordingMaxSeconds,
        RecordPointer = RecordPointer,
        StartWithWindows = StartWithWindows,
        Theme = Theme,
    };
}

/// <summary>
/// An import's record (State: Import stash…): the extracted folder beside the stash, the name the replaced folder takes,
/// the name of the marker file the extracted folder's root holds until the last step, which tells a folder renamed into
/// place from one that vanished, and the travelling values the last step applies, so finishing needs no archive.
/// </summary>
public sealed record ImportPending(string ExtractedFolder, string ReplacedFolder, string Marker, TravellingSettings Settings);

/// <summary>F5's default shortcuts, each as the text F5 writes it.</summary>
public sealed record Shortcuts
{
    public string Open { get; init; } = "Ctrl+Alt+Space";
    public string Screenshot { get; init; } = "Ctrl+PrtScn";
    public string Record { get; init; } = "Ctrl+Shift+PrtScn";
    public string StashClipboardImage { get; init; } = "Ctrl+Alt+V";
}

public enum Theme
{
    System,
    Light,
    Dark,
}

/// <summary>The grid's three thumbnail sizes (G6); the row height of each is the grid's (L2).</summary>
public enum ThumbnailSize
{
    Small,
    Medium,
    Large,
}

/// <summary>A window's size in device-independent units.</summary>
public sealed record WindowSize(double Width, double Height)
{
    /// <summary>The flyout (L2).</summary>
    public static readonly WindowSize FlyoutDefault = new(460, 640);

    public static readonly WindowSize PinnedDefault = new(880, 620);

    /// <summary>
    /// The smallest the window's client gets, in either state (L2): the editor's rows fit whole at it, the head with the
    /// line naming a missing font, Text's option row wrapped onto three lines, an animated item's filmstrip, the tool row
    /// and the footer with a Save's busy line, with about 100 left for the picture and room for the outline colour
    /// picker's 357 (G20, G21). Measured on an animated item in that state: the picture 106 tall, 79 while the Save runs.
    /// Its width is the flyout's own at its default size, the 460 above less its one-pixel border, so the default flyout
    /// is the narrowest window, never widened. Measure it again when an editor row grows.
    /// </summary>
    public static readonly WindowSize Minimum = new(458, 508);

    /// <summary>This size, or <paramref name="fallback"/> when it cannot size a window: zero, negative, or not a number.</summary>
    public WindowSize UsableOr(WindowSize fallback) =>
        double.IsFinite(Width) && double.IsFinite(Height) && Width >= 1 && Height >= 1 ? this : fallback;
}

/// <summary>A window's top-left corner on the desktop in physical pixels.</summary>
public sealed record WindowPosition(int X, int Y);

/// <summary>
/// The settings in effect, and the failure to name when the file could not be read, parsed or first written.
/// <paramref name="AsSaved"/> is false when the file exists but could not be read or parsed, so the defaults stand in
/// for values the user may have changed. <paramref name="FirstRun"/> is true when there was no settings file at this
/// start (G2's first run).
/// </summary>
public sealed record SettingsLoad(Settings Settings, Failure? Failure, bool AsSaved, bool FirstRun = false);
