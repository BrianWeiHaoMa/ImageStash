using Avalonia.Input;

namespace ImageStash.Editor;

/// <summary>What the editor's view does after an Esc press <see cref="EditorEscape"/> has handled.</summary>
public enum EscapeAct
{
    /// <summary>Not the editor's: the focused control or menu has the key.</summary>
    None,

    /// <summary>Taken, and nothing more to do: a held key's repeat, a deselection, or the row asking (G20).</summary>
    Taken,

    /// <summary>Close the picker or end the colour being picked, before the key reaches the layers.</summary>
    ClosePicker,

    /// <summary>The text field was left: focus goes back to the canvas (G25).</summary>
    LeftText,

    /// <summary>Leave the edit as Cancel does (G20).</summary>
    Leave,
}

/// <summary>
/// Esc in the editor, one act per press (G20, G25): Windows repeats the press of a held key with no release between, and
/// a held Esc would otherwise run on from the field through the selection and the asking to leaving a changed edit. Each
/// press does one thing: closes the picker; else leaves an open text field; else deselects; else leaves the edit, or on
/// a changed edit first has the row ask (<see cref="EditorSession.EscapeLeaves"/>).
/// </summary>
public sealed class EditorEscape
{
    private bool down;

    /// <summary>
    /// An Esc key-down in the editor on <paramref name="session"/>: <paramref name="pickerOpen"/> while the picker or the
    /// pane is open or a colour is being picked; <paramref name="inField"/> while focus is in a text box or outside the
    /// editor, as <see cref="EditorKeys.Route"/> takes it; <paramref name="inMenu"/> when a menu of the editor handed the
    /// key over, whose own Esc it is.
    /// </summary>
    public EscapeAct Pressed(EditorSession session, bool pickerOpen, bool inField, bool inMenu)
    {
        if (down)
        {
            return EscapeAct.Taken;
        }

        down = true;
        if (pickerOpen)
        {
            return EscapeAct.ClosePicker;
        }

        switch (EditorKeys.Route(Key.Escape, KeyModifiers.None, inField, session.Selected is not null))
        {
            case EditorKeyAction.LeaveText when session.Typing && !inMenu:
                session.EndTyping();
                return EscapeAct.LeftText;
            case EditorKeyAction.Deselect:
                session.Deselect();
                return EscapeAct.Taken;
            case EditorKeyAction.Cancel:
                return session.EscapeLeaves() ? EscapeAct.Leave : EscapeAct.Taken;
            default:
                return EscapeAct.None;
        }
    }

    /// <summary>
    /// Esc let go, or its release can no longer be seen (another edit shown, the window inactive): the next press acts.
    /// </summary>
    public void Released() => down = false;
}
