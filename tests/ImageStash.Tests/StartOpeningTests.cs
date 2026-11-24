using ImageStash.Store;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests;

/// <summary>
/// G3: "Launching it while it does not run opens the stash window the same way [as a left-click on its tray icon would],
/// or pinned on G2's first run. The start Windows makes at sign-in (F1) is not such a launch: whether or not ImageStash
/// already runs, it opens the stash window only on G2's first run." Each start runs the dispatch the app's start runs,
/// after a stash that opened and after each of the three ways it does not: settings that could not be read, an import
/// still recorded, a store that would not open (G35).
/// </summary>
[TestClass]
public sealed class StartOpeningTests
{
    private static readonly string[] ByHand = [];
    private static readonly string[] AtSignIn = [Launch.SignInArgument];

    public enum Ending
    {
        Opened,
        SettingsUnreadable,
        ImportUnfinished,
        StoreFailed,
    }

    [TestMethod]
    [DataRow(Ending.Opened)]
    [DataRow(Ending.SettingsUnreadable)]
    [DataRow(Ending.ImportUnfinished)]
    [DataRow(Ending.StoreFailed)]
    public async Task ALaterLaunchByHandAsksForTheTraysSummonOnceBeforeTheStashSettles(Ending outcome)
    {
        using var later = await LaterStart.Of(outcome);

        var shows = await StartShows.Run(ByHand, later.Settings, later.Keeper, later.Stash, later.OpenStore);

        CollectionAssert.AreEqual(new[] { (StartShows.Tray, StashState.Opening) }, shows.Asked,
            "the window shown as a tray click shows it once it exists, whatever the opening then shows (G3, G35)");
        Assert.AreEqual(outcome == Ending.Opened ? StashState.Open : StashState.Failed, shows.Listing.State,
            "and the window then holds the stash, or why it did not open");
    }

