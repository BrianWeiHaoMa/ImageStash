using ImageStash.Media;
using ImageStash.Seams;
using ImageStash.Store;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// Paste image and <c>Ctrl+V</c> over a real stash, the clipboard preset (G13): its files imported as a drop's would be
/// (G12, N4); else its picture one still PNG item named like a screenshot in local time (F12, F3), saved even when equal
/// to an item, with G13's sentence and Undo; else, from the search box, its words into the box; else a notice for nothing
/// and a refusal for a link (N2); the tag lit at the paste (G11); the clipboard read once per paste and, but for the
/// box's, never while the stash cannot take it (N9, G35); a held clipboard
/// and an unreadable picture named (E3, G35); an import's end announced only when the paste imported (G12).
/// </summary>
[TestClass]
public sealed class ClipboardPasteTests
{
    private const string StillOnly = "A pasted picture is always a still; to add an animated one, drag its file in.";

    [TestMethod]
    public async Task APastedPngIsOneItemNamedLikeAScreenshotInLocalTimeKeepingTheClipboardsBytes()
    {
        using var pasting = await ListedStash.Open(SetClock.Ahead);
        var older = await pasting.Stash.Import("older");
        await pasting.Listing.SetQuery("older");
        var png = MediaFixtures.Bytes("transparent.png");
        pasting.Clipboard.Reading = Picture(png, null);

        await pasting.Paste.Paste();

        var item = (await pasting.Stash.All()).Single(each => each.Id != older.Id);
        Assert.AreEqual("Screenshot 2026-10-16 17.30.00", item.Name, "12:00 UTC in local time, no extension in the name (F12)");
        Assert.AreEqual("Screenshot 2026-10-16 17.30.00.png", item.FileName);
        CollectionAssert.AreEqual(png, File.ReadAllBytes(item.FilePath), "a PNG's bytes as they are");
        Assert.AreEqual(new MediaFacts(ImageFormat.Png, 8, 6, false, 1, 0), item.Facts);
        Assert.AreEqual("", pasting.Listing.Query, "the search cleared (G12)");
        Assert.AreEqual(Scope.All, pasting.Listing.LitChip);
        CollectionAssert.AreEqual(new[] { item.Id, older.Id }, pasting.Listing.Items.Select(each => each.Id).ToList(), "the new item first");
        CollectionAssert.AreEqual(new[] { item.Id }, pasting.Listing.Selection.ToList(), "and selected");
        var notice = pasting.Notices.Shown.Single();
        Assert.AreEqual($"Added 1 to your stash: Screenshot 2026-10-16 17.30.00. {StillOnly}", notice.Text);
        Assert.AreEqual("Undo", notice.UndoLabel);
        Assert.IsEmpty(pasting.Stash.WorkFiles());
        Assert.AreEqual(1, pasting.Clipboard.Reads, "one read for one paste");
        Assert.IsEmpty(pasting.Failures);
    }

    [TestMethod]
    public async Task TheSamePngPastedTwiceInOneSecondIsTwoItemsOfOneNameAndTheSecondsUndoRemovesOnlyIt()
    {
        using var pasting = await ListedStash.Open(SetClock.Ahead);
        pasting.Clipboard.Reading = Picture(MediaFixtures.Bytes("transparent.png"), null);

        await pasting.Paste.Paste();
        await pasting.Paste.Paste();

        var both = (await pasting.Stash.All()).OrderBy(item => item.Id).ToList();
        CollectionAssert.AreEqual(new[] { "Screenshot 2026-10-16 17.30.00", "Screenshot 2026-10-16 17.30.00" }, both.Select(item => item.Name).ToList(), "saved even though its bytes equal an item's");
        CollectionAssert.AreEqual(new[] { "Screenshot 2026-10-16 17.30.00.png", "Screenshot 2026-10-16 17.30.00 2.png" }, both.Select(item => item.FileName).ToList());

        await pasting.Notices.Undo(pasting.Notices.Shown[^1]);

        CollectionAssert.AreEqual(new[] { both[0].Id }, (await pasting.Stash.All()).Select(item => item.Id).ToList());
        CollectionAssert.AreEqual(new[] { both[1].FilePath }, pasting.Stash.RecycleBin.Asked, "its file to the Recycle Bin (G29)");
        Assert.IsTrue(File.Exists(both[0].FilePath));
    }

