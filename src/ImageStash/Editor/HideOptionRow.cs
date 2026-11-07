using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using ImageStash.Layers;
using Thickness = Avalonia.Thickness;

namespace ImageStash.Editor;

/// <summary>
/// Hide's option row (G20, G23): Blur and Black box, the selected hide's style or the next one's, and what the mouse
/// does; it wraps onto a second line when the window is narrower than it.
/// </summary>
internal sealed class HideOptionRow : WrapPanel, IOptionRow
{
    private readonly HideTool tool;
    private readonly List<(HideKind Kind, ToggleButton Button)> kinds;

    public HideOptionRow(HideTool tool)
    {
        this.tool = tool;
        Orientation = Orientation.Horizontal;
        ItemSpacing = 12;
        LineSpacing = 6;
        kinds =
        [
            (HideKind.Blur, EditorButtons.Choice("Blur", "Blur", () => tool.Choose(HideKind.Blur))),
            (HideKind.Black, EditorButtons.Choice("Black box", "Black box", () => tool.Choose(HideKind.Black))),
        ];
        Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { kinds[0].Button, kinds[1].Button } });
        Children.Add(new TextBlock { Text = tool.Hint, Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0) });
        Refresh();
    }

    public EditorTool Tool => tool;

    public void Refresh() => EditorButtons.Check(kinds, tool.KindShown);
}
