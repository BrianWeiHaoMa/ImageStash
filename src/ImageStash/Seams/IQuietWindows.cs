using Avalonia;

namespace ImageStash.Seams;

/// <summary>
/// ImageStash's own small windows: shown without taking focus, shown only in parts, with rounded corners, and where they
/// go, above the tray icon or at the pointer (G1, G15, G16, F13).
/// </summary>
public interface IQuietWindows
{
    /// <summary>
    /// Shows <paramref name="window"/>, hidden until now, without activating it and so that a click on it does not
    /// activate it either: the app in front keeps the keystrokes (G16). Called on the UI thread.
    /// </summary>
    void ShowWithoutActivating(Avalonia.Controls.Window window);

    /// <summary>
    /// Leaves <paramref name="window"/>, shown through <see cref="ShowWithoutActivating"/>, on the screen and under the
    /// pointer only within <paramref name="parts"/>, rectangles in physical pixels from the window's own top-left:
    /// elsewhere it is absent, so what lies beneath shows and takes the clicks (G15: the recording's frame around a region
    /// that stays live and clickable). Called on the UI thread right after the showing, the window not resized
    /// afterwards. A refusal is a failure naming the window's title; the window is then still shown whole (G35).
    /// </summary>
    Outcome ShowOnly(Avalonia.Controls.Window window, IReadOnlyList<PixelRect> parts);

    /// <summary>
    /// Makes <paramref name="window"/>, shown through <see cref="ShowWithoutActivating"/>, the window in front with the
    /// keystrokes, for the user's own click into a field of it (G16: "a click into the name field activates the
    /// window, which is the user's act"); the activation stays there while the hold lasts. A further take of the same
    /// window while a hold is on keeps the window that was in front before the first; when the window's last hold is
    /// disposed, the activation goes back to that window, unless the user has put another window in front meanwhile,
    /// which then keeps it. A failure when the platform did not bring the window to the front, which then holds
    /// nothing. Called on the UI thread.
    /// </summary>
    Outcome<IDisposable> TakeActivation(Avalonia.Controls.Window window);

    /// <summary>
    /// Asks that <paramref name="window"/>, an undecorated window of ImageStash's own, have its corners rounded as the
    /// platform rounds its own windows, the radius the platform's (the recording's bar and the notifications). Only a
    /// preference: a platform or a version that rounds nothing leaves the window square, and that is no failure. Called
    /// on the UI thread before the window is first shown.
    /// </summary>
    void RoundCorners(Avalonia.Controls.Window window);

    /// <summary>
    /// The tray icon's rectangle on the desktop in physical pixels, read afresh at every call, since the taskbar can
    /// move between calls; null when the platform cannot say where the icon is.
    /// </summary>
    PixelRect? TrayIconRectangle();

    /// <summary>The mouse pointer's position on the desktop in physical pixels, from any thread; null when the platform cannot say.</summary>
    PixelPoint? PointerPosition();

    /// <summary>
    /// The rectangle of the window in front on the desktop in physical pixels, from any thread, whose display the
    /// shortcut's flyout is centred on when neither a caret nor the pointer can be found (G1); null when no window is in
    /// front or the platform cannot say.
    /// </summary>
    PixelRect? FrontWindowRectangle();
}
