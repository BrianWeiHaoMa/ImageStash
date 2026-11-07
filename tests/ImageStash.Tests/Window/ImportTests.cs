using System.Security.Cryptography;
using ImageStash.Files;
using ImageStash.Media;
using ImageStash.Seams;
using ImageStash.Store;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// The window's imports over a real stash with real pictures: every supported file added once, as a copy, named after
/// its file (G11, G12, F12); duplicates, unsupported and unreadable files and failures each named (G12, G35, N2, N4,
/// E4); the batch's busy state, its notice and its Undo (G16, R9); Import files…'s picker (G2, G7); the lit chip's tag
/// on every item a drop or <c>Ctrl+O</c> adds, read at the user's act (G11); nothing taken while the stash cannot take it;
/// every batch's end announced before the window shows its result, so a preview covering the grid closes first (G12).
/// </summary>
[TestClass]
public sealed class ImportTests
{
    private const string Formats = "ImageStash keeps GIF, PNG, JPG and WebP";

    [TestMethod]
    public async Task ADropAddsEachSupportedFileOnceSelectedFirstInAllAndNamesWhatItSkipped()
    {
        using var importing = await ListedStash.Open();
        var older = await importing.Stash.Import("older");
        await importing.Listing.SetQuery("older");
        var sources = new[] { "photo.jpg", "transparent.png", "animated.gif", "animated.webp" }.Select(MediaFixtures.Path).ToList();
        sources.Add(importing.Stash.Source("clip.mp4", TestStash.Bytes(1)));
        sources.Add(importing.Stash.Source("cat pictures.url", "[InternetShortcut]\r\nURL=https://example.com/cat.gif\r\n"u8.ToArray()));
        var before = sources.Select(path => SHA256.HashData(File.ReadAllBytes(path))).ToList();

        var results = await importing.Import.Drop(ListedStash.Contents(sources));

        CollectionAssert.AreEqual(
            new[] { ImportResultKind.Added, ImportResultKind.Added, ImportResultKind.Added, ImportResultKind.Added, ImportResultKind.Skipped, ImportResultKind.Skipped },
            results.Select(file => file.Result).ToList());
        var added = results.Take(4).Select(file => file.Item!).ToList();
        CollectionAssert.AreEqual(new[] { "photo", "transparent", "animated", "animated" }, added.Select(item => item.Name).ToList());
        CollectionAssert.AreEqual(new[] { "photo.jpg", "transparent.png", "animated.gif", "animated.webp" }, added.Select(item => item.FileName).ToList());
        Assert.AreEqual("a video", results[4].Type);
        Assert.AreEqual("a link", results[5].Type);

        var listing = importing.Listing;
        Assert.AreEqual("", listing.Query, "after an import the search is cleared");
        Assert.AreEqual(Scope.All, listing.LitChip);
        CollectionAssert.AreEquivalent(added.Select(item => item.Id).ToList(), listing.Items.Take(4).Select(item => item.Id).ToList(), "the new items first, newest added first");
        Assert.AreEqual(older.Id, listing.Items[4].Id);
        CollectionAssert.AreEquivalent(added.Select(item => item.Id).ToList(), listing.Selection.ToList());
        Assert.AreEqual(listing.Items[0].Id, listing.Focused);

        var notice = importing.Notices.Shown.Single();
        Assert.AreEqual(
            $"Added 4 to your stash: photo.jpg, transparent.png, animated.gif and animated.webp. Skipped clip.mp4 (a video) and cat pictures.url (a link): {Formats}.",
            notice.Text);
        Assert.AreEqual("Undo", notice.UndoLabel);
        CollectionAssert.AreEqual(before, sources.Select(path => SHA256.HashData(File.ReadAllBytes(path))).ToList(), "the sources stay untouched");
        Assert.HasCount(5, importing.Stash.RootFiles());
        Assert.IsEmpty(importing.Stash.WorkFiles());
        CollectionAssert.AreEqual(MediaFixtures.Bytes("animated.webp"), File.ReadAllBytes(added[3].FilePath), "the stash keeps the file as it came (F3)");
        Assert.IsEmpty(importing.Failures);
    }

