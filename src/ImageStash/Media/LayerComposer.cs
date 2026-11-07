using System.Runtime.InteropServices;
using ImageStash.Layers;
using SkiaSharp;

namespace ImageStash.Media;

/// <summary>
/// Media's one routine drawing an original under its layers, for the editor's canvas and for the saved file alike (R4).
/// A frame is composed in Composition's order: the crop cuts the original's rectangle; hide, draw and text layers draw in
/// list order inside it, each only in the frames its range admits; each caption bar adds a white band above the cropped
/// picture holding its text (<see cref="TextLettering"/>); Shrink scales the result. It is drawn onto the caller's canvas
/// in the canvas's own coordinates, one unit per output pixel with the output's top-left at the origin, so the editor
/// scales the canvas to the screen (S10) and the render leaves it as it is. Nothing is kept between calls and no thread
/// is touched.
/// </summary>
public static class LayerComposer
{
    private const string Action = "Draw the picture with its layers";

    // L2's choices, each in the original's pixels, grown with the picture by PictureScale:
    // the three thicknesses, as pen widths.
    private const float ThinWidth = 2f, MediumWidth = 4f, ThickWidth = 8f;

    // A marker is a stroke three times its pen's width, at 40% of its color's opacity.
    private const float MarkerWidthFactor = 3f, MarkerOpacity = 0.4f;

    // An arrow's head is four of its stroke widths long and as wide as it is long.
    private const float ArrowHeadLength = 4f;

    // The blur's standard deviation.
    private const float BlurSigma = 6f;

    private static readonly SKColorSpace Srgb = SKColorSpace.CreateSrgb();

    // The composed picture is premultiplied, so scaling it filters on premultiplied pixels and a transparent pixel's
    // hidden colour never bleeds (as MediaDecoder scales).
    private static readonly SKSamplingOptions Resampling = new(SKFilterMode.Linear, SKMipmapMode.Linear);

    private static readonly Outcome<IReadOnlyList<string>> Drawn = Outcome<IReadOnlyList<string>>.Of(Array.Empty<string>());

    /// <summary>
    /// The finished picture's size for <paramref name="layers"/> over an original of this size, without drawing (G24):
    /// the crop's part of the original, plus every caption bar's band, scaled by Shrink. 0 × 0 when the crop misses the
    /// picture; a side too large to count answers <see cref="int.MaxValue"/>.
    /// </summary>
    public static PixelSize OutputSize(int originalWidth, int originalHeight, LayerList layers)
    {
        if (Picture(originalWidth, originalHeight, layers) is not { } picture)
        {
            return new PixelSize(0, 0);
        }

        return new PixelSize(Shrunk(picture.Width, layers.Shrink), Shrunk(picture.Height + BandsHeight(layers, picture.Width), layers.Shrink));
    }

    /// <summary>
    /// Where the original's pixels sit in the composition before Shrink scales it: the part of the original it shows, on
    /// the original's grid (the crop's part, the whole original without a crop), and the height of the caption bands
    /// above it, so the editor can find the composed pixel over a point of the original; null when the crop misses the
    /// picture.
    /// </summary>
    public static (PixelRect Shown, int Top)? Placement(int originalWidth, int originalHeight, LayerList layers) =>
        Picture(originalWidth, originalHeight, layers) is { } picture ? (picture, (int)Int128.Min(BandsHeight(layers, picture.Width), int.MaxValue)) : null;

    /// <summary>
    /// Each caption bar's band on the original's grid, with the bar's place in the list: as wide as the cropped picture
    /// and at its left, the first bar's directly above the picture's top, each later one above the one before (G21), so
    /// the editor finds a band where it is drawn. Empty without a bar or without a picture.
    /// </summary>
    public static IReadOnlyList<(int Index, PixelRect Band)> Bands(int originalWidth, int originalHeight, LayerList layers)
    {
        if (Picture(originalWidth, originalHeight, layers) is not { } picture)
        {
            return [];
        }

        var bands = new List<(int, PixelRect)>();
        Int128 top = picture.Y;
        for (var index = 0; index < layers.Layers.Count; index++)
        {
            if (layers.Layers[index] is CaptionBarLayer bar)
            {
                var height = TextLettering.BandHeight(bar, picture.Width);
                top -= height;
                bands.Add((index, new PixelRect(picture.X, (int)Int128.Max(top, int.MinValue), picture.Width, (int)Int128.Min(height, int.MaxValue))));
            }
        }

        return bands;
    }

