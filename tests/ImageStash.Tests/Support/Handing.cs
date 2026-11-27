using ImageStash.Seams;
using ImageStash.Store;
using ImageStash.Window;

namespace ImageStash.Tests.Support;

/// <summary>The handoff over an open test stash holding the given fixtures, its notices and the clipboard on the stash's clock.</summary>
public sealed class Handing : IDisposable
{
    private Handing(TestStash stash, TimeProvider? handoffClock)
    {
        Stash = stash;
        Notices = new Notices(stash.Clock);
        Listing = new Listing(failure =>
        {
            if (!LoadsMayFail)
            {
                Assert.Fail(failure.Sentence);
            }

            LoadFailures.Add(failure);
        });
        Import = new Import(Listing, Notices, TestStash.ShortRetry);
        Clipboard = new RecordingClipboard(stash.Clock);
        Notifications = new Notices(stash.Clock);
        Stacks = new NotificationStacks(Notifications);
        Target = new RecordingTarget(Steps);
        Handoff = new Handoff(Listing, Notices, Stacks, Clipboard, Target, handoffClock ?? stash.Clock, TestStash.ShortRetry, TestStash.ShortRetry);
    }

    public TestStash Stash { get; }

    public Notices Notices { get; }

    public Listing Listing { get; }

    /// <summary>Set by a check that holds the store through a paste, whose start on Recent then cannot read it: the listing's failures are kept rather than failing the check.</summary>
    public bool LoadsMayFail { get; set; }

    /// <summary>What the listing named while <see cref="LoadsMayFail"/> was set.</summary>
    public List<Failure> LoadFailures { get; } = [];

    public Import Import { get; }

    public RecordingClipboard Clipboard { get; }

    /// <summary>The notifications above the tray the handoff raises.</summary>
    public Notices Notifications { get; }

    /// <summary>Which stack a notification stands in: <see cref="Notifications"/> above the tray, or another display's own.</summary>
    public NotificationStacks Stacks { get; }

    public RecordingTarget Target { get; }

    /// <summary>The paste's steps in order: the target's confirmation and paste, the hide, and what a check adds.</summary>
    public List<string> Steps { get; } = [];

    public Handoff Handoff { get; }

    public IReadOnlyList<StashItem> Items { get; private set; } = [];

    /// <summary>Imports the fixtures in one drop, then clears the import's notice, so each check sees only its own.</summary>
    public static Task<Handing> Open(params string[] fixtures) => Open(null, fixtures);

    /// <summary>As <see cref="Open(string[])"/>, the handoff's waits and uses on <paramref name="handoffClock"/>, as a check that steps through the paste's bound needs.</summary>
    public static async Task<Handing> Open(TimeProvider? handoffClock, params string[] fixtures)
    {
        var handing = new Handing(new TestStash(), handoffClock);
        await handing.Stash.OpenCleanly();
        await handing.Listing.StashOpened(handing.Stash.Store!);
        if (fixtures.Length > 0)
        {
            var results = await handing.Import.Drop(new DropContents(fixtures.Select(MediaFixtures.Path).Select(File).ToList(), false, false, []));
            Assert.IsTrue(results.All(file => file.Result == ImportResultKind.Added), $"{Import.NoticeWords(results)}");
            handing.Items = results.Select(file => file.Item!).ToList();
        }

        foreach (var notice in handing.Notices.Shown.ToList())
        {
            handing.Notices.Close(notice);
        }

        return handing;
    }

    public static DroppedItem File(string path) => new(System.IO.Path.GetFileName(path), path, null, IsFolder: false);

    /// <summary>The window passes itself as the clipboard's owner; the recording seam has no use for one.</summary>
    public Task<Outcome> Copy(IReadOnlyList<StashItem> items) => Handoff.Copy(null!, items);

    /// <summary>Whether the paste's hide leaves the flyout hidden, as the window answers it: so, unless a check says the window stayed (pinned, or a dialog or a drag of its own up, G2).</summary>
    public bool PasteHidesFlyout { get; set; } = true;

    /// <summary>The keys the window holds down, as its key-downs and key-ups tell them: a held Enter, which the paste waits to be let go.</summary>
    public PressOnce Keys { get; } = new();

    /// <summary>How many times the window has been shown, as the window counts them: a check summoning it during a paste adds one.</summary>
    public int TimesShown { get; set; }

    /// <summary>The paste as the window makes it, its hide recorded as a step.</summary>
    public Task<Outcome> Paste(IReadOnlyList<StashItem> items, PasteTarget target) => Handoff.Paste(null!, items, target, Hide);

    /// <summary>The window's part in a paste, its hide recorded as a step, answering <see cref="PasteHidesFlyout"/>, with <see cref="Keys"/> and <see cref="TimesShown"/>.</summary>
    public PasteHide Hide => new(() =>
    {
        Steps.Add("hide");
        return PasteHidesFlyout;
    }, Keys)
    {
        TimesShown = () => TimesShown,
    };

    /// <summary>The seam's drag as the window starts it; the recording seam has no use for the window or the press.</summary>
    public Task<Outcome<bool>> Drag(IReadOnlyList<string> paths) => Clipboard.StartDrag(null!, null!, paths);

    /// <summary>Every use row, in the order written; closes the store to read its file.</summary>
    public List<(long Item, long At)> Uses()
    {
        Stash.Close();
        return Stash.SqlQuery("SELECT item_id, used_at FROM uses ORDER BY rowid", row => (row.GetInt64(0), row.GetInt64(1)));
    }

    /// <summary>Waits up to five seconds, or <paramref name="within"/>, for <paramref name="condition"/>, failing the check if it never comes true.</summary>
    public static async Task Eventually(Func<bool> condition, TimeSpan? within = null)
    {
        var deadline = DateTime.UtcNow + (within ?? TimeSpan.FromSeconds(5));
        while (!condition())
        {
            Assert.IsLessThan(deadline, DateTime.UtcNow, "the condition did not come true");
            await Task.Delay(10);
        }
    }

    public void Dispose() => Stash.Dispose();
}
