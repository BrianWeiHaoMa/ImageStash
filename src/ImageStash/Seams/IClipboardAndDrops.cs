using Avalonia.Controls;
using Avalonia.Input;
using ImageStash.Media;

namespace ImageStash.Seams;

/// <summary>
/// The platform's clipboard and drag and drop (F13): what a drop onto the stash window carries, the clipboard's
/// write, the same write again after a Save while the clipboard is unchanged since, its two reads, and a drag of files
/// out of the window. Only <see cref="Read"/>, which only the paste calls, and <see cref="ReadImage"/>, which only
/// Stash clipboard image calls, read the clipboard (N9, R5).
/// </summary>
public interface IClipboardAndDrops
{
    /// <summary>
    /// What <paramref name="data"/>, dragged over or dropped onto the window, carries, without reading any file's
    /// bytes: each file by its local path, or, for a file a browser or chat hands over without one, by its name with
    /// a way to read it; each folder by its path; and, when it carries no file, whether it is only a link (N2). Called
    /// on the UI thread while the drag event is handled; the result is disposed once its files have been read.
    /// </summary>
    DropContents ReadDrop(IDataTransfer data);

    /// <summary>
    /// Replaces the clipboard's contents with <paramref name="content"/>, every format rendered now so it outlives the
    /// window and ImageStash itself, as a copy, never a move, for a paste into a folder (G8, G29). Called on the UI
    /// thread with the stash window as <paramref name="owner"/>; the waiting happens elsewhere, so the window keeps
    /// answering. A clipboard another program holds open is retried for <paramref name="heldClipboardRetry"/> before it
    /// fails with <see cref="HeldClipboard.Reason"/> (E3); a file no longer there once the clipboard opens, or another
    /// file put at its path meanwhile, is refused by its name; any failure leaves nothing of this write on the clipboard.
    /// </summary>
    Task<Outcome> Write(TopLevel owner, ClipboardContent content, TimeSpan heldClipboardRetry);

    /// <summary>
    /// Replaces the clipboard's contents with <paramref name="text"/>, a text box's own copy or cut, so that a clipboard
    /// another program holds open is retried for <paramref name="heldClipboardRetry"/> and then named under
    /// <paramref name="action"/> (E3, G35). Called on the UI thread with the stash window as <paramref name="owner"/>.
    /// </summary>
    Task<Outcome> WriteText(TopLevel owner, string action, string text, TimeSpan heldClipboardRetry);

    /// <summary>
    /// The identities of the items whose finished pictures this seam's latest completed write put on the clipboard
    /// (<see cref="ClipboardContent.Items"/>), while that write may still be what the clipboard holds; empty before any,
    /// and once a write has begun to replace it.
    /// </summary>
    IReadOnlyList<long> LastWritten { get; }

    /// <summary>
    /// Forgets the latest write, so <see cref="LastWritten"/> is empty and no <see cref="WriteAgain"/> writes from it
    /// until the next write; the clipboard keeps what it holds.
    /// </summary>
    void ForgetWritten();

    /// <summary>
    /// G26's refresh: replaces the clipboard's contents with <paramref name="content"/> as <see cref="Write"/> does, with
    /// the owner of that latest write, only when, with the clipboard held open, nothing has changed it since that write
    /// and its identities include <paramref name="savedItem"/>: true when written; false, writing nothing, when anything
    /// else has changed the clipboard since or the item was not among those written, which is no failure. It reads no
    /// clipboard data (N9, R5). May be called from any thread; a clipboard another program holds open is retried for
    /// <paramref name="heldClipboardRetry"/> before it fails as <see cref="Write"/> fails, and a file refused as
    /// <see cref="Write"/> refuses it, before anything on the clipboard is replaced.
    /// </summary>
    Task<Outcome<bool>> WriteAgain(long savedItem, ClipboardContent content, TimeSpan heldClipboardRetry);

    /// <summary>
    /// The clipboard read once, for Paste image and Ctrl+V and nothing else (N9, R5): its files when it holds any,
    /// else its image (the PNG bytes a program put there, as they are, else its bitmap as one frame), else its text with
    /// whether that is only a web address (N2), else nothing; no file's bytes are read. May be called from any thread; the
    /// waiting happens elsewhere, so the window keeps answering. A clipboard another program holds open is retried for
    /// <paramref name="heldClipboardRetry"/> before it fails with <see cref="HeldClipboard.Reason"/> (E3); a bitmap
    /// that cannot be read is a failure naming why, never an exception (G35).
    /// </summary>
    Task<Outcome<ClipboardReading>> Read(TimeSpan heldClipboardRetry);

