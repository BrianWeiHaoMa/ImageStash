using Avalonia.Input;
using ImageStash.Seams;

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
    MoveFirst,
    MoveLast,
    MovePageUp,
    MovePageDown,
    ExtendUp,
    ExtendDown,
    ExtendLeft,
    ExtendRight,
    FocusGrid,
    SelectAll,
    Escape,
    ShowRecent,
    ShowFavorites,
    ShowAll,
    Settings,

    /// <summary><c>Ctrl+O</c>: Import files… (G7, G12).</summary>
    ImportFiles,

    /// <summary><c>Ctrl+V</c>: the window's paste, which takes words alone in an editor text field (G7, G13; <see cref="PasteKey"/>).</summary>
    Paste,

    /// <summary>Enter: the handoff on the selection (G7, G8).</summary>
    Handoff,

    /// <summary><c>Ctrl+C</c> with no text the person selected in the box: the same clipboard content as the handoff, and the window stays (G7, G9).</summary>
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

    /// <summary><c>Ctrl+Z</c> in the grid and in the preview outside its fields: the Undo of the newest notice that still offers one (G7); the search box, a field and the editor keep their own.</summary>
    UndoNotice,

    /// <summary><c>Ctrl+T</c> from the box, the grid and the preview: the tag field on the selection, or in the preview its own tag box (G7, G17; <see cref="TagKey"/>).</summary>
    AddTags,

    /// <summary>Left or Up in the preview outside its fields: the previous item of the grid's list (G19; <see cref="PreviewStepping"/>).</summary>
    StepBack,

    /// <summary>Right or Down in the preview outside its fields: the next item of the grid's list (G19; <see cref="PreviewStepping"/>).</summary>
    StepOn,

    /// <summary>A repeat of an Enter, a <c>Ctrl+V</c> or a <c>Ctrl+Z</c> still held from its press: nothing happens, and no control sees it (G7, G8, G13, N3).</summary>
    Held,
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

    /// <summary>The open preview's name in edit: a text field whose keys are its own, but for <c>Ctrl+T</c>, which moves focus on to the preview's tag box (G17, G19).</summary>
    PreviewName,

    /// <summary>The open editor, over everything else, with no text field focused: its keys are its own (G20, G25).</summary>
    Editor,
}

