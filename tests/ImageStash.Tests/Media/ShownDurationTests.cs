using ImageStash.Media;

namespace ImageStash.Tests.Media;

/// <summary>How long a frame shows wherever ImageStash plays a picture: as a browser shows it (G6, G19, G22).</summary>
[TestClass]
public sealed class ShownDurationTests
{
    [TestMethod]
    public void AFrameAskingForTenMillisecondsOrLessShowsForAHundredAndAnyOtherForItsOwn()
    {
        Assert.AreEqual(TimeSpan.FromMilliseconds(100), ShownDuration.Of(0));
        Assert.AreEqual(TimeSpan.FromMilliseconds(100), ShownDuration.Of(10));
        Assert.AreEqual(TimeSpan.FromMilliseconds(11), ShownDuration.Of(11));
        Assert.AreEqual(TimeSpan.FromMilliseconds(300), ShownDuration.Of(300));
    }
}
