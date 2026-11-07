using System.Globalization;
using ImageStash.Layers;

namespace ImageStash.Editor;

/// <summary>A color as the picker's square and strip show it: hue in degrees from 0 to 360, saturation and value from 0 to 1.</summary>
public readonly record struct Hsv(double Hue, double Saturation, double Value);

/// <summary>
/// The colors the editor offers (G21's picker, G23's swatches): the five swatches, the colors used recently, newest first,
/// kept in memory for the run (State names what the settings file holds, and recent colors are not among them), and the
/// conversions the picker's hex field, square and strip need. One instance lives as long as the window, so every edit of
/// the run sees the same recent colors.
/// </summary>
public sealed class ColorChoices
{
    /// <summary>How many recent colors are kept (L2): one row of the picker.</summary>
    public const int RecentCount = 6;

    private readonly List<Rgba> recent = [];

    /// <summary>White, near-black, yellow, red and blue, as the swatch row shows them (L2).</summary>
    public static IReadOnlyList<Rgba> Swatches { get; } =
    [
        new(0xFF, 0xFF, 0xFF, 0xFF),
        new(0x11, 0x11, 0x11, 0xFF),
        new(0xFF, 0xD4, 0x3B, 0xFF),
        new(0xE5, 0x48, 0x4D, 0xFF),
        new(0x2F, 0x6B, 0xFF, 0xFF),
    ];

    /// <summary>The swatch a new mark starts in: red.</summary>
    public static Rgba Red => Swatches[3];

    /// <summary>The colors used recently that are not swatches, newest first, each once.</summary>
    public IReadOnlyList<Rgba> Recent
    {
        get
        {
            lock (recent)
            {
                return [.. recent];
            }
        }
    }

    /// <summary>Records <paramref name="color"/> as used: a color that is not a swatch moves to the front of <see cref="Recent"/>.</summary>
    public void Use(Rgba color)
    {
        if (Swatches.Contains(color))
        {
            return;
        }

        lock (recent)
        {
            recent.Remove(color);
            recent.Insert(0, color);
            if (recent.Count > RecentCount)
            {
                recent.RemoveAt(RecentCount);
            }
        }
    }

    /// <summary>The color as the hex field shows it, <c>#RRGGBB</c>.</summary>
    public static string Hex(Rgba color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    /// <summary>
    /// The opaque color <paramref name="text"/> names as six or three hex digits, with or without a leading <c>#</c> and
    /// surrounding spaces, in either case; null for anything else.
    /// </summary>
    public static Rgba? ParseHex(string text)
    {
        var digits = text.Trim();
        if (digits.StartsWith('#'))
        {
            digits = digits[1..];
        }

        if (digits.Length == 3)
        {
            digits = string.Concat(digits.Select(digit => new string(digit, 2)));
        }

        if (digits.Length != 6 || !digits.All(char.IsAsciiHexDigit))
        {
            return null;
        }

        var value = int.Parse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return new Rgba((byte)(value >> 16), (byte)(value >> 8), (byte)value, 0xFF);
    }

    /// <summary>The color's hue, saturation and value; a grey has hue 0.</summary>
    public static Hsv ToHsv(Rgba color)
    {
        double r = color.R / 255d, g = color.G / 255d, b = color.B / 255d;
        var max = Math.Max(r, Math.Max(g, b));
        var span = max - Math.Min(r, Math.Min(g, b));
        var hue = span == 0 ? 0
            : max == r ? 60 * ((g - b) / span % 6)
            : max == g ? 60 * ((b - r) / span + 2)
            : 60 * ((r - g) / span + 4);
        return new Hsv(hue < 0 ? hue + 360 : hue, max == 0 ? 0 : span / max, max);
    }

    /// <summary>The opaque color of that hue, saturation and value, each clamped into its range.</summary>
    public static Rgba FromHsv(Hsv hsv)
    {
        var hue = double.IsFinite(hsv.Hue) ? (hsv.Hue % 360 + 360) % 360 : 0;
        var saturation = Math.Clamp(double.IsFinite(hsv.Saturation) ? hsv.Saturation : 0, 0, 1);
        var value = Math.Clamp(double.IsFinite(hsv.Value) ? hsv.Value : 0, 0, 1);
        var chroma = value * saturation;
        var x = chroma * (1 - Math.Abs(hue / 60 % 2 - 1));
        var (r, g, b) = (int)(hue / 60) switch
        {
            0 => (chroma, x, 0d),
            1 => (x, chroma, 0d),
            2 => (0d, chroma, x),
            3 => (0d, x, chroma),
            4 => (x, 0d, chroma),
            _ => (chroma, 0d, x),
        };
        var m = value - chroma;
        return new Rgba(Channel(r + m), Channel(g + m), Channel(b + m), 0xFF);
    }

    private static byte Channel(double fraction) => (byte)Math.Round(Math.Clamp(fraction, 0, 1) * 255, MidpointRounding.AwayFromZero);
}