    /// <summary>
    /// The clipboard read once, for Stash clipboard image and nothing else (N9, R5): its image only, the PNG bytes a
    /// program put there, as they are, else its bitmap as one frame, else null; its files and its text are never
    /// asked for (G13: files on the clipboard do not count). May be called from any thread; the waiting happens
    /// elsewhere. A clipboard another program holds open is retried for <paramref name="heldClipboardRetry"/> before it
    /// fails with <see cref="HeldClipboard.Reason"/> (E3); a bitmap that cannot be read is a failure naming why, never
    /// an exception (G35).
    /// </summary>
    Task<Outcome<ClipboardImage?>> ReadImage(TimeSpan heldClipboardRetry);

    /// <summary>
    /// Drags <paramref name="filePaths"/>, files of one folder, out of <paramref name="owner"/> as a file drop that
    /// offers a copy and nothing else, so a destination never takes a stash file away (G9, G29). Called on the UI
    /// thread while the left button of <paramref name="press"/> is still down; it returns once the drag has ended:
    /// true when a destination took the drop as a copy, false when it was cancelled or nothing took it, or a failure
    /// naming the file that could not be handed over or why the drag could not run, never an exception (G35).
    /// </summary>
    Task<Outcome<bool>> StartDrag(TopLevel owner, PointerPressedEventArgs press, IReadOnlyList<string> filePaths);
}

/// <summary>
/// What one clipboard write carries: the finished pictures' files, never empty, and, for a single still, its PNG bytes
/// and its full-size frame for the bitmap, both present or both null; and the identities of the items they are, which
/// the seam remembers for a refresh (G26). The seam writes what it is given; which items get a bitmap is the handoff's
/// rule (G8).
/// </summary>
public sealed record ClipboardContent(IReadOnlyList<string> FilePaths, byte[]? Png, MediaFrame? Bitmap, IReadOnlyList<long> Items);

/// <summary>
/// What one read of the clipboard found, at most one of these: its files and folders in the order they came; the PNG
/// bytes a program put there, not decoded; its bitmap as one still frame; or its text, with whether that is only a web
/// address. None of them means it holds no file, no picture and no words (G13).
/// </summary>
public sealed record ClipboardReading(IReadOnlyList<DroppedItem> Files, byte[]? Png, MediaFrame? Image, bool OnlyLink, string? Text = null);

/// <summary>The clipboard's image as the image-only read found it: the PNG bytes a program put there, not decoded, or else its bitmap as one still frame.</summary>
public sealed record ClipboardImage(byte[]? Png, MediaFrame? Frame);

/// <summary>How long a clipboard another program holds open is retried, and the words for one still held after it (E3, G35).</summary>
public static class HeldClipboard
{
    /// <summary>
    /// Another program holds the clipboard for the milliseconds it takes to copy; two seconds covers that without a
    /// press seeming to hang (L2).
    /// </summary>
    public static readonly TimeSpan Retry = TimeSpan.FromSeconds(2);

    public const string Reason = "another program is holding the clipboard open";
}

/// <summary>
/// A file or folder a drop carries, as the user knows it by <paramref name="Name"/>: at <paramref name="LocalPath"/>,
/// or, without a path, readable once through <paramref name="OpenRead"/>.
/// </summary>
public sealed record DroppedItem(string Name, string? LocalPath, Func<Task<Stream>>? OpenRead, bool IsFolder);

/// <summary>
/// What one drop carries: its files and folders in the order they came; whether it is only a link; and whether it
/// offered files that could not be had. Disposing it lets the source go of what it handed over.
/// </summary>
public sealed class DropContents(IReadOnlyList<DroppedItem> items, bool onlyLink, bool filesUnavailable, IEnumerable<IDisposable> held) : IDisposable
{
    private readonly List<IDisposable> held = [.. held];

    public IReadOnlyList<DroppedItem> Items { get; } = items;

    public bool OnlyLink { get; } = onlyLink;

    public bool FilesUnavailable { get; } = filesUnavailable;

    public void Dispose()
    {
        foreach (var item in held)
        {
            item.Dispose();
        }

        held.Clear();
    }
}
