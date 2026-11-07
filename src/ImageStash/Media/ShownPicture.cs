using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace ImageStash.Media;

/// <summary>
/// How a picture shows at full size on screen, in the preview and the editor alike (G19, G6): its place in an area, one
/// picture pixel to one display pixel, scaled down whole when it does not fit and never enlarged; the area's neutral
/// fill; and the checkerboard laid under it, so transparency shows.
/// </summary>
public static class ShownPicture
{
    /// <summary>The checkerboard's squares, in device-independent pixels.</summary>
    private const int Square = 8;

    private static ImageBrush? lightCheckerboard;
    private static ImageBrush? darkCheckerboard;

    /// <summary>
    /// Where a picture of <paramref name="pixels"/> shows inside <paramref name="area"/> on a display of
    /// <paramref name="scaling"/>: one picture pixel to one display pixel, scaled down whole when that does not fit and
    /// never enlarged (G19), centred, its corner on a whole display pixel so it is not blurred.
    /// </summary>
    public static Rect Fit(PixelSize pixels, Size area, double scaling)
    {
        var natural = new Size(pixels.Width / scaling, pixels.Height / scaling);
        var scale = Math.Min(1, Math.Min(area.Width / natural.Width, area.Height / natural.Height));
        var size = natural * Math.Max(0, scale);
        var x = Math.Round((area.Width - size.Width) / 2 * scaling) / scaling;
        var y = Math.Round((area.Height - size.Height) / 2 * scaling) / scaling;
        return new Rect(x, y, size.Width, size.Height);
    }

    /// <summary>The checkerboard as a brush tiling two squares of each colour, with colours for the theme.</summary>
    public static ImageBrush Checkerboard(bool dark) => dark ? darkCheckerboard ??= Tiled(dark) : lightCheckerboard ??= Tiled(dark);

    private static ImageBrush Tiled(bool dark)
    {
        var (light, shade) = dark ? (Color.FromRgb(0x3A, 0x3B, 0x42), Color.FromRgb(0x2E, 0x2F, 0x35)) : (Color.FromRgb(0xFF, 0xFF, 0xFF), Color.FromRgb(0xE6, 0xE7, 0xEC));
        var tile = new WriteableBitmap(new PixelSize(2 * Square, 2 * Square), new Vector(96, 96), PixelFormats.Bgra8888, AlphaFormat.Opaque);
        using (var pixels = tile.Lock())
        {
            var row = new byte[pixels.RowBytes];
            for (var y = 0; y < 2 * Square; y++)
            {
                for (var x = 0; x < 2 * Square; x++)
                {
                    var color = (x < Square) == (y < Square) ? light : shade;
                    row[x * 4] = color.B;
                    row[x * 4 + 1] = color.G;
                    row[x * 4 + 2] = color.R;
                    row[x * 4 + 3] = 0xFF;
                }

                System.Runtime.InteropServices.Marshal.Copy(row, 0, pixels.Address + y * pixels.RowBytes, row.Length);
            }
        }

        return new ImageBrush(tile)
        {
            TileMode = TileMode.Tile,
            Stretch = Stretch.None,
            AlignmentX = AlignmentX.Left,
            AlignmentY = AlignmentY.Top,
            DestinationRect = new RelativeRect(0, 0, 2 * Square, 2 * Square, RelativeUnit.Absolute),
        };
    }
}
