using System.Runtime.Versioning;
using ImageStash.Files;
using ImageStash.Media;
using ImageStash.Store;
using ImageStash.Tests.Support;

namespace ImageStash.Tests.Store;

/// <summary>
/// The start's adoption (State: Open with…, "At start, every recorded session's copy is classified the same way before
/// anything reads the stash"; G27, G28, G35): a rewrite found at start on an item with nothing pending adopted before any
/// query answers, with a fresh record the next start expires, and reported once to the first asking; a held copy, an
/// undecodable rewrite and a row whose change the start cannot finish left for later; an adoption whose step is still
/// refused undone at that start, the rewrite awaiting again.
/// </summary>
[TestClass]
public sealed class ItemAdoptionAtStartTests
{
    private static readonly byte[] Still = MediaFixtures.Bytes("still.png");

    private static readonly byte[] Rewrite = MediaFixtures.DistinctPng(5, 12, 10);

    [TestMethod]
    public async Task ACopyRewrittenWhileClosedIsAdoptedBeforeAnyQueryAndReportedOnce()
    {
        using var stash = new TestStash();
        var (cat, copy) = await Begun(stash);
        stash.Close();
        OpenedWith.Rewrite(copy, Rewrite);
        stash.ReadCalls.Clear();

        await stash.OpenCleanly();
        var onDiskWhenOpened = File.ReadAllBytes(stash.Folder.InRoot("cat.png"));
        var readWhenOpened = stash.ReadCalls.ToList();
        var listed = (await stash.All()).Single();
        var first = (await stash.Store!.OpenWithSessions()).Value!;
        var second = (await stash.Store.OpenWithSessions()).Value!;

        CollectionAssert.AreEqual(Rewrite, onDiskWhenOpened, "adopted before anything read the stash");
        CollectionAssert.AreEqual(new[] { "cat.png" }, readWhenOpened);
        Assert.AreEqual((new MediaFacts(ImageFormat.Png, 12, 10, false, 1, 0), 1L, false), (listed.Facts, listed.RenderedRevision, listed.Edited));
        var record = OpenedWith.Undo(stash, cat.Id)!;
        Assert.AreEqual((1L, "cat.png", false), (record.Revision, record.OriginalFile, record.InOriginals), "a fresh record, live");
        CollectionAssert.AreEqual(new[] { new AdoptedAtStart(cat.Id, 1) }, first.Adoptions.ToList());
        CollectionAssert.AreEqual(new[] { new OpenWithSession(cat.Id, copy) }, first.Sessions.ToList());
        Assert.IsEmpty(second.Adoptions, "reported once");
        Assert.HasCount(1, second.Sessions);
        Assert.IsEmpty(stash.RecycleBin.Asked);

        Assert.IsEmpty((await stash.Reopen()).Failures);
        Assert.IsNull(OpenedWith.Undo(stash, cat.Id), "expired at the next start");
        CollectionAssert.AreEqual(new[] { EditedItems.Hash(Still) }, OpenedWith.Recycled(stash));
        Assert.IsEmpty((await stash.Store!.OpenWithSessions()).Value!.Adoptions);
    }

    [TestMethod]
    public async Task OfTwoSessionsOnlyTheRewrittenOneIsAdopted()
    {
        using var stash = new TestStash();
        var (cat, catCopy) = await Begun(stash);
        var dog = await stash.ImportFixture("dog", "photo.jpg");
        await OpenedWith.Begin(stash, dog);
        stash.Close();
        OpenedWith.Rewrite(catCopy, Rewrite);

        await stash.OpenCleanly();

        CollectionAssert.AreEqual(new[] { new AdoptedAtStart(cat.Id, 1) }, (await stash.Store!.OpenWithSessions()).Value!.Adoptions.ToList());
        TestStash.AssertSameItem(dog, await EditedItems.Listed(stash.Store, dog.Id));
        Assert.IsNull(OpenedWith.Undo(stash, dog.Id));
    }

