using ImageStash.Store;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// The preview's confirmation of a copy of its item, over a real handoff (G10, G19): the button that names the copy reads
/// "Copied ✓" for a second and a half once the write has returned, the Copy button while there is a paste target and the
/// handoff button while there is none; nothing for a copy of another item, a paste, or a closed preview.
/// </summary>
[TestClass]
public sealed class PreviewCopyConfirmationTests
{
    [TestMethod]
    public async Task ACopyOfThePreviewsItemTurnsTheButtonThatNamesTheCopyForASecondAndAHalf()
    {
        using var handing = await Handing.Open("transparent.png");
        var clock = new SetClock();
        var item = handing.Items[0];
        var confirmation = new PreviewCopyConfirmation(handing.Handoff, () => item, clock);
        var changes = 0;
        confirmation.Changed += () => changes++;

        var copied = await handing.Copy([item]);
        var withTarget = (confirmation.HandoffWords(RecordingTarget.Relay), confirmation.CopyWords(RecordingTarget.Relay));
        var withNone = confirmation.HandoffWords(null);
        clock.Now += TimeSpan.FromSeconds(1.4);
        confirmation.Sweep();
        var during = confirmation.CopyWords(RecordingTarget.Relay);
        clock.Now += TimeSpan.FromSeconds(0.1);
        confirmation.Sweep();

        Assert.IsTrue(copied.Succeeded, copied.Failure?.Sentence);
        Assert.AreEqual(("Paste into Relay", "Copied ✓"), withTarget, "with a target, the Copy button (G19)");
        Assert.AreEqual("Copied ✓", withNone, "with none, the handoff button, which is the copy");
        Assert.AreEqual("Copied ✓", during);
        Assert.AreEqual(("Paste into Relay", "Copy"), (confirmation.HandoffWords(RecordingTarget.Relay), confirmation.CopyWords(RecordingTarget.Relay)), "then as before");
        Assert.AreEqual("Copy", confirmation.HandoffWords(null));
        Assert.AreEqual(2, changes, "shown, then gone: the buttons are named again each time");
    }

    [TestMethod]
    public async Task ACopyOfAnotherItemAPasteOrAClosedPreviewConfirmsNothingAndAMoveTakesTheWordsAway()
    {
        using var handing = await Handing.Open("transparent.png", "photo.jpg");
        var (shown, other) = (handing.Items[0], handing.Items[1]);
        StashItem? previewed = shown;
        var confirmation = new PreviewCopyConfirmation(handing.Handoff, () => previewed, new SetClock());

        await handing.Copy([other]);
        var afterAnother = confirmation.HandoffWords(null);
        await handing.Copy([shown, other]);
        var afterSeveral = confirmation.HandoffWords(null);
        await handing.Paste([shown], RecordingTarget.Relay);
        var afterPaste = confirmation.CopyWords(RecordingTarget.Relay);
        previewed = null;
        await handing.Copy([shown]);
        var whileClosed = confirmation.HandoffWords(null);
        previewed = shown;
        await handing.Copy([shown]);
        var confirmed = confirmation.HandoffWords(null);
        confirmation.Withdraw();

        Assert.AreEqual("Copy", afterAnother, "another item's copy is not this picture's");
        Assert.AreEqual("Copy", afterSeveral, "nor is a copy of several");
        Assert.AreEqual("Copy", afterPaste, "a paste never says it arrived (G10)");
        Assert.AreEqual("Copy", whileClosed);
        Assert.AreEqual("Copied ✓", confirmed);
        Assert.AreEqual("Copy", confirmation.HandoffWords(null), "moved to another item or closed, the words go at once");
    }
}
