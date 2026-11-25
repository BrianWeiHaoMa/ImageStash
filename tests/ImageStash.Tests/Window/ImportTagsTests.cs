using ImageStash.Media;
using ImageStash.Seams;
using ImageStash.Store;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// The lit chips' tags on an import over a real stash (G11, G12): every picture of a drop, of Import files… and of a paste
/// gets them, the item a file already was in the stash as included; the notice names the tags each item gained as the
/// store committed them, and a refused write as that file's failure (R9, G35); its Undo, offered whenever the batch added
/// an item or gave a tag, takes back both, a tag no item carries then gone (G17); the picker's title names the tags.
/// </summary>
[TestClass]
public sealed class ImportTagsTests
{
    [TestMethod]
    public async Task ADropWithAChipLitTagsTheNewPictureAndTheOneAlreadyInTheStashSaysSoAndItsUndoTakesBothBack()
    {
        using var importing = await ListedStash.Open();
        var old = await importing.Stash.ImportFixture("old", "still.png");
        var catted = await TaggedItem(importing, "catted", "cat");
        await importing.Listing.SetQuery("cat");

        var results = await importing.Import.Drop(ListedStash.Contents([MediaFixtures.Path("photo.jpg"), MediaFixtures.Path("still.png")]));

        CollectionAssert.AreEqual(new[] { ImportResultKind.Added, ImportResultKind.AlreadyInStash }, results.Select(file => file.Result).ToList());
        Assert.AreEqual(old.Id, results[1].Item!.Id, "the duplicate is not added again (G12)");
        CollectionAssert.AreEqual(new[] { "cat" }, (await TagsOf(importing, results[0].Item!)).ToList(), "the new item gets the lit tag");
        CollectionAssert.AreEqual(new[] { "cat" }, (await TagsOf(importing, old)).ToList(), "and so does the item the stash already held (G11)");
        var notice = importing.Notices.Shown.Single();
        Assert.AreEqual(
            ("Added 1, tagged #cat · 1 already in your stash", "still.png was already in your stash as “old”. This import gave it #cat. Added “photo”."),
            (notice.Heading, notice.Text));
        Assert.AreEqual("Undo", notice.UndoLabel);
        Assert.IsFalse(notice.NamesAProblem, "tags given name no problem, so the notice keeps its time (G16)");

        await importing.Notices.Undo(notice);

        Assert.IsFalse((await importing.Stash.All()).Any(item => item.Id == results[0].Item!.Id), "the added item is gone");
        Assert.IsEmpty(await TagsOf(importing, old), "and the tag it gave the item already in the stash is off it (G12)");
        CollectionAssert.AreEqual(new[] { "cat" }, (await TagsOf(importing, catted)).ToList(), "an item outside the batch keeps its tag");
        Assert.AreEqual(new TagCount("cat", 1), importing.Listing.Tags.Single(), "the chips count what the store holds");
        Assert.IsEmpty(importing.Failures);
    }

    [TestMethod]
    public async Task ADuplicateIsSaidToGainOnlyTheTagsItLackedAndUndoLeavesTheOneItCarried()
    {
        using var importing = await ListedStash.Open();
        var old = await importing.Stash.ImportFixture("old", "still.png");
        importing.Stash.Tag(old.Id, "dog");
        await TaggedItem(importing, "catted", "cat");
        await importing.Listing.SetQuery("dog cat");
        CollectionAssert.AreEqual(new[] { "dog", "cat" }, importing.Listing.LitTags.ToList(), "two chips lit");

        var again = (await importing.Import.Drop(ListedStash.Contents([MediaFixtures.Path("still.png")]))).Single();

        CollectionAssert.AreEqual(new[] { "cat" }, again.GainedTags.ToList(), "the tag it lacked, as committed");
        CollectionAssert.AreEquivalent(new[] { "dog", "cat" }, (await TagsOf(importing, old)).ToList());
        var notice = importing.Notices.Shown.Single();
        Assert.AreEqual(("Nothing was added · 1 already in your stash", "still.png was already in your stash as “old”. This import gave it #cat."), (notice.Heading, notice.Text),
            "the tag it already carried is not said to be given");

        await importing.Notices.Undo(notice);

        CollectionAssert.AreEqual(new[] { "dog" }, (await TagsOf(importing, old)).ToList(), "only the tag the batch gave comes off");
        Assert.HasCount(2, await importing.Stash.All(), "nothing was removed");
    }

