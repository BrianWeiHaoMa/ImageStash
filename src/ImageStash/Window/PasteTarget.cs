using ImageStash.Seams;

namespace ImageStash.Window;

/// <summary>
/// The paste target the footer and the item menu name and Enter pastes into (G8): set in exactly two ways, at the open
/// shortcut's press to the window in front then, unless that was ImageStash itself; and whenever the stash window is
/// activated any other way, to the window that had focus just before it, passing over the shell's task switcher, none
/// when that was ImageStash's own (a dialog or a notification), the desktop, the taskbar or nothing. A target once set
/// stands until the next press or activation, but for a change before that activation which the seam hears of only after
/// it was read: the target is then read again, while ImageStash's window is still in front. Set on the UI thread;
/// <see cref="Current"/> is read from any thread, as the shortcut's press reads it.
/// </summary>
public sealed class PasteTargetHolder
{
    private volatile PasteTarget? current;

    // The seam whose answer at the latest activation set the target, while no press or other setting has replaced it.
    private volatile ITarget? following;
    private ITarget? listeningTo;
    private SynchronizationContext? setOn;

    /// <summary>Raised whenever <see cref="Current"/> changes.</summary>
    public event Action? Changed;

    /// <summary>The target Enter pastes into now, or null when Enter copies.</summary>
    public PasteTarget? Current => current;

    /// <summary>The handoff's name, as the footer, the item menu and the preview's button say it (G8, G19): Paste into the target's app, or Copy.</summary>
    public static string HandoffName(PasteTarget? target) => target is null ? "Copy" : $"Paste into {target.AppName}";

    /// <summary>The footer's hints: Enter's handoff, and while there is a target, <c>Ctrl+C</c>'s copy beside it (G8, G9).</summary>
    public static IReadOnlyList<string> Hints(PasteTarget? target) =>
        target is null ? [$"Enter · {HandoffName(target)}"] : [$"Enter · {HandoffName(target)}", "Ctrl+C · Copy"];

    /// <summary>The open shortcut was pressed with <paramref name="reading"/> in front: ImageStash's own leaves the target as it is.</summary>
    public void FromPress(ForegroundReading reading)
    {
        following = null;
        if (!reading.IsMine)
        {
            Set(reading.Target);
        }
    }

    /// <summary>
    /// The stash window activated itself (its summon, a pin, its showing again after a capture): the target stands as it
    /// is, and no late report reads it again, since the windows in front since then are ImageStash's own doing (G8).
    /// </summary>
    public void Keep() => following = null;

    /// <summary>The window that had focus just before was ImageStash's own: Enter copies (G8).</summary>
    public void None()
    {
        following = null;
        Set(null);
    }

    /// <summary>
    /// The stash window was activated other than by its own summon from the shortcut: the window that had focus just
    /// before it, read again whenever the seam hears late of a change that came before this read (G8).
    /// </summary>
    public void FromActivation(ITarget target)
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
        following = target;
        Set(target.LastBeforeMine());
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
            Set(target.LastBeforeMine());
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
