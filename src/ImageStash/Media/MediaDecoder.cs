using System.Runtime.InteropServices;
using System.Security.Cryptography;
using ImageStash.Files;
using SkiaSharp;

namespace ImageStash.Media;

/// <summary>
/// One picture file opened for decoding (Composition, Media): its frames one at a time with their durations, in the
/// boundary pixel contract, scaled to a bound on request (R7). Every way the bytes fail to decode, a file cut short
/// included, comes back as unreadable naming the file, except a JPEG whose scan data stops at a marker, which the codec
/// completes and reports as a success, so it is read (G35, S11); a file still held after the retries is a failure that
/// says so; neither throws across the boundary (G35). Every open is read-only and shares read, write and delete, so a
/// decode in progress never blocks the store's renames and recycling, and a scanner reading the file never blocks the
/// decode (R3). A JPEG's or WebP's encoded origin is applied as each frame is handed over, so its size and every frame
/// are upright, while the frames are composed in the codec's own grid and the file's bytes stay as they came (F3).
/// </summary>
public sealed class MediaDecoder : IDisposable
{
    /// <summary>The reason an undecodable or truncated file is refused with (G35); see <see cref="IsUnreadable"/>.</summary>
    public const string UnreadableReason = "it is unreadable";

    /// <summary>
    /// The reason a decoder opened hashing fails with when a read refused part way sends it back to the path and the file
    /// there is not the one it hashed, its frames would come from two files, or when <see cref="OpenOriginal"/> finds at
    /// the path a file that is not the item's original (G35).
    /// </summary>
    public const string ChangedReason = "another file took its place while it was being read";

    private const string Action = "Read the picture";

    private static readonly SKColorSpace Srgb = SKColorSpace.CreateSrgb();

    private static readonly SKSamplingOptions Sampling = new(SKFilterMode.Linear, SKMipmapMode.Linear);

    private readonly string path;
    private readonly string name;
    private readonly TimeSpan heldFileRetry;
    private readonly SKCodecFrameInfo[] frames;
    private readonly SKBitmap canvas;
    private readonly SKEncodedOrigin origin;
    private Source source;
    private int next;
    private int inCanvas = -1;
    private SKBitmap? saved;
    private int savedFrame = -1;
    private SKBitmap? premultiplied;
    private SKBitmap? upright;

    private MediaDecoder(string path, string name, TimeSpan heldFileRetry, Source source, byte[]? sha256, SKBitmap canvas)
    {
        this.path = path;
        this.name = name;
        this.heldFileRetry = heldFileRetry;
        this.source = source;
        Sha256 = sha256;
        this.canvas = canvas;
        frames = source.Codec.FrameInfo;
        origin = EncodedOrientation.Of(source.Codec);
        // F3: an animated PNG is handled as a still, whatever a later codec makes of its extra frames.
        FrameCount = source.Format == ImageFormat.Png ? 1 : Math.Max(1, source.Codec.FrameCount);
    }

    public ImageFormat Format => source.Format;

    /// <summary>The upright picture's width: the codec's height for a file its origin turns a quarter.</summary>
    public int Width => EncodedOrientation.SwapsSides(origin) ? canvas.Height : canvas.Width;

    public int Height => EncodedOrientation.SwapsSides(origin) ? canvas.Width : canvas.Height;

    /// <summary>One for a still: a PNG, a JPEG, or a GIF or WebP with a single frame.</summary>
    public int FrameCount { get; }

    /// <summary>
    /// Opened hashing, the SHA-256 of the file every frame is decoded from, taken over the handle the codec reads, so a
    /// rename that puts another file at the path changes nothing it answers (G12); null when opened without hashing.
    /// </summary>
    public byte[]? Sha256 { get; }

    /// <summary>Whether <paramref name="failure"/> is the refusal of an undecodable or truncated file, rather than a failure to read it.</summary>
    public static bool IsUnreadable(Failure failure) => failure.Reason == UnreadableReason;

