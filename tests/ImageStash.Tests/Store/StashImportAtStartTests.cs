using System.IO.Compression;
using System.Runtime.Versioning;
using ImageStash.Files;
using ImageStash.Store;
using ImageStash.Tests.Support;
using static ImageStash.Tests.Support.ExportedStash;
using static ImageStash.Tests.Support.MovingStash;

namespace ImageStash.Tests.Store;

/// <summary>
/// What a kill or a held file leaves of an import after its record, finished at the next start (G36's last clause, G28,
/// G35, State: Import stash…): every step a kill can stop at finished from what the names on disk show, the marker
/// deciding where they cannot, nothing done twice; the extracted folder vanished, the stash and it both gone, and none of
/// the three there; what a kill before the record or after the last write left swept; a step after the record refused
/// past its retries answered as a failure carrying the stash, the record kept until a start finishes it.
/// </summary>
[TestClass]
[SupportedOSPlatform("windows")]
public sealed class StashImportAtStartTests
{
    private readonly List<Failure> failures = [];

    private SettingsKeeper Keeper(TestStash stash) => MovingStash.Keeper(stash, failures);

    [TestMethod]
    [DataRow(0, DisplayName = "extracted, marked and recorded")]
    [DataRow(1, DisplayName = "the stash renamed away")]
    [DataRow(2, DisplayName = "the extracted folder renamed in")]
    [DataRow(3, DisplayName = "the replaced folder recycled, the last write not made")]
    [DataRow(4, DisplayName = "the last write made, the marker still in the root")]
    [DataRow(5, DisplayName = "extracted, with the stash and the replaced folder both gone")]
    public async Task AKillAtEveryStepAfterTheRecordIsFinishedAtTheNextStartWithNothingDoneTwice(int step)
    {
        using var source = new TestStash();
        var archive = await Archive(source);
        using var stash = new TestStash();
        var keeper = Keeper(stash);
        var record = await Recorded(stash, keeper, archive);
        var replacedTree = Tree(stash.Folder.Root);
        if (step == 5)
        {
            Directory.Delete(stash.Folder.Root, recursive: true);
        }

        if (step is >= 1 and <= 4)
        {
            Directory.Move(stash.Folder.Root, record.ReplacedFolder);
        }

        if (step is >= 2 and <= 4)
        {
            Directory.Move(record.ExtractedFolder, stash.Folder.Root);
        }

        if (step is 3 or 4)
        {
            Assert.IsTrue(stash.RecycleBin.Recycle([record.ReplacedFolder], TimeSpan.Zero).Single().Succeeded);
        }

        if (step == 4)
        {
            Assert.IsTrue((await keeper.ChangeNow(s => record.Settings.AppliedTo(s) with { ImportPending = null })).Succeeded);
        }

        var finished = await StashImport.FinishAtStart(Keeper(stash), stash.RecycleBin, stash.Folder.Root, stash.Retry);

        Assert.AreEqual("", string.Join("; ", finished));
        Imported(stash, archive, record);
        Assert.HasCount(step == 5 ? 0 : 1, stash.RecycleBin.Asked, "the replaced folder recycled once, and only it");
        if (step != 5)
        {
            CollectionAssert.AreEquivalent(replacedTree, Tree(Directory.GetDirectories(stash.RecycleBin.Folder).Single()));
        }

        var again = await StashImport.FinishAtStart(Keeper(stash), stash.RecycleBin, stash.Folder.Root, stash.Retry);

        Assert.IsEmpty(again, "a second start changes nothing");
        Imported(stash, archive, record);
        Assert.HasCount(step == 5 ? 0 : 1, stash.RecycleBin.Asked);
    }

    [TestMethod]
    public async Task ARecordWhoseExtractedFolderVanishedBeforeItWasRenamedInIsClearedWithTheStashUntouched()
    {
        using var source = new TestStash();
        var archive = await Archive(source);
        using var stash = new TestStash();
        var keeper = Keeper(stash);
        var record = await Recorded(stash, keeper, archive);
        var before = Saved(stash);
        var tree = Tree(stash.Folder.Root);
        Directory.Delete(record.ExtractedFolder, recursive: true);

        var finished = await StashImport.FinishAtStart(Keeper(stash), stash.RecycleBin, stash.Folder.Root, stash.Retry);

        Assert.IsEmpty(finished);
        CollectionAssert.AreEqual(tree, Tree(stash.Folder.Root));
        Assert.AreEqual(before with { ImportPending = null }, Saved(stash), "the record cleared, and the settings it carried never applied");
        Assert.IsEmpty(stash.RecycleBin.Asked);
    }

