using System.Runtime.Versioning;
using System.Text.Json;
using ImageStash.Files;
using ImageStash.Store;
using ImageStash.Tests.Support;
using Microsoft.Data.Sqlite;
using static ImageStash.Tests.Support.ExportedStash;
using static ImageStash.Tests.Support.MovingStash;

namespace ImageStash.Tests.Store;

/// <summary>
/// Export stash… on the store (G36, State: Export stash…; G1, G35, R2): every root and <c>originals\</c> file, the store and
/// the six settings that travel written into one archive beside the chosen path and renamed over it, with nothing in the
/// stash changed; a path inside the stash or in no folder refused with nothing written; a held destination named, its old
/// file kept whole and no partial file left; the summon's reads answered while it writes, a change asked meanwhile run after.
/// </summary>
[TestClass]
[SupportedOSPlatform("windows")]
public sealed class StashExportTests
{
    private readonly List<Failure> failures = [];

    private SettingsKeeper Keeper(TestStash stash) => MovingStash.Keeper(stash, failures);

    [TestMethod]
    public async Task AnExportCarriesEveryRootAndOriginalsFileTheStoreAndTheSixTravellingValuesAndChangesNothing()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var filled = await Fill(stash);
        var keeper = Keeper(stash);
        var chosen = new Settings
        {
            Shortcuts = new Shortcuts { Open = "Ctrl+Alt+K" },
            AlsoCopyCapture = false,
            RecordingMaxSeconds = 60,
            RecordPointer = false,
            StartWithWindows = false,
            Theme = Theme.Dark,
            StashFolder = @"D:\Elsewhere",
            ThumbnailSize = ThumbnailSize.Large,
            FlyoutSize = new WindowSize(500, 700),
            PinnedSize = new WindowSize(900, 650),
            PinnedPosition = new WindowPosition(40, 50),
        };
        Assert.IsTrue((await keeper.ChangeNow(_ => chosen)).Succeeded);
        var stoppedRender = Path.GetRelativePath(
            stash.Folder.Root, EditedItems.Row(stash, filled.Pending.Id).Steps.Single(step => step.Step == PendingStep.PlaceRender).FromPath(stash.Folder));
        Assert.IsTrue(File.Exists(Path.Combine(stash.Folder.Root, stoppedRender)), "the pending Save's render waits in .work");
        bool ChangedByTheFinish(string line, string file) => line.StartsWith(file + " ", StringComparison.Ordinal) || line.StartsWith("stash.db ", StringComparison.Ordinal);
        var before = Tree(stash.Folder.Root).Where(line => !ChangedByTheFinish(line, stoppedRender)).ToList();

        var archive = await Export(stash, keeper);

        // The stopped Save is finished first (State: Export stash…), its render now in the root and its row committed;
        // nothing else changes.
        Assert.IsEmpty(EditedItems.Row(stash, filled.Pending.Id).Steps, "the stopped Save finished before the archive was written");
        var placed = (await EditedItems.Listed(stash.Store!, filled.Pending.Id)).FileName;
        CollectionAssert.AreEqual(before, Tree(stash.Folder.Root).Where(line => !ChangedByTheFinish(line, placed)).ToList(), "nothing else in the stash changes");
        var entries = Entries(archive);
        var expected = new[] { "manifest.json", "settings.json", "stash.db" }
            .Concat(Directory.GetFiles(stash.Folder.Root).Select(file => Path.GetFileName(file)).Where(name => name != "stash.db"))
            .Concat(Directory.GetFiles(stash.Folder.Originals).Select(file => "originals/" + Path.GetFileName(file)))
            .ToList();
        CollectionAssert.AreEquivalent(expected, entries.Keys.ToList());
        Assert.IsTrue(entries.ContainsKey("originals/" + filled.Edited.FileName), "the edited item's original");
        foreach (var name in expected.Where(name => name is not ("manifest.json" or "settings.json")))
        {
            var onDisk = Tree(stash.Folder.Root).Single(line => line.StartsWith(name.Replace('/', Path.DirectorySeparatorChar) + " ", StringComparison.Ordinal));
            Assert.AreEqual(onDisk[(onDisk.LastIndexOf(' ') + 1)..], entries[name], name);
        }