    [TestMethod]
    public async Task ADropOfOnlyPicturesAlreadyInTheStashOffersUndoWhileItGaveATagAndNoneOtherwise()
    {
        using var importing = await ListedStash.Open();
        var old = await importing.Stash.ImportFixture("old", "still.png");
        var carrying = await importing.Stash.ImportFixture("carrying", "photo.jpg");
        importing.Stash.Tag(carrying.Id, "cat");

        await importing.Listing.SetQuery("cat");
        await importing.Import.Drop(ListedStash.Contents([MediaFixtures.Path("still.png"), MediaFixtures.Path("photo.jpg")]));

        var tagged = importing.Notices.Shown.Single();
        Assert.AreEqual(("Nothing was added · 2 already in your stash", "still.png was already in your stash as “old”. This import gave it #cat. photo.jpg was already in your stash as “carrying”."),
            (tagged.Heading, tagged.Text));
        Assert.AreEqual("Undo", tagged.UndoLabel, "the batch gave a tag, so its Undo is offered (G12)");
        await importing.Notices.Undo(tagged);
        Assert.IsEmpty(await TagsOf(importing, old));
        CollectionAssert.AreEqual(new[] { "cat" }, (await TagsOf(importing, carrying)).ToList());

        await importing.Import.Drop(ListedStash.Contents([MediaFixtures.Path("still.png")]));

        var untagged = importing.Notices.Shown[^1];
        Assert.AreEqual(("Nothing was added · 1 already in your stash", "still.png was already in your stash as “old”."), (untagged.Heading, untagged.Text));
        Assert.IsNull(untagged.UndoLabel, "no chip lit: the batch changed nothing, so it offers no Undo");
    }

    [TestMethod]
    public async Task CtrlOAndAPasteOfFilesTagThePictureAlreadyInTheStashAsADropDoesAndThePickerSaysSo()
    {
        using var importing = await ListedStash.Open();
        var still = await importing.Stash.ImportFixture("still", "still.png");
        var photo = await importing.Stash.ImportFixture("photo", "photo.jpg");
        await TaggedItem(importing, "catted", "cat");
        await importing.Listing.SetQuery("cat");
        var titles = new List<string>();

        await importing.Import.Pick(title =>
        {
            titles.Add(title);
            return Task.FromResult<IReadOnlyList<string>?>([MediaFixtures.Path("still.png")]);
        }, _ => { });

        CollectionAssert.AreEqual(new[] { "Import files (tagged #cat)" }, titles, "the picker's title names the tag the files will get");
        CollectionAssert.AreEqual(new[] { "cat" }, (await TagsOf(importing, still)).ToList(), "Ctrl+O tags the item already in the stash (G11)");
        Assert.AreEqual("still.png was already in your stash as “still”. This import gave it #cat.", importing.Notices.Shown[^1].Text);

        await importing.Listing.SetQuery("cat");
        importing.Clipboard.Reading = Outcome<ClipboardReading>.Of(new ClipboardReading([ListedStash.File(MediaFixtures.Path("photo.jpg"))], null, null, false));
        await importing.Paste.Paste();

        CollectionAssert.AreEqual(new[] { "cat" }, (await TagsOf(importing, photo)).ToList(), "a pasted file too (G13)");
        Assert.AreEqual(("Nothing was added · 1 already in your stash", "photo.jpg was already in your stash as “photo”. This import gave it #cat."),
            (importing.Notices.Shown[^1].Heading, importing.Notices.Shown[^1].Text));

        await importing.Import.Pick(title =>
        {
            titles.Add(title);
            return Task.FromResult<IReadOnlyList<string>?>(null);
        }, _ => { });
        Assert.AreEqual("Import files", titles[^1], "no chip lit, the plain title");
        Assert.IsEmpty(importing.Failures);
    }

