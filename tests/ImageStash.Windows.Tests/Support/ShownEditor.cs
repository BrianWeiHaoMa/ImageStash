using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using ImageStash.Editor;
using ImageStash.Window;

namespace ImageStash.Windows.Tests.Support;

/// <summary>
/// The editor inside a shown stash window, for the desktop checks: its controls found by the words a screen reader
/// reads, the picture's place on its canvas and in the rendered window, the pointer pressed, dragged and released on
/// the picture at fractions of where it shows, and the menus it opens. Every member runs on the UI thread.
/// </summary>
public sealed class ShownEditor : IDisposable
{
    private readonly ShownWindow shown;
    private readonly Avalonia.Input.Pointer mouse = new(Avalonia.Input.Pointer.GetNextFreeId(), PointerType.Mouse, true);
    private readonly IDisposable menus;
    private ContextMenu? openMenu;

    public ShownEditor(ShownWindow shown)
    {
        this.shown = shown;
        // The ⋯ menu and the size chip's are popups of their own, outside the window's tree: each is found as it opens on
        // a button of the editor's.
        menus = TrayDesktop.Get(() => MenuBase.OpenedEvent.AddClassHandler<ContextMenu>((menu, _) =>
        {
            if ((menu.PlacementTarget ?? (menu.Parent as Popup)?.PlacementTarget) is Visual on && shown.Window.GetVisualDescendants().OfType<EditorOverlay>().SingleOrDefault() is { } overlay
                && overlay.IsVisualAncestorOf(on))
            {
                openMenu = menu;
            }
        }, RoutingStrategies.Bubble | RoutingStrategies.Direct, handledEventsToo: true));
    }

    public EditorOverlay Overlay => shown.Window.GetVisualDescendants().OfType<EditorOverlay>().Single();

    public EditorCanvas Canvas => Overlay.Canvas;

    public EditorSession? Session => shown.Editing.Session;

    /// <summary>The overlay's button, a choice among them, read by these words.</summary>
    public Button Button(string words) => Overlay.GetVisualDescendants().OfType<Button>().Single(button => button.IsEffectivelyVisible && AutomationProperties.GetName(button) == words);

    /// <summary>Whether a button with these words shows.</summary>
    public bool Shows(string words) => Overlay.GetVisualDescendants().OfType<Button>().Any(button => button.IsEffectivelyVisible && AutomationProperties.GetName(button) == words);

