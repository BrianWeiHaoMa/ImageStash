using System.Globalization;
using System.Runtime.InteropServices;
using ImageStash.Files;
using ImageStash.Layers;
using SkiaSharp;

namespace ImageStash.Media;

/// <summary>
/// An edited item's finished picture (Composition, Media): its original's kept frames drawn under its layers by the one
/// routine and written through the one encoder path (R4), into a new file at the <c>.work\</c> path Store allocated, or
/// only measured for the editor's size estimate (G24). The original is opened read-only (R3) and decoded one frame at a
/// time, and the trim's frames are composed one at a time onto one surface of the output size (R7). Before anything is
/// drawn or created, WebP as a finished format (N7), a crop that misses the picture, and an output larger than its
/// format holds or its encoder writes (E4) are refused by name; every other failure is named; nothing is thrown (G35).
/// Synchronous and touching no UI, so any thread may call it, one call at a time per original.
/// </summary>
public static class FinishedPicture
{
    private const string Action = "Save the picture";

    private const string Cancelled = "the estimate was cancelled";

    // L2: the estimate of an animation encodes at most this many of its kept frames, evenly spaced, and scales for the
    // rest (Scaled); a clip with no more kept frames is encoded whole, so its estimate is its file's length.
    private const int EstimateSamples = 8;

    private static readonly SKColorSpace Srgb = SKColorSpace.CreateSrgb();

    /// <summary>
    /// Renders <paramref name="layers"/> over the original at <paramref name="originalPath"/> into a new file at
    /// <paramref name="workPath"/> in <paramref name="finishedFormat"/>, the format Store derived by F3: as PNG or JPEG
    /// (at <see cref="JpegEncoder.Quality"/>, T2) the first kept frame; as GIF every kept frame of the trim, each shown
    /// from the sum of the kept durations before it, so the clip keeps its frames' own timing (What crosses every
    /// boundary). Answers the facts read back from the file and the SHA-256 of the bytes decoded as the original, taken
    /// over the handle every frame was read through. Failures name <paramref name="itemName"/>; a failure after the file
    /// was created leaves it for the caller to discard through Store.
    /// </summary>
    public static Outcome<MediaRender> Render(string originalPath, string itemName, LayerList layers, ImageFormat finishedFormat, string workPath, TimeSpan heldFileRetry)
    {
        var opened = Rendering.Open(originalPath, itemName, layers, finishedFormat, heldFileRetry, originalSha256: null);
        if (!opened.Succeeded)
        {
            return opened.Failure;
        }

        Outcome written;
        byte[] originalSha256;
        using (var rendering = opened.Value!)
        {
            written = finishedFormat == ImageFormat.Gif ? WriteClip(rendering, workPath, heldFileRetry) : WriteStill(rendering, finishedFormat, workPath, heldFileRetry);
            originalSha256 = rendering.OriginalSha256!;
        }

        if (!written.Succeeded)
        {
            return written.Failure;
        }

        // The facts every later reader gets from the file: a GIF of one image is a still (F3).
        var read = MediaDecoder.ReadFacts(workPath, itemName, heldFileRetry);
        return read.Succeeded ? Outcome<MediaRender>.Of(new MediaRender(read.Value!.Facts, originalSha256)) : read.Failure;
    }

