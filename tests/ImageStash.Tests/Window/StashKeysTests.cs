using Avalonia.Input;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// G7's keys, the same in both states (G2): Up and Down move the selection from either place without moving focus, Left
/// and Right from the grid, the box being an ordinary text box that keeps every character and its own caret and text
/// keys, Tab reaches the grid, <c>Ctrl+A</c> means items only in the grid, typing in the grid returns to the box, Del
/// deletes the selection in the grid and a character in the box, Enter, Esc and <c>Ctrl+O</c> work from both,
/// <c>Ctrl+C</c> copies the box's selected text while it has one and otherwise the items, and <c>Ctrl+V</c> pastes from
/// every place, held or not; inside any other text field,
/// such as the tag field or the name, the keys it uses are its own; Space in the grid opens the preview, F2 opens it
/// with the name in edit, and while the preview covers the box and the grid its keys act on its item.
/// </summary>
[TestClass]
public sealed class StashKeysTests
{
    [TestMethod]
    [DataRow(KeyPlace.SearchBox)]
    [DataRow(KeyPlace.Grid)]
    public void UpAndDownMoveTheSelectionFromTheBoxAndFromTheGrid(KeyPlace place)
    {
        Assert.AreEqual(KeyAction.MoveUp, StashKeys.Route(Key.Up, KeyModifiers.None, place));
        Assert.AreEqual(KeyAction.MoveDown, StashKeys.Route(Key.Down, KeyModifiers.None, place));
        Assert.AreEqual(KeyAction.Escape, StashKeys.Route(Key.Escape, KeyModifiers.None, place));
    }

    [TestMethod]
    public void LeftAndRightMoveTheSelectionFromTheGrid()
    {
        Assert.AreEqual(KeyAction.MoveLeft, StashKeys.Route(Key.Left, KeyModifiers.None, KeyPlace.Grid));
        Assert.AreEqual(KeyAction.MoveRight, StashKeys.Route(Key.Right, KeyModifiers.None, KeyPlace.Grid));
        Assert.AreEqual(Direction.Left, StashKeys.Direction(KeyAction.MoveLeft));
    }

    [TestMethod]
    [DataRow(Key.Left, KeyModifiers.None)]
    [DataRow(Key.Right, KeyModifiers.None)]
    [DataRow(Key.Home, KeyModifiers.None)]
    [DataRow(Key.End, KeyModifiers.None)]
    [DataRow(Key.Left, KeyModifiers.Shift)]
    [DataRow(Key.Right, KeyModifiers.Shift)]
    [DataRow(Key.Home, KeyModifiers.Shift)]
    [DataRow(Key.End, KeyModifiers.Shift)]
    [DataRow(Key.A, KeyModifiers.Control)]
    [DataRow(Key.X, KeyModifiers.Control)]
    [DataRow(Key.Z, KeyModifiers.Control)]
    public void TheBoxIsAnOrdinaryTextBoxItsCaretAndTextKeysItsOwnNeverTheGrids(Key key, KeyModifiers modifiers)
    {
        Assert.AreEqual(KeyAction.None, StashKeys.Route(key, modifiers, KeyPlace.SearchBox), "G7: left to the text box");
        Assert.AreEqual(KeyAction.None, StashKeys.Route(key, modifiers, KeyPlace.SearchBox, boxTextSelected: true), "with text selected too");
    }

    [TestMethod]
    [DataRow(KeyPlace.SearchBox)]
    [DataRow(KeyPlace.Grid)]
    public void CtrlOImportsFilesFromTheBoxAndFromTheGrid(KeyPlace place)
    {
        Assert.AreEqual(KeyAction.ImportFiles, StashKeys.Route(Key.O, KeyModifiers.Control, place));
        Assert.AreEqual(KeyAction.None, StashKeys.Route(Key.O, KeyModifiers.None, place), "a plain O is typed");
        Assert.AreEqual(KeyAction.None, StashKeys.Route(Key.O, KeyModifiers.Control | KeyModifiers.Shift, place));
    }

