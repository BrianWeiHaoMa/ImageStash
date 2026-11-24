using ImageStash.Files;

namespace ImageStash.Media;

/// <summary>
/// Which of the four formats a file's content begins as, for a file whose name has no extension and so says nothing
/// (F3): only its first bytes are read, told by the signatures every decode is told by (<see cref="TruncationCheck"/>),
/// so a file that begins as a format and is cut short is still taken as that format and then refused as unreadable
/// (G35), and nothing is decoded here.
/// </summary>
public static class FormatByContent
{
    /// <summary>How many first bytes tell the four formats apart.</summary>
    public const int HeadLength = TruncationCheck.HeadLength;

    /// <summary>The format <paramref name="head"/>, a file's first bytes, begins as; null when it begins as none.</summary>
    public static ImageFormat? Of(ReadOnlySpan<byte> head) => TruncationCheck.Begins(head);

    /// <summary>
    /// The format the file at <paramref name="path"/> begins as, or null: opened where it lies, read-only and sharing
    /// read, write and delete, so its owner's program is never blocked and nothing of it is written (R3), and retried
    /// while another program holds it, for <paramref name="heldFileRetry"/> (G35). A failure names
    /// <paramref name="action"/> and <paramref name="name"/>.
    /// </summary>
    public static Outcome<ImageFormat?> Look(string path, string name, string action, TimeSpan heldFileRetry)
    {
        var looked = HeldFileRetry.Run(action, path, heldFileRetry, () =>
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, HeadLength);
            Span<byte> head = stackalloc byte[HeadLength];
            return Of(head[..file.ReadAtLeast(head, head.Length, throwOnEndOfStream: false)]);
        });
        return looked.Succeeded ? looked : looked.Failure with { Subject = name };
    }
}
