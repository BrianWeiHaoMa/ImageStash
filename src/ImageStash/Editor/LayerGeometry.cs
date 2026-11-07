using System.Drawing;
using ImageStash.Layers;
using ImageStash.Media;

namespace ImageStash.Editor;

/// <summary>A handle of the selected layer: a corner of a hide, a box, a text's box or a caption's band, an end of an arrow, or the round handle that turns a text layer.</summary>
public enum Handle
{
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
    Start,
    End,
    Rotate,
}

/// <summary>
/// Where the layers the editor selects lie and how a drag changes them, on the original's grid (What crosses every
/// boundary): a point is fractional, pixel (x, y) covering x to x + 1 and y to y + 1, while every layer made or changed
/// here carries whole pixels inside the original and a rectangle at least 1 × 1, so the model never refuses it. A hide
/// is under a press anywhere over its rectangle, and a mark only where Media inks it (G23: the mark under the click),
/// so a drag starting in the open space beside or inside a mark draws a new one. A hide is moved or resized by its
/// corners; a mark is only moved, its handles showing the selection (G23). A text layer is under a press anywhere over
/// its box turned with it, is moved, resized by its corners and turned by its round handle; a caption bar is under a
/// press over its band, which Media places, and only resized by its band's corners (G21). The view outlines a selected
/// layer by <see cref="Bounds"/>, turned by <see cref="Rotation"/>, and draws <see cref="Handles"/>, already turned.
/// </summary>
public static class LayerGeometry
{
    // L2: the round handle sits above a text's box by three quarters of its font size, so it looks the same at any size.
    private const float RotateGapOfSize = 0.75f;

    /// <summary>Whether the editor selects layers of this kind: the hide, draw, text and caption-bar layers it makes.</summary>
    public static bool Selectable(Layer layer) => layer is HideLayer or DrawMarkLayer or TextLayer or CaptionBarLayer;

    /// <summary>
    /// The rectangle around what the layer at <paramref name="index"/> of <paramref name="layers"/> draws, for the view's
    /// outline of the selection: a hide's rectangle; a box's grown by its ink; a stroke's or an arrow's points' pixel
    /// centres grown by its ink; a text layer's box before its rotation; a caption bar's band. Null for a kind
    /// the editor does not select.
    /// </summary>
    public static RectangleF? Bounds(LayerList layers, int index, PixelSize original) => layers.Layers[index] switch
    {
        HideLayer hide => Of(hide.Rect),
        DrawMarkLayer { Kind: MarkKind.Box } box => Grown(Of(box.Rect!.Value), LayerComposer.InkReach(box, original.Width, original.Height)),
        DrawMarkLayer stroke => Grown(Centres(stroke.Points), LayerComposer.InkReach(stroke, original.Width, original.Height)),
        TextLayer text => Of(text.Box),
        CaptionBarLayer => Band(layers, index, original) is { } band ? Of(band) : null,
        _ => null,
    };

    /// <summary>A text layer's rotation in degrees clockwise about its box's centre; 0 for every other kind, which never turns.</summary>
    public static double Rotation(Layer layer) => layer is TextLayer text ? text.Rotation : 0;

    /// <summary>
    /// Whether a press at <paramref name="point"/> is on the layer at <paramref name="index"/>: within
    /// <paramref name="tolerance"/> of a hide's rectangle, of a mark's ink or of a text layer's box as it is turned, each
    /// only over the shown picture, which Media clips them to, so a press beside a cropped picture is on none of what it
    /// cut away (G23: the mark under the click) and a caption's band above it stays the caption's; or of a caption bar's band.
    /// </summary>
    public static bool Hits(LayerList layers, int index, PointF point, float tolerance, PixelSize original) => layers.Layers[index] switch
    {
        HideLayer or DrawMarkLayer or TextLayer when Shown(layers, original) is not { } shown || !Within(Of(shown), point) => false,
        // A hide, mark or text the crop cut away whole draws nothing, so a press near it, within the tolerance, is not on it.
        HideLayer or DrawMarkLayer or TextLayer when !Overlap(Of(Shown(layers, original)!.Value), Reach(layers, index, original)) => false,
        HideLayer hide => Over(hide.Rect, point, tolerance),
        DrawMarkLayer mark => LayerComposer.InkDistance(mark, original.Width, original.Height, point.X, point.Y) <= tolerance,
        TextLayer text => Within(Grown(Of(text.Box), tolerance), TurnedAbout(point, Centre(text.Box), -text.Rotation)),
        CaptionBarLayer => Band(layers, index, original) is { } band && Within(Grown(Of(band), tolerance), point),
        _ => false,
    };

