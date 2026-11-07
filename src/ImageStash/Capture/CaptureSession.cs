using Avalonia;
using ImageStash.Media;
using ImageStash.Seams;
using ImageStash.Store;

namespace ImageStash.Capture;

/// <summary>A screenshot written whole at a <c>.work\</c> path Store allocated, under its auto name (F12), once its caller's commit has run.</summary>
public sealed record CapturedFile(string WorkPath, string Name);

/// <summary>What a recording is asked for at its press: its maximum length (G32) and whether the pointer is drawn in (G15).</summary>
public sealed record RecordingOptions(TimeSpan Cap, bool Pointer);

/// <summary>A recording's clip, finished whole at a <c>.work\</c> path Store allocated, under the auto name taken at its start, Record (F12).</summary>
public sealed record RecordedClip(string WorkPath, string Name, GifClip Clip);

/// <summary>
/// Where a recording stands, as the tray shows it (G15, G34): its region waiting to be recorded; sampling; or neither,
/// with no recording in progress or its clip being finished and saved.
/// </summary>
public enum RecordingStage
{
    None,
    Waiting,
    Sampling,
}

/// <summary>
/// The one owner of "a capture is in progress" (Approach: at most one capture and one recording at a time), and the
/// steps of both captures. A screenshot (G14): every display grabbed once, on a pool thread, before any overlay shows, so
/// the screen is frozen by construction; the region selected over those grabs; its pixels cut from the frozen grab,
/// never grabbed again, which would take in the overlay itself; encoded by Media's one PNG encoder (F3, R4) and written
/// into <c>.work\</c> (R2); then the caller's commit, still in progress. A recording (G15): the same overlay over frozen
/// grabs; from the release the dimming is gone and the region waits, framed, its bar offering Record and Cancel, with
/// nothing written; from Record it is recorded live into <c>.work\</c> under the frame and the bar until Stop, the cap or
/// Cancel; then the caller's commit, still in progress, the bar naming the saving from the end of the sampling on. A
/// screenshot may start while a recording waits, samples or saves. Used on the UI thread only, where the overlays, the
/// frame and the bar show.
/// </summary>
public sealed class CaptureSession(IScreen screen, IRegionSelection selection, IRecordingFrame frame, ClipOpener openClip, TimeProvider clock, Action<Failure> named)
{
    public const string Action = "Take a screenshot";

    private const string NoMemory = "there is not enough memory to hold it";
    private const string Busy = "a capture is already in progress";

    private Recorder? recorder;

    // UI thread. The wait's answer while the region waits: true for Record, false for Cancel.
    private TaskCompletionSource<bool>? waiting;

    // UI thread. Set by Cancel while the bar offered it, so a clip the cap finished in the same moment is discarded too,
    // and a Cancel that comes after Record but before the recording has started ends it before it starts.
    private bool cancelled;

    /// <summary>
    /// True from the start of a screenshot until its commit has run, or it was cancelled or failed; and during a
    /// recording's selection, until its overlay has closed.
    /// </summary>
    public bool InProgress { get; private set; }

    /// <summary>True while the displays are grabbed and the overlay is up, which shows the capture is busy (G35).</summary>
    public bool Selecting { get; private set; }

    /// <summary>True from the release of a recording's region, through its wait, until its commit has run, or it was cancelled or failed.</summary>
    public bool RecordingInProgress { get; private set; }

    /// <summary>True while the released region waits to be recorded: from the frame's showing until Record or Cancel answers it.</summary>
    public bool RecordingWaiting { get; private set; }

    /// <summary>True while the recording samples: from Record until Stop, the cap, Cancel or a failure ended the sampling.</summary>
    public bool RecordingSampling { get; private set; }

    /// <summary>The recording's stage as the tray shows it.</summary>
    public RecordingStage Stage => RecordingWaiting ? RecordingStage.Waiting : RecordingSampling ? RecordingStage.Sampling : RecordingStage.None;

    /// <summary>Whether any capture is in progress: the one reading of "no capture is in progress any more" (G3).</summary>
    public bool AnyInProgress => InProgress || RecordingInProgress;

    /// <summary>Raised whenever <see cref="Stage"/> changes, on the thread that changed it, the UI thread in the product.</summary>
    public event Action? RecordingChanged;