    [TestMethod]
    public async Task APastedPictureEqualToAnItemIsANewItemCarryingTheLitTagAndItsNoticeSaysSoBeforeTheStillSentence()
    {
        using var importing = await ListedStash.Open();
        var old = await importing.Stash.ImportFixture("old", "still.png");
        await TaggedItem(importing, "catted", "cat");
        await importing.Listing.SetQuery("cat");
        importing.Clipboard.Reading = Outcome<ClipboardReading>.Of(new ClipboardReading([], MediaFixtures.Bytes("still.png"), null, false));

        await importing.Paste.Paste();

        var pasted = (await importing.Stash.All()).Single(item => item.Id != old.Id && item.Name != "catted");
        CollectionAssert.AreEqual(new[] { "cat" }, pasted.Tags.ToList(), "a new item, saved though its bytes are an item's, with the lit tag (G13)");
        Assert.IsEmpty(await TagsOf(importing, old), "the item with the same bytes is not the paste's");
        var notice = importing.Notices.Shown.Single();
        Assert.AreEqual(("Added 1 to your stash, tagged #cat", $"Added “{pasted.Name}”. {Import.StillOnly}"), (notice.Heading, notice.Text));
    }

    [TestMethod]
    public async Task ARefusedTagWriteIsTheFilesFailureTheFileStillAlreadyInTheStashAndNoTagClaimed()
    {
        using var importing = await ListedStash.Open();
        var old = await importing.Stash.ImportFixture("old", "still.png");
        await TaggedItem(importing, "catted", "cat");
        await importing.Listing.SetQuery("cat");
        // The store refuses the write of the tag alone (E3: a refused write), after the duplicate was found.
        importing.Stash.SqlWhileOpen("CREATE TRIGGER refuse_cat BEFORE INSERT ON tags WHEN NEW.tag = 'cat' BEGIN SELECT RAISE(ABORT, 'refused'); END");

        var again = (await importing.Import.Drop(ListedStash.Contents([MediaFixtures.Path("still.png")]))).Single();

        Assert.AreEqual((ImportResultKind.AlreadyInStash, old.Id), (again.Result, again.Item!.Id), "still reported as already in the stash (G12)");
        Assert.IsEmpty(again.GainedTags);
        Assert.IsEmpty(await TagsOf(importing, old));
        var notice = importing.Notices.Shown.Single();
        Assert.AreEqual("Nothing was added · 1 already in your stash", notice.Heading);
        StringAssert.StartsWith(notice.Text, "still.png was already in your stash as “old”. still.png: Add a tag failed: stash.db, ");
        Assert.DoesNotContain("This import gave it", notice.Text, "no tag the store refused is claimed (R9)");
        Assert.IsTrue(notice.NamesAProblem, "the failure is named (G35)");
        Assert.IsNull(notice.UndoLabel, "the batch changed nothing to take back");
    }

    [TestMethod]
    public async Task ADuplicateWhoseItemHasNoNameIsNamedByItsFile()
    {
        using var importing = await ListedStash.Open();
        var nameless = await importing.Stash.ImportFixture("", "still.png");

        await importing.Import.Drop(ListedStash.Contents([MediaFixtures.Path("still.png")]));

        Assert.AreEqual($"still.png was already in your stash as {nameless.FileName}.", importing.Notices.Shown.Single().Text, "as other notices name such an item, never “”");
    }

