using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using ImageStash.Window;
using ImageStash.Windows.Tests.Support;

namespace ImageStash.Windows.Tests.Window;

/// <summary>
/// A notice's clock on the real window (G16): it runs only while the stash window shows, so a notice raised while the
/// flyout is hidden is on screen at the next summon and leaves its time after it; unpinning, which hides and shows the
/// window within one showing, keeps a notice up with its time running on. These checks show the stash window and click
/// its pin.
/// </summary>
[TestClass]
public sealed class WindowShowingTests
{
    [TestMethod]
    public async Task ANoticeRaisedWhileTheFlyoutIsHiddenShowsAtTheNextSummonAndLeavesItsTimeAfterIt()
    {
        using var shown = ShownWindow.Open();
        await shown.OpenStash(shown.Picture("raised hidden.png"));
        var clock = (StoppedClock)shown.Notices.Clock;
        var notice = TrayDesktop.Get(() =>
        {
            shown.Window.Hide();
            return shown.Notices.Raise("Raised while the flyout was hidden.");
        });

        clock.Advance(TimeSpan.FromHours(1));
        TrayDesktop.Run(shown.Notices.Sweep);
        TrayDesktop.Run(() => shown.Window.SummonFromShortcut(default, new SummonAnchor(null)));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Window.IsVisible && CardSays(shown, notice.Text)));

        clock.Advance(Notices.Lasting - TimeSpan.FromMilliseconds(100));
        TrayDesktop.Run(shown.Notices.Sweep);
        TrayDesktop.Run(() => Assert.IsTrue(CardSays(shown, notice.Text), "still on screen just short of six seconds shown"));
        clock.Advance(TimeSpan.FromMilliseconds(100));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => !CardSays(shown, notice.Text)));
    }

    [TestMethod]
    public async Task UnpinningWithANoticeUpKeepsItAndItsTimeRunsOn()
    {
        using var shown = ShownWindow.Open();
        await shown.OpenStash(shown.Picture("unpinned.png"));
        var clock = (StoppedClock)shown.Notices.Clock;
        TrayDesktop.Run(() =>
        {
            shown.Window.Hide();
            shown.Window.SummonFromShortcut(default, new SummonAnchor(null));
        });
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Window.IsVisible));
        TrayDesktop.Run(() => shown.Click(Pin(shown)));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Window.ShowInTaskbar && shown.Window.IsVisible), TimeSpan.FromSeconds(10));
        var notice = TrayDesktop.Get(() => shown.Notices.Raise("Up when the window was unpinned."));
        clock.Advance(TimeSpan.FromSeconds(4));

        TrayDesktop.Run(() => shown.Click(Pin(shown)));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => !shown.Window.ShowInTaskbar && shown.Window.IsVisible), TimeSpan.FromSeconds(10));

        TrayDesktop.Run(() =>
        {
            Assert.IsTrue(shown.Window.IsVisible, "a flyout still showing");
            shown.Notices.Sweep();
            Assert.IsTrue(CardSays(shown, notice.Text), "the notice kept through the unpin");
        });
        clock.Advance(TimeSpan.FromSeconds(2));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => !CardSays(shown, notice.Text)));
    }

    private static ToggleButton Pin(ShownWindow shown) => ShownPin.Of(shown);

    /// <summary>Whether a notice card in the stash window reads <paramref name="text"/>.</summary>
    private static bool CardSays(ShownWindow shown, string text) =>
        shown.Window.GetVisualDescendants().OfType<TextBlock>().Any(block => block.Text == text && block.IsEffectivelyVisible);
}
