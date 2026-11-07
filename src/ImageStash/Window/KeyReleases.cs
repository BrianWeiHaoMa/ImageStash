using Avalonia.Input;
using Avalonia.Interactivity;

namespace ImageStash.Window;

/// <summary>
/// A key's release acts in an ImageStash window only when that window had the key's press. A global shortcut's press is
/// taken by Windows as a hot key before any window sees it, while its release still goes to the window in front, where a
/// focused button or switch takes a Space released alone as a click: the open shortcut would press whatever the person
/// left focused instead of only summoning (F5, G1).
/// </summary>
public static class KeyReleases
{
    /// <summary>
    /// From now on <paramref name="window"/> ignores the release of a key whose press it did not receive since it was last
    /// active, but in a shortcut field, which reads such a release as a combination another program holds (G32).
    /// </summary>
    public static void IgnoreUnpressed(Avalonia.Controls.Window window)
    {
        var pressed = new HashSet<Key>();
        window.AddHandler(InputElement.KeyDownEvent, (_, e) => pressed.Add(e.Key), RoutingStrategies.Tunnel, handledEventsToo: true);
        window.AddHandler(InputElement.KeyUpEvent, (_, e) =>
        {
            if (!pressed.Remove(e.Key) && e.Source is not ShortcutField)
            {
                e.Handled = true;
            }
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        // A press whose release went to another window is forgotten, so a later release alone is not taken for it.
        window.Deactivated += (_, _) => pressed.Clear();
    }
}
