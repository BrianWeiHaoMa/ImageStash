using Avalonia.Input;

namespace ImageStash.Window;

/// <summary>What a key press does in the stash window, before the focused control sees it.</summary>
public enum KeyAction
{
    /// <summary>The focused control handles the key as it would anywhere: the search box takes text, Space and Backspace included.</summary>
    None,
    MoveUp,
    MoveDown,
    MoveLeft,
    MoveRight,
    FocusGrid,
    SelectAll,
    Escape,

    /// <summary><c>Ctrl+O</c>: Import files… (G7, G12).</summary>
    ImportFiles,

    /// <summary><c>Ctrl+V</c>: Paste image, from every place, inside a field too, which has no paste of its own (G7, G13).</summary>
    Paste,

    /// <summary>Enter: the handoff on the selection (G7, G8).</summary>
    Handoff,

    /// <summary><c>Ctrl+C</c> with no text selected in the box: the same clipboard content as the handoff, and the window stays (G7, G9).</summary>
    Copy,

    /// <summary><c>Ctrl</c> with <c>+</c>: the next larger thumbnail size (G6).</summary>
    LargerThumbnails,

    /// <summary><c>Ctrl</c> with <c>-</c>: the next smaller thumbnail size (G6).</summary>
    SmallerThumbnails,

    /// <summary>Del in the grid: Delete on the selection (G7, G18); in the search box Del deletes a character.</summary>
    Delete,

    /// <summary><c>Ctrl+D</c> in the grid: toggles favorite on the selection (G7, G17); the search box takes no action on it.</summary>
    ToggleFavorite,

    /// <summary>Space in the grid: the preview on the focused item; in the preview, closes it (G7, G19).</summary>
    TogglePreview,

    /// <summary>F2 in the grid: the preview on the focused item with its name in edit; in the preview, its name in edit (G7, G17).</summary>
    Rename,

    /// <summary><c>Ctrl+E</c> in the box, the grid or the preview: the editor on the one selected item, the preview's while it is open (G7, G20).</summary>
    Edit,
}

/// <summary>Where focus is when a key is pressed in the stash window.</summary>
public enum KeyPlace
{
    SearchBox,
    Grid,

    /// <summary>A text field inside the window other than the search box, such as the tag field.</summary>
    Field,

    /// <summary>The open preview, with no text field and none of its buttons focused.</summary>
    Preview,

    /// <summary>A button of the open preview, focused.</summary>
    PreviewButton,

    /// <summary>The open editor, over everything else, with no text field focused: its keys are its own (G20, G25).</summary>
    Editor,
}