    [TestMethod]
    [DataRow(KeyPlace.SearchBox)]
    [DataRow(KeyPlace.Grid)]
    [DataRow(KeyPlace.Field)]
    [DataRow(KeyPlace.Preview)]
    [DataRow(KeyPlace.PreviewButton)]
    public void CtrlVPastesFromEveryPlaceAndShiftInsertIsNothing(KeyPlace place)
    {
        Assert.AreEqual(KeyAction.Paste, StashKeys.Route(Key.V, KeyModifiers.Control, place), "G7, G13; inside a field too, which has no paste of its own");
        Assert.AreEqual(KeyAction.None, StashKeys.Route(Key.Insert, KeyModifiers.Shift, place), "a text box's own paste, switched off (N9)");
        Assert.AreEqual(KeyAction.None, StashKeys.Route(Key.V, KeyModifiers.None, place));
        Assert.AreEqual(KeyAction.None, StashKeys.Route(Key.V, KeyModifiers.Control | KeyModifiers.Shift, place));
    }

    [TestMethod]
    public void AHeldKeyActsOnceUntilItIsLetGoOrFocusLeaves()
    {
        var paste = new PressOnce();

        Assert.IsTrue(paste.Pressed(Key.V));
        Assert.IsFalse(paste.Pressed(Key.V), "Windows repeats the press of a held key");
        paste.Released(Key.LeftCtrl);
        Assert.IsFalse(paste.Pressed(Key.V), "another key let go is not this one");
        paste.Released(Key.V);
        Assert.IsTrue(paste.Pressed(Key.V));
        paste.Released();
        Assert.IsTrue(paste.Pressed(Key.V), "a release not seen, focus having left, does not swallow the next press");
    }

    [TestMethod]
    [DataRow(KeyPlace.SearchBox)]
    [DataRow(KeyPlace.Grid)]
    public void EnterHandsOffAndCtrlCCopiesFromTheBoxAndFromTheGrid(KeyPlace place)
    {
        Assert.AreEqual(KeyAction.Handoff, StashKeys.Route(Key.Enter, KeyModifiers.None, place));
        Assert.AreEqual(KeyAction.Copy, StashKeys.Route(Key.C, KeyModifiers.Control, place), "no text selected in the box: the selected items");
        Assert.AreEqual(KeyAction.None, StashKeys.Route(Key.C, KeyModifiers.None, place), "a plain C is typed");
        Assert.AreEqual(KeyAction.None, StashKeys.Route(Key.Enter, KeyModifiers.Shift, place));
        Assert.AreEqual(KeyAction.None, StashKeys.Route(Key.C, KeyModifiers.Control | KeyModifiers.Shift, place));
    }

    [TestMethod]
    public void CtrlCWithTextSelectedInTheBoxIsTheBoxsOwnCopyAndWritesNoItem()
    {
        Assert.AreEqual(KeyAction.None, StashKeys.Route(Key.C, KeyModifiers.Control, KeyPlace.SearchBox, boxTextSelected: true), "G7: left to the text box, which copies its selected text");
        Assert.AreEqual(KeyAction.Copy, StashKeys.Route(Key.C, KeyModifiers.Control, KeyPlace.SearchBox, boxTextSelected: false));
    }

    [TestMethod]
    [DataRow(KeyPlace.Preview)]
    [DataRow(KeyPlace.PreviewButton)]
    [DataRow(KeyPlace.Editor)]
    public void TextSelectedInTheBoxACoverHidesChangesNoCopy(KeyPlace place)
    {
        Assert.AreEqual(KeyAction.Copy, StashKeys.Route(Key.C, KeyModifiers.Control, place, boxTextSelected: true), "the preview's or the editor's item (G9, G19)");
    }

