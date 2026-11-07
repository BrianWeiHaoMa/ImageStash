using ImageStash.Layers;
using ImageStash.Tests.Support;

namespace ImageStash.Tests.Layers;

/// <summary>
/// The layer list as code builds it (F7, What crosses every boundary): the seven kinds with exactly their fields, the
/// one crop, trim and Shrink, a list the model refuses a caller's defect that throws, and a module that reaches nothing
/// of the parts or the libraries (Composition).
/// </summary>
[TestClass]
public sealed class LayerListTests
{
    private static readonly Rgba Red = new(200, 30, 30, 255);

    [TestMethod]
    public void EachKindCarriesExactlyTheFieldsTheBoundaryGivesIt()
    {
        string[] Fields(Type kind) =>
            [.. kind.GetProperties().Where(property => property.Name != "EqualityContract").Select(property => property.Name).Order()];

        CollectionAssert.AreEqual(new[] { "Box", "Fill", "Font", "FontSize", "Outline", "Preset", "Range", "Rotation", "Text" }, Fields(typeof(TextLayer)));
        CollectionAssert.AreEqual(new[] { "Fill", "Font", "FontSize", "Outline", "Text" }, Fields(typeof(CaptionBarLayer)), "no position, rotation or range");
        CollectionAssert.AreEqual(new[] { "Kind", "Range", "Rect" }, Fields(typeof(HideLayer)));
        CollectionAssert.AreEqual(new[] { "Color", "Kind", "Points", "Range", "Rect", "Thickness" }, Fields(typeof(DrawMarkLayer)));
        CollectionAssert.AreEqual(new[] { "Rect" }, Fields(typeof(CropLayer)));
        CollectionAssert.AreEqual(new[] { "Range" }, Fields(typeof(TrimLayer)));
        CollectionAssert.AreEqual(new[] { "Scale" }, Fields(typeof(ShrinkLayer)));
        CollectionAssert.AreEqual(new[] { "Thin", "Medium", "Thick" }, Enum.GetNames<Thickness>(), "a step, never a pixel width");
        CollectionAssert.AreEqual(new[] { "Full", "ThreeQuarter", "Half" }, Enum.GetNames<Shrink>());
    }

    [TestMethod]
    public void TheListKeepsItsOrderAndAnswersItsOneCropTrimAndShrink()
    {
        var crop = new CropLayer(new PixelRect(1, 2, 3, 4));
        var trim = new TrimLayer(new FrameRange(1, 2));
        var pen = new DrawMarkLayer(MarkKind.Pen, [new(0, 0), new(5, 5)], null, Red, Thickness.Thin, null);
        var list = new LayerList([pen, crop, new ShrinkLayer(Shrink.Half), trim]);

        CollectionAssert.AreEqual(new Layer[] { pen, crop, new ShrinkLayer(Shrink.Half), trim }, list.Layers.ToArray());
        Assert.AreEqual(crop, list.Crop);
        Assert.AreEqual(trim, list.Trim);
        Assert.AreEqual(Shrink.Half, list.Shrink);
        Assert.IsFalse(list.IsEmpty);
        Assert.IsTrue(LayerList.Empty.IsEmpty);
        Assert.IsNull(LayerList.Empty.Crop);
        Assert.IsNull(LayerList.Empty.Trim);
        Assert.AreEqual(Shrink.Full, LayerList.Empty.Shrink, "no shrink layer is full size");
    }

    [TestMethod]
    public void ListsOfEqualLayersAreEqualAndOrderMatters()
    {
        var pen = new DrawMarkLayer(MarkKind.Pen, [new(0, 0), new(5, 5)], null, Red, Thickness.Thin, null);
        var samePen = new DrawMarkLayer(MarkKind.Pen, [new(0, 0), new(5, 5)], null, Red, Thickness.Thin, null);
        var hide = new HideLayer(new PixelRect(0, 0, 2, 2), HideKind.Black, null);

        Assert.AreEqual(new LayerList([pen, hide]), new LayerList([samePen, hide]));
        Assert.AreNotEqual(new LayerList([pen, hide]), new LayerList([hide, pen]));
        Assert.AreNotEqual(
            new LayerList([pen]),
            new LayerList([pen with { Points = [new(0, 0), new(5, 6)] }]),
            "a mark's points are part of it");
    }

    [TestMethod]
    [DynamicData(nameof(Refused))]
    public void ALayerTheModelRefusesThrowsWhenCodeBuildsTheList(string because, Layer[] layers, string problem)
    {
        var thrown = Assert.ThrowsExactly<ArgumentException>(() => new LayerList(layers), because);

        StringAssert.StartsWith(thrown.Message, problem, because);
    }

