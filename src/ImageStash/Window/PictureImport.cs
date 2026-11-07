using ImageStash.Media;
using ImageStash.Store;

namespace ImageStash.Window;

/// <summary>
/// The picture a paste or Stash clipboard image found on the clipboard, to be saved as one still PNG item named
/// <paramref name="Name"/> (F12): the PNG bytes a program put there, as they are, or else its bitmap as one frame (G13).
/// </summary>
public sealed record PastedPicture(string Name, byte[]? Png, MediaFrame? Frame);

/// <summary>
/// One picture from the clipboard made an item, the one home of these steps for the paste's batch and for Stash
/// clipboard image (G13, F3, R4), and from its facts on for a screenshot and a recording's clip (G14, G15): its PNG
/// bytes as they are, or its frame encoded by Media's one PNG encoder, staged into a <c>.work\</c> path Store allocated
/// (R2); its facts read; bytes a program labelled PNG that are another format re-encoded from their first frame, so the
/// item is a still PNG whatever the label; then committed through Store's import commit without G12's duplicate
/// question (State: the import bullet, G28), its three outcomes answered as <see cref="ImportedFile.FromCommit"/> maps
/// them. A failed discard of a staged copy is named through <paramref name="named"/> at once, whatever becomes of the
/// picture.
/// </summary>
public sealed class PictureImport(TimeSpan heldFileRetry, Action<Failure> named)
{
    private const string Copy = "Copy into the stash";

    /// <summary>
    /// Saves <paramref name="picture"/> as one still PNG item, even when its bytes equal an item's original, getting
    /// <paramref name="tags"/> in its own insert (G11); what became of it, added only once every step
    /// has completed (R9).
    /// </summary>
    public async Task<ImportedFile> Save(StashStore store, PastedPicture picture, IReadOnlyList<string> tags)
    {
        var png = picture.Png is { } bytes ? Outcome<byte[]>.Of(bytes) : await Task.Run(() => PngEncoder.Encode(picture.Frame!, picture.Name));
        var staged = png.Succeeded ? await StagePng(store, picture.Name, png.Value!) : png.Failure;
        if (!staged.Succeeded)
        {
            return new ImportedFile(picture.Name, ImportResultKind.Failed, Failure: staged.Failure);
        }

        var read = await Task.Run(() => MediaDecoder.ReadFacts(staged.Value!, picture.Name, heldFileRetry));
        if (read.Succeeded && read.Value!.Facts.Format != ImageFormat.Png)
        {
            // GIF or WebP bytes a program labelled PNG: ImportCommit would keep them as an animated .gif or .webp item.
            var first = await Task.Run(() => FirstFrameAsPng(staged.Value!, picture.Name));
            // Named at once, whatever becomes of the picture; the next start's clearing of .work\ removes the copy.
            if ((await store.Discard(staged.Value!)).Failure is { } leftOver)
            {
                named(leftOver);
            }

            staged = first.Succeeded ? await StagePng(store, picture.Name, first.Value!) : first.Failure;
            if (!staged.Succeeded)
            {
                return Unread(picture.Name, staged.Failure, null);
            }

            read = await Task.Run(() => MediaDecoder.ReadFacts(staged.Value!, picture.Name, heldFileRetry));
        }

        return await Commit(store, picture.Name, staged.Value!, read, tags);
    }

    /// <summary>
    /// Saves the picture a capture wrote whole at <paramref name="workPath"/>, a <c>.work\</c> path Store allocated, as
    /// one item named <paramref name="name"/>, even when its bytes equal an item's original and with no tag (G11, G12);
    /// what became of it, as <see cref="Save"/> answers. The file is Media's own PNG or GIF, its format read from its
    /// bytes, so nothing is re-encoded.
    /// </summary>
    public async Task<ImportedFile> SaveStaged(StashStore store, string name, string workPath) =>
        await Commit(store, name, workPath, await Task.Run(() => MediaDecoder.ReadFacts(workPath, name, heldFileRetry)), tags: []);

    /// <summary>
    /// The staged picture's facts committed through Store's import commit, getting <paramref name="tags"/> in its own
    /// insert; or, when they could not be read, its staged copy discarded and the picture unread.
    /// </summary>
    private async Task<ImportedFile> Commit(StashStore store, string name, string staged, Outcome<MediaRead> read, IReadOnlyList<string> tags)
    {
        if (!read.Succeeded)
        {
            var discarded = await store.Discard(staged);
            return Unread(name, read.Failure, discarded.Failure);
        }

        var candidate = new ImportCandidate(staged, name, read.Value!.Facts, read.Value.Sha256, tags, SavedEvenIfInStash: true);
        return ImportedFile.FromCommit(name, await store.CommitImport(candidate));
    }

    /// <summary>
    /// A picture Media could not read: unreadable by name, with the discard of its staged copy when that failed, or else
    /// named by the failure that kept it from being read, a failed discard then named on its own (G35).
    /// </summary>
    internal ImportedFile Unread(string name, Failure failure, Failure? discarded)
    {
        if (MediaDecoder.IsUnreadable(failure))
        {
            return new ImportedFile(name, ImportResultKind.Unreadable, Failure: discarded);
        }

        if (discarded is not null)
        {
            named(discarded);
        }

        return new ImportedFile(name, ImportResultKind.Failed, Failure: failure);
    }

    /// <summary>
    /// A file without a path, such as a browser's picture, or a pasted picture's bytes, written from its stream into a
    /// <c>.work\</c> path Store allocated, on a pool thread; one that fails part way leaves nothing behind, or, when its
    /// partial copy cannot be deleted, names that through <c>named</c> and leaves it to the next start's clearing of
    /// <c>.work\</c>. Its failure is this file's alone, and the batch goes on.
    /// </summary>
    internal Task<Outcome<string>> StageStream(StashStore store, string name, string extension, Func<Task<Stream>> openRead) =>
        store.WriteWork(extension, openRead, Copy, name, named);

    /// <summary>The first frame of the picture at <paramref name="path"/>, at full size, encoded by Media's one PNG encoder (R4).</summary>
    private Outcome<byte[]> FirstFrameAsPng(string path, string name)
    {
        var opened = MediaDecoder.Open(path, name, heldFileRetry);
        if (!opened.Succeeded)
        {
            return opened.Failure;
        }

        using var decoder = opened.Value!;
        var frame = decoder.NextFrame(Math.Max(decoder.Width, decoder.Height));
        return frame.Succeeded ? PngEncoder.Encode(frame.Value!, name) : frame.Failure;
    }

    /// <summary>A pasted picture's PNG bytes written into a <c>.work\</c> path Store allocated (R2).</summary>
    private Task<Outcome<string>> StagePng(StashStore store, string name, byte[] png) =>
        StageStream(store, name, ".png", () => Task.FromResult<Stream>(new MemoryStream(png, writable: false)));
}