    [TestMethod]
    [DataRow(KeyPlace.SearchBox)]
    [DataRow(KeyPlace.Grid)]
    public void CtrlWithPlusOrMinusStepsTheThumbnailSizeFromTheMainKeysAndTheKeypadInBothPlaces(KeyPlace place)
    {
        Assert.AreEqual(KeyAction.LargerThumbnails, StashKeys.Route(Key.OemPlus, KeyModifiers.Control, place));
        Assert.AreEqual(KeyAction.LargerThumbnails, StashKeys.Route(Key.OemPlus, KeyModifiers.Control | KeyModifiers.Shift, place), "Ctrl with the + Shift types");
        Assert.AreEqual(KeyAction.LargerThumbnails, StashKeys.Route(Key.Add, KeyModifiers.Control, place));
        Assert.AreEqual(KeyAction.SmallerThumbnails, StashKeys.Route(Key.OemMinus, KeyModifiers.Control, place));
        Assert.AreEqual(KeyAction.SmallerThumbnails, StashKeys.Route(Key.Subtract, KeyModifiers.Control, place));
        Assert.AreEqual(KeyAction.None, StashKeys.Route(Key.OemPlus, KeyModifiers.None, place), "a plain + or = is typed");
        Assert.AreEqual(KeyAction.None, StashKeys.Route(Key.OemMinus, KeyModifiers.None, place));
        Assert.AreEqual(KeyAction.None, StashKeys.Route(Key.Subtract, KeyModifiers.None, place));
        Assert.AreEqual(KeyAction.None, StashKeys.Route(Key.Add, KeyModifiers.Alt, place));
    }

    [TestMethod]
    public void CtrlWheelStepsOncePerNotchAddingUpATouchpadsSmallMovements()
    {
        var wheel = new SizeWheel();

        Assert.AreEqual(1, wheel.Turn(1));
        Assert.AreEqual(-2, wheel.Turn(-2));
        Assert.AreEqual(0, wheel.Turn(0.4));
        Assert.AreEqual(0, wheel.Turn(0.4));
        Assert.AreEqual(1, wheel.Turn(0.4), "three small movements make a notch");
        Assert.AreEqual(0, wheel.Turn(-0.6), "turning back starts the count again");
        Assert.AreEqual(-1, wheel.Turn(-0.6));
    }

    [TestMethod]
    public void TheBoxKeepsSpaceBackspaceDelAndItsOwnSelectAll()
    {
        Assert.AreEqual(KeyAction.None, StashKeys.Route(Key.Space, KeyModifiers.None, KeyPlace.SearchBox));
        Assert.AreEqual(KeyAction.None, StashKeys.Route(Key.Back, KeyModifiers.None, KeyPlace.SearchBox));
        Assert.AreEqual(KeyAction.None, StashKeys.Route(Key.Delete, KeyModifiers.None, KeyPlace.SearchBox));
        Assert.AreEqual(KeyAction.None, StashKeys.Route(Key.A, KeyModifiers.Control, KeyPlace.SearchBox));
    }

    [TestMethod]
    public void TabInTheBoxReachesTheGridAndCtrlAInTheGridSelectsEveryItem()
    {
        Assert.AreEqual(KeyAction.FocusGrid, StashKeys.Route(Key.Tab, KeyModifiers.None, KeyPlace.SearchBox));
        Assert.AreEqual(KeyAction.None, StashKeys.Route(Key.Tab, KeyModifiers.None, KeyPlace.Grid));
        Assert.AreEqual(KeyAction.SelectAll, StashKeys.Route(Key.A, KeyModifiers.Control, KeyPlace.Grid));
    }

    [TestMethod]
    public void DelInTheGridDeletesTheSelectionAndInTheBoxIsTheBoxs()
    {
        Assert.AreEqual(KeyAction.Delete, StashKeys.Route(Key.Delete, KeyModifiers.None, KeyPlace.Grid));
        Assert.AreEqual(KeyAction.None, StashKeys.Route(Key.Delete, KeyModifiers.None, KeyPlace.SearchBox), "the box deletes a character (G7)");
        Assert.AreEqual(KeyAction.None, StashKeys.Route(Key.Delete, KeyModifiers.Shift, KeyPlace.Grid));
        Assert.AreEqual(KeyAction.None, StashKeys.Route(Key.Delete, KeyModifiers.Control, KeyPlace.Grid));
        Assert.AreEqual(KeyAction.None, StashKeys.Route(Key.Back, KeyModifiers.None, KeyPlace.Grid), "Backspace is not Delete");
    }

