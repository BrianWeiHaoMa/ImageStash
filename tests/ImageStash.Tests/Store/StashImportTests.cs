using System.Runtime.Versioning;
using ImageStash.Files;
using ImageStash.Media;
using ImageStash.Store;
using ImageStash.Tests.Support;
using static ImageStash.Tests.Support.EditedItems;
using static ImageStash.Tests.Support.ExportedStash;
using static ImageStash.Tests.Support.MovingStash;

namespace ImageStash.Tests.Store;

/// <summary>
/// Import stash… on the store (G36, State: Import stash…; G29, G31, G35, R2): an export's archive replaces the stash at its
/// location, the replaced folder recycled whole and the six travelling values replaced, the imported items opening as
/// they were exported; the manifest's summary; a file that is not an export, a newer build's, one whose entry escapes or
/// whose store does not open as a stash refused by name with nothing changed; an earlier build's store migrated before
/// the record; a Move and a transfer refused at submission against each other; every call of a store the import spent
/// refused. What a kill or a held file leaves after the record: <see cref="StashImportAtStartTests"/>.
/// </summary>
[TestClass]
[SupportedOSPlatform("windows")]
public sealed class StashImportTests
{
    private static readonly Settings SourceSettings = new()
    {
        Shortcuts = new Shortcuts { Open = "Ctrl+Alt+K", Screenshot = "Ctrl+Alt+S", Record = "Ctrl+Alt+R", StashClipboardImage = "Ctrl+Alt+C" },
        AlsoCopyCapture = false,
        RecordingMaxSeconds = 60,
        RecordPointer = false,
        StartWithWindows = false,
        Theme = Theme.Dark,
        StashFolder = @"D:\Source's stash",
        ThumbnailSize = ThumbnailSize.Large,
        FlyoutSize = new WindowSize(501, 701),
        PinnedSize = new WindowSize(901, 651),
        PinnedPosition = new WindowPosition(41, 51),
    };

    private static readonly Settings TargetSettings = new()
    {
        Shortcuts = new Shortcuts { Open = "Ctrl+Alt+J" },
        AlsoCopyCapture = true,
        RecordingMaxSeconds = 15,
        RecordPointer = true,
        StartWithWindows = true,
        Theme = Theme.Light,
        StashFolder = @"E:\Target's stash",
        ThumbnailSize = ThumbnailSize.Small,
        FlyoutSize = new WindowSize(502, 702),
        PinnedSize = new WindowSize(902, 652),
        PinnedPosition = new WindowPosition(42, 52),
    };

    private readonly List<Failure> failures = [];

    private SettingsKeeper Keeper(TestStash stash) => MovingStash.Keeper(stash, failures);

    [TestMethod]
    public async Task AnImportReplacesTheStashAtItsLocationRecyclesTheReplacedFolderWholeAndOpensAsItWasExported()
    {
        using var source = new TestStash();
        await source.OpenCleanly();
        var filled = await Fill(source);
        var exported = await source.All();
        var archive = await Export(source, await WithSettings(source, SourceSettings));
        using var target = new TestStash();
        var store = await Target(target);
        var keeper = await WithSettings(target, TargetSettings);
        var replacedTree = Tree(target.Folder.Root);

        var imported = await store.Import(keeper, archive);

        Assert.IsTrue(imported.Succeeded, imported.Failure?.ToString());
        Assert.AreEqual(target.Folder.Root, imported.Value);
        // The store as its rows: the import lets go of the exporting PC's Open with… sessions, so its bytes differ there.
        static List<string> Pictures(IEnumerable<string> tree) => [.. tree.Where(entry => !entry.StartsWith("stash.db ", StringComparison.Ordinal))];
        CollectionAssert.AreEquivalent(Pictures(TreeOf(archive)), Pictures(Tree(target.Folder.Root)), "the archive's pictures and nothing else: no manifest, no settings, no marker");
        Assert.IsTrue(File.Exists(target.Folder.StoreFile), "and its store");
        Assert.IsEmpty(Beside(target), "no extracted or replaced folder is left beside the stash");
        var recycled = target.RecycleBin.Asked.Single();
        StringAssert.StartsWith(Path.GetFileName(recycled), Path.GetFileName(target.Folder.Root) + ".replaced-", "the replaced folder, whole, not its files one by one");
        CollectionAssert.AreEquivalent(replacedTree, Tree(Directory.GetDirectories(target.RecycleBin.Folder).Single()));
        Assert.IsNull(keeper.Current.ImportPending);
        var saved = Saved(target);
        Assert.AreEqual(TravellingSettings.Of(SourceSettings).AppliedTo(TargetSettings), saved, "the six values the archive's, every other the target's own");
        Assert.IsEmpty(failures);

        target.Close();
        var reopened = await target.Open();
        Assert.IsNotNull(reopened.Store, string.Join("; ", reopened.Failures));
        var listed = await target.All();
        CollectionAssert.AreEquivalent(exported.Select(item => item.Id).ToList(), listed.Select(item => item.Id).ToList());
        foreach (var item in listed)
        {
            var was = exported.Single(e => e.Id == item.Id);
            // The pending Save was listed on the picture on disk, its original; the open finished it into its render.
            var fileName = item.Id == filled.Pending.Id ? "cat.png" : was.FileName;
            Assert.AreEqual((was.Name, was.Favorite, string.Join(",", was.Tags), fileName), (item.Name, item.Favorite, string.Join(",", item.Tags), item.FileName), was.Name);
            Assert.IsTrue(File.Exists(item.FilePath), item.Name);
            StringAssert.StartsWith(item.FilePath, target.Folder.Root);
        }

        Assert.AreEqual(1, (await reopened.Store.UseHistory(filled.Plain.Id)).Value!.Count);
        Assert.AreEqual(Arrow, (await reopened.Store.ForEdit(filled.Edited.Id)).Value!.Layers);
        Assert.IsTrue(File.Exists(target.Folder.InOriginals(filled.Edited.FileName)), "the edited item's original where it was");
        Assert.AreEqual(BlackBoxAndBox, (await reopened.Store.ForEdit(filled.Pending.Id)).Value!.Layers, "the pending Save finished at the open");
        Assert.IsTrue(reopened.Failures.All(failure => failure.Reason.Length > 0), "an Open with… session whose copy this PC lacks is named, never thrown");
    }


