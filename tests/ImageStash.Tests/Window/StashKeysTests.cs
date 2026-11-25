using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// G7's keys, the same in both states (G2): Up and Down move the selection from either place without moving focus, Left
/// and Right from the grid, the box being an ordinary text box that keeps every character and its own caret and text
/// keys, Tab reaches the grid, <c>Ctrl+A</c> means items only in the grid, typing in the grid returns to the box, Del
/// deletes the selection in the grid and a character in the box, Enter, Esc and <c>Ctrl+O</c> work from both,
/// <c>Ctrl+C</c> copies the text the person selected in the box while it has such a selection and otherwise the items,
/// the box's whole text as a summon selects it not being theirs, Enter and <c>Ctrl+V</c> act once per press however
/// long they are held, and <c>Ctrl+V</c> pastes from every place; inside any other text field,
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
    [DataRow(Key.Left, KeyAction.MoveLeft)]
    [DataRow(Key.Right, KeyAction.MoveRight)]
    public void OnlyPlainHorizontalArrowsInTheLiterallyEmptyBoxMoveTheSelection(Key key, KeyAction movement)
    {
        Assert.AreEqual(movement, StashKeys.Route(key, KeyModifiers.None, KeyPlace.SearchBox, boxHasText: false));
        Assert.AreEqual(KeyAction.None, StashKeys.Route(key, KeyModifiers.Shift, KeyPlace.SearchBox, boxHasText: false));
        Assert.AreEqual(KeyAction.None, StashKeys.Route(key, KeyModifiers.Control, KeyPlace.SearchBox, boxHasText: false));
        Assert.AreEqual(KeyAction.None, StashKeys.Route(key, KeyModifiers.None, KeyPlace.SearchBox, boxHasText: true));
        Assert.AreEqual(KeyAction.None, StashKeys.Route(key, KeyModifiers.None, KeyPlace.SearchBox, boxTextSelected: true, boxHasText: true));
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
    [DataRow(KeyPlace.Preview)]
    public void EnterHandsOffOncePerPressHoweverLongItIsHeld(KeyPlace place)
    {
        var keys = new PressOnce();
        var enter = StashKeys.Route(Key.Enter, KeyModifiers.None, place);

        Assert.AreEqual(KeyAction.Handoff, keys.Acting(Key.Enter, enter));
        Assert.AreEqual(KeyAction.Held, keys.Acting(Key.Enter, enter), "Windows repeats the press of a held key: no second handoff (G8, N3)");
        Assert.AreEqual(KeyAction.Held, keys.Acting(Key.Enter, enter));
        keys.Released(Key.Enter);
        Assert.AreEqual(KeyAction.Handoff, keys.Acting(Key.Enter, enter), "let go and pressed again: a second handoff");
    }

    [TestMethod]
    public void EnterAndCtrlVAreEachHeldOnTheirOwn()
    {
        var keys = new PressOnce();

        Assert.AreEqual(KeyAction.Paste, keys.Acting(Key.V, KeyAction.Paste));
        Assert.AreEqual(KeyAction.Handoff, keys.Acting(Key.Enter, KeyAction.Handoff), "a held Ctrl+V does not swallow Enter");
        Assert.AreEqual(KeyAction.Held, keys.Acting(Key.V, KeyAction.Paste), "a held Ctrl+V still pastes once (G13)");
        keys.Released(Key.V);
        Assert.AreEqual(KeyAction.Held, keys.Acting(Key.Enter, KeyAction.Handoff), "letting go of V does not open the next press of a held Enter");
        Assert.AreEqual(KeyAction.Paste, keys.Acting(Key.V, KeyAction.Paste));
        keys.Released(Key.Enter);
        Assert.AreEqual(KeyAction.Held, keys.Acting(Key.V, KeyAction.Paste), "nor letting go of Enter a held V's");
        Assert.AreEqual(KeyAction.Handoff, keys.Acting(Key.Enter, KeyAction.Handoff));
        Assert.AreEqual(KeyAction.MoveDown, keys.Acting(Key.Down, KeyAction.MoveDown));
        Assert.AreEqual(KeyAction.MoveDown, keys.Acting(Key.Down, KeyAction.MoveDown), "a held arrow moves on as it repeats");
    }

    [TestMethod]
    public void AnEnterAFieldTookHandsNothingOffOnItsRepeatsOnceFocusIsBackOnThePreview()
    {
        var keys = new PressOnce();

        Assert.AreEqual(KeyAction.None, keys.Acting(Key.Enter, StashKeys.Route(Key.Enter, KeyModifiers.None, KeyPlace.Field)), "the name or tag field commits on its own Enter (G19)");
        Assert.AreEqual(KeyAction.None, keys.Acting(Key.Enter, StashKeys.Route(Key.Enter, KeyModifiers.None, KeyPlace.Field)), "its repeats stay the field's own");
        Assert.AreEqual(KeyAction.Held, keys.Acting(Key.Enter, StashKeys.Route(Key.Enter, KeyModifiers.None, KeyPlace.Preview)), "the commit gave focus back to the preview: the same press hands nothing off");
        keys.Released(Key.Enter);
        Assert.AreEqual(KeyAction.Handoff, keys.Acting(Key.Enter, StashKeys.Route(Key.Enter, KeyModifiers.None, KeyPlace.Preview)), "let go and pressed again, it does");
        Assert.AreEqual(KeyAction.None, keys.Acting(Key.Enter, StashKeys.Route(Key.Enter, KeyModifiers.None, KeyPlace.Editor)), "in the editor Enter never pastes (G20)");
    }

    [TestMethod]
    public void AnEnterWhoseReleaseTheWindowCannotSeeLetsTheNextSummonsEnterHandOff()
    {
        var keys = new PressOnce();
        Assert.AreEqual(KeyAction.Handoff, keys.Acting(Key.Enter, KeyAction.Handoff));
        Assert.AreEqual(KeyAction.Held, keys.Acting(Key.Enter, KeyAction.Handoff));

        // The flyout hid, or focus left, with Enter down: its key-up goes to another window.
        keys.Released();

        Assert.AreEqual(KeyAction.Handoff, keys.Acting(Key.Enter, KeyAction.Handoff), "the next summon's Enter hands off");
    }

    [TestMethod]
    public async Task EnterLetGoIsAtOnceWhenItIsUpAndOtherwiseAtItsRelease()
    {
        var keys = new PressOnce();
        Assert.IsTrue(keys.LetGo(Key.Enter).IsCompleted, "not held: nothing to wait for, as a double-click or the menu has none");

        keys.Acting(Key.Enter, KeyAction.Handoff);
        var letGo = keys.LetGo(Key.Enter);
        keys.Acting(Key.Enter, KeyAction.Handoff);
        keys.Released(Key.V);
        Assert.IsFalse(letGo.IsCompleted, "still held, its repeats coming");
        keys.Released(Key.Enter);
        await letGo.WaitAsync(TimeSpan.FromSeconds(5));

        keys.Acting(Key.Enter, KeyAction.Handoff);
        var unseen = keys.LetGo(Key.Enter);
        keys.Released();
        keys.Released(Key.V);
        Assert.IsFalse(unseen.IsCompleted, "focus gone with Enter down: its release unseen, it may still be down (N3)");
        Assert.IsTrue(keys.LetGo(Key.Enter).IsCompleted, "a later wait has no press of the window's to wait for");
        keys.Released(Key.Enter);
        await unseen.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [TestMethod]
    public void AnotherEnterPressCannotReleaseTheOneLostWithFocus()
    {
        var keys = new PressOnce();
        keys.Acting(Key.Enter, KeyAction.Handoff);
        var first = keys.LetGo(Key.Enter);
        keys.Released();

        keys.Acting(Key.Enter, KeyAction.Handoff);
        var second = keys.LetGo(Key.Enter);
        keys.Released(Key.Enter);

        Assert.IsFalse(first.IsCompleted);
        Assert.IsTrue(second.IsCompletedSuccessfully);
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
    public void CtrlCAfterASummonCopiesTheItemsNotTheSearchTextTheSummonSelected()
    {
        var mark = new SummonSelection();
        mark.Made();

        Assert.AreEqual(KeyAction.Copy, StashKeys.Route(Key.C, KeyModifiers.Control, KeyPlace.SearchBox, mark.PersonSelected("my")), "the summon's selection is not the person's (G7, G1)");
        mark.KeyDown(Key.Down, KeyModifiers.None);
        mark.KeyDown(Key.Up, KeyModifiers.None);
        mark.KeyDown(Key.LeftCtrl, KeyModifiers.Control);
        mark.KeyDown(Key.LeftShift, KeyModifiers.Shift);
        Assert.AreEqual(KeyAction.Copy, StashKeys.Route(Key.C, KeyModifiers.Control, KeyPlace.SearchBox, mark.PersonSelected("my")), "Up and Down move the grid's selection and leave the box's alone, and Ctrl or Shift alone selects nothing");
        Assert.IsTrue(mark.Summons);
    }

    [TestMethod]
    [DataRow(Key.A, KeyModifiers.Control)]
    [DataRow(Key.Left, KeyModifiers.Shift)]
    [DataRow(Key.Right, KeyModifiers.Shift)]
    [DataRow(Key.Home, KeyModifiers.Shift)]
    [DataRow(Key.End, KeyModifiers.Shift)]
    [DataRow(Key.Left, KeyModifiers.Control | KeyModifiers.Shift)]
    [DataRow(Key.Up, KeyModifiers.Shift)]
    [DataRow(Key.Home, KeyModifiers.None)]
    [DataRow(Key.End, KeyModifiers.None)]
    public void TextThePersonSelectsInTheBoxAfterASummonIsTheBoxsOwnCopy(Key key, KeyModifiers modifiers)
    {
        var mark = new SummonSelection();
        mark.Made();

        // Ctrl+A, and Shift+End with the caret at the end, leave the very range the summon made.
        mark.KeyDown(key, modifiers);

        Assert.IsTrue(mark.PersonSelected("my"));
        Assert.AreEqual(KeyAction.None, StashKeys.Route(Key.C, KeyModifiers.Control, KeyPlace.SearchBox, mark.PersonSelected("my")), "G7: the text the person selected, copied by the box itself, no item and no use");
    }

    [TestMethod]
    public void TypingOverTheSummonsSelectionAndSelectingTheNewTextCopiesThatText()
    {
        var mark = new SummonSelection();
        mark.Made();

        mark.Changed();

        Assert.IsTrue(mark.PersonSelected("dog"), "the text typed in its place, then selected");
        Assert.IsFalse(mark.PersonSelected(""), "no selected text: the items");
        Assert.AreEqual(KeyAction.None, StashKeys.Route(Key.C, KeyModifiers.Control, KeyPlace.Field, mark.PersonSelected("my")), "another field's Ctrl+C stays its own");
        Assert.AreEqual(KeyAction.None, StashKeys.Route(Key.C, KeyModifiers.Control, KeyPlace.Field), "with nothing of the box's selected too");
    }

    [TestMethod]
    public void TheSearchBoxsSelectingKeysTextAndFocusMakeTheSummonsSelectionThePersons() => ControlThread.Run(() =>
    {
        var box = new TextBox { Text = "my" };
        var mark = new SummonSelection();
        mark.Follow(box);
        box.SelectAll();
        mark.Made();
        box.SelectAll();
        Assert.AreEqual("my", box.SelectedText);
        Assert.IsFalse(mark.PersonSelected(box.SelectedText), "selecting all again is no change of the person's");

        // The window takes Down for the grid before the box sees it.
        box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Down, Handled = true });
        Assert.IsFalse(mark.PersonSelected(box.SelectedText), "an arrow that leaves the box's selection alone");
        box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.A, KeyModifiers = KeyModifiers.Control, Handled = true });
        Assert.IsTrue(mark.PersonSelected(box.SelectedText), "Ctrl+A in the box, however handled, the very range the summon made");

        mark.Made();
        box.Text = "my d";
        Assert.IsFalse(mark.Summons, "typing changed the text");

        mark.Made();
        box.RaiseEvent(new FocusChangedEventArgs(InputElement.LostFocusEvent));
        Assert.IsFalse(mark.Summons, "focus left the box, which clears its selection");

        mark.Made();
        var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true);
        box.RaiseEvent(new PointerPressedEventArgs(box, pointer, box, new Point(2, 2), 0, new PointerPointProperties(RawInputModifiers.RightMouseButton, PointerUpdateKind.RightButtonPressed), KeyModifiers.None, 1));
        Assert.IsTrue(mark.Summons, "a right-click inside the selected text leaves it as it is");
        box.RaiseEvent(new PointerPressedEventArgs(box, pointer, box, new Point(2, 2), 0, new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed), KeyModifiers.None, 1));
        Assert.IsFalse(mark.Summons, "a click or a drag's start in the box");
    });

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
    [DataRow(Key.Up, KeyModifiers.None, KeyAction.StepBack)]
    [DataRow(Key.Down, KeyModifiers.None, KeyAction.StepOn)]
    [DataRow(Key.Left, KeyModifiers.None, KeyAction.StepBack)]
    [DataRow(Key.Right, KeyModifiers.None, KeyAction.StepOn)]
    [DataRow(Key.Tab, KeyModifiers.None, KeyAction.None)]
    [DataRow(Key.A, KeyModifiers.Control, KeyAction.None)]
    [DataRow(Key.R, KeyModifiers.None, KeyAction.None)]
    [DataRow(Key.Back, KeyModifiers.None, KeyAction.None)]
    [DataRow(Key.Insert, KeyModifiers.Shift, KeyAction.None)]
    public void InThePreviewTheArrowsStepItAndTabCtrlAAndTypingReachNeitherTheGridNorTheBoxItCovers(Key key, KeyModifiers modifiers, KeyAction expected)
    {
        Assert.AreEqual(expected, StashKeys.Route(key, modifiers, KeyPlace.Preview));
        Assert.AreEqual(expected, StashKeys.Route(key, modifiers, KeyPlace.PreviewButton));
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
