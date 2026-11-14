namespace ImageStash.Seams;

/// <summary>Which window stands just in front of a window of ImageStash's, and putting the window back behind it (G3).</summary>
public interface IWindowOrder
{
    /// <summary>
    /// The nearest window standing in front of <paramref name="window"/>, a shown window of ImageStash's that is not
    /// topmost, passing over the windows it owns and those not on the screen: shown, not minimised and not topmost
    /// either, another app's or another of ImageStash's; its handle, opaque to everything but the seam. Null when
    /// nothing but topmost windows stands in front of it, or the platform cannot say. Called on the UI thread.
    /// </summary>
    nint? InFrontOf(Avalonia.Controls.Window window);

    /// <summary>
    /// Puts <paramref name="window"/>, shown again, directly behind <paramref name="inFront"/>, an answer of
    /// <see cref="InFrontOf"/>, activating nothing, so that window stands in front of it again; when that window is gone,
    /// hidden, minimised or topmost by now, behind the window in front now instead, unless that is the desktop, the
    /// taskbar, topmost, or the window itself or one it owns, when it is left where it is. Called on the UI thread.
    /// </summary>
    void PutBack(Avalonia.Controls.Window window, nint inFront);
}
