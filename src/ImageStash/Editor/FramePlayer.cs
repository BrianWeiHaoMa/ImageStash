using ImageStash.Media;

namespace ImageStash.Editor;

/// <summary>A frame of the original as the editor shows it, with its index on the original's timeline, the first frame 0: one value, so the two always agree.</summary>
public readonly record struct ShownFrame(MediaFrame Frame, int Index);

/// <summary>
/// The editor's own player of an animated original (G22, G25, G6, R7): one decoder, opened with the session and closed
/// with it, reads the original's frames at full size one at a time, each after the one before, on a pool thread; the
/// frame showing and its index change together, on the UI thread, and the one other frame held is the one on its way.
/// It plays over its frames and round again, each showing for <see cref="ShownDuration"/> of its own, as the preview
/// times them: a frame read early shows at its time, one read late shows at once and the next is timed from it, so
/// frames never rush to catch up. A pause keeps the frame showing and what was left of its time; a step or a seek
/// pauses, then shows the frame asked for, reached by decoding forward, from the first frame again when it lies before
/// the decoder's place, so a clip's length never grows what is held. A frame on its way when the play is paused, stepped
/// or sought never shows unasked; it is kept only while it is the next one asked for. The decoder shares the file's
/// deletion and is never opened again by path, so a Save's move of the original goes through while it plays (R3, G29).
/// A frame it cannot read stops the play on the frame showing, limits the frames it plays to those before it, and is
/// told (G35).
/// </summary>
internal sealed class FramePlayer : IDisposable
{
    private readonly object gate = new();
    private readonly MediaDecoder decoder;
    private readonly string name;
    private readonly TimeProvider time;
    private readonly long origin;
    private readonly Action<Action> onUiThread;
    private readonly Action changed;
    private readonly Action<Failure> failed;

    /// <summary>Reads one frame at a time, each after the one before: nothing else touches the decoder.</summary>
    private Task decoding = Task.CompletedTask;

    /// <summary>The index of the frame the decoder reads next; touched only by <see cref="decoding"/>'s reads.</summary>
    private int position = 1;

    private ShownFrame shown;

    /// <summary>The frame read and not yet shown: the one on its way, the only full-size frame held beside the one showing (R7).</summary>
    private ShownFrame? ahead;

    private int count;
    private bool playing;

    /// <summary>Moved on by every pause, step, seek and failure, so a frame asked for before it never shows.</summary>
    private long generation;

    /// <summary>The frame to show next, asked for by the play or by a step or a seek; null while none is.</summary>
    private int? wanted;

    /// <summary>When the play shows <see cref="wanted"/>, on the player's own timeline; null shows it as soon as it is read.</summary>
    private TimeSpan? wantedAt;

    /// <summary>Whether <see cref="wanted"/> was read early and waits for its time, which then counts as the time it showed.</summary>
    private bool waiting;

    /// <summary>While paused: how long the frame showing still shows once the play goes on.</summary>
    private TimeSpan left;

    private ITimer? timer;
    private bool disposed;

    /// <param name="decoder">The original, open, its first frame read: the player's own, closed when it ends.</param>
    /// <param name="first">The original's first frame at full size, which shows first.</param>
    /// <param name="onUiThread">Runs an action on the UI thread, where the frame showing changes.</param>
    /// <param name="changed">Told, on the UI thread, when the frame showing or the play's state has changed.</param>
    /// <param name="failed">Told, on the UI thread, of a frame it could not read.</param>
    public FramePlayer(MediaDecoder decoder, MediaFrame first, string name, TimeProvider time, Action<Action> onUiThread, Action changed, Action<Failure> failed)
    {
        this.decoder = decoder;
        this.name = name;
        this.time = time;
        this.onUiThread = onUiThread;
        this.changed = changed;
        this.failed = failed;
        origin = time.GetTimestamp();
        count = decoder.FrameCount;
        shown = new ShownFrame(first, 0);
        left = ShownDuration.Of(first.DurationMs);
    }

    public ShownFrame Shown
    {
        get
        {
            lock (gate)
            {
                return shown;
            }
        }
    }

    public bool Playing
    {
        get
        {
            lock (gate)
            {
                return playing;
            }
        }
    }

    /// <summary>How many frames it plays: the file's, or those before the first frame that could not be read.</summary>
    public int Count
    {
        get
        {
            lock (gate)
            {
                return count;
            }
        }
    }

    /// <summary>The full-size frames it holds now: the one showing, and the one on its way when there is one (R7).</summary>
    public int FullSizeFramesHeld
    {
        get
        {
            lock (gate)
            {
                return ahead is null ? 1 : 2;
            }
        }
    }

    private TimeSpan Now => time.GetElapsedTime(origin);

    /// <summary>Plays on from the frame showing, after what was left of its time, or from a frame a step or a seek has asked for once it shows.</summary>
    public void Play()
    {
        lock (gate)
        {
            if (disposed || playing || count < 2)
            {
                return;
            }

            playing = true;
            if (wanted is null)
            {
                Want(Following(shown.Index), Now + left);
            }
        }

        changed();
    }

    /// <summary>Pauses on the frame showing; the frame on its way is not shown.</summary>
    public void Pause()
    {
        lock (gate)
        {
            if (disposed || !playing)
            {
                return;
            }

            playing = false;
            if (wantedAt is { } at)
            {
                left = Max(TimeSpan.Zero, at - Now);
                Forget();
            }
        }

        changed();
    }

