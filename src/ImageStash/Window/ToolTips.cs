using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Platform;
using ImageStash.Look;

namespace ImageStash.Window;

/// <summary>
/// Every tooltip's one placement, whole on a display of any scaling (E1). Avalonia 12.1.4 first lays a popup out at the
/// scaling of the place its window starts, and once it has placed a popup at the pointer it never sizes that popup's
/// window again, so a tip on a display of another scaling keeps a width too small for its word, whose last letters wrap
/// out of sight. A popup placed against its control is sized anew at every layout. Tips open beneath their control or
/// against the visible bounds of a drawn target. <see cref="ToolTipRule"/> decides attached tips and supplies the face.
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

    /// <summary>The caller supplies the visible rectangle within the control as the placement rectangle.</summary>
    internal static Popup ForDrawnTarget(Control control) => new()
    {
        PlacementTarget = control,
        Placement = PlacementMode.Custom,
        CustomPopupPlacementCallback = PlaceDrawnTarget,
        IsHitTestVisible = false,
        TakesFocusFromNativeControl = false,
        Child = ToolTipRule.ForDrawnTarget(),
    };

    internal static void ShowDrawnTarget(Popup popup, Rect visible, PixelPoint pointer, string? words)
    {
        var target = popup.PlacementTarget!;
        if (TopLevel.GetTopLevel(target) is not { } window)
        {
            return;
        }

        var screen = ScreenAt(window, pointer);
        popup.MaxWidth = screen is null ? double.PositiveInfinity : screen.WorkingArea.Width / screen.Scaling;
        popup.MaxHeight = screen is null ? double.PositiveInfinity : screen.WorkingArea.Height / screen.Scaling;
        // When a tile spans displays, use the part under the pointer to choose the popup's display.
        popup.PlacementRect = screen is null ? visible : VisiblePart(visible, ClientRect(target, screen.Bounds));
        ((ToolTip)popup.Child!).Content = words;
        popup.IsOpen = true;
    }

    private static void PlaceDrawnTarget(CustomPopupPlacement placement)
    {
        var window = TopLevel.GetTopLevel(placement.Target)!;
        var tile = placement.AnchorRectangle;
        var screen = ScreenAt(window, window.PointToScreen(tile.Center));
        if (screen is null)
        {
            return;
        }

        var area = ClientRect(window, screen.WorkingArea);
        // The anchor is in the owner's units, while Win32 sizes the popup at the destination display's scale.
        var position = DrawnTipPosition(tile, area, placement.PopupSize, screen.Scaling / window.RenderScaling);
        var offset = position - tile.TopLeft;
        placement.Anchor = PopupAnchor.TopLeft;
        placement.Gravity = PopupGravity.BottomRight;
        placement.ConstraintAdjustment = PopupPositionerConstraintAdjustment.None;
        placement.Offset = new Point(offset.X, offset.Y);
    }

    private static Screen? ScreenAt(TopLevel window, PixelPoint at)
    {
        var screens = window.Screens;
        return screens?.ScreenFromPoint(at) ?? screens?.ScreenFromVisual(window);
    }

    private static Rect ClientRect(Visual target, PixelRect rect) =>
        new(target.PointToClient(rect.TopLeft), target.PointToClient(rect.BottomRight));

    internal static Rect VisiblePart(Rect tile, Rect area) =>
        tile.Intersect(area) is { Width: > 0, Height: > 0 } visible ? visible : tile;

    /// <summary>Fits the popup beside the visible tile in the owner's units, allowing for the popup's own scale.</summary>
    internal static Point DrawnTipPosition(Rect tile, Rect area, Size content, double scaleRatio)
    {
        tile = VisiblePart(tile, area);
        var width = Math.Min(content.Width * scaleRatio, area.Width);
        var height = Math.Min(content.Height * scaleRatio, area.Height);
        var gap = 4 * scaleRatio;
        var below = tile.Bottom + gap;
        var above = tile.Top - gap - height;
        var y = below + height > area.Bottom && (above >= area.Top || tile.Top - area.Top > area.Bottom - tile.Bottom) ? above : below;
        return new Point(Math.Clamp(tile.Center.X - width / 2, area.Left, area.Left + (area.Width - width)),
            Math.Clamp(y, area.Top, area.Top + (area.Height - height)));
    }
}
