using ImageStash.Files;
using ImageStash.Store;
using ImageStash.Tests.Support;
using Microsoft.Data.Sqlite;

namespace ImageStash.Tests.Files;

/// <summary>
/// G35: a held file is retried before its failure is named, even when its holder lets go just as a refusal is being
/// told from a held file; and G35's entry: a store transaction that fails because the store is held is retried whole
/// within the duration, then named.
/// </summary>
[TestClass]
public sealed class HeldFileRetryTests
{
    [TestMethod]
    public void AFileRefusedJustAsItsHolderLetsGoIsTriedAgain()
    {
        using var scratch = new ScratchFolder();
        var path = scratch.File("settings.json");
        File.WriteAllText(path, "{}");
        var attempts = 0;

        // The first attempt is refused as a replace onto a held file is; by the time the policy looks, the file is free.
        var outcome = HeldFileRetry.Run("Save settings", path, TimeSpan.FromSeconds(5), () =>
        {
            if (++attempts == 1)
            {
                throw new UnauthorizedAccessException();
            }
        });

        Assert.IsTrue(outcome.Succeeded, outcome.Failure?.ToString());
        Assert.AreEqual(2, attempts);
    }

    [TestMethod]
    public void AReplacementRefusedAgainWithItsHolderGoneEachTimeItIsLookedAtIsTriedAgain()
    {
        using var scratch = new ScratchFolder();
        var path = scratch.File("settings.json");
        File.WriteAllText(path, "{}");
        var attempts = 0;

        // A holder that comes back, as one reading the file again and again does, gone each time the policy looks.
        var outcome = HeldFileRetry.Run("Save settings", path, TimeSpan.FromSeconds(5), () =>
        {
            if (++attempts <= 8)
            {
                throw new UnauthorizedAccessException();
            }
        }, replaced: path);

        Assert.IsTrue(outcome.Succeeded, outcome.Failure?.ToString());
        Assert.AreEqual(9, attempts);
    }

    [TestMethod]
    public void AReplacementRefusedWithNothingHoldingTheFileIsNamedAsRefusedOnceTheDurationEnds()
    {
        using var scratch = new ScratchFolder();
        var path = scratch.File("settings.json");
        File.WriteAllText(path, "{}");
        var attempts = 0;

        var outcome = HeldFileRetry.Run("Save settings", path, TimeSpan.FromMilliseconds(300), () =>
        {
            attempts++;
            throw new UnauthorizedAccessException();
        }, replaced: path);

        Assert.AreEqual(new Failure("Save settings", "settings.json", "Windows refused access to it"), outcome.Failure);
        Assert.IsGreaterThan(2, attempts);
    }

    [TestMethod]
    public async Task ATryThatWaitedLongerThanTheDurationForItsTurnAndFoundTheFileHeldIsStillRetried()
    {
        var attempts = 0;

        var answer = await HeldFileRetry.RunSpaced(TimeSpan.FromMilliseconds(200), async () =>
        {
            if (++attempts == 1)
            {
                await Task.Delay(400);
            }

            return attempts;
        }, tried => tried == 1);

        Assert.AreEqual(2, answer, "the file found held on the first try is owed the duration from then (G35)");
    }

    [TestMethod]
    public void ARefusalOfAFileNobodyHoldsIsNamedWithoutWaitingOutTheDuration()
    {
        using var scratch = new ScratchFolder();
        var path = scratch.File("settings.json");
        File.WriteAllText(path, "{}");
        var attempts = 0;
        var clock = System.Diagnostics.Stopwatch.StartNew();

        var outcome = HeldFileRetry.Run("Save settings", path, TimeSpan.FromSeconds(5), () =>
        {
            attempts++;
            throw new UnauthorizedAccessException();
        });

        Assert.AreEqual(new Failure("Save settings", "settings.json", "Windows refused access to it"), outcome.Failure);
        Assert.AreEqual(2, attempts);
        Assert.IsLessThan(1000, clock.ElapsedMilliseconds);
    }

    [TestMethod]
    public void TheSpacingBetweenAttemptsOnAHeldFileIsSpentInTheCallersWaitInsteadOfASleep()
    {
        using var scratch = new ScratchFolder();
        var path = scratch.File("held.png");
        File.WriteAllText(path, "held");
        var attempts = 0;
        var waits = new List<TimeSpan>();
        var clock = System.Diagnostics.Stopwatch.StartNew();

        var outcome = HeldFileRetry.Run("Rename", path, TimeSpan.FromSeconds(5), () =>
        {
            if (++attempts <= 5)
            {
                throw new IOException("held", SharingViolation);
            }
        }, wait: waits.Add);

        Assert.IsTrue(outcome.Succeeded, outcome.Failure?.ToString());
        Assert.AreEqual(6, attempts);
        CollectionAssert.AreEqual(Enumerable.Repeat(TimeSpan.FromMilliseconds(100), 5).ToList(), waits, "once between each two attempts, with the spacing");
        Assert.IsLessThan(250, clock.ElapsedMilliseconds, "five spacings slept would take half a second");
    }