    /// <summary>
    /// The byte length <see cref="Render"/> would write for the same arguments, through the same encoders and in memory
    /// (R8): exact for PNG, JPEG and a clip of at most <see cref="EstimateSamples"/> kept frames; for a longer clip,
    /// evenly spaced kept frames encoded as a clip and scaled to the kept count, over the original only when its bytes
    /// hash to <paramref name="originalSha256"/>, the store's record (<see cref="MediaDecoder.OpenOriginal"/>). Refusals
    /// and failures are the render's. <paramref name="cancel"/> is heeded between frames: a cancelled estimate stops at
    /// the frame it is on and answers a failure, never a size (G24), so a Save waiting for the encoder goes ahead.
    /// </summary>
    public static Outcome<long> Estimate(string originalPath, byte[] originalSha256, string itemName, LayerList layers, ImageFormat finishedFormat, TimeSpan heldFileRetry, CancellationToken cancel = default)
    {
        // Hashed: the footer states the size of this item's picture (G24), never of a file renames put at the path.
        var opened = Rendering.Open(originalPath, itemName, layers, finishedFormat, heldFileRetry, originalSha256);
        if (!opened.Succeeded)
        {
            return opened.Failure;
        }

        using var rendering = opened.Value!;
        if (cancel.IsCancellationRequested)
        {
            return new Failure(Action, itemName, Cancelled);
        }

        if (finishedFormat != ImageFormat.Gif)
        {
            var still = EncodeStill(rendering, finishedFormat);
            return still.Succeeded ? Outcome<long>.Of(still.Value!.LongLength) : still.Failure;
        }

        using var measured = new MemoryStream();
        var clip = GifEncoder.Open(measured, rendering.Size.Width, rendering.Size.Height, itemName);
        if (!clip.Succeeded)
        {
            return clip.Failure;
        }

        var kept = rendering.KeptCount;
        var samples = Math.Min(kept, EstimateSamples);
        var sampled = Enumerable.Range(0, samples).Select(i => samples == 1 ? 0 : (int)((2L * i * (kept - 1) + samples - 1) / (2 * (samples - 1)))).ToHashSet();
        var firstImageEnd = EncodeClip(rendering, clip.Value!, sampled.Contains, cancel);
        return firstImageEnd.Succeeded ? Outcome<long>.Of(Scaled(measured.Length, firstImageEnd.Value, kept, samples)) : firstImageEnd.Failure;
    }

    /// <summary>
    /// The estimate's one scaling rule (L2): the sample's first image, which is whole, as it is, and its later images,
    /// which hold what changed, in proportion to the kept frames after the first; a sample of every kept frame is exact.
    /// </summary>
    private static long Scaled(long length, long firstImageEnd, int kept, int samples) =>
        samples >= kept ? length : firstImageEnd + (long)((Int128)(length - firstImageEnd) * (kept - 1) / (samples - 1));

    private static Outcome WriteClip(Rendering rendering, string workPath, TimeSpan heldFileRetry)
    {
        var clip = GifEncoder.Open(workPath, rendering.Size.Width, rendering.Size.Height, rendering.Name, heldFileRetry);
        if (!clip.Succeeded)
        {
            return clip.Failure;
        }

        var encoded = EncodeClip(rendering, clip.Value!, _ => true);
        return encoded.Succeeded ? Outcome.Done : encoded.Failure;
    }

    /// <summary>
    /// The kept frames <paramref name="sampled"/> admits, by their place among the kept frames, appended to
    /// <paramref name="clip"/>, each at the sum of the durations of those appended before it, the first at 0, and Stop at
    /// the sum of them all. Answers the bytes written by the end of the first image (0 for a clip of one frame).
    /// </summary>
    private static Outcome<long> EncodeClip(Rendering rendering, GifEncoder clip, Func<int, bool> sampled, CancellationToken cancel = default)
    {
        long atMs = 0, firstImageEnd = 0;
        var appended = 0;
        var walked = rendering.Walk(sampled, frame =>
        {
            var added = clip.Append(frame, atMs);
            atMs += frame.DurationMs;

            // A frame is written once the next one arrives.
            if (++appended == 2)
            {
                firstImageEnd = clip.BytesWritten;
            }

            return added;
        }, cancel);
        var stopped = walked.Succeeded ? clip.Stop(atMs) : walked.Failure;
        if (!stopped.Succeeded)
        {
            // A refused frame or Stop leaves the clip open; cancelled, its file is closed for the caller to discard.
            clip.Cancel();
            return stopped.Failure;
        }

        return Outcome<long>.Of(firstImageEnd);
    }

    /// <summary>
    /// Creates the file new, so a path already taken is refused and left as it was (R2), and writes it whole, again from
    /// its start while another program holds it (G35).
    /// </summary>
    private static Outcome WriteStill(Rendering rendering, ImageFormat format, string workPath, TimeSpan heldFileRetry)
    {
        var encoded = EncodeStill(rendering, format);
        if (!encoded.Succeeded)
        {
            return encoded.Failure;
        }

        var created = HeldFileRetry.Run(Action, workPath, heldFileRetry, () => new FileStream(workPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read, bufferSize: 0));
        if (!created.Succeeded)
        {
            return created.Failure with { Subject = rendering.Name };
        }

        using var file = created.Value!;
        var written = HeldFileRetry.Run(Action, workPath, heldFileRetry, () =>
        {
            file.Position = 0;
            file.Write(encoded.Value!);
        });
        return written.Succeeded ? Outcome.Done : written.Failure with { Subject = rendering.Name };
    }

