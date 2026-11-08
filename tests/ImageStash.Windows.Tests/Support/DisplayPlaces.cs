using Avalonia;
using ImageStash.Seams;
using ImageStash.Windows.Seams;

namespace ImageStash.Windows.Tests.Support;

/// <summary>
/// Where the checks' windows stand on the primary display, in physical pixels, from its bounds, working area and
/// scaling: the stash window shown at the working area's top right, the patterns laid under it and around its + menu
/// and tooltip, and around a pinned window's + button; the stand-in tray icon in the display's middle, and the pattern and the counter laid under the
/// notification shown above it. Every length is its length on a display at 100 % times the display's scaling, rounded
/// once, so a scaled display sees the same geometry magnified (E1). On a 1280 × 800 display at 100 % these are the places
/// the checks have always used; on a larger one they move with the windows the product places from the display.
/// </summary>
public static class DisplayPlaces
{
    /// <summary>The primary display as the Screen seam reports it, read without a window.</summary>
    public static Display Primary() => new WindowsScreen().Displays().Single(display => display.IsPrimary);

    /// <summary><paramref name="length"/>, a length on a display at 100 %, on <paramref name="display"/>.</summary>
    public static int Scaled(Display display, int length) => (int)Math.Round(length * display.Scaling);

    /// <summary>The flyout's client, <paramref name="client"/> physical pixels, its top-right corner a margin in from the working area's.</summary>
    public static PixelRect Flyout(Display display, PixelSize client)
    {
        var area = display.WorkingArea;
        var margin = Scaled(display, 20);
        return new PixelRect(area.Right - client.Width - margin, area.Y + margin, client.Width, client.Height);
    }

    /// <summary>A pattern wholly under the flyout's client, clear of its edges.</summary>
    public static PixelRect UnderFlyout(Display display, PixelSize client)
    {
        var flyout = Flyout(display, client);
        return new PixelRect(flyout.X + Scaled(display, 20), flyout.Y + Scaled(display, 20), Scaled(display, 220), Scaled(display, 200));
    }

    /// <summary>
    /// Around the flyout's + button, its menu and its tooltip: from left of the flyout to the working area's right edge,
    /// not the flyout's, since the menu opens at the button and may reach past the flyout's own right edge.
    /// </summary>
    public static PixelRect AroundPlusMenu(Display display, PixelSize client)
    {
        var flyout = Flyout(display, client);
        var left = flyout.X - Scaled(display, 160);
        return new PixelRect(left, flyout.Y, display.WorkingArea.Right - left, Scaled(display, 440));
    }

    /// <summary>
    /// Around the + button and its tooltip of a pinned window the check placed itself, its client <paramref name="pinned"/>
    /// in physical pixels: the window's top, with room above it and beyond its right edge, scaled as the window is.
    /// </summary>
    public static PixelRect AroundPinnedPlusButton(Display display, PixelRect pinned) =>
        new(pinned.X - Scaled(display, 60), pinned.Y - Scaled(display, 80), pinned.Width + Scaled(display, 160), Scaled(display, 480));

    /// <summary>
    /// The stand-in tray icon: below the display's centre line, since a notification stands above its icon only while the
    /// icon's centre is at or below that line, else below it (<c>Summon.NotificationPlace</c>); and away from the bottom-right
    /// corner, where an unactivated Windows draws its watermark into every grab.
    /// </summary>
    public static PixelRect TrayIcon(Display display)
    {
        var centre = display.Bounds.Center;
        var side = Scaled(display, 24);
        return new PixelRect(centre.X + Scaled(display, 40), centre.Y + Scaled(display, 120), side, side);
    }

    /// <summary>A pattern under a notification shown above <see cref="TrayIcon"/>, wherever its tooltip shows.</summary>
    public static PixelRect UnderNotification(Display display)
    {
        var icon = TrayIcon(display);
        return new PixelRect(icon.X - Scaled(display, 280), icon.Y - Scaled(display, 200), Scaled(display, 560), Scaled(display, 320));
    }

    /// <summary>A counter whose field a notification shown above <see cref="TrayIcon"/> stands over.</summary>
    public static PixelRect Counter(Display display)
    {
        var icon = TrayIcon(display);
        return new PixelRect(icon.X - Scaled(display, 280), icon.Y - Scaled(display, 140), Scaled(display, 560), Scaled(display, 280));
    }
}
