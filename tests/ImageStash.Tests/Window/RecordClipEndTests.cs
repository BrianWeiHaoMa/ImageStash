using System.Runtime.Versioning;
using ImageStash.Files;
using ImageStash.Media;
using ImageStash.Store;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// A recording's end over a real stash, Media's GIF encoder and the file it writes, and a clock the check steps (G15,
/// G16): the clip committed with the facts and the content hash its encoder took as it wrote it, which equal Media's
/// reading of the saved file, without that file being read back before it becomes an item (G12, G19); on the clipboard as
/// soon as its file is in place, before the stash is listed again and the window is back, then the notification (G28,
/// R9, G3); a write refused at the encoder's own file, during a frame or at the clip's closing bytes, leaving no item,
/// named, and nothing of the clip in <c>.work\</c> (G35).
/// </summary>
[TestClass]
public sealed class RecordClipEndTests
{
    private const string Clip = "Recording 2026-10-20 17.30.00";

    [TestMethod]
    public async Task TheClipIsCommittedWithTheFactsItsEncoderWroteAndCopiedBeforeTheStashIsListedAgain()
    {
        using var recording = await ClipRecording.Open();
        await recording.Stash.Import("older");
        await recording.Listing.ShowScope(Scope.All);
        var atTheWrite = "";
        recording.Clipboard.AtWrite = async () => atTheWrite = await CaptureEnds.AtTheWrite(recording);

        await ClipRecording.Record(recording);

        Assert.AreEqual("hide; in place True; listed False; 0 notifications", atTheWrite,
            "the clip committed with its file in place, the clipboard written before All is read again to list it, the window still away (G15, G28, G3)");
        var clip = (await recording.Stash.All()).MaxBy(each => each.Id)!;
        Assert.AreEqual(new MediaFacts(ImageFormat.Gif, 32, 24, true, 4, 280), clip.Facts, "one image per sample, the last lasting until Stop (G15, F3)");
        CaptureEnds.CommittedAsRead(recording.Stash, clip, "the facts and the hash its encoder wrote, as Media reads the saved file");
        Assert.HasCount(2, recording.Listing.Items, "then listed");
        CollectionAssert.AreEqual(new[] { "hide", "restore Flyout" }, recording.WindowSteps, "then the window back");
        Assert.AreEqual($"Recording stashed: “{Clip}”. 0.3 s · On the clipboard · Ctrl+V pastes it.", recording.Notifications.Shown.Single().Text);
        Assert.IsEmpty(recording.Failures);
    }

    [TestMethod]
    public async Task TheClipIsInRecentUnusedByWhenItWasSavedAndTheWindowShowingRecentListsItKeepingTheSelection()
    {
        using var recording = await ClipRecording.Open();
        var older = await recording.Stash.Import("older");
        await recording.Stash.Store!.RecordUses([older], recording.Stash.Clock.Now);
        await recording.Listing.ShowScope(Scope.Recent);
        recording.Stash.Clock.Now += TimeSpan.FromSeconds(1);

        await ClipRecording.Record(recording);

        var clip = (await recording.Stash.All()).MaxBy(each => each.Id)!;
        CollectionAssert.AreEqual(new[] { clip.Id, older.Id }, (await recording.Stash.Store!.Recent()).Value!.Select(each => each.Id).ToList(),
            "G4: an item a recording added is listed while no use of it is recorded, placed by when it was added");
        CollectionAssert.AreEqual(new[] { older.Id, clip.Id }, recording.Listing.Items.Select(each => each.Id).ToList(), "the new recording joins the end of the displayed list");
        CollectionAssert.AreEqual(new[] { older.Id }, recording.Listing.Selection.ToList(), "the selection as it was left (G1)");
        Assert.AreEqual(1L, recording.Uses(), "a capture and its copy are no use (G10)");
    }

