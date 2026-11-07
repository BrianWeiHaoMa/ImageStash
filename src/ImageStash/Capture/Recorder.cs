using Avalonia;
using ImageStash.Media;
using ImageStash.Seams;

namespace ImageStash.Capture;

/// <summary>What the recording's bar reads while it runs (G15): the time on the clip's timeline and the size so far.</summary>
public interface IRecordingReadings
{
    long ElapsedMs { get; }

    long BytesWritten { get; }
}

/// <summary>
/// One recording's loop (G15, F3, T3): the region sampled through the Screen seam every 70 ms on a thread of its own, the
/// ticks anchored to the first so a grab's own time never slows the pace; each frame, with the latest place on that 70 ms
/// timeline its tick reached, handed through a bounded queue to an encoder thread that appends it to the clip at the
/// <c>.work\</c> path it was given (R2). A sample due while the queue is full is skipped before any grab, so the frame
/// before it lasts longer and the clip still lasts the recorded time (T3), with a fixed few full frames held however long
/// the clip runs (R7). It ends on Stop, which drains the queue and closes the last frame at Stop's moment; at the cap,
/// which ends the clip at the cap itself; on Cancel, which discards the queue; or on a refused grab or an encoder failure,
/// named (E5, G35). Nothing throws across it, and it never deletes a file: a clip it did not finish is left closed for its
/// caller to discard.
/// One instance per recording; its calls come from any thread.
/// </summary>
public sealed class Recorder(IScreen screen, ClipOpener openClip, TimeProvider clock) : IRecordingReadings
{
    public const string Action = "Record a clip";

    /// <summary>How many full frames may wait for the encoder (R7).</summary>
    public const int QueueBound = 2;

    /// <summary>The time between samples: the GIF timing nearest 15 per second (F3), seven centiseconds.</summary>
    public static readonly TimeSpan SamplePeriod = TimeSpan.FromMilliseconds(70);

    private const string NoMemory = "there is not enough memory to hold it";

    private readonly object gate = new();
    private readonly Queue<(MediaFrame Frame, long AtMs)> queue = new();
    private readonly TaskCompletionSource<Outcome<GifClip?>> ended = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> samplingEnded = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private IClipEncoder? encoder;
    private ITimer? timer;
    private PixelRect region;
    private bool withPointer;
    private long capMs;
    private string name = "";

    // Under the gate. The first of Stop, the cap, Cancel and a failure ends the sampling; Cancel, when it came first,
    // decides the outcome, else the first failure of a step the clip needed, else the encoder's Stop.
    private bool started;
    private Ending ending;
    private Failure? failure;
    private long stopMs;
    private long? anchor;
    private TimeSpan due;
    private bool woken;
    private bool samplerDone;
    private int framesHeld;

    private enum Ending
    {
        None,
        Stop,
        Cancel,
        Failed,
    }

    /// <summary>
    /// Done with the clip written whole and closed (Stop or the cap, with a frame); done with null (Cancel, its file
    /// closed and left; or Stop with no frame, which leaves no file); or the failure that ended it. Once complete, nothing
    /// of the recorder runs or holds a frame.
    /// </summary>
    public Task<Outcome<GifClip?>> Ended => ended.Task;

    /// <summary>
    /// Completes, always before <see cref="Ended"/>, once the sampling has ended: true when what was sampled is being
    /// finished into the clip (Stop or the cap), false when it is discarded (Cancel, a failure, or a start that went no further).
    /// </summary>
    public Task<bool> SamplingEnded => samplingEnded.Task;

    /// <summary>0 until the first sample's grab starts; then the time on the clip's timeline, never past the cap; frozen once the sampling ended.</summary>
    public long ElapsedMs
    {
        get
        {
            lock (gate)
            {
                return ending == Ending.None ? Elapsed() : stopMs;
            }
        }
    }

    /// <summary>The encoder's bytes written so far.</summary>
    public long BytesWritten => encoder?.BytesWritten ?? 0;