    [TestMethod]
    public async Task AfterUndoATagOnlyTheBatchCarriedLeavesAnInactivePlaceAndIsGoneFromThePicker()
    {
        using var importing = await ListedStash.Open();
        var old = await importing.Stash.ImportFixture("old", "still.png");
        var lone = await TaggedItem(importing, "lone", "fresh");
        await importing.Listing.SetQuery("fresh");
        await importing.Import.Drop(ListedStash.Contents([MediaFixtures.Path("photo.jpg"), MediaFixtures.Path("still.png")]));
        var notice = importing.Notices.Shown.Single();
        // The person takes the tag off the item that carried it before: only the batch's pictures carry it now.
        await importing.Organising.RemoveTag([lone], "fresh");
        Assert.AreEqual(new TagCount("fresh", 2), importing.Listing.Tags.Single());

        await importing.Notices.Undo(notice);

        Assert.IsEmpty(await TagsOf(importing, old));
        Assert.IsEmpty(importing.Listing.Tags, "a tag no item carries ceases to exist (G17)");
        Assert.AreEqual(new TagCount("fresh", 0), importing.Listing.Chips.Single());
    }

    [TestMethod]
    public async Task TwoDropsSnapshotTheTagOnEitherSideOfItsLastCarrierLosingIt()
    {
        using var importing = await ListedStash.Open();
        var carrier = await TaggedItem(importing, "carrier", "cat");
        var store = importing.Stash.Store!;
        await importing.Listing.ShowScope(Scope.All);
        await importing.Listing.ChooseTag("cat");
        var firstFile = new HeldBackFile(MediaFixtures.Path("photo.jpg"));
        var first = importing.Import.Drop(firstFile.Drop);
        Task<IReadOnlyList<ImportedFile>> second;
        try
        {
            Assert.IsTrue((await store.RemoveTag([carrier], "cat")).Single().Succeeded);
            await importing.Listing.Refresh();
            Assert.AreEqual(new TagCount("cat", 0), importing.Listing.Chips.Single());
            Assert.IsEmpty(importing.Listing.LitTags);
            second = importing.Import.Drop(ListedStash.Contents([MediaFixtures.Path("transparent.png")]));
        }
        finally
        {
            firstFile.Release();
        }

        var firstItem = (await first).Single().Item!;
        var secondItem = (await second).Single().Item!;
        CollectionAssert.AreEqual(new[] { "cat" }, (await TagsOf(importing, firstItem)).ToArray(), "the earlier drop keeps its live snapshot");
        Assert.IsEmpty(await TagsOf(importing, secondItem), "the second drop took its snapshot while the place was dead");
    }

    [TestMethod]
    public async Task AFailedLastStepStillNamesTheTagGivenToTheListedPicture()
    {
        using var importing = await ListedStash.Open();
        var carrier = await TaggedItem(importing, "carrier", "Cat");
        await importing.Listing.SetQuery("Cat");
        importing.Stash.SqlWhileOpen(
            "CREATE TRIGGER refuse_clearing BEFORE UPDATE OF pending_steps ON items WHEN NEW.pending_steps IS NULL BEGIN SELECT RAISE(ABORT, 'the write was refused'); END");

        var results = await importing.Import.Drop(ListedStash.Contents([MediaFixtures.Path("still.png")]));

        var file = results.Single();
        Assert.AreEqual(ImportResultKind.Failed, file.Result);
        CollectionAssert.AreEqual(new[] { "Cat" }, (await TagsOf(importing, file.Item!)).ToList());
        var notice = importing.Notices.Shown.Single();
        Assert.AreEqual("Nothing was added · 1 failed", notice.Heading);
        StringAssert.StartsWith(notice.Text, "still.png: Add to the stash failed: ");
        StringAssert.Contains(notice.Text, "the write was refused");
        StringAssert.Contains(notice.Text, "This import gave the picture #Cat.");
        Assert.AreEqual("Undo", notice.UndoLabel);

        await importing.Notices.Undo(notice);

        Assert.AreEqual(carrier.Id, (await importing.Stash.All()).Single().Id);
    }

