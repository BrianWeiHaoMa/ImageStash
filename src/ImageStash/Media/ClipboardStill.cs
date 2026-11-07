using ImageStash.Files;

namespace ImageStash.Media;

/// <summary>
/// A still's two clipboard forms beside its file (Composition, Media; G8): its PNG bytes, which are the file's own when
/// it is a PNG and otherwise its picture encoded as PNG, losslessly; and its full-size frame in the boundary pixel
/// contract, for the bitmap, transparency kept.
/// </summary>
public sealed record ClipboardStill(byte[] Png, MediaFrame Frame)
{
    private const string Action = "Read the picture";

    /// <summary>
    /// The clipboard forms of the still at <paramref name="path"/>, read through Media's boundary, so a file that does
    /// not decode is unreadable and a held one is retried for <paramref name="heldFileRetry"/>; failures name
    /// <paramref name="name"/> and are never thrown (G35).
    /// </summary>
    public static Outcome<ClipboardStill> Read(string path, string name, TimeSpan heldFileRetry)
    {
        var opened = MediaDecoder.Open(path, name, heldFileRetry);
        if (!opened.Succeeded)
        {
            return opened.Failure;
        }

        using var decoder = opened.Value!;
        // A bound of the longer edge gives the frame at full size, never enlarged.
        var frame = decoder.NextFrame(Math.Max(decoder.Width, decoder.Height));
        if (!frame.Succeeded)
        {
            return frame.Failure;
        }

        var png = decoder.Format == ImageFormat.Png ? FileBytes(path, name, heldFileRetry) : PngEncoder.Encode(frame.Value!, name);
        return png.Succeeded ? Outcome<ClipboardStill>.Of(new ClipboardStill(png.Value!, frame.Value!)) : png.Failure;
    }

    /// <summary>The file's bytes as they are, opened read-only and shared as every Media open is (R3).</summary>
    private static Outcome<byte[]> FileBytes(string path, string name, TimeSpan heldFileRetry)
    {
        var read = HeldFileRetry.Run(Action, path, heldFileRetry, () =>
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var bytes = new MemoryStream();
            file.CopyTo(bytes);
            return bytes.ToArray();
        });
        return read.Succeeded ? read : read.Failure with { Subject = name };
    }
}