    /// <summary>The full frames the recorder holds now: queued, being grabbed or being appended (R7).</summary>
    public int FramesHeld
    {
        get
        {
            lock (gate)
            {
                return framesHeld;
            }
        }
    }

    /// <summary>True from <see cref="Start"/> until Stop, the cap, Cancel or a failure ended the sampling.</summary>
    public bool Sampling
    {
        get
        {
            lock (gate)
            {
                return started && ending == Ending.None;
            }
        }
    }

    /// <summary>
    /// Opens the clip at <paramref name="workPath"/>, a <c>.work\</c> path Store allocated and the only path the recorder
    /// writes, with the region's size, then starts the sampler and the encoder threads and returns at once. A refused
    /// open, or a thread that cannot start, is this action's failure on <paramref name="name"/>, and <see cref="Ended"/>
    /// completes with it, or with nothing when a Cancel from another thread came first.
    /// </summary>
    public Outcome Start(PixelRect region, bool withPointer, TimeSpan cap, string workPath, string name)
    {
        lock (gate)
        {
            if (started)
            {
                throw new InvalidOperationException("A recorder records one clip.");
            }

            started = true;
        }

        (this.region, this.withPointer, capMs, this.name) = (region, withPointer, (long)cap.TotalMilliseconds, name);
        var opened = openClip(workPath, region.Width, region.Height, name);
        if (!opened.Succeeded)
        {
            return Refused(new Failure(Action, name, opened.Failure.Reason));
        }

        encoder = opened.Value!;
        try
        {
            // The first tick is awaited through the clock like every other, due at once.
            timer = clock.CreateTimer(_ => Wake(), null, TimeSpan.Zero, Timeout.InfiniteTimeSpan);
            new Thread(Encode) { IsBackground = true, Name = "Recording encoder" }.Start();
        }
        catch (Exception e)
        {
            timer?.Dispose();
            encoder.Cancel();
            return Refused(Unexpected(e));
        }

        try
        {
            new Thread(Sample) { IsBackground = true, Name = "Recording sampler" }.Start();
            return Outcome.Done;
        }
        catch (Exception e)
        {
            // The encoder thread runs, and ends the clip once the sampler, which never ran, counts as done.
            timer.Dispose();
            var failed = Unexpected(e);
            lock (gate)
            {
                Fail(failed);
                samplerDone = true;
                Monitor.PulseAll(gate);
            }

            return failed;
        }
    }

    /// <summary>Ends the sampling at this moment: what was sampled before it is appended, then the clip closes at it. After Cancel or another end, nothing.</summary>
    public void Stop()
    {
        lock (gate)
        {
            if (started && ending == Ending.None)
            {
                EndSampling(Ending.Stop, Elapsed());
            }
        }
    }

    /// <summary>Ends the sampling and discards what waits for the encoder; the clip's file is left closed. After Stop or another end, nothing.</summary>
    public void Cancel()
    {
        lock (gate)
        {
            if (started && ending == Ending.None)
            {
                EndSampling(Ending.Cancel, Elapsed());
                Discard();
            }
        }
    }

    /// <summary>A start that went no further: <see cref="Ended"/> completes with the refusal, or with nothing when Cancel came first.</summary>
    private Outcome Refused(Failure refused)
    {
        bool cancelled;
        lock (gate)
        {
            cancelled = ending == Ending.Cancel;
            if (ending == Ending.None)
            {
                EndSampling(Ending.Failed, 0);
            }
        }

        ended.SetResult(cancelled ? Outcome<GifClip?>.Of(null) : refused);
        return refused;
    }

    private void Wake()
    {
        lock (gate)
        {
            woken = true;
            Monitor.PulseAll(gate);
        }
    }

