using ImageStash.Seams;

namespace ImageStash.Window;

/// <summary>The kinds of reason a Settings field refuses a combination for, or a shortcut is unavailable for (G32, G33).</summary>
public enum ShortcutReasonKind
{
    HeldByAnotherProgram,
    TypesACharacter,
    NotAKey,
    /// <summary>A reason the platform words itself, kept whole in <see cref="ShortcutReason.Words"/>.</summary>
    Stated,
    /// <summary>The plain <c>Ctrl+V</c>, the paste keystroke the handoff sends (G8, G32).</summary>
    PasteKeystroke,
    /// <summary>Another of the four uses the combination, named by <see cref="ShortcutReason.Other"/>.</summary>
    UsedByAnother,
    /// <summary>A field saw the combination's modifiers come up before its key, so nothing was recorded.</summary>
    ModifiersLetGoFirst,
    /// <summary>The setting, as written, is no shortcut ImageStash understands, or is empty.</summary>
    NotAShortcut,
    /// <summary>A key every app needs, pressed with none of Ctrl, Alt and the Windows key (<see cref="PlainKeyRefusal"/>).</summary>
    PlainKey,
}

/// <summary>
/// Why a shortcut is refused or unavailable: its kind, and apart from it the detail its words name: the character the
/// layout types (<see cref="Character"/>, null when it has no printable form), the act that uses the combination
/// (<see cref="Other"/>), or the platform's own words (<see cref="Words"/>). Two reasons are the same when their kinds
/// are; the detail never makes a new reason.
/// </summary>
public sealed record ShortcutReason(ShortcutReasonKind Kind, string? Character = null, ShortcutAct? Other = null, string? Words = null)
{
    public static readonly ShortcutReason PasteKeystroke = new(ShortcutReasonKind.PasteKeystroke);

    public static readonly ShortcutReason ModifiersLetGoFirst = new(ShortcutReasonKind.ModifiersLetGoFirst);

    public static readonly ShortcutReason NotAShortcut = new(ShortcutReasonKind.NotAShortcut);

    public static readonly ShortcutReason PlainKey = new(ShortcutReasonKind.PlainKey);

    public static ShortcutReason UsedBy(ShortcutAct other) => new(ShortcutReasonKind.UsedByAnother, Other: other);

    /// <summary>The seam's reason, its detail kept.</summary>
    public static ShortcutReason Of(ShortcutRefusal refused) => new(
        refused.Kind switch
        {
            ShortcutRefusalKind.HeldByAnotherProgram => ShortcutReasonKind.HeldByAnotherProgram,
            ShortcutRefusalKind.TypesACharacter => ShortcutReasonKind.TypesACharacter,
            ShortcutRefusalKind.NotAKey => ShortcutReasonKind.NotAKey,
            _ => ShortcutReasonKind.Stated,
        },
        refused.Character,
        Words: refused.Words);
}

/// <summary>
/// A combination a Settings field refused, or the shortcut an act cannot have now (G32, G33): the act, the combination
/// as its field shows it (the setting as written when it is no shortcut, empty when the setting is), and why, each
/// readable apart. <see cref="ShortcutWords"/> says it.
/// </summary>
public sealed record ShortcutProblem(ShortcutAct Act, string Combination, ShortcutReason Reason);
