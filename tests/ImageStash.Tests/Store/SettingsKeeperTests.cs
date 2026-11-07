using System.Diagnostics;
using ImageStash.Files;
using ImageStash.Store;
using ImageStash.Tests.Support;

namespace ImageStash.Tests.Store;

/// <summary>
/// The settings in effect and their one writer (State: Settings): a change takes effect at once and is written off the
/// caller's thread, the latest of a burst winning; a flush at Quit writes at once (G2, G3); a held file is retried, then
/// named (G35); nothing is written over a file start could not read.
/// </summary>
[TestClass]
public sealed class SettingsKeeperTests
{
    private static readonly TimeSpan ShortRetry = TimeSpan.FromMilliseconds(300);

    private readonly List<Failure> failures = [];

    private SettingsKeeper Keeper(ScratchFolder scratch, TimeSpan quiet) =>
        new(scratch.Path, Settings.Load(scratch.Path, ShortRetry), ShortRetry, quiet, failures.Add);

    private static Settings Saved(ScratchFolder scratch) => Settings.Load(scratch.Path, ShortRetry).Settings;

    [TestMethod]
    public async Task AChangeTakesEffectAtOnceAndIsWrittenAfterTheQuietSpell()
    {
        using var scratch = new ScratchFolder();
        var keeper = Keeper(scratch, TimeSpan.FromMilliseconds(50));

        keeper.Change(s => s with { PinnedSize = new WindowSize(1200, 800) });

        Assert.AreEqual(new WindowSize(1200, 800), keeper.Current.PinnedSize);
        var waited = Stopwatch.StartNew();
        while (Saved(scratch).PinnedSize != new WindowSize(1200, 800))
        {
            Assert.IsLessThan(5000, waited.ElapsedMilliseconds, "the change was never written");
            await Task.Delay(20);
        }

        Assert.IsEmpty(failures);
    }

    [TestMethod]
    public async Task ResizedThenQuitAtOnceTheLastSizeAndPlaceAreWrittenAndTheFlyoutsKept()
    {
        using var scratch = new ScratchFolder();
        var keeper = Keeper(scratch, TimeSpan.FromSeconds(30));

        for (var width = 900; width <= 1180; width += 20)
        {
            keeper.Change(s => s with { PinnedSize = new WindowSize(width, 800) });
        }

        keeper.Change(s => s with { PinnedPosition = new WindowPosition(50, 60) });
        var flushing = Stopwatch.StartNew();
        await keeper.Flush();

        Assert.IsLessThan(5000, flushing.ElapsedMilliseconds, "a flush does not wait for the quiet spell");
        var saved = Saved(scratch);
        Assert.AreEqual(new WindowSize(1180, 800), saved.PinnedSize);
        Assert.AreEqual(new WindowPosition(50, 60), saved.PinnedPosition);
        Assert.AreEqual(WindowSize.FlyoutDefault, saved.FlyoutSize);
    }

    [TestMethod]
    public async Task AWriteRefusedPastTheRetryIsNamedAndTheChangeStaysInEffect()
    {
        using var scratch = new ScratchFolder();
        var keeper = Keeper(scratch, TimeSpan.Zero);

        using (new FileStream(scratch.File(Settings.FileName), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            keeper.Change(s => s with { PinnedSize = new WindowSize(1000, 700) });
            await keeper.Flush();
        }

        var failure = failures.Single();
        Assert.AreEqual("Save settings", failure.Action);
        Assert.AreEqual(Settings.FileName, failure.Subject);
        Assert.AreEqual(new WindowSize(1000, 700), keeper.Current.PinnedSize);
    }

    [TestMethod]
    public async Task NothingIsWrittenOverASettingsFileStartCouldNotRead()
    {
        using var scratch = new ScratchFolder();
        const string Broken = """{"stashFolder":"D:\\Stash","startWithWindows":fal""";
        File.WriteAllText(scratch.File(Settings.FileName), Broken);
        var keeper = Keeper(scratch, TimeSpan.Zero);

        keeper.Change(s => s with { PinnedSize = new WindowSize(1000, 700) });
        await keeper.Flush();

        Assert.AreEqual(Broken, File.ReadAllText(scratch.File(Settings.FileName)));
        Assert.AreEqual(new WindowSize(1000, 700), keeper.Current.PinnedSize, "the size still holds for this session");
        Assert.IsEmpty(failures);
    }

    [TestMethod]
    public async Task ChangeNowWritesAtOnceAfterAWriteWaitingForQuietAndAnswersItsWrite()
    {
        using var scratch = new ScratchFolder();
        var keeper = Keeper(scratch, TimeSpan.FromSeconds(30));
        keeper.Change(s => s with { PinnedSize = new WindowSize(1000, 700) });

        var writing = Stopwatch.StartNew();
        var written = await keeper.ChangeNow(s => s with { MoveCopyInProgress = @"D:\New" });

        Assert.IsTrue(written.Succeeded, written.Failure?.Sentence);
        Assert.IsLessThan(5000, writing.ElapsedMilliseconds, "the write waiting for quiet is written at once, not after it");
        var saved = Saved(scratch);
        Assert.AreEqual((@"D:\New", new WindowSize(1000, 700)), (saved.MoveCopyInProgress, saved.PinnedSize), "the file holds both when the answer comes");
        Assert.AreEqual(saved, keeper.Current);
        Assert.IsEmpty(failures);
    }

    [TestMethod]
    public async Task AChangeNowRefusedLeavesCurrentAsItWasAndKeepsAChangeMadeMeanwhile()
    {
        using var scratch = new ScratchFolder();
        var keeper = Keeper(scratch, TimeSpan.FromSeconds(30));
        var before = Saved(scratch);

        Outcome written;
        using (new FileStream(scratch.File(Settings.FileName), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var writing = keeper.ChangeNow(s => s with { StashFolder = @"D:\New", PreviousStashFolderPendingRemoval = @"D:\Old" });
            keeper.Change(s => s with { PinnedSize = new WindowSize(1000, 700) });
            written = await writing;
        }

        Assert.AreEqual(new Failure("Save settings", Settings.FileName, HeldFileRetry.HeldReason), written.Failure);
        Assert.AreEqual(((string?)null, (string?)null, new WindowSize(1000, 700)), (keeper.Current.StashFolder, keeper.Current.PreviousStashFolderPendingRemoval, keeper.Current.PinnedSize));
        Assert.AreEqual(before, Saved(scratch), "nothing of it landed");
        Assert.IsEmpty(failures, "its caller names it");
        await keeper.Flush();
        Assert.AreEqual(((string?)null, new WindowSize(1000, 700)), (Saved(scratch).StashFolder, Saved(scratch).PinnedSize), "a later write carries the other change and not the refused one");
    }

    [TestMethod]
    public async Task AChangeNowOverAFileStartCouldNotReadAnswersThatFailureAndChangesNothing()
    {
        using var scratch = new ScratchFolder();
        const string Broken = """{"stashFolder":"D:\\Stash","startWithWindows":fal""";
        File.WriteAllText(scratch.File(Settings.FileName), Broken);
        var keeper = Keeper(scratch, TimeSpan.Zero);

        var written = await keeper.ChangeNow(s => s with { MoveCopyInProgress = @"D:\New" });

        Assert.AreEqual(Settings.Load(scratch.Path, ShortRetry).Failure, written.Failure);
        Assert.IsNull(keeper.Current.MoveCopyInProgress);
        Assert.AreEqual(Broken, File.ReadAllText(scratch.File(Settings.FileName)));
    }
}