    /// <summary>Whether a drag from one of the selected layer's corners resizes it: a hide's, a text layer's and a caption bar's do; a mark is only moved (G21, G23).</summary>
    public static bool Resizes(Layer layer) => layer is HideLayer or TextLayer or CaptionBarLayer;

    /// <summary>The index of the topmost selectable layer a press at <paramref name="point"/> is on, the latest in the list first; null when none.</summary>
    public static int? TopmostAt(LayerList layers, PointF point, float tolerance, PixelSize original)
    {
        for (var i = layers.Layers.Count - 1; i >= 0; i--)
        {
            if (Hits(layers, i, point, tolerance, original))
            {
                return i;
            }
        }

        return null;
    }

    /// <summary>
    /// The handles of the layer at <paramref name="index"/> where they lie: a hide's or a box's four corners, an arrow's
    /// two ends; a text layer's four corners and its round handle above the top's centre, all turned with it about its
    /// box's centre; a caption bar's band's four corners; a stroke has none. A hide's, a text layer's and a caption bar's
    /// corners resize it (<see cref="Resizes"/>); a mark's show it is selected. Given the <paramref name="room"/> where the
    /// view can show and press a handle, a text's round handle that would lie beyond it, and its corners when none lies in
    /// it, are pulled back along their lines from the box's centre to the room's edge, so a text grown large at the
    /// picture's edge, or turned, can still be turned and resized (G21).
    /// </summary>
    public static IReadOnlyList<(Handle Handle, PointF At)> Handles(LayerList layers, int index, PixelSize original, RectangleF? room = null) => layers.Layers[index] switch
    {
        HideLayer hide => Corners(hide.Rect),
        DrawMarkLayer { Kind: MarkKind.Box } box => Corners(box.Rect!.Value),
        DrawMarkLayer { Kind: MarkKind.Arrow } arrow => [(Handle.Start, Centre(arrow.Points[0])), (Handle.End, Centre(arrow.Points[1]))],
        TextLayer text => TextHandles(text, room),
        CaptionBarLayer => Band(layers, index, original) is { } band ? Corners(band) : [],
        _ => [],
    };

    /// <summary>
    /// The handle of the layer at <paramref name="index"/>, placed as <see cref="Handles"/> places it in <paramref name="room"/>,
    /// nearest <paramref name="point"/> within <paramref name="tolerance"/>, of two as near the later listed, which the view
    /// draws over the other (a round handle pulled onto a corner); or null. Inside the layer's box, turned with a text, a
    /// handle is only taken nearer than the box's centre, so a layer smaller than the tolerance still moves from its
    /// middle while its corners resize it (G21, G23).
    /// </summary>
    public static Handle? HandleAt(LayerList layers, int index, PointF point, float tolerance, PixelSize original, RectangleF? room = null) =>
        Nearest(Handles(layers, index, original, room), point, tolerance, Middle(layers, index, original, point));

    /// <summary>The corner of <paramref name="rect"/> nearest <paramref name="point"/> within <paramref name="tolerance"/>, as <see cref="HandleAt"/> finds a hide's; or null.</summary>
    internal static Handle? CornerAt(PixelRect rect, PointF point, float tolerance) =>
        Nearest(Corners(rect), point, tolerance, Within(Of(rect), point) ? Centre(rect) : null);

