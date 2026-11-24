namespace ImageStash.Window;

/// <summary>
/// The one home of what a shortcut's refusal and its unavailability say (G32, G33, G35): the sentence under a Settings
/// field's row, which names the combination and why and says what to do, and the tooltip's line, which names the
/// combination, why, and the act left with no shortcut, with its shorter forms for a tooltip that cannot hold it whole.
/// Every reason comes out whole, one this file has no words for as the platform's own.
/// </summary>
public static class ShortcutWords
{
    /// <summary>The act's name, as its tray entry and its Settings row name it.</summary>
    public static string ActName(ShortcutAct act) => act switch
    {
        ShortcutAct.Open => TrayMenu.OpenHeader,
        ShortcutAct.Screenshot => TrayMenu.TakeScreenshotHeader,
        ShortcutAct.Record => TrayMenu.RecordClipHeader,
        ShortcutAct.StashClipboardImage => TrayMenu.StashClipboardImageHeader,
        _ => throw new ArgumentOutOfRangeException(nameof(act)),
    };

    /// <summary>The sentence under the field's row: what was refused and why, and what to do (G32, G35).</summary>
    public static string Row(ShortcutProblem problem)
    {
        var combination = problem.Combination;
        var reason = problem.Reason;
        return reason.Kind switch
        {
            ShortcutReasonKind.HeldByAnotherProgram => $"{combination} is already used by another app. Press a different combination.",
            ShortcutReasonKind.TypesACharacter => $"{combination} types {Typed(reason)} on your keyboard layout, so it can't be a shortcut.",
            ShortcutReasonKind.PasteKeystroke => $"{combination} is the paste key ImageStash presses, so it can't be a shortcut.",
            ShortcutReasonKind.UsedByAnother when reason.Other is { } other => $"{ActName(other)} already uses {combination}.",
            ShortcutReasonKind.ModifiersLetGoFirst => $"{combination} wasn't recorded because a modifier came up first. Hold Ctrl, Alt or Shift until the key is up.",
            ShortcutReasonKind.NotAKey => $"{combination} uses a key this PC can't register. Press a different combination.",
            ShortcutReasonKind.NotAShortcut when IsBlank(combination) => "No shortcut is set here. Press a new one.",
            ShortcutReasonKind.NotAShortcut => $"\"{combination}\" isn't a shortcut ImageStash understands. Press a new one.",
            _ => $"{combination} couldn't be registered{Stated(reason, ": {0}")}. Press a different combination.",
        };
    }

    /// <summary>
    /// The tooltip's line, one short sentence: the combination, why, and which act has no shortcut (G33), as the row's
    /// words say it.
    /// </summary>
    public static string ToolTipLine(ShortcutProblem problem)
    {
        var combination = problem.Combination;
        var reason = problem.Reason;
        var act = ActName(problem.Act);
        var cause = reason.Kind switch
        {
            ShortcutReasonKind.HeldByAnotherProgram => $"{combination} is used by another app",
            // Short, so the line keeps its why beside two other failures cut to their actions (G33).
            ShortcutReasonKind.TypesACharacter => $"{combination} types {Typed(reason)}",
            ShortcutReasonKind.PasteKeystroke => $"{combination} is the paste key ImageStash presses",
            ShortcutReasonKind.UsedByAnother when reason.Other is { } other => $"{ActName(other)} already uses {combination}",
            ShortcutReasonKind.ModifiersLetGoFirst => $"{combination} wasn't recorded",
            ShortcutReasonKind.NotAKey => $"{combination} uses a key this PC can't register",
            ShortcutReasonKind.NotAShortcut when IsBlank(combination) => null,
            ShortcutReasonKind.NotAShortcut => $"\"{combination}\" isn't a shortcut ImageStash understands",
            _ => $"{combination} couldn't be registered{Stated(reason, " ({0})")}",
        };
        return cause is null ? $"{act} has no shortcut set." : $"{cause}, so {act} has no shortcut.";
    }

    /// <summary>The tooltip's line with its why given way: the act and its combination, still saying which shortcut (G33).</summary>
    public static string ToolTipLineWithoutWhy(ShortcutProblem problem) =>
        IsBlank(problem.Combination) ? $"{ActName(problem.Act)} has no shortcut" : $"{ActName(problem.Act)}: {problem.Combination} is unavailable";

    /// <summary>
    /// The shortest line still saying which shortcut: its combination, or its act's name when that is shorter. Four of
    /// these, one per act, always fit the tooltip together, since each is no longer than its act's.
    /// </summary>
    public static string ToolTipLineShortest(ShortcutProblem problem)
    {
        var byAct = $"{ActName(problem.Act)} unavailable";
        var byCombination = $"{problem.Combination} is unavailable";
        return IsBlank(problem.Combination) || byAct.Length <= byCombination.Length ? byAct : byCombination;
    }

    /// <summary>The character the layout types, or, with none printable (a dead key, a control code, white space), "a character".</summary>
    private static string Typed(ShortcutReason reason) => reason.Character is { Length: > 0 } character ? character : "a character";

    /// <summary>The platform's words, their own full stop dropped, in <paramref name="format"/>; nothing when it gave none.</summary>
    private static string Stated(ShortcutReason reason, string format) =>
        reason.Words?.Trim().TrimEnd('.') is { Length: > 0 } words ? string.Format(format, words) : "";

    private static bool IsBlank(string combination) => string.IsNullOrWhiteSpace(combination);
}
