using ImageStash.Seams;

namespace ImageStash.Window;

/// <summary>
/// The notification of a paste that could not be made (G8): "Copied · press Ctrl+V" first, then one sentence naming
/// the target as the footer names it and saying why, never that anything arrived (G10), and advising its <c>Ctrl+V</c>
/// only where a window is left to take it. It is raised above the tray with every other notification, unless the stash
/// window stood on another display when the handoff was asked, where it stands at that display's bottom-right
/// working-area corner in that display's own stack (<see cref="NotificationStacks"/>), so a later one on a third display
/// moves none already showing, and the tray's notifications stay where they are (G16).
/// </summary>
public sealed class PasteFallback(NotificationStacks stacks)
{
    /// <summary>G8's words, the notification's first line: what is on the clipboard, never that anything arrived (G10).</summary>
    public const string Copied = "Copied · press Ctrl+V";

    /// <summary>Why a paste asked while an edit is open was not made: the editor keeps the window (G2).</summary>
    public const string EditOpen = "an edit is open";

    /// <summary>Why a paste whose Enter was held past the paste's bound was not made: in front, the target would have taken its repeats as Send (N3).</summary>
    public const string EnterHeld = "Enter was still held, and would have reached it";

    /// <summary>
    /// Raises the notification for a paste into <paramref name="appName"/> not made for <paramref name="reason"/>, once
    /// the clipboard is written (technical G8): on <paramref name="stoodOn"/> when that is not the primary display, else
    /// above the tray.
    /// </summary>
    public void Raise(string reason, string appName, Display? stoodOn) => stacks.On(stoodOn).Raise(Says(reason, appName), heading: Copied);

    /// <summary>
    /// Names a paste's clipboard write that failed behind the hidden flyout, where the fallback would have stood, so the
    /// person sees it without summoning the window; never as "Copied", since nothing was (G8, G35).
    /// </summary>
    public void RaiseFailure(Failure failure, Display? stoodOn) => stacks.On(stoodOn).Raise(failure);

    /// <summary>
    /// Why, in a person's words, one sentence for each reason: rights that could not be read are never called higher, since
    /// the window may be another account's (E6), and a keystroke Windows did not take whole is never said to have pasted
    /// nothing, which ImageStash cannot know (G10).
    /// </summary>
    private static string Says(string reason, string app) => reason switch
    {
        PasteTargetReasons.Gone => $"ImageStash did not paste into {app} because its window is gone.",
        PasteTargetReasons.HigherRights => $"ImageStash cannot paste into {app} because it runs as administrator; press Ctrl+V there.",
        PasteTargetReasons.RightsUnreadable => $"ImageStash did not paste into {app} because Windows would not let it check that app's rights; press Ctrl+V there.",
        PasteTargetReasons.NotBroughtToFront => $"ImageStash did not paste into {app} because it did not come to the front in time; press Ctrl+V there.",
        PasteTargetReasons.KeystrokeRefused => $"Windows did not take the whole paste keystroke for {app}, so if the picture is not there, press Ctrl+V there.",
        PasteTargetReasons.KeyHeld => $"ImageStash did not paste into {app} because a key was still held down; let it go and press Ctrl+V there.",
        EnterHeld => $"ImageStash did not paste into {app} because Enter was still held, which it could have taken as Send; press Ctrl+V there.",
        EditOpen => $"ImageStash did not paste into {app} because an edit is open in ImageStash; press Ctrl+V there.",
        _ => $"ImageStash did not paste into {app} because {reason}; press Ctrl+V there.",
    };
}