    [TestMethod]
    public async Task ABitmapIsEncodedAsAPngWithItsAlphaAndTwiceInOneSecondIsTwoItems()
    {
        using var pasting = await ListedStash.Open(SetClock.Ahead);
        // BGRA, straight alpha: opaque red, half-transparent blue, fully transparent, opaque white, translucent green, opaque black.
        byte[] pixels = [0, 0, 255, 255, 255, 0, 0, 128, 0, 0, 0, 0, 255, 255, 255, 255, 0, 200, 0, 64, 0, 0, 0, 255];
        pasting.Clipboard.Reading = Picture(null, new MediaFrame(3, 2, 0, pixels));

        await pasting.Paste.Paste();
        await pasting.Paste.Paste();

        var both = (await pasting.Stash.All()).OrderBy(item => item.Id).ToList();
        CollectionAssert.AreEqual(new[] { "Screenshot 2026-10-16 17.30.00.png", "Screenshot 2026-10-16 17.30.00 2.png" }, both.Select(item => item.FileName).ToList());
        Assert.AreEqual(new MediaFacts(ImageFormat.Png, 3, 2, false, 1, 0), both[0].Facts);
        CollectionAssert.AreEqual(pixels, MediaFixtures.FirstFrame(both[0].FilePath).Pixels, "the stored PNG holds the bitmap's pixels, alpha included");
        Assert.AreEqual($"Added 1 to your stash: Screenshot 2026-10-16 17.30.00. {StillOnly}", pasting.Notices.Shown[^1].Text);

        await pasting.Notices.Undo(pasting.Notices.Shown[^1]);

        CollectionAssert.AreEqual(new[] { both[0].Id }, (await pasting.Stash.All()).Select(item => item.Id).ToList());
    }

    [TestMethod]
    public async Task GifBytesAProgramLabelledPngBecomeAStillPngOfTheirFirstFrame()
    {
        using var pasting = await ListedStash.Open(SetClock.Ahead);
        pasting.Clipboard.Reading = Picture(MediaFixtures.Bytes("animated.gif"), null);

        await pasting.Paste.Paste();

        var item = (await pasting.Stash.All()).Single();
        Assert.AreEqual("Screenshot 2026-10-16 17.30.00.png", item.FileName, "a still PNG whatever the label (G13, F3)");
        Assert.AreEqual(ImageFormat.Png, item.Facts.Format);
        Assert.IsFalse(item.Facts.Animated);
        CollectionAssert.AreEqual(MediaFixtures.FirstFrame(MediaFixtures.Path("animated.gif")).Pixels, MediaFixtures.FirstFrame(item.FilePath).Pixels, "the GIF's first frame");
        Assert.IsEmpty(pasting.Stash.WorkFiles(), "neither staged copy is left");
        Assert.AreEqual($"Added 1 to your stash: Screenshot 2026-10-16 17.30.00. {StillOnly}", pasting.Notices.Shown.Single().Text);
    }

    [TestMethod]
    public async Task BytesThatAreNoPictureAreUnreadableByTheirNameAndLeaveNothing()
    {
        using var pasting = await ListedStash.Open(SetClock.Ahead);
        await pasting.Listing.SetQuery("kept");
        pasting.Clipboard.Reading = Picture([0x89, 0x50, 0x4E, 0x47, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16], null);

        await pasting.Paste.Paste();

        Assert.AreEqual($"Nothing was added. Screenshot 2026-10-16 17.30.00 is unreadable. {StillOnly}", pasting.Notices.Shown.Single().Text);
        Assert.IsNull(pasting.Notices.Shown.Single().UndoLabel);
        Assert.IsEmpty(pasting.Stash.RootFiles());
        Assert.IsEmpty(pasting.Stash.WorkFiles());
        Assert.IsEmpty(await pasting.Stash.All());
    }

