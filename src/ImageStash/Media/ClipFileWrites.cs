using System.Security.Cryptography;
using ImageStash.Files;

namespace ImageStash.Media;

/// <summary>
/// A clip's file as the library under Media's GIF encoder writes to it: the library's writes are gathered in memory, in a
/// buffer kept for the clip (R7), and <see cref="WriteGathered"/> writes them to the file in one write, run again while
/// another program holds the file, for the retry G35 gives a held file, so a brief hold only delays the clip; a write
/// still refused carries its failure out. Nothing gathered is left unwritten when a call of the encoder returns, so the
/// file's length is the bytes written so far, and a clip finished without Stop has nothing more to write. Each write that
/// lands is added to the file's content hash, in the order it lands: the file is filled front to back and never sought
/// back in, so the hash asked for after the last write is the file's (<see cref="Sha256"/>).
/// </summary>
internal sealed class ClipFileWrites(FileStream file, string action, TimeSpan heldFileRetry) : Stream
{
    private readonly MemoryStream gathered = new();

    // The content hash of the bytes written to the file so far, each image's one write appended once it succeeded.
    private readonly IncrementalHash hash = ContentHash.WhileWriting();

    public override bool CanRead => false;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => file.Position + gathered.Length;
        set => throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count) => gathered.Write(buffer, offset, count);

    public override void Write(ReadOnlySpan<byte> buffer) => gathered.Write(buffer);

    public override void WriteByte(byte value) => gathered.WriteByte(value);

    // A refused write leaves the file as it was (a write refused by a lock, observed), so it is made again at the same place.
    public void WriteGathered()
    {
        if (gathered.Length == 0)
        {
            return;
        }

        var written = HeldFileRetry.Run(action, file.Name, heldFileRetry, () => file.Write(gathered.GetBuffer(), 0, (int)gathered.Length));
        if (written.Succeeded)
        {
            hash.AppendData(gathered.GetBuffer(), 0, (int)gathered.Length);
        }

        gathered.SetLength(0);
        if (!written.Succeeded)
        {
            throw new RefusedClipWrite(written.Failure);
        }
    }

    /// <summary>The SHA-256 of every byte written to the file, in order; asked once, after the last write.</summary>
    public byte[] Sha256() => hash.GetHashAndReset();

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            hash.Dispose();
        }

        base.Dispose(disposing);
    }
}

/// <summary>A write <see cref="ClipFileWrites"/> found still refused after its retries, carrying its failure out of the library.</summary>
internal sealed class RefusedClipWrite(Failure failure) : IOException(failure.Reason)
{
    public Failure Failure { get; } = failure;
}
