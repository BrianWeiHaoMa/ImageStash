namespace ImageStash.Media;

/// <summary>The four formats ImageStash takes (F3).</summary>
public enum ImageFormat
{
    Png,
    Jpeg,
    Gif,
    WebP,
}

/// <summary>
/// F3's file extensions for each format: <see cref="Of"/> those a stored file keeps, the first the one a file named for
/// its format gets; <see cref="Taken"/> every one a file is taken as that format by.
/// </summary>
public static class ImageFormatExtensions
{
    public static IReadOnlyList<string> Of(ImageFormat format) => format switch
    {
        ImageFormat.Png => [".png"],
        ImageFormat.Jpeg => [".jpg", ".jpeg"],
        ImageFormat.Gif => [".gif"],
        _ => [".webp"],
    };

    /// <summary>
    /// The format's own extensions and, for a JPEG, the names browsers give some, which a stored file does not keep: it
    /// is kept under <c>.jpg</c>, a name every chat takes (F3).
    /// </summary>
    public static IReadOnlyList<string> Taken(ImageFormat format) => format == ImageFormat.Jpeg ? [.. Of(format), ".jfif", ".jpe", ".pjpeg"] : Of(format);
}

/// <summary>
/// What Media reads from a picture file: its format, its size in pixels, whether it is animated, how many frames it
/// has (one for a still), and the sum of its frame durations in milliseconds (0 for a still).
/// </summary>
public sealed record MediaFacts(ImageFormat Format, int Width, int Height, bool Animated, int FrameCount, long DurationMs);
