using ImageStash.Layers;

namespace ImageStash.Tests.Layers;

/// <summary>
/// The layer list as the store keeps it (F7, F11, G35): every kind and field round-trips through its JSON, the text of one
/// list is pinned so the format cannot drift under a shipped stash, and a column the model cannot read is a failure
/// naming the item, never an exception.
/// </summary>
[TestClass]
public sealed class LayerListJsonTests
{
    // Pinned: a store written by this build holds exactly this text for this list, and every later build reads it (F11).
    private const string EveryKindJson =
        """[{"kind":"text","preset":"meme","text":"WHEN IT COMPILES","font":"Impact","fontSize":48,"fill":"#ffffffff","outline":"auto","box":[10,20,300,60],"rotation":-15.5,"range":[0,4]},"""
        + """{"kind":"captionBar","text":"me, later","font":"Segoe UI","fontSize":16,"fill":"#000000ff","outline":"none"},"""
        + """{"kind":"text","preset":"label","text":"x","font":"Georgia","fontSize":12,"fill":"#102030ff","outline":"#ffffff80","box":[0,0,5,5],"rotation":0},"""
        + """{"kind":"hide","style":"blur","rect":[1,2,30,40]},"""
        + """{"kind":"hide","style":"black","rect":[5,6,7,8],"range":[2,3]},"""
        + """{"kind":"mark","mark":"pen","points":[[1,1],[5,9],[12,3]],"color":"#c81e1eff","thickness":"thin"},"""
        + """{"kind":"mark","mark":"marker","points":[[0,0],[40,0]],"color":"#ffeb3bff","thickness":"thick","range":[1,1]},"""
        + """{"kind":"mark","mark":"arrow","points":[[2,2],[20,30]],"color":"#0078d4ff","thickness":"medium"},"""
        + """{"kind":"mark","mark":"box","rect":[3,4,50,60],"color":"#c81e1eff","thickness":"medium"},"""
        + """{"kind":"crop","rect":[0,0,320,200]},"""
        + """{"kind":"trim","range":[1,3]},"""
        + """{"kind":"shrink","scale":"1/2"}]""";

    private const string Item = "the audacity";

    [TestMethod]
    public void AListOfEveryKindRoundTripsThroughItsJsonAndItsTextIsPinned()
    {
        var json = EveryKind.ToJson();

        Assert.AreEqual(EveryKindJson, json, "the store's format changed: a shipped stash would no longer read the same (F11)");
        var read = LayerList.FromJson(json, Item);
        Assert.IsTrue(read.Succeeded, read.Failure?.Sentence);
        Assert.AreEqual(EveryKind, read.Value);
        CollectionAssert.AreEqual(EveryKind.Layers.ToArray(), read.Value!.Layers.ToArray());
    }

    [TestMethod]
    public void TheEmptyListIsTheColumnsDefault()
    {
        Assert.AreEqual("[]", LayerList.Empty.ToJson());
        Assert.AreSame(LayerList.Empty, LayerList.FromJson("[]", Item).Value);
    }

    [TestMethod]
    public void TextOfAnyCharactersRoundTrips()
    {
        var text = "a \"quote\", a back\\slash,\nline two, café, 😂 and </script>";
        var list = new LayerList([new CaptionBarLayer(text, "Segoe UI Emoji", 20, new(0, 0, 0, 255), Outline.None)]);

        var read = LayerList.FromJson(list.ToJson(), Item);

        Assert.AreEqual(text, ((CaptionBarLayer)read.Value!.Layers[0]).Text);
    }

    [TestMethod]
    public void ATextsBoxStartingLeftOfAndAboveThePictureRoundTripsWhileAHidesIsAFailure()
    {
        const string turned = """[{"kind":"text","preset":"label","text":"abcdef","font":"Segoe UI","fontSize":8,"fill":"#ffffffff","outline":"auto","box":[-5,-2,24,10],"rotation":90}]""";

        var read = LayerList.FromJson(turned, Item);

        Assert.IsTrue(read.Succeeded, read.Failure?.Sentence);
        Assert.AreEqual(new PixelRect(-5, -2, 24, 10), ((TextLayer)read.Value!.Layers.Single()).Box);
        Assert.AreEqual(turned, read.Value.ToJson());
        Assert.AreEqual(
            new Failure("Read the item's layers", Item, "layer 1 (hide) has a rectangle starting left of or above the picture"),
            LayerList.FromJson("""[{"kind":"hide","style":"blur","rect":[-1,0,2,2]}]""", Item).Failure);
    }

