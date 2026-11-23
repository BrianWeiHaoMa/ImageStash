using ImageStash.Seams;
using ImageStash.Store;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// The paste moment across the window's parts, wired as the window wires them over a real store: two typed letters
/// select the reaction whose word they start, before newer items holding them mid-word (G4, G5); Enter pastes it once into the
/// target and records one use (G8, G10); the hidden flyout's next summon starts on Recent with no search and that item
/// selected alone (G1); and the footer counts a multi-selection beside the target's app (G8).
/// </summary>
[TestClass]
public sealed class PasteMomentTests
{
    [TestMethod]
    public async Task TwoLettersSelectTheReactionWhoseWordTheyStartEnterPastesItOnceTheNextSummonStartsOnRecentAndTheFooterCountsSeveral()
    {
        using var listed = await ListedStash.Open();
        var today = listed.Stash.Clock.Now;
        listed.Stash.Clock.Now = today.AddDays(-200);
        var reallyTired = await listed.Stash.ImportFixture("really tired", "still.png");
        listed.Stash.Clock.Now = today.AddDays(-1);
        Assert.IsTrue((await listed.Stash.Store!.RecordUses([reallyTired], listed.Stash.Clock.Now)).Succeeded);
        listed.Stash.Clock.Now = today.AddHours(-2);
        await listed.Stash.Import("Screenshot 2026-11-22 10.30.00");
        await listed.Stash.Import("bore");
        listed.Stash.Clock.Now = today;
        await listed.Listing.ShowScope(Scope.All);
        listed.PasteTarget.FromPress(new ForegroundReading(false, RecordingTarget.Relay));
        var usesBefore = listed.Uses();

        await listed.Listing.SetQuery("re");
        var selectedByTyping = listed.Listing.SelectedItems.Select(item => item.Name).ToList();
        var hides = 0;
        // Enter as the window hands over: the selection once what was typed has arrived, into the target the footer names.
        await listed.Listing.Settle();
        var pasted = await listed.Handoff.HandOver(null!, listed.Listing.SelectedItems, listed.PasteTarget.Current, new PasteHide(() =>
        {
            hides++;
            return true;
        }, listed.Keys));
        await listed.Summon();

        CollectionAssert.AreEqual(new[] { "really tired" }, selectedByTyping, "the word-start match, though the others are newer (G4, G5)");
        Assert.IsTrue(pasted.Succeeded, pasted.Failure?.Sentence);
        Assert.HasCount(1, listed.Target.Pasted, "one paste into the target (G8)");
        Assert.AreEqual(RecordingTarget.Relay, listed.Target.Pasted.Single().Target);
        CollectionAssert.AreEqual(new[] { reallyTired.FilePath }, listed.Clipboard.Written.Single().Content.FilePaths.ToList());
        Assert.AreEqual(usesBefore + 1, listed.Uses(), "one use recorded (G10)");
        Assert.AreEqual(1, hides, "the flyout is told to hide (G8)");
        Assert.AreEqual(Scope.Recent, listed.Listing.Scope, "the next summon starts on Recent (G1)");
        Assert.AreEqual("", listed.Listing.Query, "with no search");
        CollectionAssert.AreEqual(new[] { reallyTired.Id }, listed.Listing.SelectedItems.Select(item => item.Id).ToList(), "the item just pasted, selected alone");
        Assert.AreEqual(reallyTired.Id, listed.Listing.Items[0].Id, "and first");

        await listed.Listing.ShowScope(Scope.All);
        listed.Listing.SelectAll();

        Assert.AreEqual("Enter · Paste 3 items into Relay", listed.FooterHints.Texts[0], "the footer counts the selection beside the target's app (G8)");
    }
}
