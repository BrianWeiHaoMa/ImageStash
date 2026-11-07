using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>G6's badge: an animated item names the file it is handed over as; a still, a still GIF included, carries none.</summary>
[TestClass]
public sealed class ThumbnailGridTests
{
    [TestMethod]
    public void AnAnimatedItemNamesItsFileAndAStillCarriesNoBadge()
    {
        Assert.AreEqual("GIF", TileBadge.Of(TestItems.Sized(1, 8, 6, animated: true) with { FileName = "dance.gif" }));
        Assert.AreEqual("WEBP", TileBadge.Of(TestItems.Sized(2, 8, 6, animated: true) with { FileName = "clap.webp" }));
        Assert.IsNull(TileBadge.Of(TestItems.Sized(3, 8, 6) with { FileName = "still.gif" }));
        Assert.IsNull(TileBadge.Of(TestItems.Sized(4, 8, 6) with { FileName = "still.webp" }));
    }

    [TestMethod]
    public async Task AnAnimatedWebpTheStoreSavedIsBadgedGifAndASavedStillWebpCarriesNone()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var clip = await stash.ImportFixture("clip", "animated.webp");
        var still = await stash.ImportFixture("still", "still.webp");
        Assert.AreEqual("WEBP", TileBadge.Of(clip));

        await stash.SaveEdit(clip, EditedItems.BlackBoxAndBox);
        await stash.SaveEdit(still, EditedItems.BlackBoxAndBox);

        var listed = (await stash.All()).ToDictionary(item => item.Id);
        Assert.AreEqual("GIF", TileBadge.Of(listed[clip.Id]), "the file handed over is its GIF render (G6, N7)");
        Assert.IsNull(TileBadge.Of(listed[still.Id]));
    }
}
