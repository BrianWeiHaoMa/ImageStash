using System.Security.Cryptography;
using ImageStash.Seams;
using ImageStash.Store;
using ImageStash.Window;

namespace ImageStash.Tests.Support;

/// <summary>
/// A <see cref="TestStash"/> being moved (State: Move): the settings file beside it that a Move writes and start reads,
/// the empty folders chosen for it, every folder and file a Move leaves with its hash, a second name for a folder, and
/// progress told on the store's thread at the moment it is reported.
/// </summary>
public static class MovingStash
{
    /// <summary>
    /// A keeper over the settings folder beside the stash, read by start's own Load: the file a Move writes. A write it
    /// refuses is added to <paramref name="failures"/>.
    /// </summary>
    public static SettingsKeeper Keeper(TestStash stash, List<Failure> failures)
    {
        var folder = stash.Outside("settings");
        return new SettingsKeeper(folder, Settings.Load(folder, stash.Retry), stash.Retry, TimeSpan.Zero, failures.Add);
    }

    /// <summary>
    /// Move… over a stash that is still opening, as a Settings page is made for the checks of its other rows: asked, it
    /// refuses before its picker; nothing it holds is ever reached.
    /// </summary>
    public static Moving NeverOpened(SettingsKeeper keeper, IDialogsAndExplorer dialogs)
    {
        var listing = new Listing(_ => { });
        var notices = new Notices(TimeProvider.System);
        var handoff = new Handoff(listing, notices, notices, new RecordingClipboard(TimeProvider.System), new RecordingTarget(), TimeProvider.System, TestStash.ShortRetry, TestStash.ShortRetry);
        return new Moving(listing, notices, keeper, dialogs, handoff, _ => throw new InvalidOperationException("No stash opens here."), () => Task.CompletedTask);
    }

    /// <summary>
    /// Export stash… and Import stash… over a stash that is still opening, beside <paramref name="moving"/>, as a Settings
    /// page is made for the checks of its other rows: asked, each refuses before its picker; nothing it holds is reached.
    /// </summary>
    public static Transferring NeverOpenedTransfers(SettingsKeeper keeper, IDialogsAndExplorer dialogs, Moving moving)
    {
        var listing = new Listing(_ => { });
        var notices = new Notices(TimeProvider.System);
        var handoff = new Handoff(listing, notices, notices, new RecordingClipboard(TimeProvider.System), new RecordingTarget(), TimeProvider.System, TestStash.ShortRetry, TestStash.ShortRetry);
        var editing = new Editing(listing, notices, handoff, ImageStash.Media.FinishedPicture.Render, new ImageStash.Editor.ColorChoices(), TestStash.ShortRetry);
        var openWith = new OpenWith(listing, notices, editing, dialogs, TimeProvider.System, action => action());
        return new Transferring(listing, notices, keeper, dialogs, new TestRecycleBin(Path.GetTempPath()), handoff, openWith, moving,
            _ => throw new InvalidOperationException("No stash is imported here."), TimeProvider.System, TestStash.ShortRetry);
    }

    public static string SettingsPath(TestStash stash) => Path.Combine(stash.Outside("settings"), Settings.FileName);

    public static Settings Saved(TestStash stash) => Settings.Load(stash.Outside("settings"), TestStash.ShortRetry).Settings;

    public static string Chosen(TestStash stash, string name = "new") => Directory.CreateDirectory(stash.Outside(name)).FullName;

    // A second name for a folder, as a junction gives one without administrator rights; disposing it deletes the
    // junction alone, never what it reaches.
    public static async Task<IDisposable> Junction(string link, string target)
    {
        using var mklink = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"") { CreateNoWindow = true, UseShellExecute = false })!;
        await mklink.WaitForExitAsync();
        Assert.IsTrue(Directory.Exists(link), "the junction was not made");
        return new JunctionLink(link);
    }

    // Every folder (ending in a separator) and every file with its hash, by its path inside the folder.
    public static List<string> Tree(string folder) =>
        !Directory.Exists(folder)
            ? []
            : [
                .. Directory.EnumerateDirectories(folder, "*", SearchOption.AllDirectories).Select(d => Path.GetRelativePath(folder, d) + Path.DirectorySeparatorChar),
                .. Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(folder, f) + " " + Convert.ToHexString(SHA256.HashData(Shared(f)))),
            ];

    // Read beside the store's own connection, which holds the store file open for writing.
    private static byte[] Shared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        return bytes.ToArray();
    }

    public sealed class JunctionLink(string link) : IDisposable
    {
        public void Dispose() => Directory.Delete(link);
    }

    // Told synchronously on the store's thread, as the Move reports.
    public sealed class Reported(Action<MoveProgress> report) : IProgress<MoveProgress>
    {
        public void Report(MoveProgress value) => report(value);
    }
}
