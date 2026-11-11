using ImageStash.Capture;
using ImageStash.Media;

namespace ImageStash.Tests.Capture;

/// <summary>
/// The capture overlay's cross as pixels (G14, G15, E1): light arms with a dark edge beyond every light run across them,
/// transparent elsewhere, growing with the display's scaling, its hot spot at the arms' crossing; light and dark far apart,
/// so one or the other shows on white, black and the dimmed grey between; and no edge pixel of blue 0, which the
/// platform would mask out.
/// </summary>
[TestClass]
public sealed class RegionCursorTests
{
    private static readonly double[] Scalings = [1, 1.25, 1.5, 2, 3];

    [TestMethod]
    public void TheCrossGrowsWithTheScalingItsSidesAndItsArmsThickness()
    {
        var drawn = Scalings.Select(scaling => (scaling, cross: RegionCursor.Drawn(scaling))).ToList();

        for (var i = 1; i < drawn.Count; i++)
        {
            var (before, after) = (drawn[i - 1].cross, drawn[i].cross);
            Assert.IsGreaterThan(before.Picture.Width, after.Picture.Width, $"its side at {drawn[i].scaling}");
            Assert.IsGreaterThanOrEqualTo(Thickness(before), Thickness(after), $"its arms' light at {drawn[i].scaling}");
        }

        Assert.IsGreaterThan(Thickness(RegionCursor.Drawn(1)), Thickness(RegionCursor.Drawn(2)), "twice the scaling, thicker arms");
        foreach (var (scaling, cross) in drawn)
        {
            Assert.AreEqual(cross.Picture.Width, cross.Picture.Height, $"square at {scaling}");
            // Windows shows a picture cursor pixel for pixel: about 23 pixels at 100 %, and so many more at each scaling.
            Assert.IsLessThanOrEqualTo(3 * scaling + 2, Math.Abs(cross.Picture.Width - 23 * scaling), $"{cross.Picture.Width} pixels at {scaling}");
        }
    }

    [TestMethod]
    public void TheHotSpotIsAtTheArmsCrossingWhoseArmsRunFromItBothWays()
    {
        foreach (var scaling in Scalings)
        {
            var (picture, hot) = RegionCursor.Drawn(scaling);
            Assert.AreEqual(Kind.Light, KindAt(picture, hot.X, hot.Y), $"the hot spot on light at {scaling}");
            var row = Enumerable.Range(0, picture.Width).Where(x => KindAt(picture, x, hot.Y) == Kind.Light).ToList();
            var column = Enumerable.Range(0, picture.Height).Where(y => KindAt(picture, hot.X, y) == Kind.Light).ToList();
            Assert.AreEqual(row.Count, row[^1] - row[0] + 1, $"one light run along the hot spot's row at {scaling}");
            Assert.AreEqual(column.Count, column[^1] - column[0] + 1, $"and along its column at {scaling}");
            Assert.IsLessThanOrEqualTo(1, Math.Abs(hot.X - row[0] - (row[^1] - hot.X)), $"the hot spot midway along the horizontal arm at {scaling}: {row[0]}..{row[^1]}, {hot.X}");
            Assert.IsLessThanOrEqualTo(1, Math.Abs(hot.Y - column[0] - (column[^1] - hot.Y)), $"and the vertical one at {scaling}: {column[0]}..{column[^1]}, {hot.Y}");
            Assert.IsGreaterThan(picture.Width / 2, row.Count, $"long arms at {scaling}");
        }
    }

