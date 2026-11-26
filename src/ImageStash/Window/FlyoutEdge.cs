using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using ImageStash.Look;

namespace ImageStash.Window;

/// <summary>
/// The flyout's edge (F10, L2, G2): a hairline all the way round in the look's own colour, its corners rounded by the
/// radius Windows 11 rounds the window itself by, and along it the grips a drag of the border resizes the flyout from.
/// It lies over the content, so it takes no room from it: the client keeps every pixel of the window's minimum, at
/// which the editor fits whole (G20). The flyout has no frame of its own (`WindowChrome`), so the window Windows reports
/// is the flyout a person sees, and a place the summon computes is where its edge shows (G1). The pinned window has the
/// system's frame instead, and none of this shows there.
/// </summary>
public sealed class FlyoutEdge
{
    /// <summary>The name the edge's layer goes by among the window's controls.</summary>
    public const string LayerName = "FlyoutEdge";

    /// <summary>The strip along each side a press resizes the flyout from, in device-independent units, kept narrow since it lies over the content.</summary>
    public const double Grip = 4;

    /// <summary>The square at each corner a press resizes the flyout from both ways, inside the head's and the footer's margins.</summary>
    public const double CornerGrip = 8;

    /// <param name="resize">Starts a drag of the border at that edge for the press, as <c>Window.BeginResizeDrag</c> does.</param>
    public FlyoutEdge(Action<WindowEdge, PointerPressedEventArgs> resize)
    {
        Line = new Border { BorderThickness = new Thickness(1), IsHitTestVisible = false };
        Line.Bind(Border.BorderBrushProperty, Line.GetResourceObservable(LookKeys.WindowEdge));
        Line.Bind(Border.CornerRadiusProperty, Line.GetResourceObservable(LookKeys.WindowRadius));
        // No ground of its own, so only the grips take presses and everything else reaches the content beneath.
        Layer = new Panel { Name = LayerName };
        Layer.Children.Add(Line);
        // The corners after the sides, so a press where they meet takes both ways.
        foreach (var edge in new[] { WindowEdge.West, WindowEdge.East, WindowEdge.North, WindowEdge.South, WindowEdge.NorthWest, WindowEdge.NorthEast, WindowEdge.SouthWest, WindowEdge.SouthEast })
        {
            Layer.Children.Add(GripAt(edge, resize));
        }
    }

    /// <summary>The layer laid over the whole window, above every other part of it.</summary>
    public Panel Layer { get; }

    /// <summary>The hairline.</summary>
    public Border Line { get; }

    /// <summary>The grips, one per side and corner, each answering the edge it resizes.</summary>
    public IEnumerable<(WindowEdge Edge, Border Grip)> Grips => Layer.Children.OfType<Border>().Where(b => b.Tag is WindowEdge).Select(b => ((WindowEdge)b.Tag!, b));

    /// <summary>Whether the edge and its grips are on the window: on the flyout, off the pinned window (G2).</summary>
    public bool Shown
    {
        get => Layer.IsVisible;
        set => Layer.IsVisible = value;
    }

    private static Border GripAt(WindowEdge edge, Action<WindowEdge, PointerPressedEventArgs> resize)
    {
        var (horizontal, width, cursor) = edge switch
        {
            WindowEdge.West => (HorizontalAlignment.Left, Grip, StandardCursorType.LeftSide),
            WindowEdge.East => (HorizontalAlignment.Right, Grip, StandardCursorType.RightSide),
            WindowEdge.North => (HorizontalAlignment.Stretch, double.NaN, StandardCursorType.TopSide),
            WindowEdge.South => (HorizontalAlignment.Stretch, double.NaN, StandardCursorType.BottomSide),
            WindowEdge.NorthWest => (HorizontalAlignment.Left, CornerGrip, StandardCursorType.TopLeftCorner),
            WindowEdge.NorthEast => (HorizontalAlignment.Right, CornerGrip, StandardCursorType.TopRightCorner),
            WindowEdge.SouthWest => (HorizontalAlignment.Left, CornerGrip, StandardCursorType.BottomLeftCorner),
            _ => (HorizontalAlignment.Right, CornerGrip, StandardCursorType.BottomRightCorner),
        };
        var (vertical, height) = edge switch
        {
            WindowEdge.West or WindowEdge.East => (VerticalAlignment.Stretch, double.NaN),
            WindowEdge.North => (VerticalAlignment.Top, Grip),
            WindowEdge.South => (VerticalAlignment.Bottom, Grip),
            WindowEdge.NorthWest or WindowEdge.NorthEast => (VerticalAlignment.Top, CornerGrip),
            _ => (VerticalAlignment.Bottom, CornerGrip),
        };
        var grip = new Border
        {
            Tag = edge,
            Width = width,
            Height = height,
            HorizontalAlignment = horizontal,
            VerticalAlignment = vertical,
            // Clear, yet a ground, so the grip is hit where nothing is drawn.
            Background = Avalonia.Media.Brushes.Transparent,
        };
        // Made once the grip is on a window, whose platform makes cursors.
        grip.AttachedToVisualTree += (_, _) => grip.Cursor ??= new Cursor(cursor);
        grip.PointerPressed += (_, e) =>
        {
            // The grip and everything above it take no focus, so the press leaves focus where it was (G2) and the
            // drag that follows is no leaving: the window is neither deactivated nor hidden.
            if (e.GetCurrentPoint(grip).Properties.IsLeftButtonPressed)
            {
                e.Handled = true;
                resize(edge, e);
            }
        };
        return grip;
    }
}
