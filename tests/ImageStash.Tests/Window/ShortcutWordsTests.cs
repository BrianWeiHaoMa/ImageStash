using Avalonia.Input;
using ImageStash.Seams;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// What a shortcut's refusal and its unavailability say: the row's sentence names the combination and why and says what
/// to do (G32, G35), never as "… failed: …"; the tooltip's line names the combination, why, and the act left with no
/// shortcut (G33); the character a combination types is named only where it shows; and every reason, one these words
/// do not foresee included, comes out whole.
/// </summary>
[TestClass]
public sealed class ShortcutWordsTests
{
    private static readonly ShortcutReason Held = ShortcutReason.Of(ShortcutRefusal.HeldByAnotherProgram);

    [TestMethod]
    [DataRow("held", "Ctrl+PrtScn is already used by another app. Press a different combination.")]
    [DataRow("types @", "Ctrl+PrtScn types @ on your keyboard layout, so it can't be a shortcut.")]
    [DataRow("types", "Ctrl+PrtScn types a character on your keyboard layout, so it can't be a shortcut.")]
    [DataRow("paste", "Ctrl+PrtScn is the paste key ImageStash presses, so it can't be a shortcut.")]
    [DataRow("used", "Take a screenshot already uses Ctrl+PrtScn.")]
    [DataRow("let go", "Ctrl+PrtScn wasn't recorded because a modifier came up first. Hold Ctrl, Alt or Shift until the key is up.")]
    [DataRow("not a key", "Ctrl+PrtScn uses a key this PC can't register. Press a different combination.")]
    [DataRow("not a shortcut", "\"Ctrl+PrtScn\" isn't a shortcut ImageStash understands. Press a new one.")]
    [DataRow("stated", "Ctrl+PrtScn couldn't be registered: The parameter is incorrect. Press a different combination.")]
    [DataRow("stated blank", "Ctrl+PrtScn couldn't be registered. Press a different combination.")]
    [DataRow("plain", "Add Ctrl, Alt or Win: Ctrl+PrtScn is needed by every app.")]
    public void TheRowSaysWhatWasRefusedWhyAndWhatToDo(string reason, string sentence)
    {
        Assert.AreEqual(sentence, ShortcutWords.Row(new ShortcutProblem(ShortcutAct.Record, "Ctrl+PrtScn", Reason(reason))));
    }

    [TestMethod]
    [DataRow("held", "Ctrl+PrtScn is used by another app, so Record a clip has no shortcut.")]
    [DataRow("types @", "Ctrl+PrtScn types @, so Record a clip has no shortcut.")]
    [DataRow("types", "Ctrl+PrtScn types a character, so Record a clip has no shortcut.")]
    [DataRow("paste", "Ctrl+PrtScn is the paste key ImageStash presses, so Record a clip has no shortcut.")]
    [DataRow("used", "Take a screenshot already uses Ctrl+PrtScn, so Record a clip has no shortcut.")]
    [DataRow("let go", "Ctrl+PrtScn wasn't recorded, so Record a clip has no shortcut.")]
    [DataRow("not a key", "Ctrl+PrtScn uses a key this PC can't register, so Record a clip has no shortcut.")]
    [DataRow("not a shortcut", "\"Ctrl+PrtScn\" isn't a shortcut ImageStash understands, so Record a clip has no shortcut.")]
    [DataRow("stated", "Ctrl+PrtScn couldn't be registered (The parameter is incorrect), so Record a clip has no shortcut.")]
    [DataRow("stated blank", "Ctrl+PrtScn couldn't be registered, so Record a clip has no shortcut.")]
    [DataRow("plain", "Ctrl+PrtScn is needed by every app, so Record a clip has no shortcut.")]
    public void TheTooltipLineSaysTheCombinationWhyAndWhichActHasNoShortcut(string reason, string line)
    {
        Assert.AreEqual(line, ShortcutWords.ToolTipLine(new ShortcutProblem(ShortcutAct.Record, "Ctrl+PrtScn", Reason(reason))));
    }

    [TestMethod]
    public void EveryKindOfReasonComesOutWholeNamingTheCombinationAndTheAct()
    {
        var kinds = Enum.GetValues<ShortcutReasonKind>().Append((ShortcutReasonKind)99);
        foreach (var kind in kinds)
        {
            var reason = new ShortcutReason(kind, Other: kind == ShortcutReasonKind.UsedByAnother ? ShortcutAct.Open : null);
            foreach (var act in Enum.GetValues<ShortcutAct>())
            {
                var problem = new ShortcutProblem(act, "Win+Alt+F9", reason);
                var row = ShortcutWords.Row(problem);
                var line = ShortcutWords.ToolTipLine(problem);

                StringAssert.Contains(row, "Win+Alt+F9", $"{kind}: the row names what was refused (G35)");
                Assert.DoesNotContain("failed", row, $"{kind}: guidance, never a failure's sentence");
                StringAssert.Contains(line, "Win+Alt+F9", $"{kind}: the tooltip names which combination (G33)");
                StringAssert.Contains(line, ShortcutWords.ActName(act), $"{kind}: and which act has no shortcut (G33)");
                if (kind != ShortcutReasonKind.HeldByAnotherProgram)
                {
                    Assert.AreNotEqual(ShortcutWords.Row(problem with { Reason = Held }), row, $"{kind}: says its own why, not another's");
                }
            }
        }
    }

