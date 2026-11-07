namespace ImageStash.Layers;

/// <summary>
/// One layer of an item's edit (F7): one of the seven kinds below, each with exactly the fields What crosses every
/// boundary gives it, positions and sizes on the original's pixel grid. Only a <see cref="LayerList"/> judges whether a
/// layer is valid, so no list that exists holds one the model refuses.
/// </summary>
public abstract record Layer;

/// <summary>
/// A text layer made with the Text tool's Meme or Label preset: its box by top-left and size, its font size in the
/// original's pixels, its rotation in degrees clockwise about the box's centre, and a time range (null: the whole clip),
/// which the editor gives only on an animated item (G22).
/// </summary>
public sealed record TextLayer(
    TextPreset Preset,
    string Text,
    string Font,
    int FontSize,
    Rgba Fill,
    Outline Outline,
    PixelRect Box,
    double Rotation,
    FrameRange? Range) : Layer;

/// <summary>A caption bar: a white band above the cropped picture holding the text; no position, rotation or range (F7, G21, G22).</summary>
public sealed record CaptionBarLayer(string Text, string Font, int FontSize, Rgba Fill, Outline Outline) : Layer;

/// <summary>A hidden rectangle, blurred or covered in black (G23), with a time range (null: the whole clip).</summary>
public sealed record HideLayer(PixelRect Rect, HideKind Kind, FrameRange? Range) : Layer;

/// <summary>
/// A draw mark (G23): a pen or marker stroke along <see cref="Points"/> (two or more), an arrow from its first point to
/// its second, where the head is, or a box outlining <see cref="Rect"/>; with its color, one of three thickness steps,
/// and a time range (null: the whole clip). A box has no points; the other marks have no rectangle.
/// </summary>
public sealed record DrawMarkLayer(
    MarkKind Kind,
    IReadOnlyList<PixelPoint> Points,
    PixelRect? Rect,
    Rgba Color,
    Thickness Thickness,
    FrameRange? Range) : Layer
{
    // Its own copy, so a list validated once cannot be changed through the caller's collection.
    private readonly IReadOnlyList<PixelPoint> points = Own(Points);

    public IReadOnlyList<PixelPoint> Points { get => points; init => points = Own(value); }

    public bool Equals(DrawMarkLayer? other) =>
        other is not null && Kind == other.Kind && Rect == other.Rect && Color == other.Color && Thickness == other.Thickness
        && Range == other.Range && (Points ?? []).SequenceEqual(other.Points ?? []);

    public override int GetHashCode() => HashCode.Combine(Kind, Points?.Count, Rect, Color, Thickness, Range);

    private static IReadOnlyList<PixelPoint> Own(IReadOnlyList<PixelPoint>? points) => points is null ? null! : Array.AsReadOnly(points.ToArray());
}

/// <summary>The crop's rectangle of the original; the whole original is kept, so it can be widened later (G22).</summary>
public sealed record CropLayer(PixelRect Rect) : Layer;

/// <summary>The trim's kept frames of the original; frames outside it stay in the original (G22).</summary>
public sealed record TrimLayer(FrameRange Range) : Layer;

/// <summary>The Shrink choice, applied to the whole result at Save (G24).</summary>
public sealed record ShrinkLayer(Shrink Scale) : Layer;

public enum TextPreset
{
    Meme,
    Label,
}

public enum HideKind
{
    Blur,
    Black,
}

public enum MarkKind
{
    Pen,
    Marker,
    Arrow,
    Box,
}

/// <summary>A mark's thickness as a step, never a pixel width, so the widths live in one place (L2).</summary>
public enum Thickness
{
    Thin,
    Medium,
    Thick,
}

/// <summary>Shrink's scale: 1, 3/4 or 1/2 (What crosses every boundary).</summary>
public enum Shrink
{
    Full,
    ThreeQuarter,
    Half,
}
