using Avalonia.Threading;
using ImageStash.Seams;
using ImageStash.Store;
using ImageStash.Window;
using ImageStash.Windows.Seams;
using ImageStash.Windows.Tests.Support;
using static ImageStash.Windows.Tests.Support.ShownWindow;

namespace ImageStash.Windows.Tests.Window;

/// <summary>A searched paste into a chat through the real open shortcut, keys, target seam and clipboard.</summary>
[TestClass]
public sealed class PasteMomentTests
{
    [TestMethod]
    public async Task TypingReAndPressingEnterPastesTheReactionOnceAndTheNextSummonOpensOnItInRecent()
    {
        const ushort enter = 0x0D;
        using var pointer = TestMouse.PutBackAfterwards();
        using var targets = new WindowsTarget();
        using var shown = Open(platformTarget: targets);
        var storeClock = new StoppedClock();
        storeClock.Advance(TimeSpan.FromDays(-180));
        var reaction = (await shown.OpenStash(storeClock, shown.Picture("really tired.png"))).Single();
        storeClock.Advance(TimeSpan.FromDays(180));
        var recorded = await shown.Listing.Store!.RecordUses([reaction], storeClock.GetUtcNow().AddDays(-1));
        Assert.IsTrue(recorded.Succeeded, recorded.Failure?.Sentence);
        var newer = await shown.AddTo("Screenshot 2026-11-22 10.30.00.png", "bore.png");
        await TrayDesktop.Get(() => shown.Listing.ShowScope(Scope.All));

        var flyout = shown.Window;
        TrayDesktop.Run(() =>
        {
            flyout.Hide();
            flyout.ShowActivated = true;
        });
        using var chat = TargetApp.Start("ImageStash check chat", DisplayPlaces.At(DisplayPlaces.Primary(), 100, 100));
        chat.ClickTitleBar();
        using var tray = new WindowsTray();
        using var shortcuts = new WindowsShortcuts();
        var summon = new ShortcutSummon(targets, new WindowsQuietWindows(tray), () => flyout.PasteTarget.Current,
            reading => Dispatcher.UIThread.Post(() => flyout.PasteTarget.FromPress(reading)),
            anchor => Dispatcher.UIThread.Post(() => flyout.SummonFromShortcut(anchor)));
        var open = Shortcut.Parse("Ctrl+Alt+Space").Value!;
        var registered = shortcuts.Register(open, summon.Pressed);
        Assert.IsNull(registered, $"refused: {registered?.Kind}");

        var handle = TrayDesktop.Get(() => flyout.TryGetPlatformHandle()!.Handle);
        TestHotKeys.Press(open);
        await Eventually(() => FrontWindow.GetForegroundWindow() == handle
            && TrayDesktop.Get(() => flyout.IsActive && shown.Search.IsFocused && flyout.PasteTarget.Current?.Handle == chat.Window));
        TestHotKeys.Tap('R');
        TestHotKeys.Tap('E');
        await Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled && shown.Listing.Query == "re"));
        TrayDesktop.Run(() =>
        {
            CollectionAssert.AreEquivalent(new[] { reaction.Id, newer[0].Id, newer[1].Id }, shown.Listing.Items.Select(item => item.Id).ToList());
            Assert.AreEqual(reaction.Id, shown.Listing.Items[0].Id, "the older reaction comes first for re");
        });
        var before = chat.Messages().Count;

        TestHotKeys.Tap(enter);

        await Eventually(() => chat.Messages().Skip(before).Contains("WM_KEYUP:0x11") && shown.Uses() == 2);
        Assert.AreEqual(chat.Window, FrontWindow.GetForegroundWindow(), "the chat is in front for the paste");
        TrayDesktop.Run(() => Assert.IsFalse(flyout.IsVisible, "the flyout hid for the paste"));
        CollectionAssert.AreEqual(new[] { reaction.FilePath }, TestClipboard.Read().Files.ToList());
        var keys = chat.Messages().Skip(before).Where(message => message.StartsWith("WM_KEY") || message.StartsWith("WM_SYSKEY")).ToList();
        CollectionAssert.AreEqual(new[] { "WM_KEYDOWN:0x11", "WM_KEYDOWN:0x56", "WM_KEYUP:0x56", "WM_KEYUP:0x11" }, keys, "one Ctrl+V and no Enter");

        TestHotKeys.Press(open);
        await Eventually(() => FrontWindow.GetForegroundWindow() == handle && TrayDesktop.Get(() => flyout.IsActive));
        await TrayDesktop.Get(() => shown.Listing.Settle());

        TrayDesktop.Run(() =>
        {
            Assert.IsTrue(shown.Chip("Recent").IsEffectivelyVisible && shown.Lit("Recent"), "the Recent chip is lit");
            Assert.AreEqual("", shown.Listing.Query, "the search was cleared");
            Assert.AreEqual("", shown.Search.Text ?? "", "the box is empty");
            Assert.IsTrue(shown.Search.IsFocused, "and focused");
            CollectionAssert.AreEqual(new[] { reaction.Id }, shown.Listing.Selection.ToList(), "really tired is selected alone");
            CollectionAssert.AreEqual(new[] { $"Enter · Paste into {chat.AppName}", "Ctrl+C · Copy" }, shown.FooterHints(), "the paste left no copy confirmation");
        });
    }
}
