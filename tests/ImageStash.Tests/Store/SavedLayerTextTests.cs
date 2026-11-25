using System.Text.RegularExpressions;
using ImageStash.Files;
using ImageStash.Layers;
using ImageStash.Store;
using ImageStash.Tests.Support;
using static ImageStash.Tests.Support.CaptionedItems;

namespace ImageStash.Tests.Store;

[TestClass]
public sealed class SavedLayerTextTests
{
    private const string Meme = "  mixed Case 😺\r\n";
    private const string Label = "half\t an éclair";
    private const string Bar = "row\u0000 end ";
    private static readonly Rgba Black = new(0, 0, 0, 255);

    [TestMethod]
    public async Task SaveKeepsEachTextWholeAndSaveAsNewUsesItsOwnLayers()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var item = await stash.ImportFixture("clip", "animated.gif");
        var layers = new LayerList([
            new TextLayer(TextPreset.Meme, Meme, "Impact", 12, Black, Outline.None, new(-30, -30, 4, 4), 0, new(0, 0)),
            new HideLayer(new(0, 0, 4, 4), HideKind.Black, null),
            new CaptionBarLayer(Bar, "Georgia", 12, Black, Outline.None),
            new TextLayer(TextPreset.Label, Label, "Consolas", 12, Black, Outline.None, new(0, 0, 4, 4), 0, null),
            new CropLayer(new(0, 0, 4, 4)),
        ]);
        var saved = await Save(stash, item, layers);
        await HasText(store, saved.Id, [Meme, Bar, Label], []);

        var copyLayers = Caption("for the copy");
        var made = await store.SaveAsNewItem(saved, copyLayers, await stash.Render(saved, copyLayers));
        Assert.IsTrue(made.Succeeded, made.Failure?.ToString());
        await HasText(store, made.Value!.Id, ["for the copy"], []);
        await HasText(store, saved.Id, [Meme, Bar, Label], []);

        var reverted = await store.Save(saved, LayerList.Empty, null);
        Assert.IsTrue(reverted.Succeeded, reverted.Failure?.ToString());
        await HasText(store, saved.Id, [], []);
        await HasText(store, made.Value.Id, ["for the copy"], []);
    }

    [TestMethod]
    public async Task AnAdoptionDropsThePreviousPictureTextAndUndoRestoresIt()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var item = await stash.ImportFixture("still", "still.png");
        var saved = await Save(stash, item, Caption("restore this"));
        await OpenedWith.AwaitingRewrite(stash, saved, MediaFixtures.Bytes("photo.jpg"));

        var adopted = await OpenedWith.Adopt(stash, saved.Id);
        await HasText(store, adopted.Id, [], []);

        await OpenedWith.UndoAdoption(stash, adopted.Id, adopted.RenderedRevision);
        await HasText(store, adopted.Id, ["restore this"], []);
    }

    [TestMethod]
    public async Task APendingAdoptionKeepsTheTextOfThePreviousPictureUntilItFinishes()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var item = await stash.ImportFixture("still", "still.png");
        var saved = await Save(stash, item, Caption("for the original"));
        await OpenedWith.AwaitingRewrite(stash, saved, MediaFixtures.Bytes("photo.jpg"));

        // Adoption commits its empty list before it can remove the old render.
        using (TestStash.Hold(saved.FilePath))
        {
            var refused = await store.AdoptRewrite(saved.Id);
            Assert.IsFalse(refused.Succeeded);
            Assert.AreEqual(HeldFileRetry.HeldReason, refused.Failure!.Reason);
            await HasText(store, saved.Id, [], ["for the original"]);
        }

        stash.Close();
        await stash.OpenCleanly();
        await HasText(stash.Store!, saved.Id, [], []);
    }

    [TestMethod]
    public async Task ADeleteMarkHidesTheTextWhileUndoCanStillRestoreIt()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var item = await Save(stash, await stash.ImportFixture("still", "still.png"), Caption("remember"));
        var deleted = (await store.Delete([item.Id])).Single();
        Assert.IsTrue(deleted.Succeeded, deleted.Failure?.ToString());
        Assert.IsFalse((await Read(store)).ContainsKey(item.Id));

        Assert.IsTrue((await store.UndoDelete([deleted.Value!])).Single().Succeeded);
        await HasText(store, item.Id, ["remember"], []);

        Assert.IsTrue((await store.Delete([item.Id])).Single().Succeeded);
        Assert.IsTrue((await store.FinishDelete([item.Id])).Single().Succeeded);
        Assert.IsFalse((await Read(store)).ContainsKey(item.Id));
        Assert.IsEmpty(stash.SqlQueryWhileOpen("SELECT id FROM items", row => row.GetInt64(0)));
        Assert.IsEmpty(stash.SqlQueryWhileOpen("SELECT item_id FROM saved_layer_text", row => row.GetInt64(0)));
    }

    [TestMethod]
    public async Task ReopeningRollsBackASecondSaveAndRestoresItsPreviousText()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var item = await Save(stash, await stash.ImportFixture("still", "still.png"), Caption("published"));
        var next = Caption("waiting to publish");
        var render = await stash.Render(item, next);

        using (TestStash.Hold(item.FilePath))
        {
            var refused = await store.Save(item, next, render);
            Assert.IsFalse(refused.Succeeded);
            Assert.AreEqual(HeldFileRetry.HeldReason, refused.Failure!.Reason);
            await HasText(store, item.Id, ["waiting to publish"], ["published"]);

            var opening = await stash.Reopen();
            Assert.AreEqual("Finish an unfinished save", opening.Failures.Single().Action);
            await HasText(opening.Store!, item.Id, ["published"], []);
            Assert.AreEqual(item.RenderedRevision, (await opening.Store!.Item(item.Id)).Value!.RenderedRevision);
        }
    }

    [TestMethod]
    public async Task CaptionedItemsAtStashCapacityUseOneBulkRead()
    {
        var sql = new List<string>();
        var (read, statements) = await StoreStatements.Count(now => CaptionedItems.Fill(now), SavedLayerText.ReadSnapshot, sql.Add);

        Assert.IsTrue(read.Succeeded, read.Failure?.ToString());
        Assert.HasCount(CaptionedItems.Count, read.Value!);
        Assert.IsLessThanOrEqualTo(3, statements, "one SELECT and its transaction");
        Assert.IsFalse(sql.Any(statement => Regex.IsMatch(statement, @"\b(?:previous_)?layers\b", RegexOptions.IgnoreCase)),
            "the bulk read never loads the drawing payloads");
        for (var id = 1; id <= CaptionedItems.Count; id++)
        {
            Assert.AreEqual($"saved caption {id}", read.Value![id].Current.Single());
            Assert.IsEmpty(read.Value[id].Previous);
        }
    }
}