    /// <summary>The words of every text the overlay shows, in order.</summary>
    public List<string> Texts() => Overlay.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible && !string.IsNullOrEmpty(text.Text)).Select(text => text.Text!).ToList();

    /// <summary>The tool row's words, in order.</summary>
    public List<string> Tools() => Overlay.GetVisualDescendants().OfType<ToggleButton>().Where(button => button.Tag is EditorTool).Select(button => AutomationProperties.GetName(button)!).ToList();

    /// <summary>The words on the output size chip.</summary>
    public string Size() => ((Panel)Button(EditorOverlay.OutputSize).Content!).Children.OfType<TextBlock>().First().Text ?? "";

    /// <summary>Where the picture shows on the canvas, in its units.</summary>
    public Rect Box => Canvas.Mapping()!.Box;

    /// <summary>The point of the canvas at these fractions of the shown picture's width and height.</summary>
    public Point At(double x, double y) => new(Box.X + Box.Width * x, Box.Y + Box.Height * y);

    /// <summary>Where the picture shows in the window as <see cref="RenderedWindow"/> renders it, in physical pixels.</summary>
    public Rect PictureInRendering()
    {
        var canvas = RenderedWindow.Place(shown.Window, Canvas);
        var scaling = shown.Window.RenderScaling;
        return new Rect(canvas.X + Box.X * scaling, canvas.Y + Box.Y * scaling, Box.Width * scaling, Box.Height * scaling);
    }

    /// <summary>The point of the canvas over the point (<paramref name="x"/>, <paramref name="y"/>) of the original's grid.</summary>
    public Point OnCanvas(double x, double y) => Canvas.Mapping()!.ToCanvas((float)x, (float)y);

    /// <summary>How many pixels of the original's grid <paramref name="units"/> of the canvas span, as the canvas maps a drag.</summary>
    public double OnOriginal(double units) => Canvas.Mapping()!.ToOriginal(units);

    /// <summary>
    /// How far from a square handle's centre, in the canvas's units, a sample lies beyond it: past the handle's half width
    /// and one more unit, since the canvas snaps the handle's corner, not its centre, to the pixel grid.
    /// </summary>
    public static double BeyondHandle => EditorCanvas.HandleSize / 2 + 1;

    /// <summary>Where a point of the canvas, in its units, shows in the window as <see cref="RenderedWindow"/> renders it, in physical pixels.</summary>
    public Point InRendering(Point onCanvas)
    {
        var canvas = RenderedWindow.Place(shown.Window, Canvas);
        var scaling = shown.Window.RenderScaling;
        return new Point(canvas.X + onCanvas.X * scaling, canvas.Y + onCanvas.Y * scaling);
    }

    /// <summary>
    /// Where a rectangle of the canvas, in its units, shows in the rendering, in physical pixels, its edges rounded inward,
    /// so it holds no pixel a boundary blends.
    /// </summary>
    public Rect InRendering(Rect onCanvas)
    {
        var (topLeft, bottomRight) = (InRendering(onCanvas.TopLeft), InRendering(onCanvas.BottomRight));
        var (left, top, right, bottom) = (Math.Ceiling(topLeft.X), Math.Ceiling(topLeft.Y), Math.Floor(bottomRight.X), Math.Floor(bottomRight.Y));
        return new Rect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }

    /// <summary>The rendering's pixels wholly inside the white of the square handle centred at <paramref name="centre"/> on the canvas: a unit either side of its centre.</summary>
    public Rect InsideHandle(Point centre) => InRendering(new Rect(centre.X - 1, centre.Y - 1, 2, 2));

    /// <summary>The rendering's pixels wholly inside the white of the round handle centred at <paramref name="centre"/>: two units either side of its centre.</summary>
    public Rect InsideRoundHandle(Point centre) => InRendering(new Rect(centre.X - 2, centre.Y - 2, 4, 4));

    /// <summary>A press of the left button on the canvas at <paramref name="at"/>, in its units: the <paramref name="clicks"/>th of a run of clicks there.</summary>
    public void Press(Point at, int clicks = 1, KeyModifiers modifiers = KeyModifiers.None) =>
        Canvas.RaiseEvent(new PointerPressedEventArgs(Canvas, mouse, shown.Window, InWindow(at), 0,
            new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed), modifiers, clicks));

    /// <summary>The pointer moved with the left button down to <paramref name="at"/> on the canvas.</summary>
    public void Move(Point at, KeyModifiers modifiers = KeyModifiers.None) =>
        Canvas.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent, Canvas, mouse, shown.Window, InWindow(at), 0,
            new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.Other), modifiers));

    /// <summary>The left button released on the canvas at <paramref name="at"/>.</summary>
    public void Release(Point at, KeyModifiers modifiers = KeyModifiers.None) =>
        Canvas.RaiseEvent(new PointerReleasedEventArgs(Canvas, mouse, shown.Window, InWindow(at), 0,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased), modifiers, MouseButton.Left));

    /// <summary>A press of the left button on <paramref name="on"/>, one of the overlay's controls, at <paramref name="at"/> in its units.</summary>
    public void Press(Control on, Point at) =>
        on.RaiseEvent(new PointerPressedEventArgs(on, mouse, shown.Window, on.TranslatePoint(at, shown.Window)!.Value, 0,
            new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed), KeyModifiers.None, 1));

    /// <summary>The pointer moved with the left button down to <paramref name="at"/> on <paramref name="on"/>, in its units.</summary>
    public void Move(Control on, Point at) =>
        on.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent, on, mouse, shown.Window, on.TranslatePoint(at, shown.Window)!.Value, 0,
            new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.Other), KeyModifiers.None));

    /// <summary>The left button released on <paramref name="on"/> at <paramref name="at"/>, in its units.</summary>
    public void Release(Control on, Point at) =>
        on.RaiseEvent(new PointerReleasedEventArgs(on, mouse, shown.Window, on.TranslatePoint(at, shown.Window) ?? at, 0,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased), KeyModifiers.None, MouseButton.Left));

    /// <summary>A drag on the picture between these fractions of where it shows, through ten steps.</summary>
    public void Drag((double X, double Y) from, (double X, double Y) to) => Drag(At(from.X, from.Y), At(to.X, to.Y));

    /// <summary>A drag on the canvas between these points of it, through ten steps, with <paramref name="modifiers"/> held.</summary>
    public void Drag(Point from, Point to, KeyModifiers modifiers = KeyModifiers.None)
    {
        Press(from, 1, modifiers);
        for (var step = 1; step <= 10; step++)
        {
            Move(new Point(from.X + (to.X - from.X) * step / 10, from.Y + (to.Y - from.Y) * step / 10), modifiers);
        }

        Release(to, modifiers);
    }

    /// <summary>A double-click on the canvas at <paramref name="at"/>: a click, then a press whose click count is two, and its release.</summary>
    public void DoubleClick(Point at)
    {
        Press(at);
        Release(at);
        Press(at, 2);
        Release(at);
    }

    /// <summary>The filmstrip under the picture of an animated item.</summary>
    public Filmstrip Filmstrip => Overlay.Filmstrip;

    /// <summary>The middle of frame <paramref name="frame"/>'s cell on the filmstrip, in its units.</summary>
    public Point Cell(int frame) => new((Filmstrip.CellLeft(frame) + Filmstrip.CellRight(frame)) / 2, Filmstrip.LaneHeight + Filmstrip.CellsHeight / 2);

    /// <summary>Where the filmstrip lies in the window as <see cref="RenderedWindow"/> renders it, in physical pixels.</summary>
    public Rect FilmstripInRendering() => RenderedWindow.Place(shown.Window, Filmstrip);

    /// <summary>A press of the left button on frame <paramref name="frame"/>'s cell of the filmstrip.</summary>
    public void PressCell(int frame) =>
        Filmstrip.RaiseEvent(new PointerPressedEventArgs(Filmstrip, mouse, shown.Window, OnStrip(Cell(frame)), 0,
            new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed), KeyModifiers.None, 1));

    /// <summary>The pointer moved with the left button down onto frame <paramref name="frame"/>'s cell.</summary>
    public void MoveToCell(int frame) =>
        Filmstrip.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent, Filmstrip, mouse, shown.Window, OnStrip(Cell(frame)), 0,
            new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.Other), KeyModifiers.None));

    /// <summary>The left button released over frame <paramref name="frame"/>'s cell.</summary>
    public void ReleaseOnCell(int frame) =>
        Filmstrip.RaiseEvent(new PointerReleasedEventArgs(Filmstrip, mouse, shown.Window, OnStrip(Cell(frame)), 0,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased), KeyModifiers.None, MouseButton.Left));

    /// <summary>A click on frame <paramref name="frame"/>'s cell of the filmstrip.</summary>
    public void ClickCell(int frame)
    {
        PressCell(frame);
        ReleaseOnCell(frame);
    }

    /// <summary>
    /// The point of the filmstrip on the cell edge that begins frame <paramref name="edge"/> (the strip's end after the
    /// last frame), where the trim's handles stand: at the cells' middle, or in the lane above them, where a layer's bar is.
    /// </summary>
    public Point Edge(int edge, bool inLane = false) =>
        new(edge < Filmstrip.CellCount ? Filmstrip.CellLeft(edge) : Filmstrip.CellRight(edge - 1), inLane ? Filmstrip.LaneHeight / 2 : Filmstrip.LaneHeight + Filmstrip.CellsHeight / 2);

    /// <summary>A drag on the filmstrip between these points of it, through ten steps: a handle's or a bar end's, when one is there.</summary>
    public void DragOnStrip(Point from, Point to)
    {
        Filmstrip.RaiseEvent(new PointerPressedEventArgs(Filmstrip, mouse, shown.Window, OnStrip(from), 0,
            new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed), KeyModifiers.None, 1));
        for (var step = 1; step <= 10; step++)
        {
            Filmstrip.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent, Filmstrip, mouse, shown.Window, OnStrip(new Point(from.X + (to.X - from.X) * step / 10, from.Y + (to.Y - from.Y) * step / 10)), 0,
                new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.Other), KeyModifiers.None));
        }

        Filmstrip.RaiseEvent(new PointerReleasedEventArgs(Filmstrip, mouse, shown.Window, OnStrip(to), 0,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased), KeyModifiers.None, MouseButton.Left));
    }

    /// <summary>The field a text is typed in, while it shows.</summary>
    public TextBox? Field => Overlay.GetVisualDescendants().OfType<TextBox>().SingleOrDefault(box => box.IsEffectivelyVisible && AutomationProperties.GetName(box) == "Text");

    /// <summary>A click on the picture at these fractions of where it shows.</summary>
    public void Click(double x, double y)
    {
        Press(At(x, y));
        Release(At(x, y));
    }

    /// <summary>The left button pressed on the overlay's button with these words, and left down.</summary>
    public void PressButton(string words)
    {
        var button = Button(words);
        button.RaiseEvent(new PointerPressedEventArgs(button, mouse, shown.Window, Centre(button), 0,
            new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed), KeyModifiers.None, 1));
    }

    /// <summary>The left button released on the overlay's button with these words.</summary>
    public void ReleaseButton(string words)
    {
        var button = Button(words);
        button.RaiseEvent(new PointerReleasedEventArgs(button, mouse, shown.Window, Centre(button), 0,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased), KeyModifiers.None, MouseButton.Left));
    }

    /// <summary>A click on the overlay's button with these words, its Click raised as a press and release would.</summary>
    public void Choose(string words) => ShownWindow.RaiseClick(Button(words));

    /// <summary>A click on the ⋯ button; the open menu's entries as they show, a separator as "—".</summary>
    public List<string> ClickMore() => ClickMenuButton("More options");

    /// <summary>The menu open on one of the editor's buttons, or null.</summary>
    public ContextMenu? OpenMenu => openMenu;

    /// <summary>A click on the editor's button with these words, which opens a menu; its entries as they show, a separator as "—".</summary>
    public List<string> ClickMenuButton(string words)
    {
        openMenu?.Close();
        openMenu = null;
        ShownWindow.RaiseClick(Button(words));
        Assert.IsNotNull(openMenu, "a menu opened");
        return openMenu.Items.Select(entry => entry is Separator ? "—" : (string)((MenuItem)entry!).Header!).ToList();
    }

    /// <summary>Clicks the open ⋯ menu's entry with these words.</summary>
    public void ClickMenuEntry(string words)
    {
        var entry = openMenu!.Items.OfType<MenuItem>().Single(item => (string)item.Header! == words);
        entry.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        openMenu.Close();
        openMenu = null;
    }

    /// <summary>Chooses Full, Three-quarter or Half size behind the size chip.</summary>
    public void ChooseShrink(string words)
    {
        ClickMenuButton(EditorOverlay.OutputSize);
        ClickMenuEntry(words);
    }

    public void Dispose()
    {
        TrayDesktop.Run(() => openMenu?.Close());
        menus.Dispose();
    }

    private Point InWindow(Point onCanvas) => Canvas.TranslatePoint(onCanvas, shown.Window)!.Value;

    private Point OnStrip(Point onFilmstrip) => Filmstrip.TranslatePoint(onFilmstrip, shown.Window)!.Value;

    private Point Centre(Control control) => control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), shown.Window)!.Value;
}
