using Avalonia.Automation;
using Avalonia.Controls;
using ImageStash.Store;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// Import stash…'s offer of a chosen export over a real stash (G36): it says how many items the whole stash holds and the
/// file holds, that the stash and the settings that travel are replaced, and where the replaced files go as G29 qualifies
/// the Recycle Bin, as the Import row says too; Export current stash first… runs Export stash… whole and leaves the offer
/// standing, nothing replaced until Replace, nothing else starting while it writes and never writing over the offered
/// file; a Replace turned away, or pressed under a count the stash no longer holds, leaves the offer standing; and no
/// button of the three invites the click (F10).
/// </summary>
[TestClass]
public sealed class ImportOfferTests
{
    private const string SettingsWords = "The file's settings replace yours: the four shortcuts, Also copy, the recording's maximum length and pointer, Start with Windows and Theme. ";
    private const string Leaving = "Your current pictures, tags and edits leave ImageStash. ";
    private const string RecycleBin = "Replaced files go to the Recycle Bin, which may not keep a large stash.";

    [TestMethod]
    public async Task TheOfferCountsTheWholeStashWhateverTheGridShowsAndTheFilesItems()
    {
        using var listed = await ListedStash.Open();
        var cat = await listed.Stash.Import("cat");
        await listed.Stash.Import("dog");
        await listed.Stash.Import("owl");
        Assert.IsTrue((await listed.Organising.SetFavorite([cat], true)).Succeeded);
        var archive = await ExportedStash.Of(listed.Stash.Outside("export.zip"), null, "a", "b", "c", "d", "e", "f", "g");
        await listed.Listing.SetQuery("cat");
        Assert.HasCount(1, listed.Listing.Items, "the search leaves one in the grid");

        var page = await listed.Offered(archive);

        // G36: "says how many items it holds and that the current stash and those settings will be replaced by it".
        Assert.AreEqual($"Replace your 3 items with the 7 in export.zip? {Leaving}{SettingsWords}{RecycleBin}", page.ImportOffer,
            "the whole stash's count, never the grid's, and the file's");
        page.CancelImport();
        await listed.Listing.ShowScope(Scope.Favorites);
        Assert.HasCount(1, listed.Listing.Items);

        StringAssert.StartsWith((await listed.Offered(archive)).ImportOffer, "Replace your 3 items with the 7 in export.zip? ", "Favorites showing, still the whole stash");
    }

    [TestMethod]
    public void OneItemAndNoItemsReadAsAPersonSaysThemAndThousandsAreGrouped()
    {
        Assert.AreEqual($"Replace your 1,240 items with the 85 in x.zip? {Leaving}{SettingsWords}{RecycleBin}", Words(1240, 85));
        Assert.AreEqual($"Replace your 1 item with the 1 item in x.zip? {Leaving}{SettingsWords}{RecycleBin}", Words(1, 1));
        Assert.AreEqual($"Replace your 1 item with the 2 items in x.zip? {Leaving}{SettingsWords}{RecycleBin}", Words(1, 2));
        Assert.AreEqual($"Replace your empty stash with the 12,000 items in x.zip? {SettingsWords}{RecycleBin}", Words(0, 12000), "nothing leaves an empty stash");
        Assert.AreEqual($"Replace your 2 items with the empty stash in x.zip? {Leaving}{SettingsWords}{RecycleBin}", Words(2, 0));
    }

    [TestMethod]
    public async Task TheOfferAndTheImportRowSayTheSameOfTheRecycleBin()
    {
        using var listed = await ListedStash.Open();
        var row = listed.Page().Rows.Single(each => each.Title == SettingsPage.ImportTitle).Description!;

        // G29: the Recycle Bin "keeps what it is sent only up to the size Windows allows it for that drive".
        StringAssert.EndsWith(row, RecycleBin, "the Import row's line");
        StringAssert.EndsWith(Words(3, 4), RecycleBin, "and the offer, in the same words");
        Assert.DoesNotContain("goes to the Recycle Bin", row, "no unqualified promise left on the row");
    }

