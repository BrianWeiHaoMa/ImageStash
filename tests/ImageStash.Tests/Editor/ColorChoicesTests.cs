using ImageStash.Editor;
using ImageStash.Layers;

namespace ImageStash.Tests.Editor;

/// <summary>
/// The picker's colors (G21, G23, L2): the five swatches; the recent colors, newest first, each once, the swatches never
/// among them, as many as one row holds; the hex field's reading and writing; the square and strip's hue, saturation and
/// value, which give back the color they were read from.
/// </summary>
[TestClass]
public sealed class ColorChoicesTests
{
    [TestMethod]
    public void TheSwatchesAreTheFiveInkColorsWithRedTheFirstMarks()
    {
        CollectionAssert.AreEqual(new[] { "#FFFFFF", "#111111", "#FFD43B", "#E5484D", "#2F6BFF" }, ColorChoices.Swatches.Select(ColorChoices.Hex).ToArray());
        Assert.AreEqual(ColorChoices.Swatches[3], ColorChoices.Red);
    }

    [TestMethod]
    public void RecentColorsAreNewestFirstEachOnceWithoutSwatchesUpToOneRow()
    {
        var colors = new ColorChoices();
        var picked = Enumerable.Range(1, ColorChoices.RecentCount + 2).Select(i => new Rgba((byte)(i * 20), 0x40, 0x80, 0xFF)).ToList();

        colors.Use(picked[0]);
        colors.Use(picked[1]);
        colors.Use(picked[0]);
        colors.Use(ColorChoices.Red);
        CollectionAssert.AreEqual(new[] { picked[0], picked[1] }, colors.Recent.ToArray());

        picked.ForEach(colors.Use);
        Assert.HasCount(ColorChoices.RecentCount, colors.Recent);
        Assert.AreEqual(picked[^1], colors.Recent[0]);
        Assert.DoesNotContain(picked[0], colors.Recent);
        Assert.IsEmpty(new ColorChoices().Recent, "each window keeps its own");
    }

    [TestMethod]
    public void TheHexFieldReadsSixOrThreeDigitsWithOrWithoutTheHash()
    {
        var orange = new Rgba(0xFF, 0x7A, 0x1A, 0xFF);

        Assert.AreEqual("#FF7A1A", ColorChoices.Hex(orange));
        Assert.AreEqual(orange, ColorChoices.ParseHex("#FF7A1A"));
        Assert.AreEqual(orange, ColorChoices.ParseHex(" ff7a1a "));
        Assert.AreEqual(new Rgba(0xFF, 0xAA, 0x00, 0xFF), ColorChoices.ParseHex("#fa0"));
        foreach (var text in new[] { "", "#", "#12345", "#1234567", "#GG0000", "red", "#FF7A1A80" })
        {
            Assert.IsNull(ColorChoices.ParseHex(text), text);
        }
    }

    [TestMethod]
    public void HueSaturationAndValueGiveBackTheColor()
    {
        foreach (var color in ColorChoices.Swatches.Append(new Rgba(0xFF, 0x7A, 0x1A, 0xFF)).Append(new Rgba(0, 0, 0, 0xFF)).Append(new Rgba(0x14, 0xB8, 0xA6, 0xFF)))
        {
            Assert.AreEqual(color, ColorChoices.FromHsv(ColorChoices.ToHsv(color)), ColorChoices.Hex(color));
        }

        Assert.AreEqual(new Hsv(0, 1, 1), ColorChoices.ToHsv(new Rgba(255, 0, 0, 255)));
        Assert.AreEqual(new Rgba(0, 0, 255, 255), ColorChoices.FromHsv(new Hsv(240, 1, 1)));
        Assert.AreEqual(new Rgba(255, 0, 0, 255), ColorChoices.FromHsv(new Hsv(360, 2, 7)), "out of range, each is clamped");
    }
}