/// <summary>
/// G7's keys, in one place for both states (G2): the search box is an ordinary text box, so Left, Right, Home, End,
/// Shift with them, <c>Ctrl+A</c>, <c>Ctrl+X</c> and <c>Ctrl+Z</c> are its own; Up and Down move the grid's selection
/// from the box or the grid without moving focus, and plain Left and Right move it from the grid or the empty box; Tab in the box moves focus
/// to the grid. Shift extends a range with those same arrows in the grid, and with Up and Down in the box.
/// In the grid Home and End select the endpoints, Page Up and Page Down move by a viewport, and <c>Ctrl+A</c>
/// selects every item; Enter, Esc, <c>Ctrl+O</c> and G6's <c>Ctrl</c> with
/// <c>+</c> or <c>-</c>, on the main keys or the numeric keypad's, work from both; <c>Ctrl+T</c>, from both, opens the
/// tag field on the selection (<see cref="TagKey"/>); <c>Ctrl+C</c> copies the text the
/// person selected in the box while it has such a selection, which is the box's own key, and otherwise the selected
/// items, from both, the box's whole text as a summon selects it not being the person's until they change it there
/// (<see cref="SummonSelection"/>); Enter and <c>Ctrl+V</c> act once per press however long they are held, Enter
/// counted wherever it lands, so a press a field took never hands off (<see cref="PressOnce"/>); Del in the grid
/// deletes the selection, and in the box a character; <c>Ctrl+D</c> in the grid toggles favorite; Space in the grid
/// opens the preview, and F2 opens it with the name in edit; <c>Ctrl+Z</c> in the grid runs the Undo of the newest
/// notice that still offers one, once per press however long it is held, while in the box it stays the box's own; and a
/// character typed in the grid goes into the box with focus. <c>Ctrl+V</c> is the window's paste from every place, the
/// one read of the clipboard, and <c>Shift+Insert</c> is nothing, since no text box has a paste of its own (G13, N9);
/// with the caret in a layer's text or the color picker's hex box it pastes the clipboard's words there and never adds
/// an item (<see cref="PasteKey"/>).
/// Inside any other text field the keys it uses are its own (typed characters, Space, Backspace, Del, the arrows,
/// Enter, Esc, F2, its text's <c>Ctrl+A</c> and <c>Ctrl+C</c>), so none of them deletes, hands off, closes or moves the
/// selection; only <c>Ctrl+O</c>, <c>Ctrl+V</c> and the thumbnail size, which a field leaves unused, still reach the
/// window, and in the preview's name in edit <c>Ctrl+T</c> too. While the preview covers the box and the grid, Enter,
/// <c>Ctrl+C</c>, Del, <c>Ctrl+D</c> and F2 act on its item, Esc and Space close it, <c>Ctrl+Z</c> runs the newest
/// notice's Undo as in the grid, <c>Ctrl+T</c> moves focus to its tag box, a focused button of its
/// own takes Space as its press, <c>Ctrl+O</c>, <c>Ctrl+V</c> and the thumbnail size still work, Left and Up show the
/// previous item of the grid's list and Right and Down the next, on its picture and on a focused button of its own alike,
/// on every repeat of a held key (<see cref="PreviewStepping"/>), while inside its name in edit and its tag box the
/// arrows are the field's, and <c>Ctrl+A</c> and typing reach neither the grid nor the box, which it hides (G7, G17,
/// G19). <c>Ctrl+E</c> in the box, the grid or the preview opens the editor. While the editor is open over all of it, only <c>Ctrl+C</c>, which
/// copies the item as last saved, <c>Ctrl+O</c>, <c>Ctrl+V</c> and the thumbnail size still reach the window; every
/// other key is the editor's, so Enter never pastes and <c>Ctrl+Z</c> is the edit's own undo (G20, G25, G9, G11, G13).
/// </summary>
public static class StashKeys
{
    private static readonly (Key Key, KeyAction Action, Scope Scope)[] ScopeKeys =
    [
        (Key.D1, KeyAction.ShowRecent, Scope.Recent),
        (Key.D2, KeyAction.ShowFavorites, Scope.Favorites),
        (Key.D3, KeyAction.ShowAll, Scope.All),
    ];

    internal const string SettingsHint = "Ctrl+,";

    internal static string ScopeHint(Scope scope) =>
        Shortcut.Of(new KeyGesture(ScopeKeys.Single(choice => choice.Scope == scope).Key, KeyModifiers.Control)).Text;

    internal static Scope? ScopeChoice(KeyAction action) =>
        action is KeyAction.ShowRecent or KeyAction.ShowFavorites or KeyAction.ShowAll
            ? ScopeKeys.Single(choice => choice.Action == action).Scope : null;

    /// <summary><c>Ctrl+V</c>, the window's paste, as the <c>+</c> menu and the empty stash name it; <see cref="Route"/> routes it.</summary>
    public static readonly KeyGesture PasteKey = new(Key.V, KeyModifiers.Control);

    /// <summary><c>Ctrl+O</c>, the window's Import files…, as the <c>+</c> menu and the empty stash name it; <see cref="Route"/> routes it.</summary>
    public static readonly KeyGesture ImportFilesKey = new(Key.O, KeyModifiers.Control);