    [TestMethod]
    public async Task AnItemAddedAfterThePageOpenedIsCountedAndAReplaceUnderACountNoLongerTrueIsTurnedAwayWithTheOfferSaidAgain()
    {
        using var listed = await ListedStash.Open();
        await listed.Stash.Import("cat");
        var archive = await ExportedStash.Of(listed.Stash.Outside("export.zip"), null, "one", "two");
        listed.Dialogs.OpenFileAnswer = () => Task.FromResult<string?>(archive);
        var page = listed.Page();
        await listed.Stash.Capture("Screenshot 2026-11-24 10.00.00");

        Assert.IsTrue((await page.Import(null!)).Succeeded);
        StringAssert.StartsWith(page.ImportOffer, "Replace your 2 items with the 2 in export.zip? ", "the capture made after the page opened is counted");

        await listed.Stash.Capture("Screenshot 2026-11-24 10.00.05");
        var turnedAway = await page.ConfirmImport();

        Assert.IsTrue(turnedAway.Succeeded, turnedAway.Failure?.Sentence);
        StringAssert.StartsWith(page.ImportOffer, "Replace your 3 items with the 2 in export.zip? ", "the offer said again with the count the stash holds");
        Assert.HasCount(3, await listed.Stash.All(), "nothing replaced under a count the offer did not show");
        Assert.IsNull(listed.Listing.CannotStartReason, "changes may begin again");

        var replaced = await page.ConfirmImport();

        Assert.IsTrue(replaced.Succeeded, replaced.Failure?.Sentence);
        CollectionAssert.AreEquivalent(new[] { "one", "two" }, (await listed.Stash.All()).Select(item => item.Name).ToList(), "Replace under the count shown replaces");
    }

    [TestMethod]
    public async Task ExportFirstRunsExportStashWholeAndLeavesTheOfferStandingNothingStartingMeanwhileAndNothingReplacedUntilReplace()
    {
        using var listed = await ListedStash.Open();
        var cat = await listed.Stash.Import("cat");
        await listed.Stash.Import("dog");
        var archive = await ExportedStash.Of(listed.Stash.Outside("export.zip"), null, "one");
        var page = await listed.Offered(archive);
        var offered = page.ImportOffer;
        var mine = listed.Stash.Outside("mine.zip");
        var picker = new TaskCompletionSource<string?>();
        listed.Dialogs.SaveAnswer = () => picker.Task;
        Assert.IsTrue((await listed.Deletion.Delete([cat])).Succeeded, "a notice whose Undo stands, which the export waits for");

        var exporting = page.Export(null!);

        Assert.IsFalse((await page.ConfirmImport()).Succeeded, "Replace turned away while the picker is up");
        Assert.IsFalse((await listed.Transferring.Export(null!)).Succeeded, "and a second export");
        Assert.AreEqual(offered, page.ImportOffer, "the offer stands");

        picker.SetResult(mine);
        await Handing.Eventually(() => page.ExportLine == "Waiting for the Undo of a notice before exporting your stash…");
        var refused = await page.ConfirmImport();

        // G36: "from the moment one is asked nothing else starts until it ends".
        Assert.AreEqual(StashExport.InProgress, refused.Failure?.Reason, "Replace turned away while the export is asked");
        Assert.AreEqual(StashExport.InProgress, (await listed.Transferring.Export(null!)).Failure?.Reason, "and a second export");
        Assert.AreEqual(offered, page.ImportOffer, "the offer stands, its words as they were");
        Assert.HasCount(1, listed.Dialogs.SavePickers, "one picker only");

        // The Delete's notice, beside the refusals' (G35: each named in a notice too).
        await listed.Turns.Act(() => listed.Notices.Undo(listed.Notices.Shown.Single(notice => notice.Text.StartsWith("Deleted"))));
        listed.Stash.Clock.Now += Notices.Lasting;
        listed.Turns.Act(listed.Notices.Sweep);
        var exported = await exporting.WaitAsync(TimeSpan.FromSeconds(20));

        Assert.IsTrue(exported.Succeeded, exported.Failure?.Sentence);
        Assert.AreEqual(Transferring.ExportedWords(mine), page.ExportLine, "where the file is, said as Export stash… says it");
        Assert.AreEqual(2, StashImport.Summary(mine, TestStash.ShortRetry).Value!.Items, "the current stash written whole");
        CollectionAssert.AreEquivalent(new[] { "cat", "dog" }, (await listed.Stash.All()).Select(item => item.Name).ToList(), "nothing replaced at the export's end (G36: \"once the user confirms\")");
        await Handing.Eventually(() => page.ImportOffer == offered);
        Assert.IsNull(listed.Listing.CannotStartReason, "changes may begin again");

        var replaced = await page.ConfirmImport();

        Assert.IsTrue(replaced.Succeeded, replaced.Failure?.Sentence);
        CollectionAssert.AreEqual(new[] { "one" }, (await listed.Stash.All()).Select(item => item.Name).ToList(), "Replace then replaces");
    }