    [TestMethod]
    public void TheDurationIsElapsedTimeSoAWaitThatRunsLongStillEndsTheAttempts()
    {
        using var scratch = new ScratchFolder();
        var path = scratch.File("held.png");
        File.WriteAllText(path, "held");
        var attempts = 0;

        // Each wait overruns its spacing threefold, as a read answered in it may: the attempts end by time, not by count.
        var outcome = HeldFileRetry.Run("Rename", path, TimeSpan.FromMilliseconds(900), () =>
        {
            attempts++;
            throw new IOException("held", SharingViolation);
        }, wait: spacing => Thread.Sleep(spacing * 3));

        Assert.AreEqual(new Failure("Rename", "held.png", HeldFileRetry.HeldReason), outcome.Failure);
        Assert.IsInRange(3, 5, attempts);
    }

    [TestMethod]
    public void AnAttemptWithoutARetryOfItsOwnIsAskedAgainWhileItsAnswerIsHeldAndTheLastAnswerStands()
    {
        var answers = new Queue<string>(["held", "held", "done"]);
        var waits = new List<TimeSpan>();

        var freed = HeldFileRetry.RunWhileHeld(TimeSpan.FromSeconds(5), answers.Dequeue, answer => answer == "held", waits.Add);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var asked = 0;
        var stillHeld = HeldFileRetry.RunWhileHeld(TimeSpan.FromMilliseconds(300), () => ++asked, _ => true);

        Assert.AreEqual("done", freed);
        Assert.HasCount(2, waits);
        Assert.IsGreaterThan(1, stillHeld, "asked again until the duration is spent");
        Assert.AreEqual(asked, stillHeld, "the last answer stands");
        Assert.IsGreaterThanOrEqualTo(300, clock.ElapsedMilliseconds, "slept between attempts without a wait of the caller's");
    }

    [TestMethod]
    [DataRow(FileShare.None)]
    [DataRow(FileShare.Read)]
    [DataRow(FileShare.ReadWrite)]
    public void AFileAnotherHandleHoldsOpenIsHeldWhateverSharingThatHandleAllows(FileShare sharing)
    {
        using var scratch = new ScratchFolder();
        var path = scratch.File("held.png");
        File.WriteAllText(path, "held");

        using (new FileStream(path, FileMode.Open, FileAccess.Read, sharing))
        {
            Assert.IsTrue(HeldFileRetry.OpenElsewhere(path), "the probe asks for no sharing, so any other open handle refuses it (E3)");
        }

        Assert.IsFalse(HeldFileRetry.OpenElsewhere(path), "let go, the file is free");
    }

    [TestMethod]
    public void AFolderOrAMissingFileIsNotHeldSoItsRefusalIsNamedAtOnce()
    {
        using var scratch = new ScratchFolder();

        // A folder opened as a file is refused as access denied, and a missing file as not found: neither is a file
        // another program holds, so neither waits out the duration (G35).
        Assert.IsFalse(HeldFileRetry.OpenElsewhere(scratch.Path), "a folder");
        Assert.IsFalse(HeldFileRetry.OpenElsewhere(scratch.File("missing.png")), "a missing file");
    }