    [TestMethod]
    public void CtrlDInTheGridTogglesFavoriteAndInTheBoxActsOnNothing()
    {
        Assert.AreEqual(KeyAction.ToggleFavorite, StashKeys.Route(Key.D, KeyModifiers.Control, KeyPlace.Grid));
        Assert.AreEqual(KeyAction.None, StashKeys.Route(Key.D, KeyModifiers.Control, KeyPlace.SearchBox), "G7: Ctrl+D acts in the grid");
        Assert.AreEqual(KeyAction.None, StashKeys.Route(Key.D, KeyModifiers.None, KeyPlace.Grid), "a typed d goes to the box");
        Assert.AreEqual(KeyAction.None, StashKeys.Route(Key.D, KeyModifiers.Control | KeyModifiers.Shift, KeyPlace.Grid));
    }

    [TestMethod]
    [DataRow(Key.Delete, KeyModifiers.None)]
    [DataRow(Key.Back, KeyModifiers.None)]
    [DataRow(Key.Space, KeyModifiers.None)]
    [DataRow(Key.Left, KeyModifiers.None)]
    [DataRow(Key.Right, KeyModifiers.None)]
    [DataRow(Key.Up, KeyModifiers.None)]
    [DataRow(Key.Down, KeyModifiers.None)]
    [DataRow(Key.Enter, KeyModifiers.None)]
    [DataRow(Key.Escape, KeyModifiers.None)]
    [DataRow(Key.Tab, KeyModifiers.None)]
    [DataRow(Key.A, KeyModifiers.Control)]
    [DataRow(Key.C, KeyModifiers.Control)]
    [DataRow(Key.D, KeyModifiers.Control)]
    [DataRow(Key.Insert, KeyModifiers.Shift)]
    [DataRow(Key.R, KeyModifiers.None)]
    [DataRow(Key.F2, KeyModifiers.None)]
    public void InsideAnotherFieldTheKeysItUsesAreItsOwnAndNoneActsOnTheSelection(Key key, KeyModifiers modifiers)
    {
        Assert.AreEqual(KeyAction.None, StashKeys.Route(key, modifiers, KeyPlace.Field),
            "Del, Enter, Esc, the arrows and the rest edit the field or commit or cancel it; none deletes, hands off, closes or moves the selection");
    }

    [TestMethod]
    public void InsideAnotherFieldCtrlOCtrlVAndTheThumbnailSizeTheFieldLeavesUnusedStillReachTheWindow()
    {
        Assert.AreEqual(KeyAction.ImportFiles, StashKeys.Route(Key.O, KeyModifiers.Control, KeyPlace.Field));
        Assert.AreEqual(KeyAction.Paste, StashKeys.Route(Key.V, KeyModifiers.Control, KeyPlace.Field));
        Assert.AreEqual(KeyAction.LargerThumbnails, StashKeys.Route(Key.OemPlus, KeyModifiers.Control, KeyPlace.Field));
        Assert.AreEqual(KeyAction.LargerThumbnails, StashKeys.Route(Key.Add, KeyModifiers.Control | KeyModifiers.Shift, KeyPlace.Field));
        Assert.AreEqual(KeyAction.SmallerThumbnails, StashKeys.Route(Key.Subtract, KeyModifiers.Control, KeyPlace.Field));
        Assert.AreEqual(KeyAction.None, StashKeys.Route(Key.O, KeyModifiers.None, KeyPlace.Field), "a plain O is typed");
    }