    /// <summary>Whether <paramref name="point"/> lies on <paramref name="rect"/> or within <paramref name="tolerance"/> of it, as a press on a hide does.</summary>
    internal static bool Over(PixelRect rect, PointF point, float tolerance) => Within(Grown(Of(rect), tolerance), point);

    /// <summary>
    /// The whole pixels a drag from <paramref name="from"/> to <paramref name="to"/> covers, either way round, kept inside
    /// <paramref name="picture"/>, the part of the original the picture shows (<see cref="Picture"/>).
    /// </summary>
    public static PixelRect Covering(PointF from, PointF to, PixelRect picture)
    {
        var (left, right) = Span((int)MathF.Floor(MathF.Min(from.X, to.X)), (int)MathF.Ceiling(MathF.Max(from.X, to.X)), picture.X, picture.X + picture.Width);
        var (top, bottom) = Span((int)MathF.Floor(MathF.Min(from.Y, to.Y)), (int)MathF.Ceiling(MathF.Max(from.Y, to.Y)), picture.Y, picture.Y + picture.Height);
        return new PixelRect(left, top, right - left, bottom - top);
    }

    /// <summary>
    /// The pixel under <paramref name="point"/>, or the nearest pixel of <paramref name="picture"/>, the part of the
    /// original the picture shows, when the point is off it.
    /// </summary>
    public static PixelPoint PixelUnder(PointF point, PixelRect picture) => new(
        Math.Clamp((int)MathF.Floor(point.X), picture.X, picture.X + picture.Width - 1),
        Math.Clamp((int)MathF.Floor(point.Y), picture.Y, picture.Y + picture.Height - 1));

    /// <summary>
    /// The part of the original the picture shows, where a new layer is drawn so that it shows: the crop's part, or the
    /// whole original without a crop or when the crop misses it.
    /// </summary>
    public static PixelRect Picture(LayerList layers, PixelSize original) => Shown(layers, original) ?? new PixelRect(0, 0, original.Width, original.Height);

    /// <summary>
    /// <paramref name="layer"/> moved by whole pixels, as far as it stays inside the original, a text layer as far as its
    /// box turned with it does (<see cref="TextPlacement"/>), its size, shape, color and range kept; a caption bar, which
    /// sits above the picture (G21), and a kind the editor does not select come back as they are.
    /// </summary>
    public static Layer Moved(Layer layer, int dx, int dy, PixelSize original) => layer switch
    {
        HideLayer hide => hide with { Rect = Shifted(hide.Rect, dx, dy, original) },
        DrawMarkLayer { Kind: MarkKind.Box } box => box with { Rect = Shifted(box.Rect!.Value, dx, dy, original) },
        DrawMarkLayer stroke => stroke with { Points = Shifted(stroke.Points, dx, dy, original) },
        TextLayer text => text with { Box = TextPlacement.Shifted(text.Box, text.Rotation, dx, dy, original) },
        _ => layer,
    };

    /// <summary>
    /// The layer at <paramref name="index"/> with the corner <paramref name="handle"/> names dragged by whole pixels, the
    /// opposite one kept. A hide's corner dragged past the far one turns the rectangle round rather than giving it a size
    /// below 1 × 1 or a place outside the original. A text layer's and a caption bar's font size scales by how far the
    /// corner goes along the diagonal from the opposite corner (G21: the corner is the one way to a bigger text), never
    /// below 1: a Meme's box width scales with it, a text layer's box then follows its letters (<see cref="Measured"/>) with
    /// the opposite corner where it was, and a caption's band keeps the picture's width and grows with its lines. Any
    /// other layer or handle leaves it as it is.
    /// </summary>
    public static Layer Resized(LayerList layers, int index, Handle handle, int dx, int dy, PixelSize original) => (layers.Layers[index], handle) switch
    {
        (HideLayer hide, < Handle.Start) => hide with { Rect = Stretched(hide.Rect, handle, dx, dy, original) },
        (TextLayer text, < Handle.Start) => Scaled(text, handle, dx, dy, original),
        (CaptionBarLayer bar, < Handle.Start) when Band(layers, index, original) is { } band => bar with { FontSize = ScaledSize(bar.FontSize, Factor(band, handle, dx, dy, 0)) },
        var (layer, _) => layer,
    };