    [TestMethod]
    public async Task ClipboardFilesAreImportedAsADropsWouldAndTheSameFileAgainIsAlreadyInTheStash()
    {
        using var pasting = await ListedStash.Open(SetClock.Ahead);
        var gif = MediaFixtures.Path("animated.gif");
        var video = pasting.Stash.Source("clip.mp4", TestStash.Bytes(1));
        var link = pasting.Stash.Source("cat.url", "[InternetShortcut]\r\nURL=https://example.com/cat.gif\r\n"u8.ToArray());
        pasting.Clipboard.Reading = Files(gif, video, link);
        var busy = new List<string?>();
        pasting.Import.Changed += () => busy.Add(pasting.Import.Busy);

        await pasting.Paste.Paste();

        var item = (await pasting.Stash.All()).Single();
        Assert.AreEqual("animated", item.Name, "named after its file (F12)");
        Assert.AreEqual("animated.gif", item.FileName, "the file it came as (F3)");
        CollectionAssert.AreEqual(MediaFixtures.Bytes("animated.gif"), File.ReadAllBytes(item.FilePath));
        Assert.AreEqual(
            "Added 1 to your stash: animated.gif. Skipped clip.mp4 (a video) and cat.url (a link): ImageStash keeps GIF, PNG, JPG and WebP.",
            pasting.Notices.Shown.Single().Text,
            "the drop's notice, without the pasted picture's sentence");
        Assert.AreEqual("Undo", pasting.Notices.Shown.Single().UndoLabel);
        Assert.Contains("Adding 1 of 3 to your stash…", busy, "the batch's busy state");
        CollectionAssert.AreEqual(new[] { item.Id }, pasting.Listing.Selection.ToList());

        pasting.Clipboard.Reading = Files(gif);
        await pasting.Paste.Paste();

        Assert.AreEqual("Nothing was added. animated.gif was already in your stash as “animated”.", pasting.Notices.Shown[^1].Text, "G12's question for a file");
        Assert.HasCount(1, await pasting.Stash.All());
    }

    [TestMethod]
    public async Task TheTagLitAtThePasteGoesOnEveryItemItAddsWhateverIsLitWhenTheReadReturns()
    {
        using var pasting = await ListedStash.Open(SetClock.Ahead);
        pasting.Stash.Tag((await pasting.Stash.Import("tagged")).Id, "reaction");
        pasting.Stash.Tag((await pasting.Stash.Import("other")).Id, "other");

        await pasting.Listing.SetQuery("reaction");
        pasting.Clipboard.Reading = Files(MediaFixtures.Path("still.png"));
        await pasting.Paste.Paste();
        await pasting.Listing.SetQuery("#reaction");
        pasting.Clipboard.Reading = Picture(MediaFixtures.Bytes("transparent.png"), null);
        await pasting.Paste.Paste();

        // The read waits; meanwhile another chip is lit.
        await pasting.Listing.SetQuery("reaction");
        var held = new TaskCompletionSource();
        pasting.Clipboard.HoldRead = held.Task;
        pasting.Clipboard.Reading = Picture(MediaFixtures.Bytes("still.png"), null);
        var waiting = pasting.Paste.Paste();
        await pasting.Listing.SetQuery("other");
        held.SetResult();
        await waiting;

        var added = (await pasting.Stash.All()).Where(item => item.Name is not ("tagged" or "other")).OrderBy(item => item.Id).ToList();
        Assert.HasCount(3, added);
        Assert.IsTrue(added.All(item => item.Tags.SequenceEqual(["reaction"])), string.Join("; ", added.Select(item => $"{item.Name}: {string.Join(",", item.Tags)}")));
    }

