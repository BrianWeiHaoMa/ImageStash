using ImageStash.Store;
using ImageStash.Tests.Support;
using static ImageStash.Tests.Support.CaptionedItems;
using static ImageStash.Tests.Support.ExportedStash;

namespace ImageStash.Tests.Store;

[TestClass]
public sealed class SavedLayerTextMigrationTests
{
    [TestMethod]
    public async Task VersionFourBackfillsAnEarlierEditAndLeavesTheOldStoreWholeIfTheStepFails()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var first = await Save(stash, await stash.ImportFixture("first", "still.png"), Caption("first caption"));
        var second = await Save(stash, await stash.ImportFixture("second", "transparent.png"), Caption("second caption"));
        stash.Close();
        VersionFourStore.Make(stash.Folder.StoreFile);
        var before = LayersOnDisk(stash);
        var files = stash.FilesAndUses();
        var schema = Schema(stash);
        // The step creates and fills the projection table before it reaches this trigger's already taken name.
        stash.Sql("""
            CREATE TRIGGER items_insert_layer_text AFTER INSERT ON items BEGIN SELECT 1; END;
            """);

        var opening = await stash.Open();

        Assert.IsNull(opening.Store);
        Assert.AreEqual("Open the stash", opening.Failures.Single().Action);
        StringAssert.Contains(opening.Failures.Single().Reason, "trigger items_insert_layer_text already exists");
        Assert.AreEqual(4L, StoreVersion(stash.Folder.StoreFile));
        CollectionAssert.AreEqual(before, LayersOnDisk(stash));
        CollectionAssert.AreEqual(files, stash.FilesAndUses());
        stash.Sql("DROP TRIGGER items_insert_layer_text");
        CollectionAssert.AreEqual(schema, Schema(stash), "the failed step added neither a table nor triggers");

