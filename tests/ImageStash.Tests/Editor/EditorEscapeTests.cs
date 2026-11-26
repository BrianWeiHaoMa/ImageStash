using ImageStash.Editor;
using ImageStash.Layers;
using ImageStash.Tests.Support;

namespace ImageStash.Tests.Editor;

/// <summary>
/// Esc in the editor, one act per press, through <see cref="EditorEscape"/> as the overlay hands it every Esc key-down
/// and release, on a stepped clock. G20: "While they differ, an Esc with nothing selected first has the option row say,
/// in place of what the mouse does, that Esc pressed again discards the changes, and leaves the edit only when pressed
/// again while the row says so". The row asks for <see cref="EditorSession.AskingFor"/>.
/// </summary>
[TestClass]
public sealed class EditorEscapeTests
{
    [TestMethod]
    public async Task OnAChangedEditTheFirstEscAsksAndASecondWhileItAsksLeaves()
    {
        using var editing = await Changed();
        var (session, escape) = (editing.Session, new EditorEscape());
        var hintBefore = Hint(session);

        Assert.AreEqual(EscapeAct.Taken, Press(escape, session), "the first Esc keeps the edit");
        Assert.IsTrue(session.IsOpen);
        Assert.AreEqual(EditorSession.AskingWords, Hint(session), "the row says it in place of what the mouse does");
        Assert.AreNotEqual(hintBefore, Hint(session));

        editing.Clock.Advance(EditorSession.AskingFor - TimeSpan.FromMilliseconds(1));
        Assert.AreEqual(EscapeAct.Leave, Press(escape, session), "pressed again while the row says so, it leaves");
    }

    [TestMethod]
    public async Task OnceTheTimeHasPassedTheToolsHintIsBackWithoutAKeyAndAnEscOnlyAsksAgain()
    {
        using var editing = await Changed();
        var (session, escape) = (editing.Session, new EditorEscape());
        var hintBefore = Hint(session);
        var changes = 0;
        session.Changed += () => changes++;
        Press(escape, session);

        editing.Clock.Advance(EditorSession.AskingFor);

        Assert.IsGreaterThanOrEqualTo(2, changes, "the row was told the asking ended, with no key pressed");
        Assert.AreEqual(hintBefore, Hint(session), "the tool's hint is back");
        Assert.AreEqual(EscapeAct.Taken, Press(escape, session), "an Esc after the time only asks again");
        Assert.IsTrue(session.IsOpen);
        Assert.AreEqual(EditorSession.AskingWords, Hint(session));
    }

    [TestMethod]
    public async Task AnEscOnceTheTimeHasPassedOnlyAsksAgainThoughTheTimerHasNotYetRun()
    {
        using var editing = await Changed();
        var (session, escape) = (editing.Session, new EditorEscape());
        Press(escape, session);

        // Time moves on with the timer still pending, as on a busy UI thread.
        editing.Clock.Pass(EditorSession.AskingFor);

        Assert.IsNull(session.Asking, "the words are gone once their time has passed, whenever read");
        Assert.AreEqual(EscapeAct.Taken, Press(escape, session), "so this Esc asks again rather than leaving");
        Assert.IsTrue(session.IsOpen);
    }

    [TestMethod]
    public async Task AHeldEscsRepeatsNeverLeaveAChangedEdit()
    {
        using var editing = await Changed();
        var (session, escape) = (editing.Session, new EditorEscape());

        Assert.AreEqual(EscapeAct.Taken, escape.Pressed(session, pickerOpen: false, inField: false, inMenu: false));
        for (var repeat = 0; repeat < 20; repeat++)
        {
            Assert.AreEqual(EscapeAct.Taken, escape.Pressed(session, pickerOpen: false, inField: false, inMenu: false), "a repeat with no release between is no second press");
        }

        Assert.IsTrue(session.IsOpen);
        escape.Released();
        Assert.AreEqual(EscapeAct.Leave, Press(escape, session), "a second press after the release leaves");
    }

