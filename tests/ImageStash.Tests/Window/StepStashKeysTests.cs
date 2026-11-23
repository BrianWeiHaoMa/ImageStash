using Avalonia.Input;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// Where the arrows go with the preview open (G19, G7): outside its fields, on its picture or a focused button of its own,
/// Left and Up step back through the grid's list and Right and Down step on, on every repeat of a held key; inside its
/// name in edit and its tag box they are the field's; while a list of every tag is open over it, nothing; under the open
/// editor they are the editor's (G25); the search box and the grid keep what they do with them.
/// </summary>
[TestClass]
public sealed class StepStashKeysTests
{
    [TestMethod]
    [DataRow(KeyPlace.Preview)]
    [DataRow(KeyPlace.PreviewButton)]
    public void OnThePreviewAndOnAFocusedButtonOfItsLeftAndUpStepBackAndRightAndDownStepOn(KeyPlace place)
    {
        Assert.AreEqual(KeyAction.StepBack, StashKeys.Route(Key.Left, KeyModifiers.None, place));
        Assert.AreEqual(KeyAction.StepBack, StashKeys.Route(Key.Up, KeyModifiers.None, place));
        Assert.AreEqual(KeyAction.StepOn, StashKeys.Route(Key.Right, KeyModifiers.None, place));
        Assert.AreEqual(KeyAction.StepOn, StashKeys.Route(Key.Down, KeyModifiers.None, place));
    }

    [TestMethod]
    [DataRow(KeyPlace.Preview, Key.Left, KeyModifiers.Shift)]
    [DataRow(KeyPlace.Preview, Key.Right, KeyModifiers.Control)]
    [DataRow(KeyPlace.PreviewButton, Key.Down, KeyModifiers.Alt)]
    public void AnArrowWithAModifierStepsNothing(KeyPlace place, Key key, KeyModifiers modifiers) =>
        Assert.AreEqual(KeyAction.None, StashKeys.Route(key, modifiers, place));

    [TestMethod]
    [DataRow(KeyPlace.PreviewName, Key.Left)]
    [DataRow(KeyPlace.PreviewName, Key.Right)]
    [DataRow(KeyPlace.PreviewName, Key.Up)]
    [DataRow(KeyPlace.PreviewName, Key.Down)]
    [DataRow(KeyPlace.Field, Key.Left)]
    [DataRow(KeyPlace.Field, Key.Right)]
    [DataRow(KeyPlace.Field, Key.Up)]
    [DataRow(KeyPlace.Field, Key.Down)]
    public void InsideTheNameInEditAndTheTagBoxTheArrowsAreTheFields(KeyPlace place, Key key) =>
        Assert.AreEqual(KeyAction.None, StashKeys.Route(key, KeyModifiers.None, place), "the caret's, or a suggestion's (G7, G19)");

    [TestMethod]
    [DataRow(Key.Left)]
    [DataRow(Key.Right)]
    [DataRow(Key.Up)]
    [DataRow(Key.Down)]
    public void UnderTheOpenEditorTheArrowsAreTheEditorsAndThePreviewStaysOnItsItem(Key key)
    {
        var place = StashKeys.PlaceOf(inSearchBox: false, inOtherField: false, previewOpen: true, onPreviewButton: false, editOpen: true);
        Assert.AreEqual(KeyPlace.Editor, place, "the editor covers the preview (G20)");
        Assert.AreEqual(KeyAction.None, StashKeys.Route(key, KeyModifiers.None, place), "a frame's step is the editor's own (G25)");
    }

    [TestMethod]
    public void TheBoxAndTheGridKeepWhatTheArrowsDoThere()
    {
        Assert.AreEqual(KeyAction.MoveUp, StashKeys.Route(Key.Up, KeyModifiers.None, KeyPlace.SearchBox));
        Assert.AreEqual(KeyAction.MoveDown, StashKeys.Route(Key.Down, KeyModifiers.None, KeyPlace.SearchBox));
        Assert.AreEqual(KeyAction.None, StashKeys.Route(Key.Left, KeyModifiers.None, KeyPlace.SearchBox), "the box's caret (G7)");
        Assert.AreEqual(KeyAction.None, StashKeys.Route(Key.Right, KeyModifiers.None, KeyPlace.SearchBox));
        Assert.AreEqual(KeyAction.MoveLeft, StashKeys.Route(Key.Left, KeyModifiers.None, KeyPlace.Grid));
        Assert.AreEqual(KeyAction.MoveRight, StashKeys.Route(Key.Right, KeyModifiers.None, KeyPlace.Grid));
        Assert.AreEqual(KeyAction.MoveUp, StashKeys.Route(Key.Up, KeyModifiers.None, KeyPlace.Grid));
        Assert.AreEqual(KeyAction.MoveDown, StashKeys.Route(Key.Down, KeyModifiers.None, KeyPlace.Grid));
    }

    [TestMethod]
    [DataRow(KeyPlace.Preview, Key.Left)]
    [DataRow(KeyPlace.Preview, Key.Down)]
    [DataRow(KeyPlace.PreviewButton, Key.Right)]
    [DataRow(KeyPlace.PreviewButton, Key.Up)]
    public void WhileAListOfEveryTagIsOpenOverThePreviewTheArrowsStepNothingAndEscClosesTheList(KeyPlace place, Key key)
    {
        Assert.AreEqual(KeyAction.None, StashKeys.WithTagListOpen(key, KeyModifiers.None, StashKeys.Route(key, KeyModifiers.None, place)), "outside its menus only (G19)");
        Assert.AreEqual(KeyAction.Escape, StashKeys.WithTagListOpen(Key.Escape, KeyModifiers.None, StashKeys.Route(Key.Escape, KeyModifiers.None, KeyPlace.Field)), "Esc closes the list, a field's own Esc included (G17)");
        Assert.AreEqual(KeyAction.Handoff, StashKeys.WithTagListOpen(Key.Enter, KeyModifiers.None, StashKeys.Route(Key.Enter, KeyModifiers.None, place)), "another key goes as routed");
        Assert.AreEqual(KeyAction.MoveDown, StashKeys.WithTagListOpen(Key.Down, KeyModifiers.None, StashKeys.Route(Key.Down, KeyModifiers.None, KeyPlace.SearchBox)), "the box's Down still moves the grid's selection");
    }

    [TestMethod]
    [DataRow(Key.Right, KeyAction.StepOn)]
    [DataRow(Key.Left, KeyAction.StepBack)]
    public void AHeldArrowStepsOnEveryRepeat(Key key, KeyAction step)
    {
        var held = new PressOnce();
        for (var repeat = 0; repeat < 4; repeat++)
        {
            Assert.AreEqual(step, held.Acting(key, StashKeys.Route(key, KeyModifiers.None, KeyPlace.Preview)), $"repeat {repeat}, never let go");
        }
    }
}
