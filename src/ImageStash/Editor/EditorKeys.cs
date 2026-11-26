using Avalonia.Input;

namespace ImageStash.Editor;

/// <summary>What a key pressed in the editor does.</summary>
public enum EditorKeyAction
{
    /// <summary>Nothing of the editor's: the focused control has the key, and Enter never pastes (G20).</summary>
    None,

    /// <summary><c>Ctrl+Z</c> (G25).</summary>
    Undo,

    /// <summary><c>Ctrl+Y</c> (G25).</summary>
    Redo,

    /// <summary><c>Ctrl+S</c> (G25).</summary>
    Save,

    /// <summary>Del: removes the selected layer (G23, G25).</summary>
    DeleteSelected,

    /// <summary>Esc with a layer selected.</summary>
    Deselect,

    /// <summary>Esc with nothing selected: leaves the item as it was (G20).</summary>
    Cancel,

    /// <summary>C: Compare, held until C is released (G25).</summary>
    Compare,

    /// <summary><c>Ctrl+Enter</c> or Esc in a text field: leaves it, closing a layer's typing (G25).</summary>
    LeaveText,

    /// <summary>Space: plays or pauses an animated item (G25).</summary>
    PlayPause,

    /// <summary>Right: steps one frame on (G25).</summary>
    StepForward,

    /// <summary>Left: steps one frame back (G25).</summary>
    StepBack,

    /// <summary>The key typing <c>[</c>: with Trim current, the kept frames start at the frame showing (G25).</summary>
    TrimStartHere,

    /// <summary>The key typing <c>]</c>: with Trim current, the kept frames end at the frame showing (G25).</summary>
    TrimEndHere,
}

/// <summary>
/// G25's keys in the editor: <c>Ctrl+Z</c>, <c>Ctrl+Y</c> and <c>Ctrl+S</c> from everywhere in it; Del, Esc, C, Space,
/// Left and Right, and the keys typing <c>[</c> and <c>]</c>, outside a text field, Esc deselecting before it cancels;
/// inside a text field (a layer's, or the picker's hex box) <c>Ctrl+Enter</c> and Esc leave it and every other key is
/// the field's, Enter a new line and Del a character. Enter is never the editor's, and never a handoff.
/// </summary>
public static class EditorKeys
{
    /// <param name="symbol">
    /// The character the key types on the keyboard layout in use, with the modifiers held (Avalonia's <c>KeySymbol</c>):
    /// <c>[</c> and <c>]</c> are found by it, so on a layout typing them with AltGr that press is the key.
    /// </param>
    public static EditorKeyAction Route(Key key, KeyModifiers modifiers, bool inTextField, bool selected, string? symbol = null) => (key, modifiers) switch
    {
        (Key.Z, KeyModifiers.Control) => EditorKeyAction.Undo,
        (Key.Y, KeyModifiers.Control) => EditorKeyAction.Redo,
        (Key.S, KeyModifiers.Control) => EditorKeyAction.Save,
        (Key.Enter, KeyModifiers.Control) or (Key.Escape, KeyModifiers.None) when inTextField => EditorKeyAction.LeaveText,
        _ when inTextField => EditorKeyAction.None,
        (Key.Delete, KeyModifiers.None) => EditorKeyAction.DeleteSelected,
        (Key.Escape, KeyModifiers.None) => selected ? EditorKeyAction.Deselect : EditorKeyAction.Cancel,
        (Key.C, KeyModifiers.None) => EditorKeyAction.Compare,
        (Key.Space, KeyModifiers.None) => EditorKeyAction.PlayPause,
        (Key.Right, KeyModifiers.None) => EditorKeyAction.StepForward,
        (Key.Left, KeyModifiers.None) => EditorKeyAction.StepBack,
        _ when symbol == "[" => EditorKeyAction.TrimStartHere,
        _ when symbol == "]" => EditorKeyAction.TrimEndHere,
        _ => EditorKeyAction.None,
    };
}