    [TestMethod]
    public async Task AnExportReplacedAtItsPathAfterTheOfferIsOfferedAgainWithItsNewCountNothingReplaced()
    {
        using var listed = await ListedStash.Open();
        await listed.Stash.Import("cat");
        await listed.Stash.Import("dog");
        var archive = await ExportedStash.Of(listed.Stash.Outside("export.zip"), null, "one");
        var page = await listed.Offered(archive);
        // A sync client puts another export at the same path while the offer stands.
        File.Copy(await ExportedStash.Of(listed.Stash.Outside("other.zip"), null, "a", "b", "c"), archive, overwrite: true);

        var turnedAway = await page.ConfirmImport();

        Assert.IsTrue(turnedAway.Succeeded, turnedAway.Failure?.Sentence);
        StringAssert.StartsWith(page.ImportOffer, "Replace your 2 items with the 3 in export.zip? ", "the file as it now is");
        CollectionAssert.AreEquivalent(new[] { "cat", "dog" }, (await listed.Stash.All()).Select(item => item.Name).ToList(), "nothing replaced under a count the offer did not show");

        Assert.IsTrue((await page.ConfirmImport()).Succeeded);
        CollectionAssert.AreEquivalent(new[] { "a", "b", "c" }, (await listed.Stash.All()).Select(item => item.Name).ToList(), "Replace under the counts shown replaces");
    }

    [TestMethod]
    public async Task AnUndoDuringReplacesWaitIsCountedAndTheOfferSaidAgainNothingReplaced()
    {
        using var listed = await ListedStash.Open();
        var cat = await listed.Stash.Import("cat");
        await listed.Stash.Import("dog");
        var archive = await ExportedStash.Of(listed.Stash.Outside("export.zip"), null, "one");
        Assert.IsTrue((await listed.Deletion.Delete([cat])).Succeeded);
        var page = await listed.Offered(archive);
        StringAssert.StartsWith(page.ImportOffer, "Replace your 1 item with the 1 item in export.zip? ");

        var replacing = page.ConfirmImport();
        await Handing.Eventually(() => page.ImportLine == "Waiting for the Undo of a notice before importing export.zip…");
        await listed.Turns.Act(() => listed.Notices.Undo(listed.Notices.Shown.Single(notice => notice.Text.StartsWith("Deleted"))));
        listed.Stash.Clock.Now += Notices.Lasting;
        listed.Turns.Act(listed.Notices.Sweep);
        var turnedAway = await replacing.WaitAsync(TimeSpan.FromSeconds(20));

        Assert.IsTrue(turnedAway.Succeeded, turnedAway.Failure?.Sentence);
        StringAssert.StartsWith(page.ImportOffer, "Replace your 2 items with the 1 item in export.zip? ", "the item the Undo restored, counted once nothing else could start");
        CollectionAssert.AreEquivalent(new[] { "cat", "dog" }, (await listed.Stash.All()).Select(item => item.Name).ToList(), "nothing replaced");
        Assert.IsNull(listed.Listing.CannotStartReason, "changes may begin again");
    }

    [TestMethod]
    public async Task AfterExportFirstTheOfferCountsWhatTheStashGainedWhileThePickerWasUp()
    {
        using var listed = await ListedStash.Open();
        await listed.Stash.Import("cat");
        var archive = await ExportedStash.Of(listed.Stash.Outside("export.zip"), null, "one", "two");
        var page = await listed.Offered(archive);
        StringAssert.StartsWith(page.ImportOffer, "Replace your 1 item with the 2 items in export.zip? ");
        var mine = listed.Stash.Outside("mine.zip");
        listed.Dialogs.SaveAnswer = async () =>
        {
            await listed.Stash.Capture("Screenshot 2026-11-24 10.00.00");
            return mine;
        };

        var exported = await page.Export(null!);

        Assert.IsTrue(exported.Succeeded, exported.Failure?.Sentence);
        Assert.AreEqual(2, StashImport.Summary(mine, TestStash.ShortRetry).Value!.Items, "the capture exported with the rest");
        await Handing.Eventually(() => page.ImportOffer?.StartsWith("Replace your 2 items with the 2 in export.zip? ") == true);
    }

