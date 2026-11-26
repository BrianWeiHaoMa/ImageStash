using ImageStash.Files;
using ImageStash.Seams;
using ImageStash.Store;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests;

/// <summary>
/// G35 and What crosses every boundary: a refusal names what was refused and why, and a failure what could not be done and
/// why, each as one sentence a person would say, with no "failed" in it; its action, subject and reason all still show, so
/// two that differ in any one of them read apart. Which one a record is, it says itself.
/// </summary>
[TestClass]
public sealed class OutcomeTests
{
    [TestMethod]
    public void ARefusalOfOneSubjectReadsAsOneSentenceNamingTheActTheSubjectAndTheReason()
    {
        var refused = Failure.Refusal(ClipboardPaste.Action, "the clipboard", "it holds no picture or file");

        Assert.AreEqual("Can't paste: the clipboard holds no picture or file.", refused.Sentence);
        Assert.AreEqual("Can't add to the stash: clip.gif is unreadable.", Failure.Refusal(Import.Action, "clip.gif", "it is unreadable").Sentence);
        Assert.AreEqual("Can't paste: the clipboard's picture is gone.", Failure.Refusal(ClipboardPaste.Action, "the clipboard", "its picture is gone").Sentence);
    }

    [TestMethod]
    public void ARefusalOfSeveralSubjectsAgreesWithThem()
    {
        var refused = Failure.Refusal(Handoff.Action, "cat, dog and owl", "they are no longer in the stash");

        Assert.AreEqual("Can't copy: cat, dog and owl are no longer in the stash.", refused.Sentence);
    }

    [TestMethod]
    public void AReasonThatIsAClauseOfItsOwnKeepsTheSubjectNamedBesideTheAct()
    {
        var alone = Failure.Refusal(Screenshot.Action, "the screen", "your stash is still opening");
        var several = Failure.Refusal(Import.Action, "3 files", "your stash is being moved");

        Assert.AreEqual("Can't take a screenshot (the screen): your stash is still opening.", alone.Sentence);
        Assert.AreEqual("Can't add to the stash (3 files): your stash is being moved.", several.Sentence);
    }

    [TestMethod]
    public void ARefusalHasOneCapitalAtItsStartAndOnePeriodAtItsEnd()
    {
        foreach (var refused in new[]
        {
            Failure.Refusal(Import.Action, "cat.png", "it is unreadable."),
            Failure.Refusal(StashClipboardImage.Action, "the clipboard", "it holds no image"),
            Failure.Refusal(RecordClip.Action, "the screen", "the last screenshot is still being saved"),
        })
        {
            var sentence = refused.Sentence;
            Assert.IsTrue(sentence.StartsWith("Can't ", StringComparison.Ordinal), sentence);
            Assert.IsFalse(sentence[1..].Any(char.IsUpper), $"one capital, at the start: {sentence}");
            Assert.IsTrue(sentence.EndsWith('.') && !sentence.EndsWith("..", StringComparison.Ordinal), $"one closing period: {sentence}");
            Assert.IsFalse(sentence.Contains("failed", StringComparison.OrdinalIgnoreCase), sentence);
        }
    }

    [TestMethod]
    public void TwoRefusalsDifferingInOneWordReadApart()
    {
        var refused = Failure.Refusal(Handoff.Action, "cat", "it is no longer in the stash");

        Assert.AreNotEqual(refused.Sentence, Failure.Refusal(ClipboardPaste.Action, "cat", "it is no longer in the stash").Sentence, "the act");
        Assert.AreNotEqual(refused.Sentence, Failure.Refusal(Handoff.Action, "dog", "it is no longer in the stash").Sentence, "the subject");
        Assert.AreNotEqual(refused.Sentence, Failure.Refusal(Handoff.Action, "cat", "it is still being saved").Sentence, "the reason");
        Assert.AreNotEqual(refused.Sentence, Failure.Refusal(Handoff.Action, "cat", "your stash is still opening").Sentence);
        Assert.AreNotEqual(Failure.Refusal(Handoff.Action, "cat", "your stash is still opening").Sentence,
            Failure.Refusal(Handoff.Action, "dog", "your stash is still opening").Sentence, "the subject beside a reason of its own");
    }