    /// <summary>
    /// <paramref name="mark"/> as a mark of <paramref name="kind"/> over the same pixels: a pen and a marker share their
    /// points; an arrow runs from a stroke's first point to its last, or across a box from corner to corner; a box covers
    /// the pixels of a stroke's or an arrow's points; a stroke goes round a box's edge.
    /// </summary>
    public static DrawMarkLayer Reshaped(DrawMarkLayer mark, MarkKind kind)
    {
        if (kind == mark.Kind)
        {
            return mark;
        }

        if (mark.Kind == MarkKind.Box)
        {
            var box = mark.Rect!.Value;
            PixelPoint topLeft = new(box.X, box.Y), bottomRight = new(box.X + box.Width - 1, box.Y + box.Height - 1);
            PixelPoint[] points = kind == MarkKind.Arrow
                ? [topLeft, bottomRight]
                : [topLeft, new(bottomRight.X, topLeft.Y), bottomRight, new(topLeft.X, bottomRight.Y), topLeft];
            return mark with { Kind = kind, Rect = null, Points = points };
        }

        return kind switch
        {
            MarkKind.Box => mark with
            {
                Kind = kind,
                Points = [],
                Rect = new PixelRect(
                    mark.Points.Min(p => p.X),
                    mark.Points.Min(p => p.Y),
                    mark.Points.Max(p => p.X) - mark.Points.Min(p => p.X) + 1,
                    mark.Points.Max(p => p.Y) - mark.Points.Min(p => p.Y) + 1),
            },
            MarkKind.Arrow => mark with { Kind = kind, Points = [mark.Points[0], mark.Points[^1]] },
            _ => mark with { Kind = kind },
        };
    }

    /// <summary>
    /// <paramref name="layer"/>, a text layer, turned so that its round handle points from its box's centre towards
    /// <paramref name="point"/>, the handle's place above the centre being 0° and a quarter turn clockwise 90°, in −180 …
    /// 180; to the nearest multiple of 15° when <paramref name="snapped"/> (G21: Shift held). Its box stays where it is: its
    /// centre, which it turns about, lies on the picture (<see cref="TextPlacement"/>), so it is never turned out of reach.
    /// A point on the centre gives no direction and leaves it as it is, as it does any other kind.
    /// </summary>
    public static Layer Turned(Layer layer, PointF point, bool snapped)
    {
        if (layer is not TextLayer text)
        {
            return layer;
        }

        var centre = Centre(text.Box);
        double dx = point.X - centre.X, dy = point.Y - centre.Y;
        // Atan2 of a zero vector answers 0 or 180 by the zeros' signs, a direction nobody pointed in.
        if (dx == 0 && dy == 0 || !double.IsFinite(dx) || !double.IsFinite(dy))
        {
            return layer;
        }

        var degrees = Math.Atan2(dx, -dy) * 180 / Math.PI;
        if (snapped)
        {
            degrees = Math.Round(degrees / 15, MidpointRounding.AwayFromZero) * 15;
        }

        return text with { Rotation = degrees <= -180 ? degrees + 360 : degrees };
    }

