namespace ImageStash.Media;

/// <summary>The four formats ImageStash takes (F3).</summary>
public enum ImageFormat
{
    Png,
    Jpeg,
    Gif,
    WebP,
}

/// <summary>F3's file extensions for each format; the first is the one a file named for its format gets.</summary>
public static class ImageFormatExtensions
{
    public static IReadOnlyList<string> Of(ImageFormat format) => format switch
    {
        ImageFormat.Png => [".png"],
        ImageFormat.Jpeg => [".jpg", ".jpeg"],
        ImageFormat.Gif => [".gif"],
        _ => [".webp"],
    };
}

/// <summary>
/// What Media reads from a picture file: its format, its size in pixels, whether it is animated, how many frames it
/// has (one for a still), and the sum of its frame durations in milliseconds (0 for a still).
/// </summary>
public sealed record MediaFacts(ImageFormat Format, int Width, int Height, bool Animated, int FrameCount, long DurationMs);
