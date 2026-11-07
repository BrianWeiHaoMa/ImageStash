using System.Globalization;
using System.Text;
using ImageStash.Layers;
using SkiaSharp;

namespace ImageStash.Media;

/// <summary>
/// The letters of a text or caption-bar layer (G21), laid out by one rule that the drawing, <see cref="LayerComposer.TextSize"/>
/// and a band's height all read: the text's lines are its <c>\n</c>-separated lines, a Meme's and a caption's further
/// wrapped at the width they have, at spaces, a word wider than it broken between characters and never fewer than one
/// character on a line; a Label's never wrapped. Lines are 1.2 font sizes apart, the first baseline below the top by the
/// font's ascent. The family is found through <see cref="Typefaces"/>, a missing one drawn in the default typeface and
/// said so; a character the typeface has no glyph for is drawn in the typeface the font manager offers for it (an emoji
/// font), else in the layer's own, never refused (G35). No shaping: an emoji sequence draws as its parts.
/// </summary>
internal sealed class TextLettering : IDisposable
{
    // L2: the line pitch and a caption band's padding above and below its lines, in tenths of the font size, so a band's
    // height is counted exactly in whole numbers.
    private const int PitchTenths = 12, BandPaddingTenths = 8;

    // L2: the outline's stroke widths, in font sizes. The stroke lies under the fill, so half of it shows round the
    // letters: 0.06 of the size round a Meme's, 0.045 round a Label's and a caption's.
    private const float MemeOutline = 0.12f, OtherOutline = 0.09f;

    private readonly Dictionary<SKTypeface, SKFont> fonts = new(ReferenceEqualityComparer.Instance);
    private readonly List<Line> lines = [];
    private readonly float size;
    private readonly SKTypeface face;
    private readonly SKFont primary;

    private TextLettering(string text, string family, int fontSize, float? wrapWidth)
    {
        size = fontSize;
        var matched = Typefaces.Match(family);
        FamilyMissing = matched is null;
        face = matched ?? SKTypeface.Default;
        primary = FontOf(face);
        foreach (var paragraph in text.Split('\n'))
        {
            Lay(Glyphs(paragraph), wrapWidth);
        }
    }

    /// <summary>Whether the layer's family is not on this PC, so its letters are in the default typeface.</summary>
    public bool FamilyMissing { get; }

    /// <summary>A text layer's letters: a Meme in uppercase, wrapped at its box's width; a Label as typed, unwrapped.</summary>
    public static TextLettering Of(TextLayer layer) => layer.Preset == TextPreset.Meme
        ? new(Uppercase.Of(layer.Text), layer.Font, layer.FontSize, layer.Box.Width)
        : new(layer.Text, layer.Font, layer.FontSize, null);

    /// <summary>A caption's letters, wrapped at its band's width, the cropped picture's.</summary>
    public static TextLettering Of(CaptionBarLayer bar, int bandWidth) => new(bar.Text, bar.Font, bar.FontSize, bandWidth);

    /// <summary>A caption band's height: 1.2 font sizes per laid-out line plus 0.8, rounded up; in 128 bits, so it never overflows.</summary>
    public static Int128 BandHeight(CaptionBarLayer bar, int bandWidth)
    {
        using var lettering = Of(bar, bandWidth);
        return ((Int128)bar.FontSize * (PitchTenths * (Int128)lettering.lines.Count + BandPaddingTenths) + 9) / 10;
    }

    /// <summary>Black on a light fill, white on a dark one (G21): the fill's luminance, 0.299 R + 0.587 G + 0.114 B over 255, above a half.</summary>
    public static Rgba AutoOutline(Rgba fill) =>
        0.299 * fill.R + 0.587 * fill.G + 0.114 * fill.B > 0.5 * 255 ? new Rgba(0, 0, 0, 255) : new Rgba(255, 255, 255, 255);

    /// <summary>The laid-out size: the widest line and the lines' pitch, each rounded up and at least 1.</summary>
    public PixelSize Size()
    {
        var width = Math.Ceiling((double)lines.Max(line => line.Width));
        var height = ((Int128)size * PitchTenths * lines.Count + 9) / 10;
        return new PixelSize((int)Math.Clamp(width, 1, int.MaxValue), (int)Int128.Clamp(height, 1, int.MaxValue));
    }

    /// <summary>
    /// Draws a text layer onto <paramref name="canvas"/>, whose coordinates are the original's grid: from its box's
    /// top-left, a Meme's lines centred in the box, turned by its rotation clockwise about the box's centre. Its family,
    /// when missing, joins <paramref name="replaced"/> once.
    /// </summary>
    public static bool DrawText(SKCanvas canvas, TextLayer layer, List<string> replaced)
    {
        using var lettering = Of(layer);
        var box = layer.Box;
        canvas.Save();
        canvas.RotateDegrees((float)(layer.Rotation % 360), box.X + box.Width / 2f, box.Y + box.Height / 2f);
        lettering.Draw(canvas, box.X, box.Y, layer.Preset == TextPreset.Meme ? box.Width : null, layer.Fill, layer.Outline,
            layer.Preset == TextPreset.Meme ? MemeOutline : OtherOutline);
        canvas.Restore();
        return lettering.Report(layer.Font, replaced);
    }

