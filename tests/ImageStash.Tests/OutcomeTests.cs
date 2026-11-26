using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests;

/// <summary>
/// G35 and What crosses every boundary: a refusal names what was refused and why, as one sentence a person would say,
/// with no "failed" in it; its action, subject and reason all still show, so two refusals that differ in any one of them
/// read apart. A failure's sentence is the record's own, unchanged. Which one a record is, it says itself.
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
    public void AFailureKeepsItsSentenceAndTheRecordSaysWhichItIs()
    {
        var failed = new Failure("Rename", "cat", "its file is held by another program");
        var refused = failed with { Refused = true };

        Assert.AreEqual("Rename failed: cat, its file is held by another program", failed.Sentence);
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

        Assert.AreEqual($"Can't add to the stash: clip.gif is unreadable. Add to the stash failed: held.png, it is held. Added {Notices.Describe([item])}.", details);
    }
}
