using System.Globalization;
using ImageStash.Store;

namespace ImageStash.Tests.Store;

/// <summary>F12's automatic names: <c>Screenshot YYYY-MM-DD HH.MM.SS</c>, a stashed clipboard image's too, and <c>Recording YYYY-MM-DD HH.MM.SS</c>, in the local time each is given.</summary>
[TestClass]
public sealed class AutoNameTests
{
    [TestMethod]
    public void AScreenshotIsNamedByItsLocalTimeToTheSecondOnATwentyFourHourClock()
    {
        Assert.AreEqual("Screenshot 2026-10-18 09.05.07", AutoName.Screenshot(new DateTimeOffset(2026, 10, 18, 9, 5, 7, 999, TimeSpan.FromHours(-7))));
        Assert.AreEqual("Screenshot 2026-01-02 21.30.00", AutoName.Screenshot(new DateTimeOffset(2026, 1, 2, 21, 30, 0, TimeSpan.FromHours(5.5))));
    }

    [TestMethod]
    public void ARecordingIsNamedByItsLocalTimeToTheSecondOnATwentyFourHourClock()
    {
        Assert.AreEqual("Recording 2026-10-18 21.05.07", AutoName.Recording(new DateTimeOffset(2026, 10, 18, 21, 5, 7, 999, TimeSpan.FromHours(-7))));
        Assert.AreEqual("Recording 2026-01-02 09.30.00", AutoName.Recording(new DateTimeOffset(2026, 1, 2, 9, 30, 0, TimeSpan.FromHours(5.5))));
    }

    [TestMethod]
    public void TheNameIsTheSameUnderEveryCultureAndCalendar()
    {
        var before = CultureInfo.CurrentCulture;
        try
        {
            // Thai culture counts Buddhist-era years; Arabic Saudi Arabia's counts Hijri years with its own digits.
            foreach (var culture in new[] { "th-TH", "ar-SA", "de-DE" })
            {
                CultureInfo.CurrentCulture = new CultureInfo(culture);
                Assert.AreEqual("Screenshot 2026-10-18 09.05.07", AutoName.Screenshot(new DateTimeOffset(2026, 10, 18, 9, 5, 7, TimeSpan.Zero)), culture);
                Assert.AreEqual("Recording 2026-10-18 09.05.07", AutoName.Recording(new DateTimeOffset(2026, 10, 18, 9, 5, 7, TimeSpan.Zero)), culture);
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }
}