    /// <summary>The kept frame, composed, as PNG or JPEG bytes: F3 reaches these formats only for stills.</summary>
    private static Outcome<byte[]> EncodeStill(Rendering rendering, ImageFormat format)
    {
        Outcome<byte[]>? encoded = null;
        var walked = rendering.Walk(_ => true, frame =>
        {
            encoded = format == ImageFormat.Jpeg ? JpegEncoder.Encode(frame, rendering.Name) : PngEncoder.Encode(frame, rendering.Name);
            return encoded;
        });
        return walked.Succeeded ? encoded! : walked.Failure;
    }

    /// <summary>
    /// E4: the output against the longest side its format holds, or its encoder writes where that is less: a GIF side at
    /// most 65,535; a JPEG side at most 65,500, past which the encoder faults the process (S11); a PNG side at most
    /// 1,000,000, past which the encoder writes nothing.
    /// </summary>
    private static string? TooLarge(PixelSize size, ImageFormat format)
    {
        var (largest, holder) = format switch
        {
            ImageFormat.Jpeg => (JpegEncoder.LargestSide, "ImageStash's JPEG encoder writes"),
            ImageFormat.Gif => (GifEncoder.LargestSide, "a GIF holds"),
            _ => (PngEncoder.LargestSide, "ImageStash's PNG encoder writes"),
        };
        return size.Width <= largest && size.Height <= largest ? null
            : string.Create(CultureInfo.InvariantCulture, $"it would be {size.Width:N0} × {size.Height:N0} pixels, and {holder} at most {largest:N0} pixels a side");
    }

    /// <summary>
    /// An original opened for its finished picture, past every refusal: its decoder, the kept frames on its timeline, the
    /// output's size, and the one surface each kept frame is composed on and the one frame it is read back into (R7).
    /// </summary>
    private sealed class Rendering : IDisposable
    {
        private readonly MediaDecoder decoder;
        private readonly LayerList layers;
        private readonly int first;
        private readonly int last;
        private readonly SKSurface surface;
        private readonly byte[] composed;

        private Rendering(string name, MediaDecoder decoder, LayerList layers, int first, int last, PixelSize size, SKSurface surface, byte[] composed)
        {
            Name = name;
            Size = size;
            this.decoder = decoder;
            this.layers = layers;
            this.first = first;
            this.last = last;
            this.surface = surface;
            this.composed = composed;
        }

        /// <summary>The item's name, which every failure names.</summary>
        public string Name { get; }

        public PixelSize Size { get; }

        public int KeptCount => last - first + 1;

        /// <summary>Opened hashing, the SHA-256 of the original every frame is decoded from (<see cref="MediaDecoder.Sha256"/>).</summary>
        public byte[]? OriginalSha256 => decoder.Sha256;

