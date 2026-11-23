using Avalonia;
using Avalonia.Controls;

namespace ImageStash.Window;

/// <summary>
/// Whether the stash window is showing, for its notices' clock (G16): showing while it is visible and not minimised,
/// whichever way it hid (the open shortcut, Esc, focus lost, a paste, Close, a capture's start) or was minimised. A hide
/// and show within one showing, as unpinning makes, stops the clock only for that instant, so no notice loses its time.
/// </summary>
public sealed class WindowShowing(Notices notices)
{
    /// <summary>Whether a window <paramref name="visible"/> and <paramref name="minimised"/> or not is showing.</summary>
    public static bool Shows(bool visible, bool minimised) => visible && !minimised;

    /// <summary>The window is now <paramref name="visible"/> or not, and <paramref name="minimised"/> or not.</summary>
    public void Seen(bool visible, bool minimised) => notices.WindowShows(Shows(visible, minimised));

    /// <summary>Follows <paramref name="window"/> from now on, as it is now and at each change of its visibility or state.</summary>
    public void Follow(Avalonia.Controls.Window window)
    {
        Seen(window.IsVisible, window.WindowState == WindowState.Minimized);
        window.PropertyChanged += (_, e) =>
        {
            if (e.Property == Visual.IsVisibleProperty || e.Property == Avalonia.Controls.Window.WindowStateProperty)
            {
                Seen(window.IsVisible, window.WindowState == WindowState.Minimized);
            }
        };
    }
}