    [TestMethod]
    public void AFailureAndARefusalOfOneRecordReadApartAndTheRecordSaysWhichItIs()
    {
        var failed = new Failure("Rename", "cat", "its file is held by another program");
        var refused = failed with { Refused = true };

        Assert.AreEqual("Couldn't rename: cat's file is held by another program.", failed.Sentence);
        Assert.IsFalse(failed.Refused);
        Assert.IsTrue(refused.Refused);
        Assert.AreNotEqual(failed, refused);
        Assert.AreEqual(failed.Reason, refused.Reason, "a reason compared by value still compares");
        Assert.AreEqual("Can't rename: cat's file is held by another program.", refused.Sentence);
    }

    [TestMethod]
    public void ARefusalInsideAnImportsNoticeEndsOnceBesideItsOtherLines()
    {
        var item = TestItems.Sized(7, 8, 6);
        var refused = new ImportedFile("clip.gif", ImportResultKind.Failed, Failure: Failure.Refusal(Import.Action, "clip.gif", "it is unreadable"));
        var held = new ImportedFile("held.png", ImportResultKind.Failed, Failure: new Failure(Import.Action, "held.png", "it is held"));
        var added = new ImportedFile("cat.png", ImportResultKind.Added, item);

        var (_, details) = Import.NoticeWords([refused, held, added]);

        Assert.AreEqual($"Can't add to the stash: clip.gif is unreadable. Couldn't add to the stash: held.png is held. Added {Notices.Describe([item])}.", details);
    }

    [TestMethod]
    public void EachKindOfActReadsAfterTheOpeningNamingTheActTheSubjectAndTheReasonOnce()
    {
        // A plain act on an item, its reason's "it" taken by the subject.
        Assert.AreEqual("Couldn't rename: cat.png is held open by another program. Try again in a moment.",
            new Failure("Rename", "cat.png", HeldFileRetry.HeldReason).Sentence);
        // Acts whose words already hold their object keep the subject beside them, never run into them.
        Assert.AreEqual(@"Couldn't move the stash (D:\Reactions): the disk is full.", new Failure(StashMove.Action, @"D:\Reactions", "the disk is full").Sentence);
        Assert.AreEqual("Couldn't save a copy: the file picker did not open (The parameter is incorrect).",
            new Failure(ItemMenu.SaveACopyAction, "the file picker", Failure.PlatformSaid("it did not open", "The parameter is incorrect.")).Sentence);
        // A finishing at start.
        Assert.AreEqual("Couldn't finish an unfinished delete: cat.png's folder does not exist.",
            new Failure("Finish an unfinished delete", "cat.png", HeldFileRetry.FolderMissingReason).Sentence);
        // Open with…, named for what it does.
        Assert.AreEqual("Couldn't open with another app (cat.png): Windows could not show the Open with dialog (Class not registered).",
            new Failure(OpenWith.Action, "cat.png", Failure.PlatformSaid("Windows could not show the Open with dialog", "Class not registered")).Sentence);
        // A seam's failure, as the Target seam words it.
        Assert.AreEqual("Couldn't paste: Relay runs with higher rights than ImageStash.",
            new Failure(PasteTargetReasons.Action, "Relay", PasteTargetReasons.HigherRights).Sentence);
        // Several subjects agree with their verb; a subject the platform gave none of reads without brackets.
        Assert.AreEqual("Couldn't copy: 2 items are no longer in the stash.", new Failure(Handoff.Action, "2 items", "they are no longer in the stash").Sentence);
        Assert.AreEqual("Couldn't show the window in parts: Windows would not give it that shape.", new Failure("Show the window in parts", "", "Windows would not give it that shape").Sentence);
    }

    [TestMethod]
    public void AFailureHasOneCapitalAtItsStartAndOnePeriodAtItsEnd()
    {
        foreach (var failed in new[]
        {
            new Failure(Import.Action, "cat.png", "the disk is full."),
            new Failure(Import.Action, "cat.png", "the disk is full. "),
            new Failure("Rename", "cat", HeldFileRetry.HeldReason),
            new Failure("Open the stash", "stash", Failure.PlatformSaid("it could not be opened", "Access is denied.")),
            new Failure(PasteTargetReasons.Action, "the editor field", "it closed before the paste could complete"),
        })
        {
            var sentence = failed.Sentence;
            Assert.IsTrue(sentence.StartsWith("Couldn't ", StringComparison.Ordinal), sentence);
            Assert.IsFalse(sentence[1..].Where(char.IsUpper).Except("TA").Any(), $"one capital, at the start, but for the next step and the platform's words: {sentence}");
            Assert.IsTrue(sentence.EndsWith('.') && !sentence.Contains("..", StringComparison.Ordinal) && !sentence.Contains(". .", StringComparison.Ordinal), $"one closing period: {sentence}");
            Assert.IsFalse(sentence.Contains("failed", StringComparison.OrdinalIgnoreCase), sentence);
        }
    }