    [TestMethod]
    public async Task APasteQueuedBehindABatchKeepsTheTagLitAtThePressThoughThatBatchsEndClearedTheSearch()
    {
        using var pasting = await ListedStash.Open(SetClock.Ahead);
        pasting.Stash.Tag((await pasting.Stash.Import("tagged")).Id, "reaction");
        var twenty = Enumerable.Range(1, 20).Select(i => pasting.Stash.Source($"q{i:00}.png", MediaFixtures.DistinctPng(i))).ToList();
        await pasting.Listing.SetQuery("reaction");
        pasting.Clipboard.Reading = Picture(MediaFixtures.Bytes("transparent.png"), null);

        var batch = pasting.Import.Drop(ListedStash.Contents(twenty));
        var paste = pasting.Paste.Paste();
        await Task.WhenAll(batch, paste);

        var pasted = (await pasting.Stash.All()).Single(item => item.Name.StartsWith("Screenshot ", StringComparison.Ordinal));
        CollectionAssert.AreEqual(new[] { "reaction" }, pasted.Tags.ToList(), "the tag lit at the press (G11)");
        Assert.AreEqual(pasted.Id, pasting.Listing.Items[0].Id, "the paste's batch ran after the drop's");
    }

    [TestMethod]
    public async Task ALinkIsRefusedByNameAndAnEmptyClipboardSaysItHoldsNoPictureOrFile()
    {
        using var pasting = await ListedStash.Open(SetClock.Ahead);
        await pasting.Listing.SetQuery("kept");
        var ended = 0;
        pasting.Import.Finishing += () => ended++;

        pasting.Clipboard.Reading = Outcome<ClipboardReading>.Of(new ClipboardReading([], null, null, OnlyLink: true));
        await pasting.Paste.Paste();
        pasting.Clipboard.Reading = Outcome<ClipboardReading>.Of(new ClipboardReading([], null, null, false));
        await pasting.Paste.Paste();

        CollectionAssert.AreEqual(
            new[]
            {
                "Paste failed: the link, links are not taken and nothing is downloaded; copy the picture itself",
                "Paste failed: the clipboard, it holds no picture or file",
            },
            pasting.Notices.Shown.Select(notice => notice.Text).ToList());
        Assert.AreEqual("kept", pasting.Listing.Query, "nothing was imported, so nothing was reset");
        Assert.AreEqual(0, ended, "no import ended, so a preview over the grid stays");
        Assert.IsEmpty(await pasting.Stash.All());
        Assert.IsEmpty(pasting.Stash.WorkFiles());
    }

    [TestMethod]
    public async Task WordsOnTheClipboardGoIntoTheSearchBoxOrATagFieldFromItsPasteWithNoNoticeAndNothingAdded()
    {
        using var pasting = await ListedStash.Open(SetClock.Ahead);
        pasting.Clipboard.Reading = Words("hello there");
        var ended = 0;
        pasting.Import.Finishing += () => ended++;
        var box = new List<string>();

        await pasting.Paste.Paste(box.Add);

        CollectionAssert.AreEqual(new[] { "hello there" }, box, "G13: as the clipboard holds them");
        Assert.IsEmpty(pasting.Notices.Shown);
        Assert.IsEmpty(await pasting.Stash.All());
        Assert.AreEqual(0, ended, "no import, so nothing about the window changes");
        Assert.AreEqual(1, pasting.Clipboard.Reads, "the paste's one read carries them (N9, R5)");
    }

    [TestMethod]
    public async Task WordsPastedAnywhereButTheSearchBoxOrATagFieldGetTheNoticeAndGoNowhere()
    {
        using var pasting = await ListedStash.Open(SetClock.Ahead);
        pasting.Clipboard.Reading = Words("hello");

        await pasting.Paste.Paste();

        Assert.AreEqual("Paste failed: the clipboard, it holds no picture or file", pasting.Notices.Shown.Single().Text);
        Assert.IsEmpty(await pasting.Stash.All());
    }