    [TestMethod]
    public void APropertyAKindDoesNotDefineIsIgnoredAndARangeOfNullIsTheWholeClip()
    {
        var read = LayerList.FromJson(
            """[{"kind":"crop","rect":[0,0,2,2],"feather":3},{"kind":"hide","style":"black","rect":[0,0,1,1],"range":null,"opacity":0.5}]""", Item);

        Assert.IsTrue(read.Succeeded, read.Failure?.Sentence);
        Assert.AreEqual(new LayerList([new CropLayer(new(0, 0, 2, 2)), new HideLayer(new(0, 0, 1, 1), HideKind.Black, null)]), read.Value);
    }

    [TestMethod]
    [DataRow("not json", "its layers are not readable JSON")]
    [DataRow("""[{"kind":"text","preset":"meme","text":"x","font":"Impact","fontSize":9,"fill":"#ffffffff","outline":"auto","box":[0,0,5,5],"rotation":NaN}]""", "its layers are not readable JSON")]
    [DataRow("""[{"kind":"\uD800"}]""", "its layers are not readable JSON")]
    [DataRow("""[{"kind":"captionBar","text":"\uD800","font":"Segoe UI","fontSize":16,"fill":"#000000ff","outline":"none"}]""", "its layers are not readable JSON")]
    [DataRow("{}", "its layers are not a list")]
    [DataRow("[1]", "layer 1 is not an object")]
    [DataRow("""[{"rect":[0,0,1,1]}]""", "layer 1 has no \"kind\"")]
    [DataRow("""[{"kind":"crop","rect":[0,0,2,2]},{"kind":"sticker"}]""", "layer 2 is of an unknown kind, \"sticker\"")]
    [DataRow("""[{"kind":"hide","style":"blur"}]""", "layer 1 (hide) has no \"rect\"")]
    [DataRow("""[{"kind":"hide","style":"blur","rect":[0,0,1]}]""", "layer 1 (hide) has a \"rect\" that is not four whole numbers")]
    [DataRow("""[{"kind":"hide","style":"smudge","rect":[0,0,1,1]}]""", "layer 1 (hide) has a \"style\" that is not one of blur, black")]
    [DataRow("""[{"kind":"crop","rect":[0,0,2,2]},{"kind":"crop","rect":[0,0,3,3]}]""", "the list has more than one crop")]
    [DataRow("""[{"kind":"mark","mark":"box","rect":[1,1,0,4],"color":"#c81e1eff","thickness":"thin"}]""", "layer 1 (box) has a rectangle without a positive width and height")]
    [DataRow("""[{"kind":"hide","style":"blur","rect":[0,0,1,1],"range":[3,2]}]""", "layer 1 (hide) has a range that ends before it starts")]
    [DataRow("""[{"kind":"mark","mark":"arrow","points":[[0,0],[1,1],[2,2]],"color":"#c81e1eff","thickness":"thin"}]""", "layer 1 (arrow) does not have exactly two points")]
    [DataRow("""[{"kind":"mark","mark":"pen","points":[[0,0]],"color":"#c81e1eff","thickness":"thin"}]""", "layer 1 (pen) has fewer than two points")]
    [DataRow("""[{"kind":"mark","mark":"pen","points":[[0,0],[1]],"color":"#c81e1eff","thickness":"thin"}]""", "layer 1 (mark) has a \"points\" that is not a list of points [x, y]")]
    [DataRow("""[{"kind":"mark","mark":"pen","points":[[0,0],[1,1]],"color":"red","thickness":"thin"}]""", "layer 1 (mark) has a \"color\" that is not a color #rrggbbaa")]
    [DataRow("""[{"kind":"mark","mark":"pen","points":[[0,0],[1,1]],"color":"#c81e1eff","thickness":"huge"}]""", "layer 1 (mark) has a \"thickness\" that is not one of thin, medium, thick")]
    [DataRow("""[{"kind":"text","preset":"meme","text":"x","font":"","fontSize":9,"fill":"#ffffffff","outline":"auto","box":[0,0,5,5],"rotation":0}]""", "layer 1 (text) has an empty font family")]
    [DataRow("""[{"kind":"text","preset":"meme","text":"x","font":"Impact","fontSize":9.5,"fill":"#ffffffff","outline":"auto","box":[0,0,5,5],"rotation":0}]""", "layer 1 (text) has a \"fontSize\" that is not a whole number")]
    [DataRow("""[{"kind":"text","preset":"meme","text":"x","font":"Impact","fontSize":9,"fill":"#ffffffff","outline":"auto","box":[0,0,5,5],"rotation":"Infinity"}]""", "layer 1 (text) has a \"rotation\" that is not a number")]
    [DataRow("""[{"kind":"text","preset":"meme","text":"x","font":"Impact","fontSize":9,"fill":"#ffffffff","outline":"auto","box":[0,0,5,5],"rotation":1e999}]""", "layer 1 (text) has a rotation that is not a number of degrees")]
    [DataRow("""[{"kind":"text","preset":"meme","text":"x","font":"Impact","fontSize":9,"fill":"#ffffffff","outline":"thick","box":[0,0,5,5],"rotation":0}]""", "layer 1 (text) has a \"outline\" that is not auto, none or a color #rrggbbaa")]
    [DataRow("""[{"kind":"captionBar","text":"x","font":"Impact","fontSize":0,"fill":"#ffffffff","outline":"none"}]""", "layer 1 (caption bar) has a font size below 1")]
    [DataRow("""[{"kind":"shrink","scale":"2/3"}]""", "layer 1 (shrink) has a \"scale\" that is not one of 1, 3/4, 1/2")]
    [DataRow("""[{"kind":"trim"}]""", "layer 1 (trim) has no \"range\"")]
    public void AColumnTheModelCannotReadIsAFailureNamingTheItem(string json, string reason)
    {
        var read = LayerList.FromJson(json, Item);

        Assert.AreEqual(new Failure("Read the item's layers", Item, reason), read.Failure);
    }