    [TestMethod]
    public void AHeldFileSaysToTryAgainOnceAndNoOtherFailureGainsANextStep()
    {
        var held = new Failure("Rename", "cat.png", HeldFileRetry.HeldReason).Sentence;
        var composed = new Failure("Rename", "cat.png", $"its earlier rename cannot finish yet: {HeldFileRetry.HeldReason}").Sentence;

        Assert.IsTrue(held.EndsWith(" " + Failure.TryAgain, StringComparison.Ordinal), held);
        Assert.AreEqual(1, held.Split(Failure.TryAgain).Length - 1, held);
        Assert.AreEqual("Couldn't rename: cat.png's earlier rename cannot finish yet: it is held open by another program. Try again in a moment.", composed,
            "a held file named inside another reason still ends with the next step (G35)");
        foreach (var other in new[] { HeldFileRetry.MissingReason, "the disk is full", HeldClipboard.Reason, "Windows refused access to it" })
        {
            Assert.DoesNotContain(Failure.TryAgain, new Failure("Rename", "cat.png", other).Sentence, other);
        }

        Assert.DoesNotContain(Failure.TryAgain, Failure.Refusal("Rename", "cat.png", HeldFileRetry.HeldReason).Sentence, "a refusal gains no next step");
    }

    [TestMethod]
    public void APlatformsWordsComeAfterAPlainLeadInBracketsWithoutTheirPeriod()
    {
        Assert.AreEqual("it did not open (The operation was canceled by the user)", Failure.PlatformSaid("it did not open", " The operation was canceled by the user. \r\n"));
        Assert.AreEqual("Couldn't import files: the file picker did not open (The operation was canceled by the user).",
            new Failure("Import files", "the file picker", Failure.PlatformSaid("it did not open", "The operation was canceled by the user.")).Sentence);
    }

    [TestMethod]
    public void TwoFailuresDifferingInOneWordReadApartAndOneRepeatedReadsTheSame()
    {
        var failed = new Failure("Rename", "cat", HeldFileRetry.HeldReason);

        Assert.AreNotEqual(failed.Sentence, new Failure("Delete", "cat", HeldFileRetry.HeldReason).Sentence, "the act");
        Assert.AreNotEqual(failed.Sentence, new Failure("Rename", "dog", HeldFileRetry.HeldReason).Sentence, "the subject");
        Assert.AreNotEqual(failed.Sentence, new Failure("Rename", "cat", HeldFileRetry.MissingReason).Sentence, "the reason");
        Assert.AreNotEqual(new Failure("Rename", "cat", "the disk is full").Sentence, new Failure("Rename", "dog", "the disk is full").Sentence,
            "the subject beside a reason of its own");
        Assert.AreEqual(failed.Sentence, new Failure("Rename", "cat", HeldFileRetry.HeldReason).Sentence, "one repeated reads alike, so it is named once");
        Assert.AreNotEqual(failed.Sentence, (failed with { Refused = true }).Sentence, "a refusal of the same words reads apart");
    }

    [TestMethod]
    public void TheTooltipsShorterFormsSayTheActInTheSameVoice()
    {
        var failed = new Failure("Open the stash", @"C:\Reactions", "a file is where the stash folder should be");
        var refused = Failure.Refusal(StashClipboardImage.Action, "the clipboard", "it holds no image");

        CollectionAssert.AreEqual(new[] { failed.Sentence, @"Couldn't open the stash: C:\Reactions", "Couldn't open the stash" }, failed.ToolTipForms.ToArray());
        CollectionAssert.AreEqual(new[] { refused.Sentence, "Can't stash clipboard image: the clipboard", "Can't stash clipboard image" }, refused.ToolTipForms.ToArray());
        Assert.HasCount(Failure.ToolTipFormCount, failed.ToolTipForms);
        Assert.AreEqual("Couldn't open the stash, read settings or save settings",
            Failure.ToolTipActs([failed, new Failure("Read settings", "settings.json", "x"), new Failure("Save settings", "settings.json", "y"), new Failure("Read settings", "settings.json", "z")]),
            "one opening, each act once, newest first");
    }
}