    /// <summary>The content hash (Composition: Media): the SHA-256 of the bytes from <paramref name="stream"/>'s position to its end, read through it. A read error throws.</summary>
    public static byte[] Sha256Of(Stream stream) => SHA256.HashData(stream);

    /// <summary>
    /// The facts of the file at <paramref name="path"/> and the SHA-256 of its bytes, from one open (G12). Every frame
    /// is decoded once, one at a time, so a file whose body does not decode is refused here, not by a later frame
    /// (G35, E4). Failures name <paramref name="name"/>, the name the user knows the file by.
    /// </summary>
    public static Outcome<MediaRead> ReadFacts(string path, string name, TimeSpan heldFileRetry)
    {
        var read = Read(path, name, heldFileRetry, out var sha256);
        return read.Succeeded ? Outcome<MediaRead>.Of(new MediaRead(read.Value!, sha256!)) : read.Failure;
    }

    /// <summary>
    /// The SHA-256 of the file at <paramref name="path"/> and, read as <see cref="ReadFacts"/> reads it, its facts; or the
    /// hash alone, with no facts, for bytes <see cref="ReadFacts"/> refuses as unreadable, so a caller that must record
    /// what another program wrote has their hash whether or not they decode (Composition: Media, the content hash). A
    /// file that cannot be opened or read after the retries is a failure naming <paramref name="name"/>, as there.
    /// </summary>
    public static Outcome<ReadPicture> ReadPicture(string path, string name, TimeSpan heldFileRetry)
    {
        var read = Read(path, name, heldFileRetry, out var sha256);
        if (read.Succeeded)
        {
            return Outcome<ReadPicture>.Of(new ReadPicture(sha256!, read.Value!));
        }

        return sha256 is not null && IsUnreadable(read.Failure) ? Outcome<ReadPicture>.Of(new ReadPicture(sha256, null)) : read.Failure;
    }

    /// <summary>
    /// The facts of the file, every frame decoded once, one at a time, so a body that does not decode is refused here
    /// (G35, E4); <paramref name="sha256"/> is the hash of the bytes the codec read, whether or not they decode, and null
    /// when the file could not be opened.
    /// </summary>
    private static Outcome<MediaFacts> Read(string path, string name, TimeSpan heldFileRetry, out byte[]? sha256)
    {
        var opened = Open(path, name, heldFileRetry, hash: true, out sha256);
        if (!opened.Succeeded)
        {
            return opened.Failure;
        }

        using var decoder = opened.Value!;
        long duration = 0;
        for (var frame = 0; frame < decoder.FrameCount; frame++)
        {
            var decoded = decoder.Decode(frame);
            if (!decoded.Succeeded)
            {
                return decoded.Failure;
            }

            duration += decoder.Duration(frame);
        }

        return Outcome<MediaFacts>.Of(new MediaFacts(decoder.Format, decoder.Width, decoder.Height, decoder.FrameCount > 1, decoder.FrameCount, duration));
    }

    /// <summary>Opens the file at <paramref name="path"/> for its frames; failures name <paramref name="name"/>.</summary>
    public static Outcome<MediaDecoder> Open(string path, string name, TimeSpan heldFileRetry) => Open(path, name, heldFileRetry, hash: false);

    /// <summary>
    /// Opens an item's original at <paramref name="path"/> for its frames, hashing, only when its bytes are those whose
    /// SHA-256 the store recorded, <paramref name="originalSha256"/>: renames that put another file at the path while the
    /// open waited on a held file, or after the path was read, fail it with <see cref="ChangedReason"/>, never decoded.
    /// </summary>
    public static Outcome<MediaDecoder> OpenOriginal(string path, string name, TimeSpan heldFileRetry, byte[] originalSha256)
    {
        var opened = Open(path, name, heldFileRetry, hash: true);
        if (opened.Value is { } decoder && !decoder.Sha256.AsSpan().SequenceEqual(originalSha256))
        {
            decoder.Dispose();
            return new Failure(Action, name, ChangedReason);
        }

        return opened;
    }

