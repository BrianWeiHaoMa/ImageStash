using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using ImageStash.Layers;
using LayerThickness = ImageStash.Layers.Thickness;
using Thickness = Avalonia.Thickness;
using ImageStash.Look;

namespace ImageStash.Editor;

/// <summary>
/// Draw's option row (G20, G21, G23): Pen, Marker, Arrow and Box; the five swatches and the More swatch, which opens the
/// colour picker in the editor's pane and shows a picked colour inside it; the three thicknesses drawn as dots; and what the mouse does. It
/// shows the selected mark's values, else the next mark's, and wraps onto further lines when the window is narrower.
/// </summary>
internal sealed class DrawOptionRow : WrapPanel, IOptionRow
{
    public const string MoreColors = "More colors";

    private readonly DrawTool tool;
    private readonly List<(MarkKind Kind, ToggleButton Button)> marks;
    private readonly List<(Rgba Color, ToggleButton Button)> swatches;
    private readonly List<(LayerThickness Thickness, ToggleButton Button)> thicknesses;
    private readonly ToggleButton more;
    private readonly Border moreFill;

    public DrawOptionRow(DrawTool tool, ColorChoices colors, EditorCanvas canvas, EditorPane pane)
    {
        this.tool = tool;
        Orientation = Orientation.Horizontal;
        ItemSpacing = 12;
        LineSpacing = 6;
        marks = [.. new[] { (MarkKind.Pen, "Pen"), (MarkKind.Marker, "Marker"), (MarkKind.Arrow, "Arrow"), (MarkKind.Box, "Box") }
            .Select(mark => (mark.Item1, EditorButtons.Choice(mark.Item2, mark.Item2, () => tool.Choose(mark.Item1))))];
        swatches = [.. ColorChoices.Swatches.Select(color => (color, EditorButtons.Swatch(color, () => tool.Choose(color))))];
        var picker = new ColorPicker(colors, canvas, () => tool.ColorShown, tool.Choose, new ColorDrag(tool.BeginColor, tool.Preview, tool.LetGo, tool.AbandonColor));
        picker.Done += pane.Hide;
        (more, moreFill) = EditorButtons.More(MoreColors, anchor =>
        {
            picker.Reset();
            pane.Show(picker, anchor);
            Refresh();
        });
        thicknesses = [.. new[] { (LayerThickness.Thin, "Thin", 4d), (LayerThickness.Medium, "Medium", 7d), (LayerThickness.Thick, "Thick", 11d) }
            .Select(each => (each.Item1, EditorButtons.Choice(Dot(each.Item3), each.Item2, () => tool.Choose(each.Item1))))];

        Children.Add(Group(marks.Select(mark => mark.Button)));
        Children.Add(Group([.. swatches.Select(swatch => swatch.Button), more]));
        Children.Add(Group(thicknesses.Select(each => each.Button)));
        Children.Add(new TextBlock { Text = tool.Hint, Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0) });
        Refresh();
    }

    public EditorTool Tool => tool;

    public void Refresh()
    {
        EditorButtons.Check(marks, tool.MarkShown);
        EditorButtons.Check(swatches, tool.ColorShown);
        EditorButtons.Check(thicknesses, tool.ThicknessShown);
        EditorButtons.ShowPicked(more, moreFill, tool.ColorShown);
    }

    private static StackPanel Group(IEnumerable<Control> buttons)
    {
        var group = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        group.Children.AddRange(buttons);
        return group;
    }

    /// <summary>A thickness drawn as a dot of the text's colour for the theme.</summary>
    private static Control Dot(double size)
    {
        var dot = new Ellipse { Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        dot.Bind(Shape.FillProperty, dot.GetResourceObservable(LookKeys.Words));
        return dot;
    }
}