    [TestMethod]
    public async Task FilesOrAPictureBesideWordsAreImportedFromTheSearchBoxAndTheWordsGoNowhere()
    {
        using var pasting = await ListedStash.Open(SetClock.Ahead);
        var box = new List<string>();

        pasting.Clipboard.Reading = Outcome<ClipboardReading>.Of(new ClipboardReading([ListedStash.File(MediaFixtures.Path("still.png"))], null, null, false, "words"));
        await pasting.Paste.Paste(box.Add);
        pasting.Clipboard.Reading = Outcome<ClipboardReading>.Of(new ClipboardReading([], MediaFixtures.Bytes("transparent.png"), null, false, "words"));
        await pasting.Paste.Paste(box.Add);

        Assert.HasCount(2, await pasting.Stash.All(), "G13: an import, as from anywhere else");
        Assert.IsEmpty(box);
    }

    [TestMethod]
    public async Task AnEmptyClipboardStillGetsItsNoticeFromTheSearchBox()
    {
        using var pasting = await ListedStash.Open(SetClock.Ahead);
        var box = new List<string>();

        pasting.Clipboard.Reading = Outcome<ClipboardReading>.Of(new ClipboardReading([], null, null, false));
        await pasting.Paste.Paste(box.Add);
        pasting.Clipboard.Reading = Words("");
        await pasting.Paste.Paste(box.Add);

        Assert.IsEmpty(box);
        CollectionAssert.AreEqual(
            new[] { "Paste failed: the clipboard, it holds no picture or file", "Paste failed: the clipboard, it holds no picture or file" },
            pasting.Notices.Shown.Select(notice => notice.Text).ToList());
    }

    [TestMethod]
    public async Task ACopiedAddressIsWordsInTheSearchBoxAndARefusedLinkAnywhereElseWithNothingFetched()
    {
        using var pasting = await ListedStash.Open(SetClock.Ahead);
        pasting.Clipboard.Reading = Outcome<ClipboardReading>.Of(new ClipboardReading([], null, null, OnlyLink: true, "https://example.com/cat.gif"));
        var box = new List<string>();

        await pasting.Paste.Paste(box.Add);
        await pasting.Paste.Paste();

        CollectionAssert.AreEqual(new[] { "https://example.com/cat.gif" }, box);
        Assert.AreEqual("Paste failed: the link, links are not taken and nothing is downloaded; copy the picture itself", pasting.Notices.Shown.Single().Text);
        Assert.IsEmpty(await pasting.Stash.All());
    }

    [TestMethod]
    public async Task AClipboardAnotherProgramHoldsIsNamedAndTheNextPasteWorks()
    {
        using var pasting = await ListedStash.Open(SetClock.Ahead);
        pasting.Clipboard.Reading = new Failure(ClipboardPaste.Action, "the clipboard", HeldClipboard.Reason);

        await pasting.Paste.Paste();
        pasting.Clipboard.Reading = Picture(MediaFixtures.Bytes("transparent.png"), null);
        await pasting.Paste.Paste();

        Assert.AreEqual("Paste failed: the clipboard, another program is holding the clipboard open", pasting.Notices.Shown[0].Text);
        Assert.HasCount(1, await pasting.Stash.All(), "only the second paste added");
    }

    [TestMethod]
    public async Task WhileTheReadWaitsTheBusyStateNamesThePasteAndADropStillRuns()
    {
        using var pasting = await ListedStash.Open(SetClock.Ahead);
        var held = new TaskCompletionSource();
        pasting.Clipboard.HoldRead = held.Task;
        pasting.Clipboard.Reading = Picture(MediaFixtures.Bytes("transparent.png"), null);
        var changes = 0;
        pasting.Paste.Changed += () => changes++;

        var waiting = pasting.Paste.Paste();
        Assert.AreEqual("Pasting from the clipboard…", pasting.Paste.Busy);
        var dropped = await pasting.Import.Drop(ListedStash.Contents([MediaFixtures.Path("still.png")]));
        Assert.AreEqual(ImportResultKind.Added, dropped.Single().Result, "the window goes on while the read waits");
        Assert.AreEqual("Pasting from the clipboard…", pasting.Paste.Busy);
        held.SetResult();
        await waiting;

        Assert.IsNull(pasting.Paste.Busy);
        Assert.AreEqual(2, changes);
        Assert.HasCount(2, await pasting.Stash.All());
    }

