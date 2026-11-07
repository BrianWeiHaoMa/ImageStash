using System.Runtime.Versioning;
using ImageStash.Files;
using ImageStash.Store;
using ImageStash.Tests.Support;
using static ImageStash.Tests.Support.EditedItems;
using static ImageStash.Tests.Support.MovingStash;

namespace ImageStash.Tests.Store;

/// <summary>
/// Move on the store (G30, State: Move, How the guarantees hold: G30; G28, G29, G35, G1): every file and the store copied
/// into a chosen empty folder on the store's thread, the settings switched in one write, the old folder removed; a folder
/// Move cannot keep a stash whole in refused with nothing written; a copy or a switch that fails changing nothing; a
/// removal still refused named after the switch, finished at the next start or before the next Move; from the copy on
/// every change refused with one reason while the summon's listing is answered. The folders themselves:
/// <see cref="StashMoveFoldersTests"/>.
/// </summary>
[TestClass]
[SupportedOSPlatform("windows")]
public sealed class StashMoveTests
{
    private readonly List<Failure> failures = [];

    private SettingsKeeper Keeper(TestStash stash) => MovingStash.Keeper(stash, failures);

    [TestMethod]
    public async Task AMoveCarriesEveryFileAndTheStoreSwitchesTheSettingsOnceAndRemovesTheOldFolder()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var plain = await stash.ImportFixture("plain", "photo.jpg");
        var edited = (await stash.SaveEdit(await stash.ImportFixture("edited", "still.png"), BlackBoxAndBox)).Value!;
        var cat = (await stash.SaveEdit(await stash.ImportFixture("cat", "transparent.png"), BlackBoxAndBox)).Value!;
        var render = await stash.Render(cat, Arrow);
        using (TestStash.Hold(cat.FilePath))
        {
            Assert.IsFalse((await store.Save(cat, Arrow, render)).Succeeded, "the Save's place is left pending");
        }

        Assert.IsTrue((await store.AddTag([plain], "one")).Concat(await store.AddTag([edited], "two")).Concat(await store.AddTag([cat], "three")).All(o => o.Succeeded));
        Assert.IsTrue((await store.SetFavorite([plain, edited], true)).All(o => o.Succeeded));
        Assert.IsTrue((await store.RecordUses([plain], stash.Clock.Now)).Succeeded);
        Assert.HasCount(1, stash.WorkFiles(), "the pending render in .work");
        var before = Tree(stash.Folder.Root);
        var keeper = Keeper(stash);
        var chosen = Chosen(stash);
        var seen = new List<(MoveProgress Progress, Settings Saved)>();

        var moved = await store.Move(keeper, chosen, new Reported(p => seen.Add((p, Saved(stash)))));

        Assert.IsTrue(moved.Succeeded, moved.Failure?.Sentence);
        Assert.AreEqual(chosen, moved.Value);
        Assert.IsFalse(Directory.Exists(stash.Folder.Root), "the old folder is removed (G29)");
        CollectionAssert.AreEquivalent(before, Tree(chosen), "every root file, originals, .work and the store, byte for byte");
        var copying = seen.Where(s => s.Progress.Phase == MovePhase.Copying).Select(s => s.Saved).ToList();
        var removing = seen.Where(s => s.Progress.Phase != MovePhase.Copying).Select(s => s.Saved).ToList();
        Assert.IsNotEmpty(copying);
        Assert.IsTrue(copying.All(s => s.MoveCopyInProgress == chosen && s.StashFolder is null && s.PreviousStashFolderPendingRemoval is null), "the copy runs recorded, the stash still the old one");
        Assert.IsNotEmpty(removing);
        Assert.IsTrue(removing.All(s => s.StashFolder == chosen && s.MoveCopyInProgress is null && s.PreviousStashFolderPendingRemoval == stash.Folder.Root), "one write switches the location and records the removal");
        var saved = Saved(stash);
        Assert.AreEqual((chosen, (string?)null, (string?)null), (saved.StashFolder, saved.MoveCopyInProgress, saved.PreviousStashFolderPendingRemoval));
        Assert.AreEqual(saved, keeper.Current);
        Assert.IsEmpty(failures);

