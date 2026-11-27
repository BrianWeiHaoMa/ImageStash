using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// The flyout above every other window but its own Settings page while that is the active window, however often the
/// two take turns, and above again at the next summon; the pinned window never above (G1, G2). The real windows'
/// activations reach the rule in the desktop suite.
/// </summary>
[TestClass]
public sealed class FlyoutTopmostTests
{
    [TestMethod]
    public void TheFlyoutGivesWayToItsActivePageEachTimeAndIsAboveAgainOnceActivatedOrThePageCloses()
    {
        var rule = new FlyoutTopmost();
        rule.Framed(pinnedNow: false);
        Assert.IsTrue(rule.Topmost, "the flyout shown by a summon");

        rule.PageActivated();
        Assert.IsFalse(rule.Topmost, "the page just opened from it, nothing more touched");
        rule.WindowActivated();
        Assert.IsTrue(rule.Topmost, "the flyout clicked");
        rule.PageActivated();
        Assert.IsFalse(rule.Topmost, "the page clicked again: in front again, not only at its opening");
        rule.WindowActivated();
        rule.PageActivated();
        Assert.IsFalse(rule.Topmost, "and every time after");
        rule.PageClosed();
        Assert.IsTrue(rule.Topmost, "the page closed");
    }

    [TestMethod]
    public void EverySummonPutsTheFlyoutAboveAgainThoughThePageWasLastActive()
    {
        var rule = new FlyoutTopmost();
        rule.Framed(pinnedNow: false);
        rule.PageActivated();

        // Shown again with Windows refusing it the foreground, so no activation of its own comes.
        rule.Framed(pinnedNow: false);

        Assert.IsTrue(rule.Topmost, "above a full-screen app at the next summon");
    }

    [TestMethod]
    public void ThePinnedWindowIsNeverAboveWithThePageOpenOrClosed()
    {
        var rule = new FlyoutTopmost();
        rule.Framed(pinnedNow: true);
        Assert.IsFalse(rule.Topmost, "pinned");

        rule.PageActivated();
        Assert.IsFalse(rule.Topmost, "pinned, the page active");
        rule.WindowActivated();
        Assert.IsFalse(rule.Topmost, "pinned, activated");
        rule.PageClosed();
        Assert.IsFalse(rule.Topmost, "pinned, the page closed");

        rule.Framed(pinnedNow: false);
        Assert.IsTrue(rule.Topmost, "unpinned to the flyout");
        rule.Framed(pinnedNow: true);
        Assert.IsFalse(rule.Topmost, "pinned again");
    }
}
