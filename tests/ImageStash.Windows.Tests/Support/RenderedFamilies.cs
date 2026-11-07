using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace ImageStash.Windows.Tests.Support;

/// <summary>
/// The font families text is drawn in, read from the glyph typefaces Avalonia laid it out with, not from the family a
/// control asked for: a requested family the font manager does not resolve as asked draws in another (F10).
/// </summary>
public static class RenderedFamilies
{
    /// <summary>The text family F10 names, as Windows 11 installs it.</summary>
    public const string SegoeUi = "Segoe UI";

    /// <summary>Each family a laid-out text's runs are drawn in.</summary>
    public static IReadOnlyList<string> Of(TextLayout layout) =>
        layout.TextLines.SelectMany(line => line.TextRuns).OfType<ShapedTextRun>().Select(run => Name(run.GlyphRun.GlyphTypeface)).Distinct().ToList();

    /// <summary>The family a glyph typeface belongs to: Segoe UI's semibold face names itself "Segoe UI Semibold".</summary>
    public static string Name(GlyphTypeface face) =>
        face.FamilyName == "Segoe UI Semibold" ? SegoeUi : face.FamilyName;
}