    /// <summary>
    /// Opens the file at <paramref name="path"/> for its frames, answering <see cref="Sha256"/> when
    /// <paramref name="hash"/> asks: every frame is then decoded from the bytes hashed, and a frame that could only be
    /// read from another file fails with <see cref="ChangedReason"/>. Failures name <paramref name="name"/>.
    /// </summary>
    public static Outcome<MediaDecoder> Open(string path, string name, TimeSpan heldFileRetry, bool hash) => Open(path, name, heldFileRetry, hash, out _);

    /// <summary>As the public open, answering in <paramref name="hashed"/> the hash of the bytes read, decodable or not, or null when the file could not be read.</summary>
    private static Outcome<MediaDecoder> Open(string path, string name, TimeSpan heldFileRetry, bool hash, out byte[]? hashed)
    {
        byte[]? sha256 = null;
        var opened = HeldFileRetry.Run(Action, path, heldFileRetry, () => Source.Open(path, hash, out sha256));
        hashed = opened.Succeeded ? sha256 : null;
        if (!opened.Succeeded)
        {
            return opened.Failure with { Subject = name };
        }

        if (opened.Value is not { } source)
        {
            return Unreadable(name);
        }

        // One full-size buffer for every frame (R7). A size it cannot hold is a file this PC cannot decode (E4); SkiaSharp
        // refuses a bitmap of 2 GiB or more whatever the memory.
        if (Allocate(new SKImageInfo(source.Codec.Info.Width, source.Codec.Info.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul, Srgb)) is not { } canvas)
        {
            source.Dispose();
            return Unreadable(name);
        }

        try
        {
            return Outcome<MediaDecoder>.Of(new MediaDecoder(path, name, heldFileRetry, source, sha256, canvas));
        }
        catch (Exception)
        {
            // Reading the frame table of a file with very many frames can exhaust memory: one this PC cannot decode.
            canvas.Dispose();
            source.Dispose();
            return Unreadable(name);
        }
    }

    /// <summary>
    /// The next frame, scaled to fit within <paramref name="bound"/> pixels on its longer edge at the picture's aspect
    /// ratio, never enlarged, so a bound at least the picture's size gives it as it is; null after the last frame.
    /// </summary>
    public Outcome<MediaFrame?> NextFrame(int bound)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(bound, 1);
        if (next == FrameCount)
        {
            return Outcome<MediaFrame?>.Of(null);
        }

        var decoded = Decode(next);
        if (!decoded.Succeeded)
        {
            return decoded.Failure;
        }

