using ImageStash.Seams;
using ImageStash.Store;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// A capture's copy over a real stash with real pictures (G14): the same clipboard content as a copy of the item
/// (G8), its item confirmed by identity, under the one write at a time a copy and a paste hold, so it never lands
/// inside another's write; no use recorded and nothing raised in the window (G10), its failure returned with the item
/// as its subject for the caller to name (G35).
/// </summary>
[TestClass]
public sealed class HandoffCaptureCopyTests
{
    [TestMethod]
    public async Task AStillGoesAsACopyOfItWouldAndRecordsNoUse()
    {
        using var handing = await Handing.Open("transparent.png");
        var item = handing.Items.Single();

        var copied = await handing.Handoff.CopyCapture(null!, item);

        Assert.IsTrue(copied.Succeeded, copied.Failure?.Sentence);
        var content = handing.Clipboard.Written.Single().Content;
        CollectionAssert.AreEqual(new[] { item.FilePath }, content.FilePaths.ToList());
        CollectionAssert.AreEqual(File.ReadAllBytes(item.FilePath), content.Png, "its PNG, the file's own bytes (G8)");
        Assert.AreEqual((8, 6), (content.Bitmap!.Width, content.Bitmap.Height));
        Assert.AreEqual(128, MediaFixtures.Alpha(content.Bitmap, 1, 1), "the bitmap as a copy's keeps the alpha");
        Assert.IsEmpty(handing.Uses(), "a capture is no use (G10)");
        Assert.IsEmpty(handing.Notices.Shown);
        Assert.IsEmpty(handing.Notifications.Shown);
        Assert.IsNull(handing.Handoff.Busy);
    }

    [TestMethod]
    public async Task AnAnimatedItemGoesAsItsFileOnly()
    {
        using var handing = await Handing.Open("animated.gif");

        await handing.Handoff.CopyCapture(null!, handing.Items[0]);

        var content = handing.Clipboard.Written.Single().Content;
        CollectionAssert.AreEqual(new[] { handing.Items[0].FilePath }, content.FilePaths.ToList());
        Assert.IsNull(content.Png);
        Assert.IsNull(content.Bitmap);
    }

    [TestMethod]
    public async Task AHeldClipboardIsReturnedNamedByTheItemAndNothingIsRaised()
    {
        using var handing = await Handing.Open("transparent.png");
        handing.Clipboard.FailNext = RecordingClipboard.Held;

        var copied = await handing.Handoff.CopyCapture(null!, handing.Items[0]);

        Assert.AreEqual(new Failure("Copy", "transparent", HeldClipboard.Reason), copied.Failure, "the item, not the clipboard, is what the user knows (G35)");
        Assert.IsEmpty(handing.Notices.Shown, "the caller names it, above the tray");
        Assert.IsEmpty(handing.Notifications.Shown);
        Assert.IsEmpty(handing.Uses());
    }

    [TestMethod]
    public async Task AnItemNoLongerInTheStashIsRefusedAndNothingIsWritten()
    {
        using var handing = await Handing.Open("transparent.png");
        var item = handing.Items[0];
        await handing.Stash.Store!.Remove([item.Id]);

        var copied = await handing.Handoff.CopyCapture(null!, item);

        Assert.AreEqual(new Failure("Copy", "transparent", ItemFactChange.NotInStash), copied.Failure, "confirmed by identity (G8)");
        Assert.AreEqual(0, handing.Clipboard.Begun);
    }

    [TestMethod]
    public async Task ACaptureCopyDuringACopyWritesWholeAfterItAndOnlyTheCopyRecordsAUse()
    {
        using var handing = await Handing.Open("animated.gif", "transparent.png");
        var (copiedItem, captured) = (handing.Items[0], handing.Items[1]);
        var release = new TaskCompletionSource();
        handing.Clipboard.Hold = release.Task;

        var copying = handing.Copy([copiedItem]);
        await Handing.Eventually(() => handing.Clipboard.Begun == 1);
        var capturing = handing.Handoff.CopyCapture(null!, captured);
        await Task.Delay(100);

        Assert.AreEqual(1, handing.Clipboard.Begun, "the capture's write waits for the copy's, never a gate of its own");
        release.SetResult();
        await Task.WhenAll(copying, capturing);
        CollectionAssert.AreEqual(new[] { copiedItem.FilePath, captured.FilePath }, handing.Clipboard.Written.Select(write => write.Content.FilePaths.Single()).ToList());
        CollectionAssert.AreEqual(new[] { copiedItem.Id }, handing.Uses().Select(use => use.Item).ToList(), "the copy's use alone (G10)");
    }

    [TestMethod]
    public async Task APasteHoldsTheClipboardThroughItsKeystrokeBeforeACaptureCopyWrites()
    {
        using var handing = await Handing.Open("animated.gif", "transparent.png");
        var (handed, captured) = (handing.Items[0], handing.Items[1]);
        var keystroke = new TaskCompletionSource();
        handing.Target.HoldPaste = keystroke.Task;

        var pasting = handing.Paste([handed], RecordingTarget.Relay);
        await Handing.Eventually(() => handing.Steps.Contains("hide"));
        var capturing = handing.Handoff.CopyCapture(null!, captured);
        await Task.Delay(100);

        Assert.AreEqual(1, handing.Clipboard.Begun, "nothing lands on the clipboard before the target pastes it (G8)");
        keystroke.SetResult();
        await Task.WhenAll(pasting, capturing);
        Assert.HasCount(2, handing.Clipboard.Written);
    }
}
