using Avalonia;
using Avalonia.Controls;

namespace ImageStash.Window;

/// <summary>
/// Every tooltip's one placement, whole on a display of any scaling (E1). Avalonia 12.1.4 first lays a popup out at the
/// scaling of the place its window starts, and once it has placed a popup at the pointer it never sizes that popup's
/// window again, so a tip on a display of another scaling keeps a width too small for its word, whose last letters wrap
/// out of sight. A popup placed against its control is sized anew at every layout, so tips open beneath their control.
/// Whether a control carries a tip, and its words, are <see cref="Look.ToolTipRule"/>'s, the only setter of a tip.
/// </summary>
public static class ToolTips
{
    static ToolTips()
    {
        // At the setting of a tip, since Avalonia lets no default be overridden on Control, which declares these.
        ToolTip.TipProperty.Changed.AddClassHandler<Control>((control, _) =>
        {
            ToolTip.SetPlacement(control, PlacementMode.Bottom);
            // Beneath the control itself, where the default's offset is meant to clear the pointer.
            ToolTip.SetVerticalOffset(control, 4);
        });
    }

    /// <summary>Puts the placement in force for the process, for every tip set from now on; only the first call does anything.</summary>
    public static void PlaceBeneathTheirControls()
    {
    }
}
