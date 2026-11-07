using ImageStash.Media;
using ImageStash.Seams;

namespace ImageStash.Windows.Seams;

/// <summary>
/// The installed fonts as SkiaSharp's font manager names and matches them over DirectWrite, through Media's one home for
/// matching, so every name the menu lists is one a layer draws in (F8, G21).
/// </summary>
public sealed class WindowsFonts : IFonts
{
    public IReadOnlyList<string> Families() => Typefaces.Families();

    public bool IsInstalled(string family) => Typefaces.Match(family) is not null;
}