    [TestMethod]
    public void AMissingColumnIsAFailureNamingTheItem()
    {
        var read = LayerList.FromJson(null!, Item);

        Assert.AreEqual(new Failure("Read the item's layers", Item, "its layers are missing"), read.Failure);
    }

    private static LayerList EveryKind => new(
    [
        new TextLayer(TextPreset.Meme, "WHEN IT COMPILES", "Impact", 48, new(255, 255, 255, 255), Outline.Auto, new(10, 20, 300, 60), -15.5, new(0, 4)),
        new CaptionBarLayer("me, later", "Segoe UI", 16, new(0, 0, 0, 255), Outline.None),
        new TextLayer(TextPreset.Label, "x", "Georgia", 12, new(16, 32, 48, 255), Outline.Of(new(255, 255, 255, 128)), new(0, 0, 5, 5), 0, null),
        new HideLayer(new(1, 2, 30, 40), HideKind.Blur, null),
        new HideLayer(new(5, 6, 7, 8), HideKind.Black, new(2, 3)),
        new DrawMarkLayer(MarkKind.Pen, [new(1, 1), new(5, 9), new(12, 3)], null, new(200, 30, 30, 255), Thickness.Thin, null),
        new DrawMarkLayer(MarkKind.Marker, [new(0, 0), new(40, 0)], null, new(255, 235, 59, 255), Thickness.Thick, new(1, 1)),
        new DrawMarkLayer(MarkKind.Arrow, [new(2, 2), new(20, 30)], null, new(0, 120, 212, 255), Thickness.Medium, null),
        new DrawMarkLayer(MarkKind.Box, [], new(3, 4, 50, 60), new(200, 30, 30, 255), Thickness.Medium, null),
        new CropLayer(new(0, 0, 320, 200)),
        new TrimLayer(new(1, 3)),
        new ShrinkLayer(Shrink.Half),
    ]);
}
