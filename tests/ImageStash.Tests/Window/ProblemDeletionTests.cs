using ImageStash.Store;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// A Delete's notices and G16's stay (G18, G35): the result and each refusal's own failure, raised by one act, stand
/// together; each failure stays past its time, while the result, which names no problem though it says what Undo
/// brings back, leaves at its time with its Undo, finishing what it deleted.
/// </summary>
[TestClass]
public sealed class ProblemDeletionTests
{
    [TestMethod]
    public async Task ADeleteOfThreeTwoRefusedForDifferentReasonsShowsItsResultAndBothFailuresAndOnlyTheFailuresStay()
    {
        using var listed = await ListedStash.Open();
        var (first, held, locked) = (await listed.Stash.Import("first"), await listed.Stash.Import("held"), await listed.Stash.Import("locked"));
        await listed.Listing.ShowScope(Scope.All);
        listed.Listing.SelectAll();

        using (new FileStream(held.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (new FileStream(locked.FilePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            await listed.Deletion.Delete(listed.Listing.SelectedItems);
        }

        var shown = listed.Notices.Shown.ToList();
        Assert.HasCount(3, shown, string.Join(" | ", shown.Select(notice => notice.Text)));
        var result = shown[0];
        Assert.AreEqual("Deleted “first”. “held” could not be deleted yet; Undo brings it back.", result.Text);
        Assert.IsFalse(result.NamesAProblem, "a result, whose refusals are named by notices of their own");
        Assert.IsTrue(shown.Skip(1).All(notice => notice.NamesAProblem));
        StringAssert.Contains(shown[1].Text + shown[2].Text, held.FileName);
        StringAssert.Contains(shown[1].Text + shown[2].Text, locked.FileName);

        listed.Stash.Clock.Now += TimeSpan.FromMinutes(1);
        listed.Notices.Sweep();
        await listed.Stash.All();

        CollectionAssert.AreEqual(shown.Skip(1).ToList(), listed.Notices.Shown.ToList(), "both failures stay; the result left at its time");
        Assert.HasCount(1, listed.Stash.WorkFiles(), "its leaving finished the Delete of what went to the Recycle Bin: only the refused item's copy is left");
    }

    [TestMethod]
    public async Task ADeletesResultWithNoRefusalLeavesAtItsTime()
    {
        using var listed = await ListedStash.Open();
        var cat = await listed.Stash.Import("cat");
        await listed.Listing.ShowScope(Scope.All);

        Assert.IsTrue((await listed.Deletion.Delete([cat])).Succeeded);
        listed.Stash.Clock.Now += Notices.Lasting;
        listed.Notices.Sweep();

        Assert.IsEmpty(listed.Notices.Shown);
    }
}