    /// <param name="boxTextSelected">The search box has text the person selected (<see cref="SummonSelection.PersonSelected"/>), as it can only while it has focus: it clears its selection on losing focus.</param>
    /// <param name="boxHasText">Whether the box contains any text, including spaces that yield no search terms.</param>
    public static KeyAction Route(Key key, KeyModifiers modifiers, KeyPlace place, bool boxTextSelected = false, bool boxHasText = true) => place switch
    {
        KeyPlace.Field or KeyPlace.PreviewName => (key, modifiers) switch
        {
            (Key.T, KeyModifiers.Control) when place == KeyPlace.PreviewName => KeyAction.AddTags,
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
            (Key.Z, KeyModifiers.Control) => KeyAction.UndoNotice,
            (Key.T, KeyModifiers.Control) => KeyAction.AddTags,
            (Key.Escape, KeyModifiers.None) => KeyAction.Escape,
            (Key.Space, KeyModifiers.None) when place == KeyPlace.Preview => KeyAction.TogglePreview,
            (Key.Left or Key.Up, KeyModifiers.None) => KeyAction.StepBack,
            (Key.Right or Key.Down, KeyModifiers.None) => KeyAction.StepOn,
            (Key.O, KeyModifiers.Control) => KeyAction.ImportFiles,
            (Key.V, KeyModifiers.Control) => KeyAction.Paste,
            (Key.OemPlus or Key.Add, KeyModifiers.Control or (KeyModifiers.Control | KeyModifiers.Shift)) => KeyAction.LargerThumbnails,
            (Key.OemMinus or Key.Subtract, KeyModifiers.Control) => KeyAction.SmallerThumbnails,
            _ => KeyAction.None,
        },
        _ => Route(key, modifiers, inSearchBox: place == KeyPlace.SearchBox, boxTextSelected, boxHasText),
    };

    /// <summary>
    /// Where the window's keys go: a text field other than the search box keeps its own, the preview's name in edit
    /// (<paramref name="inPreviewName"/>) told apart; the open editor covers all the rest, so it is next; then the open
    /// preview, which covers the box and the grid, its own focused button apart; then the box, else the grid.
    /// </summary>
    public static KeyPlace PlaceOf(bool inSearchBox, bool inOtherField, bool previewOpen, bool onPreviewButton, bool editOpen, bool inPreviewName = false) =>
        inOtherField ? (inPreviewName && previewOpen && !editOpen ? KeyPlace.PreviewName : KeyPlace.Field)
        : editOpen ? KeyPlace.Editor
        : previewOpen ? (onPreviewButton ? KeyPlace.PreviewButton : KeyPlace.Preview)
        : inSearchBox ? KeyPlace.SearchBox
        : KeyPlace.Grid;

    private static KeyAction Route(Key key, KeyModifiers modifiers, bool inSearchBox, bool boxTextSelected, bool boxHasText) => (key, modifiers) switch
    {
        (Key.Up, KeyModifiers.None) => KeyAction.MoveUp,
        (Key.Down, KeyModifiers.None) => KeyAction.MoveDown,
        (Key.Left, KeyModifiers.None) when !inSearchBox || !boxHasText => KeyAction.MoveLeft,
        (Key.Right, KeyModifiers.None) when !inSearchBox || !boxHasText => KeyAction.MoveRight,
        (Key.Up, KeyModifiers.Shift) => KeyAction.ExtendUp,
        (Key.Down, KeyModifiers.Shift) => KeyAction.ExtendDown,
        (Key.Left, KeyModifiers.Shift) when !inSearchBox => KeyAction.ExtendLeft,
        (Key.Right, KeyModifiers.Shift) when !inSearchBox => KeyAction.ExtendRight,
        (Key.Home, KeyModifiers.None) when !inSearchBox => KeyAction.MoveFirst,
        (Key.End, KeyModifiers.None) when !inSearchBox => KeyAction.MoveLast,
        (Key.PageUp, KeyModifiers.None) when !inSearchBox => KeyAction.MovePageUp,
        (Key.PageDown, KeyModifiers.None) when !inSearchBox => KeyAction.MovePageDown,
        (Key.Escape, KeyModifiers.None) => KeyAction.Escape,
        (Key.Enter, KeyModifiers.None) => KeyAction.Handoff,
        (Key.C, KeyModifiers.Control) when !(inSearchBox && boxTextSelected) => KeyAction.Copy,
        (Key.E, KeyModifiers.Control) => KeyAction.Edit,
        (Key.O, KeyModifiers.Control) => KeyAction.ImportFiles,
        (Key.V, KeyModifiers.Control) => KeyAction.Paste,
        (Key.T, KeyModifiers.Control) => KeyAction.AddTags,
        (Key.OemComma, KeyModifiers.Control) => KeyAction.Settings,
        // The main keyboard's + is Shift with = on many layouts, so Ctrl+= and Ctrl+Shift+= both mean Ctrl with +.
        (Key.OemPlus or Key.Add, KeyModifiers.Control or (KeyModifiers.Control | KeyModifiers.Shift)) => KeyAction.LargerThumbnails,
        (Key.OemMinus or Key.Subtract, KeyModifiers.Control) => KeyAction.SmallerThumbnails,
        (Key.Tab, KeyModifiers.None) when inSearchBox => KeyAction.FocusGrid,
        (Key.A, KeyModifiers.Control) when !inSearchBox => KeyAction.SelectAll,
        (Key.Delete, KeyModifiers.None) when !inSearchBox => KeyAction.Delete,
        (Key.D, KeyModifiers.Control) when !inSearchBox => KeyAction.ToggleFavorite,
        (Key.Space, KeyModifiers.None) when !inSearchBox => KeyAction.TogglePreview,
        (Key.F2, KeyModifiers.None) when !inSearchBox => KeyAction.Rename,
        (Key.Z, KeyModifiers.Control) when !inSearchBox => KeyAction.UndoNotice,
        (_, KeyModifiers.Control) => ScopeKeys.FirstOrDefault(choice => choice.Key == key).Action,
        _ => KeyAction.None,
    };