    /// <summary>
    /// <paramref name="text"/> with its box following its letters as Media lays them out (<see cref="LayerComposer.TextSize"/>):
    /// a Meme keeps its width and takes the measured height, keeping the edge of the picture <paramref name="layers"/> show
    /// that it holds to (<see cref="HoldsBottom"/>) where it is, so a bottom Meme grows and shrinks upward and stays at the
    /// bottom (G21); a Label takes the measured width and height from its top-left; then moved only as far as keeps its
    /// box, turned with it, inside the original (<see cref="TextPlacement.Kept"/>).
    /// </summary>
    public static TextLayer Measured(TextLayer text, LayerList layers, PixelSize original)
    {
        var (box, fitted) = (text.Box, Fitted(text));
        var y = text.Preset == TextPreset.Meme && Shown(layers, original) is { } shown && HoldsBottom(box, shown)
            ? (int)Math.Clamp((long)box.Y + box.Height - fitted.Height, int.MinValue, int.MaxValue)
            : box.Y;
        return text with { Box = TextPlacement.Kept(new PixelRect(box.X, y, fitted.Width, fitted.Height), text.Rotation, original) };
    }

    /// <summary>
    /// Whether a Meme's <paramref name="box"/> holds to the bottom of the <paramref name="shown"/> picture rather than its
    /// top: the edge it is nearer, but a box sticking out past one edge only holds to the other, so a bottom Meme grown
    /// above a cropped picture's top still shrinks back to the bottom (G21).
    /// </summary>
    private static bool HoldsBottom(PixelRect box, PixelRect shown)
    {
        long above = (long)box.Y - shown.Y, below = (long)shown.Y + shown.Height - ((long)box.Y + box.Height);
        return above < 0 != below < 0 ? above < 0 : below < above;
    }

    /// <summary>A caption bar's band, as Media places it above the picture (<see cref="LayerComposer.Bands"/>); null without one.</summary>
    internal static PixelRect? Band(LayerList layers, int index, PixelSize original)
    {
        foreach (var (at, band) in LayerComposer.Bands(original.Width, original.Height, layers))
        {
            if (at == index)
            {
                return band;
            }
        }

        return null;
    }

    /// <summary>The part of the original the picture shows (the crop's, else all of it), or null when the crop misses it.</summary>
    internal static PixelRect? Shown(LayerList layers, PixelSize original) => LayerComposer.Placement(original.Width, original.Height, layers)?.Shown;

    private static RectangleF Of(PixelRect rect) => new(rect.X, rect.Y, rect.Width, rect.Height);

    private static RectangleF Grown(RectangleF rect, float by) => RectangleF.FromLTRB(rect.Left - by, rect.Top - by, rect.Right + by, rect.Bottom + by);

    /// <summary>The rectangle round what a hide, mark or text layer draws: its <see cref="Bounds"/>, a text's box as it is turned.</summary>
    private static RectangleF Reach(LayerList layers, int index, PixelSize original) =>
        layers.Layers[index] is TextLayer text ? TextPlacement.Bounds(text.Box, text.Rotation) : Bounds(layers, index, original)!.Value;

    /// <summary>Whether <paramref name="a"/> and <paramref name="b"/> share some area, more than an edge.</summary>
    private static bool Overlap(RectangleF a, RectangleF b) => a.Left < b.Right && b.Left < a.Right && a.Top < b.Bottom && b.Top < a.Bottom;

    /// <summary>Whether <paramref name="point"/> lies inside <paramref name="rect"/> or on any of its edges.</summary>
    private static bool Within(RectangleF rect, PointF point) =>
        point.X >= rect.Left && point.X <= rect.Right && point.Y >= rect.Top && point.Y <= rect.Bottom;

    /// <summary>A stroke's point is drawn at the centre of its pixel (Media), so its bounds are those of the centres.</summary>
    private static RectangleF Centres(IReadOnlyList<PixelPoint> points) =>
        RectangleF.FromLTRB(points.Min(p => p.X) + 0.5f, points.Min(p => p.Y) + 0.5f, points.Max(p => p.X) + 0.5f, points.Max(p => p.Y) + 0.5f);

    private static PointF Centre(PixelPoint point) => new(point.X + 0.5f, point.Y + 0.5f);

    /// <summary>The exact centre of a box, half pixels kept, which Media turns a text layer about.</summary>
    private static PointF Centre(PixelRect box) => new(box.X + box.Width / 2f, box.Y + box.Height / 2f);