/// <summary>
/// G7's keys, in one place for both states (G2): the search box is an ordinary text box, so Left, Right, Home, End,
/// Shift with them, <c>Ctrl+A</c>, <c>Ctrl+X</c> and <c>Ctrl+Z</c> are its own; Up and Down move the grid's selection
/// from the box or the grid without moving focus, and Left and Right move it from the grid; Tab in the box moves focus
/// to the grid; <c>Ctrl+A</c> in the grid selects every item; Enter, Esc, <c>Ctrl+O</c> and G6's <c>Ctrl</c> with
/// <c>+</c> or <c>-</c>, on the main keys or the numeric keypad's, work from both; <c>Ctrl+C</c> copies the box's
/// selected text while it has one, which is the box's own key, and otherwise the selected items, from both; Del in the
/// grid deletes the selection, and in the box a character; <c>Ctrl+D</c> in the grid toggles favorite; Space in the
/// grid opens the preview, and F2 opens it with the name in edit; and a character typed in the grid goes into the box
/// with focus. <c>Ctrl+V</c> is the window's paste from every place, the one read of the clipboard, and
/// <c>Shift+Insert</c> is nothing, since no text box has a paste of its own (G13, N9). Inside any other text field the
/// keys it uses are its own (typed characters, Space, Backspace, Del, the arrows, Enter, Esc, F2, its text's <c>Ctrl+A</c> and
/// <c>Ctrl+C</c>), so none of them deletes, hands off, closes or moves the selection; only <c>Ctrl+O</c>,
/// <c>Ctrl+V</c> and the thumbnail size, which a field leaves unused, still reach the window. While the preview covers
/// the box and the grid, Enter, <c>Ctrl+C</c>, Del, <c>Ctrl+D</c> and F2 act on its item, Esc and Space close it, a
/// focused button of its own takes Space as its press, <c>Ctrl+O</c>, <c>Ctrl+V</c> and the thumbnail size still work,
/// and the arrows, <c>Ctrl+A</c> and typing reach neither the grid nor the box, which it hides (G7, G17, G19).
/// <c>Ctrl+E</c> in the box, the grid or the preview opens the editor. While the editor is open over all of it, only
/// <c>Ctrl+C</c>, which copies the item as last saved, <c>Ctrl+O</c>, <c>Ctrl+V</c> and the thumbnail size still reach
/// the window; every other key is the editor's, so Enter never pastes (G20, G9, G11, G13).
/// </summary>
public static class StashKeys
{
    /// <param name="boxTextSelected">The search box has text selected, as it can only while it has focus: it clears its selection on losing focus.</param>
    public static KeyAction Route(Key key, KeyModifiers modifiers, KeyPlace place, bool boxTextSelected = false) => place switch
    {
        KeyPlace.Field => (key, modifiers) switch
        {
            (Key.O, KeyModifiers.Control) => KeyAction.ImportFiles,
            (Key.V, KeyModifiers.Control) => KeyAction.Paste,
            (Key.OemPlus or Key.Add, KeyModifiers.Control or (KeyModifiers.Control | KeyModifiers.Shift)) => KeyAction.LargerThumbnails,
            (Key.OemMinus or Key.Subtract, KeyModifiers.Control) => KeyAction.SmallerThumbnails,
            _ => KeyAction.None,
        },
        KeyPlace.Editor => (key, modifiers) switch
        {
            (Key.C, KeyModifiers.Control) => KeyAction.Copy,
            (Key.O, KeyModifiers.Control) => KeyAction.ImportFiles,
            (Key.V, KeyModifiers.Control) => KeyAction.Paste,
            (Key.OemPlus or Key.Add, KeyModifiers.Control or (KeyModifiers.Control | KeyModifiers.Shift)) => KeyAction.LargerThumbnails,
            (Key.OemMinus or Key.Subtract, KeyModifiers.Control) => KeyAction.SmallerThumbnails,
            _ => KeyAction.None,
        },
        KeyPlace.Preview or KeyPlace.PreviewButton => (key, modifiers) switch
        {
            (Key.Enter, KeyModifiers.None) => KeyAction.Handoff,
            (Key.E, KeyModifiers.Control) => KeyAction.Edit,
            (Key.C, KeyModifiers.Control) => KeyAction.Copy,
            (Key.Delete, KeyModifiers.None) => KeyAction.Delete,
            (Key.D, KeyModifiers.Control) => KeyAction.ToggleFavorite,
            (Key.F2, KeyModifiers.None) => KeyAction.Rename,
            (Key.Escape, KeyModifiers.None) => KeyAction.Escape,
            (Key.Space, KeyModifiers.None) when place == KeyPlace.Preview => KeyAction.TogglePreview,
            (Key.O, KeyModifiers.Control) => KeyAction.ImportFiles,
            (Key.V, KeyModifiers.Control) => KeyAction.Paste,
            (Key.OemPlus or Key.Add, KeyModifiers.Control or (KeyModifiers.Control | KeyModifiers.Shift)) => KeyAction.LargerThumbnails,
            (Key.OemMinus or Key.Subtract, KeyModifiers.Control) => KeyAction.SmallerThumbnails,
            _ => KeyAction.None,
        },
        _ => Route(key, modifiers, inSearchBox: place == KeyPlace.SearchBox, boxTextSelected),
    };

    /// <summary>
    /// Where the window's keys go: a text field other than the search box keeps its own; the open editor covers all the
    /// rest, so it is next; then the open preview, which covers the box and the grid, its own focused button apart; then
    /// the box, else the grid.
    /// </summary>
    public static KeyPlace PlaceOf(bool inSearchBox, bool inOtherField, bool previewOpen, bool onPreviewButton, bool editOpen) =>
        inOtherField ? KeyPlace.Field
        : editOpen ? KeyPlace.Editor
        : previewOpen ? (onPreviewButton ? KeyPlace.PreviewButton : KeyPlace.Preview)
        : inSearchBox ? KeyPlace.SearchBox
        : KeyPlace.Grid;

