using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using ImageStash.Seams;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// A Settings shortcut field given its key events in the orders a person may press and let go of keys, a press whose
/// KeyDown never reaches it coming only as its KeyUp, as Windows withholds PrtScn's and another program's hot key takes
/// the combination it holds: whatever modifiers changed around that press, a combination another program holds is
/// refused on the spot and nothing else is bound (G32, F5), the field still recording for the retry, and a combination
/// pressed with its modifiers held first is still recorded, a modifier let go before they went down no part of it. Esc
/// alone, Tab and Shift+Tab are never read as a press, by either event, before a refusal or after one.
/// </summary>
// One at a time: a real control's first properties write Avalonia's metadata cache, which takes one writer at a time.
[DoNotParallelize]
[TestClass]
public sealed class ShortcutFieldTests
{
    private const KeyModifiers Ctrl = KeyModifiers.Control;
    private const KeyModifiers Shift = KeyModifiers.Shift;
    private const KeyModifiers Alt = KeyModifiers.Alt;
    private const string HeldCtrlF9 = "Ctrl+F9 is already used by another app. Press a different combination.";

    [TestMethod]
    public void CtrlHeldThroughTheClickIsPartOfTheHeldCombinationPressedAfterIt()
    {
        var (started, recording, field) = Recording(held: "Ctrl+F9");

        // Ctrl went down before the click; F9's KeyDown went to the program holding Ctrl+F9; Ctrl let go before F9.
        Up(field, Key.LeftCtrl, KeyModifiers.None);
        Up(field, Key.F9, KeyModifiers.None);

        Assert.AreEqual(HeldCtrlF9, recording.WarningWords, "on the spot (G32)");
        RefusedStillRecording(started, recording, field);
    }

    [TestMethod]
    public void AModifierPressedUnderAHeldCombinationAfterItsOwnCameUpBindsNothing()
    {
        var (started, recording, field) = Recording(held: "Ctrl+F9");

        Down(field, Key.LeftCtrl, Ctrl);
        Up(field, Key.LeftCtrl, KeyModifiers.None);
        Down(field, Key.LeftShift, Shift);
        Up(field, Key.F9, Shift);

        Assert.AreEqual(HeldCtrlF9, recording.WarningWords, "Ctrl+F9 was pressed, never Shift+F9 (G32, F5)");
        RefusedStillRecording(started, recording, field);
    }

    [TestMethod]
    public void ASecondClickWhileRecordingForgetsNoModifierTheFieldSawHeld()
    {
        var (started, recording, field) = Recording(held: "Ctrl+F9");

        Down(field, Key.LeftCtrl, Ctrl);
        Up(field, Key.LeftCtrl, KeyModifiers.None);
        Click(field);
        Up(field, Key.F9, KeyModifiers.None);

        Assert.AreEqual(HeldCtrlF9, recording.WarningWords, "never plain F9 (G32, F5)");
        RefusedStillRecording(started, recording, field);
    }

    [TestMethod]
    public void AModifierTappedBeforeTheKeyIsNoPartOfTheHeldCombinationNamed()
    {
        var (started, recording, field) = Recording(held: "Ctrl+F9");

        Down(field, Key.LeftCtrl, Ctrl);
        Down(field, Key.LeftShift, Ctrl | Shift);
        Up(field, Key.LeftShift, Ctrl);
        Up(field, Key.LeftCtrl, KeyModifiers.None);
        Up(field, Key.F9, KeyModifiers.None);

        Assert.AreEqual(HeldCtrlF9, recording.WarningWords, "the combination pressed, held by another program, is named (G32, G35)");
        RefusedStillRecording(started, recording, field);
    }