    /// <summary>
    /// The size of a text layer's letters as <see cref="Compose"/> lays them out, in the original's pixels: a Meme's in
    /// uppercase wrapped at its box's width, a Label's as typed; the widest line by the lines' pitch, each at least 1.
    /// </summary>
    public static PixelSize TextSize(TextLayer layer)
    {
        using var lettering = TextLettering.Of(layer);
        return lettering.Size();
    }

    /// <summary>The colour an outline set to Auto draws in for <paramref name="fill"/>: black on a light fill, white on a dark one (G21).</summary>
    public static Rgba AutoOutline(Rgba fill) => TextLettering.AutoOutline(fill);

    /// <summary>
    /// How far a mark's ink reaches beyond the centres of its points' pixels or the edges of its rectangle, in the
    /// original's pixels: half its stroke's width, or for an arrow half the width of its head at its longest, so the
    /// editor can outline what is drawn.
    /// </summary>
    public static float InkReach(DrawMarkLayer mark, int originalWidth, int originalHeight)
    {
        var width = StrokeWidth(mark, PictureScale(originalWidth, originalHeight));
        return mark.Kind == MarkKind.Arrow ? width * ArrowHeadLength / 2 : width / 2;
    }

    /// <summary>
    /// How far the point (<paramref name="x"/>, <paramref name="y"/>) of the original's grid lies from the ink
    /// <see cref="Compose"/> strokes for <paramref name="mark"/>: 0 on it, otherwise the distance to it, in the original's
    /// pixels, so the editor selects a mark only where it is drawn (G23). A stroke's joins and ends count as round.
    /// </summary>
    public static float InkDistance(DrawMarkLayer mark, int originalWidth, int originalHeight, float x, float y)
    {
        var width = StrokeWidth(mark, PictureScale(originalWidth, originalHeight));
        var point = new SKPoint(x, y);
        switch (mark.Kind)
        {
            case MarkKind.Box:
                var box = mark.Rect!.Value;
                return Math.Max(0, ToOutline(point, SKRect.Create(box.X, box.Y, box.Width, box.Height)) - width / 2);
            case MarkKind.Arrow:
                SKPoint from = Centre(mark.Points[0]), to = Centre(mark.Points[1]);
                var length = SKPoint.Distance(from, to);
                if (length == 0)
                {
                    return Math.Max(0, SKPoint.Distance(point, to) - width / 2);
                }

                // As Arrow draws it: a shaft to the head's base and a head as wide as it is long.
                var head = Math.Min(width * ArrowHeadLength, length);
                var along = new SKPoint((to.X - from.X) / length, (to.Y - from.Y) / length);
                var across = new SKPoint(-along.Y * head / 2, along.X * head / 2);
                var bottom = new SKPoint(to.X - along.X * head, to.Y - along.Y * head);
                return Math.Min(Math.Max(0, ToSegment(point, from, bottom) - width / 2), ToTriangle(point, to, bottom + across, bottom - across));
            default:
                var nearest = float.MaxValue;
                for (var i = 0; i < mark.Points.Count; i++)
                {
                    nearest = Math.Min(nearest, ToSegment(point, Centre(mark.Points[i]), Centre(mark.Points[Math.Min(i + 1, mark.Points.Count - 1)])));
                }

                return Math.Max(0, nearest - width / 2);
        }
    }

