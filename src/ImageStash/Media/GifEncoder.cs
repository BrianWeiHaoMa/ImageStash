using System.Drawing;
using System.Runtime.InteropServices;
using ImageStash.Files;
using KGySoft.Drawing.Imaging;
using Library = KGySoft.Drawing.Imaging.GifEncoder;

namespace ImageStash.Media;

/// <summary>
/// Media's one GIF encoder (R4): a clip written frame by frame into the <c>.work\</c> file it is opened on (R2, R7), or
/// into a caller's stream for a clip that is only measured. Each full frame comes with its time on the clip's timeline
/// and becomes one image of the GIF: the whole frame first, then only the rectangle of the pixels that differ from the
/// frame before it, or its top-left pixel when none does, so every frame keeps its own delay; each image has its own
/// palette, its own colours when they fit one and Media's <see cref="MedianCut"/> otherwise, without dithering, and
/// reaches the file in one write; a pixel at or above half
/// alpha shows its own straight colour, opaque, and one below it is transparent, since GIF has nothing between (G21);
/// each image is kept under the next,
/// unless the next turns one of its pixels transparent: GIF clears a pixel only by disposing of the image that painted
/// it, so that frame is written whole and restored to the background, and the next image covers every opaque pixel of
/// its frame. A frame's delay is known once the next frame or Stop arrives, so one frame is always pending. The clip
/// loops forever. Refusals and failures name the clip and are never thrown (G35). Calls take turns;
/// <see cref="BytesWritten"/> may be read from any thread while one runs.
/// </summary>
public sealed class GifEncoder : IClipEncoder
{
    private const string Action = "Write the clip as GIF";

    private const string Finished = "the clip is already finished";

    private const string TooLong = "a frame would show for longer than a GIF can hold";

    private const string TooLarge = "its size is more than a GIF can hold";

    /// <summary>The longest side a clip opens with: a GIF's logical screen is two 16-bit fields.</summary>
    public const int LargestSide = ushort.MaxValue;

    // A GIF's delays are 16-bit fields too.
    private const long LongestDelayCs = ushort.MaxValue;

    // Below half alpha a pixel takes GIF's one transparent index, the library's default; at or above it the pixel is
    // opaque, in its own straight colour. A recording's frames are opaque.
    private const uint OpaqueFrom = 128;

    // A local palette's entries, GIF's transparent index among them.
    private const int PaletteSize = MedianCut.PaletteSize;

    private readonly object gate = new();
    private readonly string? path;
    private readonly string name;
    private readonly TimeSpan heldFileRetry;
    private readonly int width;
    private readonly int height;

    // The clip's file, which the encoder created and closes, or the caller's stream, which it never closes (path null).
    private readonly Stream output;
    private readonly Library library;

    // What the library writes for the clip's file, gathered until the image is whole; null for a caller's stream.
    private readonly GatheredWrites? gathered;

    // The opaque colours the image being written shows, while one palette can hold them.
    private readonly HashSet<uint> colours = [];

    // The median cut's tables, its indices for the largest image, a full frame's, and its entries, kept for the clip (R7).
    private readonly MedianCut medianCut = new();
    private readonly uint[] entries = new uint[PaletteSize];
    private byte[]? indices;

    // The two full frames the encoder owns, reused for the whole clip (R7): the frame the pending one is drawn over, and
    // the pending one, copied from the caller's, which is the encoder's only until the call returns.
    private byte[]? previous;
    private byte[]? pending;
    private Rectangle pendingImage;
    private long pendingMs = -1;
    private long firstCs;
    private int written;
    private bool finished;
    private long bytesWritten;

    private GifEncoder(string? path, string name, TimeSpan heldFileRetry, int width, int height, Stream output, Library library, GatheredWrites? gathered)
    {
        this.path = path;
        this.name = name;
        this.heldFileRetry = heldFileRetry;
        this.width = width;
        this.height = height;
        this.output = output;
        this.library = library;
        this.gathered = gathered;
    }

    /// <summary>
    /// The bytes of the images written so far, the pending frame's not among them; after Stop, the file's length (G15's
    /// bar). For a clip written to a caller's stream, that stream's position.
    /// </summary>
    public long BytesWritten => Interlocked.Read(ref bytesWritten);