    [TestMethod]
    public void SpaceInTheGridOpensThePreviewAndInTheBoxOrAFieldIsTyped()
    {
        Assert.AreEqual(KeyAction.TogglePreview, StashKeys.Route(Key.Space, KeyModifiers.None, KeyPlace.Grid), "G7: Space acts on the selection in the grid (G19)");
        Assert.AreEqual(KeyAction.None, StashKeys.Route(Key.Space, KeyModifiers.None, KeyPlace.SearchBox), "the box takes every typed character, Space included (G7)");
        Assert.AreEqual(KeyAction.None, StashKeys.Route(Key.Space, KeyModifiers.None, KeyPlace.Field));
        Assert.AreEqual(KeyAction.None, StashKeys.Route(Key.Space, KeyModifiers.Shift, KeyPlace.Grid));
        Assert.AreEqual(KeyAction.None, StashKeys.Route(Key.Space, KeyModifiers.Control, KeyPlace.Grid));
    }

    [TestMethod]
    public void F2RenamesFromTheGridAndInThePreviewAndInTheBoxOrAFieldIsTheirs()
    {
        Assert.AreEqual(KeyAction.Rename, StashKeys.Route(Key.F2, KeyModifiers.None, KeyPlace.Grid), "G7: F2 acts on the selection in the grid (G17)");
        Assert.AreEqual(KeyAction.Rename, StashKeys.Route(Key.F2, KeyModifiers.None, KeyPlace.Preview), "on the previewed item (G17, G19)");
        Assert.AreEqual(KeyAction.Rename, StashKeys.Route(Key.F2, KeyModifiers.None, KeyPlace.PreviewButton), "a focused button of the preview has no F2 of its own");
        Assert.AreEqual(KeyAction.None, StashKeys.Route(Key.F2, KeyModifiers.None, KeyPlace.SearchBox), "G7 names F2 among the grid's keys, not the box's");
        Assert.AreEqual(KeyAction.None, StashKeys.Route(Key.F2, KeyModifiers.None, KeyPlace.Field), "inside the name or the tag field it is the field's");
        Assert.AreEqual(KeyAction.None, StashKeys.Route(Key.F2, KeyModifiers.Shift, KeyPlace.Grid));
        Assert.AreEqual(KeyAction.None, StashKeys.Route(Key.F2, KeyModifiers.Control, KeyPlace.Preview));
    }

    [TestMethod]
    [DataRow(KeyPlace.Preview)]
    [DataRow(KeyPlace.PreviewButton)]
    public void InThePreviewEnterCtrlCDelAndCtrlDActOnItsItemAndEscCtrlOCtrlVAndTheSizeStillWork(KeyPlace place)
    {
        Assert.AreEqual(KeyAction.Handoff, StashKeys.Route(Key.Enter, KeyModifiers.None, place), "Enter hands off wherever focus is outside a field, a focused button too");
        Assert.AreEqual(KeyAction.Copy, StashKeys.Route(Key.C, KeyModifiers.Control, place));
        Assert.AreEqual(KeyAction.Delete, StashKeys.Route(Key.Delete, KeyModifiers.None, place));
        Assert.AreEqual(KeyAction.ToggleFavorite, StashKeys.Route(Key.D, KeyModifiers.Control, place));
        Assert.AreEqual(KeyAction.Escape, StashKeys.Route(Key.Escape, KeyModifiers.None, place));
        Assert.AreEqual(KeyAction.ImportFiles, StashKeys.Route(Key.O, KeyModifiers.Control, place));
        Assert.AreEqual(KeyAction.Paste, StashKeys.Route(Key.V, KeyModifiers.Control, place));
        Assert.AreEqual(KeyAction.LargerThumbnails, StashKeys.Route(Key.OemPlus, KeyModifiers.Control, place));
        Assert.AreEqual(KeyAction.SmallerThumbnails, StashKeys.Route(Key.Subtract, KeyModifiers.Control, place));
    }