    [TestMethod]
    public async Task EveryBatchsEndIsAnnouncedBeforeTheSearchIsClearedAndTheNewItemsSelectedAddingOrNot()
    {
        using var importing = await ListedStash.Open();
        var older = await importing.Stash.Import("older");
        await importing.Listing.SetQuery("older");
        var atTheEnd = new List<(string Query, IReadOnlySet<long> Selection)>();
        importing.Import.Finishing += () => atTheEnd.Add((importing.Listing.Query, importing.Listing.Selection.ToHashSet()));

        var added = await importing.Import.Drop(ListedStash.Contents([MediaFixtures.Path("still.png")]));
        await importing.Listing.SetQuery("older");
        await importing.Import.Drop(ListedStash.Contents([importing.Stash.Source("clip.mp4", TestStash.Bytes(1))]));

        Assert.HasCount(2, atTheEnd, "once per batch, one that added nothing too");
        Assert.AreEqual("older", atTheEnd[0].Query, "before the search is cleared");
        CollectionAssert.AreEqual(new[] { older.Id }, atTheEnd[0].Selection.ToList(), "before the new item is selected");
        Assert.AreEqual("older", atTheEnd[1].Query);
        Assert.AreEqual("", importing.Listing.Query, "and the reset follows it");
        Assert.AreEqual(ImportResultKind.Added, added[0].Result);
    }

    [TestMethod]
    public async Task TheSameFileDroppedAgainIsAlreadyInTheStashAndNothingIsAdded()
    {
        using var importing = await ListedStash.Open();
        await importing.Import.Drop(ListedStash.Contents([MediaFixtures.Path("animated.gif")]));
        var root = importing.Stash.RootFiles();

        var again = await importing.Import.Drop(ListedStash.Contents([MediaFixtures.Path("animated.gif")]));

        Assert.AreEqual(ImportResultKind.AlreadyInStash, again.Single().Result);
        Assert.AreEqual("animated", again.Single().Item!.Name);
        var notice = importing.Notices.Shown[^1];
        Assert.AreEqual("Nothing was added. animated.gif was already in your stash as “animated”.", notice.Text);
        Assert.IsNull(notice.UndoLabel, "nothing was added, so there is nothing to undo");
        CollectionAssert.AreEquivalent(root, importing.Stash.RootFiles());
        Assert.HasCount(1, await importing.Stash.All());
    }

    [TestMethod]
    public async Task UndoRemovesEverythingTheBatchAddedAndAFileDroppedAgainIsNew()
    {
        using var importing = await ListedStash.Open();
        await importing.Import.Drop(ListedStash.Contents(new[] { "photo.jpg", "transparent.png", "animated.gif", "animated.webp" }.Select(MediaFixtures.Path).ToList()));
        var earlier = await importing.Stash.All();
        var two = await importing.Import.Drop(ListedStash.Contents([MediaFixtures.Path("still.png"), MediaFixtures.Path("still.gif")]));
        var notice = importing.Notices.Shown[^1];

        await importing.Notices.Undo(notice);

        CollectionAssert.AreEquivalent(earlier.Select(item => item.Id).ToList(), (await importing.Stash.All()).Select(item => item.Id).ToList());
        CollectionAssert.AreEquivalent(earlier.Select(item => item.Id).ToList(), importing.Listing.Items.Select(item => item.Id).ToList(), "the grid no longer shows them");
        CollectionAssert.AreEquivalent(two.Select(file => file.Item!.FilePath).ToList(), importing.Stash.RecycleBin.Asked, "their files went to the Recycle Bin (G29)");
        Assert.HasCount(4, importing.Stash.RootFiles());
        CollectionAssert.DoesNotContain(importing.Notices.Shown.ToList(), notice);
        var asked = importing.Stash.RecycleBin.Asked.Count;
        await importing.Notices.Undo(notice);
        Assert.HasCount(asked, importing.Stash.RecycleBin.Asked, "Undo acts once");

        var again = await importing.Import.Drop(ListedStash.Contents([MediaFixtures.Path("still.png")]));

        Assert.AreEqual(ImportResultKind.Added, again.Single().Result, "an undone file is not a duplicate");
        Assert.HasCount(5, await importing.Stash.All());
    }