    /// <summary>
    /// One screenshot: the file written and <paramref name="finish"/> run on it, which commits it; a done outcome with
    /// null when the user cancelled, which says nothing (E3); or the failure that stopped it, named as the step that
    /// failed names it: a refused grab unchanged, before any overlay shows (E5, G35); a refused encode or write as this
    /// action on the auto name. Refused at once, showing nothing, while another screenshot or a recording's selection is
    /// in progress; a recording sampling or saving does not stop it. A partial file the write could not delete is named
    /// through the constructor's <c>named</c>.
    /// </summary>
    public async Task<Outcome<CapturedFile?>> TakeScreenshot(StashStore store, Func<CapturedFile, Task> finish)
    {
        if (InProgress)
        {
            return new Failure(Action, "the screen", Busy);
        }

        InProgress = true;
        Selecting = true;
        try
        {
            var selected = await SelectOverFrozenGrabs();
            Selecting = false;
            if (!selected.Succeeded)
            {
                return selected.Failure;
            }

            if (selected.Value is not (var name, var picture))
            {
                return Outcome<CapturedFile?>.Of(null);
            }

            var png = await Task.Run(() => PngEncoder.Encode(picture, name));
            if (!png.Succeeded)
            {
                return new Failure(Action, name, png.Failure.Reason);
            }

            var written = await store.WriteWork(".png", () => Task.FromResult<Stream>(new MemoryStream(png.Value!, writable: false)), Action, name, named);
            if (!written.Succeeded)
            {
                return written.Failure;
            }

            var file = new CapturedFile(written.Value!, name);
            await finish(file);
            return Outcome<CapturedFile?>.Of(file);
        }
        finally
        {
            Selecting = false;
            InProgress = false;
        }
    }

    /// <summary>
    /// One recording of a region the user selects (G15): its clip written and <paramref name="finish"/> run on it, which
    /// commits it; a done outcome with null when the user cancelled the selection, the wait or the recording, or Stop came
    /// before any frame, which says nothing (E3); or the failure that ended it, unchanged as its step names it: a refused
    /// grab as the screen names it (E5), a frame the platform will not show for the wait as this action on the screen, a
    /// refused open, encode or close, or a frame that will not take its recording look, as this action on the auto name.
    /// A clip a cancel or a failure leaves is discarded through Store (R2), and a refused discard is named through the
    /// constructor's <c>named</c>. Refused at once, showing nothing, while any capture is in progress (Approach). From the
    /// release the overlay is gone and the screen live, and the frame and the bar show until <paramref name="finish"/> has
    /// run (F4, G35): first while the region waits for Record, with nothing written, so a kill then leaves nothing (G28,
    /// R2); from Record while it records, named for that moment (F12), the cap in <paramref name="options"/> counted from it.
    /// </summary>
    public async Task<Outcome<RecordedClip?>> Record(StashStore store, RecordingOptions options, Func<RecordedClip, Task> finish)
    {
        if (AnyInProgress)
        {
            return new Failure(Recorder.Action, "the screen", Busy);
        }

        InProgress = true;
        Selecting = true;
        SelectedRegion region;
        try
        {
            var selected = await SelectToRecord();
            if (!selected.Succeeded)
            {
                return selected.Failure;
            }

            if (selected.Value is not { } released)
            {
                return Outcome<RecordedClip?>.Of(null);
            }

            // Past the overlay the recording is the capture in progress, and a screenshot may start beside it.
            region = released;
            RecordingInProgress = true;
        }
        finally
        {
            Selecting = false;
            InProgress = false;
        }

        try
        {
            var waited = await WaitForRecord(region, options.Cap);
            if (!waited.Succeeded)
            {
                return waited.Failure;
            }

            return waited.Value ? await RecordLive(store, region, options, finish) : Outcome<RecordedClip?>.Of(null);
        }
        finally
        {
            // Whatever ended this, nothing of the recording shows past it (F4).
            frame.Close();
            (recorder, waiting) = (null, null);
            SetStage(waiting: false, sampling: false);
            RecordingInProgress = false;
        }
    }

    /// <summary>
    /// While the region waits to be recorded: it starts, its name and its timeline from this moment (F12, G15). Otherwise
    /// nothing; while it samples, Stop is what ends it.
    /// </summary>
    public void StartRecording()
    {
        if (RecordingWaiting)
        {
            waiting?.TrySetResult(true);
        }
    }

    /// <summary>
    /// While the recording samples: the sampling ends at this moment and the clip is finished and saved, the bar naming
    /// the saving at once (G15, G35). Otherwise nothing.
    /// </summary>
    public void StopRecording()
    {
        if (RecordingSampling && recorder is { } running)
        {
            // The recorder told first, so Stop's moment is the press's; the flag cleared before it, so the end of the
            // sampling, seen from another thread, finds nothing left to say.
            RecordingSampling = false;
            running.Stop();
            frame.Saving();
            RecordingChanged?.Invoke();
        }
    }