    /// <summary><paramref name="point"/> turned about <paramref name="centre"/> by <paramref name="degrees"/> clockwise on the grid, whose y runs down.</summary>
    private static PointF TurnedAbout(PointF point, PointF centre, double degrees)
    {
        var (sin, cos) = Math.SinCos(degrees * Math.PI / 180);
        double x = point.X - centre.X, y = point.Y - centre.Y;
        return new PointF((float)(centre.X + x * cos - y * sin), (float)(centre.Y + x * sin + y * cos));
    }

    private static (Handle Handle, PointF At)[] Corners(PixelRect rect) =>
    [
        (Handle.TopLeft, new(rect.X, rect.Y)),
        (Handle.TopRight, new(rect.X + rect.Width, rect.Y)),
        (Handle.BottomLeft, new(rect.X, rect.Y + rect.Height)),
        (Handle.BottomRight, new(rect.X + rect.Width, rect.Y + rect.Height)),
    ];

    private static (Handle Handle, PointF At)[] TextHandles(TextLayer text, RectangleF? room)
    {
        var centre = Centre(text.Box);
        var rotate = TurnedAbout(new PointF(centre.X, text.Box.Y - RotateGapOfSize * text.FontSize), centre, text.Rotation);
        var corners = Corners(text.Box).Select(corner => (corner.Handle, At: TurnedAbout(corner.At, centre, text.Rotation))).ToArray();
        if (room is not { } within)
        {
            return [.. corners, (Handle.Rotate, rotate)];
        }

        // A corner on its line from the centre still resizes along that diagonal (Factor), so one pulled in works as the corner.
        return [.. corners.Any(corner => Within(within, corner.At)) ? corners : corners.Select(corner => (corner.Handle, PulledInto(within, centre, corner.At))), (Handle.Rotate, PulledInto(within, centre, rotate))];
    }

    /// <summary>
    /// <paramref name="point"/> where it lies in <paramref name="room"/>, else where the line to it from
    /// <paramref name="from"/> leaves the room, or the room's nearest point when <paramref name="from"/> lies outside it too.
    /// </summary>
    private static PointF PulledInto(RectangleF room, PointF from, PointF point)
    {
        if (Within(room, point))
        {
            return point;
        }

        if (!Within(room, from))
        {
            return new PointF(Math.Clamp(point.X, room.Left, room.Right), Math.Clamp(point.Y, room.Top, room.Bottom));
        }

        var along = Math.Min(Inside(from.X, point.X, room.Left, room.Right), Inside(from.Y, point.Y, room.Top, room.Bottom));
        return new PointF(from.X + (point.X - from.X) * along, from.Y + (point.Y - from.Y) * along);
    }

    /// <summary>How far from <paramref name="from"/> towards <paramref name="to"/>, as a fraction of the way, a coordinate stays within <paramref name="low"/> … <paramref name="high"/>.</summary>
    private static float Inside(float from, float to, float low, float high) =>
        to < low ? (low - from) / (to - from) : to > high ? (high - from) / (to - from) : 1;

    private static float Square(float value) => value * value;

    private static float Distance(PointF a, PointF b) => MathF.Sqrt(Square(a.X - b.X) + Square(a.Y - b.Y));

    /// <summary>The centre of the layer's box, turned with a text, when <paramref name="point"/> lies inside it; else null.</summary>
    private static PointF? Middle(LayerList layers, int index, PixelSize original, PointF point) => layers.Layers[index] switch
    {
        TextLayer text => Within(Of(text.Box), TurnedAbout(point, Centre(text.Box), -text.Rotation)) ? Centre(text.Box) : null,
        _ => Bounds(layers, index, original) is { } box && Within(box, point) ? new PointF(box.X + box.Width / 2, box.Y + box.Height / 2) : null,
    };

