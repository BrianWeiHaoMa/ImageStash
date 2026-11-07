using ImageStash.Media;
using ImageStash.Windows.Seams;

namespace ImageStash.Windows.Tests.Seams;

/// <summary>F8, F13: the font menu's list and Media's matching agree on this PC, the ten F8 names among them.</summary>
[TestClass]
public sealed class WindowsFontsTests
{
    private static readonly string[] MenuFonts =
        ["Impact", "Arial Black", "Segoe UI", "Comic Sans MS", "Georgia", "Consolas", "Bahnschrift", "Segoe Script", "Ink Free", "Verdana"];

    [TestMethod]
    public void TheFamiliesAreMediasAndHoldTheTenTheMenuNames()
    {
        var families = new WindowsFonts().Families();

        CollectionAssert.AreEqual(Typefaces.Families().ToList(), families.ToList());
        foreach (var name in MenuFonts)
        {
            Assert.Contains(name, families, $"{name} is listed");
        }
    }

    [TestMethod]
    public void AFamilyIsInstalledWhenALayerDrawsInIt()
    {
        var fonts = new WindowsFonts();

        Assert.IsTrue(fonts.IsInstalled("Arial Black"), "Windows files Arial Black as the Arial family's Black style");
        Assert.IsTrue(fonts.IsInstalled("Impact"));
        Assert.IsFalse(fonts.IsInstalled("No Such Font"));
        Assert.IsTrue(fonts.Families().All(fonts.IsInstalled), "every listed family is installed");
    }
}