    /// <summary>
    /// Draws <paramref name="original"/>, a full-size frame of the original in the boundary pixel contract, under
    /// <paramref name="layers"/> onto <paramref name="canvas"/>, filling <see cref="OutputSize"/> canvas units;
    /// <paramref name="frameIndex"/> is the frame's index on the original's timeline, against which each range is judged
    /// as written (G22). Done, it answers the font families it drew in a fallback, each once in list order, so the editor
    /// says so (G21); a missing family is never a failure. A failure names <paramref name="name"/> and is never thrown (G35).
    /// </summary>
    public static Outcome<IReadOnlyList<string>> Compose(SKCanvas canvas, MediaFrame original, LayerList layers, int frameIndex, string name)
    {
        if (original.Width < 1 || original.Height < 1 || original.Pixels.LongLength != 4L * original.Width * original.Height)
        {
            return new Failure(Action, name, "its pixels do not fill its size");
        }

        if (Picture(original.Width, original.Height, layers) is not { } picture)
        {
            // A crop that misses the picture leaves nothing to draw.
            return Drawn;
        }

        // Whoever reads the composition back holds it in the boundary pixel contract: one array of at most this many bytes.
        var bands = BandsHeight(layers, picture.Width);
        var noMemory = new Failure(Action, name, "there is not enough memory to draw it");
        if (4 * (Int128)picture.Width * (picture.Height + bands) > Array.MaxLength)
        {
            return noMemory;
        }

        try
        {
            // The whole frame is composed in memory before it is scaled (G23: what a box covers never reaches a
            // neighbouring pixel through a filter). Rectangles are filled, copied and blurred by whole pixels, so they land
            // exactly at any size; only the marks' strokes are placed in Skia's floating-point coordinates.
            var top = (int)bands;
            using var composition = Allocate(new SKImageInfo(picture.Width, picture.Height + top, SKColorType.Bgra8888, SKAlphaType.Premul, Srgb));
            using var whole = composition?.PeekPixels();
            using var area = whole?.ExtractSubset(SKRectI.Create(0, top, picture.Width, picture.Height));
            using var areaBitmap = new SKBitmap();
            if (whole is null || area is null || !Copy(original, picture, area) || !areaBitmap.InstallPixels(area))
            {
                return noMemory;
            }

            // Every caption bar's band is white (G21); they stack above the picture in list order, the first lowest.
            if (top > 0)
            {
                whole.Erase(SKColors.White, SKRectI.Create(0, 0, picture.Width, top));
            }

            // Each black box on this frame blacks out what it covers before any layer is drawn, so a blur listed below it
            // has nothing of those pixels to spread (G23); it is filled again at its place, over the marks below it.
            foreach (var hide in layers.Layers.OfType<HideLayer>().Where(hide => hide.Kind == HideKind.Black && Admits(hide.Range, frameIndex)))
            {
                Black(area, picture, hide.Rect);
            }

            using var drawing = new SKCanvas(areaBitmap);

            // Layers are on the original's grid: the crop offsets the canvas, never a layer (G22). A caption's letters
            // are drawn on the whole composition, each in its own band, the first band lowest.
            drawing.Translate(-picture.X, -picture.Y);
            using var wholeBitmap = new SKBitmap();
            using var captions = top > 0 && wholeBitmap.InstallPixels(whole) ? new SKCanvas(wholeBitmap) : null;
            var bandBottom = top;
            var scale = PictureScale(original.Width, original.Height);
            var replaced = new List<string>();
            foreach (var layer in layers.Layers)
            {
                var drawn = layer switch
                {
                    HideLayer { Kind: HideKind.Black } hide when Admits(hide.Range, frameIndex) => Black(area, picture, hide.Rect),
                    HideLayer hide when Admits(hide.Range, frameIndex) => Blur(area, picture, hide.Rect, BlurSigma * scale),
                    DrawMarkLayer mark when Admits(mark.Range, frameIndex) => Mark(drawing, mark, scale),
                    TextLayer text when Admits(text.Range, frameIndex) => TextLettering.DrawText(drawing, text, replaced),
                    CaptionBarLayer bar => captions is not null && TextLettering.DrawCaption(captions, bar, picture.Width, ref bandBottom, replaced),
                    _ => true,
                };
                if (!drawn)
                {
                    return noMemory;
                }
            }

            using var composed = SKImage.FromPixels(whole);
            if (composed is null)
            {
                return noMemory;
            }

            var output = OutputSize(original.Width, original.Height, layers);
            canvas.Save();
            canvas.Scale(output.Width / (float)whole.Width, output.Height / (float)whole.Height);
            canvas.DrawImage(composed, 0, 0, Resampling, null);
            canvas.Restore();
            return replaced.Count == 0 ? Drawn : Outcome<IReadOnlyList<string>>.Of(replaced);
        }
        catch (OutOfMemoryException)
        {
            return noMemory;
        }
    }

    private static bool Admits(FrameRange? range, int frameIndex) => range?.Admits(frameIndex) ?? true;

    /// <summary>The crop's part of the original (the whole original without one), or null when nothing of it is left.</summary>
    private static PixelRect? Picture(int width, int height, LayerList layers)
    {
        var crop = layers.Crop?.Rect ?? new PixelRect(0, 0, width, height);
        return OnPicture(crop, new PixelRect(0, 0, width, height)) is { } part ? new PixelRect(part.Left, part.Top, part.Width, part.Height) : null;
    }

    /// <summary>
    /// The caption bars' bands together over a cropped picture <paramref name="width"/> pixels wide, each as high as its
    /// laid-out lines need (<see cref="TextLettering.BandHeight"/>); counted in 128 bits, so no list overflows it.
    /// </summary>
    private static Int128 BandsHeight(LayerList layers, int width) => layers.Layers.OfType<CaptionBarLayer>()
        .Aggregate(Int128.Zero, (sum, bar) => sum + TextLettering.BandHeight(bar, width));

