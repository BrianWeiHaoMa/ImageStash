using Avalonia.Controls.Primitives;
using Avalonia.Media;
using ImageStash.Media;
using SkiaSharp;

namespace ImageStash.Editor;

/// <summary>
/// A font family name as the editor's own controls show it (F8): in the typeface Media matches the name to
/// (<see cref="Typefaces.Match"/>), given to Avalonia as that typeface's family with its weight, width and slant, so
/// "Arial Black", which DirectWrite files as the Arial family's Black style, shows black rather than in a fallback; a
/// name Media cannot match shows in the default face. Only the editor's own text takes it: a layer is drawn by Media (R4).
/// </summary>
internal readonly record struct FontFace(FontFamily Family, FontWeight Weight, FontStyle Style, FontStretch Stretch)
{
    public static FontFace Of(string family) =>
        Typefaces.Match(family) is { } typeface
            ? new FontFace(new FontFamily(typeface.FamilyName), (FontWeight)typeface.FontWeight, StyleOf(typeface.FontSlant), (FontStretch)Math.Clamp(typeface.FontWidth, 1, 9))
            : new FontFace(FontFamily.Default, FontWeight.Normal, FontStyle.Normal, FontStretch.Normal);

    /// <summary>Gives <paramref name="control"/> this face.</summary>
    public void ApplyTo(TemplatedControl control)
    {
        control.FontFamily = Family;
        control.FontWeight = Weight;
        control.FontStyle = Style;
        control.FontStretch = Stretch;
    }

    private static FontStyle StyleOf(SKFontStyleSlant slant) => slant switch
    {
        SKFontStyleSlant.Italic => FontStyle.Italic,
        SKFontStyleSlant.Oblique => FontStyle.Oblique,
        _ => FontStyle.Normal,
    };
}