    [TestMethod]
    [DoNotParallelize]
    public async Task AnImportAskedWhileAMovesRemovalOfTheFolderHoldingTheStashStandsFinishesItFirstOrIsRefusedChangingNothing()
    {
        using var source = new TestStash();
        await source.OpenCleanly();
        await source.ImportFixture("edited", "still.png");
        var archive = await Export(source, Keeper(source));
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var plain = await stash.ImportFixture("plain", "photo.jpg");
        var keeper = Keeper(stash);
        // Moved into a folder inside the old one, whose removal a held file left for later (State: Move): the import's
        // folders beside the stash would lie inside the folder that removal empties.
        var inside = Directory.CreateDirectory(stash.Folder.InRoot("moved")).FullName;
        FileStream? held = null;
        try
        {
            var moved = await store.Move(keeper, inside, new Reported(p =>
            {
                if (p is { Phase: MovePhase.Removing, Done: 0 } && held is null)
                {
                    held = new FileStream(plain.FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                }
            }));
            Assert.AreEqual(inside, moved.Value, moved.Failure?.ToString());
            Assert.AreEqual(stash.Folder.Root, Saved(stash).PreviousStashFolderPendingRemoval);
            store.Dispose();
            var movedStore = (await stash.OpenAt(inside)).Store!;
            var tree = Tree(inside);

            var refused = await movedStore.Import(keeper, archive);

            Assert.AreEqual(new Failure(StashMove.FinishAction, Path.GetFileName(plain.FilePath), HeldFileRetry.HeldReason), refused.Failure);
            Assert.IsNull(refused.Value);
            Assert.IsNull(Saved(stash).ImportPending);
            CollectionAssert.AreEqual(tree, Tree(inside));
            Assert.AreEqual(stash.Folder.Root, Saved(stash).PreviousStashFolderPendingRemoval, "kept for the next start or the next try");
        }
        finally
        {
            held?.Dispose();
        }

        var imported = await stash.Store!.Import(keeper, archive);

        Assert.IsTrue(imported.Succeeded, imported.Failure?.ToString());
        Assert.IsNull(Saved(stash).PreviousStashFolderPendingRemoval, "the removal finished first");
        CollectionAssert.AreEqual(new[] { inside }, Directory.GetFileSystemEntries(stash.Folder.Root));
        CollectionAssert.AreEquivalent(TreeOf(archive), Tree(inside));
    }

    [TestMethod]
    public async Task AReplacedFolderWindowsRemovesForGoodCompletesTheImportWithNothingRecordedOfIt()
    {
        // G36, G29: the replaced stash's files go to the Recycle Bin, and a removal for good completes that step.
        using var source = new TestStash();
        await source.OpenCleanly();
        var plain = await source.ImportFixture("plain", "photo.jpg");
        var archive = await Export(source, Keeper(source));
        using var target = new TestStash();
        var store = await Target(target);
        var keeper = Keeper(target);
        target.RecycleBin.RemovingForGood = _ => true;

        var imported = await store.Import(keeper, archive);

        Assert.IsTrue(imported.Succeeded, imported.Failure?.ToString());
        StringAssert.StartsWith(Path.GetFileName(target.RecycleBin.Asked.Single()), Path.GetFileName(target.Folder.Root) + ".replaced-");
        Assert.IsEmpty(Beside(target), "the replaced folder is gone");
        Assert.IsFalse(Directory.Exists(target.RecycleBin.Folder) && Directory.EnumerateFileSystemEntries(target.RecycleBin.Folder).Any(), "nothing went to the bin");
        Assert.IsEmpty(target.RecycleBin.Removed);
        Assert.IsNull(keeper.Current.ImportPending);
        Assert.IsEmpty(failures);
        target.Close();
        var reopened = await target.Open();
        Assert.IsEmpty(reopened.Failures);
        Assert.AreEqual(plain.Id, (await target.All()).Single().Id);
    }

    [TestMethod]
    public async Task ASummaryReadsTheManifestAloneAndAnImportRefusesAnArchiveWhoseCountItsStoreDoesNotHold()
    {
        using var source = new TestStash();
        await source.OpenCleanly();
        await Fill(source);
        var archive = await Export(source, Keeper(source));
        var miscounted = Rewritten(archive, source.Outside("miscounted.zip"), ("manifest.json", Manifest(items: 99)));

        var summary = StashImport.Summary(archive, TestStash.ShortRetry);
        var miscountedSummary = StashImport.Summary(miscounted, TestStash.ShortRetry);

        Assert.AreEqual(new ExportSummary(4, StoreSchema.CurrentVersion, typeof(StashStore).Assembly.GetName().Version!.ToString()), summary.Value);
        Assert.AreEqual(99, miscountedSummary.Value!.Items, "the summary reads the manifest alone");
        using var target = new TestStash();
        var store = await Target(target);
        var tree = Tree(target.Folder.Root);
        // No ImageStash wrote a manifest its store disagrees with, and the confirmation said 99 (G36 "says how many items it holds").
        Assert.AreEqual(new Failure(StashImport.Action, "miscounted.zip", TransferReasons.NotAnExport), (await store.Import(Keeper(target), miscounted)).Failure);
        CollectionAssert.AreEqual(tree, Tree(target.Folder.Root));
        Assert.IsEmpty(Beside(target));
    }

    [TestMethod]
    [DoNotParallelize]
    public async Task AnArchiveASyncClientHoldsIsReadOnceLetGoAndOneHeldThroughoutIsRefusedAsHeldNotAsNoExport()
    {
        using var source = new TestStash();
        await source.OpenCleanly();
        await source.ImportFixture("cat", "still.png");
        var archive = await Export(source, Keeper(source));

        var letGo = HeldFiles.HeldFor(archive, TimeSpan.FromMilliseconds(400));
        var summary = StashImport.Summary(archive, TimeSpan.FromSeconds(3));
        await letGo;
        var heldThroughout = HeldFiles.WhileLocked(archive, () => StashImport.Summary(archive, TestStash.ShortRetry));

        Assert.AreEqual(1, summary.Value?.Items, summary.Failure?.ToString());
        Assert.AreEqual(new Failure(StashImport.Action, "export.zip", HeldFileRetry.HeldReason), heldThroughout.Failure);

        using var target = new TestStash { Retry = TimeSpan.FromSeconds(3) };
        var store = await Target(target);
        letGo = HeldFiles.HeldFor(archive, TimeSpan.FromMilliseconds(400));
        var imported = await store.Import(Keeper(target), archive);
        await letGo;
        Assert.IsTrue(imported.Succeeded, imported.Failure?.ToString());
        target.Close();
        Assert.IsNotNull((await target.Open()).Store);
        Assert.AreEqual("cat", (await target.All()).Single().Name);
    }

    [TestMethod]
    [DoNotParallelize]
    public async Task AnArchiveASyncClientTakesHoldOfWhileItIsExtractedIsExtractedAgainOnceLetGoAndOneHeldThroughoutChangesNothing()
    {
        using var source = new TestStash();
        await source.OpenCleanly();
        // Noise, so the archive is far larger than a read buffer and its extraction reads the file while it is held.
        var noise = new byte[256 * 256 * 4];
        new Random(3).NextBytes(noise);
        using (var bitmap = new SkiaSharp.SKBitmap(new SkiaSharp.SKImageInfo(256, 256, SkiaSharp.SKColorType.Bgra8888, SkiaSharp.SKAlphaType.Unpremul)))
        {
            System.Runtime.InteropServices.Marshal.Copy(noise, 0, bitmap.GetPixels(), noise.Length);
            using var png = bitmap.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
            await source.ImportPicture("cat", "cat.png", png.ToArray());
        }

        var archive = await Export(source, Keeper(source));
        Assert.IsGreaterThan(200_000L, new FileInfo(archive).Length);

        using var target = new TestStash { Retry = TimeSpan.FromSeconds(3) };
        var store = await Target(target);
        Task? letGo = null;
        var imported = await store.Import(Keeper(target), archive, new TransferReported(progress =>
        {
            if (progress is { Phase: TransferPhase.Extracting, Done: 0 } && letGo is null)
            {
                letGo = HeldFiles.HeldFor(archive, TimeSpan.FromMilliseconds(400));
            }
        }));
        await letGo!;

        Assert.IsTrue(imported.Succeeded, imported.Failure?.ToString());
        target.Close();
        Assert.IsNotNull((await target.Open()).Store);
        Assert.AreEqual("cat", (await target.All()).Single().Name);
        Assert.IsEmpty(Beside(target));

        using var held = new TestStash();
        var heldStore = await Target(held);
        var tree = Tree(held.Folder.Root);
        FileStream? holder = null;
        var refused = await heldStore.Import(Keeper(held), archive, new TransferReported(progress =>
        {
            if (progress is { Phase: TransferPhase.Extracting, Done: 0 } && holder is null)
            {
                holder = new FileStream(archive, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                holder.Lock(0, long.MaxValue);
            }
        }));
        holder!.Dispose();

        Assert.AreEqual(new Failure(StashImport.Action, "export.zip", HeldFileRetry.HeldReason), refused.Failure);
        CollectionAssert.AreEqual(tree, Tree(held.Folder.Root), "the stash as it was");
        Assert.IsEmpty(Beside(held), "the extracted folder discarded");
    }

    [TestMethod]
    [DataRow("no manifest", TransferReasons.NotAnExport)]
    [DataRow("text", TransferReasons.NotAnExport)]
    [DataRow("another format", TransferReasons.NotAnExport)]
    [DataRow("manifest not JSON", TransferReasons.NotAnExport)]
    [DataRow("newer store", TransferReasons.NewerBuild)]
    [DataRow("newer format", TransferReasons.NewerBuild)]
    [DataRow("escaping backslash", TransferReasons.NotAnExport)]
    [DataRow("escaping slash", TransferReasons.NotAnExport)]
    [DataRow("nested", TransferReasons.NotAnExport)]
    [DataRow("settings not valid", TransferReasons.NotAnExport)]
    [DataRow("no store", TransferReasons.NotAnExport)]
    public async Task AFileThatIsNotAnExportOrIsANewerBuildsIsRefusedByNameAndNothingChanges(string kind, string reason)
    {
        using var source = new TestStash();
        await source.OpenCleanly();
        await source.ImportFixture("plain", "photo.jpg");
        var real = await Export(source, Keeper(source));
        var path = source.Outside("refused.zip");
        var picture = MediaFixtures.Bytes("still.png");
        _ = kind switch
        {
            "no manifest" => Rewritten(real, path, ("manifest.json", null)),
            "text" => WriteText(path),
            "another format" => Rewritten(real, path, ("manifest.json", Manifest(format: "someone-elses-export"))),
            "manifest not JSON" => Rewritten(real, path, ("manifest.json", "{ not json"u8.ToArray())),
            "newer store" => Rewritten(real, path, ("manifest.json", Manifest(storeVersion: StoreSchema.CurrentVersion + 1))),
            "newer format" => Rewritten(real, path, ("manifest.json", Manifest(formatVersion: 2))),
            "escaping backslash" => Rewritten(real, path, (@"..\outside.png", picture)),
            "escaping slash" => Rewritten(real, path, ("../outside.png", picture)),
            "nested" => Rewritten(real, path, ("originals/deeper/inside.png", picture)),
            "settings not valid" => Rewritten(real, path, ("settings.json", """{"theme":"neon"}"""u8.ToArray())),
            _ => Rewritten(real, path, ("stash.db", null)),
        };
        using var target = new TestStash();
        var store = await Target(target);
        var keeper = await WithSettings(target, TargetSettings);
        var tree = Tree(target.Folder.Root);
        var settingsFile = File.ReadAllText(SettingsPath(target));

        var summary = StashImport.Summary(path, TestStash.ShortRetry);
        var imported = await store.Import(keeper, path);

        var refusal = new Failure(StashImport.Action, "refused.zip", reason);
        Assert.AreEqual(refusal, summary.Failure);
        Assert.AreEqual(refusal, imported.Failure);
        Assert.IsNull(imported.Value);
        CollectionAssert.AreEqual(tree, Tree(target.Folder.Root));
        Assert.AreEqual(settingsFile, File.ReadAllText(SettingsPath(target)));
        Assert.IsEmpty(Beside(target));
        Assert.IsFalse(File.Exists(Path.Combine(Path.GetDirectoryName(target.Folder.Root)!, "outside.png")));
        Assert.HasCount(3, await target.All(), "the store still answers");
    }

    [TestMethod]
    [DataRow("not SQLite")]
    [DataRow("no tables")]
    [DataRow("empty")]
    [DataRow("a picture missing")]
    [DataRow("a file name outside")]
    [DataRow("a step outside")]
    [DataRow("a held copy outside")]
    [DataRow("an earlier build's unknown format")]
    [DataRow("an earlier build's undecodable steps")]
    [DataRow("an earlier build's file name outside")]
    public async Task AnArchiveWhoseStoreDoesNotOpenAsItsStashIsRefusedWithNoRecordAndNoStoreMade(string kind)
    {
        using var source = new TestStash();
        await source.OpenCleanly();
        await source.ImportFixture("plain", "photo.jpg");
        var real = await Export(source, Keeper(source));
        var refused = source.Outside("refused.zip");
        // A store whose rows would reach outside the stash folder once opened: the step that finishes a rename, the
        // recycling of a deleted item's file, the deletion of its held copy (G35, R2).
        var path = kind switch
        {
            "not SQLite" => Rewritten(real, refused, ("stash.db", TestStash.Bytes(7, 4096))),
            "no tables" => Rewritten(real, refused, ("stash.db", StoreWithoutTables(source.Outside("no-tables.db")))),
            "empty" => Rewritten(real, refused, ("stash.db", [])),
            "a picture missing" => Rewritten(real, refused, ("plain.jpg", null)),
            "a file name outside" => WithStore(real, refused, @"UPDATE items SET finished_file = '..\victim.png', original_file = '..\victim.png'"),
            "a step outside" => WithStore(real, refused, @"UPDATE items SET pending_steps = '[{""step"":""rename in root"",""from"":""..\victim.png"",""to"":""plain.jpg""}]'"),
            // A store below this build's version, whose recorded sizes are read again before the rest is judged.
            "an earlier build's unknown format" => WithStore(real, refused, "UPDATE items SET format = 'Bogus'; PRAGMA user_version = 2"),
            "an earlier build's undecodable steps" => WithStore(real, refused, "UPDATE items SET pending_steps = 'not a list of steps'; PRAGMA user_version = 2"),
            "an earlier build's file name outside" => WithStore(real, refused, @"UPDATE items SET finished_file = '..\victim.jpg', original_file = '..\victim.jpg'; PRAGMA user_version = 2"),
            _ => WithStore(real, refused, @"UPDATE items SET deleted = 1, held_copies = '[{""file"":""finished_file"",""copy"":""..\..\victim.png""}]'"),
        };
        using var target = new TestStash();
        var opened = await Target(target);
        var keeper = Keeper(target);
        var tree = Tree(target.Folder.Root);

        var imported = await opened.Import(keeper, path);

        Assert.AreEqual(StashImport.Action, imported.Failure?.Action);
        Assert.AreEqual("refused.zip", imported.Failure!.Subject);
        if (kind != "not SQLite")
        {
            Assert.AreEqual(TransferReasons.NotAnExport, imported.Failure.Reason);
        }

        Assert.IsFalse(File.Exists(Path.Combine(Path.GetDirectoryName(target.Folder.Root)!, "victim.png")));

        Assert.IsNull(imported.Value);
        Assert.IsNull(Saved(target).ImportPending);
        CollectionAssert.AreEqual(tree, Tree(target.Folder.Root));
        Assert.IsEmpty(Beside(target), "the extracted folder, and any store made in it, gone");
        Assert.HasCount(3, await target.All());
    }

    [TestMethod]
    public async Task AnEarlierBuildsStoreIsMigratedBeforeTheRecordAndTheStashOpensAfterTheImport()
    {
        using var source = new TestStash();
        await source.OpenCleanly();
        await Fill(source);
        var archive = WithVersionOneStore(await Export(source, Keeper(source)), source.Outside("version-one.zip"));
        Assert.AreEqual(1L, StoreVersion(Extracted(archive, source.Outside("check.db"))));
        using var target = new TestStash();
        var store = await Target(target);
        var keeper = Keeper(target);
        long? versionAtTheRecord = null;
        ImportPending? recordOnDisk = null;

        var imported = await store.Import(keeper, archive, new TransferReported(progress =>
        {
            if (progress.Phase == TransferPhase.Replacing && versionAtTheRecord is null)
            {
                recordOnDisk = Saved(target).ImportPending;
                versionAtTheRecord = StoreVersion(Path.Combine(recordOnDisk!.ExtractedFolder, StashFolder.StoreFileName));
            }
        }));

        Assert.IsTrue(imported.Succeeded, imported.Failure?.ToString());
        Assert.IsNotNull(recordOnDisk, "the record was on disk before the stash was touched");
        Assert.AreEqual((long)StoreSchema.CurrentVersion, versionAtTheRecord);
        target.Close();
        var reopened = await target.Open();
        Assert.IsNotNull(reopened.Store, string.Join("; ", reopened.Failures));
        Assert.HasCount(4, await target.All());
    }

    [TestMethod]
    public async Task AnEarlierBuildsExportHasItsPhotosSizesReadAgainUprightAtItsImport()
    {
        using var source = new TestStash();
        await source.OpenCleanly();
        var photo = await source.ImportFixture("portrait", "oriented-6.jpg");
        // What the build before orientation exported: the photo at the size its codec decodes, the store at version 2.
        var archive = WithStore(await Export(source, Keeper(source)), source.Outside("earlier.zip"), "UPDATE items SET width = 40, height = 24; PRAGMA user_version = 2");
        using var target = new TestStash();
        var store = await Target(target);

        var imported = await store.Import(Keeper(target), archive);

        Assert.IsTrue(imported.Succeeded, imported.Failure?.ToString());
        target.Close();
        var reopened = await target.Open();
        Assert.IsNotNull(reopened.Store, string.Join("; ", reopened.Failures));
        var listed = (await target.All()).Single();
        target.Close();
        Assert.AreEqual((long)StoreSchema.CurrentVersion, target.SqlQuery("PRAGMA user_version", r => r.GetInt64(0)).Single());
        Assert.AreEqual((photo.Name, 24, 40), (listed.Name, listed.Facts.Width, listed.Facts.Height), "listed upright, as on the PC that exported it (G36, G6)");
    }

    [TestMethod]
    public async Task AnEarlierBuildsExportWhosePhotoCannotBeReadAgainIsRefusedByThatFilesNameAndNothingChanges()
    {
        using var source = new TestStash();
        await source.OpenCleanly();
        await source.ImportFixture("portrait", "oriented-6.jpg");
        // Not a picture where the photo was: no build would have taken it in, and its size cannot be read again.
        var archive = Rewritten(
            WithStore(await Export(source, Keeper(source)), source.Outside("earlier.zip"), "UPDATE items SET width = 40, height = 24; PRAGMA user_version = 2"),
            source.Outside("damaged.zip"),
            ("portrait.jpg", TestStash.Bytes(7)));
        using var target = new TestStash();
        var store = await Target(target);
        var before = Tree(target.Folder.Root);

        var imported = await store.Import(Keeper(target), archive);

        Assert.AreEqual(new Failure(StashImport.Action, "damaged.zip", $"portrait.jpg in it could not be read again: {MediaDecoder.UnreadableReason}"), imported.Failure, "named, never dropped while the version is committed (G35, G36)");
        CollectionAssert.AreEqual(before, Tree(target.Folder.Root), "the current stash as it was");
        Assert.IsEmpty(Beside(target), "no extracted folder is left beside the stash");
    }

    [TestMethod]
    [DoNotParallelize]
    [DataRow("import", "move")]
    [DataRow("import", "export")]
    [DataRow("import", "import")]
    [DataRow("export", "move")]
    [DataRow("export", "import")]
    [DataRow("export", "export")]
    [DataRow("move", "import")]
    [DataRow("move", "export")]
    public async Task AMoveOrATransferAskedWhileAnotherRunsIsRefusedAtOnceWithTheRunningOnesWords(string running, string asked)
    {
        using var source = new TestStash();
        await source.OpenCleanly();
        await source.ImportFixture("plain", "photo.jpg");
        var real = await Export(source, Keeper(source));
        // An import that fails before its record once let go, so nothing it ran could hide a Move run after it.
        var failing = Rewritten(real, source.Outside("failing.zip"), ("stash.db", TestStash.Bytes(3, 4096)));
        using var stash = new TestStash();
        var store = await Target(stash);
        var keeper = Keeper(stash);
        var chosen = Chosen(stash);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        void Block()
        {
            if (!entered.IsSet)
            {
                entered.Set();
                release.Wait();
            }
        }

        Task<Outcome> first = running switch
        {
            "import" => Wide(store.Import(keeper, failing, new TransferReported(_ => Block()))),
            "export" => store.Export(keeper, stash.Outside("first.zip"), new TransferReported(_ => Block())),
            _ => Wide(store.Move(keeper, chosen, new Reported(_ => Block()))),
        };
        Assert.IsTrue(entered.Wait(TimeSpan.FromSeconds(30)), "the first one runs");
        Task<Outcome> second = asked switch
        {
            "import" => Wide(store.Import(keeper, real)),
            "export" => store.Export(keeper, stash.Outside("second.zip")),
            _ => Wide(store.Move(keeper, Chosen(stash, "other"))),
        };
        var answeredAtOnce = second.IsCompleted;
        release.Set();
        var firstDone = await first;
        var refusal = (await second).Failure;

        Assert.IsTrue(answeredAtOnce, "refused at submission, nothing queued");
        var words = running switch { "import" => StashImport.InProgress, "export" => StashExport.InProgress, _ => MoveReasons.AlreadyMoving };
        var subject = asked switch { "import" => "export.zip", "export" => "second.zip", _ => "other" };
        Assert.AreEqual(new Failure(asked == "move" ? StashMove.Action : asked == "export" ? StashExport.Action : StashImport.Action, subject, words), refusal);
        Assert.AreEqual(running != "import", firstDone.Succeeded, firstDone.Failure?.ToString());
        Assert.IsFalse(File.Exists(stash.Outside("second.zip")));
        if (running != "move")
        {
            Assert.IsEmpty(Directory.EnumerateFileSystemEntries(Chosen(stash, "other")), "no Move ran after it");
            Assert.IsNull(Saved(stash).MoveCopyInProgress);
            Assert.HasCount(3, await stash.All());
        }
    }

    [TestMethod]
    [DoNotParallelize]
    [DataRow(false, DisplayName = "after a whole import")]
    [DataRow(true, DisplayName = "after an import that failed past its record")]
    public async Task EveryCallOfAStoreAnImportSpentIsRefusedAndNoneThrows(bool failedPastTheRecord)
    {
        using var source = new TestStash();
        await source.OpenCleanly();
        await source.ImportFixture("plain", "photo.jpg");
        var archive = await Export(source, Keeper(source));
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var cat = await stash.ImportFixture("cat", "still.png");
        var deleted = (await store.Delete([(await stash.ImportFixture("gone", "animated.gif")).Id])).Single().Value!;
        var staged = await stash.Stage("late.png", MediaFixtures.DistinctPng(9));
        var keeper = Keeper(stash);
        FileStream? held = null;
        if (failedPastTheRecord)
        {
            stash.RecycleBin.BeforeEach = _ => held ??= TestStash.Hold(SettingsPath(stash));
        }

        Outcome<string> imported;
        try
        {
            imported = await store.Import(keeper, archive);
        }
        finally
        {
            held?.Dispose();
        }

        Assert.AreEqual(failedPastTheRecord, !imported.Succeeded, imported.Failure?.ToString());
        Assert.AreEqual(stash.Folder.Root, imported.Value);
        var answers = new (string Entry, Func<Task<Outcome?>> Call)[]
        {
            ("All", async () => await store.All()),
            ("Recent", async () => await store.Recent()),
            ("Search", async () => await store.Search("cat")),
            ("Favorites", async () => await store.Favorites()),
            ("Item", async () => await store.Item(cat.Id)),
            ("TagCounts", async () => await store.TagCounts()),
            ("UseHistory", async () => await store.UseHistory(cat.Id)),
            ("OpenWithSessions", async () => await store.OpenWithSessions()),
            ("AllocateWorkPath", () => Task.FromResult<Outcome?>(store.AllocateWorkPath(".png"))),
            ("Stage", async () => await store.Stage(MediaFixtures.Path("still.png"))),
            ("Discard", async () => await store.Discard(staged)),
            ("WriteWork", async () => await store.WriteWork(".png", () => Task.FromResult<Stream>(new MemoryStream([1])), "Paste", "image", _ => { })),
            ("CommitImport", async () => await store.CommitImport(TestStash.Candidate(staged, "late", MediaFixtures.DistinctPng(9)))),
            ("ForEdit", async () => await store.ForEdit(cat.Id)),
            ("Confirm", async () => { using var c = await store.Confirm([cat], "Copy"); return c.Items.Single(); }),
            ("HoldWhileDragged", () => Task.FromResult<Outcome?>(store.HoldWhileDragged([cat], "Drag out"))),
            ("AddTag", async () => (await store.AddTag([cat], "x")).Single()),
            ("RemoveTag", async () => (await store.RemoveTag([cat], "x")).Single()),
            ("SetFavorite", async () => (await store.SetFavorite([cat], true)).Single()),
            ("RecordUses", async () => await store.RecordUses([cat], stash.Clock.Now)),
            ("Rename", async () => await store.Rename(cat, "dog")),
            ("Save", async () => await store.Save(cat, Arrow, null)),
            ("SaveAsNewItem", async () => await store.SaveAsNewItem(cat, Arrow, null)),
            ("Remove", async () => (await store.Remove([cat.Id])).Single()),
            ("Delete", async () => (await store.Delete([cat.Id])).Single()),
            ("UndoDelete", async () => (await store.UndoDelete([deleted])).Single()),
            ("FinishDelete", async () => (await store.FinishDelete([deleted.Id])).Single()),
            ("BeginOpenWith", async () => await store.BeginOpenWith(cat)),
            ("ClassifyOpenWith", async () => await store.ClassifyOpenWith(cat.Id)),
            ("AdoptRewrite", async () => await store.AdoptRewrite(cat.Id)),
            ("UndoAdoption", async () => await store.UndoAdoption(cat.Id, 1)),
            ("ExpireAdoption", async () => await store.ExpireAdoption(cat.Id, 1)),
            ("Move", async () => await store.Move(keeper, Chosen(stash))),
            ("Export", async () => await store.Export(keeper, stash.Outside("again.zip"))),
            ("Import", async () => await store.Import(keeper, archive)),
        };
        foreach (var (entry, call) in answers)
        {
            var answer = await call();
            Assert.AreEqual(StashImport.InProgress, answer?.Failure?.Reason, entry);
        }
    }

    [TestMethod]
    public async Task AnImportedItemCarriesNoOpenWithSessionSoAnEarlierExportImportedBackKeepsItsPictureThoughThisPcsCopyWasRewrittenSince()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var item = await stash.ImportFixture("cat", "still.png");
        var original = File.ReadAllBytes(item.FilePath);
        var copy = await OpenedWith.Begin(stash, item);
        var keeper = Keeper(stash);
        var archive = await Export(stash, keeper);

        // An outside edit after the export, adopted, its notice gone, as Move's wait leaves it before the import.
        OpenedWith.Rewrite(copy, MediaFixtures.DistinctPng(73));
        Assert.AreEqual(OpenWithCopy.AwaitingAdoption, await OpenedWith.Classify(stash, item.Id));
        var adopted = await OpenedWith.Adopt(stash, item.Id);
        Assert.IsTrue((await stash.Store!.ExpireAdoption(item.Id, adopted.RenderedRevision)).Succeeded);
        Assert.IsTrue((await stash.Store!.Import(keeper, archive)).Succeeded);
        stash.Close();

        var reopened = await stash.Open();
        Assert.IsNotNull(reopened.Store, string.Join("; ", reopened.Failures));
        var imported = (await stash.All()).Single();
        CollectionAssert.AreEqual(original, File.ReadAllBytes(imported.FilePath), "the export's picture, not this PC's later outside edit (G36)");
        Assert.IsEmpty((await reopened.Store.OpenWithSessions()).Value!.Sessions, "no session of the replaced item's copy carried to the imported one");
    }

    [TestMethod]
    public async Task AnArchiveWhoseStoreTheListingCannotReadIsRefusedBeforeTheStashIsReplaced()
    {
        using var source = new TestStash();
        await source.OpenCleanly();
        await source.ImportFixture("cat", "still.png");
        var archive = await Export(source, Keeper(source));
        var bad = WithStore(archive, source.Outside("bad-tags.zip"), "ALTER TABLE tags RENAME COLUMN tag TO broken_tag");
        using var target = new TestStash();
        await target.OpenCleanly();
        await target.ImportFixture("keep", "photo.jpg");
        var keeper = Keeper(target);
        var before = Tree(target.Folder.Root);

        var imported = await target.Store!.Import(keeper, bad);

        Assert.AreEqual((StashImport.Action, "bad-tags.zip", TransferReasons.NotAnExport), (imported.Failure?.Action, imported.Failure?.Subject, imported.Failure?.Reason));
        Assert.IsNull(imported.Value);
        CollectionAssert.AreEqual(before, Tree(target.Folder.Root));
        Assert.IsEmpty(target.RecycleBin.Asked);
        Assert.IsNull(keeper.Current.ImportPending);
    }

    [TestMethod]
    public async Task AnArchiveWhoseStoreHoldsANullStepIsRefusedByNameAndNothingThrows()
    {
        using var source = new TestStash();
        await source.OpenCleanly();
        await source.ImportFixture("cat", "still.png");
        var archive = await Export(source, Keeper(source));
        var bad = WithStore(archive, source.Outside("null-step.zip"), "UPDATE items SET pending_steps = '[null]'");
        using var target = new TestStash();
        await target.OpenCleanly();
        await target.ImportFixture("keep", "photo.jpg");
        var keeper = Keeper(target);

        var imported = await target.Store!.Import(keeper, bad);

        Assert.AreEqual((StashImport.Action, "null-step.zip", TransferReasons.NotAnExport), (imported.Failure?.Action, imported.Failure?.Subject, imported.Failure?.Reason));
        Assert.IsNull(imported.Value);
        Assert.IsNull(keeper.Current.ImportPending);
        Assert.IsEmpty(target.RecycleBin.Asked);
    }

    [TestMethod]
    public async Task AnExportTakenWhileARenameWaitsOnAHeldFileImportsThoughTheEarlierNameIsNoFileName()
    {
        using var source = new TestStash();
        await source.OpenCleanly();
        var item = await source.ImportFixture("mood: delighted", "still.png");
        using (TestStash.Hold(item.FilePath))
        {
            var renamed = await source.Store!.Rename(item, "delighted");
            Assert.IsFalse(renamed.Succeeded, "the held file refuses the rename, leaving its step pending");
            Assert.IsNotNull(renamed.Value, "the name committed");
        }

        var archive = await Export(source, Keeper(source));
        using var target = new TestStash();
        await target.OpenCleanly();

        var imported = await target.Store!.Import(Keeper(target), archive);

        Assert.IsTrue(imported.Succeeded, "the product's own export imports (G36): " + imported.Failure);
        target.Close();
        Assert.IsNotNull((await target.Open()).Store);
        Assert.AreEqual("delighted", (await target.All()).Single().Name);
    }

    [TestMethod]
    public async Task AnArchiveWhoseOnlyRowWaitsOnAPictureItDoesNotCarryIsRefusedBeforeTheStashIsReplaced()
    {
        using var source = new TestStash();
        await source.OpenCleanly();
        await source.ImportFixture("cat", "still.png");
        var archive = await Export(source, Keeper(source));
        // No export carries a row whose picture has yet to reach the root: its file waits in .work\, which none carries.
        var bad = WithStore(archive, source.Outside("unfinished.zip"),
            """UPDATE items SET pending_steps = '[{"step":"place in root","from":"missing.png","to":"cat.png"}]'""");
        bad = Rewritten(bad, source.Outside("missing-picture.zip"), ("cat.png", null));
        using var target = new TestStash();
        await target.OpenCleanly();
        await target.ImportFixture("keep", "photo.jpg");
        var keeper = Keeper(target);
        var before = Tree(target.Folder.Root);

        var imported = await target.Store!.Import(keeper, bad);

        Assert.AreEqual((StashImport.Action, TransferReasons.NotAnExport), (imported.Failure?.Action, imported.Failure?.Reason), imported.Failure?.ToString());
        Assert.IsNull(imported.Value);
        CollectionAssert.AreEqual(before, Tree(target.Folder.Root));
        Assert.IsEmpty(target.RecycleBin.Asked);
    }

    [TestMethod]
    [DataRow("UPDATE items SET format = 'NotAFormat'")]
    [DataRow("UPDATE items SET added_at = 9223372036854775807")]
    [DataRow("UPDATE items SET pending_steps = '[{\"step\":\"place in root\",\"to\":\"cat.png\"}]'")]
    public async Task AnArchiveWhoseStoreHoldsAValueNoBuildWritesIsRefusedByNameAndNothingThrows(string sql)
    {
        using var source = new TestStash();
        await source.OpenCleanly();
        await source.ImportFixture("cat", "still.png");
        var archive = await Export(source, Keeper(source));
        var invalid = WithStore(archive, source.Outside("invalid.zip"), sql);
        using var target = new TestStash();
        await target.OpenCleanly();
        await target.ImportFixture("keep", "photo.jpg");
        var before = Tree(target.Folder.Root);
        var keeper = Keeper(target);

        var imported = await target.Store!.Import(keeper, invalid);

        Assert.AreEqual((StashImport.Action, "invalid.zip", TransferReasons.NotAnExport), (imported.Failure?.Action, imported.Failure?.Subject, imported.Failure?.Reason));
        CollectionAssert.AreEqual(before, Tree(target.Folder.Root));
        Assert.IsNull(keeper.Current.ImportPending);
        Assert.IsEmpty(target.RecycleBin.Asked);
    }

    [TestMethod]
    public async Task AnArchiveWhoseStoreRecordsAStepNoBuildWritesIsRefusedBeforeTheStashIsReplaced()
    {
        using var source = new TestStash();
        await source.OpenCleanly();
        await source.ImportFixture("cat", "still.png");
        var archive = await Export(source, Keeper(source));
        // Its manifest counts nothing, as the step leaves its one row unlisted: only the step tells it from an export.
        var invalid = WithStore(archive, source.Outside("unknown-step.zip"), """UPDATE items SET pending_steps = '[{"step":"invented","from":"cat.png","to":"cat.png"}]'""");
        invalid = Rewritten(invalid, source.Outside("unknown-count.zip"), ("manifest.json", Manifest(items: 0)));
        using var target = new TestStash();
        await target.OpenCleanly();
        await target.ImportFixture("keep", "photo.jpg");
        var before = Tree(target.Folder.Root);

        var imported = await target.Store!.Import(Keeper(target), invalid);

        Assert.AreEqual((StashImport.Action, TransferReasons.NotAnExport), (imported.Failure?.Action, imported.Failure?.Reason), imported.Failure?.ToString());
        CollectionAssert.AreEqual(before, Tree(target.Folder.Root));
        Assert.IsEmpty(target.RecycleBin.Asked);
    }

    // Opens the stash with three items of its own, as the stash an import replaces.
    private static async Task<StashStore> Target(TestStash stash)
    {
        var store = await stash.OpenCleanly();
        foreach (var name in new[] { "one", "two", "three" })
        {
            await stash.Import(name);
        }

        return store;
    }

    // A keeper over the stash's settings file holding the given settings, written.
    private async Task<SettingsKeeper> WithSettings(TestStash stash, Settings settings)
    {
        var keeper = Keeper(stash);
        Assert.IsTrue((await keeper.ChangeNow(_ => settings)).Succeeded);
        return keeper;
    }

    private static string WriteText(string path)
    {
        File.WriteAllText(path, "a note, not an archive");
        return path;
    }

    private static string Extracted(string archive, string path)
    {
        File.WriteAllBytes(path, Entry(archive, StashFolder.StoreFileName));
        return path;
    }

    private static async Task<Outcome> Wide<T>(Task<Outcome<T>> outcome) => await outcome;
}