    [TestMethod]
    public void EveryLightRunAcrossAnArmHasADarkPixelBeyondEachEdgeAndTheRestIsTransparent()
    {
        foreach (var scaling in Scalings)
        {
            var (picture, _) = RegionCursor.Drawn(scaling);
            var (width, height) = (picture.Width, picture.Height);
            for (var line = 0; line < width; line++)
            {
                foreach (var (start, end, across) in Runs(picture, line, horizontal: true).Concat(Runs(picture, line, horizontal: false)))
                {
                    Assert.IsTrue(start > 0 && Beyond(start - 1), $"dark before the light run {start}..{end} {across} at {scaling}");
                    Assert.IsTrue(end < width - 1 && Beyond(end + 1), $"dark after the light run {start}..{end} {across} at {scaling}");

                    bool Beyond(int at) => across == "along a row" ? KindAt(picture, at, line) == Kind.Dark : KindAt(picture, line, at) == Kind.Dark;
                }
            }

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    if (KindAt(picture, x, y) == Kind.Dark)
                    {
                        Assert.IsTrue(NearLight(picture, x, y), $"({x}, {y}) dark only as the arms' edge at {scaling}");
                    }
                }
            }
        }
    }

    [TestMethod]
    public void LightAndDarkAreFarApartOpaqueAndTheDarkIsNeverOfBlueZero()
    {
        var (picture, _) = RegionCursor.Drawn(1.5);
        var colours = Enumerable.Range(0, picture.Width * picture.Height).Select(i => BitConverter.ToUInt32(picture.Pixels, i * 4)).Where(pixel => pixel != 0).Distinct().ToList();

        Assert.HasCount(2, colours, "a light and a dark, nothing between");
        Assert.IsTrue(colours.All(pixel => pixel >> 24 == 255), "both opaque");
        var (dark, light) = (colours.MinBy(Luma), colours.MaxBy(Luma));
        Assert.IsGreaterThanOrEqualTo(240, Luma(light), "light enough to show on black and on the dimmed grey");
        Assert.IsLessThanOrEqualTo(40, Luma(dark), "dark enough to show on white");
        Assert.AreNotEqual(0u, dark & 0xFF, "the dark's blue above 0, or the platform's mask leaves it out");
    }

    private enum Kind
    {
        Clear,
        Dark,
        Light,
    }

    private static Kind KindAt(MediaFrame picture, int x, int y)
    {
        if (x < 0 || y < 0 || x >= picture.Width || y >= picture.Height)
        {
            return Kind.Clear;
        }

        var pixel = BitConverter.ToUInt32(picture.Pixels, (y * picture.Width + x) * 4);
        return pixel >> 24 == 0 ? Kind.Clear : Luma(pixel) >= 128 ? Kind.Light : Kind.Dark;
    }

    private static double Luma(uint pixel) => 0.2126 * (pixel >> 16 & 0xFF) + 0.7152 * (pixel >> 8 & 0xFF) + 0.0722 * (pixel & 0xFF);

    /// <summary>The light arms' thickness: the light pixels across the horizontal arm, in the column at the picture's left quarter.</summary>
    private static int Thickness((MediaFrame Picture, Avalonia.PixelPoint HotSpot) cross) =>
        Enumerable.Range(0, cross.Picture.Height).Count(y => KindAt(cross.Picture, cross.Picture.Width / 4, y) == Kind.Light);

    /// <summary>The dark edge's width: the dark pixels above the horizontal arm, in the column at the picture's left quarter.</summary>
    private static int Edge(MediaFrame picture) =>
        Enumerable.Range(0, picture.Height).TakeWhile(y => KindAt(picture, picture.Width / 4, y) != Kind.Light).Count(y => KindAt(picture, picture.Width / 4, y) == Kind.Dark);

    /// <summary>Whether a light pixel lies within the edge's width of (<paramref name="x"/>, <paramref name="y"/>), across an arm, beyond its end or at a corner.</summary>
    private static bool NearLight(MediaFrame picture, int x, int y)
    {
        var edge = Edge(picture);
        return Enumerable.Range(-edge, 2 * edge + 1).SelectMany(dy => Enumerable.Range(-edge, 2 * edge + 1).Select(dx => (dx, dy)))
            .Any(d => KindAt(picture, x + d.dx, y + d.dy) == Kind.Light);
    }

    /// <summary>The runs of light pixels across an arm along row or column <paramref name="line"/>: those shorter than half the side, so not the arm's own length.</summary>
    private static IEnumerable<(int Start, int End, string Across)> Runs(MediaFrame picture, int line, bool horizontal)
    {
        var length = horizontal ? picture.Width : picture.Height;
        var at = 0;
        while (at < length)
        {
            if ((horizontal ? KindAt(picture, at, line) : KindAt(picture, line, at)) != Kind.Light)
            {
                at++;
                continue;
            }

            var start = at;
            while (at < length && (horizontal ? KindAt(picture, at, line) : KindAt(picture, line, at)) == Kind.Light)
            {
                at++;
            }

            if (at - start < length / 2)
            {
                yield return (start, at - 1, horizontal ? "along a row" : "along a column");
            }
        }
    }
}