    [TestMethod]
    public void ItsOwnShortcutAnotherProgramTookWhileItRecordsIsLeftUnregisteredUntilChangedInSettings()
    {
        var (started, recording, field) = Recording();
        var screenshot = Shortcut.Parse("Ctrl+PrtScn").Value!;

        // Released for the recording, then taken by another program, which lets it go again before the recording ends.
        Down(field, Key.LeftCtrl, Ctrl);
        Up(field, Key.LeftCtrl, KeyModifiers.None);
        started.Seam.RefuseNext(ShortcutRefusal.HeldByAnotherProgram);
        Up(field, Key.PrintScreen, KeyModifiers.None);

        var sentence = "Ctrl+PrtScn is already used by another app. Press a different combination.";
        Assert.AreEqual(sentence, recording.WarningWords, "on the spot (G32)");
        Assert.AreEqual("Ctrl+PrtScn is used by another app, so Take a screenshot has no shortcut.", started.Tray.ToolTip, "it could not be registered (G33)");
        Assert.AreEqual(ShortcutAct.Screenshot, started.Shortcuts.FirstUnavailable, "Settings opens on its row (G33)");
        CollectionAssert.DoesNotContain(started.Seam.Registered.ToList(), screenshot, "left unregistered until it is changed in Settings (G33)");
    }

    [TestMethod]
    public void PrtScnPressedAfterItsModifiersIsRecordedThoughAnotherProgramHoldsPlainPrtScn()
    {
        var (started, recording, field) = Recording(held: "PrtScn");

        Down(field, Key.LeftCtrl, Ctrl);
        Down(field, Key.LeftAlt, Ctrl | Alt);
        Up(field, Key.PrintScreen, Ctrl | Alt);

        Assert.IsNull(recording.Warning, "a screenshot tool's PrtScn is no reason to refuse Ctrl+Alt+PrtScn (F5)");
        Assert.AreEqual("Ctrl+Alt+PrtScn", started.Settings.Current.Shortcuts.Screenshot);
        CollectionAssert.Contains(started.Seam.Registered.ToList(), Shortcut.Parse("Ctrl+Alt+PrtScn").Value!);
    }

    [TestMethod]
    public void AModifierTappedBeforeAPrtScnCombinationIsNoPartOfItThoughAnotherProgramHoldsPlainPrtScn()
    {
        var (started, recording, field) = Recording(held: "PrtScn");

        // Ctrl tapped, then Shift held for PrtScn, whose KeyDown Windows withholds: Shift+PrtScn was pressed (G32).
        Down(field, Key.LeftCtrl, Ctrl);
        Up(field, Key.LeftCtrl, KeyModifiers.None);
        Down(field, Key.LeftShift, Shift);
        Up(field, Key.PrintScreen, Shift);

        Assert.IsNull(recording.Warning, "a screenshot tool's PrtScn is no reason to refuse Shift+PrtScn (G32, F5)");
        Assert.AreEqual("Shift+PrtScn", started.Settings.Current.Shortcuts.Screenshot);
        CollectionAssert.Contains(started.Seam.Registered.ToList(), Shortcut.Parse("Shift+PrtScn").Value!);
    }

    [TestMethod]
    public void AModifierPressedWhilePrtScnIsHeldIsPartOfItsReadingThoughAnotherProgramHoldsTheCombinationWithoutIt()
    {
        var (started, recording, field) = Recording(held: "Alt+PrtScn");

        // Alt down, PrtScn down (withheld), Ctrl down, PrtScn up: held from Ctrl's press until PrtScn came up, Ctrl+Alt (G32).
        Down(field, Key.LeftAlt, Alt);
        Down(field, Key.LeftCtrl, Ctrl | Alt);
        Up(field, Key.PrintScreen, Ctrl | Alt);

        Assert.IsNull(recording.Warning, "Alt+PrtScn, which another program holds, is not the combination read (G32)");
        Assert.AreEqual("Ctrl+Alt+PrtScn", started.Settings.Current.Shortcuts.Screenshot);
        CollectionAssert.Contains(started.Seam.Registered.ToList(), Shortcut.Parse("Ctrl+Alt+PrtScn").Value!);
    }

    [TestMethod]
    public void EscAloneEndsTheRecordingReadAsNoPress()
    {
        var (started, recording, field) = Recording();

        Down(field, Key.Escape, KeyModifiers.None);
        Up(field, Key.Escape, KeyModifiers.None);

        Assert.IsNull(recording.Warning, "no refusal: Esc asked for no registration (G32, G35)");
        Unchanged(started, recording);
        Assert.IsFalse(started.Seam.Asked.Any(shortcut => shortcut.Text.Contains("Esc")), "never asked of the seam");
    }

