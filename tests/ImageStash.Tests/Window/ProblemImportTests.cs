using ImageStash.Store;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// The import's notice that names a problem (G12, G16, G35), over a real store: one naming a skipped, unreadable or
/// failed file, or a duplicate whose result carries a failure, stays past its time; its Undo removes the batch before
/// that time and offers nothing after it (G18); one saying only what was added or already in the stash leaves at its time.
/// </summary>
[TestClass]
public sealed class ProblemImportTests
{
    private static readonly string[] Four = ["photo.jpg", "transparent.png", "animated.gif", "animated.webp"];

    [TestMethod]
    public async Task BeforeItsTimeTheUndoOfAnImportOfFourAndOneSkippedRemovesTheFour()
    {
        using var listed = await ListedStash.Open();
        await DropFourAndAClip(listed);
        var notice = listed.Notices.Shown.Single();

        listed.Stash.Clock.Now += Notices.LastingFor(notice.Words) - TimeSpan.FromMilliseconds(1);
        listed.Notices.Sweep();
        await listed.Notices.Undo(notice);

        Assert.IsEmpty(await listed.Stash.All());
    }

    [TestMethod]
    public async Task AfterAMinuteOfShownTimeTheNoticeNamingClipMp4StillShowsAndNoLongerOffersItsUndo()
    {
        using var listed = await ListedStash.Open();
        await DropFourAndAClip(listed);
        var notice = listed.Notices.Shown.Single();

        listed.Stash.Clock.Now += TimeSpan.FromMinutes(1);
        listed.Notices.Sweep();
        await listed.Notices.Undo(notice);

        Assert.IsTrue(notice.NamesAProblem);
        StringAssert.Contains(notice.Text, "clip.mp4");
        CollectionAssert.AreEqual(new[] { notice }, listed.Notices.Shown.ToList());
        Assert.IsFalse(listed.Notices.OffersUndo(notice));
        Assert.HasCount(4, await listed.Stash.All(), "an Undo pressed after its time removes nothing");
    }

    [TestMethod]
    public async Task AnImportNamingAnUnreadableFileStays()
    {
        using var listed = await ListedStash.Open();
        await listed.Import.Drop(ListedStash.Contents([MediaFixtures.Path("still.png"), listed.Stash.Source("broken.png", TestStash.Bytes(5))]));

        listed.Stash.Clock.Now += TimeSpan.FromMinutes(1);
        listed.Notices.Sweep();

        StringAssert.Contains(listed.Notices.Shown.Single().Text, "broken.png is unreadable");
    }

    [TestMethod]
    public async Task AnImportSayingOnlyAddedOrAlreadyInYourStashLeavesAtItsTime()
    {
        using var listed = await ListedStash.Open();
        await listed.Import.Drop(ListedStash.Contents([MediaFixtures.Path("still.png")]));
        await listed.Import.Drop(ListedStash.Contents([MediaFixtures.Path("still.png")]));
        StringAssert.Contains(listed.Notices.Shown[1].Text, "was already in your stash");
        Assert.IsFalse(listed.Notices.Shown.Any(notice => notice.NamesAProblem));

        listed.Stash.Clock.Now += Notices.LastingAtMost;
        listed.Notices.Sweep();

        Assert.IsEmpty(listed.Notices.Shown);
    }

    [TestMethod]
    public void AFileNamesAProblemWhenSkippedUnreadableOrFailedOrWhenItsResultCarriesAFailure()
    {
        var failure = new Failure("Tag", "cat", "the store refused it");

        Assert.IsFalse(new ImportedFile("cat.png", ImportResultKind.Added).NamesAProblem);
        Assert.IsFalse(new ImportedFile("cat.png", ImportResultKind.AlreadyInStash).NamesAProblem);
        Assert.IsTrue(new ImportedFile("cat.png", ImportResultKind.AlreadyInStash, Failure: failure).NamesAProblem);
        Assert.IsTrue(new ImportedFile("clip.mp4", ImportResultKind.Skipped, Type: "a video").NamesAProblem);
        Assert.IsTrue(new ImportedFile("broken.png", ImportResultKind.Unreadable).NamesAProblem);
        Assert.IsTrue(new ImportedFile("held.png", ImportResultKind.Failed, Failure: failure).NamesAProblem);
    }

    private static Task<IReadOnlyList<ImportedFile>> DropFourAndAClip(ListedStash listed) =>
        listed.Import.Drop(ListedStash.Contents([.. Four.Select(MediaFixtures.Path), listed.Stash.Source("clip.mp4", TestStash.Bytes(1))]));
}