    [TestMethod]
    public void InThePreviewSpaceClosesItUnlessOneOfItsButtonsHasFocusWhichTakesSpaceAsItsPress()
    {
        Assert.AreEqual(KeyAction.TogglePreview, StashKeys.Route(Key.Space, KeyModifiers.None, KeyPlace.Preview));
        Assert.AreEqual(KeyAction.None, StashKeys.Route(Key.Space, KeyModifiers.None, KeyPlace.PreviewButton), "a button reached by Tab is pressed by Space (G7)");
    }

    [TestMethod]
    [DataRow(Key.Up, KeyModifiers.None)]
    [DataRow(Key.Down, KeyModifiers.None)]
    [DataRow(Key.Left, KeyModifiers.None)]
    [DataRow(Key.Right, KeyModifiers.None)]
    [DataRow(Key.Tab, KeyModifiers.None)]
    [DataRow(Key.A, KeyModifiers.Control)]
    [DataRow(Key.R, KeyModifiers.None)]
    [DataRow(Key.Back, KeyModifiers.None)]
    [DataRow(Key.Insert, KeyModifiers.Shift)]
    public void InThePreviewTheArrowsTabCtrlAAndTypingReachNeitherTheGridNorTheBoxItCovers(Key key, KeyModifiers modifiers)
    {
        Assert.AreEqual(KeyAction.None, StashKeys.Route(key, modifiers, KeyPlace.Preview));
        Assert.AreEqual(KeyAction.None, StashKeys.Route(key, modifiers, KeyPlace.PreviewButton));
    }

    [TestMethod]
    public void TheKeysGoToAFieldFirstThenTheOpenEditorThenTheOpenPreviewThenTheBoxElseTheGrid()
    {
        Assert.AreEqual(KeyPlace.Field, StashKeys.PlaceOf(inSearchBox: false, inOtherField: true, previewOpen: true, onPreviewButton: false, editOpen: false), "a field's keys are its own, inside the preview too");
        Assert.AreEqual(KeyPlace.Field, StashKeys.PlaceOf(inSearchBox: false, inOtherField: true, previewOpen: true, onPreviewButton: false, editOpen: true), "and inside the editor: the picker's hex keeps its own");
        Assert.AreEqual(KeyPlace.Editor, StashKeys.PlaceOf(inSearchBox: false, inOtherField: false, previewOpen: true, onPreviewButton: true, editOpen: true), "the editor covers the preview and its buttons");
        Assert.AreEqual(KeyPlace.Editor, StashKeys.PlaceOf(inSearchBox: true, inOtherField: false, previewOpen: false, onPreviewButton: false, editOpen: true), "and the box and the grid");
        Assert.AreEqual(KeyPlace.Preview, StashKeys.PlaceOf(inSearchBox: true, inOtherField: false, previewOpen: true, onPreviewButton: false, editOpen: false), "the preview covers the box");
        Assert.AreEqual(KeyPlace.Preview, StashKeys.PlaceOf(inSearchBox: false, inOtherField: false, previewOpen: true, onPreviewButton: false, editOpen: false));
        Assert.AreEqual(KeyPlace.PreviewButton, StashKeys.PlaceOf(inSearchBox: false, inOtherField: false, previewOpen: true, onPreviewButton: true, editOpen: false));
        Assert.AreEqual(KeyPlace.SearchBox, StashKeys.PlaceOf(inSearchBox: true, inOtherField: false, previewOpen: false, onPreviewButton: false, editOpen: false));
        Assert.AreEqual(KeyPlace.Grid, StashKeys.PlaceOf(inSearchBox: false, inOtherField: false, previewOpen: false, onPreviewButton: false, editOpen: false));
    }

