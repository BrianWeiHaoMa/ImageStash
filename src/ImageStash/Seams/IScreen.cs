using Avalonia;
using ImageStash.Media;

namespace ImageStash.Seams;

/// <summary>
/// The displays and their pixels, for the capture (G14, G15): where each display is, a grab of the desktop as it is
/// composed, and keeping one of ImageStash's own windows out of every grab.
/// </summary>
public interface IScreen
{
    /// <summary>Every display now, in physical pixels; never empty. From any thread.</summary>
    IReadOnlyList<Display> Displays();

    /// <summary>
    /// The desktop's pixels inside <paramref name="rectangle"/>, physical pixels lying on one display, as the desktop is
    /// composed when the call is made: a window hidden before the call is not in it, and a window marked through
    /// <see cref="ExcludeFromCapture"/> shows what is beneath it. Every alpha byte is 255; the frame is the caller's. A
    /// grab the platform refuses (E5) is a failure naming the display, never an exception (G35). From any thread.
    /// With <paramref name="withPointer"/>, the pointer as it shows at that moment is drawn in where it lies inside the
    /// rectangle (G15); a pointer the platform is not showing is none, and one it refuses to read refuses the grab.
    /// </summary>
    Outcome<MediaFrame> Grab(PixelRect rectangle, bool withPointer);

    /// <summary>
    /// Leaves <paramref name="window"/>, one of ImageStash's windows or a popup of one, out of every grab and of other
    /// programs' captures for as long as it exists. Called on the UI thread before the window is first shown, so not
    /// even its first frame is captured; a refusal is a failure naming the window's title, or for a popup the title of
    /// the window it belongs to, and the caller shows the window anyway (G35).
    /// </summary>
    Outcome ExcludeFromCapture(Avalonia.Controls.TopLevel window);
}

/// <summary>A display: its bounds and working area in physical pixels, its scaling, and whether it is the primary one (E1, S10).</summary>
public sealed record Display(PixelRect Bounds, PixelRect WorkingArea, double Scaling, bool IsPrimary);
