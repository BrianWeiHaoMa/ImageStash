using ImageStash.Seams;

namespace ImageStash.Window;

/// <summary>
/// Whether a global shortcut would act if pressed now (G33): the one judgment every surface naming a shortcut asks, so
/// none offers a key that would do nothing. The tray's tooltip asks it for the open shortcut, the recording's bar for the
/// record shortcut.
/// </summary>
public static class ActingShortcut
{
    /// <summary>
    /// <paramref name="act"/>'s shortcut while a press of it would act: its setting a shortcut, Pause off, and the policy
    /// giving no reason it is unavailable (held by another program, typing a character); null otherwise. A shortcut held
    /// off only while a Settings field records a press counts as acting, as that moment is not chased.
    /// </summary>
    public static Shortcut? Of(GlobalShortcuts shortcuts, ShortcutAct act) =>
        !shortcuts.Paused && shortcuts.UnavailableReason(act) is null ? shortcuts.ShortcutOf(act) : null;
}
