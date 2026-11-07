using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using ImageStash.Look;
using Thickness = Avalonia.Thickness;

namespace ImageStash.Editor;

/// <summary>
/// Crop's option row (G20, G22): what the mouse does, 1:1, lit while it is on, and Reset; it wraps onto a
/// second line when the window is narrower than it. 1:1 is a symbol whose action its digits do not say, so its tip names it.
/// </summary>
internal sealed class CropOptionRow : WrapPanel, IOptionRow
{
    public const string OneToOne = "1:1";
    public const string Reset = "Reset";
    private const string SquareTip = "Square crop";

    private readonly CropTool tool;
    private readonly ToggleButton square;

    public CropOptionRow(CropTool tool)
    {
        this.tool = tool;
        Orientation = Orientation.Horizontal;
        ItemSpacing = 12;
        LineSpacing = 6;
        square = EditorButtons.Choice("1 : 1", OneToOne, () => tool.ChooseSquare(!tool.Square));
        ToolTipRule.Set(square, SquareTip);
        var reset = EditorButtons.Labelled(null, Reset);
        reset.Click += (_, _) => tool.Reset();
        Children.Add(new TextBlock { Text = tool.Hint, Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0) });
        Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { square, reset } });
        Refresh();
    }

    public EditorTool Tool => tool;

    public void Refresh() => square.IsChecked = tool.Square;
}