        stash.Close();
        var opening = await StashStore.Open(chosen, stash.RecycleBin, stash.Retry, stash.Clock, openWithRoot: stash.OpenWithRoot);
        using var moved2 = opening.Store!;
        Assert.IsEmpty(opening.Failures, string.Join("; ", opening.Failures));
        var listed = (await moved2.All()).Value!;
        Assert.AreEqual("cat edited plain", string.Join(" ", listed.Select(i => i.Name).Order()));
        Assert.AreEqual("one|two|three", string.Join("|", new[] { plain, edited, cat }.Select(i => listed.Single(l => l.Id == i.Id).Tags.Single())));
        Assert.AreEqual("edited plain", string.Join(" ", listed.Where(i => i.Favorite).Select(i => i.Name).Order()));
        Assert.AreEqual(1, (await moved2.UseHistory(plain.Id)).Value!.Count);
        Assert.AreEqual(Arrow, (await ForEdit(moved2, cat.Id)).RenderedLayers, "the pending Save finished at the new place");
        Assert.IsTrue(listed.All(i => i.FilePath.StartsWith(chosen + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    [DataRow("full", MoveReasons.NotEmpty)]
    [DataRow("missing", MoveReasons.NotAFolder)]
    [DataRow("stash", MoveReasons.TheStashItself)]
    [DataRow(".work\\inner", MoveReasons.InsideWork)]
    [DataRow("open-with", MoveReasons.InsideOpenWith)]
    [DataRow("open-with\\inner", MoveReasons.InsideOpenWith)]
    public async Task AFolderAMoveCannotKeepAStashWholeInIsRefusedByNameAndNothingIsWritten(string folder, string reason)
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        await stash.ImportFixture("plain", "photo.jpg");
        var chosen = folder switch
        {
            "full" => Chosen(stash, "full"),
            "missing" => stash.Outside("missing"),
            "stash" => stash.Folder.Root,
            ".work\\inner" => Directory.CreateDirectory(Path.Combine(stash.Folder.Work, "inner")).FullName,
            _ => Directory.CreateDirectory(Path.Combine(stash.Outside(folder))).FullName,
        };
        if (folder == "full")
        {
            File.WriteAllText(Path.Combine(chosen, "notes.txt"), "mine");
        }

        var keeper = Keeper(stash);
        var settings = File.ReadAllBytes(SettingsPath(stash));
        var before = Tree(stash.Folder.Root);
        var around = Tree(stash.Outside(""));

        var moved = await store.Move(keeper, chosen);

        Assert.AreEqual(new Failure(StashMove.Action, Path.GetFileName(chosen), reason), moved.Failure);
        CollectionAssert.AreEqual(settings, File.ReadAllBytes(SettingsPath(stash)), "the settings file unchanged byte for byte");
        CollectionAssert.AreEquivalent(before, Tree(stash.Folder.Root));
        CollectionAssert.AreEquivalent(around, Tree(stash.Outside("")), "nothing written anywhere");
        Assert.IsNull(keeper.Current.MoveCopyInProgress);
        Assert.IsTrue((await store.AddTag((await stash.All()).ToList(), "still")).All(o => o.Succeeded), "the stash takes changes as before");
    }

    [TestMethod]
    public async Task ARootFileHeldPastTheRetryFailsTheCopyAndNothingChanges()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var plain = await stash.ImportFixture("plain", "photo.jpg");
        var keeper = Keeper(stash);
        var chosen = Chosen(stash);
        var settings = Saved(stash);
        var before = Tree(stash.Folder.Root);

        var moved = HeldFiles.WhileLocked(plain.FilePath, () => store.Move(keeper, chosen).GetAwaiter().GetResult());

        Assert.AreEqual(new Failure(StashMove.Action, Path.GetFileName(plain.FilePath), HeldFileRetry.HeldReason), moved.Failure);
        Assert.IsEmpty(Directory.GetFileSystemEntries(chosen), "the partial copy deleted, the chosen folder as it was found");
        CollectionAssert.AreEquivalent(before, Tree(stash.Folder.Root));
        Assert.AreEqual(settings, Saved(stash), "the settings file reads as before, with no Move field");
        Assert.AreEqual(settings, keeper.Current);
        var again = await store.Move(keeper, chosen);
        Assert.IsTrue(again.Succeeded, "once the hold is gone a Move succeeds: " + again.Failure?.Sentence);
    }

    [TestMethod]
    public async Task ASettingsFileHeldAtTheRecordRefusesTheMoveBeforeAnythingIsCopied()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        await stash.ImportFixture("plain", "photo.jpg");
        var keeper = Keeper(stash);
        var chosen = Chosen(stash);
        var before = Tree(stash.Folder.Root);

        var moved = HeldFiles.WhileLocked(SettingsPath(stash), () => store.Move(keeper, chosen).GetAwaiter().GetResult());

        Assert.AreEqual(new Failure("Save settings", Settings.FileName, HeldFileRetry.HeldReason), moved.Failure);
        Assert.IsEmpty(Directory.GetFileSystemEntries(chosen));
        CollectionAssert.AreEquivalent(before, Tree(stash.Folder.Root));
        Assert.IsNull(keeper.Current.MoveCopyInProgress);
        Assert.IsNull(Saved(stash).MoveCopyInProgress);
    }