    [TestMethod]
    [DoNotParallelize]
    [SupportedOSPlatform("windows")]
    public async Task ARewrittenCopyHeldPastTheRetryIsLeftAwaitingAndNotReported()
    {
        using var stash = new TestStash();
        var (cat, copy) = await Begun(stash);
        stash.Close();
        OpenedWith.Rewrite(copy, Rewrite);

        var locked = HeldFiles.HeldFor(copy, TimeSpan.FromSeconds(1.5));
        await stash.OpenCleanly();
        await locked;

        Assert.IsEmpty((await stash.Store!.OpenWithSessions()).Value!.Adoptions);
        CollectionAssert.AreEqual(Still, File.ReadAllBytes(cat.FilePath));
        Assert.AreEqual(OpenWithCopy.AwaitingAdoption, await OpenedWith.Classify(stash, cat.Id), "the mark set once it opens");
    }

    [TestMethod]
    public async Task AnUndecodableRewriteAtStartIsNamedAndNotAdopted()
    {
        using var stash = new TestStash();
        var (cat, copy) = await Begun(stash);
        stash.Close();
        OpenedWith.Rewrite(copy, OpenedWith.Garbage(3));

        var opening = await stash.Open();

        Assert.AreEqual(new Failure(OpenWithCopies.ClassifyAction, "cat.png", MediaDecoder.UnreadableReason), opening.Failures.Single());
        Assert.IsEmpty((await opening.Store!.OpenWithSessions()).Value!.Adoptions);
        CollectionAssert.AreEqual(Still, File.ReadAllBytes(cat.FilePath));
        Assert.IsNull(OpenedWith.Undo(stash, cat.Id));
    }

    [TestMethod]
    public async Task AnAdoptionTheStartCannotFinishIsUndoneAtThatStartAndMadeAtTheNext()
    {
        using var stash = new TestStash();
        var (cat, _) = await OpenedWith.AwaitingCat(stash, Rewrite);
        var row = OpenedWith.WholeRow(stash, cat.Id);
        stash.Close();

        StoreOpening opening;
        using (TestStash.Hold(cat.FilePath))
        {
            opening = await stash.Open();
        }

        Assert.AreEqual(new Failure(OpenWithCopies.ClassifyAction, "cat", HeldFileRetry.HeldReason), opening.Failures.Single());
        Assert.AreEqual(row, OpenedWith.WholeRow(stash, cat.Id), "exactly as before, the rewrite awaiting, no step pending");
        TestStash.AssertSameItem(cat, await EditedItems.Listed(opening.Store!, cat.Id));
        Assert.IsEmpty(stash.WorkFiles());
        Assert.IsEmpty((await opening.Store!.OpenWithSessions()).Value!.Adoptions);

        Assert.IsEmpty((await stash.Reopen()).Failures);
        CollectionAssert.AreEqual(Rewrite, File.ReadAllBytes(stash.Folder.InRoot("cat.png")));
        CollectionAssert.AreEqual(new[] { new AdoptedAtStart(cat.Id, 1) }, (await stash.Store!.OpenWithSessions()).Value!.Adoptions.ToList());
    }

    [TestMethod]
    public async Task ASavePendingAtTheKillIsFinishedBeforeTheRewriteIsAdoptedOverIt()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var cat = await EditedItems.SecondSaveCommitted(stash);
        await OpenedWith.AwaitingRewrite(stash, cat, Rewrite);
        stash.RenderCalls.Clear();

        var opening = await stash.Reopen();

        Assert.IsEmpty(opening.Failures, string.Join("; ", opening.Failures));
        Assert.HasCount(1, stash.RenderCalls, "the Save rendered again and finished first");
        var record = OpenedWith.Undo(stash, cat.Id)!;
        Assert.AreEqual((EditedItems.Arrow.ToJson(), 3L, true), (record.Layers, record.Revision, record.InOriginals), "adopted over the finished Save");
        CollectionAssert.AreEqual(Rewrite, File.ReadAllBytes(stash.Folder.InRoot("cat.png")));
        CollectionAssert.AreEqual(new[] { new AdoptedAtStart(cat.Id, 3) }, (await stash.Store!.OpenWithSessions()).Value!.Adoptions.ToList());
        Assert.IsEmpty(EditedItems.Row(stash, cat.Id).Steps);
    }

    /// <summary>An open stash holding "cat", imported from still.png, with an Open with… session begun on it.</summary>
    private static async Task<(StashItem Cat, string Copy)> Begun(TestStash stash)
    {
        await stash.OpenCleanly();
        var cat = await stash.ImportFixture("cat", "still.png");
        return (cat, await OpenedWith.Begin(stash, cat));
    }
}