    /// <summary>Shrink's one rounding rule (L2): a side scaled to the nearest pixel, halves up, never below one.</summary>
    private static int Shrunk(Int128 side, Shrink shrink)
    {
        var scaled = shrink switch
        {
            Shrink.ThreeQuarter => (side * 3 + 2) / 4,
            Shrink.Half => (side + 1) / 2,
            _ => side,
        };
        return (int)Int128.Clamp(scaled, 1, int.MaxValue);
    }

    /// <summary>
    /// How much L2's widths and the blur grow: 1 on a picture up to 800 pixels on its longer side, in proportion beyond,
    /// so a mark keeps its weight on a large screenshot fitted in the window.
    /// </summary>
    private static float PictureScale(int originalWidth, int originalHeight) => Math.Max(1f, Math.Max(originalWidth, originalHeight) / 800f);

    /// <summary>The crop's rectangle of <paramref name="original"/>, premultiplied, into <paramref name="area"/>.</summary>
    private static bool Copy(MediaFrame original, PixelRect picture, SKPixmap area)
    {
        var pinned = GCHandle.Alloc(original.Pixels, GCHandleType.Pinned);
        try
        {
            using var source = new SKPixmap(
                new SKImageInfo(original.Width, original.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul, Srgb), pinned.AddrOfPinnedObject());
            return source.ReadPixels(area, picture.X, picture.Y);
        }
        finally
        {
            pinned.Free();
        }
    }

    /// <summary>The part of <paramref name="rect"/> on <paramref name="picture"/>, in the picture's own pixels, or null.</summary>
    private static SKRectI? OnPicture(PixelRect rect, PixelRect picture)
    {
        long left = Math.Max(rect.X, picture.X), top = Math.Max(rect.Y, picture.Y);
        long right = Math.Min((long)rect.X + rect.Width, (long)picture.X + picture.Width);
        long bottom = Math.Min((long)rect.Y + rect.Height, (long)picture.Y + picture.Height);
        return right > left && bottom > top
            ? SKRectI.Create((int)(left - picture.X), (int)(top - picture.Y), (int)(right - left), (int)(bottom - top))
            : null;
    }

    /// <summary>An opaque black fill of exactly the rectangle's pixels, so nothing of what it covers survives (G23).</summary>
    private static bool Black(SKPixmap area, PixelRect picture, PixelRect rect)
    {
        if (OnPicture(rect, picture) is { } covered)
        {
            area.Erase(SKColors.Black, covered);
        }

        return true;
    }

    /// <summary>
    /// What is drawn so far inside the rectangle, the original and every lower layer, blurred and put back in its place;
    /// sampled only inside it, so a blur over a black box stays black, and clamped at its edges, so an opaque picture
    /// stays opaque (G23).
    /// </summary>
    private static bool Blur(SKPixmap area, PixelRect picture, PixelRect rect, float sigma)
    {
        if (OnPicture(rect, picture) is not { } covered)
        {
            return true;
        }

        using var region = area.ExtractSubset(covered);
        using var drawn = region is null ? null : SKImage.FromPixelCopy(region);
        using var blurred = region is null ? null : Allocate(region.Info);
        using var filter = SKImageFilter.CreateBlur(sigma, sigma, SKShaderTileMode.Clamp);
        if (region is null || drawn is null || blurred is null || filter is null)
        {
            return false;
        }

        using (var canvas = new SKCanvas(blurred))
        using (var paint = new SKPaint { ImageFilter = filter, BlendMode = SKBlendMode.Src })
        {
            canvas.Clear(SKColors.Transparent);
            canvas.DrawImage(drawn, 0, 0, paint);
        }

        using var result = blurred.PeekPixels();
        return result.ReadPixels(region);
    }

    private static SKBitmap? Allocate(SKImageInfo info)
    {
        var bitmap = new SKBitmap();
        if (bitmap.TryAllocPixels(info))
        {
            return bitmap;
        }

        bitmap.Dispose();
        return null;
    }

    /// <summary>The width a mark is stroked with on a picture whose widths grow by <paramref name="scale"/>; a marker's is wider.</summary>
    private static float StrokeWidth(DrawMarkLayer mark, float scale)
    {
        var width = scale * mark.Thickness switch
        {
            Thickness.Thin => ThinWidth,
            Thickness.Medium => MediumWidth,
            _ => ThickWidth,
        };
        return mark.Kind == MarkKind.Marker ? width * MarkerWidthFactor : width;
    }