    [TestMethod]
    public async Task ARecordWithTheStashAndTheExtractedFolderBothGoneRenamesTheReplacedFolderBack()
    {
        using var source = new TestStash();
        var archive = await Archive(source);
        using var stash = new TestStash();
        var keeper = Keeper(stash);
        var record = await Recorded(stash, keeper, archive);
        var tree = Tree(stash.Folder.Root);
        Directory.Move(stash.Folder.Root, record.ReplacedFolder);
        Directory.Delete(record.ExtractedFolder, recursive: true);

        var finished = await StashImport.FinishAtStart(Keeper(stash), stash.RecycleBin, stash.Folder.Root, stash.Retry);

        Assert.IsEmpty(finished);
        CollectionAssert.AreEqual(tree, Tree(stash.Folder.Root), "the stash whole where it was");
        Assert.IsNull(Saved(stash).ImportPending);
        Assert.IsEmpty(Beside(stash));
        Assert.IsEmpty(stash.RecycleBin.Asked);
    }

    [TestMethod]
    public async Task ARecordWithNoneOfItsFoldersThereFailsClosedCreatingNothingAndKeepingTheRecord()
    {
        using var source = new TestStash();
        var archive = await Archive(source);
        using var stash = new TestStash();
        var keeper = Keeper(stash);
        var record = await Recorded(stash, keeper, archive);
        Directory.Delete(stash.Folder.Root, recursive: true);
        Directory.Delete(record.ExtractedFolder, recursive: true);

        var finished = await StashImport.FinishAtStart(Keeper(stash), stash.RecycleBin, stash.Folder.Root, stash.Retry);

        Assert.AreEqual(StashImport.FinishAction, finished.Single().Action);
        Assert.AreEqual("stash", finished.Single().Subject);
        Assert.IsFalse(Directory.Exists(stash.Folder.Root), "nothing created in the stash's place");
        Assert.AreEqual(record, Saved(stash).ImportPending);
    }

    [TestMethod]
    public async Task WithoutARecordAnExtractedFolderBesideTheStashAndAMarkerInItsRootAreDeletedAndAnItemNamedLikeOneIsNot()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var item = await stash.ImportPicture(".imagestash-import-mine", "mine.png", MediaFixtures.DistinctPng(4));
        stash.Close();
        var tree = Tree(stash.Folder.Root);
        var orphan = Directory.CreateDirectory(stash.Folder.Root + ".importing-0123456789ab").FullName;
        File.WriteAllBytes(Path.Combine(orphan, "stash.db"), [1, 2, 3]);
        // A folder of the person's own whose name only begins like one.
        var theirs = Directory.CreateDirectory(stash.Folder.Root + ".importing-photos").FullName;
        File.WriteAllBytes(Path.Combine(theirs, "holiday.png"), MediaFixtures.DistinctPng(6));
        var marker = stash.Folder.InRoot(".imagestash-import-0123456789ab");
        File.WriteAllBytes(marker, []);

        var finished = await StashImport.FinishAtStart(Keeper(stash), stash.RecycleBin, stash.Folder.Root, stash.Retry);

