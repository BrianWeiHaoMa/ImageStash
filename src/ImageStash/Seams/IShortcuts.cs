using System.Globalization;
using Avalonia.Input;

namespace ImageStash.Seams;

/// <summary>
/// System-wide shortcuts (F5, G33): registered as the platform's global hot keys, judged against the keyboard layout in
/// use, and pressed on a thread of the seam's own, never the UI thread.
/// </summary>
public interface IShortcuts
{
    /// <summary>
    /// Registers <paramref name="shortcut"/> system-wide, unless another program holds it or it would type a character
    /// on the keyboard layout in use; a combination that would type one is not left registered. Returns null once it is
    /// registered, else why not. <paramref name="pressed"/> runs on the seam's thread at each press that passed the layout
    /// check made before acting, and returns quickly. Called on the UI thread; returns once the registration is done or refused.
    /// </summary>
    ShortcutRefusal? Register(Shortcut shortcut, Action pressed);

    /// <summary>Releases <paramref name="shortcut"/>; nothing happens when it is not registered.</summary>
    void Release(Shortcut shortcut);

    /// <summary>
    /// <paramref name="released"/> runs on the seam's thread, with the reason, each time the seam itself releases a
    /// registered shortcut because the keyboard layout in use makes it type a character: judged at least once a second
    /// and once more at a press, before it acts, whose press is then swallowed (G33).
    /// </summary>
    void ListenForReleases(Action<Shortcut, ShortcutRefusal> released);
}

/// <summary>The kinds of reason the seam gives for not registering a shortcut, or for letting one go (G33).</summary>
public enum ShortcutRefusalKind
{
    HeldByAnotherProgram,
    TypesACharacter,
    NotAKey,
    /// <summary>A reason the platform words itself, such as an error's text, kept whole in <see cref="ShortcutRefusal.Words"/>.</summary>
    Stated,
}

/// <summary>
/// Why the seam did not register a shortcut, or let one go: its kind, and apart from it the detail a sentence names,
/// the character a combination types on the layout judged, or the platform's own words. Callers read the kind, never
/// the words, to tell reasons apart; the detail never makes a new reason.
/// </summary>
public sealed record ShortcutRefusal(ShortcutRefusalKind Kind, string? Character = null, string? Words = null)
{
    public static readonly ShortcutRefusal HeldByAnotherProgram = new(ShortcutRefusalKind.HeldByAnotherProgram);

    /// <summary>
    /// The combination types a character on the layout judged: <paramref name="character"/>, what its press types there,
    /// kept only when every one of its characters shows as a glyph; a dead key, a control code, white space or a mark
    /// that shows nothing on its own is kept as none, so a sentence then names no character.
    /// </summary>
    public static ShortcutRefusal TypesACharacter(string? character) =>
        new(ShortcutRefusalKind.TypesACharacter, character is { Length: > 0 } && character.All(Shows) ? character : null);

    public static ShortcutRefusal NotAKey(Key key) => new(ShortcutRefusalKind.NotAKey, Words: key.ToString());

    public static ShortcutRefusal Stated(string words) => new(ShortcutRefusalKind.Stated, Words: words);

    private static bool Shows(char character) =>
        !char.IsControl(character) && !char.IsWhiteSpace(character)
        && char.GetUnicodeCategory(character) is not (UnicodeCategory.Format or UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark
            or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.PrivateUse or UnicodeCategory.OtherNotAssigned);
}

/// <summary>
/// A shortcut as it crosses the seam: Avalonia's portable key identity, and its text in the spelling F5 writes, rebuilt
/// from the gesture, so what is shown is what fires.
/// </summary>
public sealed record Shortcut(KeyGesture Gesture, string Text)
{
    private const string NotAShortcut = "not a shortcut ImageStash understands";

    // The order the text writes them in: Win first, as F5 writes Win+Alt+V and Windows writes its own Win+Shift+S.
    private static readonly (string Name, KeyModifiers Modifier)[] Modifiers =
        [("Win", KeyModifiers.Meta), ("Ctrl", KeyModifiers.Control), ("Alt", KeyModifiers.Alt), ("Shift", KeyModifiers.Shift)];

    // F5's spellings and the digits, which Avalonia's key names spell D0 to D9; Enter and PageUp also share their
    // numbers with Return and Prior, so these keys are written by name, not by Enum.GetName.
    private static readonly (string Name, Key Key)[] Aliases =
    [
        ("PrtScn", Key.PrintScreen), ("Space", Key.Space), ("Enter", Key.Enter), ("Esc", Key.Escape),
        ("PageUp", Key.PageUp), ("PageDown", Key.PageDown), ("CapsLock", Key.CapsLock),
        ("0", Key.D0), ("1", Key.D1), ("2", Key.D2), ("3", Key.D3), ("4", Key.D4),
        ("5", Key.D5), ("6", Key.D6), ("7", Key.D7), ("8", Key.D8), ("9", Key.D9),
    ];

    /// <summary>
    /// Reads modifiers (Ctrl, Alt, Shift, Win; any case and order) and exactly one key, joined by <c>+</c>: the key by
    /// Avalonia's name for it or one of F5's spellings. Anything else is refused as not a shortcut ImageStash understands.
    /// </summary>
    public static Outcome<Shortcut> Parse(string text)
    {
        var modifiers = KeyModifiers.None;
        Key? key = null;
        foreach (var part in text.Split('+').Select(part => part.Trim()))
        {
            if (Modifiers.FirstOrDefault(m => string.Equals(m.Name, part, StringComparison.OrdinalIgnoreCase)) is { Name: not null } modifier)
            {
                modifiers |= modifier.Modifier;
            }
            else if (key is null && KeyNamed(part) is { } named)
            {
                key = named;
            }
            else
            {
                return new Failure("Read a shortcut", text, NotAShortcut);
            }
        }

        if (key is not { } found)
        {
            return new Failure("Read a shortcut", text, NotAShortcut);
        }

        return Outcome<Shortcut>.Of(Of(new KeyGesture(found, modifiers)));
    }

    /// <summary>The shortcut a press makes, its text in F5's spelling, as <see cref="Parse"/> reads it back.</summary>
    public static Shortcut Of(KeyGesture gesture) => new(gesture, TextOf(gesture));

    private static Key? KeyNamed(string name)
    {
        if (Aliases.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase)) is { Name: not null } alias)
        {
            return alias.Key;
        }

        // Only a name: Enum.TryParse would also take a number ("32") as whatever key has that value.
        return name.Length > 0 && char.IsLetter(name[0]) && Enum.TryParse<Key>(name, ignoreCase: true, out var key) && key != Key.None
            ? key
            : null;
    }

    private static string TextOf(KeyGesture gesture)
    {
        var parts = Modifiers.Where(m => gesture.KeyModifiers.HasFlag(m.Modifier)).Select(m => m.Name).ToList();
        parts.Add(Aliases.FirstOrDefault(a => a.Key == gesture.Key) is { Name: not null } alias ? alias.Name : gesture.Key.ToString());
        return string.Join("+", parts);
    }
}