    [TestMethod]
    public async Task TheWindowComesBackOnlyOnceTheClipIsCopiedAndListedAndBeforeItsNotification()
    {
        using var recording = await ClipRecording.Open();
        await recording.Listing.ShowScope(Scope.All);
        var atTheRestore = "";
        var hiding = new CaptureHiding(() => CaptureHide.Pinned, _ => atTheRestore = $"listed {recording.Listing.Items.Count}; copied {recording.Clipboard.Written.Count}; {recording.Notifications.Shown.Count} notifications");
        var act = new RecordClip(recording.Listing, recording.Notifications, recording.CaptureNotification, recording.Handoff, recording.Capture, recording.Settings, hiding, null!, _ => { }, TestStash.ShortRetry);

        var acting = act.StartOrStop();
        await recording.RecordFromTheWait();
        recording.StepRecording(210);
        recording.Stepped!.Pass(TimeSpan.FromMilliseconds(70));
        recording.Frame.PressStop();
        await acting.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.AreEqual("listed 1; copied 1; 0 notifications", atTheRestore, "back once the clip is copied and listed, before the notification (G3, G15)");
        Assert.HasCount(1, recording.Notifications.Shown);
    }

    [TestMethod]
    public async Task AClipStoppedBetweenTwoCentisecondsIsCommittedWithTheLengthItsDelaysMake()
    {
        using var recording = await ClipRecording.Open();

        var acting = recording.RecordClip.StartOrStop();
        await recording.RecordFromTheWait();
        recording.StepRecording(140);
        recording.Stepped!.Pass(TimeSpan.FromMilliseconds(65));
        recording.Frame.PressStop();
        await acting.WaitAsync(TimeSpan.FromSeconds(10));

        var clip = (await recording.Stash.All()).Single();
        Assert.AreEqual(new MediaFacts(ImageFormat.Gif, 32, 24, true, 3, 210), clip.Facts, "Stop at 205 ms rounds to 21 cs, halves up: 210 ms, not 205 (F3)");
        CaptureEnds.CommittedAsRead(recording.Stash, clip, "stopped between two centiseconds, as Media reads the saved file");
    }

    [TestMethod]
    public async Task AClipOfOneImageIsCommittedAsTheStillMediaReadsItAs()
    {
        using var recording = await ClipRecording.Open();

        await ClipRecording.Record(recording, untilMs: 0);

        var clip = (await recording.Stash.All()).Single();
        Assert.AreEqual(new MediaFacts(ImageFormat.Gif, 32, 24, false, 1, 0), clip.Facts, "a still, of no length, to Media (F3)");
        CaptureEnds.CommittedAsRead(recording.Stash, clip, "one image, as Media reads the saved file");
    }

    [TestMethod]
    public async Task AClipEndedAtTheCapIsCommittedAsMediaReadsIt()
    {
        using var recording = await ClipRecording.Open();
        recording.Settings.Change(settings => settings with { RecordingMaxSeconds = 1 });

        var acting = recording.RecordClip.StartOrStop();
        await recording.RecordFromTheWait();
        recording.StepRecording(1050);
        await acting.WaitAsync(TimeSpan.FromSeconds(10));

        var clip = (await recording.Stash.All()).Single();
        Assert.AreEqual(new MediaFacts(ImageFormat.Gif, 32, 24, true, 15, 1000), clip.Facts, "samples at 0, 70, … 980, the last lasting until the cap (G15, G32)");
        CaptureEnds.CommittedAsRead(recording.Stash, clip, "ended at the cap, as Media reads the saved file");
    }