    /// <summary>
    /// Opens a clip of <paramref name="width"/> × <paramref name="height"/> physical pixels, creating the file new at
    /// <paramref name="workPath"/>, a <c>.work\</c> path Store allocated. Failures name <paramref name="name"/>, the clip's name.
    /// </summary>
    public static Outcome<GifEncoder> Open(string workPath, int width, int height, string name, TimeSpan heldFileRetry)
    {
        if (!Holds(width, height))
        {
            return new Failure(Action, name, TooLarge);
        }

        // Nothing reads the clip before Stop; a scanner may, without blocking the writes. Unbuffered, so each image's one
        // write reaches the file before its call returns, where a held file is waited for, and closing writes nothing more.
        var created = HeldFileRetry.Run(Action, workPath, heldFileRetry, () => new FileStream(workPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read, bufferSize: 0));
        if (!created.Succeeded)
        {
            return created.Failure with { Subject = name };
        }

        // The library writes nothing before the first image, so an open clip is an empty file.
        var gathered = new GatheredWrites(created.Value!, heldFileRetry);
        var library = new Library(gathered, new Size(width, height)) { RepeatCount = 0 };
        return Outcome<GifEncoder>.Of(new GifEncoder(workPath, name, heldFileRetry, width, height, created.Value!, library, gathered));
    }

    /// <summary>
    /// Opens a clip of <paramref name="width"/> × <paramref name="height"/> physical pixels written into
    /// <paramref name="destination"/>, a writable, seekable stream the caller owns and the encoder never closes: a clip
    /// that is measured rather than kept, so nothing reaches the stash folder (R8). Stop's clip names no file, and a clip
    /// with no frame writes nothing. Failures name <paramref name="name"/>.
    /// </summary>
    public static Outcome<GifEncoder> Open(Stream destination, int width, int height, string name)
    {
        if (!Holds(width, height))
        {
            return new Failure(Action, name, TooLarge);
        }

        if (!destination.CanWrite || !destination.CanSeek)
        {
            return new Failure(Action, name, "its stream cannot be written and measured");
        }

        var library = new Library(destination, new Size(width, height)) { RepeatCount = 0 };
        return Outcome<GifEncoder>.Of(new GifEncoder(null, name, TimeSpan.Zero, width, height, destination, library, null));
    }

    /// <summary>The recorder's way to <see cref="Open(string, int, int, string, TimeSpan)"/> a clip, held files retried for <paramref name="heldFileRetry"/>.</summary>
    public static ClipOpener Opener(TimeSpan heldFileRetry) => (workPath, width, height, name) =>
    {
        var opened = Open(workPath, width, height, name, heldFileRetry);
        return opened.Succeeded ? Outcome<IClipEncoder>.Of(opened.Value!) : opened.Failure;
    };

    /// <summary>
    /// Adds <paramref name="frame"/>, a full frame of the clip's size in the boundary pixel contract, shown from
    /// <paramref name="atMs"/> on the clip's timeline, no earlier than the previous frame's time, its own duration not
    /// read. The frame before it is written now, lasting until <paramref name="atMs"/>, so an equal time gives it no
    /// delay. A refused frame leaves the clip as it was; a failure finishes it, its file left for the caller to discard
    /// through Store.
    /// </summary>
    public Outcome Append(MediaFrame frame, long atMs)
    {
        lock (gate)
        {
            if (Refusal(frame, atMs) is { } refused)
            {
                return refused;
            }

            try
            {
                var cleared = false;
                if (pendingMs >= 0)
                {
                    // Under the keep disposal a transparent index shows the earlier pixel, so a pixel this frame clears
                    // needs the pending frame written whole and restored to the background, the canvas then empty.
                    cleared = TurnsTransparent(pending!, frame.Pixels);
                    WritePending(Centiseconds(atMs), cleared);
                    (previous, pending) = (pending, previous);
                }
                else
                {
                    firstCs = Centiseconds(atMs);
                }

                pending ??= new byte[frame.Pixels.Length];
                frame.Pixels.CopyTo(pending, 0);
                Settled(pending);
                pendingImage = previous is null ? new Rectangle(0, 0, width, height)
                    : cleared ? OpaqueBounds(pending, width, height)
                    : ChangedRectangle(previous, pending, width, height);
                pendingMs = atMs;
                return Outcome.Done;
            }
            catch (Exception e)
            {
                return Fail(e);
            }
        }
    }

