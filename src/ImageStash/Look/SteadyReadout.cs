using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace ImageStash.Look;

/// <summary>
/// Words whose numbers change while they show, a clock that plays or a range that a drag moves: their digits all of one
/// width, and measured as wide as <see cref="Widest"/>, the widest reading they will show, so whatever stands after them
/// stays where it came in however the reading changes; when the widest wraps, they hold the whole line. Without
/// <see cref="Widest"/> they measure as the words they show.
/// </summary>
internal sealed class SteadyReadout : TextBlock
{
    private string? widest;

    public SteadyReadout() => FontFeatures = TabularDigits();

    /// <summary>
    /// The widest reading these words will show, held room for from now on; every reading of as many characters, or
    /// fewer, is no wider, its digits being of one width.
    /// </summary>
    public string? Widest
    {
        get => widest;
        set
        {
            if (widest != value)
            {
                widest = value;
                InvalidateMeasure();
            }
        }
    }

    /// <summary>Digits of one width ("tnum"): a face's own may be of several, so a 1 in place of a 0 would move what follows it.</summary>
    public static FontFeatureCollection TabularDigits() => [FontFeature.Parse("tnum")];

    // Styled as any TextBlock: the look's selectors name TextBlock, not this type.
    protected override Type StyleKeyOverride => typeof(TextBlock);

    protected override Size MeasureOverride(Size availableSize)
    {
        var reading = base.MeasureOverride(availableSize);
        if (string.IsNullOrEmpty(widest))
        {
            return reading;
        }

        var room = availableSize.Deflate(Padding);
        // A layout of its own: the TextBlock's run cache belongs to the words it shows.
        using var held = new TextLayout(
            widest,
            new Typeface(FontFamily, FontStyle, FontWeight, FontStretch),
            FontSize,
            textWrapping: TextWrapping,
            maxWidth: room.Width,
            maxHeight: room.Height,
            lineHeight: LineHeight,
            letterSpacing: LetterSpacing,
            maxLines: MaxLines,
            fontFeatures: FontFeatures);
        // Wrapped at its widest, it holds the whole line: a wrapped line's width follows where the words happen to break.
        var widestSize = new Size(held.TextLines.Count > 1 && double.IsFinite(room.Width) ? room.Width : held.WidthIncludingTrailingWhitespace, held.Height).Inflate(Padding);
        return new Size(Math.Max(reading.Width, widestSize.Width), Math.Max(reading.Height, widestSize.Height));
    }
}