    [TestMethod]
    public async Task ASettingsFileHeldAtTheSwitchDeletesTheCopyAndKeepsAChangeMadeMeanwhile()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        await stash.ImportFixture("plain", "photo.jpg");
        var keeper = Keeper(stash);
        var chosen = Chosen(stash);
        var before = Tree(stash.Folder.Root);
        FileStream? held = null;
        void HoldAtTheLastCopy(MoveProgress progress)
        {
            if (progress is { Phase: MovePhase.Copying } && progress.Done == progress.Total && held is null)
            {
                held = new FileStream(SettingsPath(stash), FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                held.Lock(0, long.MaxValue);
                keeper.Change(s => s with { PinnedSize = new WindowSize(1000, 700) });
            }
        }

        Outcome<string> moved;
        try
        {
            moved = await store.Move(keeper, chosen, new Reported(HoldAtTheLastCopy));
        }
        finally
        {
            held?.Dispose();
        }

        Assert.AreEqual(new Failure("Save settings", Settings.FileName, HeldFileRetry.HeldReason), moved.Failure);
        Assert.IsEmpty(Directory.GetFileSystemEntries(chosen), "the whole copy deleted");
        CollectionAssert.AreEquivalent(before, Tree(stash.Folder.Root), "the old folder whole");
        var current = keeper.Current;
        Assert.AreEqual(((string?)null, (string?)null, (string?)null, new WindowSize(1000, 700)), (current.StashFolder, current.MoveCopyInProgress, current.PreviousStashFolderPendingRemoval, current.PinnedSize));
        Assert.IsTrue((await store.All()).Succeeded, "the old store is the stash again");
    }

    [TestMethod]
    [DoNotParallelize]
    public async Task AMoveAskedWhileAnAdoptionRunsWaitsForItsLastStep()
    {
        using var stash = new TestStash { Retry = TimeSpan.FromSeconds(3) };
        var (cat, _) = await OpenedWith.AwaitingCat(stash, MediaFixtures.Bytes("photo.jpg"));
        var hold = TestStash.Hold(cat.FilePath);
        var adopting = stash.Store!.AdoptRewrite(cat.Id);
        await EditorSessions.Until(() => Row(stash, cat.Id).Steps.Count > 0, "the adoption never committed");
        var chosen = Chosen(stash);
        var moving = stash.Store.Move(Keeper(stash), chosen);
        await Task.Delay(1000);
        var movedBeforeTheAdoption = moving.IsCompleted;
        hold.Dispose();

        var adopted = await adopting;
        var moved = await moving;

        Assert.IsTrue(adopted.Succeeded, adopted.Failure?.Sentence);
        Assert.IsFalse(movedBeforeTheAdoption, "the Move waits for the adoption's steps");
        Assert.IsTrue(moved.Succeeded, moved.Failure?.Sentence);
        stash.Close();
        var opening = await StashStore.Open(chosen, stash.RecycleBin, stash.Retry, stash.Clock, openWithRoot: stash.OpenWithRoot);
        using var reopened = opening.Store!;
        var listed = (await reopened.All()).Value!.Single();
        Assert.AreEqual((Hash(MediaFixtures.Bytes("photo.jpg")), ".jpg"), (Hash(listed.FilePath), Path.GetExtension(listed.FilePath)), "the adopted picture is the item's one file");
        Assert.AreEqual(1, Directory.GetFiles(chosen).Count(file => !Path.GetFileName(file).StartsWith(StashFolder.StoreFileName, StringComparison.Ordinal)));
    }

