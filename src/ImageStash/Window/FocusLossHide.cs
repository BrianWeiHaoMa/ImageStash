namespace ImageStash.Window;

/// <summary>
/// When the flyout hides for losing focus to another app (G2): at once, unless the pointer's primary button is held as
/// the loss is judged, a press begun in that app, which may become a drag of a picture onto the flyout (G11). Then the
/// hide for that loss waits until the button has come up, and the window's ordinary rule judges again where focus is.
/// No event reaches a window that is not in front when a button comes up in another app, so the button is read again
/// every <see cref="Reading"/> while the wait lasts; a drag from outside that is over the window holds the wait, since
/// its drop is delivered after the button is up; a drop ends the wait in the drop's own turn, the button not read again,
/// so its import always ends in a hidden flyout. Only the hide for that loss waits: the open shortcut, Enter's paste and
/// a capture's start ask <see cref="Summon.FlyoutHides"/> as before. UI thread only; the timer hands its firing to it.
/// </summary>
public sealed class FocusLossHide
{
    /// <summary>How often the button is read again while the hide waits on it.</summary>
    public static readonly TimeSpan Reading = TimeSpan.FromMilliseconds(50);

    private readonly Func<bool> primaryButtonHeld;
    private readonly Func<bool> dragOver;
    private readonly Func<bool> focusLeft;
    private readonly Action hide;
    private readonly ITimer timer;

    /// <param name="primaryButtonHeld">Whether the pointer's primary button is held now, wherever it was pressed (the Quiet-windows seam).</param>
    /// <param name="dragOver">Whether a drag from outside the window is over it now (<see cref="DropTarget"/>).</param>
    /// <param name="focusLeft">The window's ordinary rule: whether the flyout shows while focus is with another app and nothing of its own keeps it.</param>
    /// <param name="hide">The window's one hide.</param>
    /// <param name="onUiThread">Runs the timer's firing on the UI thread.</param>
    public FocusLossHide(Func<bool> primaryButtonHeld, Func<bool> dragOver, Func<bool> focusLeft, Action hide, TimeProvider clock, Action<Action> onUiThread)
    {
        this.primaryButtonHeld = primaryButtonHeld;
        this.dragOver = dragOver;
        this.focusLeft = focusLeft;
        this.hide = hide;
        timer = clock.CreateTimer(_ => onUiThread(Read), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <summary>Whether the hide waits on a held button now.</summary>
    public bool Waiting { get; private set; }

    /// <summary>
    /// Focus may have gone to another app: the flyout hides now when the ordinary rule says so and the button is up, and
    /// waits when the button is held or a drag from outside is over it; a wait already running goes on.
    /// </summary>
    public void FocusMayHaveLeft()
    {
        if (Waiting || !focusLeft())
        {
            return;
        }

        // A quick drag may be over the window already, its button up and its drop not yet delivered.
        if (dragOver() || primaryButtonHeld())
        {
            Waiting = true;
            timer.Change(Reading, Timeout.InfiniteTimeSpan);
            return;
        }

        hide();
    }

    /// <summary>
    /// A drop from outside has been handed to the import: the press it ended is over, so a waiting flyout is judged now,
    /// in the drop's turn, and hides when focus is still with another app (G2, G12).
    /// </summary>
    public void Dropped()
    {
        if (Waiting)
        {
            Judge();
        }
    }

    /// <summary>The window has hidden, or focus has come back to it: nothing waits any more.</summary>
    public void Stop()
    {
        Waiting = false;
        timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    private void Read()
    {
        if (Waiting && (dragOver() || primaryButtonHeld()))
        {
            timer.Change(Reading, Timeout.InfiniteTimeSpan);
            return;
        }

        if (Waiting)
        {
            Judge();
            return;
        }

        // A reading handed over after the wait ended lets the timer go.
        Stop();
    }

    private void Judge()
    {
        Stop();
        if (focusLeft())
        {
            hide();
        }
    }
}