    /// <summary>
    /// A key pressed while a list of every tag is open (G17, G19), the list leaving focus where it was: Esc closes the list
    /// alone wherever focus is, a field's own Esc included; the arrows step no preview from under it, as they step none
    /// from under its menus; every other key goes as <paramref name="routed"/>.
    /// </summary>
    public static KeyAction WithTagListOpen(Key key, KeyModifiers modifiers, KeyAction routed) =>
        routed == KeyAction.Held ? routed
        : (key, modifiers) == (Key.Escape, KeyModifiers.None) ? KeyAction.Escape
        : routed is KeyAction.StepBack or KeyAction.StepOn ? KeyAction.None
        : routed;

    /// <summary>
    /// Whether text typed while the grid has focus goes into the search box. Space does not: in the grid it acts on the
    /// selection (G7).
    /// </summary>
    public static bool TypesIntoSearch(string? text) =>
        !string.IsNullOrEmpty(text) && text != " " && !text.Any(char.IsControl);

    public static Direction? Direction(KeyAction action) => action switch
    {
        KeyAction.MoveUp or KeyAction.ExtendUp => Window.Direction.Up,
        KeyAction.MoveDown or KeyAction.ExtendDown => Window.Direction.Down,
        KeyAction.MoveLeft or KeyAction.ExtendLeft => Window.Direction.Left,
        KeyAction.MoveRight or KeyAction.ExtendRight => Window.Direction.Right,
        KeyAction.MoveFirst => Window.Direction.First,
        KeyAction.MoveLast => Window.Direction.Last,
        KeyAction.MovePageUp => Window.Direction.PageUp,
        KeyAction.MovePageDown => Window.Direction.PageDown,
        _ => null,
    };

    internal static bool ExtendsRange(KeyAction action) =>
        action is KeyAction.ExtendUp or KeyAction.ExtendDown or KeyAction.ExtendLeft or KeyAction.ExtendRight;

    internal static bool RequiresGridFocus(Direction direction) =>
        direction is Window.Direction.First or Window.Direction.Last or Window.Direction.PageUp or Window.Direction.PageDown;
}