    [TestMethod]
    public async Task AnUndoTheRecycleBinRefusesNamesEveryRefusedFileRemovesTheRestAndLeavesTheOthersUsable()
    {
        using var importing = await ListedStash.Open();
        var older = await importing.Stash.Import("older");
        var three = await importing.Import.Drop(ListedStash.Contents(new[] { "photo.jpg", "transparent.png", "animated.gif" }.Select(MediaFixtures.Path).ToList()));
        var notice = importing.Notices.Shown.Single();
        importing.Stash.RecycleBin.Refusing = path => !path.EndsWith("transparent.png", StringComparison.Ordinal);

        await importing.Notices.Undo(notice);

        CollectionAssert.AreEquivalent(
            new[] { "Move to the Recycle Bin failed: photo.jpg, the test Recycle Bin refuses it", "Move to the Recycle Bin failed: animated.gif, the test Recycle Bin refuses it" },
            importing.Notices.Shown.Select(shown => shown.Text).ToList(),
            "each refused file is named in a notice of its own (G35)");
        Assert.IsFalse(File.Exists(three[1].Item!.FilePath), "the file the Recycle Bin took is gone from the stash");
        CollectionAssert.AreEqual(new[] { older.Id }, (await importing.Stash.All()).Select(item => item.Id).ToList());
        CollectionAssert.AreEqual(new[] { older.Id }, importing.Listing.Items.Select(item => item.Id).ToList(), "the item outside the batch stays usable");
    }

    [TestMethod]
    public async Task UnreadableFilesAreNamedAndLeaveNothingBehind()
    {
        using var importing = await ListedStash.Open();
        var still = MediaFixtures.Bytes("still.png");
        var sources = new List<string>
        {
            importing.Stash.Source("cut.png", still[..^12]),
            importing.Stash.Source("x.gif", TestStash.Bytes(2, 300)),
            importing.Stash.Source("t.webp", "not a picture at all"u8.ToArray()),
        };

        var results = await importing.Import.Drop(ListedStash.Contents(sources));

        Assert.IsTrue(results.All(file => file.Result == ImportResultKind.Unreadable && file.Failure is null));
        Assert.AreEqual("Nothing was added. cut.png, x.gif and t.webp are unreadable.", importing.Notices.Shown.Single().Text);
        Assert.IsEmpty(await importing.Stash.All());
        Assert.IsEmpty(importing.Stash.RootFiles());
        Assert.IsEmpty(importing.Stash.WorkFiles());
    }

