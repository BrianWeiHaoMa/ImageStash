using ImageStash.Files;

namespace ImageStash.Tests.Files;

/// <summary>A byte count in words, in units of 1,024, as the preview, the capture notification and the recording bar show it (G19, G16, G24, G15).</summary>
[TestClass]
public sealed class FileSizeTests
{
    [TestMethod]
    public void TheSizeCountsInUnitsOf1024()
    {
        Assert.AreEqual("0 KB", FileSize.Words(0), "nothing written yet, as a recording's bar reads before its first frame");
        Assert.AreEqual("1 KB", FileSize.Words(1), "never 0 KB for a file that holds something");
        Assert.AreEqual("2 KB", FileSize.Words(1536));
        Assert.AreEqual("1023 KB", FileSize.Words(1023 * 1024));
        Assert.AreEqual("1.0 MB", FileSize.Words(1024 * 1024));
        Assert.AreEqual("1.7 MB", FileSize.Words(1_782_579));
    }
}