    [TestMethod]
    [DataRow(Ending.Opened)]
    [DataRow(Ending.SettingsUnreadable)]
    [DataRow(Ending.ImportUnfinished)]
    [DataRow(Ending.StoreFailed)]
    public async Task ALaterStartAtSignInAsksForNoWindow(Ending outcome)
    {
        using var later = await LaterStart.Of(outcome);

        var shows = await StartShows.Run(AtSignIn, later.Settings, later.Keeper, later.Stash, later.OpenStore);

        Assert.IsEmpty(shows.Asked, "the start at sign-in opens no stash window after a first run (G3)");
        Assert.AreEqual(outcome == Ending.Opened ? StashState.Open : StashState.Failed, shows.Listing.State);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task AFirstRunWithAnEmptyStashAsksForThePinnedWindowOnceWithOrWithoutTheArgument(bool atSignIn)
    {
        using var scratch = new ScratchFolder();
        using var stash = new TestStash();
        var firstRun = Settings.Load(scratch.File("ImageStash"), stash.Retry);
        Assert.IsTrue(firstRun.FirstRun);

        var shows = await StartShows.Run(atSignIn ? AtSignIn : ByHand, firstRun, MovingStash.Keeper(stash, []), stash);

        CollectionAssert.AreEqual(new[] { (StartShows.Pinned, StashState.Open) }, shows.Asked,
            "pinned once its stash is known to be empty, and no flyout before it (G2, G3)");
    }

    [TestMethod]
    [DataRow(false, StashState.Open)]
    [DataRow(true, StashState.Open)]
    [DataRow(false, StashState.Failed)]
    [DataRow(true, StashState.Failed)]
    public async Task AFirstRunWhoseStashHoldsItemsOrDidNotOpenShowsWhatALaterRunShowsOnceItSettles(bool atSignIn, StashState settled)
    {
        using var scratch = new ScratchFolder();
        using var stash = new TestStash();
        var firstRun = Settings.Load(scratch.File("ImageStash"), stash.Retry);
        Func<string, Task<StoreOpening>> openStore;
        if (settled == StashState.Open)
        {
            await stash.OpenCleanly();
            await stash.Import("cat");
            openStore = _ => Task.FromResult(new StoreOpening(stash.Store, []));
        }
        else
        {
            var notAFolder = scratch.File("stash");
            File.WriteAllText(notAFolder, "a file where the stash folder should be");
            openStore = _ => stash.OpenAt(notAFolder);
        }

        var shows = await StartShows.Run(atSignIn ? AtSignIn : ByHand, firstRun, MovingStash.Keeper(stash, []), stash, openStore);

        if (atSignIn)
        {
            Assert.IsEmpty(shows.Asked, "no stash window from the start at sign-in but G2's (G3)");
        }
        else
        {
            CollectionAssert.AreEqual(new[] { (StartShows.Tray, settled) }, shows.Asked,
                "a launch by hand shows the window as a tray click would, once, after whether the stash is empty is known (G2, G3)");
        }
    }

    /// <summary>A start after an earlier one, set up to end as <see cref="Ending"/> says.</summary>
    private sealed class LaterStart : IDisposable
    {
        private readonly ListedStash? listed;

        private LaterStart(TestStash stash, SettingsLoad settings, SettingsKeeper keeper, Func<string, Task<StoreOpening>>? openStore, ListedStash? listed = null)
        {
            Stash = stash;
            Settings = settings;
            Keeper = keeper;
            OpenStore = openStore;
            this.listed = listed;
        }

        public TestStash Stash { get; }

        public SettingsLoad Settings { get; }

        public SettingsKeeper Keeper { get; }

        public Func<string, Task<StoreOpening>>? OpenStore { get; }

        public static async Task<LaterStart> Of(Ending outcome)
        {
            switch (outcome)
            {
                case Ending.ImportUnfinished:
                {
                    var listed = await ListedStash.Open();
                    var archive = await ExportedStash.Of(listed.Stash.Outside("export.zip"), null, "one");
                    // The replaced folder's recycle refused by the import and by the start after it: the record stands (State: Import stash…).
                    listed.Stash.RecycleBin.Refusing = _ => true;
                    Assert.IsFalse((await listed.ImportStash(archive)).Succeeded);
                    await listed.Settings.Flush();
                    var keeper = MovingStash.Keeper(listed.Stash, []);
                    Assert.IsNotNull(keeper.Current.ImportPending, "a record on disk at the start");
                    return new LaterStart(listed.Stash, Read(listed.Stash.Outside("settings"), listed.Stash.Retry), keeper, null, listed);
                }

                case Ending.SettingsUnreadable:
                {
                    var stash = new TestStash();
                    var folder = stash.Outside("unreadable");
                    Directory.CreateDirectory(folder);
                    File.WriteAllText(Path.Combine(folder, ImageStash.Store.Settings.FileName), """{"startWithWindows":fal""");
                    var load = ImageStash.Store.Settings.Load(folder, stash.Retry);
                    Assert.IsFalse(load.AsSaved);
                    return new LaterStart(stash, load, MovingStash.Keeper(stash, []), null);
                }

                case Ending.StoreFailed:
                {
                    var stash = new TestStash();
                    var keeper = MovingStash.Keeper(stash, []);
                    var notAFolder = stash.Outside("not a folder");
                    File.WriteAllText(notAFolder, "a file where the stash folder should be");
                    return new LaterStart(stash, Read(stash.Outside("settings"), stash.Retry), keeper, _ => stash.OpenAt(notAFolder));
                }

                default:
                {
                    var stash = new TestStash();
                    var keeper = MovingStash.Keeper(stash, []);
                    return new LaterStart(stash, Read(stash.Outside("settings"), stash.Retry), keeper, null);
                }
            }
        }

        /// <summary>The settings as a later start reads them: the file an earlier start wrote.</summary>
        private static SettingsLoad Read(string folder, TimeSpan retry)
        {
            var load = ImageStash.Store.Settings.Load(folder, retry);
            Assert.IsFalse(load.FirstRun);
            Assert.IsTrue(load.AsSaved);
            return load;
        }

        public void Dispose()
        {
            if (listed is not null)
            {
                listed.Stash.RecycleBin.Refusing = _ => false;
                listed.Dispose();
            }
            else
            {
                Stash.Dispose();
            }

        }
    }
}
