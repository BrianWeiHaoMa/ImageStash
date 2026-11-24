using ImageStash.Files;
using ImageStash.Media;
using ImageStash.Seams;
using ImageStash.Store;

namespace ImageStash.Window;

/// <summary>
/// An imported file whose name has no extension, which says nothing of what it holds, so the drop card counts it as one
/// to check (F3, G11): what its first bytes begin as decides, read before anything of it reaches <c>.work\</c>. A file on
/// this PC is looked at where it lies, read-only (R3), and then copied in; a browser's picture, whose contents open once,
/// is opened once, the bytes read to look at it written first and then the rest (R2). It is staged under its format's
/// first extension, <c>.jpg</c> for a JPEG, which the stored file keeps; one that begins as none of the four formats leaves
/// nothing behind and is skipped as a file without an extension (G12).
/// </summary>
internal static class FileWithoutExtension
{
    /// <summary>
    /// <paramref name="file"/> staged under the extension of the format its content begins as, or the failure that kept
    /// it from being looked at or copied in; null when it begins as none of the four formats.
    /// </summary>
    public static async Task<Outcome<string>?> Stage(StashStore store, DroppedItem file, PictureImport pictures, TimeSpan heldFileRetry)
    {
        if (file.LocalPath is { } path)
        {
            var looked = await Task.Run(() => FormatByContent.Look(path, file.Name, PictureImport.Copy, heldFileRetry));
            if (!looked.Succeeded)
            {
                return looked.Failure;
            }

            return looked.Value is { } format ? await store.Stage(path, ImageFormatExtensions.Of(format)[0]) : null;
        }

        Stream? unclosed = null;
        try
        {
            var head = new byte[FormatByContent.HeadLength];
            // Opened and read on a pool thread, as every stream an import copies in is: a browser may be slow to send it.
            var count = await Task.Run(async () =>
            {
                unclosed = await file.OpenRead!();
                return await unclosed.ReadAtLeastAsync(head, head.Length, throwOnEndOfStream: false);
            });
            var source = unclosed!;
            unclosed = null;
            if (FormatByContent.Of(head.AsSpan(0, count)) is not { } format)
            {
                await source.DisposeAsync();
                return null;
            }

            // The copy closes the stream with what it reads, a failure to close failing the copy as for any stream; a copy
            // refused before it began, as while a Move is asked, never took it.
            var taken = false;
            var staged = await pictures.StageStream(store, file.Name, ImageFormatExtensions.Of(format)[0], () =>
            {
                taken = true;
                return Task.FromResult<Stream>(new HeadThenRest(head[..count], source));
            });
            if (!taken)
            {
                await source.DisposeAsync();
            }

            return staged;
        }
        catch (Exception e)
        {
            // The stream is another program's and opens once, so there is nothing to retry (G35): opening, reading or
            // closing it failed, this file's failure.
            await CloseAfterFailure(unclosed);
            return new Failure(PictureImport.Copy, file.Name, HeldFileRetry.ReasonFor(e));
        }
    }

    /// <summary>A stream that broke while it was read, let go; a failure closing it adds nothing to the one already named.</summary>
    private static async Task CloseAfterFailure(Stream? broken)
    {
        try
        {
            if (broken is not null)
            {
                await broken.DisposeAsync();
            }
        }
        catch (Exception)
        {
        }
    }

    /// <summary>
    /// The bytes already read from a stream that opens once, then the rest of that stream, read on from where they ended;
    /// closing it closes that stream.
    /// </summary>
    private sealed class HeadThenRest(byte[] head, Stream rest) : Stream
    {
        private int given;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer) => given < head.Length ? FromHead(buffer) : rest.Read(buffer);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            given < head.Length ? ValueTask.FromResult(FromHead(buffer.Span)) : rest.ReadAsync(buffer, cancellationToken);

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                rest.Dispose();
            }

            base.Dispose(disposing);
        }

        private int FromHead(Span<byte> buffer)
        {
            var count = Math.Min(buffer.Length, head.Length - given);
            head.AsSpan(given, count).CopyTo(buffer);
            given += count;
            return count;
        }
    }
}
