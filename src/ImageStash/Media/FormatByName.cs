namespace ImageStash.Media;

/// <summary>
/// Whether a file is one ImageStash takes, judged by its name alone (F3, N4), so a drop can be counted before release
/// (G11) for paths and streams alike: no bytes are read.
/// </summary>
public static class FormatByName
{
    private static readonly HashSet<string> Videos = [".mp4", ".webm", ".mov"];

    private static readonly HashSet<string> Sounds =
        [".mp3", ".wav", ".ogg", ".oga", ".opus", ".m4a", ".aac", ".flac", ".wma", ".aif", ".aiff", ".mid", ".midi", ".amr"];

    /// <summary>The format <paramref name="fileName"/>'s extension names, compared ignoring case, or the type to name in its refusal.</summary>
    public static FormatVerdict Judge(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        foreach (var format in Enum.GetValues<ImageFormat>())
        {
            if (ImageFormatExtensions.Of(format).Contains(extension))
            {
                return new FormatVerdict(format, null);
            }
        }

        var type = extension switch
        {
            "" or "." => "a file without an extension",
            _ when Videos.Contains(extension) => "a video",
            _ when Sounds.Contains(extension) => "a sound file",
            // An Internet shortcut, the file a link dragged out of a browser becomes: a link, refused and never fetched (N2).
            ".url" => "a link",
            _ => $"a {extension} file",
        };
        return new FormatVerdict(null, type);
    }
}

/// <summary>
/// A name's verdict: the <see cref="Format"/> it is taken as, or, for a file ImageStash does not take, the
/// <see cref="UnsupportedType"/> its refusal names ("clip.mp4 is a video").
/// </summary>
public sealed record FormatVerdict(ImageFormat? Format, string? UnsupportedType)
{
    public bool Supported => Format is not null;
}