    /// <summary>
    /// While the region waits, Record pressed but the recording not yet started included: it ends, nothing written. While
    /// the recording samples, as far as the bar showing Cancel knows: it is discarded. Either adds nothing and says
    /// nothing (G15, E3), even when the cap ended the sampling a moment before. Otherwise, after Stop included, nothing.
    /// </summary>
    public void CancelRecording()
    {
        if (RecordingWaiting)
        {
            cancelled = true;
            waiting?.TrySetResult(false);
        }
        else if (RecordingSampling && recorder is { } running)
        {
            cancelled = true;
            RecordingSampling = false;
            running.Cancel();
            RecordingChanged?.Invoke();
        }
    }

    /// <summary>
    /// The released region waiting, framed, its bar offering Record and Cancel, until one of them, or the record shortcut,
    /// answers it: true for Record, false for Cancel; or the failure that keeps the frame from showing, as this action on
    /// the screen, since no name exists before Record (G35). Nothing is written meanwhile (G28, R2).
    /// </summary>
    private async Task<Outcome<bool>> WaitForRecord(SelectedRegion region, TimeSpan cap)
    {
        // The press that answers returns before the recording starts, so its own handler is never reentered by the start.
        (waiting, cancelled) = (new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously), false);
        if (Shown(() => frame.Show(region, cap, StartRecording, StopRecording, CancelRecording), "the screen").Failure is { } unseen)
        {
            return unseen;
        }

