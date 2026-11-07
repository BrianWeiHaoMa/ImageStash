namespace ImageStash.Layers;

/// <summary>A point on the original's pixel grid, origin top-left (What crosses every boundary).</summary>
public readonly record struct PixelPoint(int X, int Y);

/// <summary>A size in whole pixels.</summary>
public readonly record struct PixelSize(int Width, int Height);

/// <summary>A rectangle on the original's pixel grid by its top-left and size (What crosses every boundary).</summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height);

/// <summary>An 8-bit sRGB color with its alpha (What crosses every boundary).</summary>
public readonly record struct Rgba(byte R, byte G, byte B, byte A);

/// <summary>
/// The first and last frame index of the original, counted from zero, inclusive (What crosses every boundary). It is
/// kept as written when the trim moves (G22), so it may lie partly or wholly beyond the frames.
/// </summary>
public readonly record struct FrameRange(int First, int Last)
{
    public bool Admits(int frameIndex) => frameIndex >= First && frameIndex <= Last;
}

/// <summary>A text's outline: <see cref="Auto"/> (black on a light fill, white on a dark one, G21), <see cref="None"/>, or a color.</summary>
public sealed record Outline
{
    public static readonly Outline Auto = new(null, isAuto: true);

    public static readonly Outline None = new(null, isAuto: false);

    private Outline(Rgba? color, bool isAuto) => (Color, IsAuto) = (color, isAuto);

    /// <summary>The outline's color; null for <see cref="Auto"/> and <see cref="None"/>.</summary>
    public Rgba? Color { get; }

    public bool IsAuto { get; }

    public static Outline Of(Rgba color) => new(color, isAuto: false);
}