        return CopyOut(Duration(next++), bound);
    }

    /// <summary>
    /// Reads past the next frame without handing it over, so a frame further on is reached by decoding forward with
    /// nothing copied out (R7); after the last frame it does nothing.
    /// </summary>
    public Outcome Skip()
    {
        if (next == FrameCount)
        {
            return Outcome.Done;
        }

        var decoded = Decode(next);
        if (!decoded.Succeeded)
        {
            return decoded;
        }

        next++;
        return Outcome.Done;
    }

    /// <summary>
    /// Starts the frames again from the first, read from what this open already holds, so an animation loops without
    /// opening its file again: a loop then goes on through a rename or a recycle of the file, which the open shares (R7, G29).
    /// </summary>
    public void Rewind()
    {
        next = 0;
        inCanvas = -1;
        savedFrame = -1;
    }

    public void Dispose()
    {
        source.Dispose();
        canvas.Dispose();
        saved?.Dispose();
        premultiplied?.Dispose();
        upright?.Dispose();
    }

    private static Failure Unreadable(string name) => new(Action, name, UnreadableReason);

    private int Duration(int frame) => FrameCount > 1 && frame < frames.Length ? frames[frame].Duration : 0;

    private SKCodecAnimationDisposalMethod Disposal(int frame) =>
        frame >= 0 && frame < frames.Length ? frames[frame].DisposalMethod : SKCodecAnimationDisposalMethod.Keep;

    /// <summary>
    /// Decodes <paramref name="frame"/> into the canvas, again from a fresh open while a read part way is refused because
    /// the file is held. Opened hashing, it goes on only from a fresh open of the bytes it hashed; another file there is
    /// failed at once, never decoded and never retried, since waiting does not bring back the file it hashed.
    /// </summary>
    private Outcome Decode(int frame)
    {
        var decoded = HeldFileRetry.Run(Action, path, heldFileRetry, () =>
        {
            if (source.Stream.Failure is not null)
            {
                // The codec read past a refusal as if the file had ended, so nothing it holds can be trusted.
                inCanvas = -1;
                savedFrame = -1;
                var reopened = Source.Open(path, Sha256 is not null, out var reread);
                if (Sha256 is not null && !Sha256.AsSpan().SequenceEqual(reread))
                {
                    reopened?.Dispose();
                    return Decoded.Changed;
                }

                if (reopened is null)
                {
                    return Decoded.Unreadable;
                }

                source.Dispose();
                source = reopened;
            }

            var whole = DecodeInto(frame);
            source.Stream.ThrowIfFailed();
            return whole ? Decoded.Whole : Decoded.Unreadable;
        });
        inCanvas = decoded is { Succeeded: true, Value: Decoded.Whole } ? frame : -1;
        if (!decoded.Succeeded)
        {
            return decoded.Failure with { Subject = name };
        }

        return decoded.Value switch
        {
            Decoded.Whole => Outcome.Done,
            Decoded.Changed => new Failure(Action, name, ChangedReason),
            _ => Unreadable(name),
        };
    }

    private bool DecodeInto(int frame)
    {
        try
        {
            // A frame that restores what was under it leaves the next frame drawn over the one before it: keep that.
            if (Disposal(frame) == SKCodecAnimationDisposalMethod.RestorePrevious && frame > 0 && frame + 1 < FrameCount
                && inCanvas == frame - 1 && Disposal(inCanvas) != SKCodecAnimationDisposalMethod.RestorePrevious)
            {
                Save(inCanvas);
            }

            var prior = PriorFrame(frame);
            var result = source.Codec.GetPixels(canvas.Info, canvas.GetPixels(), new SKCodecOptions(frame, prior));
            if (result == SKCodecResult.InvalidParameters && prior >= 0)
            {
                result = source.Codec.GetPixels(canvas.Info, canvas.GetPixels(), new SKCodecOptions(frame));
            }

            return result == SKCodecResult.Success;
        }
        catch (Exception)
        {
            // A codec that throws has rejected the bytes (G35's entry).
            return false;
        }
    }

    /// <summary>
    /// The frame already composed in the canvas that <paramref name="frame"/> draws over, so frames are decoded once
    /// each in order rather than each from the start; -1 lets the codec decode what the frame needs itself.
    /// </summary>
    private int PriorFrame(int frame)
    {
        var required = frame < frames.Length ? frames[frame].RequiredFrame : -1;
        if (required < 0)
        {
            return -1;
        }

        if (inCanvas >= required && inCanvas < frame && Disposal(inCanvas) != SKCodecAnimationDisposalMethod.RestorePrevious)
        {
            return inCanvas;
        }

        if (saved is not null && savedFrame >= required && savedFrame < frame)
        {
            using var kept = saved.PeekPixels();
            if (kept.ReadPixels(canvas.Info, canvas.GetPixels(), canvas.RowBytes))
            {
                inCanvas = savedFrame;
                return savedFrame;
            }
        }

        return -1;
    }

    private void Save(int frame)
    {
        saved ??= Allocate(canvas.Info);
        if (saved is null)
        {
            return;
        }

        using var composed = canvas.PeekPixels();
        savedFrame = composed.ReadPixels(saved.Info, saved.GetPixels(), saved.RowBytes) ? frame : -1;
    }

    private Outcome<MediaFrame?> CopyOut(int durationMs, int bound)
    {
        var (width, height) = Fit(Width, Height, bound);
        var length = 4L * width * height;
        if (length > Array.MaxLength)
        {
            return new Failure(Action, name, "it is too large to hand over at that size");
        }

        try
        {
            var pixels = new byte[length];
            var pinned = GCHandle.Alloc(pixels, GCHandleType.Pinned);
            try
            {
                var target = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Unpremul, Srgb);
                // The frame upright, read from the canvas, which stays in the codec's grid for the next frame to draw over.
                if (Upright() is not { } shown)
                {
                    return NoMemory();
                }

                using var composed = shown.PeekPixels();
                if (width == shown.Width && height == shown.Height)
                {
                    if (!composed.ReadPixels(target, pinned.AddrOfPinnedObject(), target.RowBytes))
                    {
                        return NoMemory();
                    }

                    return Outcome<MediaFrame?>.Of(new MediaFrame(width, height, durationMs, pixels));
                }

                // Filtering mixes neighbouring pixels, which is right only once each colour is weighted by its alpha
                // (Skia filters straight-alpha pixels as they are), so the canvas is premultiplied before it is scaled.
                premultiplied ??= Allocate(shown.Info.WithAlphaType(SKAlphaType.Premul));
                using var scaled = Allocate(target.WithAlphaType(SKAlphaType.Premul));
                if (premultiplied is null || scaled is null)
                {
                    return NoMemory();
                }

                using var weighted = premultiplied.PeekPixels();
                using var scaledPixels = scaled.PeekPixels();
                if (!composed.ReadPixels(weighted.Info, weighted.GetPixels(), weighted.RowBytes)
                    || !weighted.ScalePixels(scaledPixels, Sampling)
                    || !scaledPixels.ReadPixels(target, pinned.AddrOfPinnedObject(), target.RowBytes))
                {
                    return NoMemory();
                }

                return Outcome<MediaFrame?>.Of(new MediaFrame(width, height, durationMs, pixels));
            }
            finally
            {
                pinned.Free();
            }
        }
        catch (OutOfMemoryException)
        {
            return NoMemory();
        }
    }

    private Failure NoMemory() => new(Action, name, "there is not enough memory to read it");

    /// <summary>
    /// The composed frame as the camera meant it: the canvas itself for an upright file, else the canvas turned into one
    /// more full-size buffer, kept for the open's life (R7); null when that buffer cannot be had.
    /// </summary>
    private SKBitmap? Upright()
    {
        if (origin == SKEncodedOrigin.TopLeft)
        {
            return canvas;
        }

        upright ??= Allocate(new SKImageInfo(Width, Height, SKColorType.Bgra8888, SKAlphaType.Unpremul, Srgb));
        if (upright is null)
        {
            return null;
        }

        var source = MemoryMarshal.Cast<byte, uint>(canvas.GetPixelSpan());
        var target = MemoryMarshal.Cast<byte, uint>(upright.GetPixelSpan());
        EncodedOrientation.Turn(origin, source, canvas.Width, canvas.Height, canvas.RowBytes / 4, target, upright.RowBytes / 4);
        return upright;
    }

    private static SKBitmap? Allocate(SKImageInfo info)
    {
        var bitmap = new SKBitmap();
        if (bitmap.TryAllocPixels(info))
        {
            return bitmap;
        }

        bitmap.Dispose();
        return null;
    }

    private static (int Width, int Height) Fit(int width, int height, int bound)
    {
        var longer = Math.Max(width, height);
        if (longer <= bound)
        {
            return (width, height);
        }

        int Scale(int edge) => Math.Max(1, (int)Math.Round((double)edge * bound / longer, MidpointRounding.AwayFromZero));
        return (Scale(width), Scale(height));
    }

    private enum Decoded
    {
        Whole,
        Unreadable,
        Changed,
    }

    /// <summary>An open file, checked whole, with its codec reading it through the guard.</summary>
    internal sealed class Source(SKCodec codec, CodecStream stream, ImageFormat format) : IDisposable
    {
        public SKCodec Codec { get; } = codec;

        public CodecStream Stream { get; } = stream;

        public ImageFormat Format { get; } = format;

        /// <summary>
        /// Opens the file, hashes it into <paramref name="sha256"/> when asked, whether or not it then reads, and checks
        /// it is a whole file of one of F3's formats that the codec reads as that format; null when it is not. The hash is
        /// read through the handle the codec then reads, so both are of one file whatever is renamed meanwhile. A file
        /// error throws, for the held-file policy to judge.
        /// </summary>
        public static Source? Open(string path, bool hash, out byte[]? sha256)
        {
            var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024);
            try
            {
                sha256 = hash ? Sha256Of(file) : null;
                var format = TruncationCheck.WholeFormat(file);
                if (format is null)
                {
                    file.Dispose();
                    return null;
                }

                file.Position = 0;
                var guarded = new CodecStream(file);
                SKCodec? codec = null;
                try
                {
                    // The codec owns the stream it is given and closes it when it is disposed.
                    codec = SKCodec.Create(guarded);
                }
                catch (Exception)
                {
                    // A codec that throws has rejected the bytes.
                }

                if (guarded.Failure is not null || codec is null || FormatOf(codec.EncodedFormat) != format)
                {
                    codec?.Dispose();
                    file.Dispose();
                    guarded.ThrowIfFailed();
                    return null;
                }

                return new Source(codec, guarded, format.Value);
            }
            catch
            {
                file.Dispose();
                throw;
            }
        }

        public void Dispose() => Codec.Dispose();

        /// <summary>Which of F3's formats the codec read the file as, or null for any other.</summary>
        internal static ImageFormat? FormatOf(SKEncodedImageFormat format) => format switch
        {
            SKEncodedImageFormat.Png => ImageFormat.Png,
            SKEncodedImageFormat.Jpeg => ImageFormat.Jpeg,
            SKEncodedImageFormat.Gif => ImageFormat.Gif,
            SKEncodedImageFormat.Webp => ImageFormat.WebP,
            _ => null,
        };
    }
}