    /// <summary>The sampler thread: one tick at each wake of its timer until the sampling ends.</summary>
    private void Sample()
    {
        try
        {
            while (Tick())
            {
            }
        }
        catch (Exception e)
        {
            lock (gate)
            {
                Fail(Unexpected(e));
            }
        }
        finally
        {
            timer!.Dispose();
            lock (gate)
            {
                samplerDone = true;
                Monitor.PulseAll(gate);
            }
        }
    }

    /// <summary>
    /// Waits for the next tick, then samples unless the queue is full or the cap is reached, and arms the timer for the
    /// next tick; false once the sampling has ended. Kept apart from the loop so no frame outlives its tick here.
    /// </summary>
    private bool Tick()
    {
        long atMs;
        long frameMs;
        lock (gate)
        {
            while (!woken && ending == Ending.None)
            {
                Monitor.Wait(gate);
            }

            woken = false;
            if (ending != Ending.None)
            {
                return false;
            }

            // One reading of the clock for the whole tick, so the first sample is exactly the timeline's start.
            var now = clock.GetTimestamp();
            anchor ??= now;
            var elapsed = clock.GetElapsedTime(anchor.Value, now);
            atMs = Math.Min(elapsed.Ticks / TimeSpan.TicksPerMillisecond, capMs);
            if (atMs >= capMs)
            {
                // Noticed at the first tick due at or after the cap; the clip ends at the cap itself (G15, G32).
                EndSampling(Ending.Stop, capMs);
                return false;
            }

            // A timer may fire up to its clock's granularity early; a tick never runs before it is due, so every
            // sample starts later than the one before.
            var early = due - elapsed;
            if (early > TimeSpan.Zero)
            {
                timer!.Change(early, Timeout.InfiniteTimeSpan);
                return true;
            }

            if (queue.Count >= QueueBound)
            {
                // Skipped before any grab: the frame before lasts until the next kept one (G15, T3).
                Arm(atMs);
                return true;
            }

            framesHeld++;
            // The frame's time is the latest place on the 70 ms timeline the wake has reached, not the wake itself, which
            // a system timer delays to its clock's next tick (15.625 ms on Windows): read off the wake, the frames would
            // last 8 and 6 cs by turns, and a tick run late after a slow grab 10 and then 4, where F3 fixes the rate at
            // 7 cs and only a skipped sample lengthens a frame (G15, T3); a wake past later places skips them, so the
            // frame before lasts until this one. Stop's moment and the cap stay the moments read.
            frameMs = elapsed.Ticks / SamplePeriod.Ticks * (SamplePeriod.Ticks / TimeSpan.TicksPerMillisecond);
        }

        var grab = screen.Grab(region, withPointer);
        lock (gate)
        {
            // A grab that started at or after Stop's moment is not part of the clip; one before it is, refused or not.
            var wanted = ending == Ending.None || (ending == Ending.Stop && atMs < stopMs);
            if (wanted && grab.Succeeded)
            {
                queue.Enqueue((grab.Value!, frameMs));
                Monitor.PulseAll(gate);
            }
            else
            {
                framesHeld--;
                if (wanted)
                {
                    Fail(grab.Failure!);
                }
            }

            if (ending != Ending.None)
            {
                return false;
            }

            Arm(atMs);
            return true;
        }
    }

    /// <summary>
    /// Arms the timer for the next tick after one that started at <paramref name="startedMs"/>: the next multiple of the
    /// period after it, or, when this tick's work ran past that, the latest multiple already passed, which runs at once;
    /// so the pace stays 70 ms rather than 70 ms plus the grab, and every sample starts later than the one before.
    /// </summary>
    private void Arm(long startedMs)
    {
        var done = clock.GetElapsedTime(anchor!.Value);
        var period = SamplePeriod.Ticks;
        var next = Math.Max(startedMs * TimeSpan.TicksPerMillisecond / period + 1, done.Ticks / period);
        due = TimeSpan.FromTicks(next * period);
        timer!.Change(due > done ? due - done : TimeSpan.Zero, Timeout.InfiniteTimeSpan);
    }

