using ImageStash.Seams;

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
            ShortcutReasonKind.PlainKey => $"Add Ctrl, Alt or Win: {combination}{(Alone(combination) ? " alone" : "")} is needed by every app.",
            _ => $"{combination} couldn't be registered{Stated(reason, ": {0}")}. Press a different combination.",
        };
    }

    /// <summary>
    /// The tooltip's line, one short sentence: the combination, why, and which act has no shortcut (G33), as the row's
    /// words say it.
    /// </summary>
    public static string ToolTipLine(ShortcutProblem problem)
    {
        var cause = ToolTipCause(problem);
        var act = ActName(problem.Act);
        return cause is null ? $"{act} has no shortcut set." : $"{cause}, so {act} has no shortcut.";
    }

    // While paused, the combination can identify the shortcut on a shorter line. A setting with a misplaced or
    // duplicate combination still needs the affected act's name.
    internal static string ToolTipLineWithCompactWhy(ShortcutProblem problem)
    {
        var cause = ToolTipCause(problem);
        if (cause is null)
        {
            return ToolTipLine(problem);
        }

        return NeedsActName(problem) ? $"{ActName(problem.Act)}: {cause}." : $"{cause}.";
    }

    private static string? ToolTipCause(ShortcutProblem problem)
    {
        var combination = problem.Combination;
        var reason = problem.Reason;
        return reason.Kind switch
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
            ShortcutReasonKind.PlainKey => $"{combination} is needed by every app",
            _ => $"{combination} couldn't be registered{Stated(reason, " ({0})")}",
        };
    }

    /// <summary>
    /// The notification's sentence when a shortcut becomes unavailable: the tooltip's whole line, so the two never say it
    /// differently (G16, G33).
    /// </summary>
    public static string Notification(ShortcutProblem problem) => ToolTipLine(problem);

    /// <summary>
    /// A menu entry whose shortcut is unavailable: its act's words, then that its shortcut is unavailable, the shortcut
    /// itself still shown beside it (G2, G34); the entry still runs its act (G33).
    /// </summary>
    public static string EntryMarked(string header) => $"{header} (shortcut unavailable)";

    /// <summary>What stands beside an unavailable shortcut's key caps in the empty stash's row (G2).</summary>
    public const string UnavailableBesideKeys = "unavailable";

    /// <summary>The empty stash's line on opening ImageStash while the open shortcut is unavailable: it names it and offers only the icon (G2, G3).</summary>
    public static string OpenLineUnavailable(string open) => $"Open ImageStash from its icon in the tray ({open} is unavailable).";

    /// <summary>The tooltip's line with its why given way: the act and its combination, still saying which shortcut (G33).</summary>
    public static string ToolTipLineWithoutWhy(ShortcutProblem problem) =>
        IsBlank(problem.Combination) ? $"{ActName(problem.Act)} has no shortcut" : $"{ActName(problem.Act)}: {problem.Combination} is unavailable";

    /// <summary>
    /// The shortest line still saying which shortcut: its combination, or its act's name when that is shorter. Four of
    /// these, one per act, always fit the tooltip together, since each is no longer than its act's.
    /// </summary>
    public static string ToolTipLineShortest(ShortcutProblem problem) => ToolTipLineShortest(problem, useCompactForms: false);

    internal static string ToolTipLineShortest(ShortcutProblem problem, bool useCompactForms)
    {
        var byAct = $"{ActName(problem.Act)} unavailable";
        var byCombination = $"{problem.Combination} is unavailable";
        var nameAct = IsBlank(problem.Combination) || (useCompactForms && NeedsActName(problem));
        return nameAct || byAct.Length <= byCombination.Length ? byAct : byCombination;
    }

    internal static string ToolTipUnavailableShortcuts(IEnumerable<ShortcutProblem> problems) =>
        $"Unavailable: {string.Join(", ", problems.Select(ShortName))}";

    private static string ShortName(ShortcutProblem problem)
    {
        var act = ActName(problem.Act);
        return NeedsActName(problem) || IsBlank(problem.Combination) || act.Length <= problem.Combination.Length
            ? act
            : problem.Combination;
    }

    private static bool NeedsActName(ShortcutProblem problem) =>
        problem.Reason.Kind is ShortcutReasonKind.NotAShortcut or ShortcutReasonKind.UsedByAnother;

    /// <summary>The character the layout types, or, with none printable (a dead key, a control code, white space), "a character".</summary>
    private static string Typed(ShortcutReason reason) => reason.Character is { Length: > 0 } character ? character : "a character";

    /// <summary>The platform's words, their own full stop dropped, in <paramref name="format"/>; nothing when it gave none.</summary>
    private static string Stated(ShortcutReason reason, string format) =>
        reason.Words?.Trim().TrimEnd('.') is { Length: > 0 } words ? string.Format(format, words) : "";

    private static bool IsBlank(string combination) => string.IsNullOrWhiteSpace(combination);

    /// <summary>A combination of one key and no modifier, which the plain key's words call "alone".</summary>
    private static bool Alone(string combination) => Shortcut.Parse(combination).Value is { Gesture.KeyModifiers: Avalonia.Input.KeyModifiers.None };
}
