using ImageStash.Media;
using ImageStash.Store;

namespace ImageStash.Tests.Support;

/// <summary>
/// A capture's end as the checks read it: what it has done at the moment its clipboard write lands, and whether the item
/// it committed carries the facts and the content hash Media reads from its saved file.
/// </summary>
public static class CaptureEnds
{
    /// <summary>
    /// Read inside the clipboard's write (<see cref="RecordingClipboard.AtWrite"/>), for the newest item: the window's
    /// steps so far; whether the store answers that item by its identity with no step pending, and the one file written
    /// is its file, in place in the stash folder's root; whether the listing has a load in flight or already lists the
    /// item; and how many notifications show. The listing's scope must be one that lists the item once read.
    /// </summary>
    public static async Task<string> AtTheWrite(ListedStash listed)
    {
        // Before any wait, so nothing the check does starts a load first.
        var settled = listed.Listing.Settle().IsCompleted;
        var shown = listed.Listing.Items.Select(each => each.Id).ToList();
        var steps = string.Join(", ", listed.WindowSteps);
        var notifications = listed.Notifications.Shown.Count;
        var item = (await listed.Stash.All()).MaxBy(each => each.Id)!;
        using var confirmed = await listed.Stash.Store!.Confirm([item], "Check the capture");
        var answer = confirmed.Items.Single();
        var written = listed.Clipboard.Written[^1].Content.FilePaths;
        var inPlace = answer is { Succeeded: true, Value.StepsPendingWhenRead: false }
            && written.SequenceEqual([item.FilePath])
            && File.Exists(item.FilePath)
            && Path.GetDirectoryName(item.FilePath) == Path.TrimEndingDirectorySeparator(listed.Stash.Folder.Root);
        return $"{steps}; in place {inPlace}; listed {!settled || shown.Contains(item.Id)}; {notifications} notifications";
    }

    /// <summary>The facts and the content hash the store holds for <paramref name="item"/> equal what Media reads from its saved file (G12, G19).</summary>
    public static void CommittedAsRead(TestStash stash, StashItem item, string because)
    {
        var read = MediaDecoder.ReadFacts(item.FilePath, item.Name, TestStash.ShortRetry);
        Assert.IsTrue(read.Succeeded, read.Failure?.ToString());
        Assert.AreEqual(read.Value!.Facts, item.Facts, because);
        Assert.AreEqual(Convert.ToHexString(read.Value.Sha256), OpenedWith.OriginalHash(stash, item.Id), because);
    }
}
