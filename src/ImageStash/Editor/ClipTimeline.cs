using ImageStash.Media;

namespace ImageStash.Editor;

/// <summary>One frame on the clip's timeline: its own duration as the decoder gives it, and its start, the sum of the durations before it (G22).</summary>
public readonly record struct FrameTime(int DurationMs, long StartMs);

/// <summary>
/// An animated original's timeline as the filmstrip shows it (G22, R7, R8, G35): every frame read once, in order, on a
/// pool thread, through a decoder of its own opened for the pass and closed after it, each kept only as a thumbnail
/// smaller than the frame (<see cref="Bound"/>), so never a full-size frame, with its duration and start, in memory
/// only. While the pass runs it is not <see cref="Complete"/>; a frame it cannot read, or the file held past the retry,
/// ends the pass on the frames read before it and is told with their number.
/// </summary>
internal sealed class ClipTimeline : IDisposable
{
    /// <summary>The most a thumbnail's longer edge takes (L2): sharp in the filmstrip's cells at the display scalings in use.</summary>
    public const int ThumbnailBound = 96;

    // How many frames arrive between two tellings, so a long clip fills the strip as it is read without a redraw per frame.
    private const int Batch = 16;

    private readonly object gate = new();
    private readonly List<MediaFrame> thumbnails = [];
    private readonly List<FrameTime> times = [];
    private readonly CancellationTokenSource ended = new();
    private bool complete;

    private ClipTimeline()
    {
    }

    /// <summary>The thumbnails read so far, in frame order.</summary>
    public IReadOnlyList<MediaFrame> Thumbnails
    {
        get
        {
            lock (gate)
            {
                return thumbnails.ToArray();
            }
        }
    }

    /// <summary>The frames' times read so far, in frame order.</summary>
    public IReadOnlyList<FrameTime> Times
    {
        get
        {
            lock (gate)
            {
                return times.ToArray();
            }
        }
    }

    /// <summary>Whether the pass has read the last frame or stopped at one it could not read.</summary>
    public bool Complete
    {
        get
        {
            lock (gate)
            {
                return complete;
            }
        }
    }

    /// <summary>
    /// Starts the pass over the original at <paramref name="path"/>, opened only as the bytes whose SHA-256 is
    /// <paramref name="originalSha256"/> (<see cref="MediaDecoder.OpenOriginal"/>); <paramref name="changed"/> is told as
    /// frames arrive and at the end, and <paramref name="failed"/> of a frame it could not read with the number of frames
    /// read before it. Both are told on the pass's thread.
    /// </summary>
    public static ClipTimeline Read(string path, byte[] originalSha256, string name, TimeSpan heldFileRetry, Action changed, Action<int, Failure> failed)
    {
        var timeline = new ClipTimeline();
        _ = Task.Run(() => timeline.Run(path, originalSha256, name, heldFileRetry, changed, failed));
        return timeline;
    }

    /// <summary>
    /// The longer edge of the thumbnails of frames whose longer edge is <paramref name="longer"/>: the bound, or half the
    /// frame's when the frame is no larger, so the strip keeps no frame whole however many it has (R7).
    /// </summary>
    public static int Bound(int longer) => longer > ThumbnailBound ? ThumbnailBound : Math.Max(1, (longer + 1) / 2);

    /// <summary>Stops the pass; what it has read stays.</summary>
    public void Dispose() => ended.Cancel();

    private void Run(string path, byte[] originalSha256, string name, TimeSpan heldFileRetry, Action changed, Action<int, Failure> failed)
    {
        var read = 0;
        Failure? failure = null;
        var opened = MediaDecoder.OpenOriginal(path, name, heldFileRetry, originalSha256);
        if (!opened.Succeeded)
        {
            failure = opened.Failure;
        }
        else
        {
            using var decoder = opened.Value!;
            var bound = Bound(Math.Max(decoder.Width, decoder.Height));
            long start = 0;
            while (!ended.IsCancellationRequested)
            {
                var next = decoder.NextFrame(bound);
                if (!next.Succeeded)
                {
                    failure = next.Failure;
                    break;
                }

                if (next.Value is not { } thumbnail)
                {
                    break;
                }

                lock (gate)
                {
                    thumbnails.Add(thumbnail);
                    times.Add(new FrameTime(thumbnail.DurationMs, start));
                }

                start += thumbnail.DurationMs;
                if (++read % Batch == 0)
                {
                    changed();
                }
            }
        }

        lock (gate)
        {
            complete = true;
        }

        if (ended.IsCancellationRequested)
        {
            return;
        }

        if (failure is not null)
        {
            failed(read, failure);
        }

        changed();
    }
}