    /// <summary>The encoder thread: every queued frame appended in order, then the clip stopped or cancelled and <see cref="Ended"/> completed.</summary>
    private void Encode()
    {
        Outcome<GifClip?> outcome;
        try
        {
            while (AppendNext())
            {
            }

            outcome = Finish();
        }
        catch (Exception e)
        {
            lock (gate)
            {
                Fail(Unexpected(e));
            }

            outcome = Finish();
        }

        ended.TrySetResult(outcome);
    }

    /// <summary>Appends the next queued frame; false once nothing more is to be appended. Kept apart from the loop so the frame is let go when it returns.</summary>
    private bool AppendNext()
    {
        (MediaFrame Frame, long AtMs) next;
        lock (gate)
        {
            // Cancel and a failure discard the queue; Stop drains it once the sampler's last grab is in.
            while (queue.Count == 0 && !samplerDone && ending != Ending.Cancel && failure is null)
            {
                Monitor.Wait(gate);
            }

            if (queue.Count == 0)
            {
                return false;
            }

            next = queue.Dequeue();
        }

        var appended = encoder!.Append(next.Frame, next.AtMs);
        lock (gate)
        {
            framesHeld--;
            if (!appended.Succeeded)
            {
                Fail(new Failure(Action, name, appended.Failure.Reason));
            }

            return appended.Succeeded;
        }
    }

    /// <summary>Once the sampler has ended: the clip cancelled, or the failure, or the clip closed at Stop's moment.</summary>
    private Outcome<GifClip?> Finish()
    {
        Ending how;
        Failure? failed;
        long at;
        lock (gate)
        {
            while (!samplerDone)
            {
                Monitor.Wait(gate);
            }

            (how, failed, at) = (ending, failure, stopMs);
        }

        try
        {
            if (how == Ending.Cancel || failed is not null)
            {
                encoder!.Cancel();
                return how == Ending.Cancel ? Outcome<GifClip?>.Of(null) : failed!;
            }

            var stopped = encoder!.Stop(at);
            if (stopped.Succeeded)
            {
                return stopped;
            }

            // A refused Stop leaves the clip open: closed now, for the caller to discard.
            encoder.Cancel();
            return new Failure(Action, name, stopped.Failure.Reason);
        }
        catch (Exception e)
        {
            return Unexpected(e);
        }
    }

    /// <summary>Under the gate: the sampling ends now, its time frozen at <paramref name="atMs"/>.</summary>
    private void EndSampling(Ending how, long atMs)
    {
        (ending, stopMs) = (how, atMs);
        samplingEnded.TrySetResult(how == Ending.Stop);
        Monitor.PulseAll(gate);
    }

    /// <summary>
    /// Under the gate: a step the clip needed failed. The first failure is the outcome unless Cancel came first, which
    /// names nothing; the sampling ends if it still ran, and nothing more reaches the encoder.
    /// </summary>
    private void Fail(Failure failed)
    {
        if (ending == Ending.Cancel)
        {
            return;
        }

        failure ??= failed;
        if (ending == Ending.None)
        {
            EndSampling(Ending.Failed, Elapsed());
        }

        Discard();
    }

    /// <summary>Under the gate: the queued frames let go, unappended.</summary>
    private void Discard()
    {
        framesHeld -= queue.Count;
        queue.Clear();
        Monitor.PulseAll(gate);
    }

    /// <summary>Under the gate: the time on the clip's timeline now, in whole milliseconds, never past the cap; 0 before the first sample.</summary>
    private long Elapsed() =>
        anchor is { } first ? Math.Min(clock.GetElapsedTime(first).Ticks / TimeSpan.TicksPerMillisecond, capMs) : 0;

    /// <summary>A defect that reached a thread's edge, named as this action's failure rather than ending the process (G35).</summary>
    private Failure Unexpected(Exception e) => new(Action, name, e is OutOfMemoryException ? NoMemory : e.Message);
}