    /// <summary>
    /// Pauses, then shows the frame <paramref name="frames"/> after the one showing, or before it when negative, stopping
    /// at the first and the last, never round; from a frame a step or a seek asked for and still on its way, from that one.
    /// </summary>
    public void Step(int frames)
    {
        int from;
        lock (gate)
        {
            from = wanted is { } asked && wantedAt is null ? asked : shown.Index;
        }

        Seek((int)Math.Clamp((long)from + frames, int.MinValue, int.MaxValue));
    }

    /// <summary>Pauses, then shows frame <paramref name="index"/>, kept within the frames it plays.</summary>
    public void Seek(int index)
    {
        lock (gate)
        {
            if (disposed || count < 2)
            {
                return;
            }

            var target = Math.Clamp(index, 0, count - 1);
            if (!playing && wanted == target && wantedAt is null)
            {
                // Already asked for and on its way.
                return;
            }

            if (playing && wantedAt is { } at)
            {
                left = Max(TimeSpan.Zero, at - Now);
            }

            playing = false;
            Forget();
            if (target != shown.Index)
            {
                Want(target, null);
            }
        }

        changed();
    }

    /// <summary>Plays only the first <paramref name="frames"/>: what the filmstrip could read when a later frame failed (G35).</summary>
    public void Limit(int frames)
    {
        lock (gate)
        {
            if (disposed || frames >= count)
            {
                return;
            }

            count = Math.Max(1, frames);
            if (count < 2)
            {
                // One frame read: nothing left to play.
                playing = false;
                Forget();
            }
            else if (wanted is { } asked && asked >= count)
            {
                var at = wantedAt;
                Forget();
                var instead = at is null ? count - 1 : Following(shown.Index);
                if (at is not null || instead != shown.Index)
                {
                    Want(instead, at);
                }
            }
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            playing = false;
            Forget();
            ahead = null;
            // After the read in progress, if any: the decoder is the reads' alone.
            decoding = decoding.ContinueWith(_ => decoder.Dispose(), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        }
    }

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;

    private int Following(int index) => index + 1 < count ? index + 1 : 0;

    /// <summary>Asks for frame <paramref name="index"/>, read after any read in progress, to show at <paramref name="at"/> or once read.</summary>
    private void Want(int index, TimeSpan? at)
    {
        wanted = index;
        wantedAt = at;
        waiting = false;
        var asked = generation;
        decoding = decoding.ContinueWith(_ => Read(index, asked), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
    }

    /// <summary>Forgets the frame asked for, so it never shows when it arrives.</summary>
    private void Forget()
    {
        generation++;
        wanted = null;
        wantedAt = null;
        waiting = false;
        timer?.Dispose();
        timer = null;
    }

    /// <summary>On the pool, after the read before it: frame <paramref name="index"/>, the one on its way when it is that one, else decoded.</summary>
    private void Read(int index, long asked)
    {
        bool onItsWay;
        lock (gate)
        {
            if (disposed || asked != generation)
            {
                return;
            }

            onItsWay = ahead?.Index == index;
            if (!onItsWay)
            {
                // Never a third full-size frame: the one on its way goes before another is read (R7).
                ahead = null;
            }
        }

        if (onItsWay)
        {
            Arrive(index, asked);
            return;
        }

        var read = ReadFrame(index, out var failedAt);
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            if (read.Succeeded)
            {
                ahead = new ShownFrame(read.Value!, index);
            }
            else
            {
                count = Math.Max(1, Math.Min(count, failedAt));
                playing = false;
                Forget();
            }

            if (read.Succeeded && asked != generation)
            {
                return;
            }
        }

        if (read.Succeeded)
        {
            Arrive(index, asked);
            return;
        }

        onUiThread(() =>
        {
            failed(read.Failure);
            changed();
        });
    }

    /// <summary>Decodes forward to frame <paramref name="index"/>, from the first again when it lies behind; on a failure, the frame that failed.</summary>
    private Outcome<MediaFrame> ReadFrame(int index, out int failedAt)
    {
        if (index < position)
        {
            decoder.Rewind();
            position = 0;
        }

        while (position < index)
        {
            var skipped = decoder.Skip();
            if (!skipped.Succeeded)
            {
                failedAt = position;
                position = int.MaxValue;
                return skipped.Failure;
            }

            position++;
        }

        failedAt = index;
        var next = decoder.NextFrame(Math.Max(decoder.Width, decoder.Height));
        if (!next.Succeeded || next.Value is not { } frame)
        {
            position = int.MaxValue;
            return next.Failure ?? new Failure("Read the picture", name, MediaDecoder.UnreadableReason);
        }

        position++;
        return Outcome<MediaFrame>.Of(frame);
    }

    private void Arrive(int index, long asked) => onUiThread(() => Show(index, asked));

    /// <summary>On the UI thread: shows the frame on its way when it is still the one asked for and its time has come, else waits for its time.</summary>
    private void Show(int index, long asked)
    {
        lock (gate)
        {
            if (disposed || asked != generation || wanted != index || ahead is not { } next || next.Index != index)
            {
                return;
            }

            var now = Now;
            if (wantedAt is { } at && at > now)
            {
                waiting = true;
                timer?.Dispose();
                timer = time.CreateTimer(_ => Arrive(index, asked), null, at - now, Timeout.InfiniteTimeSpan);
                return;
            }

            timer?.Dispose();
            timer = null;
            // On time, it showed at its time; late, now, and the next is timed from it.
            var shownAt = waiting && wantedAt is { } due ? due : now;
            shown = next;
            ahead = null;
            wanted = null;
            wantedAt = null;
            waiting = false;
            if (playing)
            {
                Want(Following(index), shownAt + ShownDuration.Of(next.Frame.DurationMs));
            }
            else
            {
                left = ShownDuration.Of(next.Frame.DurationMs);
            }
        }

        changed();
    }
}
