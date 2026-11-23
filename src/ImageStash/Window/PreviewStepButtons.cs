using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using ImageStash.Look;

namespace ImageStash.Window;

/// <summary>
/// The preview's ‹ and › (G19, G7): the pointer's way to step to the previous and the next item of the grid's list, as
/// Left and Right do, through <see cref="PreviewStepClicks"/>. Two round glyph buttons laid over the left and right
/// edges of the picture's row (<see cref="Over"/>), never taking room from it, so a picture keeps its own size (G19), and
/// faint until the pointer is over that row, where they show at full strength. Neither the picture's
/// child nor focusable, so a press on one neither pauses the picture nor moves focus: a name or tag in edit stays in its
/// field until the click's act commits it, and Tab's round in the preview is unchanged. Each is unavailable, and faint,
/// where its step shows no item, and has a name a screen reader reads. On the dark pill with a white glyph in either
/// take, as the picture's play mark, so each reads over a white picture and a black one (G6, F10).
/// </summary>
public sealed class PreviewStepButtons : Panel
{
    // Faint, never two solid discs over a picture nobody is pointing at, yet where the pointer finds them; an unavailable
    // one is fainter still (LookStyles).
    private const double AtRest = 0.3;

    public PreviewStepButtons()
    {
        Opacity = AtRest;
        Back = Made(StepWay.Back, "", HorizontalAlignment.Left);
        On = Made(StepWay.On, "", HorizontalAlignment.Right);
        Children.Add(Back);
        Children.Add(On);
        Show(back: false, on: false);
    }

    /// <summary>Raised by a click on ‹ (<see cref="StepWay.Back"/>) or › (<see cref="StepWay.On"/>).</summary>
    public event Action<StepWay>? Clicked;

    /// <summary>‹, the step back.</summary>
    public Button Back { get; }

    /// <summary>›, the step on.</summary>
    public Button On { get; }

    /// <summary>
    /// The picture's row: <paramref name="picture"/> with the buttons laid over it, faint while the pointer is away from
    /// the row and at full strength while it is over the picture or either button.
    /// </summary>
    public Panel Over(Control picture)
    {
        // After the picture so above it, never its child: a press on one neither pauses it nor focuses it (G19).
        var row = new Panel { Children = { picture, this } };
        row.PropertyChanged += (_, change) =>
        {
            if (change.Property == IsPointerOverProperty)
            {
                Opacity = row.IsPointerOver ? 1 : AtRest;
            }
        };
        return row;
    }

    /// <summary>Each available exactly where <paramref name="clicks"/> says its step shows an item.</summary>
    public void Show(PreviewStepClicks clicks) => Show(clicks.Available(StepWay.Back), clicks.Available(StepWay.On));

    /// <summary>What a screen reader reads for the button stepping <paramref name="way"/>.</summary>
    public static string Name(StepWay way) => way == StepWay.Back ? "Previous" : "Next";

    /// <summary>The tooltip of the button stepping <paramref name="way"/>: what it does and the key that does the same.</summary>
    public static string Tip(StepWay way) => way == StepWay.Back ? "Previous (Left)" : "Next (Right)";

    private void Show(bool back, bool on)
    {
        Back.IsEnabled = back;
        On.IsEnabled = on;
    }

    private Button Made(StepWay way, string glyph, HorizontalAlignment side)
    {
        var button = new Button
        {
            Content = IconGlyph.Of(glyph, 14),
            Width = 32,
            Height = 32,
            Padding = default,
            HorizontalAlignment = side,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0),
            // As the chips: a click leaves focus, and a name or tag in edit, where they are (G19).
            Focusable = false,
        };
        button.Classes.Add(LookStyles.PictureButton);
        AutomationProperties.SetAutomationId(button, way == StepWay.Back ? "Previous" : "Next");
        AutomationProperties.SetName(button, Name(way));
        ToolTipRule.Set(button, Tip(way));
        button.Click += (_, _) => Clicked?.Invoke(way);
        return button;
    }
}