    [TestMethod]
    public async Task ASourceHeldPastTheRetryIsNamedAsHeldAndTheOthersAreAdded()
    {
        using var importing = await ListedStash.Open();
        var held = importing.Stash.Source("held.png", MediaFixtures.Bytes("still.png"));

        IReadOnlyList<ImportedFile> results;
        using (new FileStream(held, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            results = await importing.Import.Drop(ListedStash.Contents([held, MediaFixtures.Path("photo.jpg")]));
        }

        Assert.AreEqual(ImportResultKind.Failed, results[0].Result);
        Assert.AreEqual(new Failure("Copy into the stash", "held.png", HeldFileRetry.HeldReason), results[0].Failure, "a held file is not unreadable (G35)");
        Assert.AreEqual(ImportResultKind.Added, results[1].Result);
        Assert.AreEqual($"Added 1 to your stash: photo.jpg. Copy into the stash failed: held.png, {HeldFileRetry.HeldReason}.", importing.Notices.Shown.Single().Text);
        Assert.IsEmpty(importing.Stash.WorkFiles());
    }

    [TestMethod]
    public async Task ASourceHeldBrieflyIsRetriedAndAdded()
    {
        using var importing = await ListedStash.Open();
        var held = importing.Stash.Source("briefly.png", MediaFixtures.Bytes("still.png"));
        var holder = new FileStream(held, FileMode.Open, FileAccess.Read, FileShare.None);
        _ = Task.Delay(100).ContinueWith(_ => holder.Dispose(), TaskScheduler.Default);

        var results = await importing.Import.Drop(ListedStash.Contents([held]));

        Assert.AreEqual(ImportResultKind.Added, results.Single().Result, results.Single().Failure?.Sentence);
    }

    [TestMethod]
    public void ACommitWhoseFileNeverReachedTheRootIsAFailureNotAnAddition()
    {
        var item = TestItems.Sized(7, 8, 6);
        var held = new Failure("Add to the stash", "held.png", HeldFileRetry.HeldReason);

        var neverPlaced = ImportedFile.FromCommit("held.png", Outcome<ImportResult>.FailAfterCommit(held, new ImportResult(item, AlreadyInStash: false, Listed: false)));
        var found = ImportedFile.FromCommit("again.png", Outcome<ImportResult>.FailAfterCommit(held, new ImportResult(item, AlreadyInStash: true)));

        Assert.AreEqual(new ImportedFile("held.png", ImportResultKind.Failed, Failure: held), neverPlaced, "not shown, not selected, not in the batch's Undo");
        Assert.IsFalse(neverPlaced.PutInStash);
        Assert.AreEqual(new ImportedFile("again.png", ImportResultKind.AlreadyInStash, item, Failure: held), found);
        Assert.IsFalse(found.PutInStash, "a duplicate is not the batch's to undo");
        Assert.AreEqual($"Nothing was added. Add to the stash failed: held.png, {HeldFileRetry.HeldReason}.", Import.NoticeText([neverPlaced]));
    }

    [TestMethod]
    public async Task AnImportWhoseLastStepFailedIsNamedAsFailedNotAddedAndItsUndoStillRemovesIt()
    {
        using var importing = await ListedStash.Open();
        // The rename into the root done and the transaction that clears its step refused, as a refused write leaves it.
        importing.Stash.SqlWhileOpen(
            "CREATE TRIGGER refuse_clearing BEFORE UPDATE OF pending_steps ON items WHEN NEW.pending_steps IS NULL BEGIN SELECT RAISE(ABORT, 'the write was refused'); END");

        var results = await importing.Import.Drop(ListedStash.Contents([MediaFixtures.Path("still.png")]));

        var file = results.Single();
        Assert.AreEqual(ImportResultKind.Failed, file.Result, "no success is shown before the last step completed (R9)");
        Assert.IsNotNull(file.Failure);
        var notice = importing.Notices.Shown.Single();
        Assert.StartsWith("Nothing was added. Add to the stash failed: ", notice.Text);
        Assert.AreEqual(file.Item!.Id, (await importing.Stash.All()).Single().Id, "its picture is in place, so the stash shows it (State: A live failure)");
        Assert.AreEqual("Undo", notice.UndoLabel, "the batch put it in the stash, so Undo removes it (G12)");

        await importing.Notices.Undo(notice);

        Assert.IsEmpty(await importing.Stash.All());
        Assert.IsEmpty(importing.Stash.RootFiles());
    }

    [TestMethod]
    public async Task AFolderGivesItsOwnFilesAndNotThoseOfItsSubfolders()
    {
        using var importing = await ListedStash.Open();
        var folder = importing.Stash.Outside("dropped");
        Directory.CreateDirectory(Path.Combine(folder, "inner"));
        File.Copy(MediaFixtures.Path("still.png"), Path.Combine(folder, "still.png"));
        File.Copy(MediaFixtures.Path("photo.jpg"), Path.Combine(folder, "inner", "photo.jpg"));
        File.WriteAllText(Path.Combine(folder, "notes.txt"), "notes");

        var results = await importing.Import.Drop(new DropContents([new DroppedItem("dropped", folder, null, IsFolder: true)], false, false, []));

        CollectionAssert.AreEqual(new[] { "notes.txt", "still.png" }, results.Select(file => file.Name).ToList());
        Assert.AreEqual("still", (await importing.Stash.All()).Single().Name);
        Assert.AreEqual($"Added 1 to your stash: still.png. Skipped notes.txt (a .txt file): {Formats}.", importing.Notices.Shown.Single().Text);
    }

    [TestMethod]
    public async Task APictureHandedOverWithoutAPathIsReadOnceAndNamedAfterItsName()
    {
        using var importing = await ListedStash.Open();
        var opened = 0;
        var picture = new DroppedItem("image.png", null, () =>
        {
            opened++;
            return Task.FromResult<Stream>(new MemoryStream(MediaFixtures.Bytes("transparent.png")));
        }, IsFolder: false);
        var released = new Released();

        var results = await importing.Import.Drop(new DropContents([picture], false, false, [released]));

        Assert.AreEqual(ImportResultKind.Added, results.Single().Result, results.Single().Failure?.Sentence);
        Assert.AreEqual("image", results.Single().Item!.Name);
        CollectionAssert.AreEqual(MediaFixtures.Bytes("transparent.png"), File.ReadAllBytes(results.Single().Item!.FilePath));
        Assert.AreEqual(1, opened);
        Assert.IsTrue(released.Disposed, "the source may let go of what it handed over once the batch has read it");
        Assert.IsEmpty(importing.Stash.WorkFiles());
    }

    [TestMethod]
    public async Task AStreamFailingPartWayIsNamedAndLeavesNothing()
    {
        using var importing = await ListedStash.Open();
        var picture = new DroppedItem("broken.gif", null, () => Task.FromResult<Stream>(new FailingStream(MediaFixtures.Bytes("animated.gif"))), IsFolder: false);

        var results = await importing.Import.Drop(new DropContents([picture], false, false, []));

        Assert.AreEqual(ImportResultKind.Failed, results.Single().Result);
        Assert.AreEqual(new Failure("Copy into the stash", "broken.gif", FailingStream.Reason), results.Single().Failure);
        Assert.IsEmpty(await importing.Stash.All());
        Assert.IsEmpty(importing.Stash.WorkFiles());
    }

    [TestMethod]
    public async Task TwentyFilesShowTheBusyStateAndNothingAsAddedUntilTheirBatchEndsAndADropMeanwhileRunsAfter()
    {
        using var importing = await ListedStash.Open();
        var twenty = Enumerable.Range(1, 20).Select(i => importing.Stash.Source($"p{i:00}.png", MediaFixtures.DistinctPng(i))).ToList();
        var later = importing.Stash.Source("later.png", MediaFixtures.DistinctPng(99));
        var seen = new List<(string? Busy, int Shown)>();
        importing.Import.Changed += () => seen.Add((importing.Import.Busy, importing.Listing.Items.Count));

        var first = importing.Import.Drop(ListedStash.Contents(twenty));
        var second = importing.Import.Drop(ListedStash.Contents([later]));
        await Task.WhenAll(first, second);

        var busy = seen.Where(step => step.Busy is not null).ToList();
        Assert.IsTrue(seen.TakeWhile(step => step.Busy is not null).All(step => step.Shown == 0), "nothing shows as added while the first batch runs");
        var progress = busy.Select(step => step.Busy).Where(text => text!.StartsWith("Adding ") && text.Contains(" of ")).ToList();
        CollectionAssert.AreEqual(
            Enumerable.Range(1, 20).Select(i => $"Adding {i} of 20 to your stash…").Append("Adding 1 of 1 to your stash…").ToList(),
            progress,
            "the second drop runs after the first");
        Assert.IsNull(importing.Import.Busy);
        Assert.HasCount(21, await importing.Stash.All());
        var texts = importing.Notices.Shown.Select(notice => notice.Text).ToList();
        Assert.HasCount(2, texts, "each batch has its own notice");
        Assert.AreEqual("Added 20 to your stash: " + string.Join(", ", twenty.Take(19).Select(Path.GetFileName)) + " and p20.png.", texts[0]);
        Assert.AreEqual("Added 1 to your stash: later.png.", texts[1]);
    }

    [TestMethod]
    public async Task TheImportsNoticeLastsSixSecondsLongerWhileHoveredAndItsUndoActsOnlyWhileItShows()
    {
        using var importing = await ListedStash.Open();
        await importing.Import.Drop(ListedStash.Contents([MediaFixtures.Path("still.png")]));
        var notices = importing.Notices;
        var notice = notices.Shown.Single();

        importing.Stash.Clock.Now += TimeSpan.FromSeconds(5);
        notices.Sweep();
        Assert.AreSame(notice, notices.Shown.Single(), "still there with its Undo after five seconds, the window shown or not");
        importing.Stash.Clock.Now += TimeSpan.FromSeconds(1);
        notices.Sweep();
        Assert.IsEmpty(notices.Shown);
        await notices.Undo(notice);
        Assert.HasCount(1, await importing.Stash.All(), "an Undo after the notice left does nothing");

        await importing.Import.Drop(ListedStash.Contents([MediaFixtures.Path("photo.jpg")]));
        var hovered = notices.Shown.Single();
        importing.Stash.Clock.Now += TimeSpan.FromSeconds(4);
        notices.Hover(hovered, over: true);
        importing.Stash.Clock.Now += TimeSpan.FromSeconds(10);
        notices.Sweep();
        Assert.AreSame(hovered, notices.Shown.Single(), "it stays while hovered");
        notices.EndHovers();
        importing.Stash.Clock.Now += TimeSpan.FromSeconds(1.5);
        notices.Sweep();
        Assert.AreSame(hovered, notices.Shown.Single(), "and leaves after the remainder");
        await notices.Undo(hovered);
        Assert.IsEmpty(notices.Shown);
        Assert.HasCount(1, await importing.Stash.All(), "its Undo acted while it showed");
    }

    [TestMethod]
    public async Task WhileATagChipIsLitEveryItemADropOrCtrlOAddsGetsItsTagAndADuplicateGetsNone()
    {
        using var importing = await ListedStash.Open();
        var dogged = await importing.Stash.Import("dogged");
        var catted = await importing.Stash.Import("catted");
        importing.Stash.Tag(dogged.Id, "dog");
        importing.Stash.Tag(catted.Id, "cat");

        await importing.Listing.SetQuery("DOG");
        CollectionAssert.AreEqual(new[] { "dog" }, importing.Listing.LitTags.ToList());
        var dropped = (await importing.Import.Drop(ListedStash.Contents([MediaFixtures.Path("still.png")]))).Single().Item!;

        CollectionAssert.AreEqual(new[] { "dog" }, (await TagsOf(importing, dropped)).ToList(), "a lit chip's tag, as stored (G11)");
        Assert.AreEqual("", importing.Listing.Query, "after an import the search is cleared (G12)");
        Assert.IsEmpty(importing.Listing.LitTags, "and no chip is lit");

        await importing.Listing.SetQuery("cat");
        var again = (await importing.Import.Drop(ListedStash.Contents([MediaFixtures.Path("still.png")]))).Single();
        Assert.AreEqual(ImportResultKind.AlreadyInStash, again.Result);
        CollectionAssert.AreEqual(new[] { "dog" }, (await TagsOf(importing, dropped)).ToList(), "a duplicate gets no tag");

        await importing.Listing.SetQuery("#dog");
        var picked = (await importing.Import.Pick(() => Task.FromResult<IReadOnlyList<string>?>([MediaFixtures.Path("photo.jpg")]), _ => { })).Single().Item!;
        CollectionAssert.AreEqual(new[] { "dog" }, (await TagsOf(importing, picked)).ToList(), "Ctrl+O while it is lit (G11)");

        await importing.Listing.SetQuery("do");
        var unlit = (await importing.Import.Drop(ListedStash.Contents([MediaFixtures.Path("animated.gif")]))).Single().Item!;
        Assert.IsEmpty(await TagsOf(importing, unlit), "a query that only matches a tag lights no chip, so tags nothing");
        Assert.IsEmpty(importing.Failures);
    }

    [TestMethod]
    public async Task TheTagIsTheOneTheQueryIsExactlyAmongEveryTagWhicheverChipsComeBeforeIt()
    {
        using var importing = await ListedStash.Open();
        foreach (var (tag, count) in new[] { ("cat", 5), ("animal", 4), ("a", 1) })
        {
            for (var i = 0; i < count; i++)
            {
                importing.Stash.Tag((await importing.Stash.Import($"{tag} {i}")).Id, tag);
            }
        }

        await importing.Listing.SetQuery("a");
        var dropped = (await importing.Import.Drop(ListedStash.Contents([MediaFixtures.Path("still.png")]))).Single().Item!;

        CollectionAssert.AreEqual(new[] { "a" }, (await TagsOf(importing, dropped)).ToList(), "the exact match, though cat and animal are carried by more (G5, G11)");
    }

    [TestMethod]
    public async Task ABatchQueuedBehindAnotherGetsTheTagLitAtItsDropThoughTheFirstBatchsEndClearedTheSearch()
    {
        using var importing = await ListedStash.Open();
        importing.Stash.Tag((await importing.Stash.Import("dogged")).Id, "dog");
        var twenty = Enumerable.Range(1, 20).Select(i => importing.Stash.Source($"q{i:00}.png", MediaFixtures.DistinctPng(i))).ToList();
        var later = importing.Stash.Source("later.png", MediaFixtures.DistinctPng(99));
        await importing.Listing.SetQuery("dog");

        var first = importing.Import.Drop(ListedStash.Contents(twenty));
        var second = importing.Import.Drop(ListedStash.Contents([later]));
        var results = (await first).Concat(await second).ToList();

        Assert.HasCount(21, results);
        Assert.IsTrue(results.All(file => file.Result == ImportResultKind.Added), Import.NoticeText(results));
        var all = await importing.Stash.All();
        Assert.IsTrue(results.All(file => all.Single(item => item.Id == file.Item!.Id).Tags.SequenceEqual(["dog"])), "every item of both batches carries the tag lit at its drop (G11)");
        Assert.AreEqual("", importing.Listing.Query);
    }

    [TestMethod]
    public async Task ImportFilesKeepsTheFlyoutUpWhileThePickerIsOpenHoweverItReturns()
    {
        using var importing = await ListedStash.Open();
        var dialogUp = new List<bool>();
        var chosen = new[] { MediaFixtures.Path("still.png"), MediaFixtures.Path("animated.gif") };

        var picked = await importing.Import.Pick(() =>
        {
            Assert.IsTrue(dialogUp[^1], "the flag is up before the picker opens");
            return Task.FromResult<IReadOnlyList<string>?>(chosen);
        }, dialogUp.Add);
        var cancelled = await importing.Import.Pick(() => Task.FromResult<IReadOnlyList<string>?>(null), dialogUp.Add);
        var failed = await importing.Import.Pick(() => throw new InvalidOperationException("the dialog broke"), dialogUp.Add);

        CollectionAssert.AreEqual(new[] { true, false, true, false, true, false }, dialogUp);
        CollectionAssert.AreEqual(new[] { "still", "animated" }, picked.Select(file => file.Item!.Name).ToList(), "chosen files are imported as a drop's would be");
        Assert.IsEmpty(cancelled);
        Assert.IsEmpty(failed);
        Assert.HasCount(2, await importing.Stash.All());
        CollectionAssert.AreEqual(
            new[] { "Added 2 to your stash: still.png and animated.gif.", "Import files failed: the file picker, the dialog broke" },
            importing.Notices.Shown.Select(notice => notice.Text).ToList(),
            "a cancelled picker changes nothing; a failed one is named");
    }

    [TestMethod]
    public async Task WithTwoChipsLitADropAndCtrlOAndAPastedPictureGiveEveryNewItemBothTags()
    {
        using var importing = await ListedStash.Open();
        var carrier = await importing.Stash.Import("carrier");
        importing.Stash.Tag(carrier.Id, "dog");
        importing.Stash.Tag(carrier.Id, "cat");

        await importing.Listing.SetQuery("dog cat");
        var dropped = (await importing.Import.Drop(ListedStash.Contents([MediaFixtures.Path("still.png")]))).Single().Item!;
        CollectionAssert.AreEquivalent(new[] { "dog", "cat" }, (await TagsOf(importing, dropped)).ToList(), "a drop (G11)");

        await importing.Listing.SetQuery("dog cat");
        var picked = (await importing.Import.Pick(() => Task.FromResult<IReadOnlyList<string>?>([MediaFixtures.Path("transparent.png")]), _ => { })).Single().Item!;
        CollectionAssert.AreEquivalent(new[] { "dog", "cat" }, (await TagsOf(importing, picked)).ToList(), "Ctrl+O (G11)");

        await importing.Listing.SetQuery("dog cat");
        importing.Clipboard.Reading = Outcome<ClipboardReading>.Of(new ClipboardReading([], MediaFixtures.Bytes("transparent.png"), null, false));
        await importing.Paste.Paste();
        var pasted = (await importing.Stash.All()).OrderBy(item => item.Id).Last();
        CollectionAssert.AreEquivalent(new[] { "dog", "cat" }, pasted.Tags.ToList(), "a pasted picture (G11)");
    }

    private static async Task<IReadOnlyList<string>> TagsOf(ListedStash importing, StashItem added) => (await importing.Stash.All()).Single(item => item.Id == added.Id).Tags;

    [TestMethod]
    public void ThePickerOffersTheFourFormatsFirstThenEveryFile()
    {
        Assert.HasCount(2, Import.PickerFilters);
        CollectionAssert.AreEqual(new[] { "*.png", "*.jpg", "*.jpeg", "*.gif", "*.webp" }, Import.PickerFilters[0].Patterns.ToList());
        Assert.AreEqual("All files", Import.PickerFilters[1].Name);
        CollectionAssert.AreEqual(new[] { "*.*" }, Import.PickerFilters[1].Patterns.ToList());
    }

    [TestMethod]
    public async Task NothingIsTakenWhileTheStashIsStillOpeningOrDidNotOpen()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var notices = new Notices(stash.Clock);
        var opening = new Listing(_ => { });
        var import = new Import(opening, notices, TestStash.ShortRetry);
        var picked = false;
        var released = new Released();

        await import.Drop(new DropContents([ListedStash.File(MediaFixtures.Path("still.png"))], false, false, [released]));
        await import.Pick(() => { picked = true; return Task.FromResult<IReadOnlyList<string>?>(null); }, _ => { });
        opening.StashFailed(new Failure("Open the stash", "stash.db", "it is broken"));
        await import.Drop(new DropContents([ListedStash.File(MediaFixtures.Path("still.png"))], false, false, []));

        CollectionAssert.AreEqual(
            new[]
            {
                "Add to the stash failed: still.png, your stash is still opening",
                "Add to the stash failed: the files to import, your stash is still opening",
                "Add to the stash failed: still.png, your stash could not be opened",
            },
            notices.Shown.Select(notice => notice.Text).ToList());
        Assert.IsFalse(picked, "the picker does not open");
        Assert.IsTrue(released.Disposed);
        Assert.IsEmpty(stash.WorkFiles(), "nothing is staged");
        Assert.IsEmpty(await stash.All());
    }

