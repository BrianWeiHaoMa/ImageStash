using System.Drawing;
using ImageStash.Layers;

namespace ImageStash.Editor;

/// <summary>
/// Hide (G23): a dragged rectangle becomes a layer that blurs what is under it, or with Black box covers it fully, and is
/// moved or resized later by the shared rules. A new hide is blurred until Black box is chosen,
/// and takes the style the option row shows as its drag begins: a selected hide's (G20).
/// </summary>
public sealed class HideTool : EditorTool
{
    internal HideTool(EditorSession session)
        : base(session)
    {
    }

    public override string Name => "Hide";

    public override string Hint => "Drag over what to hide";

    /// <summary>The style a new hide gets while no hide is selected.</summary>
    public HideKind Kind { get; private set; } = HideKind.Blur;

    /// <summary>The style the option row shows: the selected hide's, else <see cref="Kind"/>.</summary>
    public HideKind KindShown => Session.SelectedLayer is HideLayer hide ? hide.Kind : Kind;

    /// <summary>Chooses Blur or Black box for the next hide, and for the selected one.</summary>
    public void Choose(HideKind kind)
    {
        Kind = kind;
        ApplyToSelected(layer => (HideLayer)layer with { Kind = kind });
    }

    public override bool Owns(Layer layer) => layer is HideLayer;

    private protected override Layer Drawn(PointF from, PointF to, IReadOnlyList<PixelPoint> visited) =>
        new HideLayer(LayerGeometry.Covering(from, to, LayerGeometry.Picture(Session.Layers, Session.OriginalSize)), KindShown, null);
}
