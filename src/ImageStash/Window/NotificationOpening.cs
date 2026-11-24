using ImageStash.Seams;

namespace ImageStash.Window;

/// <summary>
/// The stash window a click on a capture notification opens (G16), and its paste target (G8). When the click brought
/// the notification in front, the target is set as it gained focus, to the window that had focus just before the
/// notification, by G8's rule, held to the notification so that a report the seam hears late still names that window,
/// and the stash window then coming in front from the notification keeps it. When the platform refused the notification
/// the activation, the window comes as the tray's summon brings it, its target the window it gains focus from. Then the
/// editor on the card's item, with Trim's tool for Trim, or that item selected on All, an open preview closed first,
/// while an open edit stays as it was left (G1). Nothing is pasted until Enter (N3).
/// </summary>
public sealed class NotificationOpening
{
    private readonly CaptureNotification captured;
    private readonly PasteTargetHolder pasteTarget;
    private readonly ITarget targets;
    private readonly Action<bool> summon;
    private readonly Editing editing;
    private readonly Action closePreview;

    /// <param name="summon">
    /// The window shown as from the tray, the flyout above the tray icon or the pinned window brought forward: told true,
    /// its coming in front is the window's own and keeps the target; told false, the summon reads it as the tray's does.
    /// </param>
    public NotificationOpening(CaptureNotification captured, PasteTargetHolder pasteTarget, ITarget targets, Action<bool> summon, Editing editing, Action closePreview)
    {
        this.captured = captured;
        this.pasteTarget = pasteTarget;
        this.targets = targets;
        this.summon = summon;
        this.editing = editing;
        this.closePreview = closePreview;
        captured.OpenRequested += asked => _ = Open(asked);
    }

    private Task Open(CaptureOpening asked)
    {
        if (asked.NotificationInFront)
        {
            // Read now, while the notification is in front and the window it came from is the one before it (G8).
            pasteTarget.FromNotificationClick(targets);
        }

        summon(asked.NotificationInFront);
        return asked.Edit is { } edit ? editing.OpenFromNotification(edit)
            : editing.IsOpen ? Task.CompletedTask
            : captured.ShowOnAll(asked.Item, closePreview);
    }
}