    [TestMethod]
    public async Task ASpellingCorrectionBetweenImportCommitsKeepsTheTagInTheBatchNotice()
    {
        using var importing = await ListedStash.Open();
        await TaggedItem(importing, "carrier", "Ärger");
        var store = importing.Stash.Store!;
        await importing.Listing.SetQuery("ärger");
        var second = new HeldBackFile(MediaFixtures.Path("transparent.png"));
        var batch = importing.Import.Drop(new DropContents([ListedStash.File(MediaFixtures.Path("photo.jpg")), second.Item], false, false, []));
        try
        {
            Assert.AreSame(second.Requested, await Task.WhenAny(batch, second.Requested));
            var renamed = await store.RenameTag("Ärger", "ärger");
            Assert.IsTrue(renamed.Succeeded, renamed.Failure?.ToString());
        }
        finally
        {
            second.Release();
        }

        var results = await batch;

        CollectionAssert.AreEqual(new[] { "Ärger", "ärger" }, results.Select(file => file.GainedTags.Single()).ToArray());
        Assert.AreEqual("Added 2 to your stash, tagged #Ärger", importing.Notices.Shown.Single().Heading);
    }

    [TestMethod]
    public async Task ANoticeNamesWhatTheImportGaveADuplicateWhenALaterChangeTookItOff()
    {
        using var importing = await ListedStash.Open();
        await TaggedItem(importing, "carrier", "Cat");
        var duplicate = await importing.Stash.ImportFixture("duplicate", "still.png");
        var store = importing.Stash.Store!;
        await importing.Listing.SetQuery("Cat");
        var second = new HeldBackFile(MediaFixtures.Path("transparent.png"));
        var batch = importing.Import.Drop(new DropContents([ListedStash.File(MediaFixtures.Path("still.png")), second.Item], false, false, []));
        try
        {
            Assert.AreSame(second.Requested, await Task.WhenAny(batch, second.Requested));
            var removed = await store.RemoveTagFromAll("Cat", 2);
            Assert.IsNotNull(removed.Value?.Receipt, removed.Failure?.ToString());
        }
        finally
        {
            second.Release();
        }

        await batch;

        Assert.IsEmpty(await TagsOf(importing, duplicate));
        var notice = importing.Notices.Shown.Single();
        Assert.AreEqual("still.png was already in your stash as “duplicate”. This import gave it #Cat. Added “transparent”.", notice.Text);
        Assert.IsTrue(importing.Notices.OffersUndo(notice));
    }

    [TestMethod]
    public async Task AddTagTogglesBothActualCarriersWhileTheSecondDroppedFileIsHeld()
    {
        using var importing = await ListedStash.Open();
        var carrier = await TaggedItem(importing, "carrier", "Cat");
        var duplicate = await importing.Stash.ImportFixture("duplicate", "still.png");
        await importing.Listing.ShowScope(Scope.All);
        var shownBeforeDrop = importing.Listing.Items.ToArray();
        var addTag = TagLists.AddTagEntries(importing.Listing.Tags, shownBeforeDrop).Single(entry => entry.Tag == "Cat");
        Assert.IsFalse(addTag.Marked, "the open list predates the duplicate gaining the tag");
        await importing.Listing.SetQuery("Cat");
        var second = new HeldBackFile(MediaFixtures.Path("transparent.png"));
        var batch = importing.Import.Drop(new DropContents([ListedStash.File(MediaFixtures.Path("still.png")), second.Item], false, false, []));
        try
        {
            Assert.AreSame(second.Requested, await Task.WhenAny(batch, second.Requested));
            CollectionAssert.AreEqual(new[] { "Cat" }, (await TagsOf(importing, duplicate)).ToArray());

            var toggled = await TagLists.Click(importing.Organising, addTag.Action, shownBeforeDrop, addTag.Tag!);

            Assert.IsTrue(toggled.Succeeded, toggled.Failure?.Sentence);
            Assert.IsEmpty(await TagsOf(importing, carrier));
            Assert.IsEmpty(await TagsOf(importing, duplicate));
            Assert.AreEqual("Removed #Cat from 2 items", importing.Notices.Shown.Single(importing.Notices.OffersUndo).Text);
        }
        finally
        {
            second.Release();
            await batch;
        }
    }