    [TestMethod]
    public async Task FromAnOpenFieldFourSeparatePressesEachDoOneThing()
    {
        using var editing = await StillInTheEditor.Open(time: new SteppedClock());
        var (session, escape) = (editing.Session, new EditorEscape());
        session.Click(10, 5);
        session.SetTypedText("hi");

        Assert.AreEqual(EscapeAct.LeftText, Press(escape, session, inField: true), "the first leaves the field");
        Assert.IsFalse(session.Typing);
        Assert.IsNotNull(session.Selected);
        Assert.AreEqual(EscapeAct.Taken, Press(escape, session), "the second deselects");
        Assert.IsNull(session.Selected);
        Assert.IsNull(session.Asking, "deselecting is not the first Esc of the asking");
        Assert.AreEqual(EscapeAct.Taken, Press(escape, session), "the third asks");
        Assert.AreEqual(EditorSession.AskingWords, session.Asking);
        Assert.AreEqual(EscapeAct.Leave, Press(escape, session), "the fourth leaves");
    }

    [TestMethod]
    public async Task AHeldEscFromAnOpenFieldLeavesTheFieldAndDoesNothingMore()
    {
        using var editing = await StillInTheEditor.Open(time: new SteppedClock());
        var (session, escape) = (editing.Session, new EditorEscape());
        session.Click(10, 5);
        session.SetTypedText("hi");

        Assert.AreEqual(EscapeAct.LeftText, escape.Pressed(session, pickerOpen: false, inField: true, inMenu: false));
        for (var repeat = 0; repeat < 10; repeat++)
        {
            escape.Pressed(session, pickerOpen: false, inField: false, inMenu: false);
        }

        Assert.IsNotNull(session.Selected, "still selected");
        Assert.IsNull(session.Asking);
        Assert.IsTrue(session.IsOpen);
    }

    [TestMethod]
    public async Task AnEscThatClosedThePickerIsNotTheFirstOfTheAsking()
    {
        using var editing = await Changed();
        var (session, escape) = (editing.Session, new EditorEscape());

        Assert.AreEqual(EscapeAct.ClosePicker, Press(escape, session, pickerOpen: true));

        Assert.IsNull(session.Asking);
        Assert.AreEqual(EscapeAct.Taken, Press(escape, session), "the next Esc asks first");
        Assert.AreEqual(EscapeAct.Leave, Press(escape, session));
    }

    [TestMethod]
    public async Task AnUnchangedEditLeavesOnOneEsc()
    {
        using var editing = await StillInTheEditor.Open(time: new SteppedClock());

        Assert.AreEqual(EscapeAct.Leave, Press(new EditorEscape(), editing.Session));
        Assert.IsNull(editing.Session.Asking);
    }

    [TestMethod]
    public async Task AnotherInputEndsTheAskingSoTheNextEscAsksAgain()
    {
        using var editing = await Changed();
        var (session, escape) = (editing.Session, new EditorEscape());
        Press(escape, session);

        session.EndAsking();

        Assert.IsNull(session.Asking);
        Assert.AreEqual(EscapeAct.Taken, Press(escape, session), "asks again rather than leaving");
    }

    [TestMethod]
    public async Task AnEscInAMenuIsTheMenusOwn()
    {
        using var editing = await Changed();
        var (session, escape) = (editing.Session, new EditorEscape());

        Assert.AreEqual(EscapeAct.None, Press(escape, session, inField: true, inMenu: true));
        Assert.IsNull(session.Asking);
    }

    /// <summary>The still with a hide drawn and deselected: changed, nothing selected.</summary>
    private static async Task<Steppable> Changed()
    {
        var clock = new SteppedClock();
        var editing = await StillInTheEditor.Open(time: clock);
        editing.Session.Tool = editing.Session.Hide();
        editing.Session.DragThrough((2, 2), (20, 12));
        editing.Session.Deselect();
        Assert.IsTrue(editing.Session.Differs);
        return new Steppable(editing, clock);
    }

    /// <summary>One press and its release.</summary>
    private static EscapeAct Press(EditorEscape escape, EditorSession session, bool pickerOpen = false, bool inField = false, bool inMenu = false)
    {
        var act = escape.Pressed(session, pickerOpen, inField, inMenu);
        escape.Released();
        return act;
    }

    /// <summary>What the overlay puts in the hint line.</summary>
    private static string Hint(EditorSession session) => session.Asking ?? session.Tool.Hint;

    private sealed class Steppable(StillInTheEditor editing, SteppedClock clock) : IDisposable
    {
        public EditorSession Session => editing.Session;

        public SteppedClock Clock => clock;

        public void Dispose() => editing.Dispose();
    }
}
