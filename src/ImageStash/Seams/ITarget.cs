using Avalonia;

namespace ImageStash.Seams;

/// <summary>
/// The paste target (G8, F13): the window the handoff pastes into, read under G8's rule from what the platform says is
/// in front; whether ImageStash may still send it input; its activation and the one paste keystroke, and nothing else
/// sent to any window (N3, R6); and the caret of its focused control, beside which the shortcut's flyout opens (G1).
/// Which windows are ImageStash's own, the desktop or the taskbar is this seam's to judge.
/// </summary>
public interface ITarget
{
    /// <summary>
    /// The window in front right now under G8's rule: ImageStash's own (a notification included), or another app's
    /// window, or none for the desktop and the taskbar; the shell's task switcher, which G8 passes over, reads as the
    /// window in front before it. Read on the thread that asks, at once, so the open shortcut's press reads the window
    /// that was in front at the press.
    /// </summary>
    ForegroundReading Foreground();

    /// <summary>
    /// The window that has focus before ImageStash's own: while another app is in front, that app's window; once a
    /// window of ImageStash's is, the one that had focus just before it, passing over the shell's task switcher (G8).
    /// Null when that window was ImageStash's own (a notification or a dialog included), the desktop or the taskbar, or
    /// when nothing has been reported. Read from any thread.
    /// </summary>
    PasteTarget? LastBeforeMine();

    /// <summary>
    /// Raised, on a thread of the seam's own, when the platform reports a change of the window in front that happened
    /// before <see cref="LastBeforeMine"/> last answered but reached the seam only after it, so that answer may have
    /// missed it and asking again may answer differently (G8). A platform whose answers never miss a change raises
    /// nothing, which is what this declaration does unless the platform's seam replaces it.
    /// </summary>
    event Action? ReportedLate
    {
        add { }
        remove { }
    }

    /// <summary>
    /// Done when <paramref name="target"/> still names a window and ImageStash may send it input: its rights are not
    /// above ImageStash's, rights that cannot be read counting as above (S6, E6); otherwise a failure naming
    /// <see cref="PasteTargetReasons.Action"/>, the app and why. Touches no window.
    /// </summary>
    Outcome Confirm(PasteTarget target);

    /// <summary>
    /// Brings <paramref name="target"/> to the front, waits up to <paramref name="bound"/> until it is the window in
    /// front, then sends it one paste keystroke and nothing else (N3, R6); a key the user still holds that would make
    /// another chord of that keystroke is waited for, with the target in front, within the same bound. When the target
    /// does not come to the front, or the key is not let go, in time, nothing is sent and the failure says so. The
    /// waiting happens off the calling thread; the task completes once the keystroke has been sent or the failure is
    /// known.
    /// </summary>
    Task<Outcome> ActivateAndPaste(PasteTarget target, TimeSpan bound);

    /// <summary>
    /// The text caret of the focused control in <paramref name="target"/>'s window, on the screen in physical pixels,
    /// by G1's chain in its order; null when no step finds one, or when the lookup has not answered within
    /// <paramref name="bound"/>, whose late answer is then dropped. Never runs on the calling thread.
    /// </summary>
    Task<PixelRect?> CaretOf(PasteTarget target, TimeSpan bound);
}

/// <summary>
/// A paste target as it crosses the seam: the platform's handle of the window, opaque to everything but the seam, and
/// the app's name as the footer and the menu show it (What crosses every boundary).
/// </summary>
public sealed record PasteTarget(nint Handle, string AppName);

/// <summary>What is in front at one instant under G8's rule: ImageStash's own window, or else the target, null for the desktop and the taskbar.</summary>
public readonly record struct ForegroundReading(bool IsMine, PasteTarget? Target);

/// <summary>The words of a paste the seam could not make, as the outcome names it (G35); the user sees G8's notification instead.</summary>
public static class PasteTargetReasons
{
    public const string Action = "Paste";
    public const string Gone = "the window is gone";
    public const string HigherRights = "it runs with higher rights than ImageStash";
    public const string RightsUnreadable = "its rights could not be read";
    public const string NotBroughtToFront = "it could not be brought to the front";
    public const string KeystrokeRefused = "the keystroke was refused";
    public const string KeyHeld = "a key was still held, which would have changed the paste";
}
