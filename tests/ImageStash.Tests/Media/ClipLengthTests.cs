using ImageStash.Media;

namespace ImageStash.Tests.Media;

/// <summary>An animation's time in words wherever ImageStash states it (G19, G16, G22): to the nearest tenth, halves up.</summary>
[TestClass]
public sealed class ClipLengthTests
{
    [TestMethod]
    public void ALengthIsInSecondsWithOneDecimal()
    {
        Assert.AreEqual("0.6 s", ClipLength.Words(600));
        Assert.AreEqual("0.0 s", ClipLength.Words(49));
        Assert.AreEqual("0.1 s", ClipLength.Words(50), "a half rounds up");
        Assert.AreEqual("0.2 s", ClipLength.Words(150));
        Assert.AreEqual("30.0 s", ClipLength.Words(29_960));
        Assert.AreEqual("75.4 s", ClipLength.Words(75_400));
    }

    [TestMethod]
    public void AMomentReadsInMinutesSecondsAndTenths()
    {
        Assert.AreEqual("0:00.0", ClipLength.Clock(0));
        Assert.AreEqual("0:01.3", ClipLength.Clock(1_250), "a half rounds up");
        Assert.AreEqual("0:02.4", ClipLength.Clock(2_400));
        Assert.AreEqual("1:00.0", ClipLength.Clock(59_960), "never 0:60.0");
        Assert.AreEqual("1:15.4", ClipLength.Clock(75_400));
    }
}
