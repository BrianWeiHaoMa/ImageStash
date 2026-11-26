using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.VisualTree;

namespace ImageStash.Editor;

/// <summary>The editor's pointer and keyboard buttons for the session's existing layer actions.</summary>
internal sealed class EditorLayerActions
{
    private (EditorSession? Session, long? ItemId) binding;
    private Button? undo;
    private Button? redo;
    private Button? delete;

    public StackPanel History { get; } = new()
    {
        Orientation = Orientation.Horizontal,
        Margin = new Thickness(0, 0, 6, 0),
    };

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
                History.Children.AddRange([undo, redo]);
                SelectedLayer.Content = delete;
            }
        }

        if (session is not null)
        {
            undo!.IsEnabled = session.CanUndoEdit;
            redo!.IsEnabled = session.CanRedoEdit;
            delete!.IsEnabled = session.Accepts && session.Selected is not null;
        }

        return hadFocus && !HasFocus;
    }

    private bool HasFocus => undo is { IsFocused: true } || redo is { IsFocused: true } || delete is { IsFocused: true };

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
