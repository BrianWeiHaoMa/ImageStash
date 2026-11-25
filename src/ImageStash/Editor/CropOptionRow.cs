using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using ImageStash.Look;

namespace ImageStash.Editor;

/// <summary>
/// Crop's option row (G20, G22): 1:1, lit while it is on, and Reset. 1:1 is a symbol whose action its digits do not say,
/// so its tip names it.
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
        EditorOptions.SetSpacing(this);
        square = EditorButtons.Choice("1 : 1", OneToOne, () => tool.ChooseSquare(!tool.Square));
        ToolTipRule.Set(square, SquareTip);
        var reset = EditorButtons.Labelled(null, Reset);
        reset.Height = EditorButtons.ChoiceHeight;
        reset.Click += (_, _) => tool.Reset();
        Children.AddRange([square, reset]);
        Refresh();
    }

    public EditorTool Tool => tool;

    public void Refresh() => square.IsChecked = tool.Square;
}
