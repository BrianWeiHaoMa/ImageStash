using ImageStash.Media;

namespace ImageStash.Tests.Media;

/// <summary>
/// Finding a family on this PC through SkiaSharp's font manager, by its own name or as family and style, and the family
/// list the font menu reads agreeing with that matching (F8, S11, E3).
/// </summary>
[TestClass]
public sealed class TypefacesTests
{
    private static readonly string[] MenuFonts =
        ["Impact", "Arial Black", "Segoe UI", "Comic Sans MS", "Georgia", "Consolas", "Bahnschrift", "Segoe Script", "Ink Free", "Verdana"];

    [TestMethod]
    public void AFamilyIsMatchedByItsNameIgnoringCaseAndAMissingOneIsNot()
    {
        Assert.IsNotNull(Typefaces.Match("Impact"));
        Assert.AreSame(Typefaces.Match("Impact"), Typefaces.Match("impact"), "one typeface, kept for the run");
        Assert.IsNull(Typefaces.Match("No Such Font"));
        Assert.IsNull(Typefaces.Match(" "));
        Assert.IsNull(Typefaces.Match("Impact\0Not Installed"), "the manager reads a name only up to a NUL, so no family is found by one holding it");
        Assert.IsNull(Typefaces.Match("Arial\0X Black"));
    }

    [TestMethod]
    public void ArialBlackIsMatchedAsTheArialFamilysBlackStyle()
    {
        var black = Typefaces.Match("Arial Black");

        Assert.IsNotNull(black, "DirectWrite files Arial Black under Arial, so the manager does not match the name itself");
        Assert.AreEqual(900, black.FontWeight);
        Assert.AreEqual(400, Typefaces.Match("Arial")!.FontWeight);
        Assert.IsNull(Typefaces.Match("Arial Blackest"), "a style the family does not have");
    }

    [TestMethod]
    public void TheFamiliesNameEveryUprightFaceOnceSortedAndEachIsMatched()
    {
        var families = Typefaces.Families();

        foreach (var name in MenuFonts.Append("Segoe UI Semibold"))
        {
            Assert.Contains(name, families, $"{name} is listed");
        }

        Assert.IsFalse(families.Any(name => name.Contains("Oblique", StringComparison.OrdinalIgnoreCase) || name.Contains("Italic", StringComparison.OrdinalIgnoreCase)), "an italic or oblique adds no name");
        Assert.DoesNotContain("Arial Bold", families, "Bold is a face of Arial, not a family of its own");
        Assert.DoesNotContain("Arial Regular", families);
        Assert.HasCount(families.Count, families.Distinct(StringComparer.OrdinalIgnoreCase), "each name once");
        CollectionAssert.AreEqual(families.Order(StringComparer.OrdinalIgnoreCase).ToList(), families.ToList(), "sorted ignoring case");
        var unmatched = families.Where(name => Typefaces.Match(name) is null).ToList();
        Assert.IsEmpty(unmatched, "every listed family draws in its own face: " + string.Join(", ", unmatched));
    }
}