    public static IEnumerable<object[]> Refused =>
    [
        ["two crops", new Layer[] { new CropLayer(new(0, 0, 2, 2)), new CropLayer(new(0, 0, 3, 3)) }, "the list has more than one crop"],
        ["two trims", new Layer[] { new TrimLayer(new(0, 1)), new TrimLayer(new(0, 2)) }, "the list has more than one trim"],
        ["two shrinks", new Layer[] { new ShrinkLayer(Shrink.Half), new ShrinkLayer(Shrink.Half) }, "the list has more than one shrink"],
        ["a box of zero width", new Layer[] { Box(new(1, 1, 0, 4)) }, "layer 1 (box) has a rectangle without a positive width and height"],
        ["a hide left of the picture", new Layer[] { new HideLayer(new(-1, 0, 2, 2), HideKind.Blur, null) }, "layer 1 (hide) has a rectangle starting left of or above the picture"],
        ["a box above the picture", new Layer[] { Box(new(0, -1, 2, 2)) }, "layer 1 (box) has a rectangle starting left of or above the picture"],
        ["a crop left of the picture", new Layer[] { new CropLayer(new(-3, 0, 2, 2)) }, "layer 1 (crop) has a rectangle starting left of or above the picture"],
        ["a text box of zero height", new Layer[] { Text() with { Box = new(-4, -4, 5, 0) } }, "layer 1 (text) has a box without a positive width and height"],
        ["a range ending before it starts", new Layer[] { new HideLayer(new(0, 0, 2, 2), HideKind.Blur, new(3, 2)) }, "layer 1 (hide) has a range that ends before it starts"],
        ["a range before the first frame", new Layer[] { new TrimLayer(new(-1, 2)) }, "layer 1 (trim) has a range starting before the first frame"],
        ["an arrow of three points", new Layer[] { new DrawMarkLayer(MarkKind.Arrow, [new(0, 0), new(1, 1), new(2, 2)], null, Red, Thickness.Thin, null) }, "layer 1 (arrow) does not have exactly two points"],
        ["a pen of one point", new Layer[] { new DrawMarkLayer(MarkKind.Pen, [new(0, 0)], null, Red, Thickness.Thin, null) }, "layer 1 (pen) has fewer than two points"],
        ["a box with points", new Layer[] { new DrawMarkLayer(MarkKind.Box, [new(0, 0), new(1, 1)], new(0, 0, 2, 2), Red, Thickness.Thin, null) }, "layer 1 (box) has points"],
        ["a pen with a rectangle", new Layer[] { new DrawMarkLayer(MarkKind.Pen, [new(0, 0), new(1, 1)], new(0, 0, 2, 2), Red, Thickness.Thin, null) }, "layer 1 (pen) has a rectangle"],
        ["a thickness outside its steps", new Layer[] { new DrawMarkLayer(MarkKind.Pen, [new(0, 0), new(1, 1)], null, Red, (Thickness)3, null) }, "layer 1 (mark) has a thickness that is not one of its values"],
        ["an empty font family", new Layer[] { Text() with { Font = " " } }, "layer 1 (text) has an empty font family"],
        ["a font size of 0", new Layer[] { new CaptionBarLayer("hi", "Impact", 0, Red, Outline.Auto) }, "layer 1 (caption bar) has a font size below 1"],
        ["a rotation of NaN", new Layer[] { Text() with { Rotation = double.NaN } }, "layer 1 (text) has a rotation that is not a number of degrees"],
        ["a rotation of infinity", new Layer[] { Text() with { Rotation = double.PositiveInfinity } }, "layer 1 (text) has a rotation that is not a number of degrees"],
        ["a missing layer", new Layer[] { Text(), null! }, "layer 2 is missing"],
        ["half a surrogate pair in the text", new Layer[] { new CaptionBarLayer("a\ud800", "Impact", 16, Red, Outline.None) }, "layer 1 (caption bar) has text that is not valid Unicode"],
        ["half a surrogate pair in the font", new Layer[] { Text() with { Font = "Imp\udc00act" } }, "layer 1 (text) has a font family that is not valid Unicode"],
    ];

    [TestMethod]
    public void ATextsBoxMayStartLeftOfOrAboveThePictureWhereATurnPutsIt()
    {
        var turned = Text() with { Box = new(-5, -2, 24, 10), Rotation = 90 };

        Assert.AreEqual(turned, new LayerList([turned]).Layers.Single(), "its letters show on the picture while its box starts off it (G21)");
    }

    [TestMethod]
    public void AMarksPointsAreItsOwn()
    {
        var points = new List<PixelPoint> { new(2, 2), new(20, 20) };
        var list = new LayerList([new DrawMarkLayer(MarkKind.Pen, points, null, Red, Thickness.Thin, null)]);

        points.Clear();
        var moved = new List<PixelPoint> { new(1, 1), new(3, 3) };
        var changed = ((DrawMarkLayer)list.Layers[0]) with { Points = moved };
        moved.Clear();

        CollectionAssert.AreEqual(new PixelPoint[] { new(2, 2), new(20, 20) }, ((DrawMarkLayer)list.Layers[0]).Points.ToArray(), "a validated list cannot change behind it");
        Assert.AreEqual(list, LayerList.FromJson(list.ToJson(), "the audacity").Value);
        Assert.HasCount(2, changed.Points, "nor a copy made with new points");
    }

    [TestMethod]
    public void TheModuleReferencesOnlyTheFrameworkAndTheOutcome()
    {
        var module = typeof(LayerList).Assembly.GetTypes().Where(type => type.Namespace == typeof(LayerList).Namespace).ToList();
        string[] admitted = [typeof(Outcome).FullName!, typeof(Outcome<>).FullName!, typeof(Failure).FullName!];

        var reached = module
            .SelectMany(ProductCalls.ReferencedTypes)
            .Where(type => !(type.Namespace is { } space && (space == "System" || space.StartsWith("System.", StringComparison.Ordinal) || space == typeof(LayerList).Namespace))
                && !admitted.Contains(type.FullName)
                && !type.Name.StartsWith('<'))
            .Select(type => type.FullName)
            .Distinct()
            .ToList();

        Assert.Contains(typeof(LayerList), module);
        Assert.IsEmpty(reached, string.Join(", ", reached));
    }

    private static DrawMarkLayer Box(PixelRect rect) => new(MarkKind.Box, [], rect, Red, Thickness.Thin, null);

    private static TextLayer Text() =>
        new(TextPreset.Meme, "HELLO", "Impact", 32, new(255, 255, 255, 255), Outline.Auto, new(0, 0, 10, 10), 0, null);
}