    [TestMethod]
    public async Task AddTagQueuedDuringDuplicateComparisonTogglesTheMembershipTheEarlierWriteLeaves()
    {
        using var importing = await ListedStash.Open();
        var carrier = await TaggedItem(importing, "carrier", "Cat");
        var bytes = TestStash.Bytes(7);
        var duplicate = await importing.Stash.Import("Cat by name", bytes);
        await importing.Listing.SetQuery("Cat");
        var opened = importing.Listing.Items.ToArray();
        var entry = TagLists.AddTagEntries(importing.Listing.Tags, opened).Single(tag => tag.Tag == "Cat");
        Assert.HasCount(2, opened);
        Assert.IsFalse(entry.Marked);
        var staged = await importing.Stash.Stage("duplicate.png", bytes);
        var candidate = TestStash.Candidate(staged, "duplicate", bytes) with { Tags = ["Cat"] };

        Task<Outcome<ImportResult>> committing;
        Task<Outcome> toggling;
        using (File.Open(duplicate.FilePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            committing = importing.Stash.Store!.CommitImport(candidate);
            toggling = TagLists.Click(importing.Organising, entry.Action, opened, entry.Tag!);
        }

        var imported = await committing;
        Assert.AreEqual(duplicate.Id, imported.Value?.Item.Id, imported.Failure?.Sentence);
        Assert.IsTrue(imported.Value!.AlreadyInStash);
        Assert.IsTrue((await toggling).Succeeded);
        Assert.IsEmpty(await TagsOf(importing, carrier));
        Assert.IsEmpty(await TagsOf(importing, duplicate));
        Assert.AreEqual("Removed #Cat from 2 items", importing.Notices.Shown.Single(importing.Notices.OffersUndo).Text);
    }

    [TestMethod]
    public async Task AMoveWaitsWhileTheNoticeOfAnImportThatOnlyGaveATagOffersItsUndoWhichRunsBeforeTheCopy()
    {
        using var importing = await ListedStash.Open();
        var old = await importing.Stash.ImportFixture("old", "still.png");
        await TaggedItem(importing, "catted", "cat");
        await importing.Listing.SetQuery("cat");
        await importing.Import.Drop(ListedStash.Contents([MediaFixtures.Path("still.png")]));
        var chosen = MovingStash.Chosen(importing.Stash);
        importing.Dialogs.PickFolderAnswer = () => Task.FromResult<string?>(chosen);

        var moving = importing.Moving.MoveTo(null!);
        await Handing.Eventually(() => importing.Moving.Busy == "Waiting for the Undo of a notice before moving your stash…");
        await importing.Turns.Act(() => importing.Notices.Undo(importing.Notices.Shown.Single()));
        var moved = await moving.WaitAsync(TimeSpan.FromSeconds(20));

        Assert.IsTrue(moved.Succeeded, moved.Failure?.Sentence);
        Assert.AreEqual(chosen, importing.Settings.Current.StashFolder);
        Assert.IsEmpty(await TagsOf(importing, old), "the tag came off before the copy began, as any change of the stash does (State: Move)");
    }

    private static async Task<StashItem> TaggedItem(ListedStash importing, string name, string tag)
    {
        var item = await importing.Stash.Import(name);
        importing.Stash.Tag(item.Id, tag);
        return item;
    }

    private static async Task<IReadOnlyList<string>> TagsOf(ListedStash importing, StashItem item) => (await importing.Stash.All()).Single(listed => listed.Id == item.Id).Tags;
}