/// <summary>
/// One act per press of a key that acts once however long it is held, and the moment it is let go: Windows repeats the
/// press of a held key, so a held <c>Ctrl+V</c> would otherwise read the clipboard again and again, each time a new item
/// (G13, N9), a held Enter would hand off again and again, its repeats reaching the target as Send once it is in
/// front (G8, N3), and a held <c>Ctrl+Z</c> would undo one notice's change and then the one before it (G7). Each key is
/// remembered on its own, so letting go of one never opens another's next press.
/// </summary>
public sealed class PressOnce
{
    private readonly object gate = new();
    private readonly HashSet<Key> down = [];
    private readonly HashSet<Key> downInMenu = [];
    private readonly Dictionary<Key, TaskCompletionSource> waiting = [];

    /// <summary>Keys that were down when focus went where their release is not seen: each may still be down (N3).</summary>
    private readonly HashSet<Key> unseen = [];

    /// <summary>Whether this press of <paramref name="key"/> acts: not while it is still held from an earlier press.</summary>
    public bool Pressed(Key key)
    {
        lock (gate)
        {
            return MarkDown(key);
        }
    }

    internal bool PressedInMenu(Key key)
    {
        lock (gate)
        {
            if (!MarkDown(key))
            {
                return false;
            }

            downInMenu.Add(key);
            return true;
        }
    }

    private bool MarkDown(Key key)
    {
        if (!down.Add(key))
        {
            return false;
        }

        // A new press after focus was lost cannot lend its release to the earlier press.
        waiting.Remove(key);
        unseen.Remove(key);
        return true;
    }

    /// <summary>
    /// What a press of <paramref name="key"/> routed to <paramref name="action"/> does: a repeat of an Enter, a
    /// <c>Ctrl+V</c> or a <c>Ctrl+Z</c> still held is <see cref="KeyAction.Held"/>. Enter and Z are remembered wherever
    /// they land, a text field's own included, so the repeats of an Enter the name or tag field took do not hand off once
    /// focus has gone back to the preview (G19), and those of a <c>Ctrl+Z</c> the search box took do not undo a notice
    /// once focus has gone to the grid (G7). In a tag field or its picker a repeat cannot commit or pick again.
    /// </summary>
    public KeyAction Acting(Key key, KeyAction action, bool tagFieldFocused = false)
    {
        lock (gate)
        {
            if (downInMenu.Contains(key))
            {
                return KeyAction.Held;
            }

            // Outside a menu, other keys act on each press, a held arrow moving on as it repeats.
            var remembered = key is Key.Enter or Key.Z || action == KeyAction.Paste;
            return remembered && !MarkDown(key) &&
                (action is KeyAction.Handoff or KeyAction.Paste or KeyAction.UndoNotice || tagFieldFocused && key == Key.Enter)
                ? KeyAction.Held : action;
        }
    }

    /// <summary>
    /// The key let go, so its next press acts, and whatever waits for its release goes on; with no key, every key, focus
    /// having gone where a release would not be seen: their next presses act, but nothing waiting for a release goes on,
    /// since the key may still be down (N3).
    /// </summary>
    public void Released(Key? key = null)
    {
        TaskCompletionSource? letGo = null;
        lock (gate)
        {
            if (key is not { } one)
            {
                unseen.UnionWith(down);
                down.Clear();
                downInMenu.Clear();
                return;
            }

            down.Remove(one);
            downInMenu.Remove(one);
            unseen.Remove(one);
            if (waiting.Remove(one, out var wait))
            {
                letGo = wait;
            }
        }

        letGo?.SetResult();
    }

    /// <summary>
    /// Completes once <paramref name="key"/> is let go: at once when it is not held, else at its release; a release unseen,
    /// focus having gone elsewhere, completes nothing, and a key down as focus went counts as held until it is next seen
    /// pressed or let go, so a caller bounds its wait and never takes a key it lost sight of as up.
    /// </summary>
    public Task LetGo(Key key)
    {
        lock (gate)
        {
            if (!down.Contains(key) && !unseen.Contains(key))
            {
                return Task.CompletedTask;
            }

            if (!waiting.TryGetValue(key, out var wait))
            {
                // Its waiter goes on after the key-up's handler, never inside it.
                waiting[key] = wait = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            return wait.Task;
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
