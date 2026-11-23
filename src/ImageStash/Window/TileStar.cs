using System.Globalization;
using Avalonia;
using Avalonia.Media;
using ImageStash.Store;

namespace ImageStash.Window;

/// <summary>
/// The star a favorite's tile carries (G6, G17), so <c>Ctrl+D</c>, a drop on Favorites and the item menu's entry each
/// show what they did: the filled star in white on a dark round mark, as the badge's words are on theirs, so it reads on
/// a white, a black or a transparent picture in either take (F10), in the tile's bottom-right corner, apart from the badge
/// in its top-left. Drawn into the grid's own picture, it is no target of its own: a click, a double-click, a right-click
/// or a drag that starts on it is the tile's (G7, G8).
/// </summary>
public static class TileStar
{
    /// <summary>The mark's side, in device-independent units: small beside the smallest tile the grid lays out (L2).</summary>
    public const double Side = 16;

    /// <summary>How far the mark stands in from the tile's right and bottom edges.</summary>
    public const double Inset = 5;

    private const double GlyphSize = 10;

    private static readonly Typeface IconFace = new("Segoe Fluent Icons", FontStyle.Normal, FontWeight.Normal, FontStretch.Normal);

    /// <summary>Whether the tile of <paramref name="item"/>, as the listing holds it, carries the star.</summary>
    public static bool Of(StashItem item) => item.Favorite;

    /// <summary>Where the star's mark lies on a tile whose box is <paramref name="tile"/>.</summary>
    public static Rect Place(Rect tile) => new(tile.Right - Inset - Side, tile.Bottom - Inset - Side, Side, Side);

    /// <summary>
    /// Draws the star on the tile of <paramref name="item"/>, whose box is <paramref name="tile"/>, while the item is a
    /// favorite, in the badge's <paramref name="words"/> on its <paramref name="ground"/>.
    /// </summary>
    public static void Draw(DrawingContext context, StashItem item, Rect tile, IBrush ground, IBrush words)
    {
        if (!Of(item))
        {
            return;
        }

        var mark = Place(tile);
        context.DrawEllipse(ground, null, mark);
        var star = new FormattedText(FavoriteStar.Filled, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, IconFace, GlyphSize, words);
        context.DrawText(star, mark.Center - new Point(star.Width / 2, star.Height / 2));
    }
}
