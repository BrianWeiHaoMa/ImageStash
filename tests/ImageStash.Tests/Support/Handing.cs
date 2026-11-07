using ImageStash.Seams;
using ImageStash.Store;
using ImageStash.Window;

namespace ImageStash.Tests.Support;

/// <summary>The handoff over an open test stash holding the given fixtures, its notices and the clipboard on the stash's clock.</summary>
public sealed class Handing : IDisposable
{
    private Handing(TestStash stash)
    {
        Stash = stash;
        Notices = new Notices(stash.Clock);
        Listing = new Listing(failure => Assert.Fail(failure.Sentence));
        Import = new Import(Listing, Notices, TestStash.ShortRetry);
        Clipboard = new RecordingClipboard(stash.Clock);
        Notifications = new Notices(stash.Clock);
        Target = new RecordingTarget(Steps);
        Handoff = new Handoff(Listing, Notices, Notifications, Clipboard, Target, stash.Clock, TestStash.ShortRetry, TestStash.ShortRetry);
    }

    public TestStash Stash { get; }

    public Notices Notices { get; }

    public Listing Listing { get; }

    public Import Import { get; }

    public RecordingClipboard Clipboard { get; }

    /// <summary>The notifications above the tray the handoff raises.</summary>
    public Notices Notifications { get; }

    public RecordingTarget Target { get; }

    /// <summary>The paste's steps in order: the target's confirmation and paste, the hide, and what a check adds.</summary>
    public List<string> Steps { get; } = [];

    public Handoff Handoff { get; }

    public IReadOnlyList<StashItem> Items { get; private set; } = [];

    /// <summary>Imports the fixtures in one drop, then clears the import's notice, so each check sees only its own.</summary>
    public static async Task<Handing> Open(params string[] fixtures)
    {
        var handing = new Handing(new TestStash());
        await handing.Stash.OpenCleanly();
        await handing.Listing.StashOpened(handing.Stash.Store!);
        if (fixtures.Length > 0)
        {
            var results = await handing.Import.Drop(new DropContents(fixtures.Select(MediaFixtures.Path).Select(File).ToList(), false, false, []));
            Assert.IsTrue(results.All(file => file.Result == ImportResultKind.Added), Import.NoticeText(results));
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

    /// <summary>The paste as the window makes it, its hide recorded as a step.</summary>
    public Task<Outcome> Paste(IReadOnlyList<StashItem> items, PasteTarget target) => Handoff.Paste(null!, items, target, () => Steps.Add("hide"));

    /// <summary>The seam's drag as the window starts it; the recording seam has no use for the window or the press.</summary>
    public Task<Outcome<bool>> Drag(IReadOnlyList<string> paths) => Clipboard.StartDrag(null!, null!, paths);

    /// <summary>Every use row, in the order written; closes the store to read its file.</summary>
    public List<(long Item, long At)> Uses()
    {
        Stash.Close();
        return Stash.SqlQuery("SELECT item_id, used_at FROM uses ORDER BY rowid", row => (row.GetInt64(0), row.GetInt64(1)));
    }

    /// <summary>Waits up to five seconds for <paramref name="condition"/>, failing the check if it never comes true.</summary>
    public static async Task Eventually(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            Assert.IsLessThan(deadline, DateTime.UtcNow, "the condition did not come true");
            await Task.Delay(10);
        }
    }

    public void Dispose() => Stash.Dispose();
}
