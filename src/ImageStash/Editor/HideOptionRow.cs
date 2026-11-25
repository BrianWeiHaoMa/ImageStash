using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using ImageStash.Layers;

namespace ImageStash.Editor;

/// <summary>
/// Hide's option row (G20, G23): Blur and Black box, the selected hide's style or the next one's.
/// </summary>
internal sealed class HideOptionRow : WrapPanel, IOptionRow
{
    private readonly HideTool tool;
    private readonly List<(HideKind Kind, ToggleButton Button)> kinds;

    public HideOptionRow(HideTool tool)
    {
        this.tool = tool;
        EditorOptions.SetSpacing(this);
        kinds =
        [
            (HideKind.Blur, EditorButtons.Choice("Blur", "Blur", () => tool.Choose(HideKind.Blur))),
            (HideKind.Black, EditorButtons.Choice("Black box", "Black box", () => tool.Choose(HideKind.Black))),
        ];
        Children.AddRange(kinds.Select(kind => kind.Button));
        Refresh();
    }

    public EditorTool Tool => tool;

    public void Refresh() => EditorButtons.Check(kinds, tool.KindShown);
}