    [TestMethod]
    [DataRow("Delete", "Add Ctrl, Alt or Win: Delete alone is needed by every app.")]
    [DataRow("F2", "Add Ctrl, Alt or Win: F2 alone is needed by every app.")]
    [DataRow("Shift+Delete", "Add Ctrl, Alt or Win: Shift+Delete is needed by every app.")]
    public void APlainKeysRowNamesTheKeyAndWhatToAdd(string combination, string sentence)
    {
        Assert.AreEqual(sentence, ShortcutWords.Row(new ShortcutProblem(ShortcutAct.Screenshot, combination, ShortcutReason.PlainKey)));
    }

    [TestMethod]
    [DataRow("\u0001")]
    [DataRow(" ")]
    [DataRow(" ")]
    [DataRow("́")]
    [DataRow("‍")]
    [DataRow("")]
    [DataRow(null)]
    public void ACharacterWithNoPrintableFormIsNeverShownAsOne(string? typed)
    {
        var problem = new ShortcutProblem(ShortcutAct.Screenshot, "Ctrl+Alt+Q", ShortcutReason.Of(ShortcutRefusal.TypesACharacter(typed)));

        Assert.AreEqual("Ctrl+Alt+Q types a character on your keyboard layout, so it can't be a shortcut.", ShortcutWords.Row(problem));
        Assert.AreEqual("Ctrl+Alt+Q types a character, so Take a screenshot has no shortcut.", ShortcutWords.ToolTipLine(problem));
    }

    [TestMethod]
    public void AnEmptySettingIsSaidAsNoShortcutSetNeverAsAQuotedBlank()
    {
        var problem = new ShortcutProblem(ShortcutAct.Open, "", ShortcutReason.NotAShortcut);

        Assert.AreEqual("No shortcut is set here. Press a new one.", ShortcutWords.Row(problem));
        Assert.AreEqual("Open ImageStash has no shortcut set.", ShortcutWords.ToolTipLine(problem));
        Assert.AreEqual("Open ImageStash has no shortcut", ShortcutWords.ToolTipLineWithoutWhy(problem));
        Assert.AreEqual("Open ImageStash unavailable", ShortcutWords.ToolTipLineShortest(problem));
    }

    [TestMethod]
    public void TheShorterLinesStillSayWhichShortcutTwoActsUnavailableAtOnceToldApart()
    {
        var open = new ShortcutProblem(ShortcutAct.Open, "Ctrl+Alt+Space", Held);
        var stash = new ShortcutProblem(ShortcutAct.StashClipboardImage, "Ctrl+Alt+Space", Held);

        Assert.AreEqual("Open ImageStash: Ctrl+Alt+Space is unavailable", ShortcutWords.ToolTipLineWithoutWhy(open));
        Assert.AreEqual("Stash clipboard image: Ctrl+Alt+Space is unavailable", ShortcutWords.ToolTipLineWithoutWhy(stash));
        Assert.AreEqual("Open ImageStash unavailable", ShortcutWords.ToolTipLineShortest(open), "the act's name where it is the shorter");
        Assert.AreEqual("Ctrl+Alt+Space is unavailable", ShortcutWords.ToolTipLineShortest(stash), "the combination where it is the shorter");
        Assert.AreNotEqual(ShortcutWords.ToolTipLine(open), ShortcutWords.ToolTipLine(stash), "the act tells them apart (G33)");
    }

    private static ShortcutReason Reason(string name) => name switch
    {
        "held" => Held,
        "types @" => ShortcutReason.Of(ShortcutRefusal.TypesACharacter("@")),
        "types" => ShortcutReason.Of(ShortcutRefusal.TypesACharacter(null)),
        "paste" => ShortcutReason.PasteKeystroke,
        "used" => ShortcutReason.UsedBy(ShortcutAct.Screenshot),
        "let go" => ShortcutReason.ModifiersLetGoFirst,
        "not a key" => ShortcutReason.Of(ShortcutRefusal.NotAKey(Key.ImeConvert)),
        "not a shortcut" => ShortcutReason.NotAShortcut,
        "stated" => ShortcutReason.Of(ShortcutRefusal.Stated("The parameter is incorrect.")),
        "stated blank" => ShortcutReason.Of(ShortcutRefusal.Stated("  ")),
        "plain" => ShortcutReason.PlainKey,
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };
}
