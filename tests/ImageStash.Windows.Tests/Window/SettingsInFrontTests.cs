using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ImageStash.Window;
using ImageStash.Windows.Tests.Support;
using static ImageStash.Windows.Tests.Support.ShownSettings;

namespace ImageStash.Windows.Tests.Window;

/// <summary>
/// The Settings page opened from the flyout's gear comes in front of it, the two taking the front by turns as two
/// ordinary windows do, the flyout above every window again once the page closes and at the next summon; the page opens
/// beside the flyout on its display, and an open page comes forward where it was left (G32); the pinned window and each
/// page ask for a title bar without the icon, at every pin.
/// </summary>
[TestClass]
public sealed class SettingsInFrontTests
{
    [TestMethod]
    public async Task TheGearsPageComesInFrontOfTheFlyoutEachTakingTheFrontWhenClickedAndTheFlyoutIsAboveAgainOnceThePageCloses()
    {
        using var settings = new SettingsFolder();
        using var pointer = TestMouse.PutBackAfterwards();
        using var shown = ShownWindow.Open();
        await shown.OpenStash();
        SettingsWindow? open = null;
        var flyout = shown.Window;
        SummonAtTheLeft(shown, settings, () => open, page => open = page);
        await Task.Delay(300);
        try
        {
            TestMouse.Click(TestMouse.Centre(TrayDesktop.Get(() => shown.SettingsButton)));
            await ShownWindow.Eventually(() => TrayDesktop.Get(() => open is { IsActive: true }));
            await Task.Delay(300);
            var page = open!;
            var (flyoutHandle, pageHandle) = TrayDesktop.Get(() => (flyout.TryGetPlatformHandle()!.Handle, page.TryGetPlatformHandle()!.Handle));
            TrayDesktop.Run(() =>
            {
                Assert.IsTrue(flyout.IsVisible, "focus went to ImageStash's own page, so the flyout stays (G2)");
                Assert.IsFalse(flyout.Topmost, "opened and nothing more touched, the flyout gives way to the page");
            });
            Assert.IsTrue(FrontWindow.StandsInFrontOf(pageHandle, flyoutHandle), "the page in front of the flyout it was opened from");

            TestMouse.Click(TestMouse.Centre(TrayDesktop.Get(() => shown.Search)));
            await ShownWindow.Eventually(() => TrayDesktop.Get(() => flyout.IsActive));
            await Task.Delay(300);
            Assert.IsTrue(TrayDesktop.Get(() => flyout.Topmost), "the flyout clicked: above again");
            Assert.IsTrue(FrontWindow.StandsInFrontOf(flyoutHandle, pageHandle), "the flyout in front");

            TestMouse.Click(TestMouse.Centre(TrayDesktop.Get(() => page.GetVisualDescendants().OfType<TextBlock>().First(text => text.Text == "Screenshots and recordings"))));
            await ShownWindow.Eventually(() => TrayDesktop.Get(() => page.IsActive));
            await Task.Delay(300);
            Assert.IsFalse(TrayDesktop.Get(() => flyout.Topmost), "the page clicked again: in front again, not only at its opening");
            Assert.IsTrue(FrontWindow.StandsInFrontOf(pageHandle, flyoutHandle), "the page in front again");

            TrayDesktop.Run(page.Close);
            await Task.Delay(300);
            Assert.IsTrue(TrayDesktop.Get(() => flyout.Topmost), "the page closed: the flyout above every window again");

            TrayDesktop.Run(() =>
            {
                flyout.Hide();
                flyout.SummonFromTray();
            });
            await Task.Delay(300);
            Assert.IsTrue(TrayDesktop.Get(() => flyout.Topmost), "and at the next summon");
        }
        finally
        {
            TrayDesktop.Run(() => open?.Close());
        }
    }

