using ImageStash.Media;

namespace ImageStash.Window;

/// <summary>
/// The grid's one playing animation (G6, R7): the frames of one item's finished picture, decoded one at a time on a
/// pool thread at its thumbnail's bound, each handed to the UI thread when its time comes by the durations the file
/// gives, and the file, opened once per play, rewound to loop. Asking for another item, or for none, stops the one
/// playing first: its file is closed before the next is opened, so two never play at once. A still never plays. A
/// picture that cannot be read stops its play and is handed to <paramref name="couldNotRead"/>, which marks the tile
/// and names a failure other than its being unreadable (G35); nothing throws. A play can be paused on the frame
/// showing and resumed from it (G19).
/// </summary>
/// <param name="toImage">Turns a decoded frame into what the grid draws; runs on the UI thread.</param>
/// <param name="onUiThread">Runs an action on the UI thread.</param>
/// <param name="release">Frees a frame the play has moved past.</param>
/// <param name="couldNotRead">Told, on the UI thread, that the playing picture could not be read, and why.</param>
public sealed class Playback<TImage>(
    TimeProvider time,
    TimeSpan heldFileRetry,
    Func<MediaFrame, TImage> toImage,
    Action<Action> onUiThread,
    Action<TImage> release,
    Action<ThumbnailKey, Failure> couldNotRead)
    where TImage : class
{
    private readonly object gate = new();
    private ThumbnailRequest? playing;
    private long generation;
    private CancellationTokenSource? stopping;
    private Task running = Task.CompletedTask;
    private TImage? shown;

    /// <summary>While paused: completed by the resume the play waits for; null while it plays.</summary>
    private TaskCompletionSource? resumed;

    /// <summary>A frame that reached the UI thread after the pause, shown first when the play resumes.</summary>
    private MediaFrame? held;

    private long pausedSince;
    private TimeSpan pausedFor;

    /// <summary>Raised on the UI thread when a frame has arrived or the play has stopped, paused or resumed.</summary>
    public event Action? Changed;

    /// <summary>Whether the play is paused on the frame showing.</summary>
    public bool Paused
    {
        get
        {
            lock (gate)
            {
                return resumed is not null;
            }
        }
    }

    /// <summary>The frame showing now for <paramref name="key"/>, or null while it does not play or no frame has arrived.</summary>
    public TImage? Frame(ThumbnailKey key)
    {
        lock (gate)
        {
            return playing?.Key == key ? shown : null;
        }
    }

    /// <summary>
    /// Plays <paramref name="request"/>'s picture, or nothing when it is null; asking again for what plays changes
    /// nothing, so it goes on where it is. Called on the UI thread.
    /// </summary>
    public void Play(ThumbnailRequest? request)
    {
        TImage? stopped;
        lock (gate)
        {
            if (request == playing)
            {
                return;
            }

            stopping?.Cancel();
            stopping = null;
            stopped = shown;
            shown = null;
            resumed = null;
            held = null;
            pausedFor = TimeSpan.Zero;
            playing = request;
            var current = ++generation;
            if (request is not null)
            {
                var stop = stopping = new CancellationTokenSource();
                var previous = running;
                // After the previous play has closed its file, so never two decoders are open for the grid.
                running = Task.Run(async () =>
                {
                    await previous.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
                    await Run(request, current, stop.Token);
                });
            }
        }

        if (stopped is not null)
        {
            release(stopped);
        }

        Changed?.Invoke();
    }

    /// <summary>
    /// Pauses the play on the frame showing, which stays: a frame on its way meanwhile is held back, and the file is read
    /// no further until <see cref="Resume"/>. Called on the UI thread; nothing playing, or paused already, changes nothing.
    /// </summary>
    public void Pause()
    {
        lock (gate)
        {
            if (playing is null || resumed is not null)
            {
                return;
            }

            resumed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            pausedSince = time.GetTimestamp();
        }

        Changed?.Invoke();
    }

    /// <summary>
    /// Goes on from the frame the pause held, never from the first: a frame held back shows now, and the next follows it
    /// by the durations, the paused time left out. Called on the UI thread; a play not paused changes nothing.
    /// </summary>
    public void Resume()
    {
        TaskCompletionSource? waking;
        TImage? before = null;
        lock (gate)
        {
            if (resumed is null)
            {
                return;
            }

            waking = resumed;
            resumed = null;
            pausedFor += time.GetElapsedTime(pausedSince);
            if (held is { } next)
            {
                held = null;
                before = shown;
                shown = toImage(next);
            }
        }

        if (before is not null)
        {
            release(before);
        }

        waking.TrySetResult();
        Changed?.Invoke();
    }

    /// <summary>Completes once no play is running: after <c>Play(null)</c>, once the last file is closed.</summary>
    public Task Idle()
    {
        lock (gate)
        {
            return running;
        }
    }

    private async Task Run(ThumbnailRequest request, long current, CancellationToken stop)
    {
        if (stop.IsCancellationRequested)
        {
            return;
        }

        var opened = MediaDecoder.Open(request.Path, request.Name, heldFileRetry);
        if (!opened.Succeeded)
        {
            Fail(request, current, opened.Failure);
            return;
        }

        using var decoder = opened.Value!;
        if (decoder.FrameCount < 2)
        {
            return;
        }

        var started = time.GetTimestamp();
        var due = TimeSpan.Zero;
        var sinceRewind = 0;
        var delivered = Task.CompletedTask;
        try
        {
            while (!stop.IsCancellationRequested)
            {
                // The frame before has reached the UI thread, and a pause has ended, before the next is read: the play
                // holds the frame showing and at most the one on its way, never a third (R7).
                await delivered.WaitAsync(stop);
                await WhilePaused(stop);
                var next = decoder.NextFrame(request.Bound);
                if (!next.Succeeded)
                {
                    Fail(request, current, next.Failure);
                    return;
                }

                if (next.Value is not { } frame)
                {
                    if (sinceRewind == 0)
                    {
                        return;
                    }

                    decoder.Rewind();
                    sinceRewind = 0;
                    continue;
                }

                sinceRewind++;
                var early = due - Elapsed(started);
                if (early > TimeSpan.Zero)
                {
                    await Task.Delay(early, time, stop);
                }
                else
                {
                    // Behind, after a slow decode or a busy PC: this frame shows now and the next is timed from it, so
                    // frames never rush to catch up.
                    due = Elapsed(started);
                }

                delivered = Show(frame, current);
                due += ShownDuration.Of(frame.DurationMs);
            }
        }
        catch (OperationCanceledException)
        {
            // Stopped while waiting for a frame's time or for the frame before to be shown.
        }
    }

    /// <summary>Completes at once while the play is not paused, else once it resumes.</summary>
    private Task WhilePaused(CancellationToken stop)
    {
        lock (gate)
        {
            return resumed is { } waiting ? waiting.Task.WaitAsync(stop) : Task.CompletedTask;
        }
    }

    /// <summary>The time the play has played since <paramref name="started"/>, the time it was paused left out.</summary>
    private TimeSpan Elapsed(long started)
    {
        lock (gate)
        {
            return time.GetElapsedTime(started) - pausedFor - (resumed is null ? TimeSpan.Zero : time.GetElapsedTime(pausedSince));
        }
    }

    /// <summary>
    /// Hands <paramref name="frame"/> to the UI thread; completes once the UI thread has taken it, to show or, arriving
    /// after a pause, to hold until the resume.
    /// </summary>
    private Task Show(MediaFrame frame, long current)
    {
        var delivered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        onUiThread(() =>
        {
            try
            {
                TImage? before;
                lock (gate)
                {
                    if (current != generation)
                    {
                        return;
                    }

                    if (resumed is not null)
                    {
                        held = frame;
                        return;
                    }

                    before = shown;
                    shown = toImage(frame);
                }

                if (before is not null)
                {
                    release(before);
                }

                Changed?.Invoke();
            }
            finally
            {
                delivered.TrySetResult();
            }
        });
        return delivered.Task;
    }

    /// <summary>A failure is told only while its play is still the one asked for: one stopped meanwhile is nobody's concern.</summary>
    private void Fail(ThumbnailRequest request, long current, Failure failure) => onUiThread(() =>
    {
        TImage? before;
        lock (gate)
        {
            if (current != generation)
            {
                return;
            }

            before = shown;
            shown = null;
        }

        if (before is not null)
        {
            release(before);
        }

        couldNotRead(request.Key, failure);
        Changed?.Invoke();
    });
}