    [TestMethod]
    public async Task ADropThatIsOnlyALinkIsRefusedByNameAndNothingIsFetched()
    {
        using var importing = await ListedStash.Open();
        await importing.Listing.SetQuery("cat");

        var link = await importing.Import.Drop(new DropContents([], onlyLink: true, filesUnavailable: false, []));
        var nothing = await importing.Import.Drop(new DropContents([], onlyLink: false, filesUnavailable: false, []));
        var unavailable = await importing.Import.Drop(new DropContents([], onlyLink: false, filesUnavailable: true, []));

        Assert.IsEmpty(link);
        Assert.IsEmpty(nothing);
        Assert.IsEmpty(unavailable);
        CollectionAssert.AreEqual(
            new[]
            {
                "Add to the stash failed: the link, links are not taken and nothing is downloaded; drag the picture itself in",
                "Add to the stash failed: what was dropped, it holds no file",
                "Add to the stash failed: what was dropped, its files could not be read from what was dragged",
            },
            importing.Notices.Shown.Select(notice => notice.Text).ToList());
        Assert.AreEqual("cat", importing.Listing.Query, "a refused drop is not an import");
        Assert.IsEmpty(importing.Stash.WorkFiles());
    }

    [TestMethod]
    public async Task AFileNamedOnlyByItsExtensionGivesAnItemWithoutAName()
    {
        using var importing = await ListedStash.Open();

        var results = await importing.Import.Drop(ListedStash.Contents([importing.Stash.Source(".png", MediaFixtures.Bytes("still.png"))]));

        Assert.AreEqual("", results.Single().Item!.Name, "nothing requires a name (G17); F12's name is the file's without its extension");
        Assert.AreEqual(ItemFileNames.Unnamed + ".png", results.Single().Item!.FileName);
    }

    private sealed class Released : IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }

    /// <summary>A browser's stream that breaks after its first bytes.</summary>
    private sealed class FailingStream(byte[] bytes) : MemoryStream(bytes)
    {
        public const string Reason = "the browser stopped sending it";

        public override int Read(byte[] buffer, int offset, int count) =>
            Position > 0 ? throw new IOException(Reason) : base.Read(buffer, offset, Math.Min(count, 16));

        public override int Read(Span<byte> buffer) =>
            Position > 0 ? throw new IOException(Reason) : base.Read(buffer[..Math.Min(buffer.Length, 16)]);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Read(buffer.Span));

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            Task.FromResult(Read(buffer, offset, count));
    }
}
