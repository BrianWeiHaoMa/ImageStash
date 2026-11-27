using Avalonia.Controls;

namespace ImageStash.Editor;

/// <summary>
/// The editor's one rule for a control that cannot always act (G20, G22): absent while it cannot act here, its row
/// keeping its place so nothing else moves; shown once it can, held in its place and dimmed while it cannot act at
/// this moment; able otherwise. Undo, Redo and Compare in the head, the layer's Delete, and Crop's and Trim's Reset
/// each take their state from <see cref="Show"/>.
/// </summary>
internal static class ControlPresence
{
    /// <param name="here">Whether the control has anything to act on in this state of the edit; false makes it absent.</param>
    /// <param name="now">Whether it can act at this moment; false holds it in its place, dimmed.</param>
    public static void Show(Control control, bool here, bool now)
    {
        control.IsVisible = here;
        control.IsEnabled = here && now;
    }
}

/// <summary>
/// Whether a group of controls has shown in an edit: once it has, it stays for the rest of that edit, so a pair of
/// opposite steps never trades places under the pointer (G20). Another edit starts without it.
/// </summary>
internal sealed class ShownForTheEdit
{
    private EditorSession? edit;
    private bool shown;

    /// <summary>Whether the group shows in <paramref name="session"/>: from the first moment <paramref name="canAct"/> held in it on.</summary>
    public bool In(EditorSession session, bool canAct)
    {
        if (!ReferenceEquals(edit, session))
        {
            edit = session;
            shown = false;
        }

        shown |= canAct;
        return shown;
    }
}
