using ImageStash.Store;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// Which item a step of the preview shows (G19): the previous or the next of the grid's list while it holds the item
/// shown, nothing past either end; for an item that left the list, the item now at the place it last stood at, or the one
/// before it, the list's last when it has shrunk past that place; for one that never stood in it, the first; with an empty
/// list, nothing.
/// </summary>
[TestClass]
public sealed class PreviewStepTests
{
    private static readonly IReadOnlyList<StashItem> Five = TestItems.Many(5);

    [TestMethod]
    [DataRow(0, StepWay.On, 2L)]
    [DataRow(2, StepWay.On, 4L)]
    [DataRow(2, StepWay.Back, 2L)]
    [DataRow(4, StepWay.Back, 4L)]
    public void WhileTheListHoldsTheItemAStepShowsItsNeighbourThere(int at, StepWay way, long expected) =>
        Assert.AreEqual(expected, PreviewStep.Of(Five, Five[at].Id, lastPlace: null, way)?.Id);

    [TestMethod]
    public void TheStepBackAtTheFirstItemAndTheStepOnAtTheLastDoNothing()
    {
        Assert.IsNull(PreviewStep.Of(Five, Five[0].Id, lastPlace: 0, StepWay.Back), "no wrap to the last (G19)");
        Assert.IsNull(PreviewStep.Of(Five, Five[4].Id, lastPlace: 4, StepWay.On), "no wrap to the first (G19)");
    }

    [TestMethod]
    public void WhileTheListHoldsTheItemItsPlaceNowDecidesNotThePlaceItLastStoodAt()
    {
        // Moved to the list's head (Recent after a copy): its place there, not the third place it stood at before.
        var moved = new[] { Five[2], Five[0], Five[1], Five[3], Five[4] };
        Assert.AreEqual(Five[0].Id, PreviewStep.Of(moved, Five[2].Id, lastPlace: 2, StepWay.On)?.Id);
        Assert.IsNull(PreviewStep.Of(moved, Five[2].Id, lastPlace: 2, StepWay.Back));
    }

    [TestMethod]
    public void ForAnItemThatLeftTheListTheStepOnShowsTheItemNowAtItsPlaceAndTheStepBackTheOneBefore()
    {
        var left = new[] { Five[0], Five[1], Five[3], Five[4] };
        Assert.AreEqual(Five[3].Id, PreviewStep.Of(left, Five[2].Id, lastPlace: 2, StepWay.On)?.Id, "what was the next stands at its place now");
        Assert.AreEqual(Five[1].Id, PreviewStep.Of(left, Five[2].Id, lastPlace: 2, StepWay.Back)?.Id);
    }

    [TestMethod]
    public void ForAnItemThatLeftTheListFromItsFirstPlaceTheStepBackDoesNothing()
    {
        var left = Five.Skip(1).ToList();
        Assert.AreEqual(Five[1].Id, PreviewStep.Of(left, Five[0].Id, lastPlace: 0, StepWay.On)?.Id);
        Assert.IsNull(PreviewStep.Of(left, Five[0].Id, lastPlace: 0, StepWay.Back));
    }

    [TestMethod]
    [DataRow(4)]
    [DataRow(9)]
    public void WhereTheListHasShrunkPastTheItemsPlaceTheStepOnDoesNothingAndTheStepBackShowsTheLastItem(int lastPlace)
    {
        var shrunk = Five.Take(3).ToList();
        Assert.IsNull(PreviewStep.Of(shrunk, Five[4].Id, lastPlace, StepWay.On));
        Assert.AreEqual(Five[2].Id, PreviewStep.Of(shrunk, Five[4].Id, lastPlace, StepWay.Back)?.Id);
    }

    [TestMethod]
    public void ForAnItemThatNeverStoodInTheListTheStepOnShowsTheFirstAndTheStepBackDoesNothing()
    {
        const long elsewhere = 99;
        Assert.AreEqual(Five[0].Id, PreviewStep.Of(Five, elsewhere, lastPlace: null, StepWay.On)?.Id);
        Assert.IsNull(PreviewStep.Of(Five, elsewhere, lastPlace: null, StepWay.Back));
    }

    [TestMethod]
    [DataRow(StepWay.Back, null)]
    [DataRow(StepWay.On, null)]
    [DataRow(StepWay.Back, 0)]
    [DataRow(StepWay.On, 2)]
    public void WithAnEmptyListBothDoNothing(StepWay way, int? lastPlace) =>
        Assert.IsNull(PreviewStep.Of([], Five[0].Id, lastPlace, way));
}
