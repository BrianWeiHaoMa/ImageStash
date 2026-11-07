using System.Runtime.ExceptionServices;

namespace ImageStash.Media;

/// <summary>
/// What the codec reads through. A read the file refuses part way (a byte-range lock, a failing disk) is kept and
/// reads as the end, because an exception thrown inside the codec's read unwinds through the native decoder, which
/// is not written to be unwound; it is thrown again once the codec has returned.
/// </summary>
internal sealed class CodecStream(FileStream file) : Stream
{
    public Exception? Failure { get; private set; }

    public override bool CanRead => true;

    public override bool CanSeek => true;

    public override bool CanWrite => false;

    public override long Length => Guard(() => file.Length, 0L);

    public override long Position
    {
        get => Guard(() => file.Position, 0L);
        set => Guard(() => file.Position = value, 0L);
    }

    public void ThrowIfFailed()
    {
        if (Failure is not null)
        {
            ExceptionDispatchInfo.Throw(Failure);
        }
    }

    public override int Read(byte[] buffer, int offset, int count) => Guard(() => file.Read(buffer, offset, count), 0);

    public override int Read(Span<byte> buffer)
    {
        if (Failure is not null)
        {
            return 0;
        }

        try
        {
            return file.Read(buffer);
        }
        catch (Exception e)
        {
            Failure = e;
            return 0;
        }
    }

    public override long Seek(long offset, SeekOrigin origin) => Guard(() => file.Seek(offset, origin), 0L);

    public override void Flush()
    {
    }

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            file.Dispose();
        }

        base.Dispose(disposing);
    }

    private T Guard<T>(Func<T> step, T failed)
    {
        if (Failure is not null)
        {
            return failed;
        }

        try
        {
            return step();
        }
        catch (Exception e)
        {
            Failure = e;
            return failed;
        }
    }
}