/// <summary>What Media reads from a file for the store: its facts and the SHA-256 of its bytes (G12).</summary>
public sealed record MediaRead(MediaFacts Facts, byte[] Sha256);

/// <summary>
/// What Media reads from a file another program may have written: the SHA-256 of its bytes, and its facts, or null
/// when the bytes are not a whole file of one of F3's formats that decodes every frame (G27, G35).
/// </summary>
public sealed record ReadPicture(byte[] Sha256, MediaFacts? Facts);

/// <summary>
/// What Media answers for a finished picture it rendered: the facts read back from the written file, and the SHA-256 of
/// the bytes it decoded as the original, every frame of them, so the store can tell a render of the item's own original
/// from one of whatever file had taken its path (G12, G26).
/// </summary>
public sealed record MediaRender(MediaFacts Facts, byte[] OriginalSha256);

/// <summary>
/// One decoded frame in the boundary pixel contract (What crosses every boundary): <see cref="Pixels"/> holds
/// <see cref="Width"/> × <see cref="Height"/> pixels of 32-bit BGRA with straight alpha in sRGB, top-down rows packed
/// without padding; <see cref="DurationMs"/> is how long it shows, 0 for a still. It is the receiver's.
/// </summary>
public sealed record MediaFrame(int Width, int Height, int DurationMs, byte[] Pixels);
