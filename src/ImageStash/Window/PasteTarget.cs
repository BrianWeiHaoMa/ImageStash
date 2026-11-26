using ImageStash.Seams;

namespace ImageStash.Window;

/// <summary>
/// The paste target the footer and the item menu name and Enter pastes into (G8): set at the open shortcut's press to
/// the window in front then, unless that was ImageStash itself; whenever the stash window is activated any other way, to
/// the window that had focus just before it, passing over the shell's task switcher, none when that was ImageStash's own
/// (a dialog or a notification), the desktop, the taskbar or nothing; and at a click on a notification that opens the
/// stash window, as the notification gains focus, to the window that had focus just before the notification, by the
/// same rule, which the stash window it opens keeps. A target once set stands until the next press or activation, but
/// for a change before that read which the seam hears of only after it: the target is then read again, while
/// ImageStash's window is still in front, for the window the read was made for. Set on the UI thread;
/// <see cref="Current"/> is read from any thread, as the shortcut's press reads it.
/// </summary>
public sealed class PasteTargetHolder
{
    private volatile PasteTarget? current;

    // The seam whose answer at the latest activation set the target, while no press or other setting has replaced it.
    private volatile ITarget? following;
    // That answer held to the notification a click brought in front, when that click set it.
    private volatile HeldBefore? held;
    private ITarget? listeningTo;
    private SynchronizationContext? setOn;

    /// <summary>Raised whenever <see cref="Current"/> changes.</summary>
    public event Action? Changed;

    /// <summary>The target Enter pastes into now, or null when Enter copies.</summary>
    public PasteTarget? Current => current;

    /// <summary>
    /// The handoff of <paramref name="items"/> items, as the footer, the item menu and the preview's button say it (G8,
    /// G19): Paste into the target's app, or Copy, saying how many while there are several ("Paste 3 items into Slack",
    /// "Copy 3 items"), so Enter never hands over a forgotten multi-selection unannounced. The preview's one item is never counted.
    /// </summary>
    public static string HandoffName(PasteTarget? target, int items)
    {
        var counted = items > 1 ? $" {Handoff.Counted(items)}" : "";
        return target is null ? $"Copy{counted}" : $"Paste{counted} into {target.AppName}";
    }

    /// <summary>What stands between a hint's key and what the key does ("Enter · Copy").</summary>
    public const string HintSeparator = " · ";

    /// <summary>The footer's hints with <paramref name="selected"/> items selected: Enter's handoff, and while there is a target, <c>Ctrl+C</c>'s copy beside it (G8, G9).</summary>
    public static IReadOnlyList<string> Hints(PasteTarget? target, int selected) =>
        target is null ? [$"Enter{HintSeparator}{HandoffName(target, selected)}"] : [$"Enter{HintSeparator}{HandoffName(target, selected)}", $"Ctrl+C{HintSeparator}Copy"];

    /// <summary>The open shortcut was pressed with <paramref name="reading"/> in front: ImageStash's own leaves the target as it is.</summary>
    public void FromPress(ForegroundReading reading)
    {
        following = null;
        held = null;
        if (!reading.IsMine)
        {
            Set(reading.Target);
        }
    }

    /// <summary>
    /// The stash window activated itself (its summon, a pin, its showing again after a capture): the target stands as it
    /// is, and no late report reads it again, since the windows in front since then are ImageStash's own doing (G8); but
    /// for a notification's click, whose read is held to that notification, which they do not change.
    /// </summary>
    public void Keep()
    {
        if (held is null)
        {
            following = null;
        }
    }

    /// <summary>The window that had focus just before was ImageStash's own: Enter copies (G8).</summary>
    public void None()
    {
        following = null;
        held = null;
        Set(null);
    }

    /// <summary>
    /// The stash window was activated other than by its own summon from the shortcut: the window that had focus just
    /// before it, read again whenever the seam hears late of a change that came before this read (G8).
    /// </summary>
    public void FromActivation(ITarget target)
    {
        Follow(target, heldTo: null);
        Set(target.LastBeforeMine());
    }

    /// <summary>
    /// A click on ImageStash's notification brought it in front to open the stash window (G16): the window that had focus
    /// just before the notification, by the same rule (G8), read again for the notification whenever the seam hears late
    /// of a change before this read, whatever ImageStash has put in front since; the stash window's coming in front from
    /// the notification is its own doing, which <see cref="Keep"/> leaves to this read.
    /// </summary>
    public void FromNotificationClick(ITarget target)
    {
        var before = target.HoldBeforeMine();
        Follow(target, before);
        Set(before.Read());
    }

    private void Follow(ITarget target, HeldBefore? heldTo)
    {
        if (listeningTo != target)
        {
            if (listeningTo is not null)
            {
                listeningTo.ReportedLate -= HeardLate;
            }

            listeningTo = target;
            target.ReportedLate += HeardLate;
        }

        setOn = SynchronizationContext.Current;
        held = heldTo;
        following = target;
    }

    // On the seam's thread: the read again is made where the target is set, or here when nothing posts there (checks).
    private void HeardLate()
    {
        if (setOn is { } context)
        {
            context.Post(_ => ReadAgain(), null);
        }
        else
        {
            ReadAgain();
        }
    }

    private void ReadAgain()
    {
        // Once another app is in front, the change is the user's next one, which the next press or activation reads.
        if (following is { } target && target.Foreground().IsMine)
        {
            Set(held is { } before ? before.Read() : target.LastBeforeMine());
        }
    }

    private void Set(PasteTarget? target)
    {
        if (target != current)
        {
            current = target;
            Changed?.Invoke();
        }
    }
}
