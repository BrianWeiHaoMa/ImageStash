using System.Collections.Concurrent;
using SkiaSharp;

namespace ImageStash.Media;

/// <summary>
/// The one home for finding a font family on this PC (F8, S11): through SkiaSharp's font manager, by the family's own
/// name or as <c>&lt;family&gt; &lt;style&gt;</c>, the way Windows names a face such as "Arial Black" that DirectWrite files
/// as the Arial family's Black style. The family list it answers and the matching agree by construction, so every name in
/// the font menu draws in its own face. Safe on any thread.
/// </summary>
public static class Typefaces
{
    // A typeface Match answers belongs here for the run: SkiaSharp hands every caller of one native typeface the same
    // object, so disposing one would break every other holder.
    private static readonly ConcurrentDictionary<string, SKTypeface> Matched = new(StringComparer.OrdinalIgnoreCase);

    private static readonly ConcurrentDictionary<int, SKTypeface> ForCharacter = new();

    /// <summary>
    /// The typeface that draws <paramref name="family"/> on this PC, ignoring case: the font manager's match for the name,
    /// else the longest prefix of the name, cut at a space, that names a family holding a style named as the rest; null
    /// when neither finds it (S11: the manager answers nothing for a family not installed). A miss is asked again at the
    /// next call, so a font installed meanwhile is found. The caller never disposes the answer.
    /// </summary>
    public static SKTypeface? Match(string family)
    {
        // The manager reads a name only up to a NUL, so it would find another family for one holding it.
        if (string.IsNullOrWhiteSpace(family) || family.Contains('\0'))
        {
            return null;
        }

        if (Matched.TryGetValue(family, out var known))
        {
            return known;
        }

        var found = SKFontManager.Default.MatchFamily(family) ?? AsFamilyAndStyle(family);
        return found is null ? null : Matched.GetOrAdd(family, found);
    }

    /// <summary>
    /// Every family name this PC can draw, read afresh: the font manager's families, and each family's upright styles
    /// other than Regular and Bold as <c>&lt;family&gt; &lt;style&gt;</c> ("Arial Black", "Segoe UI Semibold"), which
    /// <see cref="Match"/> finds; an italic or oblique adds no name of its own. Each name once, sorted ignoring case.
    /// </summary>
    public static IReadOnlyList<string> Families()
    {
        var manager = SKFontManager.Default;
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var family in manager.FontFamilies)
        {
            names.Add(family);
            using var styles = manager.GetFontStyles(family);
            for (var index = 0; index < styles.Count; index++)
            {
                if (styles.GetStyleName(index) is { } style && NamesAFace(style))
                {
                    names.Add(family + " " + style.Trim());
                }
            }
        }

        return [.. names];
    }

    /// <summary>
    /// The typeface the font manager offers for <paramref name="codePoint"/> when it has a glyph for it (an installed emoji
    /// font for an emoji, S11), else null. The caller never disposes the answer.
    /// </summary>
    internal static SKTypeface? ForCodePoint(int codePoint)
    {
        if (ForCharacter.TryGetValue(codePoint, out var known))
        {
            return known;
        }

        var found = SKFontManager.Default.MatchCharacter(codePoint);
        return found is not null && found.ContainsGlyph(codePoint) ? ForCharacter.GetOrAdd(codePoint, found) : null;
    }

    private static SKTypeface? AsFamilyAndStyle(string name)
    {
        for (var cut = name.LastIndexOf(' '); cut > 0; cut = name.LastIndexOf(' ', cut - 1))
        {
            var wanted = name[(cut + 1)..].Trim();
            using var styles = SKFontManager.Default.GetFontStyles(name[..cut].TrimEnd());
            for (var index = 0; index < styles.Count; index++)
            {
                if (wanted.Equals(styles.GetStyleName(index)?.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return styles.CreateTypeface(index);
                }
            }
        }

        return null;
    }

    /// <summary>Whether a style names a face of its own: upright, and neither the family's Regular nor its Bold.</summary>
    private static bool NamesAFace(string style)
    {
        var words = style.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length > 0
            && !style.Trim().Equals("Regular", StringComparison.OrdinalIgnoreCase)
            && !style.Trim().Equals("Bold", StringComparison.OrdinalIgnoreCase)
            && !words.Any(word => word.Equals("Italic", StringComparison.OrdinalIgnoreCase) || word.Equals("Oblique", StringComparison.OrdinalIgnoreCase));
    }
}