    /// <summary>
    /// The handle nearest <paramref name="point"/> within <paramref name="tolerance"/>, of two as near the later listed, which
    /// the view draws over the other, and nearer than <paramref name="middle"/>, the centre of the box the point lies inside; or null.
    /// </summary>
    private static Handle? Nearest(IReadOnlyList<(Handle Handle, PointF At)> handles, PointF point, float tolerance, PointF? middle) =>
        handles
            .Select((handle, order) => (handle.Handle, Order: order, Distance: Distance(handle.At, point)))
            .Where(handle => handle.Distance <= tolerance && (middle is not { } centre || handle.Distance < Distance(centre, point)))
            .OrderBy(handle => handle.Distance)
            .ThenByDescending(handle => handle.Order)
            .Select(handle => (Handle?)handle.Handle)
            .FirstOrDefault();

    /// <summary>
    /// How far a corner of <paramref name="box"/> dragged by (<paramref name="dx"/>, <paramref name="dy"/>) goes along
    /// the diagonal from the opposite corner, as a factor of the diagonal: 1 where it started, more outward, less inward,
    /// the drag first turned back into the box's own frame by its <paramref name="rotation"/>.
    /// </summary>
    private static double Factor(PixelRect box, Handle corner, int dx, int dy, double rotation)
    {
        var (sin, cos) = Math.SinCos(-rotation * Math.PI / 180);
        double along = dx * cos - dy * sin, down = dx * sin + dy * cos;
        // The diagonal from the opposite corner to the dragged one.
        double width = corner is Handle.TopLeft or Handle.BottomLeft ? -box.Width : box.Width;
        double height = corner is Handle.TopLeft or Handle.TopRight ? -box.Height : box.Height;
        return ((width + along) * width + (height + down) * height) / (width * width + height * height);
    }

    /// <summary>A font size scaled by <paramref name="factor"/> to the nearest whole pixel, never below 1 nor beyond what a size can hold.</summary>
    private static int ScaledSize(int size, double factor) => (int)Math.Clamp(Math.Round(size * factor), 1, int.MaxValue);

    private static TextLayer Scaled(TextLayer text, Handle corner, int dx, int dy, PixelSize original)
    {
        var size = ScaledSize(text.FontSize, Factor(text.Box, corner, dx, dy, text.Rotation));
        var width = text.Preset == TextPreset.Meme ? (int)Math.Clamp(Math.Round(text.Box.Width * ((double)size / text.FontSize)), 1, int.MaxValue) : text.Box.Width;
        var sized = text with { FontSize = size, Box = text.Box with { Width = width } };
        var fitted = Fitted(sized);

        // The opposite corner stays where it is on the picture, turned as the layer is: the new centre is found from it.
        var keptLeft = corner is Handle.TopRight or Handle.BottomRight;
        var keptTop = corner is Handle.BottomLeft or Handle.BottomRight;
        var kept = TurnedAbout(new PointF(keptLeft ? text.Box.X : text.Box.X + text.Box.Width, keptTop ? text.Box.Y : text.Box.Y + text.Box.Height), Centre(text.Box), text.Rotation);
        var (sin, cos) = Math.SinCos(text.Rotation * Math.PI / 180);
        double x = (keptLeft ? -1 : 1) * fitted.Width / 2.0, y = (keptTop ? -1 : 1) * fitted.Height / 2.0;
        double centreX = kept.X - (x * cos - y * sin), centreY = kept.Y - (x * sin + y * cos);
        var origin = new PixelRect(Whole(centreX - fitted.Width / 2.0), Whole(centreY - fitted.Height / 2.0), fitted.Width, fitted.Height);
        return sized with { Box = TextPlacement.Kept(origin, text.Rotation, original) };
    }

    /// <summary>A text layer's box size as its letters need it: a Meme's width as it is with the measured height, a Label's measured width and height.</summary>
    private static PixelSize Fitted(TextLayer text)
    {
        var measured = LayerComposer.TextSize(text);
        return text.Preset == TextPreset.Meme ? measured with { Width = text.Box.Width } : measured;
    }