    [TestMethod]
    public async Task ThePageOpensBesideTheFlyoutOnItsDisplayAndAnOpenPageComesForwardWhereItWasLeft()
    {
        using var settings = new SettingsFolder();
        using var pointer = TestMouse.PutBackAfterwards();
        using var shown = ShownWindow.Open();
        await shown.OpenStash();
        SettingsWindow? open = null;
        var flyout = shown.Window;
        SummonAtTheLeft(shown, settings, () => open, page => open = page);
        await Task.Delay(300);
        try
        {
            TestMouse.Click(TestMouse.Centre(TrayDesktop.Get(() => shown.SettingsButton)));
            await ShownWindow.Eventually(() => TrayDesktop.Get(() => open is { IsActive: true, FrameSize: not null }));
            // The page places itself once more, its frame then known, after the pending layout (SettingsWindow.ShowPlaced).
            await Task.Delay(300);
            var page = open!;
            var (flyoutOuter, pageOuter, area) = TrayDesktop.Get(() => (CaptureDesk.Outer(flyout), CaptureDesk.Outer(page), page.Screens.Primary!.WorkingArea));
            Assert.IsTrue(area.Contains(pageOuter), $"wholly inside the flyout's display's working area: {pageOuter} in {area}");
            // Wherever the flyout stands, a side holding the page puts it beside, not over.
            if (area.Right - flyoutOuter.Right >= pageOuter.Width || flyoutOuter.X - area.X >= pageOuter.Width)
            {
                Assert.IsFalse(pageOuter.Intersects(flyoutOuter), $"beside the flyout, not over it: {pageOuter} and {flyoutOuter}");
            }

            // Left elsewhere by the person, then asked again from the flyout.
            var left = DisplayPlaces.At(DisplayPlaces.Primary(), 300, 20);
            TrayDesktop.Run(() => page.Position = left);
            TestMouse.Click(TestMouse.Centre(TrayDesktop.Get(() => shown.Search)));
            await ShownWindow.Eventually(() => TrayDesktop.Get(() => flyout.IsActive));
            TestMouse.Click(TestMouse.Centre(TrayDesktop.Get(() => shown.SettingsButton)));
            await ShownWindow.Eventually(() => TrayDesktop.Get(() => page.IsActive));
            await Task.Delay(300);
            TrayDesktop.Run(() =>
            {
                Assert.AreSame(page, open, "the open page, not a second (F4)");
                Assert.AreEqual(left, page.Position, "brought forward where it was left, never moved (G32)");
            });
        }
        finally
        {
            TrayDesktop.Run(() => open?.Close());
        }
    }

    [TestMethod]
    public async Task ThePinnedWindowAsksForAPlainTitleBarAtEveryPinAndEachNewPageOnce()
    {
        using var settings = new SettingsFolder();
        using var shown = ShownWindow.Open();
        await shown.OpenStash();
        TrayDesktop.Run(() => shown.Window.SummonPinned());
        await Task.Delay(300);
        Assert.AreEqual(1, TrayDesktop.Get(() => shown.QuietWindows.PlainTitled.Count(asked => asked == shown.Window)), "the first pinned show");

        TrayDesktop.Run(() => shown.Click(ShownPin.Of(shown)));
        await Task.Delay(300);
        Assert.AreEqual(1, TrayDesktop.Get(() => shown.QuietWindows.PlainTitled.Count), "the flyout has no title bar to ask for");

        TrayDesktop.Run(() => shown.Click(ShownPin.Of(shown)));
        await Task.Delay(300);
        Assert.AreEqual(2, TrayDesktop.Get(() => shown.QuietWindows.PlainTitled.Count(asked => asked == shown.Window)), "pinned again, its frame drawn afresh");

        SettingsWindow? first = null;
        SettingsWindow? second = null;
        try
        {
            first = TrayDesktop.Get(() => SettingsWindow.Open(settings.TitleBars, null, () => settings.Page()));
            var again = TrayDesktop.Get(() => SettingsWindow.Open(settings.TitleBars, first, () => settings.Page()));
            Assert.AreSame(first, again);
            TrayDesktop.Run(first.Close);
            second = TrayDesktop.Get(() => SettingsWindow.Open(settings.TitleBars, first, () => settings.Page()));

            CollectionAssert.AreEqual(new Avalonia.Controls.Window[] { first, second }, TrayDesktop.Get(() => settings.TitleBars.PlainTitled.ToArray()), "each page made asks once, before it shows");
        }
        finally
        {
            TrayDesktop.Run(() => second?.Close());
        }
    }

    /// <summary>
    /// The flyout summoned and moved to the primary display's left edge, so a page beside it has room on most displays,
    /// its gear and <c>Ctrl+,</c> opening the page as the app wires them.
    /// </summary>
    private static void SummonAtTheLeft(ShownWindow shown, SettingsFolder settings, Func<SettingsWindow?> open, Action<SettingsWindow> opened) => TrayDesktop.Run(() =>
    {
        var flyout = shown.Window;
        flyout.SettingsRequested += () =>
        {
            var was = open();
            var page = SettingsWindow.Open(settings.TitleBars, was, () => settings.Page(), null, flyout.Outer);
            opened(page);
            if (!ReferenceEquals(was, page))
            {
                flyout.Follow(page);
            }
        };
        flyout.Hide();
        flyout.ShowActivated = true;
        flyout.SummonFromTray();
        // After the summon's own second placement, queued at this priority, which would centre it again.
        Dispatcher.UIThread.Post(() => flyout.Position = DisplayPlaces.At(DisplayPlaces.Primary(), 0, 20), DispatcherPriority.Background);
    });
}
