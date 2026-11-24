using Avalonia.Controls;
using ImageStash.Capture;
using ImageStash.Store;

namespace ImageStash.Window;

/// <summary>
/// Record a clip (G15), one act behind every way in, which also starts and stops the recording in progress: pressed
/// while its region waits to be recorded, it starts it, as Record does, and while it samples, it ends it, as Stop does,
/// whether or not a screenshot's overlay is up (F5). Otherwise it begins one: the stash window hidden or minimised until
/// no capture is in progress any more (G3); the region selected over the frozen displays, waiting under the frame and the
/// bar until Record and then recorded live (the capture session's); the clip saved as one GIF item with the auto name of
/// its start, Record (F12, F3), through the import commit inside the capture's span, so a kill leaves it absent or
/// complete (G28), even when its bytes equal an item's and with no tag; then, while Also copy is on, the item on the
/// clipboard as a copy puts it, recording no use (G8, G10); then the stash listed again, the window's scope, query and
/// selection kept (G10, G11); the window back as it was; then the capture notification above the tray with the clip's
/// length, saying it is on the clipboard only when it was put there (G16), in that order (<see cref="CaptureEnd"/>).
/// While the region waits, the tray's entry reads Start recording; while the recording samples, the icon carries its red
/// dot and the entry reads Stop recording, through <c>recording</c> (G15, G34). The bar names the record shortcut only while a press of it would act, read as
/// each recording starts and again at every change of the shortcuts (G33, <see cref="FollowShortcuts"/>). A stash
/// still opening or that did not open, or a maximum length in the settings
/// file below one second, is refused before anything is grabbed or hidden; a press with no recording in progress while
/// an overlay is up or while the clip is being saved does nothing, the overlay or the bar being
/// the busy state, and one while a screenshot is being saved after its release is refused by name; a cancel says
/// nothing (E3); every other refusal and failure is named above the tray, never only in the window, which may be
/// hidden, and nothing shows as saved before its file is in place (G35, R9). The clipboard is never read (N9, R5).
/// </summary>
public sealed class RecordClip
{
    public const string Action = Recorder.Action;

    private const string Subject = "the screen";

    private const string StillSaving = "the last screenshot is still being saved";

    private const string NoCap = "the recording's maximum length is not a number of seconds above zero";

    private readonly Listing listing;
    private readonly Notices notifications;
    private readonly CaptureSession capture;
    private readonly SettingsKeeper settings;
    private readonly CaptureHiding hiding;
    private readonly CaptureEnd end;
    private GlobalShortcuts? shortcuts;

    /// <param name="recording">Told the recording's stage at every change: the tray's Start recording, and its red dot and Stop recording.</param>
    public RecordClip(Listing listing, Notices notifications, CaptureNotification captured, Handoff handoff, CaptureSession capture, SettingsKeeper settings, CaptureHiding hiding, TopLevel owner, Action<RecordingStage> recording, TimeSpan heldFileRetry)
    {
        this.listing = listing;
        this.notifications = notifications;
        this.capture = capture;
        this.settings = settings;
        this.hiding = hiding;
        end = new CaptureEnd(listing, notifications, captured, handoff, settings, owner, heldFileRetry);
        // The wait and the sampling, not the saving: once Stop has been pressed nothing is left to start or stop.
        capture.RecordingChanged += () => recording(capture.Stage);
    }

    /// <summary>
    /// True from a recording's start until its capture notification, or its failure, has been raised: a Move waits for
    /// it, then for that notification's Undo (State: Move).
    /// </summary>
    public bool InProgress { get; private set; }

    /// <summary>
    /// From now on the recording's bar names the record shortcut of <paramref name="shortcuts"/> while a press of it would
    /// act, and follows each change of them (a pause, its toggle back, a rebind, one made unavailable) while it shows.
    /// </summary>
    public void FollowShortcuts(GlobalShortcuts shortcuts)
    {
        this.shortcuts = shortcuts;
        shortcuts.Changed += NameShortcut;
        NameShortcut();
    }

    /// <summary>One press: the user's, from the shortcut, the tray menu or the window's own menu. UI thread.</summary>
    public async Task StartOrStop()
    {
        if (capture.RecordingWaiting)
        {
            // G15 "Record, or the shortcut again, starts recording", unconditional: the same as Record on the bar.
            capture.StartRecording();
            return;
        }

        if (capture.RecordingSampling)
        {
            // G15 "on the shortcut", unconditional: the same as Stop on the bar, the window left as the end decides.
            capture.StopRecording();
            return;
        }

        // A new recording only: Record and Stop of the one in progress went ahead above (State: Move).
        if (listing.CannotStartReason is { } notOpen)
        {
            notifications.Raise(new Failure(Action, Subject, notOpen));
            return;
        }

        var current = settings.Current;
        if (current.RecordingMaxSeconds < 1)
        {
            notifications.Raise(new Failure(Action, Settings.FileName, NoCap));
            return;
        }

        if (capture.AnyInProgress)
        {
            // The slot is the running capture's: this press neither hides the window again nor brings it back. An
            // overlay, or the bar naming the clip's saving, shows the act is busy; a screenshot saved after its release
            // shows nothing, so the press is refused by name (G35). Nothing is awaited between this check and the
            // recording's start, so the session never refuses that start below.
            if (!capture.Selecting && !capture.RecordingInProgress)
            {
                notifications.Raise(new Failure(Action, Subject, StillSaving));
            }

            return;
        }

        InProgress = true;
        // Read afresh as it starts: a shortcut can be left unregistered without a change being raised, as at start.
        NameShortcut();
        try
        {
            await RecordAndSay(current);
        }
        finally
        {
            InProgress = false;
        }
    }

    /// <summary>The record shortcut's words, while a press of it would start and stop the recording, told to the capture for its bar.</summary>
    private void NameShortcut() =>
        capture.NameShortcut(shortcuts is null ? null : ActingShortcut.Of(shortcuts, ShortcutAct.Record)?.Text);

    /// <summary>The recording, from its selection to its end, then what it saved said above the tray.</summary>
    private async Task RecordAndSay(Settings current)
    {
        EndedCapture? ended = null;
        Outcome<RecordedClip?> recorded;
        hiding.HideForCapture();
        try
        {
            var options = new RecordingOptions(TimeSpan.FromSeconds(current.RecordingMaxSeconds), current.RecordPointer);
            recorded = await capture.Record(listing.Store!, options, async clip => ended = await end.Save(clip.Name, clip.WorkPath, clip.Clip.Written));
        }
        finally
        {
            // The window back as it was only once no capture is in progress any more: a screenshot started during the
            // recording keeps it away until that screenshot ends, which brings it back (G3).
            if (!capture.AnyInProgress)
            {
                hiding.ShowAfterCapture();
            }
        }

        if (!recorded.Succeeded)
        {
            // A grab refused, the clip's write or encoding refused, the frame not shown for the wait or the recording:
            // named as the step that failed names it, unchanged; nothing added, the clip discarded by the session.
            notifications.Raise(recorded.Failure);
            return;
        }

        // Null when cancelled: Esc or a click without a drag at the overlay, Cancel on the bar while the region waits or
        // while it records, or Stop before any frame (E3). The length is the one the encoder closed the clip at, never
        // the facts', which call a one-image clip a still (G16).
        if (ended is not null)
        {
            end.Say(ended, Action, CaptureKind.Recording, lengthMs: recorded.Value!.Clip.DurationMs);
        }
    }
}