    private static bool Mark(SKCanvas canvas, DrawMarkLayer mark, float scale)
    {
        var width = StrokeWidth(mark, scale);
        var color = new SKColor(mark.Color.R, mark.Color.G, mark.Color.B, mark.Color.A);
        using var paint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = width,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
            Color = color,
        };
        switch (mark.Kind)
        {
            case MarkKind.Box:
                var box = mark.Rect!.Value;
                paint.StrokeJoin = SKStrokeJoin.Miter;
                canvas.DrawRect(SKRect.Create(box.X, box.Y, box.Width, box.Height), paint);
                break;
            case MarkKind.Arrow:
                Arrow(canvas, Centre(mark.Points[0]), Centre(mark.Points[1]), paint);
                break;
            default:
                if (mark.Kind == MarkKind.Marker)
                {
                    paint.Color = color.WithAlpha((byte)Math.Round(mark.Color.A * MarkerOpacity));
                }

                // One path, so a stroke that crosses itself is not darker where it does.
                using (var path = new SKPath())
                {
                    path.MoveTo(Centre(mark.Points[0]));
                    foreach (var point in mark.Points.Skip(1))
                    {
                        path.LineTo(Centre(point));
                    }

                    canvas.DrawPath(path, paint);
                }

                break;
        }

        return true;
    }

    /// <summary>A shaft from <paramref name="from"/> to the head's base and a filled head whose tip is <paramref name="to"/>.</summary>
    private static void Arrow(SKCanvas canvas, SKPoint from, SKPoint to, SKPaint paint)
    {
        var length = SKPoint.Distance(from, to);
        if (length == 0)
        {
            canvas.DrawPoint(to, paint);
            return;
        }

        var head = Math.Min(paint.StrokeWidth * ArrowHeadLength, length);
        var along = new SKPoint((to.X - from.X) / length, (to.Y - from.Y) / length);
        var across = new SKPoint(-along.Y * head / 2, along.X * head / 2);
        var bottom = new SKPoint(to.X - along.X * head, to.Y - along.Y * head);

        // A butt end meets the head's base without overlapping it, so a translucent arrow is even throughout.
        paint.StrokeCap = SKStrokeCap.Butt;
        canvas.DrawLine(from, bottom, paint);
        using var path = new SKPath();
        path.MoveTo(to);
        path.LineTo(bottom + across);
        path.LineTo(bottom - across);
        path.Close();
        paint.Style = SKPaintStyle.Fill;
        canvas.DrawPath(path, paint);
    }

    /// <summary>A point of a stroke at the centre of its pixel.</summary>
    private static SKPoint Centre(PixelPoint point) => new(point.X + 0.5f, point.Y + 0.5f);

    private static float ToSegment(SKPoint point, SKPoint a, SKPoint b)
    {
        var span = b - a;
        var squared = span.X * span.X + span.Y * span.Y;
        var t = squared == 0 ? 0 : Math.Clamp(((point.X - a.X) * span.X + (point.Y - a.Y) * span.Y) / squared, 0, 1);
        return SKPoint.Distance(point, new SKPoint(a.X + span.X * t, a.Y + span.Y * t));
    }

    /// <summary>The distance from <paramref name="point"/> to the edges of <paramref name="rect"/>, from inside or outside.</summary>
    private static float ToOutline(SKPoint point, SKRect rect)
    {
        if (rect.Contains(point))
        {
            return Math.Min(Math.Min(point.X - rect.Left, rect.Right - point.X), Math.Min(point.Y - rect.Top, rect.Bottom - point.Y));
        }

        var dx = Math.Max(Math.Max(rect.Left - point.X, 0), point.X - rect.Right);
        var dy = Math.Max(Math.Max(rect.Top - point.Y, 0), point.Y - rect.Bottom);
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>0 inside the triangle, else the distance to its nearest edge.</summary>
    private static float ToTriangle(SKPoint point, SKPoint a, SKPoint b, SKPoint c)
    {
        static float Side(SKPoint p, SKPoint from, SKPoint to) => (to.X - from.X) * (p.Y - from.Y) - (to.Y - from.Y) * (p.X - from.X);
        float ab = Side(point, a, b), bc = Side(point, b, c), ca = Side(point, c, a);
        if ((ab >= 0 && bc >= 0 && ca >= 0) || (ab <= 0 && bc <= 0 && ca <= 0))
        {
            return 0;
        }

        return Math.Min(ToSegment(point, a, b), Math.Min(ToSegment(point, b, c), ToSegment(point, c, a)));
    }
}
