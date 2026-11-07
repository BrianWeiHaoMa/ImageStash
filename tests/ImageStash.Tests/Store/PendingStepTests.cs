using ImageStash.Media;
using ImageStash.Store;
using ImageStash.Tests.Support;

namespace ImageStash.Tests.Store;

/// <summary>
/// A pending step's JSON (F11): an import's step is written as earlier builds wrote it, a rename's step carries the
/// previous name and nothing else, and a step an earlier build wrote reads with no previous name; a Save's, a
/// Revert's and an Open with… adoption's kinds, each with its text, its done rule judged in the folder it writes into,
/// whether its file is in .work\, and the names it keeps taken.
/// </summary>
[TestClass]
public sealed class PendingStepTests
{
    [TestMethod]
    public void AnAdoptionsKindsAreDoneByWhatTheirFoldersHoldAndThePlaceCarriesTheAdoptedFactsAndHash()
    {
        using var scratch = new ScratchFolder();
        var folder = new StashFolder(scratch.Path);
        Assert.IsTrue(folder.Create().Succeeded);
        var hold = new PendingStep(PendingStep.HoldInWork, "cat.png", "0h.png");
        var remove = new PendingStep(PendingStep.RemoveRender, "cat.png", "0a.png");
        var place = new PendingStep(PendingStep.PlaceAdopted, "0a.png", "cat.png", Facts: new MediaFacts(ImageFormat.Png, 12, 10, false, 1, 0), Sha256: "AB12");
        File.WriteAllBytes(folder.InRoot("cat.png"), [1]);
        File.WriteAllBytes(folder.InWork("0a.png"), [2]);

        Assert.AreEqual((false, false, false), (hold.IsDone(folder), remove.IsDone(folder), place.IsDone(folder)));

        File.Move(folder.InRoot("cat.png"), folder.InWork("0h.png"));

        Assert.AreEqual((true, true, false), (hold.IsDone(folder), remove.IsDone(folder), place.IsDone(folder)), "the previous picture has left the root");

        File.Move(folder.InWork("0a.png"), folder.InRoot("cat.png"));

        Assert.AreEqual((true, true, true), (hold.IsDone(folder), remove.IsDone(folder), place.IsDone(folder)));
        Assert.IsTrue(remove.IsDone(folder), "landed under the render's own name, the adopted file is no render still to delete");
        Assert.AreEqual((false, false, true), (hold.NamesWorkFile, remove.NamesWorkFile, place.NamesWorkFile), "the record names the held file; the place names the adopted one");
        Assert.IsFalse(hold.ListsWhilePending || remove.ListsWhilePending || place.ListsWhilePending, "the row's listing is judged over its steps together");
        Assert.AreEqual((folder.InRoot("cat.png"), folder.InWork("0h.png")), (hold.FromPath(folder), hold.ToPath(folder)));
        Assert.AreEqual((folder.InRoot("cat.png"), folder.InWork("0a.png")), (remove.FromPath(folder), remove.ToPath(folder)));
        Assert.AreEqual((folder.InWork("0a.png"), folder.InRoot("cat.png")), (place.FromPath(folder), place.ToPath(folder)));
        CollectionAssert.AreEqual(new[] { "cat.png", "cat.png", "cat.png" }, new[] { hold, remove, place }.SelectMany(step => step.NamesIn(folder, folder.Root)).ToList());
        Assert.IsEmpty(new[] { hold, remove, place }.SelectMany(step => step.NamesIn(folder, folder.Originals)));

        var json = PendingStep.Encode([hold, remove, place]);

        Assert.AreEqual(
            """
            [{"step":"hold in work","from":"cat.png","to":"0h.png"},{"step":"remove render","from":"cat.png","to":"0a.png"},{"step":"place adopted","from":"0a.png","to":"cat.png","facts":{"format":"Png","width":12,"height":10,"animated":false,"frameCount":1,"durationMs":0},"sha256":"AB12"}]
            """,
            json);
        CollectionAssert.AreEqual(new[] { hold, remove, place }, PendingStep.Decode(json).ToList());
    }

    [TestMethod]
    public void AnAdoptionsRenderRemovalIsDoneOnceTheRootHasNoFileOfItsName()
    {
        using var scratch = new ScratchFolder();
        var folder = new StashFolder(scratch.Path);
        Assert.IsTrue(folder.Create().Succeeded);
        var remove = new PendingStep(PendingStep.RemoveRender, "cat.png", "0a.gif");
        File.WriteAllBytes(folder.InRoot("cat.png"), [1]);
        File.WriteAllBytes(folder.InWork("0a.gif"), [2]);

        Assert.IsFalse(remove.IsDone(folder));

        File.Delete(folder.InRoot("cat.png"));

        Assert.IsTrue(remove.IsDone(folder), "deleted, the adopted file still to place");
    }

    [TestMethod]
    public void ASavesAndARevertsStepsReadBackEqualAndThePlaceCarriesTheRendersFacts()
    {
        var steps = new PendingStep[]
        {
            new(PendingStep.MoveToOriginals, "cat.webp", "cat.webp"),
            new(PendingStep.PlaceRender, "0a.gif", "cat.gif", Facts: new MediaFacts(ImageFormat.Gif, 8, 6, true, 3, 600)),
            new(PendingStep.MoveToRoot, "cat.webp", "cat 2.webp"),
            new(PendingStep.DeleteInRoot, "cat.gif", "cat.gif"),
        };

        var json = PendingStep.Encode(steps);

        Assert.AreEqual(
            """
            [{"step":"move to originals","from":"cat.webp","to":"cat.webp"},{"step":"place render","from":"0a.gif","to":"cat.gif","facts":{"format":"Gif","width":8,"height":6,"animated":true,"frameCount":3,"durationMs":600}},{"step":"move to root","from":"cat.webp","to":"cat 2.webp"},{"step":"delete in root","from":"cat.gif","to":"cat.gif"}]
            """,
            json);
        CollectionAssert.AreEqual(steps, PendingStep.Decode(json).ToList());
        Assert.IsFalse(steps.Any(step => step.Step == PendingStep.PlaceInRoot), "a render is never placed as an import's file");
    }