        Assert.IsEmpty(finished);
        Assert.IsFalse(Directory.Exists(orphan));
        Assert.IsTrue(File.Exists(Path.Combine(theirs, "holiday.png")), "only a folder an import names so is taken for its own");
        Assert.IsFalse(File.Exists(marker));
        CollectionAssert.AreEqual(tree, Tree(stash.Folder.Root), "an item's file is never a marker, whatever its name");
        Assert.IsTrue(File.Exists(item.FilePath));
    }

    [TestMethod]
    [DoNotParallelize]
    public async Task AFileInsideTheExtractedFolderLetGoDuringTheRetriesAtTheRenameInLetsTheImportFinish()
    {
        using var source = new TestStash();
        var archive = await Archive(source);
        using var stash = new TestStash { Retry = HeldFileRetry.Duration };
        var store = await stash.OpenCleanly();
        var keeper = Keeper(stash);
        FileStream? held = null;

        var imported = await store.Import(keeper, archive, new TransferReported(progress =>
        {
            if (progress is { Phase: TransferPhase.Replacing, Done: 1 })
            {
                var record = keeper.Current.ImportPending!;
                held = TestStash.Hold(Path.Combine(record.ExtractedFolder, StashFolder.StoreFileName));
                // Let go from the retry's own wait: the read is answered there, between two tries.
                _ = store.All().ContinueWith(_ => held.Dispose(), TaskScheduler.Default);
            }
        }));

        Assert.IsNotNull(held, "the hold was taken at the rename-in");
        Assert.IsTrue(imported.Succeeded, imported.Failure?.ToString());
        Assert.IsNull(Saved(stash).ImportPending);
        CollectionAssert.AreEquivalent(TreeOf(archive), Tree(stash.Folder.Root));
    }

    [TestMethod]
    [DoNotParallelize]
    [DataRow("rename-in", DisplayName = "a file in the extracted folder held at the rename-in")]
    [DataRow("recycle", DisplayName = "a file in the replaced folder held at its recycle")]
    [DataRow("settings", DisplayName = "the settings file held at the last write")]
    public async Task AStepAfterTheRecordRefusedPastItsRetriesFailsCarryingTheStashAndAStartFinishesItOnceLetGo(string held)
    {
        using var source = new TestStash();
        var archive = await Archive(source);
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var keeper = Keeper(stash);
        FileStream? hold = null;
        stash.RecycleBin.BeforeEach = path => hold ??= held switch
        {
            "recycle" => TestStash.Hold(Path.Combine(path, StashFolder.StoreFileName)),
            "settings" => TestStash.Hold(SettingsPath(stash)),
            _ => null,
        };
        Outcome<string> imported;
        IReadOnlyList<Failure> whileHeld;
        try
        {
            imported = await store.Import(keeper, archive, new TransferReported(progress =>
            {
                if (held == "rename-in" && progress is { Phase: TransferPhase.Replacing, Done: 1 })
                {
                    hold = TestStash.Hold(Path.Combine(keeper.Current.ImportPending!.ExtractedFolder, StashFolder.StoreFileName));
                }
            }));
            Assert.AreEqual(keeper.Current.ImportPending, Saved(stash).ImportPending, "Current keeps the record as the disk does");
            whileHeld = await StashImport.FinishAtStart(Keeper(stash), stash.RecycleBin, stash.Folder.Root, stash.Retry);
        }
        finally
        {
            hold?.Dispose();
        }

        Assert.AreEqual(StashImport.Action, imported.Failure?.Action, "a failure past the record");
        Assert.AreEqual(stash.Folder.Root, imported.Value);
        Assert.AreEqual(HeldFileRetry.HeldReason, imported.Failure!.Reason);
        var record = Saved(stash).ImportPending;
        Assert.IsNotNull(record, "the record stands for the next start");
        Assert.AreEqual(StashImport.FinishAction, whileHeld.Single().Action, "a start while it is still held names it");
        Assert.AreEqual(record, Saved(stash).ImportPending);

        var finished = await StashImport.FinishAtStart(Keeper(stash), stash.RecycleBin, stash.Folder.Root, stash.Retry);

        Assert.IsEmpty(finished);
        Imported(stash, archive, record);
    }

    [TestMethod]
    public async Task AReplacedFolderTheBinRemovesForGoodInsteadOfKeepingStillLetsTheImportFinish()
    {
        using var source = new TestStash();
        var archive = await Archive(source);
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        // As Windows answers a folder larger than the bin keeps for that drive: removed for good, and said so (G29).
        stash.RecycleBin.BeforeEach = path => Directory.Delete(path, recursive: true);
        stash.RecycleBin.Refusing = _ => true;

        var imported = await store.Import(Keeper(stash), archive);

        Assert.IsTrue(imported.Succeeded, imported.Failure?.ToString());
        Assert.IsNull(Saved(stash).ImportPending);
        CollectionAssert.AreEquivalent(TreeOf(archive), Tree(stash.Folder.Root));
        Assert.IsEmpty(Beside(stash));
    }

    [TestMethod]
    [DoNotParallelize]
    public async Task AMarkerStillRefusedAfterTheLastWriteLeavesTheImportWholeAndTheNextStartDeletesIt()
    {
        using var source = new TestStash();
        var archive = await Archive(source);
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var keeper = Keeper(stash);
        FileStream? hold = null;
        string? marker = null;
        Outcome<string> imported;
        try
        {
            imported = await store.Import(keeper, archive, new TransferReported(progress =>
            {
                if (progress is { Phase: TransferPhase.Replacing, Done: 2 })
                {
                    marker = stash.Folder.InRoot(keeper.Current.ImportPending!.Marker);
                    hold = TestStash.Hold(marker);
                }
            }));
        }
        finally
        {
            hold?.Dispose();
        }

        Assert.IsTrue(imported.Succeeded, imported.Failure?.ToString());
        Assert.IsNull(keeper.Current.ImportPending);
        Assert.IsNull(Saved(stash).ImportPending);
        Assert.IsTrue(File.Exists(marker), "the marker stays in the root");
        stash.Close();
        Assert.IsNotNull((await stash.Open()).Store, "the stash opens with the marker beside its items");
        Assert.HasCount(2, await stash.All());
        stash.Close();
        var settings = Saved(stash);

        var finished = await StashImport.FinishAtStart(Keeper(stash), stash.RecycleBin, stash.Folder.Root, stash.Retry);

        Assert.IsEmpty(finished);
        Assert.IsFalse(File.Exists(marker));
        Assert.AreEqual(settings, Saved(stash));
    }

    // A source stash of two items and its export, the settings that travel all set away from their defaults.
    private async Task<string> Archive(TestStash source)
    {
        await source.OpenCleanly();
        await source.ImportFixture("plain", "photo.jpg");
        var saved = await source.SaveEdit(await source.ImportFixture("edited", "still.png"), EditedItems.Arrow);
        Assert.IsTrue(saved.Succeeded);
        var keeper = Keeper(source);
        Assert.IsTrue((await keeper.ChangeNow(s => s with { Theme = Theme.Dark, RecordingMaxSeconds = 60, AlsoCopyCapture = false, Shortcuts = new Shortcuts { Open = "Ctrl+Alt+K" } })).Succeeded);
        return await Export(source, keeper);
    }

    // The closed stash with three items of its own, and beside it the archive extracted with its marker, the record of
    // that import written: the state at the import's point of no return, built by hand.
    private static async Task<ImportPending> Recorded(TestStash stash, SettingsKeeper keeper, string archive)
    {
        await stash.OpenCleanly();
        foreach (var name in new[] { "one", "two", "three" })
        {
            await stash.Import(name);
        }

        stash.Close();
        var id = "0123456789ab";
        var travelling = TravellingSettings.Of(System.Text.Json.JsonSerializer.Deserialize<Settings>(Entry(archive, "settings.json"), Settings.Json)!);
        var record = new ImportPending(stash.Folder.Root + ".importing-" + id, stash.Folder.Root + ".replaced-" + id, ".imagestash-import-" + id, travelling);
        ZipFile.ExtractToDirectory(archive, record.ExtractedFolder);
        File.Delete(Path.Combine(record.ExtractedFolder, "manifest.json"));
        File.Delete(Path.Combine(record.ExtractedFolder, "settings.json"));
        File.WriteAllBytes(Path.Combine(record.ExtractedFolder, record.Marker), []);
        Assert.IsTrue((await keeper.ChangeNow(s => s with { ImportPending = record })).Succeeded);
        return record;
    }

    // The import finished: the archive's files at the stash's location and nothing else, no folder beside it, the
    // travelling values applied and the record cleared.
    private static void Imported(TestStash stash, string archive, ImportPending record)
    {
        CollectionAssert.AreEquivalent(TreeOf(archive), Tree(stash.Folder.Root), "the archive's files, no marker");
        Assert.IsEmpty(Beside(stash));
        var saved = Saved(stash);
        Assert.IsNull(saved.ImportPending);
        Assert.AreEqual(record.Settings, TravellingSettings.Of(saved));
    }
}