    private static KeyAction Route(Key key, KeyModifiers modifiers, bool inSearchBox, bool boxTextSelected) => (key, modifiers) switch
    {
        (Key.Up, KeyModifiers.None) => KeyAction.MoveUp,
        (Key.Down, KeyModifiers.None) => KeyAction.MoveDown,
        (Key.Left, KeyModifiers.None) when !inSearchBox => KeyAction.MoveLeft,
        (Key.Right, KeyModifiers.None) when !inSearchBox => KeyAction.MoveRight,
        (Key.Escape, KeyModifiers.None) => KeyAction.Escape,
        (Key.Enter, KeyModifiers.None) => KeyAction.Handoff,
        (Key.C, KeyModifiers.Control) when !(inSearchBox && boxTextSelected) => KeyAction.Copy,
        (Key.E, KeyModifiers.Control) => KeyAction.Edit,
        (Key.O, KeyModifiers.Control) => KeyAction.ImportFiles,
        (Key.V, KeyModifiers.Control) => KeyAction.Paste,
        // The main keyboard's + is Shift with = on many layouts, so Ctrl+= and Ctrl+Shift+= both mean Ctrl with +.
        (Key.OemPlus or Key.Add, KeyModifiers.Control or (KeyModifiers.Control | KeyModifiers.Shift)) => KeyAction.LargerThumbnails,
        (Key.OemMinus or Key.Subtract, KeyModifiers.Control) => KeyAction.SmallerThumbnails,
        (Key.Tab, KeyModifiers.None) when inSearchBox => KeyAction.FocusGrid,
        (Key.A, KeyModifiers.Control) when !inSearchBox => KeyAction.SelectAll,
        (Key.Delete, KeyModifiers.None) when !inSearchBox => KeyAction.Delete,
        (Key.D, KeyModifiers.Control) when !inSearchBox => KeyAction.ToggleFavorite,
        (Key.Space, KeyModifiers.None) when !inSearchBox => KeyAction.TogglePreview,
        (Key.F2, KeyModifiers.None) when !inSearchBox => KeyAction.Rename,
        _ => KeyAction.None,
    };

    /// <summary>
    /// Whether text typed while the grid has focus goes into the search box. Space does not: in the grid it acts on the
    /// selection (G7).
    /// </summary>
    public static bool TypesIntoSearch(string? text) =>
        !string.IsNullOrEmpty(text) && text != " " && !text.Any(char.IsControl);

    public static Direction? Direction(KeyAction action) => action switch
    {
        KeyAction.MoveUp => Window.Direction.Up,
        KeyAction.MoveDown => Window.Direction.Down,
        KeyAction.MoveLeft => Window.Direction.Left,
        KeyAction.MoveRight => Window.Direction.Right,
        _ => null,
    };
}

/// <summary>
/// One act per press of a key that acts once however long it is held: Windows repeats the press of a held key, and a
/// held <c>Ctrl+V</c> would otherwise read the clipboard again and again, each time a new item (G13, N9).
/// </summary>
public sealed class PressOnce
{
    private Key? down;

    /// <summary>Whether this press of <paramref name="key"/> acts: not while it is still held from the press that acted.</summary>
    public bool Pressed(Key key)
    {
        if (down == key)
        {
            return false;
        }

        down = key;
        return true;
    }

    /// <summary>The key let go, so its next press acts; with no key, focus gone where its release would not be seen.</summary>
    public void Released(Key? key = null)
    {
        if (key is null || down == key)
        {
            down = null;
        }
    }
}

/// <summary>
/// <c>Ctrl</c>+wheel as thumbnail size steps (G6): one step per notch of a wheel, away from the user for larger; a
/// touchpad's small movements add up to a notch, and turning back starts the count again.
/// </summary>
public sealed class SizeWheel
{
    private double turned;

    /// <summary>The steps a wheel movement of <paramref name="delta"/> notches makes, up being positive.</summary>
    public int Turn(double delta)
    {
        if (Math.Sign(delta) != Math.Sign(turned))
        {
            turned = 0;
        }

        turned += delta;
        var steps = (int)Math.Truncate(turned);
        turned -= steps;
        return steps;
    }
}
