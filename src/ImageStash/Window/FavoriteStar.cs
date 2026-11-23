namespace ImageStash.Window;

/// <summary>
/// The favorite star of Segoe Fluent Icons (F10), filled while an item is a favorite and outlined while it is not, named
/// here once for every place that shows the flag: the preview's star (<see cref="PreviewStar"/>), a tile's
/// (<see cref="TileStar"/>) and the item menu's Favorites entries (G17, G19, G6). Each shows the flag as the store last
/// answered it, the item a read handed it, never one flipped ahead of the store (R9, G28).
/// </summary>
public static class FavoriteStar
{
    /// <summary>FavoriteStar: the flag off, or the act of setting it.</summary>
    public const string Outlined = "";

    /// <summary>FavoriteStarFill: the flag on, or the act of taking it off.</summary>
    public const string Filled = "";

    public static string Glyph(bool favorite) => favorite ? Filled : Outlined;
}