    /// <summary>
    /// Ends the clip at <paramref name="atMs"/>, no earlier than the last frame's time: the last frame is written lasting
    /// until then, with the trailer, and the file is closed. The clip's duration is the sum of its delays. A clip with no
    /// frame is cancelled instead: its file is deleted, or a caller's stream left empty, and the outcome holds no clip.
    /// </summary>
    public Outcome<GifClip?> Stop(long atMs)
    {
        lock (gate)
        {
            if (finished)
            {
                return new Failure(Action, name, Finished);
            }

            if (pendingMs < 0)
            {
                finished = true;
                Close();
                if (path is null)
                {
                    return Outcome<GifClip?>.Of(null);
                }

                var deleted = HeldFileRetry.Run(Action, path, heldFileRetry, () => File.Delete(path));
                return deleted.Succeeded ? Outcome<GifClip?>.Of(null) : deleted.Failure with { Subject = name };
            }

            if (atMs < pendingMs)
            {
                return new Failure(Action, name, "Stop's time is earlier than the last frame's");
            }

            var stopCs = Centiseconds(atMs);
            if (stopCs - Centiseconds(pendingMs) > LongestDelayCs)
            {
                return new Failure(Action, name, TooLong);
            }

            try
            {
                WritePending(stopCs, cleared: false);
                library.FinalizeEncoding(leaveStreamOpen: true);
                gathered?.WriteGathered();
                Interlocked.Exchange(ref bytesWritten, output.Position);
                finished = true;
                Close();
                return Outcome<GifClip?>.Of(new GifClip(path, written, 10 * (stopCs - firstCs)));
            }
            catch (Exception e)
            {
                return Fail(e);
            }
        }
    }

    /// <summary>
    /// Finishes the clip without Stop: the pending frame is dropped, nothing more is written, not even the trailer, and
    /// the file is closed for the caller to discard through Store; a caller's stream is left as it is. After the clip is
    /// finished it does nothing.
    /// </summary>
    public void Cancel()
    {
        lock (gate)
        {
            if (!finished)
            {
                finished = true;
                Close();
            }
        }
    }

    private Failure? Refusal(MediaFrame frame, long atMs)
    {
        if (finished)
        {
            return new Failure(Action, name, Finished);
        }

        if (frame.Width != width || frame.Height != height)
        {
            return new Failure(Action, name, $"a frame of {frame.Width} × {frame.Height} is not the clip's {width} × {height}");
        }

        if (frame.Pixels?.LongLength != 4L * width * height)
        {
            return new Failure(Action, name, "a frame's pixels do not fill its size");
        }

        if (atMs < 0)
        {
            return new Failure(Action, name, "a frame's time is before the clip's start");
        }

        // An equal time is admitted: an original's frame of 0 ms keeps the delay the boundary rule gives it, 0 cs.
        if (pendingMs >= 0 && atMs < pendingMs)
        {
            return new Failure(Action, name, "a frame's time is earlier than the previous frame's");
        }

        return pendingMs >= 0 && Centiseconds(atMs) - Centiseconds(pendingMs) > LongestDelayCs ? new Failure(Action, name, TooLong) : null;
    }

    /// <summary>
    /// Writes the pending frame's image lasting until <paramref name="untilCs"/>, quantized from that image's pixels alone
    /// and kept under the next; when the next frame <paramref name="cleared"/> a pixel, the whole frame restored to the
    /// background instead, since that disposal clears only its own image's area. The image reaches the clip's file in one
    /// write, which takes milliseconds where the library's many small writes to the unbuffered file took most of a second.
    /// </summary>
    private void WritePending(long untilCs, bool cleared)
    {
        var area = cleared ? new Rectangle(0, 0, width, height) : pendingImage;
        using var image = Indexed(area);
        var disposal = cleared ? GifGraphicDisposalMethod.RestoreToBackground : GifGraphicDisposalMethod.DoNotDispose;
        library.AddImage(image, area.Location, (int)(untilCs - Centiseconds(pendingMs)), disposal);
        gathered?.WriteGathered();
        written++;
        Interlocked.Exchange(ref bytesWritten, output.Position);
    }

