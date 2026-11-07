using System.Globalization;
using ImageStash.Layers;
using ImageStash.Media;
using ImageStash.Store;
using ImageStash.Tests.Support;

namespace ImageStash.Tests.Store;

/// <summary>F11 and State: The store: a store below this build's version is brought up in one transaction before anything reads.</summary>
[TestClass]
public sealed class StoreSchemaTests
{
    private static readonly string[] Tables = ["items", "tags", "uses"];

    private static readonly string[] FinishedFacts = VersionOneStore.SecondStepColumns;

    [TestMethod]
    public async Task AStoreAtVersionZeroHoldingPartOfTheSchemaIsCompleted()
    {
        using var stash = new TestStash();
        Directory.CreateDirectory(stash.Folder.Root);
        stash.Sql("CREATE TABLE tags (item_id INTEGER NOT NULL, tag TEXT NOT NULL, PRIMARY KEY (item_id, tag))");

        await stash.OpenCleanly();
        stash.Close();

        Assert.AreEqual((long)StoreSchema.CurrentVersion, stash.SqlQuery("PRAGMA user_version", r => r.GetInt64(0)).Single());
        CollectionAssert.AreEquivalent(Tables, TableNames(stash));
    }

    [TestMethod]
    public async Task AWalStoreAtVersionZeroIsInitializedInDeleteMode()
    {
        using var stash = new TestStash();
        Directory.CreateDirectory(stash.Folder.Root);
        stash.Sql("PRAGMA journal_mode = WAL");

        await stash.OpenCleanly();
        stash.Close();

        Assert.AreEqual((long)StoreSchema.CurrentVersion, stash.SqlQuery("PRAGMA user_version", r => r.GetInt64(0)).Single());
        Assert.AreEqual(1, File.ReadAllBytes(stash.Folder.StoreFile)[18]);
        Assert.IsFalse(File.Exists(stash.Folder.StoreFile + "-wal"));
    }

    [TestMethod]
    public async Task AMigrationThatFailsPartWayLeavesTheStoreAtItsVersionWithNoneOfItsChanges()
    {
        using var stash = new TestStash();
        Directory.CreateDirectory(stash.Folder.Root);
        // A uses table without the columns the schema indexes: its step fails after creating items and tags.
        stash.Sql("CREATE TABLE uses (x)");

        var opening = await stash.Open();

        Assert.IsNull(opening.Store);
        Assert.AreEqual("Open the stash", opening.Failures.Single().Action);
        stash.Close();
        Assert.AreEqual(0L, stash.SqlQuery("PRAGMA user_version", r => r.GetInt64(0)).Single());
        CollectionAssert.AreEquivalent(new[] { "uses" }, TableNames(stash));
    }

    [TestMethod]
    public async Task AFreshStoreKeepsTheFinishedPicturesFactsAfterTheOriginalsRecordEachNullableWithoutADefault()
    {
        using var stash = new TestStash();

        await stash.OpenCleanly();
        stash.Close();

        var columns = Columns(stash);
        CollectionAssert.AreEqual(FinishedFacts, columns.Select(c => c.Name).SkipWhile(name => name != "adoption_undo").Skip(1).ToList());
        Assert.IsTrue(columns.Where(c => FinishedFacts.Contains(c.Name)).All(c => !c.NotNull && c.Default is null));
        CollectionAssert.IsSubsetOf(new[] { "format", "width", "height", "animated", "frame_count", "duration_ms" }, columns.Select(c => c.Name).ToList());
    }

    [TestMethod]
    public async Task AStoreAnEarlierBuildWroteAtVersionOneOpensAtTheCurrentVersionWithEveryItemAsItWasAndAnEditedOnesRenderFactsRead()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        // Pictures, since bringing a store up reads its photos' and its renders' headers again.
        var still = await stash.ImportFixture("still", "still.png");
        var clip = await stash.ImportFixture("clip", "animated.gif");
        var webp = await stash.ImportFixture("webp", "still.webp");
        await store.AddTag([clip], "mood");
        await store.SetFavorite([webp], true);
        await store.RecordUses([still, clip], stash.Clock.Now);
        stash.Close();
        var layers = new LayerList([new CropLayer(new(0, 0, 4, 4)), new HideLayer(new(0, 0, 1, 1), HideKind.Blur, null)]);
        stash.MakeEdited(clip, MediaFixtures.Bytes("still.gif"));
        stash.Sql("UPDATE items SET layers = $layers, layer_revision = 1, rendered_revision = 1 WHERE id = $id", ("$id", clip.Id), ("$layers", layers.ToJson()));
        await stash.OpenCleanly();
        var before = await stash.All();
        stash.Close();
        VersionOneStore.Make(stash.Folder.StoreFile);
        var rowsBefore = Rows(stash);
        var filesBefore = stash.FilesAndUses();

        await stash.OpenCleanly();
        var after = await stash.All();
        var read = await stash.Store!.Item(clip.Id);
        var edit = await stash.Store!.ForEdit(clip.Id);
        stash.Close();