    [TestMethod]
    public async Task ACancelledExportFirstSaysNothingAndAFailedOneIsNamedAsExportStashIsTheOfferStandingEitherWay()
    {
        using var listed = await ListedStash.Open();
        await listed.Stash.Import("cat");
        var archive = await ExportedStash.Of(listed.Stash.Outside("export.zip"), null, "one");
        var page = await listed.Offered(archive);
        var offered = page.ImportOffer;
        listed.Dialogs.SaveAnswer = () => Task.FromResult<string?>(null);

        Assert.IsTrue((await page.Export(null!)).Succeeded);
        Assert.AreEqual(offered, page.ImportOffer, "a cancelled picker leaves the offer");
        Assert.IsEmpty(listed.Notices.Shown, "and says nothing");

        var held = listed.Stash.Outside("mine.zip");
        File.WriteAllBytes(held, [1, 2, 3]);
        listed.Dialogs.SaveAnswer = () => Task.FromResult<string?>(held);
        Outcome failed;
        using (new FileStream(held, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            failed = await page.Export(null!);
        }

        Assert.AreEqual(StashExport.Action, failed.Failure?.Action, failed.Failure?.Sentence);
        Assert.AreEqual(failed.Failure!.Sentence, page.WarningOf(SettingsPage.ExportTitle), "named on the Export row, as today (G35)");
        CollectionAssert.Contains(listed.Notices.Shown.Select(notice => notice.Text).ToList(), failed.Failure.Sentence, "and in a notice");
        Assert.AreEqual(offered, page.ImportOffer, "the offer stands");

        Assert.IsTrue((await page.ConfirmImport()).Succeeded, "and its Replace still works");
        CollectionAssert.AreEqual(new[] { "one" }, (await listed.Stash.All()).Select(item => item.Name).ToList());
    }

    [TestMethod]
    public async Task AnExportOverTheOfferedFileIsRefusedByNameAndLeavesTheFileAndTheOfferAsTheyWere()
    {
        using var listed = await ListedStash.Open();
        await listed.Stash.Import("cat");
        await listed.Stash.Import("dog");
        var archive = await ExportedStash.Of(listed.Stash.Outside("export.zip"), null, "one");
        var page = await listed.Offered(archive);
        var offered = page.ImportOffer;
        var bytes = File.ReadAllBytes(archive);

        foreach (var named in new[] { archive, archive.ToUpperInvariant() })
        {
            listed.Dialogs.SaveAnswer = () => Task.FromResult<string?>(named);

            var refused = await page.Export(null!);

            Assert.AreEqual(Failure.Refusal(StashExport.Action, Path.GetFileName(named), Transferring.OfferedFileReason).Sentence, refused.Failure?.Sentence, "refused by name, Windows' case ignored");
            CollectionAssert.AreEqual(bytes, File.ReadAllBytes(archive), "the offered file untouched");
            Assert.AreEqual(offered, page.ImportOffer, "the offer stands");
        }

        Assert.IsTrue((await page.ConfirmImport()).Succeeded);
        CollectionAssert.AreEqual(new[] { "one" }, (await listed.Stash.All()).Select(item => item.Name).ToList(), "Replace imports the file the offer described");
    }

    [TestMethod]
    public async Task TheOffersButtonsAreExportFirstReplaceAndCancelNoneOfThemInvitedEachNamedByItsWords()
    {
        using var listed = await ListedStash.Open();
        await listed.Stash.Import("cat");
        var page = await listed.Offered(await ExportedStash.Of(listed.Stash.Outside("export.zip"), null, "one"));

        ControlThread.Run(() =>
        {
            var offer = new ImportOfferPanel(page, null!);

            CollectionAssert.AreEqual(new[] { "Export current stash first…", "Replace", "Cancel" }, offer.Buttons.Select(AutomationProperties.GetName).ToList(), "named for automation by their words");
            CollectionAssert.AreEqual(new[] { "Export current stash first…", "Replace", "Cancel" }, offer.Buttons.Select(button => (string)button.Content!).ToList());
            // F10, and "do not style Import as the invited choice": no accent and no default for Enter.
            Assert.IsFalse(offer.Buttons.Any(button => button.Classes.Contains("accent")), "none carries the accent");
            Assert.IsFalse(offer.Buttons.Any(button => button.IsDefault), "none is what Enter presses");
            Assert.IsTrue(offer.IsVisible);
            Assert.AreEqual(page.ImportOffer, offer.Children.OfType<TextBlock>().Single().Text, "the offer's words above them");

            page.CancelImport();
            offer.Show();

            Assert.IsFalse(offer.IsVisible, "gone with the offer");
        });
    }

    private static string Words(int current, int coming) =>
        new ImportOffer(@"C:\Exports\x.zip", new ExportSummary(coming, StoreSchema.CurrentVersion, "0.0.0.0"), current).Words;
}
