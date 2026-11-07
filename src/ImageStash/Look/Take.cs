using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace ImageStash.Look;

/// <summary>
/// One take of the look (F10): its name, and for light and for dark a value for every key of <see cref="LookKeys.All"/>,
/// the accent's marks built from the system's accent handed in and nothing else carrying an accent. The product's take is
/// <see cref="Paper"/>, listed in <see cref="Looks.All"/>; the desktop checks dress windows in a take of their own. The
/// colour rules a take uses (an accent's wash, words on the accent) are here.
/// </summary>
public abstract class Take
{
    /// <summary>The take's name.</summary>
    public abstract string Name { get; }

    /// <summary>A value for every key of <see cref="LookKeys.All"/> under <paramref name="variant"/>, the accent's marks from <paramref name="accent"/>.</summary>
    public ResourceDictionary Resources(ThemeVariant variant, Color accent)
    {
        var resources = new ResourceDictionary();
        Fill(resources, variant == ThemeVariant.Dark, accent);
        return resources;
    }

    /// <summary>Puts a value for every key of <see cref="LookKeys.All"/> into <paramref name="into"/>, dark or light.</summary>
    protected abstract void Fill(ResourceDictionary into, bool dark, Color accent);

    /// <summary>An opaque colour from its hex, <c>0xRRGGBB</c>.</summary>
    protected static Color Rgb(uint rgb) => Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    /// <summary><paramref name="color"/> at <paramref name="alpha"/> of 255.</summary>
    protected static Color Alpha(Color color, byte alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);

    protected static IBrush Solid(Color color) => new SolidColorBrush(color);

    protected static IBrush Solid(uint rgb) => new SolidColorBrush(Rgb(rgb));

    /// <summary>No ground at all, where a control shows the ground beneath it.</summary>
    protected static IBrush Clear => Brushes.Transparent;

    /// <summary><paramref name="from"/> moved <paramref name="part"/> of the way to <paramref name="to"/>.</summary>
    protected static Color Mix(Color from, Color to, double part) => Color.FromRgb(
        (byte)Math.Round(from.R + (to.R - from.R) * part),
        (byte)Math.Round(from.G + (to.G - from.G) * part),
        (byte)Math.Round(from.B + (to.B - from.B) * part));

    /// <summary>
    /// The accent as words, darker on a light ground or lighter on a dark one, as little as reads: moved toward black or
    /// white until it holds a contrast of 5.5 to 1 against white or against a near-black, which leaves 4.5 to 1 on the
    /// washes and greys a chip or a button lies on, so a pale or a deep system accent still gives words that read.
    /// </summary>
    protected static Color AccentWords(Color accent, bool dark)
    {
        var (toward, ground, part) = dark ? (Colors.White, Rgb(0x1E1F23), 0.4) : (Colors.Black, Colors.White, 0.22);
        var words = Mix(accent, toward, part);
        while (Contrast(words, ground) < 5.5 && part < 1)
        {
            part = Math.Min(1, part + 0.05);
            words = Mix(accent, toward, part);
        }

        return words;
    }

    /// <summary>Words on a ground of <paramref name="ground"/>: near-black or white, whichever reads better on it.</summary>
    protected static Color WordsOn(Color ground) => Contrast(Rgb(0x111215), ground) >= Contrast(Colors.White, ground) ? Rgb(0x111215) : Colors.White;

    /// <summary>The contrast ratio of two opaque colours, 1 to 21, by the sRGB relative luminance.</summary>
    public static double Contrast(Color one, Color other)
    {
        static double Channel(byte value) => value / 255.0 <= 0.04045 ? value / 255.0 / 12.92 : Math.Pow((value / 255.0 + 0.055) / 1.055, 2.4);
        static double Luminance(Color color) => 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
        var (a, b) = (Luminance(one), Luminance(other));
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    protected static CornerRadius Radius(double radius) => new(radius);
}
