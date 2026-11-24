namespace ImageStash.Tests.Support;

/// <summary>A browser's stream that breaks after its first bytes.</summary>
public sealed class FailingStream(byte[] bytes) : MemoryStream(bytes)
{
    public const string Reason = "the browser stopped sending it";

    public override int Read(byte[] buffer, int offset, int count) =>
        Position > 0 ? throw new IOException(Reason) : base.Read(buffer, offset, Math.Min(count, 16));

    public override int Read(Span<byte> buffer) =>
        Position > 0 ? throw new IOException(Reason) : base.Read(buffer[..Math.Min(buffer.Length, 16)]);

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Read(buffer.Span));

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        Task.FromResult(Read(buffer, offset, count));
}
