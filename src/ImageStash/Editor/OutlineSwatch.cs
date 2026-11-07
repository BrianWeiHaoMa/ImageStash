using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using ImageStash.Layers;
using ImageStash.Media;
using Thickness = Avalonia.Thickness;
using ImageStash.Look;

namespace ImageStash.Editor;

/// <summary>
/// The Text tool's outline swatch (G21): a disc of the text's fill ringed in its outline's colour, the
/// colour Auto gives that fill (<see cref="LayerComposer.AutoOutline"/>) lettered A, or struck through for None; a click
/// opens the colour picker with Auto and None.
/// </summary>
internal sealed class OutlineSwatch : Button
{
    public const string Called = "Outline color";

    private static readonly IBrush StrikeBrush = new SolidColorBrush(Color.FromRgb(0xD1, 0x34, 0x38));

    private readonly Ellipse ring;
    private readonly TextBlock auto;
    private readonly Control strike;
    private IDisposable? faintRing;

    public OutlineSwatch(Action<Control> clicked)
    {
        ring = new Ellipse { Width = 22, Height = 22, StrokeThickness = 4 };
        auto = new TextBlock { Text = "A", FontSize = 10, FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        strike = Strike(22);
        Content = new Panel { Width = 22, Height = 22, Children = { ring, auto, strike } };
        Height = 32;
        MinWidth = 32;
        Padding = new Thickness(5, 0);
        AutomationProperties.SetName(this, Called);
        ToolTipRule.Set(this, "Outline");
        Click += (_, _) => clicked(this);
    }

    /// <summary>Shows <paramref name="outline"/> round <paramref name="fill"/>.</summary>
    public void Show(Outline outline, Rgba fill)
    {
        var ringColor = outline.IsAuto ? LayerComposer.AutoOutline(fill) : outline.Color;
        ring.Fill = new SolidColorBrush(EditorButtons.ToColor(fill));
        faintRing?.Dispose();
        faintRing = null;
        if (ringColor is { } color)
        {
            ring.Stroke = new SolidColorBrush(EditorButtons.ToColor(color));
        }
        else
        {
            faintRing = ring.Bind(Shape.StrokeProperty, ring.GetResourceObservable(LookKeys.SwatchEdge));
        }

        ring.StrokeThickness = ringColor is null ? 1 : 4;
        auto.Foreground = ringColor is { } letter ? new SolidColorBrush(EditorButtons.ToColor(letter)) : null;
        auto.IsVisible = outline.IsAuto;
        strike.IsVisible = outline == Outline.None;
    }

    /// <summary>None's mark: a red stroke across a round of <paramref name="size"/>.</summary>
    public static Control Strike(double size) => new Line
    {
        StartPoint = new Point(size * 0.2, size * 0.8),
        EndPoint = new Point(size * 0.8, size * 0.2),
        Stroke = StrikeBrush,
        StrokeThickness = 2,
        Width = size,
        Height = size,
    };
}
