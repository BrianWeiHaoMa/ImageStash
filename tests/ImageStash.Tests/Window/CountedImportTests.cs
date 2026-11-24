using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Media;
using ImageStash.Files;
using ImageStash.Seams;
using ImageStash.Store;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// An import's notice over a real store (G12, G35): a first line counting what became of the files, then every file that
/// was not added named with its reason, and the added items by name only when they are three or fewer; the notice still
/// says it names a problem when it does, its time counts the first line with the rest (G16), and its card shows that line
/// semi-bold above the rest.
/// </summary>
[TestClass]
public sealed class CountedImportTests
{
    private const string Formats = "ImageStash keeps GIF, PNG, JPG and WebP";

    [TestMethod]
    public async Task FortyFilesAreCountedByKindAndEveryFileNotAddedIsNamedWithItsReasonAndNoAddedOne()
    {
        using var importing = await ListedStash.Open();
        await importing.Import.Drop(ListedStash.Contents([MediaFixtures.Path("animated.gif")]));
        var pictures = Enumerable.Range(1, 35).Select(i => importing.Stash.Source($"p{i:00}.png", MediaFixtures.DistinctPng(i))).ToList();
        var held = importing.Stash.Source("held.png", MediaFixtures.DistinctPng(77));
        var problems = new[]
        {
            MediaFixtures.Path("animated.gif"),
            importing.Stash.Source("clip.mp4", TestStash.Bytes(1)),
            importing.Stash.Source("cat.url", "[InternetShortcut]\r\nURL=https://example.com/cat.gif\r\n"u8.ToArray()),
            importing.Stash.Source("broken.png", "not a picture at all"u8.ToArray()),
            held,
        };

        using (new FileStream(held, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            await importing.Import.Drop(ListedStash.Contents([.. pictures.Take(20), .. problems, .. pictures.Skip(20)]));
        }

        var notice = importing.Notices.Shown[^1];
        Assert.AreEqual("Added 35 · 1 already in your stash · 2 skipped · 1 unreadable · 1 failed", notice.Heading);
        Assert.AreEqual(
            "animated.gif was already in your stash as “animated”. " +
            $"Skipped clip.mp4 (a video) and cat.url (a link): {Formats}. " +
            "broken.png is unreadable. " +
            $"Copy into the stash failed: held.png, {HeldFileRetry.HeldReason}.",
            notice.Text);
        Assert.IsFalse(Enumerable.Range(1, 35).Any(i => notice.Words.Contains($"p{i:00}")), "none of the added names");
        Assert.HasCount(36, await importing.Stash.All());
    }

    [TestMethod]
    public async Task TwoAddedAndOneSkippedNameTheSkippedFileBeforeTheAddedOnes()
    {
        using var importing = await ListedStash.Open();

        await importing.Import.Drop(ListedStash.Contents([MediaFixtures.Path("photo.jpg"), importing.Stash.Source("clip.mp4", TestStash.Bytes(1)), MediaFixtures.Path("transparent.png")]));

        var notice = importing.Notices.Shown.Single();
        Assert.AreEqual("Added 2 · 1 skipped", notice.Heading);
        Assert.AreEqual($"Skipped clip.mp4 (a video): {Formats}. Added “photo” and “transparent”.", notice.Text);
    }

    [TestMethod]
    public async Task ThreeAddedAreCountedFirstThenNamedAndFourAreCountedAlone()
    {
        using var importing = await ListedStash.Open();

        var three = await importing.Import.Drop(ListedStash.Contents(new[] { "photo.jpg", "transparent.png", "animated.gif" }.Select(MediaFixtures.Path).ToList()));
        var four = await importing.Import.Drop(ListedStash.Contents(Enumerable.Range(1, 4).Select(i => importing.Stash.Source($"p{i}.png", MediaFixtures.DistinctPng(i))).ToList()));

        var (named, counted) = (importing.Notices.Shown[0], importing.Notices.Shown[1]);
        Assert.AreEqual("Added 3 to your stash", named.Heading);
        Assert.AreEqual($"Added {Notices.Describe(three.Select(file => file.Item!).ToList())}.", named.Text, "named as a notice names items");
        Assert.AreEqual("Added “photo”, “transparent” and “animated”.", named.Text);
        Assert.AreEqual("Added 4 to your stash", counted.Heading);
        Assert.AreEqual("", counted.Text, "four are counted, not named");
        Assert.HasCount(4, four);
    }

    [TestMethod]
    public async Task NothingAddedAndTwoSkippedSaysNothingWasAddedAndNamesBoth()
    {
        using var importing = await ListedStash.Open();

        await importing.Import.Drop(ListedStash.Contents([importing.Stash.Source("clip.mp4", TestStash.Bytes(1)), importing.Stash.Source("notes.txt", TestStash.Bytes(2))]));

        var notice = importing.Notices.Shown.Single();
        Assert.AreEqual("Nothing was added · 2 skipped", notice.Heading);
        Assert.AreEqual($"Skipped clip.mp4 (a video) and notes.txt (a .txt file): {Formats}.", notice.Text);
    }

    [TestMethod]
    public async Task AnEmptyFolderKeepsItsSentence()
    {
        using var importing = await ListedStash.Open();
        var folder = importing.Stash.Outside("empty");
        Directory.CreateDirectory(folder);

        await importing.Import.Drop(new DropContents([new DroppedItem("empty", folder, null, IsFolder: true)], false, false, []));

        var notice = importing.Notices.Shown.Single();
        Assert.AreEqual("Nothing was added: there was no file in what was dropped.", notice.Heading);
        Assert.AreEqual("", notice.Text);
    }

    [TestMethod]
    public void ADuplicateCarryingAFailureIsCountedOnceAndItsFailureNamed()
    {
        var item = TestItems.Sized(7, 8, 6) with { Name = "again" };
        var discard = new Failure("Add to the stash", "again.png", HeldFileRetry.HeldReason);
        var found = ImportedFile.FromCommit("again.png", Outcome<ImportResult>.FailAfterCommit(discard, new ImportResult(item, AlreadyInStash: true)));

        var (heading, details) = Import.NoticeWords([found]);

        Assert.AreEqual("Nothing was added · 1 already in your stash", heading);
        Assert.AreEqual($"again.png was already in your stash as “again”. Add to the stash failed: again.png, {HeldFileRetry.HeldReason}.", details);
        Assert.IsTrue(found.NamesAProblem, "its failure is a problem the notice names");
    }

    [TestMethod]
    public async Task AFileWhoseLastStepFailedIsCountedAndNamedAsFailedAndItsUndoRemovesIt()
    {
        using var importing = await ListedStash.Open();
        var older = await importing.Stash.Import("older");
        // The rename into the root done and the transaction that clears its step refused, as a refused write leaves it.
        importing.Stash.SqlWhileOpen(
            "CREATE TRIGGER refuse_clearing BEFORE UPDATE OF pending_steps ON items WHEN NEW.pending_steps IS NULL BEGIN SELECT RAISE(ABORT, 'the write was refused'); END");

        await importing.Import.Drop(ListedStash.Contents([MediaFixtures.Path("still.png"), MediaFixtures.Path("photo.jpg")]));

        var notice = importing.Notices.Shown.Single();
        Assert.HasCount(3, await importing.Stash.All(), "their pictures are in place, so the stash shows them (State: A live failure)");
        Assert.AreEqual("Undo", notice.UndoLabel, "the batch put them in the stash, so Undo removes them (G12)");
        Assert.AreEqual("Nothing was added · 2 failed", notice.Heading, "no success shown before the last step (R9)");
        StringAssert.StartsWith(notice.Text, "still.png: Add to the stash failed: ");
        StringAssert.Contains(notice.Text, ". photo.jpg: Add to the stash failed: ", "each named with its failure");

        await importing.Notices.Undo(notice);

        CollectionAssert.AreEqual(new[] { older.Id }, (await importing.Stash.All()).Select(item => item.Id).ToList(), "the batch, and nothing else");
    }

    [TestMethod]
    public async Task ANoticeNamingASkippedFileStaysWithoutItsUndoAndOneOfAddedAndDuplicateLeavesAtItsTimeCountingItsFirstLine()
    {
        using var importing = await ListedStash.Open();
        await importing.Import.Drop(ListedStash.Contents([MediaFixtures.Path("animated.gif")]));
        importing.Notices.Close(importing.Notices.Shown.Single());
        await importing.Import.Drop(ListedStash.Contents([MediaFixtures.Path("photo.jpg"), importing.Stash.Source("clip.mp4", TestStash.Bytes(1))]));
        var skipping = importing.Notices.Shown.Single();
        Pass(importing, TimeSpan.FromMinutes(1));

        Assert.AreSame(skipping, importing.Notices.Shown.Single(), "it names a problem, so it stays");
        Assert.IsFalse(importing.Notices.OffersUndo(skipping), "its Undo ended with its time");

        importing.Notices.Close(skipping);
        await importing.Import.Drop(ListedStash.Contents(new[] { "animated.gif", "transparent.png", "still.png", "still.gif" }.Select(MediaFixtures.Path).ToList()));
        var plain = importing.Notices.Shown.Single();
        var lasting = Notices.LastingFor(plain.Words);
        Assert.AreEqual($"{plain.Heading} {plain.Text}", plain.Words, "the first line counted with the rest, once");
        Assert.IsGreaterThan(Notices.LastingFor(plain.Text), lasting, "the first line's words lengthen its time");

        Pass(importing, lasting - TimeSpan.FromMilliseconds(1));
        Assert.AreSame(plain, importing.Notices.Shown.Single());
        Pass(importing, TimeSpan.FromMilliseconds(1));
        Assert.IsEmpty(importing.Notices.Shown, "added and already in the stash name no problem");
    }

    [TestMethod]
    public async Task TheCardShowsTheFirstLineSemiBoldAboveTheRestAndKeepsBothOnceItsUndoHasEnded()
    {
        using var importing = await ListedStash.Open();
        await importing.Import.Drop(ListedStash.Contents([MediaFixtures.Path("photo.jpg"), importing.Stash.Source("clip.mp4", TestStash.Bytes(1))]));
        var notice = importing.Notices.Shown.Single();
        var plain = importing.Notices.Raise("Deleted “cat”.", "Undo", () => Task.FromResult(Outcome.Done));

        ControlThread.Run(() =>
        {
            var card = NoticeCard.For(notice, importing.Notices);
            Assert.AreEqual("Undo (Ctrl+Z)", ToolTip.GetTip(Undo(card)), "the window's Undo names its key");
            Pass(importing, TimeSpan.FromMinutes(1));

            var texts = card.GetLogicalDescendants().OfType<TextBlock>().Where(Words).ToList();
            CollectionAssert.AreEqual(new[] { notice.Heading, notice.Text }, texts.Select(text => text.Text).ToList(), "the first line above the rest, both kept");
            Assert.AreEqual(FontWeight.SemiBold, texts[0].FontWeight);
            Assert.AreNotEqual(FontWeight.SemiBold, texts[1].FontWeight);
            Assert.IsFalse(texts[0].GetLogicalAncestors().OfType<ScrollViewer>().Any(), "the first line stays in view");
            Assert.AreEqual(120, texts[1].GetLogicalAncestors().OfType<ScrollViewer>().Single().MaxHeight, "the rest scrolls inside the card");
            Assert.IsFalse(Undo(card).IsVisible, "no Undo once it has ended");

            var other = NoticeCard.For(plain, importing.Notices);
            var otherTexts = other.GetLogicalDescendants().OfType<TextBlock>().Where(text => !text.GetLogicalAncestors().OfType<Button>().Any()).ToList();
            Assert.AreEqual(plain.Text, otherTexts.Single().Text, "another notice gains no first line, not even an empty one");
            Assert.AreNotEqual(FontWeight.SemiBold, otherTexts.Single().FontWeight);
        });
    }

    [TestMethod]
    public async Task ANotificationAboveTheTrayNamesNoKeyOnItsUndoAndGainsNoFirstLine()
    {
        using var importing = await ListedStash.Open();
        var capture = importing.Notifications.Raise("Screenshot stashed: “cat”.", "Undo", () => Task.FromResult(Outcome.Done));

        ControlThread.Run(() =>
        {
            var card = NoticeCard.For(capture, importing.Notifications);
            Assert.IsNull(ToolTip.GetTip(Undo(card)));
            Assert.AreEqual(capture.Text, card.GetLogicalDescendants().OfType<TextBlock>().Single(text => !text.GetLogicalAncestors().OfType<Button>().Any()).Text, "no first line, not even an empty one");
        });
    }

    /// <summary>A text block reading out the notice's words, not a button's glyph.</summary>
    private static bool Words(TextBlock text) => text.Text is { Length: > 0 } && !text.GetLogicalAncestors().OfType<Button>().Any();

    private static Button Undo(Control card) => card.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Content, "Undo"));

    /// <summary>Time the window shows passing, the sweep run as the window's timer runs it.</summary>
    private static void Pass(ListedStash listed, TimeSpan shown)
    {
        listed.Stash.Clock.Now += shown;
        listed.Notices.Sweep();
    }
}