    [TestMethod]
    public void EachKindIsDoneByWhatTheFolderItWritesIntoHolds()
    {
        using var scratch = new ScratchFolder();
        var folder = new StashFolder(scratch.Path);
        Assert.IsTrue(folder.Create().Succeeded);
        var move = new PendingStep(PendingStep.MoveToOriginals, "cat.png", "cat.png");
        var place = new PendingStep(PendingStep.PlaceRender, "0a.png", "cat.png");
        var back = new PendingStep(PendingStep.MoveToRoot, "cat.png", "cat.png");
        var delete = new PendingStep(PendingStep.DeleteInRoot, "cat.png", "cat.png");
        File.WriteAllBytes(folder.InRoot("cat.png"), [1]);
        File.WriteAllBytes(folder.InWork("0a.png"), [2]);

        Assert.AreEqual((false, false, true, false), (move.IsDone(folder), place.IsDone(folder), back.IsDone(folder), delete.IsDone(folder)));

        File.Move(folder.InRoot("cat.png"), folder.InOriginals("cat.png"));
        File.Move(folder.InWork("0a.png"), folder.InRoot("cat.png"));

        Assert.IsTrue(move.IsDone(folder), "done once the original is in originals\\, though the root has cat.png again");
        Assert.IsTrue(place.IsDone(folder));
        Assert.IsFalse(back.IsDone(folder), "the original is still in originals\\");
        Assert.IsFalse(delete.IsDone(folder));
        Assert.AreEqual((true, true, false, false), (place.NamesWorkFile, PendingStep.Place("a", "b").NamesWorkFile, move.NamesWorkFile, back.NamesWorkFile || delete.NamesWorkFile));
        Assert.IsTrue(new PendingStep("a kind a later build writes", "x", "y").NamesWorkFile, "its file is kept, as its row is");
        Assert.AreEqual((folder.InRoot("cat.png"), folder.InOriginals("cat.png")), (move.FromPath(folder), move.ToPath(folder)));
        Assert.AreEqual((folder.InOriginals("cat.png"), folder.InRoot("cat.png")), (back.FromPath(folder), back.ToPath(folder)));
        Assert.AreEqual(folder.InWork("0a.png"), place.FromPath(folder));
    }

    [TestMethod]
    public void ASaveAsNewItemsCopyIsPlacedInOriginalsAndDoneOnlyOnceItHasLeftTheWorkFolderForThere()
    {
        using var scratch = new ScratchFolder();
        var folder = new StashFolder(scratch.Path);
        Assert.IsTrue(folder.Create().Succeeded);
        var copy = new PendingStep(PendingStep.PlaceInOriginals, "0a.webp", "cat 2.webp");
        File.WriteAllBytes(folder.InWork("0a.webp"), [1]);
        File.WriteAllBytes(folder.InRoot("cat 2.webp"), [2]);

        Assert.IsFalse(copy.IsDone(folder), "a file of that name in the root is not the copy");

        File.Move(folder.InWork("0a.webp"), folder.InOriginals("cat 2.webp"));

        Assert.IsTrue(copy.IsDone(folder));
        Assert.AreEqual("""[{"step":"place in originals","from":"0a.webp","to":"cat 2.webp"}]""", PendingStep.Encode([copy]));
        Assert.AreEqual((folder.InWork("0a.webp"), folder.InOriginals("cat 2.webp")), (copy.FromPath(folder), copy.ToPath(folder)));
        Assert.AreEqual((true, false), (copy.NamesWorkFile, copy.ListsWhilePending), "its file is kept in .work\\, and its row is listed once it is done");
        CollectionAssert.AreEqual(new[] { "cat 2.webp" }, copy.NamesIn(folder, folder.Originals).ToList(), "its name is taken in originals\\");
        Assert.IsEmpty(copy.NamesIn(folder, folder.Root));
    }

    [TestMethod]
    public void AnImportsStepIsWrittenAsEarlierBuildsWroteIt()
    {
        Assert.AreEqual("""[{"step":"place in root","from":"a.png","to":"b.png"}]""", PendingStep.Encode([PendingStep.Place("a.png", "b.png")]));
    }

    [TestMethod]
    public void ARenamesStepCarriesThePreviousNameAndReadsBackEqual()
    {
        var step = new PendingStep(PendingStep.RenameInOriginals, "cat.png", "dog.png", "cat");

        var json = PendingStep.Encode([step]);

        Assert.AreEqual("""[{"step":"rename in originals","from":"cat.png","to":"dog.png","previousName":"cat"}]""", json);
        Assert.AreEqual(step, PendingStep.Decode(json).Single());
    }

    [TestMethod]
    public void AStepAnEarlierBuildWroteReadsWithNoPreviousName()
    {
        var step = PendingStep.Decode("""[{"step":"place in root","from":"a.png","to":"b.png"}]""").Single();

        Assert.AreEqual(PendingStep.Place("a.png", "b.png"), step);
        Assert.IsNull(step.PreviousName);
    }
}