    [TestMethod]
    public void TabWithOrWithoutShiftIsLeftToMoveOnReadAsNoPress()
    {
        var (started, recording, field) = Recording();

        foreach (var modifiers in new[] { KeyModifiers.None, Shift })
        {
            var tab = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Tab, KeyModifiers = modifiers };
            field.RaiseEvent(tab);
            Assert.IsFalse(tab.Handled, "left to the page, which moves the focus on, and so ends the recording (G32)");
            Up(field, Key.Tab, modifiers);
        }

        Assert.IsNull(recording.Warning, "no refusal (G32, G35)");
        Assert.AreEqual("Ctrl+PrtScn", recording.Text, "nothing bound");
        Assert.IsFalse(started.Seam.Asked.Any(shortcut => shortcut.Text.Contains("Tab")), "never asked of the seam");
    }

    [TestMethod]
    public void EscOrTabWhoseKeyDownAnotherProgramTookIsReadAsNoPressEscStillEndingTheRecording()
    {
        var (escStarted, escRecording, escField) = Recording(held: "Esc");
        Up(escField, Key.Escape, KeyModifiers.None);

        Assert.IsNull(escRecording.Warning, "Esc alone is no press, held or not (G32)");
        Unchanged(escStarted, escRecording);

        var (tabStarted, tabRecording, tabField) = Recording(held: "Tab");
        Up(tabField, Key.Tab, KeyModifiers.None);

        Assert.IsNull(tabRecording.Warning, "Tab is no press, held or not (G32)");
        Assert.AreEqual("Ctrl+PrtScn", tabRecording.Text, "nothing bound");
        Assert.IsFalse(tabStarted.Seam.Asked.Any(shortcut => shortcut.Text.Contains("Tab")), "never asked of the seam");
    }

    [TestMethod]
    public void AfterARefusalAPrtScnSeenOnlyComingUpIsTheRetryAndIsTaken()
    {
        var (started, recording, field) = Recording(held: "Ctrl+F9");
        Down(field, Key.LeftCtrl, Ctrl);
        Up(field, Key.F9, Ctrl);
        Up(field, Key.LeftCtrl, KeyModifiers.None);
        Assert.AreEqual(HeldCtrlF9, recording.WarningWords);

        Down(field, Key.LeftShift, Shift);
        Up(field, Key.PrintScreen, Shift);

        Assert.IsFalse(recording.IsRecording, "a combination taken ends the recording (G32)");
        Assert.IsNull(recording.WarningWords, "and clears the row's warning");
        Assert.AreEqual("Shift+PrtScn", started.Settings.Current.Shortcuts.Screenshot, "PrtScn read with the modifiers held from the last modifier's press until it came up (G32)");
        CollectionAssert.AreEquivalent(
            new[] { "Ctrl+Alt+Space", "Shift+PrtScn", "Ctrl+Shift+PrtScn", "Win+Alt+V" }.Select(text => Shortcut.Parse(text).Value!).ToList(),
            started.Seam.Registered.ToList(), "every shortcut due registered again, the new one among them");
    }

    [TestMethod]
    public void AKeyHeldDownAfterARefusalRepeatsNoPress()
    {
        var (started, recording, field) = Recording();

        Down(field, Key.LeftCtrl, Ctrl);
        Down(field, Key.LeftAlt, Ctrl | Alt);
        Down(field, Key.Space, Ctrl | Alt);
        Up(field, Key.LeftAlt, Ctrl);
        Up(field, Key.LeftCtrl, KeyModifiers.None);
        // Space still held: Windows repeats its KeyDown, now with no modifier.
        Down(field, Key.Space, KeyModifiers.None);
        Down(field, Key.Space, KeyModifiers.None);

        Assert.AreEqual("Open ImageStash already uses Ctrl+Alt+Space.", recording.WarningWords, "the next press is the retry, and a repeat is none (G32)");
        Assert.IsFalse(started.Seam.Asked.Any(shortcut => shortcut.Text == "Space"), "plain Space never judged");
        Up(field, Key.Space, KeyModifiers.None);
        RefusedStillRecording(started, recording, field);
    }

    [TestMethod]
    public void AfterARefusalTabAndShiftTabAreStillLeftToMoveOnAndNeverRead()
    {
        var (started, recording, field) = Recording(held: "Ctrl+F9");
        Up(field, Key.F9, Ctrl);
        Assert.IsTrue(recording.IsRecording);

        foreach (var modifiers in new[] { KeyModifiers.None, Shift })
        {
            var tab = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Tab, KeyModifiers = modifiers };
            field.RaiseEvent(tab);
            Assert.IsFalse(tab.Handled, "left to the page, which moves the focus on, and so ends the recording (G32)");
            Up(field, Key.Tab, modifiers);
        }

        Assert.IsFalse(started.Seam.Asked.Any(shortcut => shortcut.Text.Contains("Tab")), "never asked of the seam");
        Assert.AreEqual("Ctrl+PrtScn", recording.Text, "nothing bound");
    }

    [TestMethod]
    public void TheStopButtonShownAsAGlyphAloneNamesItsActionInItsTip()
    {
        var (_, _, field) = Recording();

        var stop = field.GetLogicalDescendants().OfType<Button>().Single(button => AutomationProperties.GetName(button) == "Stop recording a shortcut");

        Assert.AreEqual("Stop recording a shortcut", ToolTip.GetTip(stop), "a control shown as a glyph alone carries a tip naming its action");
    }

    /// <summary>The screenshot field, recording after a click, over the default shortcuts with <paramref name="held"/> another program's.</summary>
    private static (StartedShortcuts Started, ShortcutRecording Recording, ShortcutField Field) Recording(string? held = null)
    {
        var started = held is null ? StartedShortcuts.Start() : StartedShortcuts.Start(held: Shortcut.Parse(held).Value!);
        var recording = started.Page().ShortcutField(ShortcutAct.Screenshot);
        var field = new ShortcutField(recording);
        Click(field);
        Assert.IsTrue(recording.IsRecording);
        return (started, recording, field);
    }

    /// <summary>
    /// After a refusal: the field still records, nothing is registered meanwhile, nothing bound or written; Esc then ends
    /// the recording with nothing changed, the four registered again and the refusal no longer said (G32).
    /// </summary>
    private static void RefusedStillRecording(StartedShortcuts started, ShortcutRecording recording, ShortcutField field)
    {
        Assert.IsTrue(recording.IsRecording, "the field still records: the next press is the retry (G32)");
        Assert.IsEmpty(started.Seam.Registered, "the four still released, so a retry of one of their combinations is refused, not run");
        Assert.AreEqual("Ctrl+PrtScn", started.Settings.Current.Shortcuts.Screenshot, "nothing written");

        Down(field, Key.Escape, KeyModifiers.None);
        Up(field, Key.Escape, KeyModifiers.None);

        Unchanged(started, recording);
        Assert.IsNull(recording.WarningWords, "the row says only what is still true");
    }

    private static void Unchanged(StartedShortcuts started, ShortcutRecording recording)
    {
        Assert.IsFalse(recording.IsRecording);
        Assert.AreEqual("Ctrl+PrtScn", recording.Text, "nothing bound");
        Assert.AreEqual("Ctrl+PrtScn", started.Settings.Current.Shortcuts.Screenshot, "nothing written");
        CollectionAssert.AreEquivalent(
            new[] { "Ctrl+Alt+Space", "Ctrl+PrtScn", "Ctrl+Shift+PrtScn", "Win+Alt+V" }.Select(text => Shortcut.Parse(text).Value!).ToList(),
            started.Seam.Registered.ToList(), "the four registered again, nothing else");
    }

    private static void Click(ShortcutField field) => field.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static void Down(ShortcutField field, Key key, KeyModifiers modifiers) =>
        field.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers });

    private static void Up(ShortcutField field, Key key, KeyModifiers modifiers) =>
        field.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = key, KeyModifiers = modifiers });
}
