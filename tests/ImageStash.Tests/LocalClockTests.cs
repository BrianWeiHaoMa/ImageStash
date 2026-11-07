using ImageStash.Tests.Support;

namespace ImageStash.Tests;

/// <summary>
/// F12 and What crosses every boundary: the auto names and the times on screen are in local time, the PC's zone at that
/// moment, so a zone changed while ImageStash runs (Windows' automatic time zone on a laptop that travels) is the one
/// they use. .NET keeps the local zone it first read until TimeZoneInfo.ClearCachedData, so only the product's clock
/// reads it, again at every use.
/// </summary>
[TestClass]
public sealed class LocalClockTests
{
    private static readonly string[] CachedLocalTime =
    [
        "System.TimeProvider.get_System",
        "System.TimeZoneInfo.get_Local",
        "System.DateTime.get_Now",
        "System.DateTime.get_Today",
        "System.DateTime.ToLocalTime",
        "System.DateTimeOffset.get_Now",
        "System.DateTimeOffset.ToLocalTime",
    ];

    [TestMethod]
    public void TheLocalZoneIsReadAgainAtEveryUseAsThePcHasItNow()
    {
        var first = LocalClock.Instance.LocalTimeZone;
        var second = LocalClock.Instance.LocalTimeZone;

        Assert.AreNotSame(first, second, "read again, not the zone read once and kept");
        Assert.AreEqual(TimeZoneInfo.Local.Id, second.Id);
        var now = LocalClock.Instance.GetLocalNow();
        Assert.AreEqual(LocalClock.Instance.LocalTimeZone.GetUtcOffset(now), now.Offset);
    }

    [TestMethod]
    public void NoOtherTypeOfTheProductReadsTheTimeZoneDotNetKeeps()
    {
        var readers = ProductCalls.Methods()
            .Where(method => ProductCalls.CalledMethods(method).Any(called => CachedLocalTime.Contains($"{called.DeclaringType?.FullName}.{called.Name}")))
            .Select(method => ProductCalls.Outermost(method.DeclaringType!).FullName)
            .Distinct()
            .ToList();

        CollectionAssert.AreEqual(new[] { typeof(LocalClock).FullName }, readers, string.Join(", ", readers));
    }
}