    [TestMethod]
    [DoNotParallelize]
    public async Task FromTheCopyOnEveryChangeIsRefusedWithOneReasonAndTheListingIsAnswered()
    {
        using var stash = new TestStash { Retry = TimeSpan.FromSeconds(5) };
        var store = await stash.OpenCleanly();
        var plain = await stash.ImportFixture("plain", "photo.jpg");
        var cat = await stash.ImportFixture("cat", "still.png");
        var staged = await stash.Stage("late.png", MediaFixtures.DistinctPng(9));
        var chosen = Chosen(stash);
        var copying = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = HeldFiles.HeldFor(plain.FilePath, TimeSpan.FromSeconds(1.5));
        var moving = store.Move(Keeper(stash), chosen, new Reported(_ => copying.TrySetResult()));
        await copying.Task;

        Assert.AreEqual(new Failure(StashMove.Action, "second", MoveReasons.AlreadyMoving), (await store.Move(Keeper(stash), Chosen(stash, "second"))).Failure);
        var delete = store.Delete([cat.Id]);
        var import = store.CommitImport(TestStash.Candidate(staged, "late", MediaFixtures.DistinctPng(9)));
        var rename = store.Rename(cat, "renamed");
        var tag = store.AddTag([cat], "late");
        var openWith = store.BeginOpenWith(cat);
        var forEdit = await store.ForEdit(cat.Id);
        using var confirmed = await store.Confirm([cat], "Copy");
        var dragged = store.HoldWhileDragged([cat], "Drag out");
        var recent = store.Recent();
        var recentInTime = recent.Wait(TimeSpan.FromMilliseconds(500));
        var movingWhenAnswered = !moving.IsCompleted;
        var moved = await moving;
        await release;

        Assert.IsTrue(recentInTime && recent.Result.Succeeded && movingWhenAnswered, "the summon's listing is answered during the copy (G1)");
        Assert.IsTrue(moved.Succeeded, moved.Failure?.Sentence);
        var refusals = new (string Entry, Failure? Failure)[]
        {
            ("Delete", (await delete).Single().Failure),
            ("CommitImport", (await import).Failure),
            ("Rename", (await rename).Failure),
            ("AddTag", (await tag).Single().Failure),
            ("BeginOpenWith", (await openWith).Failure),
            ("ForEdit", forEdit.Failure),
            ("Confirm", confirmed.Items.Single().Failure),
            ("HoldWhileDragged", dragged.Failure),
        };
        foreach (var (entry, failure) in refusals)
        {
            Assert.AreEqual(MoveReasons.InProgress, failure?.Reason, entry);
            Assert.AreNotEqual(StashMove.Action, failure!.Action, entry + " names its own action");
        }

        Assert.AreEqual("Copy", refusals[6].Failure!.Action);
        Assert.AreEqual("Drag out", refusals[7].Failure!.Action);
        var reopened = await StashStore.Open(chosen, stash.RecycleBin, stash.Retry, stash.Clock, openWithRoot: stash.OpenWithRoot);
        using var opened = reopened.Store!;
        Assert.AreEqual("cat plain", string.Join(" ", (await opened.All()).Value!.Select(i => i.Name).Order()), "nothing asked during the copy landed");
        Assert.IsEmpty(stash.RecycleBin.Asked);
    }

