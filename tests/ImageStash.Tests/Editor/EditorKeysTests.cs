using Avalonia.Input;
using ImageStash.Editor;

namespace ImageStash.Tests.Editor;

/// <summary>
/// G25's keys in the editor (G20): <c>Ctrl+Z</c>, <c>Ctrl+Y</c> and <c>Ctrl+S</c> from everywhere in it, a text field
/// included; Del, Esc, C, Space, Left and Right outside a text field, Esc deselecting before it cancels; Enter never the editor's.
/// </summary>
[TestClass]
public sealed class EditorKeysTests
{
    [TestMethod]
    public void OutsideATextFieldTheEditorsKeysRoute()
    {
        Assert.AreEqual(EditorKeyAction.Undo, EditorKeys.Route(Key.Z, KeyModifiers.Control, inTextField: false, selected: false));
        Assert.AreEqual(EditorKeyAction.Redo, EditorKeys.Route(Key.Y, KeyModifiers.Control, inTextField: false, selected: false));
        Assert.AreEqual(EditorKeyAction.Save, EditorKeys.Route(Key.S, KeyModifiers.Control, inTextField: false, selected: false));
        Assert.AreEqual(EditorKeyAction.DeleteSelected, EditorKeys.Route(Key.Delete, KeyModifiers.None, inTextField: false, selected: true));
        Assert.AreEqual(EditorKeyAction.Deselect, EditorKeys.Route(Key.Escape, KeyModifiers.None, inTextField: false, selected: true));
        Assert.AreEqual(EditorKeyAction.Cancel, EditorKeys.Route(Key.Escape, KeyModifiers.None, inTextField: false, selected: false));
        Assert.AreEqual(EditorKeyAction.Compare, EditorKeys.Route(Key.C, KeyModifiers.None, inTextField: false, selected: false));
        Assert.AreEqual(EditorKeyAction.None, EditorKeys.Route(Key.Enter, KeyModifiers.None, inTextField: false, selected: true));
        Assert.AreEqual(EditorKeyAction.None, EditorKeys.Route(Key.Enter, KeyModifiers.None, inTextField: false, selected: false));
        Assert.AreEqual(EditorKeyAction.PlayPause, EditorKeys.Route(Key.Space, KeyModifiers.None, inTextField: false, selected: true));
        Assert.AreEqual(EditorKeyAction.StepForward, EditorKeys.Route(Key.Right, KeyModifiers.None, inTextField: false, selected: false));
        Assert.AreEqual(EditorKeyAction.StepBack, EditorKeys.Route(Key.Left, KeyModifiers.None, inTextField: false, selected: false));
    }

    [TestMethod]
    public void InATextFieldOnlyUndoRedoSaveAndLeavingAreTheEditors()
    {
        Assert.AreEqual(EditorKeyAction.Undo, EditorKeys.Route(Key.Z, KeyModifiers.Control, inTextField: true, selected: true));
        Assert.AreEqual(EditorKeyAction.Redo, EditorKeys.Route(Key.Y, KeyModifiers.Control, inTextField: true, selected: true));
        Assert.AreEqual(EditorKeyAction.Save, EditorKeys.Route(Key.S, KeyModifiers.Control, inTextField: true, selected: true));
        foreach (var key in new[] { Key.Delete, Key.C, Key.Enter, Key.Back, Key.Space, Key.Left, Key.Right })
        {
            Assert.AreEqual(EditorKeyAction.None, EditorKeys.Route(key, KeyModifiers.None, inTextField: true, selected: true), key + " is the field's: Enter a new line, Del a character, Space a space, the arrows the caret");
        }
    }

    [TestMethod]
    public void CtrlEnterAndEscLeaveATextFieldAndNothingElseDoes()
    {
        Assert.AreEqual(EditorKeyAction.LeaveText, EditorKeys.Route(Key.Enter, KeyModifiers.Control, inTextField: true, selected: true));
        Assert.AreEqual(EditorKeyAction.LeaveText, EditorKeys.Route(Key.Escape, KeyModifiers.None, inTextField: true, selected: true));
        Assert.AreEqual(EditorKeyAction.LeaveText, EditorKeys.Route(Key.Escape, KeyModifiers.None, inTextField: true, selected: false));
        Assert.AreEqual(EditorKeyAction.None, EditorKeys.Route(Key.Enter, KeyModifiers.Shift, inTextField: true, selected: true));
        Assert.AreEqual(EditorKeyAction.None, EditorKeys.Route(Key.Enter, KeyModifiers.Control, inTextField: false, selected: true), "outside a field Ctrl+Enter is nothing of the editor's");
        Assert.AreEqual(EditorKeyAction.Deselect, EditorKeys.Route(Key.Escape, KeyModifiers.None, inTextField: false, selected: true), "outside a field Esc deselects as built");
    }

    [TestMethod]
    public void OtherModifiersAreNotTheEditorsKeys()
    {
        Assert.AreEqual(EditorKeyAction.None, EditorKeys.Route(Key.C, KeyModifiers.Control, inTextField: false, selected: true), "Ctrl+C is the window's copy");
        Assert.AreEqual(EditorKeyAction.None, EditorKeys.Route(Key.Z, KeyModifiers.Control | KeyModifiers.Shift, inTextField: false, selected: true));
        Assert.AreEqual(EditorKeyAction.None, EditorKeys.Route(Key.Delete, KeyModifiers.Shift, inTextField: false, selected: true));
        Assert.AreEqual(EditorKeyAction.None, EditorKeys.Route(Key.Right, KeyModifiers.Shift, inTextField: false, selected: true));
        Assert.AreEqual(EditorKeyAction.None, EditorKeys.Route(Key.Space, KeyModifiers.Control, inTextField: false, selected: true));
    }
}
