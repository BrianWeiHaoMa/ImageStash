using ImageStash.Layers;
using ImageStash.Store;
using ImageStash.Tests.Support;
using static ImageStash.Tests.Support.CaptionedItems;

namespace ImageStash.Tests.Store;

[TestClass]
public sealed class SavedPictureSearchTests
{
    private static readonly Rgba Black = new(0, 0, 0, 255);

    [TestMethod]
    public async Task EveryWordCanMatchADifferentNameTagOrLayerAndTextStaysInItsOwnLayer()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var item = await stash.ImportFixture("Otter", "animated.gif");
        Assert.IsTrue((await store.AddTag([item], "wildlife")).Single().Succeeded);
        var saved = await Save(stash, item, new LayerList([
            new TextLayer(TextPreset.Meme, "jubilo", "Impact", 12, Black, Outline.None, new(-30, -30, 4, 4), 0, new(0, 0)),
            new TextLayer(TextPreset.Label, "BÄRCHEN", "Consolas", 12, Black, Outline.None, new(0, 0, 8, 6), 0, null),
            new CaptionBarLayer("river", "Georgia", 12, Black, Outline.None),
            new HideLayer(new(0, 0, 2, 2), HideKind.Black, null),
        ]));
        await Finds(store, "  tter #WILD  #bÄrc  ", saved.Id);
        await Finds(store, "jubILO", saved.Id);
        await Finds(store, "ärchen", saved.Id);
        await Finds(store, "river", saved.Id);
        await Finds(store, "jubilo ärchen river", saved.Id);
        await Finds(store, "lobä");
        await Finds(store, "Impact");
        await Finds(store, "Consolas");
        await Finds(store, "Georgia");
        await Finds(store, "Label");
        await Finds(store, "Meme");
        await Finds(store, "hide");
    }

    [TestMethod]
    public async Task AVisibleCaptionIsSearchableOnlyAsSavedTextAndBySubstring()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var original = await stash.ImportPicture("Road", "road.png", MediaFixtures.DistinctPng(1, 512, 128));
        var saved = await Save(stash, original,
            new LayerList([new CaptionBarLayer("TURN LEFT", "Segoe UI", 36, Black, Outline.None)]));
        var flattened = await stash.ImportPicture("Rendered copy", saved.FileName, File.ReadAllBytes(saved.FilePath));

        await Finds(store, "urn left", saved.Id);
        await Finds(store, "turrn");
        await Finds(store, "Rendered", flattened.Id);
    }

    [TestMethod]
    public async Task OnlyNameAndTagWordStartsGetPriorityAndBothGroupsKeepTheirUseAndAddedOrder()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var now = stash.Clock.Now;
        var namePrefix = await stash.Import("cat nap");
        var tagPrefix = await stash.Import("cat resting");
        var unusedPrefixOld = await stash.Import("cat nap first");
        stash.Clock.Now = now.AddSeconds(1);
        var unusedPrefixNew = await stash.Import("cat nap second");
        Assert.IsTrue((await store.AddTag([tagPrefix], "nap")).Single().Succeeded);

        var captionOnly = await Save(stash, await stash.ImportFixture("print", "still.png"), Caption("cat nap"));
        var mixedPrefix = await Save(stash, await stash.ImportFixture("cat", "transparent.png"), Caption("nap"));
        var unusedCaptionOld = await Save(stash, await stash.ImportFixture("before", "still.webp"), Caption("cat nap"));
        stash.Clock.Now = now.AddSeconds(2);
        var unusedCaptionNew = await Save(stash, await stash.ImportFixture("after", "photo.jpg"), Caption("cat nap"));
        Assert.IsTrue((await store.RecordUses([captionOnly], now.AddMinutes(3))).Succeeded);
        Assert.IsTrue((await store.RecordUses([mixedPrefix], now.AddMinutes(2))).Succeeded);
        Assert.IsTrue((await store.RecordUses([tagPrefix], now.AddMinutes(1))).Succeeded);
        Assert.IsTrue((await store.RecordUses([namePrefix], now)).Succeeded);

        await Finds(store, "cat nap", tagPrefix.Id, namePrefix.Id, unusedPrefixNew.Id, unusedPrefixOld.Id,
            captionOnly.Id, mixedPrefix.Id, unusedCaptionNew.Id, unusedCaptionOld.Id);
        await Finds(store, "  #  ", unusedCaptionNew.Id, unusedCaptionOld.Id, mixedPrefix.Id, captionOnly.Id,
            unusedPrefixNew.Id, unusedPrefixOld.Id, tagPrefix.Id, namePrefix.Id);
    }

    [TestMethod]
    public async Task ADeletedPictureIsHiddenFromCaptionSearchUntilUndoRestoresIt()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var item = await Save(stash, await stash.ImportFixture("still", "still.png"), Caption("remember"));
        var finished = File.ReadAllBytes(item.FilePath);
        await Finds(store, "remember", item.Id);

        var deleted = (await store.Delete([item.Id])).Single();
        Assert.IsTrue(deleted.Succeeded, deleted.Failure?.ToString());
        await Finds(store, "remember");
        Assert.IsEmpty(await stash.All());

        Assert.IsTrue((await store.UndoDelete([deleted.Value!])).Single().Succeeded);
        var restored = (await Finds(store, "remember", item.Id)).Single();
        CollectionAssert.AreEqual(finished, File.ReadAllBytes(restored.FilePath));
    }
}
