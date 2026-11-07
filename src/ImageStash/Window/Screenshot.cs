using Avalonia.Controls;
using ImageStash.Capture;
using ImageStash.Media;
using ImageStash.Store;

namespace ImageStash.Window;

/// <summary>
/// Take a screenshot (G14), one act behind every way in: the stash window hidden or minimised until the capture ends
/// (G3); every display frozen and a region dragged over them (the capture session's); the region saved as one PNG item
/// with the auto name of the release (F12, F3), through the import commit inside the capture's span, so a kill leaves it
/// absent or complete (G28), even when its bytes equal an item's and with no tag, the window's scope, query and selection
/// left as the listing's refresh keeps them (G10, G11); the window back as it was; then, while Also copy is on, the item
/// on the clipboard as a copy puts it, recording no use (G8, G10); then the capture notification above the tray, saying
/// it is on the clipboard only when it was put there (G16). A stash still opening or that did not open is refused before
/// anything is grabbed or hidden; a press while the overlay is up does nothing, the overlay being the busy state, and one
/// after the release, while the last screenshot is still being saved, is refused by name; a cancel says nothing (E3);
/// every other refusal and failure is named above the tray, never only in the window, which may be hidden, and nothing
/// shows as saved before its file is in place (G35, R9). The clipboard is never read (N9, R5).
/// </summary>
public sealed class Screenshot(Listing listing, Notices notifications, CaptureNotification captured, Handoff handoff, CaptureSession capture, SettingsKeeper settings, CaptureHiding hiding, TopLevel owner, TimeSpan heldFileRetry)
{
    public const string Action = CaptureSession.Action;

    private const string Subject = "the screen";

    private const string StillSaving = "the last screenshot is still being saved";

    private readonly PictureImport pictures = new(heldFileRetry, failure => notifications.Raise(failure));

    /// <summary>
    /// True from the screenshot's start until its capture notification, or its failure, has been raised: a Move waits
    /// for it, then for that notification's Undo (State: Move).
    /// </summary>
    public bool InProgress { get; private set; }

    /// <summary>One act: the user's, from the shortcut, the tray menu or the window's own menu. UI thread.</summary>
    public async Task Take()
    {
        if (listing.CannotStartReason is { } notOpen)
        {
            notifications.Raise(new Failure(Action, Subject, notOpen));
            return;
        }

        if (capture.InProgress)
        {
            // The slot is the running capture's: this press neither hides the window again nor brings it back. While the
            // overlay is up it shows the act is busy; once released, nothing on screen does, so the press is refused by
            // name (G35). Nothing is awaited between this check and the capture's start, so the session never refuses
            // that start below.
            if (!capture.Selecting)
            {
                notifications.Raise(new Failure(Action, Subject, StillSaving));
            }

            return;
        }

        InProgress = true;
        try
        {
            await TakeAndSay();
        }
        finally
        {
            InProgress = false;
        }
    }

    /// <summary>The screenshot, then what it saved said above the tray.</summary>
    private async Task TakeAndSay()
    {
        ImportedFile? saved = null;
        Outcome<CapturedFile?> taken;
        hiding.HideForCapture();
        try
        {
            taken = await capture.TakeScreenshot(listing.Store!, async file =>
            {
                saved = await pictures.SaveStaged(listing.Store!, file.Name, file.WorkPath);
                // After any outcome: a row committed before a later step failed is listed or not as Store recorded it.
                await listing.Refresh();
            });
        }
        finally
        {
            // The capture has ended, committed and listed, cancelled or failed: the window back as it was, once no
            // capture is in progress any more, so a recording that is still running keeps it away until its end (G3).
            if (!capture.AnyInProgress)
            {
                hiding.ShowAfterCapture();
            }
        }

        if (!taken.Succeeded)
        {
            // A grab refused, the write refused: named as the step that failed names it, unchanged; nothing added.
            notifications.Raise(taken.Failure);
            return;
        }

        switch (saved)
        {
            case null:
                // Cancelled: Esc, or a click without a drag (G14, E3).
                break;
            case { Result: ImportResultKind.Added, Item: { } item }:
                captured.Raise(item, CaptureKind.Screenshot, await CopiedIfAsked(item), lengthMs: null);
                break;
            case { Result: ImportResultKind.Unreadable }:
                notifications.Raise(new Failure(Action, saved.Name, MediaDecoder.UnreadableReason));
                if (saved.Failure is { } discarded)
                {
                    notifications.Raise(discarded);
                }

                break;
            case { Failure: { } failure }:
                // Refused before the commit, which left nothing, or failed after it, which left the item as Store
                // committed it for the next start to finish: no copy and no capture notification (R9), and nothing
                // discarded, since a committed step still names the file (What crosses every boundary).
                notifications.Raise(failure);
                break;
        }
    }

    /// <summary>
    /// The saved item onto the clipboard while Also copy is on, as the capture ends (G14): whether it is there now. A
    /// write refused is named beside the capture notification, which then says in the stash only (G16, G35).
    /// </summary>
    private async Task<bool> CopiedIfAsked(StashItem item)
    {
        if (!settings.Current.AlsoCopyCapture)
        {
            return false;
        }

        var copied = await handoff.CopyCapture(owner, item);
        if (!copied.Succeeded)
        {
            notifications.Raise(copied.Failure);
        }

        return copied.Succeeded;
    }
}