        Assert.AreEqual((long)StoreSchema.CurrentVersion, stash.SqlQuery("PRAGMA user_version", r => r.GetInt64(0)).Single());
        Assert.HasCount(3, after);
        // Version 1 recorded no render facts, so the edited clip listed its original's; brought up, it lists the render
        // it hands over, a still GIF (G6, G19).
        var render = new MediaFacts(ImageFormat.Gif, 8, 6, false, 1, 0);
        foreach (var (was, now) in before.Zip(after))
        {
            TestStash.AssertSameItem(was.Id == clip.Id ? was with { Facts = render } : was, now);
        }

        TestStash.AssertSameItem(before.Single(item => item.Id == clip.Id) with { Facts = render }, read.Value);
        CollectionAssert.AreEqual(new[] { ImageFormat.WebP, ImageFormat.Gif, ImageFormat.Png }, after.Select(item => item.Facts.Format).ToList());
        Assert.AreEqual(("mood", true), (after[1].Tags.Single(), after[0].Favorite));
        Assert.AreEqual(still.FilePath, after[2].FilePath);
        Assert.AreEqual(layers, edit.Value!.Layers);
        CollectionAssert.AreEqual(rowsBefore, Rows(stash), "every value version 1 held");
        CollectionAssert.AreEqual(
            new[] { clip.Id },
            stash.SqlQuery($"SELECT id FROM items WHERE {string.Join(" OR ", FinishedFacts.Select(c => c + " IS NOT NULL"))}", r => r.GetInt64(0)),
            "only the edited item's render facts are recorded");
        CollectionAssert.AreEqual(filesBefore, stash.FilesAndUses());
    }

    [TestMethod]
    public async Task AStoreAtVersionZeroWhoseSecondStepFailsKeepsNothingOfTheFirstStepEither()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        stash.Close();
        // An items table the first step leaves as it is, holding one of the second step's columns: the first step runs
        // whole, creating tags and uses, and the second fails part way.
        foreach (var column in FinishedFacts.Where(column => column != "finished_frame_count"))
        {
            stash.Sql($"ALTER TABLE items DROP COLUMN {column}");
        }

        stash.Sql("DROP TABLE tags; DROP TABLE uses; PRAGMA user_version = 0;");

        var opening = await stash.Open();

        Assert.IsNull(opening.Store);
        Assert.AreEqual("Open the stash", opening.Failures.Single().Action);
        stash.Close();
        Assert.AreEqual(0L, stash.SqlQuery("PRAGMA user_version", r => r.GetInt64(0)).Single());
        CollectionAssert.AreEquivalent(new[] { "items" }, TableNames(stash), "the first step's tables kept though the second step failed");
        CollectionAssert.AreEqual(new[] { "finished_frame_count" }, Columns(stash).Select(c => c.Name).Where(FinishedFacts.Contains).ToList());
    }

    [TestMethod]
    public async Task AVersionOneStoreTheSecondStepCannotCompleteStaysAtVersionOneWithNoneOfThatStepsColumns()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        await stash.Import("kept");
        stash.Close();
        VersionOneStore.Make(stash.Folder.StoreFile);
        // One of the step's columns already there: the step fails on it after adding the four before it.
        stash.Sql("ALTER TABLE items ADD COLUMN finished_frame_count INTEGER");

        var opening = await stash.Open();

        Assert.IsNull(opening.Store);
        Assert.AreEqual("Open the stash", opening.Failures.Single().Action);
        stash.Close();
        Assert.AreEqual(1L, stash.SqlQuery("PRAGMA user_version", r => r.GetInt64(0)).Single());
        CollectionAssert.AreEqual(new[] { "finished_frame_count" }, Columns(stash).Select(c => c.Name).Where(FinishedFacts.Contains).ToList());
        Assert.AreEqual(1L, stash.SqlQuery("SELECT COUNT(*) FROM items", r => r.GetInt64(0)).Single());
    }


    /// <summary>Every value of items' version 1 columns, of tags and of uses, row by row, read with the store closed.</summary>
    private static List<string> Rows(TestStash stash)
    {
        var itemColumns = string.Join(", ", Columns(stash).Select(c => c.Name).Where(name => !FinishedFacts.Contains(name)));
        return
        [
            .. new[] { $"SELECT {itemColumns} FROM items", "SELECT * FROM tags", "SELECT * FROM uses" }.SelectMany(sql => stash.SqlQuery(
                sql + " ORDER BY rowid",
                r => string.Join("|", Enumerable.Range(0, r.FieldCount).Select(i => r.IsDBNull(i) ? "NULL"
                    : r.GetValue(i) is byte[] bytes ? Convert.ToHexString(bytes)
                    : Convert.ToString(r.GetValue(i), CultureInfo.InvariantCulture))))),
        ];
    }

    private static List<(string Name, bool NotNull, string? Default)> Columns(TestStash stash) =>
        stash.SqlQuery("PRAGMA table_info(items)", r => (r.GetString(1), r.GetBoolean(3), r.IsDBNull(4) ? null : r.GetString(4)));

    private static List<string> TableNames(TestStash stash) =>
        stash.SqlQuery("SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%'", r => r.GetString(0));
}