    [TestMethod]
    public void CtrlEEditsFromTheBoxTheGridAndThePreviewAndIsNothingInAFieldOrTheEditor()
    {
        foreach (var place in new[] { KeyPlace.SearchBox, KeyPlace.Grid, KeyPlace.Preview, KeyPlace.PreviewButton })
        {
            Assert.AreEqual(KeyAction.Edit, StashKeys.Route(Key.E, KeyModifiers.Control, place), $"G7, G20: Ctrl+E acts on the selection ({place})");
        }

        Assert.AreEqual(KeyAction.None, StashKeys.Route(Key.E, KeyModifiers.Control, KeyPlace.Field), "a field keeps its keys");
        Assert.AreEqual(KeyAction.None, StashKeys.Route(Key.E, KeyModifiers.Control, KeyPlace.Editor), "an edit is already open");
        Assert.AreEqual(KeyAction.None, StashKeys.Route(Key.E, KeyModifiers.None, KeyPlace.Grid), "E alone is typing, not Edit");
    }

    [TestMethod]
    [DataRow(Key.Enter, KeyModifiers.None)]
    [DataRow(Key.Escape, KeyModifiers.None)]
    [DataRow(Key.Space, KeyModifiers.None)]
    [DataRow(Key.Delete, KeyModifiers.None)]
    [DataRow(Key.F2, KeyModifiers.None)]
    [DataRow(Key.D, KeyModifiers.Control)]
    [DataRow(Key.A, KeyModifiers.Control)]
    [DataRow(Key.Up, KeyModifiers.None)]
    [DataRow(Key.Down, KeyModifiers.None)]
    [DataRow(Key.Left, KeyModifiers.None)]
    [DataRow(Key.Right, KeyModifiers.None)]
    [DataRow(Key.Tab, KeyModifiers.None)]
    [DataRow(Key.C, KeyModifiers.None)]
    [DataRow(Key.Z, KeyModifiers.Control)]
    [DataRow(Key.S, KeyModifiers.Control)]
    [DataRow(Key.Insert, KeyModifiers.Shift)]
    public void InTheEditorEveryKeyButCopyPasteImportAndTheThumbnailSizeIsTheEditorsSoEnterNeverPastes(Key key, KeyModifiers modifiers)
    {
        Assert.AreEqual(KeyAction.None, StashKeys.Route(key, modifiers, KeyPlace.Editor), "G20: Enter never pastes; Esc, Space, Del, F2, Ctrl+D, Ctrl+A and the arrows are the editor's or nothing");
    }

    [TestMethod]
    public void InTheEditorCtrlCCopiesCtrlVPastesCtrlOImportsAndTheThumbnailSizeStillSteps()
    {
        Assert.AreEqual(KeyAction.Copy, StashKeys.Route(Key.C, KeyModifiers.Control, KeyPlace.Editor), "G9: the item as last saved, the window staying");
        Assert.AreEqual(KeyAction.Paste, StashKeys.Route(Key.V, KeyModifiers.Control, KeyPlace.Editor), "G13: the paste runs beneath the editor");
        Assert.AreEqual(KeyAction.ImportFiles, StashKeys.Route(Key.O, KeyModifiers.Control, KeyPlace.Editor), "G12: the import runs beneath the editor");
        Assert.AreEqual(KeyAction.LargerThumbnails, StashKeys.Route(Key.OemPlus, KeyModifiers.Control, KeyPlace.Editor));
        Assert.AreEqual(KeyAction.SmallerThumbnails, StashKeys.Route(Key.OemMinus, KeyModifiers.Control, KeyPlace.Editor));
    }

    [TestMethod]
    public void TextTypedInTheGridGoesToTheBoxButSpaceAndControlCharactersDoNot()
    {
        Assert.IsTrue(StashKeys.TypesIntoSearch("d"));
        Assert.IsTrue(StashKeys.TypesIntoSearch("é"));
        Assert.IsTrue(StashKeys.TypesIntoSearch("#"));
        Assert.IsFalse(StashKeys.TypesIntoSearch(" "));
        Assert.IsFalse(StashKeys.TypesIntoSearch("\b"));
        Assert.IsFalse(StashKeys.TypesIntoSearch(""));
        Assert.IsFalse(StashKeys.TypesIntoSearch(null));
    }
}