    /// <summary>
    /// The pending frame's <paramref name="area"/> as an indexed image: in its own colours when one palette holds them all,
    /// which is where median cut ends when it cuts until each box holds one colour, and through <see cref="MedianCut"/>
    /// otherwise, whose indices and entries the library writes as they are. A median cut alone may merge a colour a few
    /// pixels carry, a mark's or a black box's, into another though every colour would fit (G21, G23). The frame is
    /// <see cref="Settled"/>: each pixel opaque or transparent, so the library's matching blends nothing.
    /// </summary>
    private IReadWriteBitmapData Indexed(Rectangle area)
    {
        var pixels = MemoryMarshal.Cast<byte, uint>(pending!);
        if (OwnColours(pixels, area) is { } own)
        {
            using var whole = BitmapDataFactory.CreateBitmapData(pending!, new Size(width, height), width * 4, KnownPixelFormat.Format32bppArgb);
            return whole.Clone(area, KnownPixelFormat.Format8bppIndexed, PredefinedColorsQuantizer.FromCustomPalette(own), null);
        }

        indices ??= new byte[width * height];
        var count = medianCut.Quantize(pixels, width, area, indices, entries);
        var palette = new Palette(entries.Take(count).Select(Color32.FromArgb), default, (byte)OpaqueFrom);
        return BitmapDataFactory.CreateBitmapData(indices, area.Size, area.Width, KnownPixelFormat.Format8bppIndexed, palette);
    }

    /// <summary>The palette of the <paramref name="area"/>'s own colours, with the transparent entry when a pixel is transparent; null when one palette cannot hold them.</summary>
    private Palette? OwnColours(ReadOnlySpan<uint> pixels, Rectangle area)
    {
        var transparent = false;
        colours.Clear();
        for (var y = area.Top; y < area.Bottom; y++)
        {
            foreach (var pixel in pixels.Slice(y * width + area.Left, area.Width))
            {
                var alpha = pixel >> 24;
                transparent |= alpha < OpaqueFrom;
                if (alpha >= OpaqueFrom && colours.Add(pixel) && colours.Count > PaletteSize)
                {
                    return null;
                }
            }
        }

        if (colours.Count + (transparent ? 1 : 0) > PaletteSize)
        {
            return null;
        }

        var own = colours.Select(colour => Color32.FromArgb(colour));
        return new Palette(transparent ? own.Append(default) : own, default, (byte)OpaqueFrom);
    }

    private Failure Fail(Exception e)
    {
        finished = true;
        Close();
        return e switch
        {
            RefusedWrite refused => refused.Failure with { Subject = name },
            OutOfMemoryException => new Failure(Action, name, "there is not enough memory to write it"),
            _ => new Failure(Action, name, HeldFileRetry.ReasonFor(e)),
        };
    }

    private void Close()
    {
        previous = null;
        pending = null;
        indices = null;
        if (path is not null)
        {
            output.Dispose();
        }
    }

    /// <summary>
    /// Turns each pixel of <paramref name="frame"/> into what the GIF shows of it: at or above <see cref="OpaqueFrom"/> its
    /// straight colour, fully opaque, rather than that colour blended over the quantizer's black, which would fringe a
    /// white text's edges with grey (G21); below it, fully transparent. Opaque pixels, a recording's, are left as they are.
    /// </summary>
    private static void Settled(byte[] frame)
    {
        var pixels = MemoryMarshal.Cast<byte, uint>(frame.AsSpan());
        for (var i = 0; i < pixels.Length; i++)
        {
            var alpha = pixels[i] >> 24;
            if (alpha != 255)
            {
                pixels[i] = alpha >= OpaqueFrom ? pixels[i] | 0xFF000000 : 0;
            }
        }
    }

    private static bool Holds(int width, int height) => width is >= 1 and <= LargestSide && height is >= 1 and <= LargestSide;

