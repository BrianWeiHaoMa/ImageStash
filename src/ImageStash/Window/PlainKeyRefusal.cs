using Avalonia.Input;
using ImageStash.Seams;

namespace ImageStash.Window;

/// <summary>
/// G32's refusal of a key every app needs: a key pressed with none of Ctrl, Alt and the Windows key held, Shift alone
/// admitting none, unless that key is PrtScn, Pause, Scroll Lock or one of F13 to F24. It is judged by the keys alone,
/// never by what they type, so Delete, an arrow and F2 are refused though they type nothing. A Settings field refuses
/// such a candidate before the seam is asked, so it is never registered even for an instant; a shortcut an earlier build
/// or an imported export already stored as one is registered as any other (G31, G33): the rule is the field's alone.
/// </summary>
public static class PlainKeyRefusal
{
    private const KeyModifiers Admitting = KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Meta;

    /// <summary>Whether a field refuses <paramref name="candidate"/> as a key every app needs.</summary>
    public static bool Refuses(Shortcut candidate) =>
        (candidate.Gesture.KeyModifiers & Admitting) == KeyModifiers.None && !NeededByNoApp(candidate.Gesture.Key);

    // Named one by one rather than as a range of the enum, whose order is Avalonia's to keep.
    private static bool NeededByNoApp(Key key) => key is Key.PrintScreen or Key.Pause or Key.Scroll
        or Key.F13 or Key.F14 or Key.F15 or Key.F16 or Key.F17 or Key.F18 or Key.F19 or Key.F20 or Key.F21 or Key.F22 or Key.F23 or Key.F24;
}