    [TestMethod]
    public void AFolderHoldingOneHeldFileAmongFreeOnesAtAnyDepthHoldsAnOpenFileUntilItsHolderLetsGo()
    {
        using var scratch = new ScratchFolder();
        var folder = Path.Combine(scratch.Path, "extracted");
        Directory.CreateDirectory(Path.Combine(folder, "originals"));
        File.WriteAllText(Path.Combine(folder, "free.png"), "free");
        File.WriteAllText(Path.Combine(folder, "stash.db"), "free");
        var held = Path.Combine(folder, "originals", "held.png");
        File.WriteAllText(held, "held");

        using (new FileStream(held, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            Assert.IsTrue(HeldFileRetry.HoldsAnOpenFile(folder), "a held file in a folder inside it counts (G35, G36)");
        }

        Assert.IsFalse(HeldFileRetry.HoldsAnOpenFile(folder), "its holder gone, nothing inside is held");
        Assert.IsFalse(HeldFileRetry.HoldsAnOpenFile(Path.Combine(scratch.Path, "no such folder")), "a missing folder");
    }

    [TestMethod]
    public async Task AStoreHeldBrieflyByAnotherConnectionIsWaitedFor()
    {
        using var stash = new TestStash { Retry = TimeSpan.FromSeconds(5) };
        var store = await stash.OpenCleanly();
        var staged = await stash.Stage("waited.png", TestStash.Bytes(30));
        var holder = HoldStore(stash);

        var committing = store.CommitImport(TestStash.Candidate(staged, "waited", TestStash.Bytes(30)));
        await Task.Delay(700);
        Release(holder);

        var committed = await committing;
        Assert.IsTrue(committed.Succeeded, committed.Failure?.ToString());
        Assert.AreEqual(committed.Value!.Item.Id, (await stash.All()).Single().Id);
    }

    [TestMethod]
    public async Task AStoreHeldPastTheDurationIsNamed()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var staged = await stash.Stage("refused.png", TestStash.Bytes(31));
        var holder = HoldStore(stash);

        Outcome<ImportResult> committed;
        try
        {
            committed = await store.CommitImport(TestStash.Candidate(staged, "refused", TestStash.Bytes(31)));
        }
        finally
        {
            Release(holder);
        }

        Assert.AreEqual(new Failure(ImportCommit.Action, StashFolder.StoreFileName, HeldFileRetry.HeldReason), committed.Failure);
        Assert.IsFalse(File.Exists(staged), "a failure before the commit leaves nothing staged");
        Assert.IsEmpty(await stash.All());
        var again = await stash.Stage("refused.png", TestStash.Bytes(31));
        Assert.IsTrue((await store.CommitImport(TestStash.Candidate(again, "refused", TestStash.Bytes(31)))).Succeeded);
    }

    [TestMethod]
    public async Task AStoreHeldAfterTheRenameLeavesTheItemListedAndTheNextStartClearsItsStep()
    {
        using var stash = new TestStash { Retry = TimeSpan.FromSeconds(2) };
        var store = await stash.OpenCleanly();
        var bytes = TestStash.Bytes(32);
        var staged = await stash.Stage("late.png", bytes);

        // The staged file is held so the rename waits; once the row is committed the store is held and the rename let through.
        using var stagedHolder = new FileStream(staged, FileMode.Open, FileAccess.Read, FileShare.None);
        var committing = store.CommitImport(TestStash.Candidate(staged, "late", bytes));
        await WaitForRow(stash, "late");
        var holder = HoldStore(stash);
        stagedHolder.Dispose();
        Outcome<ImportResult> committed;
        try
        {
            committed = await committing;
        }
        finally
        {
            Release(holder);
        }

        Assert.AreEqual(new Failure(ImportCommit.Action, StashFolder.StoreFileName, HeldFileRetry.HeldReason), committed.Failure);
        var item = committed.Value!.Item;
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(item.FilePath));
        Assert.AreEqual(item.Id, (await stash.All()).Single().Id, "the picture on disk is in the stash");

        stash.Close();
        await stash.OpenCleanly();
        Assert.AreEqual(item.Id, (await stash.All()).Single().Id);
        stash.Close();
        Assert.IsTrue(stash.SqlQuery("SELECT pending_steps IS NULL FROM items", r => r.GetBoolean(0)).Single());
    }

    // ERROR_SHARING_VIOLATION as an HRESULT, the refusal a file another program holds open gives.
    private const int SharingViolation = unchecked((int)0x80070020);

    private static async Task WaitForRow(TestStash stash, string name)
    {
        using var reader = new SqliteConnection($"Data Source={stash.Folder.StoreFile};Mode=ReadOnly;Pooling=False");
        reader.Open();
        using var count = reader.CreateCommand();
        count.CommandText = "SELECT count(*) FROM items WHERE name = $name";
        count.Parameters.AddWithValue("$name", name);
        for (var waited = 0; (long)count.ExecuteScalar()! == 0; waited += 20)
        {
            Assert.IsLessThan(5000, waited, $"no row for {name} was committed");
            await Task.Delay(20);
        }
    }

    private static SqliteConnection HoldStore(TestStash stash)
    {
        var holder = new SqliteConnection($"Data Source={stash.Folder.StoreFile};Pooling=False");
        holder.Open();
        using var begin = holder.CreateCommand();
        begin.CommandText = "BEGIN IMMEDIATE";
        begin.ExecuteNonQuery();
        return holder;
    }

    private static void Release(SqliteConnection holder)
    {
        using var rollback = holder.CreateCommand();
        rollback.CommandText = "ROLLBACK";
        rollback.ExecuteNonQuery();
        holder.Dispose();
    }
}