    [TestMethod]
    [SupportedOSPlatform("windows")]
    public async Task TheSavedClipIsNotReadBackBeforeItBecomesAnItem()
    {
        using var recording = await ClipRecording.Open();
        var acting = recording.RecordClip.StartOrStop();
        await recording.RecordFromTheWait();
        // Samples at 0 and 70: the first image is written.
        recording.StepRecording(70);
        var path = Path.Combine(recording.Stash.Folder.Work, recording.Stash.WorkFiles().Single());
        StashItem clip;
        using (var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            // The header every read of the file starts with, kept from any other handle until the clip is an item; the
            // encoder writes only past it, and the rename into place is not a read.
            reader.Lock(0, 6);
            recording.StepRecording(140);
            recording.Stepped!.Pass(TimeSpan.FromMilliseconds(70));
            recording.Frame.PressStop();
            await acting.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.AreEqual("", string.Join(" | ", recording.Notifications.Shown.Where(shown => shown.UndoLabel is null).Select(shown => shown.Text)), "nothing failed reading the clip");
            clip = (await recording.Stash.All()).Single();
            Assert.AreEqual(new MediaFacts(ImageFormat.Gif, 32, 24, true, 4, 280), clip.Facts, "committed from what its encoder wrote");
        }

        CaptureEnds.CommittedAsRead(recording.Stash, clip, "the facts and the hash its encoder wrote, as Media reads the file once it may");
    }

    [TestMethod]
    [SupportedOSPlatform("windows")]
    public async Task AWriteRefusedDuringAFrameLeavesNoItemAndIsNamed()
    {
        using var recording = await ClipRecording.Open();
        var acting = recording.RecordClip.StartOrStop();
        await recording.RecordFromTheWait();
        recording.StepRecording(70);
        var path = Path.Combine(recording.Stash.Folder.Work, recording.Stash.WorkFiles().Single());
        using (var holder = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            // Everything past the first image refused, as a disk or a program holding the file part way would refuse it:
            // the second image's write fails after its retries.
            holder.Lock(holder.Length, long.MaxValue - holder.Length);
            recording.StepRecording(70);
            await acting.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.AreEqual($"Record a clip failed: {Clip}, {HeldFileRetry.HeldReason}", recording.Notifications.Shown.Single().Text, "named as the action on the clip (G35)");
            Assert.IsEmpty(await recording.Stash.All(), "no item of a clip written part way");
            Assert.AreEqual(0, recording.Clipboard.Begun, "nothing copied");
        }

        Assert.IsEmpty(recording.Stash.WorkFiles(), "nothing of the clip left in .work\\");
        await recording.Reopen();
        Assert.IsEmpty(await recording.Stash.All());
        Assert.IsEmpty(recording.Stash.WorkFiles());
        Assert.IsEmpty(recording.Stash.RootFiles());
    }

    [TestMethod]
    [SupportedOSPlatform("windows")]
    public async Task AWriteRefusedAtTheClipsClosingByteLeavesNoItemAndIsNamed()
    {
        using var recording = await ClipRecording.Open();
        var acting = recording.RecordClip.StartOrStop();
        await recording.RecordFromTheWait();
        recording.StepRecording(70);
        var path = Path.Combine(recording.Stash.Folder.Work, recording.Stash.WorkFiles().Single());
        var first = new FileInfo(path).Length;
        recording.StepRecording(70);
        // The second image, the second picture drawn over the first; the fourth, written at Stop, is the same image.
        var image = new FileInfo(path).Length - first;
        recording.StepRecording(70);
        var beforeStop = new FileInfo(path).Length;
        using (var holder = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            // Every image may be written; the trailer after the last, the clip's closing byte, is refused.
            holder.Lock(beforeStop + image, long.MaxValue - beforeStop - image);
            recording.Stepped!.Pass(TimeSpan.FromMilliseconds(70));
            recording.Frame.PressStop();
            await acting.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.AreEqual(beforeStop + image, holder.Length, "the last image written whole, its trailer refused");
            Assert.AreEqual($"Record a clip failed: {Clip}, {HeldFileRetry.HeldReason}", recording.Notifications.Shown.Single().Text, "named as the action on the clip (G35)");
            Assert.IsEmpty(await recording.Stash.All(), "no item of a clip without its end");
            Assert.AreEqual(0, recording.Clipboard.Begun, "nothing copied");
        }

        Assert.IsEmpty(recording.Stash.WorkFiles(), "nothing of the clip left in .work\\");
        await recording.Reopen();
        Assert.IsEmpty(await recording.Stash.All());
        Assert.IsEmpty(recording.Stash.WorkFiles());
    }
}
