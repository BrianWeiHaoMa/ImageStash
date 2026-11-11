using Avalonia;
using ImageStash.Media;
using ImageStash.Store;

namespace ImageStash.Window;

/// <summary>A tile of the grid: its item, the item's place in the listing's order, and its box in the grid.</summary>
public sealed record GridTile(long Id, int Index, double X, double Y, double Width, double Height)
{
    public double CentreX => X + Width / 2;
}

/// <summary>One row of the grid, top to bottom.</summary>
public sealed record GridRow(double Y, double Height, IReadOnlyList<GridTile> Tiles);

/// <summary>
/// G6's justified rows, laid out from the facts the store holds, never from decoded pixels, so the grid of 5,000 items
/// is known before any thumbnail is read (G1, E4): each tile keeps its item's aspect ratio, clamped to 0.62–2.1 (the
/// picture is cropped to the tile in the grid only), every full row is scaled to fill the width exactly, and the last
/// row keeps the target height.
/// </summary>
public static class JustifiedRows
{
    public const double NarrowestRatio = 0.62;
    public const double WidestRatio = 2.1;

    /// <summary>The space between tiles and between rows, in device-independent units.</summary>
    public const double Gap = 4;

    /// <summary>The target row height of each thumbnail size (L2), in device-independent units.</summary>
    public static double RowHeight(ThumbnailSize size) => size switch
    {
        ThumbnailSize.Small => 64,
        ThumbnailSize.Large => 136,
        ThumbnailSize.ExtraLarge => 192,
        ThumbnailSize.Huge => 256,
        _ => 96,
    };

    /// <summary>The size <paramref name="steps"/> steps larger, or smaller when negative, held at the smallest and the largest (G6).</summary>
    public static ThumbnailSize Step(ThumbnailSize size, int steps) =>
        (ThumbnailSize)Math.Clamp((int)size + steps, (int)ThumbnailSize.Small, (int)ThumbnailSize.Huge);

    /// <summary>The tile's aspect ratio, width over height, for a picture of these facts.</summary>
    public static double Ratio(MediaFacts facts) =>
        facts.Width > 0 && facts.Height > 0 ? Math.Clamp((double)facts.Width / facts.Height, NarrowestRatio, WidestRatio) : 1;

    public static IReadOnlyList<GridRow> Layout(IReadOnlyList<StashItem> items, double width, double rowHeight, double gap = Gap)
    {
        var rows = new List<GridRow>();
        var start = 0;
        var y = 0.0;
        var sum = 0.0;
        for (var i = 0; i < items.Count; i++)
        {
            sum += Ratio(items[i].Facts);
            var count = i - start + 1;
            if (sum * rowHeight + gap * (count - 1) >= width)
            {
                var height = (width - gap * (count - 1)) / sum;
                rows.Add(Row(items, start, count, y, height, gap, width));
                y += height + gap;
                start = i + 1;
                sum = 0;
            }
        }

        if (start < items.Count)
        {
            rows.Add(Row(items, start, items.Count - start, y, rowHeight, gap, fill: null));
        }

        return rows;
    }

    /// <summary>The first and last index of the rows that lie within <paramref name="margin"/> of the visible band; (0, -1) when none does.</summary>
    public static (int First, int Last) InView(IReadOnlyList<GridRow> rows, double top, double height, double margin)
    {
        var from = top - margin;
        var to = top + height + margin;
        var first = FirstEndingAfter(rows, from);
        var last = first - 1;
        while (last + 1 < rows.Count && rows[last + 1].Y <= to)
        {
            last++;
        }

        return last < first ? (0, -1) : (first, last);
    }

    /// <summary>
    /// The rectangle of a picture <paramref name="sourceWidth"/> × <paramref name="sourceHeight"/> that a tile of
    /// <paramref name="width"/> × <paramref name="height"/> shows: the largest one of the tile's shape, centred.
    /// </summary>
    public static Rect Cover(int sourceWidth, int sourceHeight, double width, double height)
    {
        var scale = Math.Max(width / sourceWidth, height / sourceHeight);
        var shownWidth = Math.Min(sourceWidth, width / scale);
        var shownHeight = Math.Min(sourceHeight, height / scale);
        return new Rect((sourceWidth - shownWidth) / 2, (sourceHeight - shownHeight) / 2, shownWidth, shownHeight);
    }

    /// <param name="fill">The width a full row fills exactly; null for the last row, which keeps its natural widths.</param>
    private static GridRow Row(IReadOnlyList<StashItem> items, int start, int count, double y, double height, double gap, double? fill)
    {
        var tiles = new GridTile[count];
        var x = 0.0;
        for (var i = 0; i < count; i++)
        {
            var item = items[start + i];
            // The last tile of a full row takes what is left, so rounding never leaves a ragged right edge.
            var tileWidth = fill is { } full && i == count - 1 ? full - x : Ratio(item.Facts) * height;
            tiles[i] = new GridTile(item.Id, start + i, x, y, tileWidth, height);
            x += tileWidth + gap;
        }

        return new GridRow(y, height, tiles);
    }

    private static int FirstEndingAfter(IReadOnlyList<GridRow> rows, double y)
    {
        int low = 0, high = rows.Count;
        while (low < high)
        {
            var middle = (low + high) / 2;
            if (rows[middle].Y + rows[middle].Height < y)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }
}
