using ImageStash.Files;
using SkiaSharp;

namespace ImageStash.Media;

/// <summary>
/// What a picture file's header and frame table say, read without decoding a pixel or hashing a byte: the facts
/// <see cref="MediaDecoder.ReadFacts"/> answers for a whole file (its format, its upright size with the encoded origin
/// applied as the decoder applies it, whether it is animated, its frame count and the sum of its frames' durations), at
/// the cost of a header, for a reader that looks again at many files already taken in (G31). It does not judge whether
/// the file is whole: a file cut short is refused as unreadable wherever it is decoded (G35). The open is read-only and
/// shared as every Media open is (R3); a file held past the retries is a failure naming it, and bytes the codec does not
/// read as one of F3's formats are unreadable; nothing is thrown (G35).
/// </summary>
public static class PictureHeader
{
    private const string Action = "Read the picture";

    /// <summary>The facts of the file at <paramref name="path"/>; failures name <paramref name="name"/>.</summary>
    public static Outcome<MediaFacts> Read(string path, string name, TimeSpan heldFileRetry)
    {
        var read = HeldFileRetry.Run(Action, path, heldFileRetry, () => Facts(path));
        if (!read.Succeeded)
        {
            return read.Failure with { Subject = name };
        }

        return read.Value is { } facts ? Outcome<MediaFacts>.Of(facts) : new Failure(Action, name, MediaDecoder.UnreadableReason);
    }

    /// <summary>The facts, or null for bytes the codec rejects; a file error throws, for the held-file policy to judge.</summary>
    private static MediaFacts? Facts(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096);
        var guarded = new CodecStream(file);
        SKCodec? codec = null;
        MediaFacts? facts = null;
        try
        {
            // The codec owns the stream it is given and closes it when it is disposed.
            codec = SKCodec.Create(guarded);
            if (codec is not null && MediaDecoder.Source.FormatOf(codec.EncodedFormat) is { } format)
            {
                var origin = EncodedOrientation.Of(codec);
                var (width, height) = EncodedOrientation.SwapsSides(origin) ? (codec.Info.Height, codec.Info.Width) : (codec.Info.Width, codec.Info.Height);
                // F3: an animated PNG is handled as a still, as the decoder handles it.
                var frames = format == ImageFormat.Png ? 1 : Math.Max(1, codec.FrameCount);
                var durations = frames > 1 ? codec.FrameInfo.Take(frames).Sum(frame => (long)frame.Duration) : 0;
                facts = new MediaFacts(format, width, height, frames > 1, frames, durations);
            }
        }
        catch (Exception)
        {
            // A codec that throws, or a frame table this PC cannot hold, has rejected the bytes; a refused read is thrown below.
            facts = null;
        }
        finally
        {
            codec?.Dispose();
        }

        guarded.ThrowIfFailed();
        return facts;
    }
}
