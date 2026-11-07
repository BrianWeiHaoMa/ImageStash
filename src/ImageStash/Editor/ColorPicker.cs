using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using ImageStash.Layers;
using Thickness = Avalonia.Thickness;
using ImageStash.Look;

namespace ImageStash.Editor;

/// <summary>
/// The colour picker a More swatch or the outline swatch opens in the editor's pane, inside the window (G21, F4):
/// a square of saturation and value for the hue the strip beneath it chooses, the hex, Pick from the picture, and
/// the colours used recently; opened from the outline swatch, Auto (black on a light fill, white on a dark one) and
/// None above the square. While the square or the strip is dragged, what the picker was opened for shows each colour
/// the pointer passes, as no step, so the choice is made by looking at the picture; letting go applies the last one, as
/// one step and one recent colour (G21, G25), and a drag that ends any other way (its pointer lost, the picker closed)
/// puts back what was there. A colour is applied at once when Enter is pressed in the hex, when a recent colour is
/// clicked, or when the eyedropper's click samples the picture. The hex is a field with no paste of its own: ImageStash
/// reads the clipboard only for its three actions (N9, R5).
/// </summary>
internal sealed class ColorPicker : StackPanel
{
    public const string PickFromThePicture = "Pick from the picture";

    public const string HexName = "Hex";

    /// <summary>What a screen reader calls it.</summary>
    public const string Called = "Color picker";

    public const string Auto = "Auto", None = "None";

    private readonly Func<Rgba> current;
    private readonly Action<Rgba> chosen;
    private readonly ColorDrag drag;
    private readonly ColorChoices colors;
    private readonly ValueSquare square;
    private readonly HueStrip strip;
    private readonly TextBox hex;
    private readonly WrapPanel recent;
    private readonly List<(Outline Outline, ToggleButton Button)> outlines = [];
    private readonly Func<Outline?>? outlineShown;
    private Hsv shown;