        SetStage(waiting: true, sampling: false);
        var record = await waiting.Task;
        return Outcome<bool>.Of(record && !cancelled);
    }

    /// <summary>The live part of a recording, from Record: the name, the clip's path, the recorder, the frame's recording look, and the end.</summary>
    private async Task<Outcome<RecordedClip?>> RecordLive(StashStore store, SelectedRegion region, RecordingOptions options, Func<RecordedClip, Task> finish)
    {
        // F12: the recording's start, which is Record, never the release's moment, nor Stop's, nor the commit's.
        var name = AutoName.Recording(clock.GetLocalNow());
        var path = store.AllocateWorkPath(".gif");
        if (!path.Succeeded)
        {
            return new Failure(Recorder.Action, name, path.Failure.Reason);
        }

        var workPath = path.Value!;
        var running = new Recorder(screen, openClip, clock);
        var started = running.Start(region.Rectangle, options.Pointer, options.Cap, workPath, name);
        if (!started.Succeeded)
        {
            // An encoder that opened before a thread failed to start leaves its file closed for the discard.
            await running.Ended;
            await DiscardClip(store, workPath);
            return started.Failure;
        }

        recorder = running;
        try
        {
            if (Shown(() => frame.Recording(running), name).Failure is { } unseen)
            {
                // A recording the frame cannot mark ends at its start, so nothing is recorded unmarked (G15, G35).
                running.Cancel();
                await running.Ended;
                await DiscardClip(store, workPath);
                return unseen;
            }

            SetStage(waiting: false, sampling: true);
            // The cap, a refused grab and an encoder failure end the sampling too, always before the clip has ended:
            // the bar names the saving of a clip being finished, and offers no Stop or Cancel that would do nothing.
            if (await running.SamplingEnded && RecordingSampling)
            {
                frame.Saving();
            }

            SetStage(waiting: false, sampling: false);
            var ended = await running.Ended;
            if (!cancelled && ended.Succeeded && ended.Value is { } clip)
            {
                var recorded = new RecordedClip(workPath, name, clip);
                await finish(recorded);
                return Outcome<RecordedClip?>.Of(recorded);
            }

            await DiscardClip(store, workPath);
            return ended.Succeeded ? Outcome<RecordedClip?>.Of(null) : ended.Failure;
        }
        finally
        {
            // Whatever ended this, no thread of the recording samples past it; after its end, Cancel does nothing.
            running.Cancel();
        }
    }

    /// <summary>
    /// The frame and the bar shown as <paramref name="show"/> shows them, or the failure that keeps them from marking the
    /// region, named as this action on <paramref name="subject"/>.
    /// </summary>
    private static Outcome Shown(Func<Outcome> show, string subject)
    {
        try
        {
            return show().Failure is { } refused
                ? new Failure(Recorder.Action, subject, refused.Reason)
                : Outcome.Done;
        }
        catch (Exception e)
        {
            // A window the platform will not make is this recording's failure, never one that ends ImageStash (G35).
            return new Failure(Recorder.Action, subject, e is OutOfMemoryException ? NoMemory : e.Message);
        }
    }

    /// <summary>A clip the recording did not finish, deleted through Store; one still held after the retries is named and left to the next start's clearing of <c>.work\</c>.</summary>
    private async Task DiscardClip(StashStore store, string workPath)
    {
        if (File.Exists(workPath) && (await store.Discard(workPath)).Failure is { } refused)
        {
            named(refused);
        }
    }

    private void SetStage(bool waiting, bool sampling)
    {
        if (RecordingWaiting != waiting || RecordingSampling != sampling)
        {
            (RecordingWaiting, RecordingSampling) = (waiting, sampling);
            RecordingChanged?.Invoke();
        }
    }

    /// <summary>
    /// The grabs, the selection over them and the region's pixels cut from them: the auto name at the release's moment
    /// with those pixels, or null when cancelled. The grabs are let go when this returns, before the write and the
    /// commit, so nothing keeps a display's pixels past the selection.
    /// </summary>
    private async Task<Outcome<(string Name, MediaFrame Picture)?>> SelectOverFrozenGrabs()
    {
        var selected = await SelectOverGrabs(CaptureMode.Screenshot, Action);
        if (!selected.Succeeded)
        {
            return selected.Failure;
        }

        if (selected.Value is not var (region, grabs))
        {
            return Outcome<(string, MediaFrame)?>.Of(null);
        }

        // F12: the moment of the release, read as the selection returns, not the press's nor the write's.
        var name = AutoName.Screenshot(clock.GetLocalNow());
        var grab = grabs.Single(g => g.Display == region.Display);
        try
        {
            return Outcome<(string, MediaFrame)?>.Of((name, Cut(grab, region.Rectangle)));
        }
        catch (OutOfMemoryException)
        {
            return new Failure(Action, name, NoMemory);
        }
    }

    /// <summary>The region a recording's overlay returned, or null when cancelled; its grabs are let go when this returns, before the recording starts.</summary>
    private async Task<Outcome<SelectedRegion?>> SelectToRecord()
    {
        var selected = await SelectOverGrabs(CaptureMode.Recording, Recorder.Action);
        return selected.Succeeded ? Outcome<SelectedRegion?>.Of(selected.Value?.Region) : selected.Failure;
    }

    /// <summary>
    /// Every display grabbed, then the overlay over those grabs, telling what its release starts: the region with the
    /// grabs, or null when cancelled; a refused grab unchanged, before any overlay shows (E5, G35); no room for the
    /// overlay named as <paramref name="action"/>. Once the caller lets go of what this returns, nothing keeps a display's pixels.
    /// </summary>
    private async Task<Outcome<(SelectedRegion Region, IReadOnlyList<DisplayGrab> Grabs)?>> SelectOverGrabs(CaptureMode mode, string action)
    {
        var grabs = await Task.Run(GrabEveryDisplay);
        if (!grabs.Succeeded)
        {
            return grabs.Failure;
        }

        SelectedRegion? region;
        try
        {
            region = await selection.Select(grabs.Value!, mode);
        }
        catch (OutOfMemoryException)
        {
            // Displays the PC could grab may still leave no room for the overlay's copy of them beside the grabs (G35).
            return new Failure(action, "the screen", NoMemory);
        }

        return Outcome<(SelectedRegion, IReadOnlyList<DisplayGrab>)?>.Of(region is null ? null : (region, grabs.Value!));
    }

    /// <summary>Every display, each grabbed once through the seam with its bounds; the first refusal ends it, unchanged.</summary>
    private Outcome<IReadOnlyList<DisplayGrab>> GrabEveryDisplay()
    {
        var grabs = new List<DisplayGrab>();
        foreach (var display in screen.Displays())
        {
            // G14 saves the screen's pixels and names no pointer; only a recording draws it in (G15).
            var grab = screen.Grab(display.Bounds, withPointer: false);
            if (!grab.Succeeded)
            {
                return grab.Failure;
            }

            grabs.Add(new DisplayGrab(display, grab.Value!));
        }

        return Outcome<IReadOnlyList<DisplayGrab>>.Of(grabs);
    }

    /// <summary>The pixels of <paramref name="rectangle"/>, on the desktop and inside the grab's display, as a frame of their own.</summary>
    private static MediaFrame Cut(DisplayGrab grab, PixelRect rectangle)
    {
        var bounds = grab.Display.Bounds;
        if (rectangle.Width < 1 || rectangle.Height < 1 || !bounds.Contains(rectangle))
        {
            throw new ArgumentException($"The region {rectangle} does not lie on the display at {bounds}.", nameof(rectangle));
        }

        var pixels = new byte[4L * rectangle.Width * rectangle.Height];
        var rowBytes = rectangle.Width * 4;
        for (var y = 0; y < rectangle.Height; y++)
        {
            var from = ((long)(rectangle.Y - bounds.Y + y) * grab.Frame.Width + (rectangle.X - bounds.X)) * 4;
            Array.Copy(grab.Frame.Pixels, from, pixels, (long)y * rowBytes, rowBytes);
        }

        return new MediaFrame(rectangle.Width, rectangle.Height, 0, pixels);
    }
}