        /// <summary>
        /// Opens the original hashing: without <paramref name="originalSha256"/> the render answers the hash for the store to
        /// judge before it commits; with it the hash is judged here, since nothing commits an estimate.
        /// </summary>
        public static Outcome<Rendering> Open(string originalPath, string itemName, LayerList layers, ImageFormat format, TimeSpan heldFileRetry, byte[]? originalSha256)
        {
            // Store derives the finished format by F3, which never gives WebP; nothing is opened for it.
            if (format == ImageFormat.WebP)
            {
                return new Failure(Action, itemName, "ImageStash writes no WebP");
            }

            var opened = originalSha256 is null
                ? MediaDecoder.Open(originalPath, itemName, heldFileRetry, hash: true)
                : MediaDecoder.OpenOriginal(originalPath, itemName, heldFileRetry, originalSha256);
            if (!opened.Succeeded)
            {
                return opened.Failure;
            }

            var decoder = opened.Value!;
            var size = LayerComposer.OutputSize(decoder.Width, decoder.Height, layers);
            var refusal = size.Width == 0 ? "its crop lies wholly outside the picture" : TooLarge(size, format);
            var noMemory = new Failure(Action, itemName, "there is not enough memory to draw it");
            SKSurface? surface = null;
            Rendering? rendering = null;
            try
            {
                if (refusal is not null)
                {
                    return new Failure(Action, itemName, refusal);
                }

                // Each kept frame is read back whole into one array (What crosses every boundary).
                if (4L * size.Width * size.Height > Array.MaxLength
                    || (surface = SKSurface.Create(new SKImageInfo(size.Width, size.Height, SKColorType.Bgra8888, SKAlphaType.Premul, Srgb))) is null)
                {
                    return noMemory;
                }

                // The trim keeps frames of the original, each end clamped to its frames, so a trim beyond them keeps the
                // last (G22); PNG and JPEG hold one frame, the first kept.
                var frames = decoder.FrameCount;
                var first = Math.Clamp(layers.Trim?.Range.First ?? 0, 0, frames - 1);
                var last = format == ImageFormat.Gif ? Math.Clamp(layers.Trim?.Range.Last ?? frames - 1, 0, frames - 1) : first;
                rendering = new Rendering(itemName, decoder, layers, first, last, size, surface, new byte[4L * size.Width * size.Height]);
                return Outcome<Rendering>.Of(rendering);
            }
            catch (OutOfMemoryException)
            {
                return noMemory;
            }
            finally
            {
                if (rendering is null)
                {
                    surface?.Dispose();
                    decoder.Dispose();
                }
            }
        }

        /// <summary>
        /// Decodes the original's frames in order through the last kept one, since a frame may be drawn over the one
        /// before it, and composes each kept frame <paramref name="wanted"/> admits by its place among the kept frames,
        /// handing it to <paramref name="take"/> with its frame's duration; the frame is the receiver's only until it
        /// returns. A frame nothing takes is decoded but not copied out (R7). Stops at the first failure, and at the
        /// frame it is on once <paramref name="cancel"/> is cancelled.
        /// </summary>
        public Outcome Walk(Func<int, bool> wanted, Func<MediaFrame, Outcome> take, CancellationToken cancel = default)
        {
            var bound = Math.Max(decoder.Width, decoder.Height);
            for (var index = 0; index <= last; index++)
            {
                if (cancel.IsCancellationRequested)
                {
                    return new Failure(Action, Name, Cancelled);
                }

                if (index < first || !wanted(index - first))
                {
                    var skipped = decoder.Skip();
                    if (!skipped.Succeeded)
                    {
                        return skipped;
                    }

                    continue;
                }

                var next = decoder.NextFrame(bound);
                if (!next.Succeeded)
                {
                    return next.Failure;
                }

                // Every frame through the decoder's count comes before its end, and the last kept is below that count.
                var frame = Compose(next.Value!, index);
                Outcome taken = frame.Succeeded ? take(frame.Value!) : frame.Failure;
                if (!taken.Succeeded)
                {
                    return taken;
                }
            }

            return Outcome.Done;
        }

        public void Dispose()
        {
            surface.Dispose();
            decoder.Dispose();
        }

        /// <summary>
        /// <paramref name="original"/> under the layers onto the cleared surface, at its index on the original's timeline,
        /// against which each range is judged as written (G22), and read back whole: encoders see only composed pixels (G23).
        /// </summary>
        private Outcome<MediaFrame> Compose(MediaFrame original, int index)
        {
            surface.Canvas.Clear(SKColors.Transparent);
            var drawn = LayerComposer.Compose(surface.Canvas, original, layers, index, Name);
            if (!drawn.Succeeded)
            {
                return drawn.Failure;
            }

            var info = new SKImageInfo(Size.Width, Size.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul, Srgb);
            var pinned = GCHandle.Alloc(composed, GCHandleType.Pinned);
            try
            {
                return surface.ReadPixels(info, pinned.AddrOfPinnedObject(), info.RowBytes, 0, 0)
                    ? Outcome<MediaFrame>.Of(new MediaFrame(Size.Width, Size.Height, original.DurationMs, composed))
                    : new Failure(Action, Name, "there is not enough memory to draw it");
            }
            finally
            {
                pinned.Free();
            }
        }
    }
}
