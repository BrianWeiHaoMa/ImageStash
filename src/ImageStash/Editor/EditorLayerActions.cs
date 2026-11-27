using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.VisualTree;

namespace ImageStash.Editor;

/// <summary>
/// The editor's pointer and keyboard buttons for the session's existing layer actions: Undo, Redo and Compare in the
/// head, absent on a fresh item until the edit holds a layer or a change and from then in their places for the rest of
/// it, Undo and Redo dimmed while they cannot act; and Delete in the place the option row keeps for it, shown only
/// while a layer is selected (G20, <see cref="ControlPresence"/>).
/// </summary>
internal sealed class EditorLayerActions
{
    private readonly Button compare;
    private readonly ShownForTheEdit history = new();
    private (EditorSession? Session, long? ItemId) binding;
    private Button? undo;
    private Button? redo;
    private Button? delete;

    /// <param name="compare">Compare, held to show the original, which stands after Redo and shows with the pair.</param>
    public EditorLayerActions(Button compare)
    {
        this.compare = compare;
        compare.Margin = new Thickness(6, 0, 0, 0);
    }

    public StackPanel History { get; } = new() { Orientation = Orientation.Horizontal };

    public ContentControl SelectedLayer { get; } = new()
    {
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Top,
    };

    /// <summary>Refreshes the controls and reports when the editor must replace a button's lost focus.</summary>
    public bool Show(EditorSession? session)
    {
        var hadFocus = HasFocus;
        var current = (session, session?.Item.Id);
        if (binding != current)
        {
            // A press or queued automation click belongs to the edit that supplied the button.
            binding = current;
            History.Children.Clear();
            SelectedLayer.Content = null;
            undo = redo = delete = null;
            if (session is not null)
            {
                undo = Make("\ue7a7", "Undo", "Undo (Ctrl+Z)", session.Undo);
                redo = Make("\ue7a6", "Redo", "Redo (Ctrl+Y)", session.Redo);
                delete = Make("\ue74d", "Delete", "Delete (Del)", session.DeleteSelected);
                delete.Height = EditorButtons.ChoiceHeight;
                History.Children.AddRange([undo, redo, compare]);
                SelectedLayer.Content = delete;
            }
        }

        if (session is not null)
        {
            var shown = history.In(session, HoldsLayerOrChange(session));
            ControlPresence.Show(undo!, shown, session.CanUndoEdit);
            ControlPresence.Show(redo!, shown, session.CanRedoEdit);
            ControlPresence.Show(compare, shown, true);
            ControlPresence.Show(delete!, session.Selected is not null, session.Accepts);
        }

        return hadFocus && !HasFocus;
    }

    // A button gone absent may still hold focus, out of sight; the editor's keys need it back on the canvas.
    private bool HasFocus => Focused(undo) || Focused(redo) || Focused(delete);

    private static bool Focused(Button? button) => button is { IsFocused: true, IsVisible: true };

    /// <summary>Whether the edit holds a layer, opened with one or added, or a step to undo or redo: Compare has something to show.</summary>
    private static bool HoldsLayerOrChange(EditorSession session) =>
        !session.Layers.IsEmpty || !session.Edit.RenderedLayers.IsEmpty || session.CanUndo || session.CanRedo;

    private Button Make(string glyph, string name, string tip, Action act)
    {
        var issuedFor = binding;
        var button = EditorButtons.Icon(glyph, name, tip);
        button.GettingFocus += (_, e) =>
        {
            // The action closes the typing run itself. Leaving it on pointer-down can make Undo take an older step.
            if (e.NavigationMethod == NavigationMethod.Pointer && issuedFor.Session is { Typing: true })
            {
                e.TryCancel();
            }
        };
        button.Click += (_, _) =>
        {
            if (binding == issuedFor && button.IsAttachedToVisualTree() && button.IsEffectivelyVisible && button.IsEffectivelyEnabled)
            {
                act();
            }
        };
        return button;
    }
}
