using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace ImageStash.Window;

internal static class MenuKeys
{
    // A key pressed in a popup can repeat in the window after the popup closes.
    public static void TrackWindowKeys(ContextMenu menu, PressOnce held, Action? firstEnter = null)
    {
        menu.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            if (e.Key is not (Key.Enter or Key.Escape) && (e.Key, e.KeyModifiers) is not (Key.Z or Key.V, KeyModifiers.Control))
            {
                return;
            }

            if (!held.PressedInMenu(e.Key))
            {
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                firstEnter?.Invoke();
            }
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        menu.AddHandler(InputElement.KeyUpEvent, (_, e) => held.Released(e.Key), RoutingStrategies.Tunnel, handledEventsToo: true);
    }
}