    [TestMethod]
    public async Task EveryCallOfAStoreAMoveSwitchedAwayFromIsRefusedAndNoneThrows()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var cat = await stash.ImportFixture("cat", "still.png");
        var deleted = (await store.Delete([(await stash.ImportFixture("gone", "photo.jpg")).Id])).Single().Value!;
        var staged = await stash.Stage("late.png", MediaFixtures.DistinctPng(9));
        Assert.IsTrue((await store.Move(Keeper(stash), Chosen(stash))).Succeeded);

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
            ("Move", async () => await store.Move(Keeper(stash), Chosen(stash, "again"))),
        };
        foreach (var (entry, call) in answers)
        {
            var answer = await call();
            Assert.AreEqual(MoveReasons.InProgress, answer?.Failure?.Reason, entry);
        }
    }

    [TestMethod]
    [DoNotParallelize]
    public async Task ARemovalStillRefusedAfterTheSwitchIsNamedAndTheNextStartFinishesIt()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var plain = await stash.ImportFixture("plain", "photo.jpg");
        var keeper = Keeper(stash);
        var chosen = Chosen(stash);
        FileStream? held = null;
        Outcome<string> moved;
        try
        {
            moved = await store.Move(keeper, chosen, new Reported(p =>
            {
                if (p is { Phase: MovePhase.Removing, Done: 0 } && held is null)
                {
                    held = new FileStream(plain.FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                }
            }));

            Assert.AreEqual(new Failure(StashMove.Action, Path.GetFileName(plain.FilePath), HeldFileRetry.HeldReason), moved.Failure);
            Assert.AreEqual(chosen, moved.Value, "a failure after the switch carries the new folder");
            var saved = Saved(stash);
            Assert.AreEqual((chosen, (string?)null, stash.Folder.Root), (saved.StashFolder, saved.MoveCopyInProgress, saved.PreviousStashFolderPendingRemoval));
            Assert.IsTrue(File.Exists(stash.Folder.StoreFile), "the store file goes last");

            var stillHeld = await StashMove.FinishAtStart(keeper, chosen, stash.Retry);
            Assert.AreEqual(new Failure(StashMove.FinishAction, Path.GetFileName(plain.FilePath), HeldFileRetry.HeldReason), stillHeld.Single());
            Assert.AreEqual(stash.Folder.Root, Saved(stash).PreviousStashFolderPendingRemoval, "kept for the next start");
        }
        finally
        {
            held?.Dispose();
        }

        stash.Close();
        Assert.IsEmpty(await StashMove.FinishAtStart(keeper, chosen, stash.Retry));
        Assert.IsFalse(Directory.Exists(stash.Folder.Root));
        Assert.IsNull(Saved(stash).PreviousStashFolderPendingRemoval);
        Assert.AreEqual(chosen, Saved(stash).StashFolder);
    }

    [TestMethod]
    [DoNotParallelize]
    public async Task AMoveAskedWhileAnEarlierRemovalStillWaitsFinishesItFirstOrIsRefusedKeepingItsRecord()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var plain = await stash.ImportFixture("plain", "photo.jpg");
        var keeper = Keeper(stash);
        var second = Chosen(stash, "second");
        var third = Chosen(stash, "third");
        FileStream? held = null;
        try
        {
            await store.Move(keeper, second, new Reported(p =>
            {
                if (p is { Phase: MovePhase.Removing, Done: 0 } && held is null)
                {
                    held = new FileStream(plain.FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                }
            }));
            var opening = await StashStore.Open(second, stash.RecycleBin, stash.Retry, stash.Clock, openWithRoot: stash.OpenWithRoot);
            using var atSecond = opening.Store!;

            var refused = await atSecond.Move(keeper, third);

            Assert.AreEqual(new Failure(StashMove.FinishAction, Path.GetFileName(plain.FilePath), HeldFileRetry.HeldReason), refused.Failure);
            Assert.AreEqual((second, stash.Folder.Root), (Saved(stash).StashFolder, Saved(stash).PreviousStashFolderPendingRemoval), "the earlier removal's record kept");
            Assert.IsEmpty(Directory.GetFileSystemEntries(third));
            held!.Dispose();
            held = null;

            var moved = await atSecond.Move(keeper, third);

            Assert.IsTrue(moved.Succeeded, moved.Failure?.Sentence);
            Assert.IsFalse(Directory.Exists(stash.Folder.Root), "the earlier removal finished first");
            Assert.IsFalse(Directory.Exists(second));
            Assert.AreEqual((third, (string?)null), (Saved(stash).StashFolder, Saved(stash).PreviousStashFolderPendingRemoval));
        }
        finally
        {
            held?.Dispose();
        }
    }

    [TestMethod]
    [DoNotParallelize]
    public async Task ASettingsWriteWaitingOnAHeldFileStillLetsTheListingBeAnswered()
    {
        using var stash = new TestStash { Retry = TimeSpan.FromSeconds(5) };
        var store = await stash.OpenCleanly();
        await stash.ImportFixture("plain", "photo.jpg");
        var keeper = Keeper(stash);
        var release = HeldFiles.HeldFor(SettingsPath(stash), TimeSpan.FromSeconds(1.5));
        var moving = store.Move(keeper, Chosen(stash));
        await Task.Delay(300);

        var recent = store.Recent();
        var answeredInTime = recent.Wait(TimeSpan.FromMilliseconds(500));
        var movingWhenAnswered = !moving.IsCompleted;
        var moved = await moving;
        await release;

        Assert.IsTrue(answeredInTime && recent.Result.Succeeded && movingWhenAnswered, "the summon's listing is answered while the record's write waits (G1)");
        Assert.IsTrue(moved.Succeeded, moved.Failure?.Sentence);
    }
}
