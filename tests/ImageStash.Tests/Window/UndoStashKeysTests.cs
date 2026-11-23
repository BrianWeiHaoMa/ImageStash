using Avalonia.Input;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// Where <c>Ctrl+Z</c> goes (G7, G25): in the grid and in the preview outside its fields, the newest notice's Undo, once
/// per press however long it is held; in the search box, a field and the editor, theirs.
/// </summary>
[TestClass]
public sealed class UndoStashKeysTests
{
    [TestMethod]
    [DataRow(KeyPlace.Grid, KeyAction.UndoNotice)]
    [DataRow(KeyPlace.Preview, KeyAction.UndoNotice)]
    [DataRow(KeyPlace.PreviewButton, KeyAction.UndoNotice)]
    [DataRow(KeyPlace.SearchBox, KeyAction.None)]
    [DataRow(KeyPlace.Field, KeyAction.None)]
    [DataRow(KeyPlace.PreviewName, KeyAction.None)]
    [DataRow(KeyPlace.Editor, KeyAction.None)]
    public void CtrlZRunsTheNewestUndoFromTheGridAndThePreviewAndIsTheOwnKeyElsewhere(KeyPlace place, KeyAction expected)
    {
        Assert.AreEqual(expected, StashKeys.Route(Key.Z, KeyModifiers.Control, place));
        Assert.AreEqual(KeyAction.None, StashKeys.Route(Key.Z, KeyModifiers.None, place), "Z alone is typing, not Undo");
        Assert.AreEqual(KeyAction.None, StashKeys.Route(Key.Z, KeyModifiers.Control | KeyModifiers.Shift, place));
    }

    [TestMethod]
    public void ARepeatOfAHeldCtrlZIsHeldUntilItIsLetGo()
    {
        var keys = new PressOnce();

        Assert.AreEqual(KeyAction.UndoNotice, keys.Acting(Key.Z, KeyAction.UndoNotice));
        Assert.AreEqual(KeyAction.Held, keys.Acting(Key.Z, KeyAction.UndoNotice));
        keys.Released(Key.V);
        Assert.AreEqual(KeyAction.Held, keys.Acting(Key.Z, KeyAction.UndoNotice), "another key's release opens nothing");
        keys.Released(Key.Z);
        Assert.AreEqual(KeyAction.UndoNotice, keys.Acting(Key.Z, KeyAction.UndoNotice));
        Assert.AreEqual(KeyAction.None, keys.Acting(Key.Z, KeyAction.None), "the box's own Ctrl+Z is never held back");
    }

    [TestMethod]
    public void ACtrlZHeldInTheSearchBoxDoesNotUndoANoticeWhenFocusMovesToTheGridBeforeItIsLetGo()
    {
        var keys = new PressOnce();

        Assert.AreEqual(KeyAction.None, keys.Acting(Key.Z, StashKeys.Route(Key.Z, KeyModifiers.Control, KeyPlace.SearchBox)), "the box's own");
        Assert.AreEqual(KeyAction.Held, keys.Acting(Key.Z, StashKeys.Route(Key.Z, KeyModifiers.Control, KeyPlace.Grid)), "its repeat in the grid is the same press");
        keys.Released(Key.Z);
        Assert.AreEqual(KeyAction.UndoNotice, keys.Acting(Key.Z, StashKeys.Route(Key.Z, KeyModifiers.Control, KeyPlace.Grid)), "a new press in the grid acts");
    }
}
