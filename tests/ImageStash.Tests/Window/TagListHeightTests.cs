using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// How tall a list of tags stands (G4, G17), the one rule the chip row's and the tag field's list and the item menu's
/// Add tag ▸ and Remove tag ▸ read: at most the chip row's list as it stands, inside the display's working area less a
/// margin, never shorter than one entry's room.
/// </summary>
[TestClass]
public sealed class TagListHeightTests
{
    [TestMethod]
    [DataRow(800d, 360d, DisplayName = "a display with room: the tallest a list stands")]
    [DataRow(400d, 360d, DisplayName = "exactly room for the tallest and its margin")]
    [DataRow(300d, 260d, DisplayName = "a short display: its working area less the margin")]
    [DataRow(60d, 40d, DisplayName = "a display shorter than the margin and an entry: an entry's room")]
    [DataRow(10d, 40d, DisplayName = "a display shorter than the margin: an entry's room")]
    public void AListStandsNoTallerThanTheTallestNorThanItsDisplayLessAMargin(double workingAreaHeight, double expected) =>
        Assert.AreEqual(expected, TagListHeight.For(workingAreaHeight));
}