    [TestMethod]
    public async Task NothingIsReadWhileTheStashIsStillOpeningOrDidNotOpen()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var notices = new Notices(stash.Clock);
        var opening = new Listing(_ => { });
        var clipboard = new RecordingClipboard(stash.Clock) { Reading = Picture(MediaFixtures.Bytes("transparent.png"), null) };
        var paste = new ClipboardPaste(opening, notices, new Import(opening, notices, TestStash.ShortRetry), clipboard, stash.Clock, TestStash.ShortRetry);

        await paste.Paste();
        opening.StashFailed(new Failure("Open the stash", "stash.db", "it is broken"));
        await paste.Paste();

        CollectionAssert.AreEqual(
            new[]
            {
                "Add to the stash failed: the clipboard, your stash is still opening",
                "Add to the stash failed: the clipboard, your stash could not be opened",
            },
            notices.Shown.Select(notice => notice.Text).ToList());
        Assert.AreEqual(0, clipboard.Reads, "nothing could be added, so nothing is read (N9)");
        Assert.IsEmpty(stash.WorkFiles());
        Assert.IsEmpty(await stash.All());
    }

    [TestMethod]
    public async Task TheSearchBoxsPasteTakesWordsWhileTheStashCannotTakeAnItemAndRefusesAPictureByName()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var notices = new Notices(stash.Clock);
        var opening = new Listing(_ => { });
        var clipboard = new RecordingClipboard(stash.Clock) { Reading = Words("hello") };
        var paste = new ClipboardPaste(opening, notices, new Import(opening, notices, TestStash.ShortRetry), clipboard, stash.Clock, TestStash.ShortRetry);
        var box = new List<string>();

        await paste.Paste(box.Add);

        CollectionAssert.AreEqual(new[] { "hello" }, box, "G13: the words need no stash");
        Assert.IsEmpty(notices.Shown);

        clipboard.Reading = Picture(MediaFixtures.Bytes("transparent.png"), null);
        await paste.Paste(box.Add);

        Assert.AreEqual("Add to the stash failed: the clipboard, your stash is still opening", notices.Shown.Single().Text);
        Assert.HasCount(1, box);
        Assert.IsEmpty(stash.WorkFiles());
    }

    [TestMethod]
    public async Task AnImportsEndIsAnnouncedForAPastedPictureAndForPastedFiles()
    {
        using var pasting = await ListedStash.Open(SetClock.Ahead);
        var ended = 0;
        pasting.Import.Finishing += () => ended++;

        pasting.Clipboard.Reading = Picture(MediaFixtures.Bytes("transparent.png"), null);
        await pasting.Paste.Paste();
        pasting.Clipboard.Reading = Files(MediaFixtures.Path("still.png"));
        await pasting.Paste.Paste();

        Assert.AreEqual(2, ended, "so a preview over the grid closes before All shows the new item (G12)");
    }

    private static Outcome<ClipboardReading> Picture(byte[]? png, MediaFrame? image) =>
        Outcome<ClipboardReading>.Of(new ClipboardReading([], png, image, false));

    private static Outcome<ClipboardReading> Words(string text) =>
        Outcome<ClipboardReading>.Of(new ClipboardReading([], null, null, false, text));

    private static Outcome<ClipboardReading> Files(params string[] paths) =>
        Outcome<ClipboardReading>.Of(new ClipboardReading(paths.Select(ListedStash.File).ToList(), null, null, false));
}
