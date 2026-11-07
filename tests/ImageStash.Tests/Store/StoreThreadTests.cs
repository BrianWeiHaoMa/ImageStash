using System.Diagnostics;
using ImageStash.Files;
using ImageStash.Media;
using ImageStash.Store;
using ImageStash.Tests.Support;

namespace ImageStash.Tests.Store;

/// <summary>
/// The store's one thread (Approach, State: The store, Every change; G1, G35): while idle it runs reads and changes in
/// the order they were asked; while a change waits between two attempts on a held file it answers the reads queued
/// meanwhile, on itself, each whole, and no change; the held-file duration stays elapsed time however many reads come; a
/// wait asked inside a transaction is refused as a defect, and one asked off the thread sleeps and answers nothing.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class StoreThreadTests
{
    [TestMethod]
    public async Task AReadQueuedWhileAChangeWaitsOnAHeldFileIsAnsweredOnTheThreadAndAChangeQueuedThenRunsAfterTheWaitingOne()
    {
        using var opened = await OpenedStore.Open(HeldFileRetry.Duration);
        var path = opened.File("held.png");
        var storeThread = await opened.Thread.Run(() => Environment.CurrentManagedThreadId);
        var order = new List<string>();

        Task<Outcome> waiting;
        Task<int> reading;
        Task<bool> second;
        bool readInTime, waitingWhenRead, secondWhileHeld;
        using (Hold(path))
        {
            waiting = opened.Thread.Run(() =>
            {
                var moved = opened.Store.RetryHeldFile("Move", path, () => File.Move(path, path + ".moved"));
                order.Add("the waiting change ends");
                return moved;
            });
            await Task.Delay(ReadsWhileAChangeWaits.AskedAfter);
            second = opened.Thread.Run(() => { order.Add("the second change"); return true; });
            reading = opened.Thread.Read(() => { order.Add("the read"); return Environment.CurrentManagedThreadId; });
            readInTime = await Task.WhenAny(reading, Task.Delay(ReadsWhileAChangeWaits.AnsweredWithin)) == reading;
            waitingWhenRead = waiting.IsCompleted;
            await Task.Delay(TimeSpan.FromSeconds(1));
            secondWhileHeld = second.IsCompleted;
        }

        Assert.IsTrue((await waiting).Succeeded, (await waiting).Failure?.ToString());
        Assert.IsTrue(await second);
        Assert.IsTrue(readInTime, "answered between the attempts (G1)");
        Assert.IsFalse(waitingWhenRead);
        Assert.IsFalse(secondWhileHeld, "no change runs inside the waiting one");
        Assert.AreEqual(storeThread, await reading, "answered by the store's own thread, through its one connection");
        CollectionAssert.AreEqual(new[] { "the read", "the waiting change ends", "the second change" }, order);
    }

    [TestMethod]
    public async Task WhileNothingWaitsAReadAskedAfterAChangeIsAnsweredOnlyOnceThatChangeHasRun()
    {
        using var opened = await OpenedStore.Open(HeldFileRetry.Duration);
        var order = new List<string>();

        var change = opened.Thread.Run(() => { Thread.Sleep(300); order.Add("change"); return true; });
        var read = opened.Thread.Read(() => { order.Add("read"); return true; });
        await Task.WhenAll(change, read);

        CollectionAssert.AreEqual(new[] { "change", "read" }, order);
    }

    [TestMethod]
    public async Task AnItemReadAskedRightAfterARenameAnswersTheNewName()
    {
        using var stash = new TestStash();
        var store = await stash.OpenCleanly();
        var cat = await stash.Import("cat");

        var renaming = store.Rename(cat, "dog");
        var read = await store.Item(cat.Id);

        Assert.AreEqual(("dog", stash.Folder.InRoot("dog.png")), (read.Value!.Name, read.Value.FilePath));
        Assert.IsTrue((await renaming).Succeeded);
    }

    [TestMethod]
    public async Task AReadThatThrowsDuringAWaitFailsItsOwnCallerAndTheWaitingChangeCompletesAsIfItHadNotRun()
    {
        using var opened = await OpenedStore.Open(HeldFileRetry.Duration);
        var path = opened.File("held.png");
        var attempts = 0;

        Task<Outcome> waiting;
        Task<int> throwing;
        using (Hold(path))
        {
            waiting = opened.Thread.Run(() => opened.Store.RetryHeldFile("Move", path, () => { attempts++; File.Move(path, path + ".moved"); }));
            await Task.Delay(ReadsWhileAChangeWaits.AskedAfter);
            throwing = opened.Thread.Read<int>(() => throw new InvalidOperationException("a broken read"));
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => throwing.WaitAsync(ReadsWhileAChangeWaits.AnsweredWithin));
            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        var moved = await waiting;
        Assert.IsTrue(moved.Succeeded, moved.Failure?.ToString());
        Assert.IsTrue(File.Exists(path + ".moved"));
        Assert.IsGreaterThan(2, attempts, "it went on trying after the read failed");
        Assert.IsTrue(await opened.Thread.Read(() => true), "the thread still answers");
    }

    [TestMethod]
    public async Task AWaitOnAHeldFileAskedInsideATransactionIsRefusedAndTheTransactionCommitsNothing()
    {
        using var opened = await OpenedStore.Open(HeldFileRetry.Duration);
        var path = opened.File("free.png");
        var store = opened.Store;
        var commits = await opened.Thread.Read(() => store.Commits);

        // Each change makes a table, then asks for a wait: the throw must roll the table back.
        Func<object>[] waits =
        [
            () => store.RetryHeldFile("Move", path, () => 0),
            () => store.TryHeldFileOnce("Move", path, () => 0),
            () => store.RetryWhileHeld(() => 0, _ => false),
        ];
        foreach (var wait in waits)
        {
            var asked = opened.Thread.Run(() => store.Change("Scratch", () =>
            {
                store.Execute("CREATE TABLE scratch (x)");
                return wait();
            }));
            var refused = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => asked);
            StringAssert.Contains(refused.Message, "inside a store transaction");
        }

        var insideRead = opened.Thread.Read(() => store.Read("Scratch", () => store.RetryHeldFile("Move", path, () => 0)));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => insideRead);
        var tables = await opened.Thread.Read(() => store.Read("Scratch", () => store.Scalar("SELECT count(*) FROM sqlite_master WHERE name = 'scratch'")).Value);
        Assert.AreEqual(0L, tables, "nothing committed");
        Assert.AreEqual(commits, await opened.Thread.Read(() => store.Commits));
    }

    [TestMethod]
    public async Task AWaitAskedOffTheThreadWhileItIsInsideATransactionIsNotRefusedAndSleepsBetweenItsAttempts()
    {
        using var opened = await OpenedStore.Open(HeldFileRetry.Duration);
        var path = opened.File("held.png");
        using var inside = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var change = opened.Thread.Run(() => opened.Store.Change("Scratch", () =>
        {
            inside.Set();
            release.Wait();
        }));
        inside.Wait();
        var attempts = 0;

        var hold = Hold(path);
        var moving = Task.Run(() => opened.Store.RetryHeldFile("Move", path, () => { attempts++; File.Move(path, path + ".moved"); }));
        await Task.Delay(300);
        hold.Dispose();
        var moved = await moving;
        release.Set();

        Assert.IsTrue(moved.Succeeded, moved.Failure?.ToString());
        Assert.IsGreaterThan(1, attempts);
        Assert.IsTrue((await change).Succeeded);
    }

    [TestMethod]
    public async Task ReadsQueuedFasterThanTheyAreAnsweredNeitherStopTheAttemptsNorStretchTheDuration()
    {
        using var opened = await OpenedStore.Open(TimeSpan.FromSeconds(1));
        var path = opened.File("held.png");
        var clock = Stopwatch.StartNew();
        var attempts = 0;
        TimeSpan ended = default, behindRan = default;

        Task<Outcome> waiting;
        Task<bool> behind;
        using (Hold(path))
        {
            waiting = opened.Thread.Run(() =>
            {
                var moved = opened.Store.RetryHeldFile("Move", path, () => { attempts++; File.Move(path, path + ".moved"); });
                ended = clock.Elapsed;
                return moved;
            });
            using var stop = new CancellationTokenSource();
            // Every 10 ms a read that takes 15 ms: more arrive than are answered, for at most two seconds.
            var producing = Task.Run(async () =>
            {
                while (!stop.IsCancellationRequested && clock.Elapsed < TimeSpan.FromSeconds(2))
                {
                    _ = opened.Thread.Read(() => { Thread.Sleep(15); return true; });
                    await Task.Delay(10);
                }
            });
            await Task.Delay(100);
            behind = opened.Thread.Run(() => { behindRan = clock.Elapsed; return true; });
            await Task.WhenAny(behind, Task.Delay(TimeSpan.FromSeconds(3)));
            stop.Cancel();
            await producing;
        }

        var outcome = await waiting;
        Assert.IsTrue(await behind);
        Assert.AreEqual(new Failure("Move", "held.png", HeldFileRetry.HeldReason), outcome.Failure);
        Assert.IsInRange(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1.5), ended, "named close to its budget (G35)");
        Assert.IsGreaterThanOrEqualTo(6, attempts, "an attempt about every spacing through the backlog");
        Assert.IsInRange(ended, ended + TimeSpan.FromMilliseconds(300), behindRan, "the change queued behind it runs next");
    }

    [TestMethod]
    public async Task AWaitAskedOffTheThreadLetsTheThreadAnswerAReadItselfAndKeepsItsOwnSpacing()
    {
        using var opened = await OpenedStore.Open(HeldFileRetry.Duration);
        var path = opened.File("held.png");
        var storeThread = await opened.Thread.Run(() => Environment.CurrentManagedThreadId);
        var clock = Stopwatch.StartNew();
        var tried = new List<long>();
        var caller = 0;

        var hold = Hold(path);
        var moving = Task.Run(() =>
        {
            caller = Environment.CurrentManagedThreadId;
            return opened.Store.RetryHeldFile("Move", path, () => { tried.Add(clock.ElapsedMilliseconds); File.Move(path, path + ".moved"); });
        });
        await Task.Delay(150);
        var reading = opened.Thread.Read(() => Environment.CurrentManagedThreadId);
        var readInTime = await Task.WhenAny(reading, Task.Delay(ReadsWhileAChangeWaits.AnsweredWithin)) == reading;
        await Task.Delay(350);
        hold.Dispose();
        var moved = await moving;

        Assert.IsTrue(readInTime);
        Assert.AreEqual(storeThread, await reading);
        Assert.AreNotEqual(caller, await reading, "never answered on the caller's thread");
        Assert.IsTrue(moved.Succeeded, moved.Failure?.ToString());
        Assert.IsGreaterThanOrEqualTo(4, tried.Count);
        Assert.IsTrue(tried.Zip(tried.Skip(1), (a, b) => b - a).All(gap => gap >= 90), $"spaced: {string.Join(", ", tried)}");
    }

    [TestMethod]
    public async Task AnAttemptRetriedWhileHeldOnTheThreadAnswersReadsBetweenItsAttemptsAndAnswersItsLast()
    {
        using var opened = await OpenedStore.Open(HeldFileRetry.Duration);
        var clock = Stopwatch.StartNew();

        var retrying = opened.Thread.Run(() => opened.Store.RetryWhileHeld(() => clock.Elapsed < TimeSpan.FromMilliseconds(800) ? "held" : "done", answer => answer == "held"));
        await Task.Delay(ReadsWhileAChangeWaits.AskedAfter);
        var reading = opened.Thread.Read(() => true);
        var readInTime = await Task.WhenAny(reading, Task.Delay(ReadsWhileAChangeWaits.AnsweredWithin)) == reading;
        var retryingWhenRead = retrying.IsCompleted;

        Assert.IsTrue(readInTime);
        Assert.IsFalse(retryingWhenRead);
        Assert.AreEqual("done", await retrying);
    }

    // Read sharing only, as a scanner reads a fresh file (E3): a move or delete of it is refused while this is open.
    private static FileStream Hold(string path) => new(path, FileMode.Open, FileAccess.Read, FileShare.Read);

    /// <summary>A store opened directly on its own thread, as <see cref="StashStore.Open"/> opens one, in a scratch folder.</summary>
    private sealed class OpenedStore : IDisposable
    {
        private readonly ScratchFolder scratch;

        private OpenedStore(ScratchFolder scratch, StoreThread thread, StoreContext store)
        {
            this.scratch = scratch;
            Thread = thread;
            Store = store;
        }

        public StoreThread Thread { get; }

        public StoreContext Store { get; }

        public static async Task<OpenedStore> Open(TimeSpan retry)
        {
            var scratch = new ScratchFolder();
            var thread = new StoreThread();
            var opened = await thread.Run(() => StoreOpen.Open(
                new StashFolder(Path.Combine(scratch.Path, "stash")), new TestRecycleBin(Path.Combine(scratch.Path, "recycled")), retry,
                TimeProvider.System, FinishedPicture.Render, null, MediaDecoder.ReadPicture, thread));
            Assert.IsTrue(opened.Succeeded, opened.Failure?.ToString());
            return new OpenedStore(scratch, thread, opened.Value.Store);
        }

        /// <summary>A file outside the stash, written now, for a wait to be asked on.</summary>
        public string File(string name)
        {
            var path = scratch.File(name);
            System.IO.File.WriteAllText(path, name);
            return path;
        }

        public void Dispose()
        {
            Thread.Run(() => { Store.Connection.Dispose(); return true; });
            Thread.Dispose();
            scratch.Dispose();
        }
    }
}