        var reopened = await stash.OpenCleanly();
        Assert.AreEqual(5L, StoreVersion(stash.Folder.StoreFile));
        await HasText(reopened, first.Id, ["first caption"], []);
        await HasText(reopened, second.Id, ["second caption"], []);
    }

    [TestMethod]
    [DataRow("none")]
    [DataRow("layers")]
    [DataRow("previous_layers")]
    public async Task AnEarlierPendingSaveBackfillsBothListsIndependently(string damagedColumn)
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var item = await Save(stash, await stash.ImportFixture("pending edit", "still.png"), Caption("on disk"));
        var unrelated = await Save(stash, await stash.ImportFixture("other", "transparent.png"), Caption("different"));
        Assert.IsTrue((await store.AddTag([item], "pending")).Single().Succeeded);
        var next = Caption("committed");
        var render = await stash.Render(item, next);

        using var held = TestStash.Hold(item.FilePath);
        Assert.IsFalse((await store.Save(item, next, render)).Succeeded);
        stash.Close();
        VersionFourStore.Make(stash.Folder.StoreFile);
        if (damagedColumn != "none")
        {
            // A whole list rejected by the layer model even though its first layer is valid.
            var validThenInvalid = Caption("should never leak").ToJson()[..^1] + """,{"kind":"other"}]""";
            stash.Sql($"UPDATE items SET {damagedColumn} = $bad WHERE id = $id", ("$bad", validThenInvalid), ("$id", item.Id));
        }

        // With publication still held, prevent the normal startup rollback so both earlier lists remain observable.
        stash.Sql($"""
            CREATE TRIGGER keep_pending BEFORE UPDATE OF layers ON items
            WHEN OLD.id = {item.Id} AND NEW.pending_steps IS NULL
            BEGIN SELECT RAISE(ABORT, 'refused during rollback'); END;
            """);

        var opening = await stash.Open();

        Assert.IsNotNull(opening.Store, string.Join(", ", opening.Failures));
        Assert.HasCount(2, opening.Failures);
        await HasText(opening.Store, item.Id,
            damagedColumn == "layers" ? [] : ["committed"],
            damagedColumn == "previous_layers" ? [] : ["on disk"]);
        await HasText(opening.Store, unrelated.Id, ["different"], []);
        var itemRead = (await opening.Store.Item(item.Id)).Value!;
        Assert.AreEqual(("pending edit", "pending"), (itemRead.Name, itemRead.Tags.Single()));
        var edit = await opening.Store.ForEdit(item.Id);
        if (damagedColumn == "none")
        {
            Assert.AreEqual((next, Caption("on disk")), (edit.Value!.Layers, edit.Value.RenderedLayers));
        }
        else
        {
            Assert.IsFalse(edit.Succeeded);
            Assert.AreEqual("pending edit", edit.Failure!.Subject);
        }
    }

    [TestMethod]
    public async Task AnEarlierExportBackfillsItsOwnTextWhenItReplacesAnOverlappingId()
    {
        using var source = new TestStash();
        await source.OpenCleanly();
        var incoming = await Save(source, await source.ImportFixture("incoming", "still.png"), Caption("incoming caption"));
        var archive = await Export(source, MovingStash.Keeper(source, []));
        var olderFile = source.Outside("older-stash.db");
        File.WriteAllBytes(olderFile, Entry(archive, StashFolder.StoreFileName));
        VersionFourStore.Make(olderFile);
        var older = Rewritten(archive, source.Outside("older.zip"),
            (StashFolder.StoreFileName, File.ReadAllBytes(olderFile)), ("manifest.json", Manifest(storeVersion: 4, items: 1)));

        using var target = new TestStash();
        var store = await target.OpenCleanly();
        var outgoing = await Save(target, await target.ImportFixture("outgoing", "transparent.png"), Caption("outgoing caption"));
        Assert.AreEqual(incoming.Id, outgoing.Id);
        await HasText(store, outgoing.Id, ["outgoing caption"], []);

        var imported = await store.Import(MovingStash.Keeper(target, []), older);

        Assert.IsTrue(imported.Succeeded, imported.Failure?.ToString());
        target.Close();
        var reopened = await target.OpenCleanly();
        Assert.AreEqual(incoming.Name, (await target.All()).Single().Name);
        await HasText(reopened, incoming.Id, ["incoming caption"], []);
    }

    [TestMethod]
    [DataRow("CAST($json AS BLOB)")]
    [DataRow("$json || char(0)")]
    [DataRow("$json || CAST(x'80' AS TEXT)")]
    public async Task CoercingMalformedStorageDoesNotTurnItIntoSearchableText(string stored)
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var item = await stash.ImportFixture("still", "still.png");
        stash.Close();
        VersionFourStore.Make(stash.Folder.StoreFile);
        stash.Sql($"UPDATE items SET layers = {stored} WHERE id = $id",
            ("$json", Caption("do not index this").ToJson()), ("$id", item.Id));

        var reopened = await stash.OpenCleanly();

        await HasText(reopened, item.Id, [], []);
        Assert.AreEqual(item.Name, (await stash.All()).Single().Name);
    }

    [TestMethod]
    [DataRow(VersionFourStore.WithoutFifthStep)]
    [DataRow("DROP TRIGGER items_insert_layer_text")]
    [DataRow("DROP TRIGGER items_update_layer_text")]
    [DataRow("DROP TRIGGER items_delete_layer_text")]
    [DataRow("CREATE UNIQUE INDEX one_list_per_item ON saved_layer_text(item_id)")]
    [DataRow("DROP TRIGGER items_update_layer_text; CREATE TRIGGER items_update_layer_text AFTER UPDATE ON items BEGIN SELECT 1; END;")]
    [DataRow("""
        ALTER TABLE saved_layer_text RENAME TO unvalidated;
        CREATE TABLE saved_layer_text AS SELECT * FROM unvalidated;
        INSERT INTO saved_layer_text SELECT * FROM unvalidated;
        """)]
    public async Task AnArchiveClaimingVersionFiveWithoutItsProjectionSchemaIsRefusedBeforeReplacement(string missing)
    {
        using var source = new TestStash();
        await source.OpenCleanly();
        await Save(source, await source.ImportFixture("incoming", "still.png"), Caption("incoming caption"));
        var archive = WithStore(await Export(source, MovingStash.Keeper(source, [])), source.Outside("incomplete.zip"), missing);
        using var target = new TestStash();
        var store = await target.OpenCleanly();
        var outgoing = await Save(target, await target.ImportFixture("outgoing", "transparent.png"), Caption("outgoing caption"));

        var imported = await store.Import(MovingStash.Keeper(target, []), archive);

        Assert.IsFalse(imported.Succeeded);
        Assert.AreEqual("incomplete.zip", imported.Failure!.Subject);
        Assert.AreEqual(TransferReasons.NotAnExport, imported.Failure.Reason);
        Assert.AreEqual(outgoing.Name, (await target.All()).Single().Name);
        await HasText(store, outgoing.Id, ["outgoing caption"], []);
        Assert.IsEmpty(Beside(target));
    }

    [TestMethod]
    public async Task ImportDerivesTextFromTheSourceListsEvenIfTheArchiveHasStaleText()
    {
        using var source = new TestStash();
        await source.OpenCleanly();
        var edited = await Save(source, await source.ImportFixture("edited", "still.png"), Caption("source text"));
        var damaged = await Save(source, await source.ImportFixture("damaged", "transparent.png"), Caption("discarded text"));
        var archive = WithStore(await Export(source, MovingStash.Keeper(source, [])), source.Outside("stale.zip"), $$"""
            UPDATE items SET layers = '{' WHERE id = {{damaged.Id}};
            UPDATE saved_layer_text SET strings = '["stale text"]';
            """);
        using var target = new TestStash();
        var store = await target.OpenCleanly();

        var imported = await store.Import(MovingStash.Keeper(target, []), archive);

        Assert.IsTrue(imported.Succeeded, imported.Failure?.ToString());
        target.Close();
        var reopened = await target.OpenCleanly();
        await HasText(reopened, edited.Id, ["source text"], []);
        await HasText(reopened, damaged.Id, [], []);
        Assert.AreEqual(damaged.Name, (await reopened.ForEdit(damaged.Id)).Failure!.Subject);
    }

    [TestMethod]
    [DataRow("UTF-16le")]
    [DataRow("UTF-16be")]
    public async Task StagingPreservesTextInEitherSQLiteUtf16Encoding(string encoding)
    {
        using var source = new TestStash();
        Directory.CreateDirectory(source.Folder.Root);
        source.Sql($"PRAGMA encoding = '{encoding}'; CREATE TABLE seed (value); DROP TABLE seed;");
        Assert.AreEqual(encoding, source.SqlQuery("PRAGMA encoding", row => row.GetString(0)).Single());
        await source.OpenCleanly();
        const string caption = "éclair 😺";
        var edited = await Save(source, await source.ImportFixture("edited", "still.png"), Caption(caption));
        var archive = await Export(source, MovingStash.Keeper(source, []));
        using var target = new TestStash();
        var store = await target.OpenCleanly();

        var imported = await store.Import(MovingStash.Keeper(target, []), archive);

        Assert.IsTrue(imported.Succeeded, imported.Failure?.ToString());
        target.Close();
        var reopened = await target.OpenCleanly();
        await HasText(reopened, edited.Id, [caption], []);
    }

    private static List<(long Id, string Layers)> LayersOnDisk(TestStash stash) =>
        stash.SqlQuery("SELECT id, layers FROM items ORDER BY id", row => (row.GetInt64(0), row.GetString(1)));

    private static List<string> Schema(TestStash stash) =>
        stash.SqlQuery("SELECT sql FROM sqlite_schema WHERE sql IS NOT NULL ORDER BY name", row => row.GetString(0));
}
