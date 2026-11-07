using System.Drawing;
using ImageStash.Layers;

namespace ImageStash.Editor;

/// <summary>
/// Draw (G23): Pen, Marker (Media's translucent wide stroke), Arrow (a line with its head where the drag ends) and Box (an
/// outlined rectangle), in a swatch color or any picked color, at three thicknesses; each mark its own layer. A new mark
/// starts as a medium red pen, and takes the kind, color and thickness the option row shows as its
/// drag begins: a selected mark's (G20).
/// </summary>
public sealed class DrawTool : EditorTool
{
    internal DrawTool(EditorSession session)
        : base(session)
    {
    }

    public override string Name => "Draw";

    public override string Hint => "Drag to draw a mark; click a mark to select it";

    /// <summary>The kind of mark a drag draws next while no mark is selected.</summary>
    public MarkKind Mark { get; private set; } = MarkKind.Pen;

    /// <summary>The color of the next mark while no mark is selected.</summary>
    public Rgba Color { get; private set; } = ColorChoices.Red;

    /// <summary>The thickness of the next mark while no mark is selected.</summary>
    public Thickness Thickness { get; private set; } = Thickness.Medium;

    /// <summary>The kind the option row shows: the selected mark's, else <see cref="Mark"/>.</summary>
    public MarkKind MarkShown => Selected?.Kind ?? Mark;

    /// <summary>The color the option row shows: the selected mark's, else <see cref="Color"/>.</summary>
    public Rgba ColorShown => Selected?.Color ?? Color;

    /// <summary>The thickness the option row shows: the selected mark's, else <see cref="Thickness"/>.</summary>
    public Thickness ThicknessShown => Selected?.Thickness ?? Thickness;

    private DrawMarkLayer? Selected => Session.SelectedLayer as DrawMarkLayer;

    /// <summary>Chooses the kind of the next mark; the selected mark becomes that kind over the same pixels.</summary>
    public void Choose(MarkKind mark)
    {
        Mark = mark;
        ApplyToSelected(layer => LayerGeometry.Reshaped((DrawMarkLayer)layer, mark));
    }

    /// <summary>Chooses the color of the next mark and of the selected one; a color that is not a swatch becomes a recent one (G21).</summary>
    public void Choose(Rgba color)
    {
        Color = color;
        Session.Colors.Use(color);
        ApplyToSelected(layer => (DrawMarkLayer)layer with { Color = color });
    }

    /// <summary>Shows the selected mark in the <paramref name="color"/> the picker is dragged to, as no step; with no mark selected, nothing yet (G23, G25); false when the drag was ended meanwhile.</summary>
    public bool Preview(Rgba color) => PreviewOnSelected(layer => (DrawMarkLayer)layer with { Color = color });

    /// <summary>
    /// The picker's drag let go on <paramref name="color"/>: the next mark's color, and the selected mark's as one step,
    /// recorded once as a recent one (G21, G23); false, applying nothing, when the drag was abandoned meanwhile.
    /// </summary>
    public bool LetGo(Rgba color)
    {
        if (!CommitOnSelected(layer => (DrawMarkLayer)layer with { Color = color }))
        {
            return false;
        }

        Color = color;
        Session.Colors.Use(color);
        Session.RaiseChanged();
        return true;
    }

    /// <summary>Chooses the thickness of the next mark and of the selected one.</summary>
    public void Choose(Thickness thickness)
    {
        Thickness = thickness;
        ApplyToSelected(layer => (DrawMarkLayer)layer with { Thickness = thickness });
    }

    public override bool Owns(Layer layer) => layer is DrawMarkLayer;

    private protected override Layer Drawn(PointF from, PointF to, IReadOnlyList<PixelPoint> visited)
    {
        var (picture, mark, color, thickness) = (LayerGeometry.Picture(Session.Layers, Session.OriginalSize), MarkShown, ColorShown, ThicknessShown);
        return mark switch
        {
            MarkKind.Box => new DrawMarkLayer(mark, [], LayerGeometry.Covering(from, to, picture), color, thickness, null),
            MarkKind.Arrow => new DrawMarkLayer(mark, [LayerGeometry.PixelUnder(from, picture), LayerGeometry.PixelUnder(to, picture)], null, color, thickness, null),
            // A stroke needs two points; one that stayed on one pixel is a dot there.
            _ => new DrawMarkLayer(mark, visited.Count > 1 ? visited : [visited[0], visited[0]], null, color, thickness, null),
        };
    }
}