    /// <summary>A coordinate to the nearest whole pixel, within what an origin can hold.</summary>
    private static int Whole(double value) => (int)Math.Clamp(Math.Round(value, MidpointRounding.AwayFromZero), int.MinValue, int.MaxValue);

    /// <summary>The edges <paramref name="low"/> to <paramref name="high"/> kept within <paramref name="start"/> … <paramref name="end"/> and at least one pixel apart.</summary>
    private static (int Low, int High) Span(int low, int high, int start, int end)
    {
        (low, high) = (Math.Clamp(Math.Min(low, high), start, end), Math.Clamp(Math.Max(low, high), start, end));
        return high > low ? (low, high) : high < end ? (low, high + 1) : (low - 1, high);
    }

    /// <summary><paramref name="rect"/> moved by whole pixels as far as it stays inside the original, its size kept.</summary>
    internal static PixelRect Shifted(PixelRect rect, int dx, int dy, PixelSize original) =>
        rect with { X = Placed(rect.X, dx, rect.Width, original.Width), Y = Placed(rect.Y, dy, rect.Height, original.Height) };

    /// <summary>An edge moved by <paramref name="by"/> as far as its span stays inside 0 … <paramref name="limit"/>, never below 0.</summary>
    private static int Placed(int start, int by, int length, int limit) => (int)Math.Max(0, Math.Min((long)start + by, (long)limit - length));

    /// <summary>
    /// The points moved together by as much of the offset as keeps every one of them on the original; points a stored
    /// list placed beyond it stay where they are.
    /// </summary>
    private static PixelPoint[] Shifted(IReadOnlyList<PixelPoint> points, int dx, int dy, PixelSize original)
    {
        dx = Within(dx, -points.Min(p => p.X), original.Width - 1 - points.Max(p => p.X));
        dy = Within(dy, -points.Min(p => p.Y), original.Height - 1 - points.Max(p => p.Y));
        return [.. points.Select(p => new PixelPoint(p.X + dx, p.Y + dy))];
    }

    private static int Within(int value, int lowest, int highest) => lowest > highest ? 0 : Math.Clamp(value, lowest, highest);

    /// <summary>
    /// <paramref name="rect"/> with the corner <paramref name="corner"/> names dragged by whole pixels, the opposite one
    /// kept, inside the original and never below 1 × 1: dragged past the far corner it turns round (<see cref="Edges"/>).
    /// </summary>
    internal static PixelRect Stretched(PixelRect rect, Handle corner, int dx, int dy, PixelSize original)
    {
        var left = corner is Handle.TopLeft or Handle.BottomLeft;
        var top = corner is Handle.TopLeft or Handle.TopRight;
        var (x0, x1) = Edges(left ? rect.X + rect.Width : rect.X, (left ? rect.X : (long)rect.X + rect.Width) + dx, movedWasLow: left, original.Width);
        var (y0, y1) = Edges(top ? rect.Y + rect.Height : rect.Y, (top ? rect.Y : (long)rect.Y + rect.Height) + dy, movedWasLow: top, original.Height);
        return new PixelRect(x0, y0, x1 - x0, y1 - y0);
    }

    /// <summary>
    /// The edges of a side whose <paramref name="moved"/> edge was dragged and whose <paramref name="kept"/> edge stays,
    /// inside 0 … <paramref name="limit"/>: a dragged edge that lands on the kept one leaves one pixel on the side it came
    /// from, or on the other where the picture ends.
    /// </summary>
    private static (int Low, int High) Edges(int kept, long moved, bool movedWasLow, int limit)
    {
        var edge = (int)Math.Clamp(moved, 0, limit);
        if (edge != kept)
        {
            return (Math.Min(edge, kept), Math.Max(edge, kept));
        }

        return movedWasLow && kept > 0 || kept == limit ? (kept - 1, kept) : (kept, kept + 1);
    }
}
