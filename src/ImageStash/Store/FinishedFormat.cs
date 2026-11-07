using ImageStash.Media;

namespace ImageStash.Store;

/// <summary>
/// The format of an edited item's finished picture (F3, N7): the original's, except WebP, which ImageStash does not
/// encode: a GIF for an animation and a PNG for a still. Derived from the original's record alone (Composition: Store),
/// never from the finished picture's facts or file name: a WebP animation trimmed to one frame renders a GIF of one
/// image, which reads back as a still, and its next Save is still a GIF.
/// </summary>
internal static class FinishedFormat
{
    public static ImageFormat Of(MediaFacts original) => original.Format switch
    {
        ImageFormat.WebP => original.Animated ? ImageFormat.Gif : ImageFormat.Png,
        var format => format,
    };

    /// <summary>The extension a finished picture of <paramref name="format"/> is named with (State: File names).</summary>
    public static string Extension(ImageFormat format) => ImageFormatExtensions.Of(format)[0];
}