    /// <summary>
    /// Draws a caption in its band, the band directly above <paramref name="bandBottom"/> and <paramref name="width"/>
    /// wide, which then moves to the band's top: its lines centred, 0.4 font sizes below the band's top, clipped to the
    /// band. Its family, when missing, joins <paramref name="replaced"/> once.
    /// </summary>
    public static bool DrawCaption(SKCanvas canvas, CaptionBarLayer bar, int width, ref int bandBottom, List<string> replaced)
    {
        using var lettering = Of(bar, width);
        var height = (int)BandHeight(bar, width);
        bandBottom -= height;
        canvas.Save();
        canvas.ClipRect(SKRect.Create(0, bandBottom, width, height));
        lettering.Draw(canvas, 0, bandBottom + lettering.size * BandPaddingTenths / 20f, width, bar.Fill, bar.Outline, OtherOutline);
        canvas.Restore();
        return lettering.Report(bar.Font, replaced);
    }

    public void Dispose()
    {
        foreach (var font in fonts.Values)
        {
            font.Dispose();
        }
    }

    private bool Report(string family, List<string> replaced)
    {
        if (FamilyMissing && !replaced.Contains(family, StringComparer.OrdinalIgnoreCase))
        {
            replaced.Add(family);
        }

        return true;
    }

    /// <summary>The outline under every line first, then the fill, so no line's outline covers another's letters.</summary>
    private void Draw(SKCanvas canvas, float left, float top, float? centredIn, Rgba fill, Outline outline, float outlineWidth)
    {
        var stroke = outline.IsAuto ? AutoOutline(fill) : outline.Color;
        if (stroke is { } color)
        {
            using var paint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = outlineWidth * size,
                StrokeJoin = SKStrokeJoin.Round,
                Color = new SKColor(color.R, color.G, color.B, color.A),
            };
            Pass(canvas, left, top, centredIn, paint);
        }

        using var letters = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = new SKColor(fill.R, fill.G, fill.B, fill.A) };
        Pass(canvas, left, top, centredIn, letters);
    }

    private void Pass(SKCanvas canvas, float left, float top, float? centredIn, SKPaint paint)
    {
        var baseline = top - primary.Metrics.Ascent;
        foreach (var line in lines)
        {
            var x = left + (centredIn is { } width ? (width - line.Width) / 2 : 0);
            for (var start = 0; start < line.Glyphs.Count;)
            {
                // Neighbours in one font are drawn as one run.
                var font = line.Glyphs[start].Font;
                var run = new StringBuilder();
                var runWidth = 0f;
                var end = start;
                for (; end < line.Glyphs.Count && line.Glyphs[end].Font == font; end++)
                {
                    run.Append(line.Glyphs[end].Text);
                    runWidth += line.Glyphs[end].Advance;
                }

                canvas.DrawText(run.ToString(), x, baseline, SKTextAlign.Left, font, paint);
                x += runWidth;
                start = end;
            }

            baseline += size * PitchTenths / 10f;
        }
    }

    /// <summary>
    /// Each character of <paramref name="paragraph"/> in the font that has its glyph, with its advance; a control
    /// character draws nothing.
    /// </summary>
    private List<Glyph> Glyphs(string paragraph)
    {
        var glyphs = new List<Glyph>();
        foreach (var rune in paragraph.EnumerateRunes())
        {
            if (Rune.GetUnicodeCategory(rune) == UnicodeCategory.Control)
            {
                continue;
            }

            var font = face.ContainsGlyph(rune.Value) || Typefaces.ForCodePoint(rune.Value) is not { } fallback ? primary : FontOf(fallback);
            var text = rune.ToString();
            glyphs.Add(new Glyph(text, font, font.MeasureText(text), rune.Value == ' '));
        }

        return glyphs;
    }

    /// <summary>Adds the paragraph's lines: one without a width to wrap at, else as many as wrapping at spaces gives.</summary>
    private void Lay(List<Glyph> glyphs, float? wrapWidth)
    {
        if (wrapWidth is not { } limit)
        {
            lines.Add(new Line(glyphs));
            return;
        }

        var line = new List<Glyph>();
        var lineWidth = 0f;
        var word = new List<Glyph>();
        Glyph? space = null;
        foreach (var glyph in glyphs.Append(new Glyph(" ", primary, 0, IsSpace: true)))
        {
            if (!glyph.IsSpace)
            {
                word.Add(glyph);
                continue;
            }

            var wordWidth = word.Sum(each => each.Advance);
            if (line.Count > 0 && space is { } before && lineWidth + before.Advance + wordWidth <= limit)
            {
                line.Add(before);
                line.AddRange(word);
                lineWidth += before.Advance + wordWidth;
            }
            else if (word.Count > 0)
            {
                if (line.Count > 0)
                {
                    lines.Add(new Line(line));
                    (line, lineWidth) = ([], 0);
                }

                // A word wider than the line is broken between characters, at least one on each line.
                foreach (var each in word)
                {
                    if (line.Count > 0 && lineWidth + each.Advance > limit)
                    {
                        lines.Add(new Line(line));
                        (line, lineWidth) = ([], 0);
                    }

                    line.Add(each);
                    lineWidth += each.Advance;
                }
            }

            word = [];
            space = glyph;
        }

        lines.Add(new Line(line));
    }

    private SKFont FontOf(SKTypeface typeface)
    {
        if (!fonts.TryGetValue(typeface, out var font))
        {
            // Unhinted and with linear metrics, so a line measures the same at every size the picture is drawn at.
            font = new SKFont(typeface, size) { Subpixel = true, LinearMetrics = true, Hinting = SKFontHinting.None, Edging = SKFontEdging.Antialias };
            fonts.Add(typeface, font);
        }

        return font;
    }

    private sealed record Glyph(string Text, SKFont Font, float Advance, bool IsSpace);

    private sealed class Line(List<Glyph> glyphs)
    {
        public List<Glyph> Glyphs { get; } = glyphs;

        public float Width { get; } = glyphs.Sum(glyph => glyph.Advance);
    }
}