    /// <summary>
    /// A time on the clip's timeline in whole centiseconds, halves up (What crosses every boundary): every delay is the
    /// difference of two rounded times, so rounding never accumulates and the delays add up to the recorded time.
    /// </summary>
    private static long Centiseconds(long ms) => ms / 10 + (ms % 10 >= 5 ? 1 : 0);

    /// <summary>
    /// The bounding box of the pixels of <paramref name="after"/> that differ from <paramref name="before"/>, both full
    /// frames of <paramref name="width"/> × <paramref name="height"/>; the top-left pixel alone when none does, since
    /// every frame is still an image of its own.
    /// </summary>
    private static Rectangle ChangedRectangle(byte[] before, byte[] after, int width, int height)
    {
        var a = MemoryMarshal.Cast<byte, uint>(before);
        var b = MemoryMarshal.Cast<byte, uint>(after);
        int top = -1, bottom = -1, left = width, right = -1;
        for (var y = 0; y < height; y++)
        {
            var rowA = a.Slice(y * width, width);
            var rowB = b.Slice(y * width, width);
            var first = rowA.CommonPrefixLength(rowB);
            if (first == width)
            {
                continue;
            }

            top = top < 0 ? y : top;
            bottom = y;
            left = Math.Min(left, first);
            var last = width - 1;
            while (last > right && rowA[last] == rowB[last])
            {
                last--;
            }

            right = Math.Max(right, last);
        }

        return top < 0 ? new Rectangle(0, 0, 1, 1) : new Rectangle(left, top, right - left + 1, bottom - top + 1);
    }

    /// <summary>Whether <paramref name="after"/> has a pixel transparent where <paramref name="before"/> has it opaque, as the quantizer judges alpha.</summary>
    private static bool TurnsTransparent(byte[] before, byte[] after)
    {
        var a = MemoryMarshal.Cast<byte, uint>(before);
        var b = MemoryMarshal.Cast<byte, uint>(after);
        for (var i = 0; i < a.Length; i++)
        {
            if (a[i] >> 24 >= OpaqueFrom && b[i] >> 24 < OpaqueFrom)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The bounding box of the opaque pixels of <paramref name="frame"/>, a full frame of <paramref name="width"/> ×
    /// <paramref name="height"/>, which is all an image over an empty canvas needs; the top-left pixel alone when none is.
    /// </summary>
    private static Rectangle OpaqueBounds(byte[] frame, int width, int height)
    {
        var pixels = MemoryMarshal.Cast<byte, uint>(frame);
        int top = -1, bottom = -1, left = width, right = -1;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (pixels[y * width + x] >> 24 >= OpaqueFrom)
                {
                    top = top < 0 ? y : top;
                    bottom = y;
                    left = Math.Min(left, x);
                    right = Math.Max(right, x);
                }
            }
        }

        return top < 0 ? new Rectangle(0, 0, 1, 1) : new Rectangle(left, top, right - left + 1, bottom - top + 1);
    }

    /// <summary>
    /// The clip's file as the library writes to it: the library's writes are gathered in memory, in a buffer kept for the
    /// clip (R7), and <see cref="WriteGathered"/> writes them to the file in one write, run again while another program
    /// holds the file, for the retry G35 gives a held file, so a brief hold only delays the clip; a write still refused
    /// carries its failure out. Nothing gathered is left unwritten when a call of the encoder returns, so the file's length
    /// is the bytes written so far, and a clip finished without Stop has nothing more to write.
    /// </summary>
    private sealed class GatheredWrites(FileStream file, TimeSpan heldFileRetry) : Stream
    {
        private readonly MemoryStream gathered = new();

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

            var written = HeldFileRetry.Run(Action, file.Name, heldFileRetry, () => file.Write(gathered.GetBuffer(), 0, (int)gathered.Length));
            gathered.SetLength(0);
            if (!written.Succeeded)
            {
                throw new RefusedWrite(written.Failure);
            }
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();
    }

    private sealed class RefusedWrite(Failure failure) : IOException(failure.Reason)
    {
        public Failure Failure { get; } = failure;
    }
}

/// <summary>
/// A clip written whole: its <c>.work\</c> file (null for a clip written to a caller's stream), how many frames it holds,
/// and how long it plays, the sum of its delays.
/// </summary>
public sealed record GifClip(string? WorkPath, int FrameCount, long DurationMs);