    /// <param name="current">The colour the picker opens on.</param>
    /// <param name="chosen">Told of each colour applied at once: the hex's, a recent one's, the eyedropper's.</param>
    /// <param name="drag">Told of a drag on the square or the strip: each colour it passes, its let-go, its end without one.</param>
    /// <param name="outline">For the outline swatch: the outline shown, and what is told of Auto or None, which the picker then offers (G21).</param>
    public ColorPicker(ColorChoices colors, EditorCanvas canvas, Func<Rgba> current, Action<Rgba> chosen, ColorDrag drag, (Func<Outline?> Shown, Action<Outline> Chosen)? outline = null)
    {
        this.current = current;
        this.chosen = chosen;
        this.drag = drag;
        this.colors = colors;
        outlineShown = outline?.Shown;
        Spacing = 10;
        Width = EditorPane.ContentWidth;
        AutomationProperties.SetName(this, Called);
        square = new ValueSquare { Height = 150 };
        square.Moved += (saturation, value) => Preview(shown with { Saturation = saturation, Value = value });
        square.Pressed += drag.Begin;
        square.LetGo += LetGo;
        square.Abandoned += Abandon;
        strip = new HueStrip { Height = 14 };
        strip.Moved += hue => Preview(shown with { Hue = hue });
        strip.Pressed += drag.Begin;
        strip.LetGo += LetGo;
        strip.Abandoned += Abandon;
        // One surface held at a time, so a second pointer never drives the drag the first one began or ended (G25).
        square.Other = strip;
        strip.Other = square;

        hex = new TextBox { Width = 110, VerticalContentAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(hex, HexName);
        // Its own paste would read the clipboard's text (N9, R5); its menu would offer a Paste that does nothing.
        hex.PastingFromClipboard += (_, e) => e.Handled = true;
        hex.ContextFlyout = null;
        hex.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None)
            {
                if (ColorChoices.ParseHex(hex.Text ?? "") is { } typed)
                {
                    Apply(typed);
                }

                e.Handled = true;
            }
        }, RoutingStrategies.Tunnel);
        var pick = EditorButtons.Labelled("", PickFromThePicture);
        pick.Click += (_, _) =>
        {
            Done?.Invoke();
            canvas.PickColor(chosen);
            canvas.Focus();
        };
        // The hex, Pick from the picture and Recent each take a line of the card's width, and the recent colours wrap,
        // so nothing offered is cut or runs past the card (G20, G21).
        hex.HorizontalAlignment = HorizontalAlignment.Left;
        pick.HorizontalAlignment = HorizontalAlignment.Stretch;
        pick.HorizontalContentAlignment = HorizontalAlignment.Left;

        recent = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 2, LineSpacing = 2 };
        var recentRow = new StackPanel { Spacing = 6, Children = { new TextBlock { Text = "Recent", Opacity = 0.7 }, recent } };
        if (outline is { } offered)
        {
            outlines.Add((Outline.Auto, OutlineChoice(Auto, "A", "Black on light, white on dark", () => offered.Chosen(Outline.Auto))));
            outlines.Add((Outline.None, OutlineChoice(None, "", null, () => offered.Chosen(Outline.None))));
            Children.Add(new StackPanel { Spacing = 2, Children = { outlines[0].Button, outlines[1].Button } });
        }

        Children.Add(square);
        Children.Add(strip);
        Children.Add(hex);
        Children.Add(pick);
        Children.Add(recentRow);
    }

    /// <summary>Raised when the picker has done its part and closes: Pick from the picture was chosen.</summary>
    public event Action? Done;

    /// <summary>The recent colours as their swatches show them, newest first.</summary>
    public IEnumerable<Button> RecentSwatches => recent.Children.OfType<Button>();

    /// <summary>Shows the colour it was opened for as it is now, Auto or None checked when either is, and the colours used recently, as it opens.</summary>
    public void Reset()
    {
        Show(ColorChoices.ToHsv(current()));
        ShowRecent();
        ShowOutline();
    }

    /// <summary>Applies <paramref name="color"/> at once and shows it as the colour's own.</summary>
    private void Apply(Rgba color)
    {
        chosen(color);
        Show(ColorChoices.ToHsv(color));
        ShowRecent();
        ShowOutline();
    }

    /// <summary>
    /// The square or the strip moved to <paramref name="hsv"/> while pressed: shown in the picker, and on what it was opened
    /// for, as no step; once something else has ended the drag (Undo, a colour chosen at once), the picker shows what is there.
    /// </summary>
    private void Preview(Hsv hsv)
    {
        Show(hsv);
        if (!drag.Preview(ColorChoices.FromHsv(hsv)))
        {
            Reset();
        }
    }

    /// <summary>
    /// The square or the strip let go: the colour shown applied, and kept as the square or the strip chose it, whose hue a
    /// white, black or grey does not carry and the next move of the square keeps (G21); after a drag something else
    /// ended meanwhile, nothing is applied and the picker shows what is there.
    /// </summary>
    private void LetGo()
    {
        if (!drag.LetGo(ColorChoices.FromHsv(shown)))
        {
            Reset();
            return;
        }

        ShowRecent();
        ShowOutline();
    }

    /// <summary>A drag that ends without its let-go: what it showed put back, and the picker showing what is there.</summary>
    private void Abandon()
    {
        drag.Abandon();
        Reset();
    }

    /// <summary>A row of the outline's own choices: a round mark, its name, and beneath the name faint words saying what it does.</summary>
    private ToggleButton OutlineChoice(string name, string mark, string? words, Action picked)
    {
        var row = new DockPanel();
        var said = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { new TextBlock { Text = name } } };
        var round = new Border
        {
            Width = 22,
            Height = 22,
            CornerRadius = new CornerRadius(11),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 10, 0),
            Child = mark.Length == 0 ? OutlineSwatch.Strike(20) : new TextBlock { Text = mark, FontSize = 11, FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        round.Bind(Border.BorderBrushProperty, round.GetResourceObservable(LookKeys.SwatchEdge));
        DockPanel.SetDock(round, Dock.Left);
        row.Children.Add(round);
        if (words is not null)
        {
            // Beneath the name, not beside it, where it would leave the name no room (G21).
            said.Children.Add(new TextBlock { Text = words, Opacity = 0.6, FontSize = 12, TextWrapping = TextWrapping.Wrap });
        }

        row.Children.Add(said);
        var choice = EditorButtons.Choice(row, name, () =>
        {
            picked();
            ShowOutline();
        });
        choice.HorizontalAlignment = HorizontalAlignment.Stretch;
        choice.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        choice.Height = double.NaN;
        choice.MinHeight = 36;
        choice.Padding = new Thickness(10, 4);
        return choice;
    }

    private void ShowOutline()
    {
        if (outlineShown is not null)
        {
            EditorButtons.Check(outlines, outlineShown()!);
        }
    }

    private void Show(Hsv hsv)
    {
        shown = hsv;
        square.Show(hsv);
        strip.Show(hsv.Hue);
        hex.Text = ColorChoices.Hex(ColorChoices.FromHsv(hsv));
    }

    private void ShowRecent()
    {
        recent.Children.Clear();
        foreach (var color in colors.Recent)
        {
            recent.Children.Add(EditorButtons.Swatch(color, () => Apply(color)));
        }
    }

    /// <summary>A surface the pointer is pressed on and dragged over, telling where it is as it moves and when it lets go.</summary>
    private abstract class PickSurface : Control
    {
        private IPointer? holder;

        protected PickSurface() => Look = new DrawnLook(this);

        protected DrawnLook Look { get; }

        /// <summary>The picker's other surface: a press here waits until it is let go.</summary>
        public PickSurface? Other { get; set; }

        /// <summary>Whether a pointer holds it pressed: only that pointer's moves and let-go count.</summary>
        public bool IsHeld => holder is not null;

        /// <summary>Raised as a press begins, before its first move.</summary>
        public event Action? Pressed;

        public event Action? LetGo;

        /// <summary>Raised when a press ends without its let-go: its pointer lost, or the surface gone from the window.</summary>
        public event Action? Abandoned;

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            e.Handled = true;
            if (IsHeld || Other is { IsHeld: true })
            {
                return;
            }

            holder = e.Pointer;
            e.Pointer.Capture(this);
            Pressed?.Invoke();
            Move(e.GetPosition(this));
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            if (ReferenceEquals(e.Pointer, holder))
            {
                Move(e.GetPosition(this));
            }
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            if (ReferenceEquals(e.Pointer, holder))
            {
                holder = null;
                e.Pointer.Capture(null);
                LetGo?.Invoke();
            }
        }

        protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
        {
            base.OnPointerCaptureLost(e);
            if (ReferenceEquals(e.Pointer, holder))
            {
                EndUnreleased();
            }
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
            EndUnreleased();
        }

        protected abstract void Move(Point point);

        private void EndUnreleased()
        {
            if (IsHeld)
            {
                holder = null;
                Abandoned?.Invoke();
            }
        }
    }

    /// <summary>The square: saturation from left to right, value from bottom to top, for one hue; a ring where the colour is.</summary>
    private sealed class ValueSquare : PickSurface
    {
        private Hsv hsv;

        public ValueSquare() => Cursor = new Cursor(StandardCursorType.Cross);

        public event Action<double, double>? Moved;

        public void Show(Hsv shown)
        {
            hsv = shown;
            InvalidateVisual();
        }

        public override void Render(DrawingContext context)
        {
            var area = new Rect(Bounds.Size);
            var pure = ColorChoices.FromHsv(new Hsv(hsv.Hue, 1, 1));
            var corner = (float)Look.Radius(LookKeys.SmallRadius);
            context.FillRectangle(new SolidColorBrush(EditorButtons.ToColor(pure)), area, corner);
            context.FillRectangle(Gradient(new RelativePoint(0, 0, RelativeUnit.Relative), new RelativePoint(1, 0, RelativeUnit.Relative), Colors.White, Color.FromArgb(0, 255, 255, 255)), area, corner);
            context.FillRectangle(Gradient(new RelativePoint(0, 0, RelativeUnit.Relative), new RelativePoint(0, 1, RelativeUnit.Relative), Color.FromArgb(0, 0, 0, 0), Colors.Black), area, corner);
            var at = new Point(hsv.Saturation * area.Width, (1 - hsv.Value) * area.Height);
            context.DrawEllipse(null, new Pen(Brushes.White, 2), at, 7, 7);
        }

        protected override void Move(Point point) =>
            Moved?.Invoke(Math.Clamp(point.X / Bounds.Width, 0, 1), 1 - Math.Clamp(point.Y / Bounds.Height, 0, 1));
    }

    /// <summary>The hue strip, red to red through the spectrum, with a knob at the hue.</summary>
    private sealed class HueStrip : PickSurface
    {
        private double hue;

        public event Action<double>? Moved;

        public void Show(double shown)
        {
            hue = shown;
            InvalidateVisual();
        }

        public override void Render(DrawingContext context)
        {
            var area = new Rect(Bounds.Size);
            var spectrum = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative) };
            for (var stop = 0; stop <= 6; stop++)
            {
                spectrum.GradientStops.Add(new GradientStop(EditorButtons.ToColor(ColorChoices.FromHsv(new Hsv(stop * 60 % 360, 1, 1))), stop / 6d));
            }

            context.FillRectangle(spectrum, area, (float)(area.Height / 2));
            var at = new Point(hue / 360 * area.Width, area.Height / 2);
            context.DrawEllipse(Brushes.White, new Pen(new SolidColorBrush(Color.FromArgb(0x60, 0, 0, 0)), 1), at, area.Height / 2 + 2, area.Height / 2 + 2);
        }

        // Short of 360, which is red again, so the knob stays at the strip's end.
        protected override void Move(Point point) => Moved?.Invoke(Math.Clamp(point.X / Bounds.Width, 0, 1) * 359.9);
    }

    private static LinearGradientBrush Gradient(RelativePoint start, RelativePoint end, Color from, Color to) => new()
    {
        StartPoint = start,
        EndPoint = end,
        GradientStops = { new GradientStop(from, 0), new GradientStop(to, 1) },
    };
}

/// <summary>What a <see cref="ColorPicker"/> tells of a drag on its square or strip (G21, G25).</summary>
/// <param name="Begin">The square or the strip pressed: a drag begins.</param>
/// <param name="Preview">Each colour the drag passes, shown as no step; false when the drag was ended meanwhile.</param>
/// <param name="LetGo">The colour let go on, applied as one step; false when the drag was abandoned meanwhile and nothing was applied.</param>
/// <param name="Abandon">The drag ended without its let-go: what it showed put back.</param>
internal sealed record ColorDrag(Action Begin, Func<Rgba, bool> Preview, Func<Rgba, bool> LetGo, Action Abandon);