        using var manifest = JsonDocument.Parse(Entry(archive, "manifest.json"));
        Assert.AreEqual("imagestash-export", manifest.RootElement.GetProperty("format").GetString());
        Assert.AreEqual(1, manifest.RootElement.GetProperty("formatVersion").GetInt32());
        Assert.AreEqual((long)StoreSchema.CurrentVersion, manifest.RootElement.GetProperty("storeVersion").GetInt64());
        Assert.AreEqual(typeof(StashStore).Assembly.GetName().Version!.ToString(), manifest.RootElement.GetProperty("build").GetString());
        Assert.AreEqual(4, manifest.RootElement.GetProperty("items").GetInt32());

        using var settings = JsonDocument.Parse(Entry(archive, "settings.json"));
        CollectionAssert.AreEquivalent(
            new[] { "shortcuts", "alsoCopyCapture", "recordingMaxSeconds", "recordPointer", "startWithWindows", "theme" },
            settings.RootElement.EnumerateObject().Select(property => property.Name).ToList(),
            "the six values, and not the stash location, the thumbnail size or a window's size or place");
        Assert.AreEqual("Ctrl+Alt+K", settings.RootElement.GetProperty("shortcuts").GetProperty("open").GetString());
        Assert.AreEqual("dark", settings.RootElement.GetProperty("theme").GetString());
        Assert.AreEqual(60, settings.RootElement.GetProperty("recordingMaxSeconds").GetInt32());
        Assert.IsFalse(settings.RootElement.GetProperty("alsoCopyCapture").GetBoolean());
        Assert.IsFalse(settings.RootElement.GetProperty("recordPointer").GetBoolean());
        Assert.IsFalse(settings.RootElement.GetProperty("startWithWindows").GetBoolean());
        Assert.HasCount(1, Directory.GetFiles(Path.GetDirectoryName(archive)!, "*.zip*"), "no partial file beside the archive");
        Assert.IsNotNull(await stash.Store!.Item(filled.Plain.Id), "the store stays open and live");
        Assert.IsEmpty(failures);
    }

    [TestMethod]
    [DataRow("no folder", DisplayName = "a folder that is not there")]
    [DataRow("root", DisplayName = "inside the stash folder")]
    [DataRow("originals", DisplayName = "inside originals")]
    [DataRow("junction", DisplayName = "inside the stash under another name")]
    public async Task APathInNoFolderOrInsideTheStashIsRefusedByNameAndNothingIsWritten(string where)
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        await stash.ImportFixture("plain", "photo.jpg");
        var before = Tree(stash.Folder.Root);
        using var junction = where == "junction" ? await Junction(stash.Outside("link"), stash.Folder.Root) : null;
        var archive = where switch
        {
            "no folder" => Path.Combine(stash.Outside("missing"), "export.zip"),
            "root" => stash.Folder.InRoot("export.zip"),
            "originals" => stash.Folder.InOriginals("export.zip"),
            _ => Path.Combine(stash.Outside("link"), "export.zip"),
        };

        var exported = await stash.Store!.Export(Keeper(stash), archive);

        var reason = where == "no folder" ? HeldFileRetry.FolderMissingReason : MoveReasons.InsideTheStash;
        Assert.AreEqual(new Failure(StashExport.Action, "export.zip", reason), exported.Failure);
        Assert.IsFalse(File.Exists(archive));
        CollectionAssert.AreEqual(before, Tree(stash.Folder.Root));
        Assert.IsFalse(Directory.Exists(stash.Outside("missing")));
    }

    [TestMethod]
    [DoNotParallelize]
    public async Task ADestinationHeldPastTheRetryIsNamedItsOldFileKeptWholeAndNoPartialFileLeft()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        await stash.ImportFixture("plain", "photo.jpg");
        var folder = Directory.CreateDirectory(stash.Outside("exports")).FullName;
        var archive = Path.Combine(folder, "export.zip");
        File.WriteAllBytes(archive, [1, 2, 3]);

        Outcome exported;
        using (TestStash.Hold(archive))
        {
            exported = await stash.Store!.Export(Keeper(stash), archive);
        }

        Assert.AreEqual(new Failure(StashExport.Action, "export.zip", HeldFileRetry.HeldReason), exported.Failure);
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, File.ReadAllBytes(archive));
        CollectionAssert.AreEqual(new[] { "export.zip" }, Directory.GetFiles(folder).Select(Path.GetFileName).ToList());
    }

    [TestMethod]
    [DoNotParallelize]
    public async Task AStashFileASyncClientHoldsIsExportedOnceLetGoAndOneHeldThroughoutIsNamedAsHeldWithNoPartialFileLeft()
    {
        using var stash = new TestStash { Retry = TimeSpan.FromSeconds(3) };
        await stash.OpenCleanly();
        var cat = await stash.ImportFixture("cat", "still.png");

        var letGo = HeldFiles.HeldFor(cat.FilePath, TimeSpan.FromMilliseconds(400));
        var exported = await stash.Store!.Export(Keeper(stash), stash.Outside("export.zip"));
        await letGo;

        Assert.IsTrue(exported.Succeeded, exported.Failure?.ToString());
        Assert.AreEqual(EditedItems.Hash(MediaFixtures.Bytes("still.png")), Entries(stash.Outside("export.zip"))[cat.FileName]);

        using var shortly = new TestStash();
        await shortly.OpenCleanly();
        var dog = await shortly.ImportFixture("dog", "still.png");
        var folder = Directory.CreateDirectory(shortly.Outside("held")).FullName;
        var archive = Path.Combine(folder, "export.zip");
        var refused = HeldFiles.WhileLocked(dog.FilePath, () => shortly.Store!.Export(Keeper(shortly), archive).GetAwaiter().GetResult());

        Assert.AreEqual(new Failure(StashExport.Action, "export.zip", HeldFileRetry.HeldReason), refused.Failure);
        Assert.IsEmpty(Directory.GetFiles(folder), "no archive and no partial file");
    }

    [TestMethod]
    public async Task AReadQueuedWhileAnExportWritesIsAnsweredBeforeItEndsAndAChangeQueuedRunsAfterIt()
    {
        using var stash = new TestStash();
        Directory.CreateDirectory(stash.Folder.Root);
        await stash.OpenCleanly();
        stash.Close();
        stash.AddPictures(300);
        var store = await stash.OpenCleanly();
        var first = (await stash.All())[0];
        Task<Outcome<IReadOnlyList<StashItem>>>? read = null;
        Task<IReadOnlyList<Outcome>>? tagged = null;
        bool? readAnsweredBeforeTheEnd = null;
        bool? tagRunBeforeTheEnd = null;

        var exported = await store.Export(Keeper(stash), stash.Outside("export.zip"), new TransferReported(progress =>
        {
            if (progress is { Phase: TransferPhase.Writing, Done: 1 })
            {
                read = store.All();
                tagged = store.AddTag([first], "late");
            }
            else if (progress.Phase == TransferPhase.Writing && progress.Done == progress.Total)
            {
                readAnsweredBeforeTheEnd = read!.IsCompleted;
                tagRunBeforeTheEnd = tagged!.IsCompleted;
            }
        }));

        Assert.IsTrue(exported.Succeeded, exported.Failure?.ToString());
        Assert.IsTrue(readAnsweredBeforeTheEnd, "the listing is answered while the export writes (G1)");
        Assert.IsFalse(tagRunBeforeTheEnd, "a change waits for the export to end");
        Assert.HasCount(300, (await read!).Value!);
        Assert.AreEqual("", TestStash.Failures(await tagged!));
        CollectionAssert.Contains((await store.Item(first.Id)).Value!.Tags.ToList(), "late");
        var archived = stash.Outside("archived.db");
        File.WriteAllBytes(archived, Entry(stash.Outside("export.zip"), "stash.db"));
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = archived, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM tags WHERE tag = 'late'";
        Assert.AreEqual(0L, command.ExecuteScalar(), "the tag is in the stash, not in the archive");
    }

    [TestMethod]
    public async Task AnExportAfterAStoppedAdoptionFinishesItFirstAndCarriesTheAdoptedPicture()
    {
        using var stash = new TestStash();
        var rewrite = MediaFixtures.DistinctPng(5, 12, 10);
        var (cat, _) = await StoppedAdoption(stash, rewrite);

        var archive = await Export(stash, Keeper(stash));

        Assert.IsEmpty(EditedItems.Row(stash, cat.Id).Steps, "the adoption finished before the archive was written");
        var listed = await EditedItems.Listed(stash.Store!, cat.Id);
        CollectionAssert.AreEqual(rewrite, File.ReadAllBytes(listed.FilePath));
        Assert.AreEqual(EditedItems.Hash(rewrite), Entries(archive)[listed.FileName], "the archive carries the adopted picture");
        CollectionAssert.AreEquivalent(
            Directory.GetFiles(stash.Folder.Root).Select(Path.GetFileName).Where(name => name != "stash.db").ToList(),
            Entries(archive).Keys.Where(name => name is not ("manifest.json" or "settings.json" or "stash.db")).ToList(),
            "every root file the stash lists, and nothing else");
    }

    [TestMethod]
    [DoNotParallelize]
    public async Task AnExportAfterAStoppedUndoOfAnAdoptionFinishesItFirstAndCarriesTheRestoredOriginal()
    {
        using var stash = new TestStash { Retry = TimeSpan.FromMilliseconds(900) };
        var (before, adopted, _) = await OpenedWith.AdoptedCat(stash, MediaFixtures.DistinctPng(5, 12, 10), edited: false);
        Outcome<StashItem?> undone;
        using (TestStash.Hold(adopted.FilePath))
        {
            undone = await stash.Store!.UndoAdoption(adopted.Id, adopted.RenderedRevision);
        }

        Assert.AreEqual(HeldFileRetry.HeldReason, undone.Failure?.Reason, "the Undo stopped with its steps on the row");
        Assert.IsNotEmpty(EditedItems.Row(stash, adopted.Id).Steps);

        var archive = await Export(stash, Keeper(stash));

        Assert.IsEmpty(EditedItems.Row(stash, adopted.Id).Steps, "the Undo finished before the archive was written");
        var listed = await EditedItems.Listed(stash.Store!, adopted.Id);
        Assert.AreEqual(EditedItems.Hash(MediaFixtures.Bytes("still.png")), Entries(archive)[listed.FileName], "the archive carries the restored original");
        Assert.AreEqual(before.FileName, listed.FileName);
    }

    [TestMethod]
    public async Task AnExportWhileAStoppedAdoptionsFileIsStillHeldIsRefusedNamingTheItemAndWritesNothing()
    {
        using var stash = new TestStash();
        var (cat, adoptedInWork) = await StoppedAdoption(stash, MediaFixtures.DistinctPng(5, 12, 10));
        var before = Tree(stash.Folder.Root);
        var archive = stash.Outside("export.zip");

        Outcome exported;
        using (TestStash.Hold(adoptedInWork))
        {
            exported = await stash.Store!.Export(Keeper(stash), archive);
        }

        Assert.AreEqual((StashExport.Action, "cat"), (exported.Failure?.Action, exported.Failure?.Subject), exported.Failure?.ToString());
        StringAssert.Contains(exported.Failure!.Reason, HeldFileRetry.HeldReason);
        Assert.IsFalse(File.Exists(archive));
        CollectionAssert.AreEqual(before, Tree(stash.Folder.Root));
        Assert.IsNotEmpty(EditedItems.Row(stash, cat.Id).Steps, "the steps stay for a later change or the next start");
    }

    // An Open with… adoption whose placing a held file stopped, the hold then gone: the previous original and the adopted
    // file both wait in .work\, which an export leaves out (State: Export stash…, A live failure). The item, and the
    // adopted file's path in .work\.
    private static async Task<(StashItem Cat, string AdoptedInWork)> StoppedAdoption(TestStash stash, byte[] rewrite)
    {
        var (cat, _) = await OpenedWith.AwaitingCat(stash, rewrite);
        List<FileStream> holds = [];
        stash.Reader = OpenedWith.Holding(holds);
        var adopting = await stash.Store!.AdoptRewrite(cat.Id);
        holds.ForEach(hold => hold.Dispose());
        stash.Reader = ImageStash.Media.MediaDecoder.ReadPicture;
        Assert.AreEqual(HeldFileRetry.HeldReason, adopting.Failure?.Reason, adopting.Failure?.ToString());
        var place = EditedItems.Row(stash, cat.Id).Steps.Single(step => step.Step == PendingStep.PlaceAdopted);
        Assert.IsFalse(place.IsDone(stash.Folder), "the adopted file waits in .work");
        return (cat, place.FromPath(stash.Folder));
    }

    [TestMethod]
    public async Task AnExportAfterAStoppedSaveFinishesItFirstAndCarriesTheSecondRender()
    {
        using var stash = new TestStash();
        var (cat, render) = await StoppedSecondSave(stash);

        var archive = await Export(stash, Keeper(stash));

        var row = EditedItems.Row(stash, cat.Id);
        Assert.IsEmpty(row.Steps, "the Save finished before the archive was written");
        Assert.AreEqual(row.LayerRevision, row.RenderedRevision, "the finished picture is the second Save's render");
        var listed = await EditedItems.Listed(stash.Store!, cat.Id);
        Assert.AreEqual(EditedItems.Hash(render), EditedItems.Hash(listed.FilePath));
        Assert.AreEqual(EditedItems.Hash(render), Entries(archive)[listed.FileName], "the archive carries the second render, not the first");
        Assert.AreEqual(EditedItems.BlackBoxAndBox, (await EditedItems.ForEdit(stash.Store!, cat.Id)).RenderedLayers);
    }

    [TestMethod]
    public async Task AnExportWhileAStoppedSaveCannotBeRenderedAgainIsRefusedNamingTheItemAndWritesNothing()
    {
        using var stash = new TestStash();
        var (cat, _) = await StoppedSecondSave(stash);
        // Finishing renders the Save again from the original, so an original another program holds keeps it from finishing.
        var original = stash.Folder.InOriginals(EditedItems.Row(stash, cat.Id).OriginalFile);
        var before = Tree(stash.Folder.Root);
        var archive = stash.Outside("export.zip");

        Outcome exported;
        using (new FileStream(original, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            exported = await stash.Store!.Export(Keeper(stash), archive);
        }

        Assert.AreEqual((StashExport.Action, "cat"), (exported.Failure?.Action, exported.Failure?.Subject), exported.Failure?.ToString());
        StringAssert.Contains(exported.Failure!.Reason, HeldFileRetry.HeldReason);
        Assert.IsFalse(File.Exists(archive));
        CollectionAssert.AreEqual(before, Tree(stash.Folder.Root));
        Assert.IsNotEmpty(EditedItems.Row(stash, cat.Id).Steps, "the steps stay for a later change or the next start");
    }

    [TestMethod]
    public async Task AnExportAfterAStoppedSaveAsNewItemUndoesItFirstAsTheStartWouldAndTheArchiveCarriesNoneOfIt()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var cat = await stash.ImportFixture("cat", "still.png");
        var render = await stash.Render(cat, EditedItems.BlackBoxAndBox);
        Outcome<StashItem> copy;
        using (TestStash.Hold(render.WorkPath))
        {
            copy = await stash.Store!.SaveAsNewItem(cat, EditedItems.BlackBoxAndBox, render);
        }

        Assert.AreEqual(HeldFileRetry.HeldReason, copy.Failure?.Reason, copy.Failure?.ToString());
        var stopped = EditedItems.Row(stash, copy.Value!.Id);
        Assert.IsTrue(File.Exists(stash.Folder.InOriginals(stopped.OriginalFile)), "the copy's original reached originals\\");
        Assert.IsTrue(File.Exists(render.WorkPath), "its render waits in .work");

        var archive = await Export(stash, Keeper(stash));

        Assert.IsEmpty(stash.SqlQueryWhileOpen("SELECT id FROM items WHERE id = $id", r => r.GetInt64(0), ("$id", copy.Value.Id)), "the stopped copy undone, as the next start undoes it");
        Assert.IsFalse(File.Exists(stash.Folder.InOriginals(stopped.OriginalFile)));
        Assert.IsFalse(File.Exists(render.WorkPath));
        CollectionAssert.AreEqual(new[] { "cat.png" }, Entries(archive).Keys.Where(name => name is not ("manifest.json" or "settings.json" or "stash.db")).ToList(), "the archive carries no stopped change");
        CollectionAssert.AreEqual(new[] { cat.Id }, (await stash.All()).Select(item => item.Id).ToList());
    }

    // An edited item's second Save whose render's placing a held file stopped, the hold then gone: the render waits in
    // .work\, which an export leaves out (State: Export stash…, A live failure). The item, and the render's bytes.
    private static async Task<(StashItem Cat, byte[] Render)> StoppedSecondSave(TestStash stash)
    {
        await stash.OpenCleanly();
        var first = await stash.SaveEdit(await stash.ImportFixture("cat", "still.png"), EditedItems.Arrow);
        Assert.IsTrue(first.Succeeded, first.Failure?.ToString());
        var render = await stash.Render(first.Value!, EditedItems.BlackBoxAndBox);
        var bytes = File.ReadAllBytes(render.WorkPath);
        Outcome<StashItem> second;
        using (TestStash.Hold(render.WorkPath))
        {
            second = await stash.Store!.Save(first.Value!, EditedItems.BlackBoxAndBox, render);
        }

        Assert.AreEqual(HeldFileRetry.HeldReason, second.Failure?.Reason, second.Failure?.ToString());
        Assert.IsTrue(File.Exists(render.WorkPath), "the second render waits in .work");
        return (first.Value!, bytes);
    }

    [TestMethod]
    public async Task AnExportCountsTheItemsTheStashListsSoTheImportBringsWhatTheSummaryOffers()
    {
        using var source = new TestStash();
        await source.OpenCleanly();
        await source.ImportFixture("saved", "photo.jpg");
        var bytes = MediaFixtures.Bytes("still.png");
        var work = await source.Stage("pending.png", bytes);
        using (TestStash.Hold(work))
        {
            var added = await source.Store!.CommitImport(TestStash.Candidate(work, "pending", bytes));
            Assert.IsFalse(added.Succeeded);
            Assert.IsNotNull(added.Value, "the import row committed before the refused file rename");
            Assert.IsFalse(added.Value.Listed);
        }

        var archive = await Export(source, Keeper(source));
        var offered = StashImport.Summary(archive, TestStash.ShortRetry).Value!;
        using var target = new TestStash();
        await target.OpenCleanly();
        Assert.IsTrue((await target.Store!.Import(Keeper(target), archive)).Succeeded);
        target.Close();
        Assert.IsNotNull((await target.Open()).Store);

        Assert.AreEqual(1, offered.Items, "the one item the stash lists, not the row whose file never reached the root (G36)");
        Assert.HasCount(offered.Items, await target.All(), "the confirmation's count is what the import brings (G36)");
    }

    [TestMethod]
    public void TheSuggestedNameIsTheMomentInLocalTimeWithTheArchivesExtension()
    {
        Assert.AreEqual("ImageStash export 2026-11-05 09.04.03.zip", StashExport.SuggestedFileName(new DateTimeOffset(2026, 11, 5, 9, 4, 3, TimeSpan.FromHours(5.5))));
    }
}
