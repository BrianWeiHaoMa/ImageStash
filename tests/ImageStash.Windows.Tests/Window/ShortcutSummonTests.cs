using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using ImageStash.Window;
using ImageStash.Windows.Tests.Support;

namespace ImageStash.Windows.Tests.Window;

/// <summary>
/// The open shortcut through the real stash window (F5, G1, G2): it shows the flyout beside the caret, else at the
/// pointer, wholly on the display, as it was left and focused as every summon is, within a second; again, it hides it; a pinned window, even
/// minimised, comes forward instead; a flyout whose dialog is up stays; after Close the next press shows the flyout.
/// These summons show the window on the primary display for a moment.
/// </summary>
[TestClass]
public sealed class ShortcutSummonTests
{
    [TestMethod]
    public async Task TheShortcutShowsTheFlyoutAtThePointerAsItWasLeftWithTheBoxsTextSelectedAndThenHidesIt()
    {
        using var shown = ShownWindow.Open();
        await shown.OpenStash(shown.Picture("shortcut one.png"), shown.Picture("shortcut two.png"));
        // Left as a person might have left it: a query typed and the second item selected, the flyout hidden.
        TrayDesktop.Run(() => shown.Search.Text = "shortcut");
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled && shown.Listing.Query == "shortcut"));
        var left = TrayDesktop.Get(() => shown.Listing.Items[1].Id);
        TrayDesktop.Run(() =>
        {
            shown.Listing.Focus(left);
            shown.Window.Hide();
        });
        var area = TrayDesktop.Get(() => shown.Window.Screens.Primary!.WorkingArea);
        var pointer = new PixelPoint(area.X + 40, area.Y + 40);

        var clock = Stopwatch.StartNew();
        TrayDesktop.Run(() => shown.Window.SummonFromShortcut(default, new SummonAnchor(pointer)));
        var took = clock.ElapsedMilliseconds;
        await TrayDesktop.Get(() => shown.Listing.Settle());

        TrayDesktop.Run(() =>
        {
            var window = shown.Window;
            Assert.IsTrue(window.IsVisible, "the flyout shows");
            Assert.IsFalse(window.ShowInTaskbar, "as the flyout, not pinned");
            Assert.AreEqual(pointer, window.Position, "its top-left corner at the pointer, where it fits");
            var outer = new PixelRect(window.Position, PixelSize.FromSize(window.FrameSize!.Value, window.RenderScaling));
            Assert.IsTrue(area.Contains(outer), $"{outer} lies wholly inside {area}");
            Assert.IsTrue(shown.Search.IsFocused, "the search box has focus");
            Assert.AreEqual("shortcut", shown.Search.Text, "the search it was left with (G1)");
            Assert.AreEqual("shortcut", shown.Search.SelectedText, "all of it selected, so that typing replaces it (G1)");
            Assert.AreEqual("shortcut", shown.Listing.Query);
            CollectionAssert.AreEqual(new[] { left }, shown.Listing.Selection.ToList(), "the selection it was left with, not the first item");
        });
        Assert.IsLessThan(1000, took, "shown within a second");

        TrayDesktop.Run(() => shown.Window.SummonFromShortcut(default, new SummonAnchor(pointer)));

        TrayDesktop.Run(() => Assert.IsFalse(shown.Window.IsVisible, "the shortcut again hides the flyout"));
    }

    [TestMethod]
    public async Task WithACaretTheFlyoutOpensBesideItAndNotAtThePointer()
    {
        using var shown = ShownWindow.Open();
        await shown.OpenStash(shown.Picture("caret one.png"));
        TrayDesktop.Run(shown.Window.Hide);
        var area = TrayDesktop.Get(() => shown.Window.Screens.Primary!.WorkingArea);
        var scaling = TrayDesktop.Get(() => shown.Window.Screens.Primary!.Scaling);
        var caret = new PixelRect(area.X + 300, area.Bottom - 60, 1, 16);

        TrayDesktop.Run(() => shown.Window.SummonFromShortcut(default, new SummonAnchor(new PixelPoint(area.X + 40, area.Y + 40), caret)));

        TrayDesktop.Run(() =>
        {
            var window = shown.Window;
            var outer = new PixelRect(window.Position, PixelSize.FromSize(window.FrameSize!.Value, window.RenderScaling));
            Assert.IsTrue(window.IsVisible, "the flyout shows");
            Assert.AreEqual(caret.X - (int)Math.Round(Summon.CaretInset * scaling), outer.X, "its left edge left of the caret's");
            Assert.IsLessThanOrEqualTo(2, Math.Abs(caret.Y - (int)Math.Round(Summon.CaretGap * scaling) - outer.Bottom), $"its bottom {outer.Bottom} just above the caret's line at {caret.Y}");
            Assert.IsTrue(area.Contains(outer), $"{outer} lies wholly inside {area}");
            Assert.IsTrue(shown.Search.IsFocused, "the search box has focus");
        });
    }

    [TestMethod]
    public void APinnedWindowEvenMinimisedComesForwardInsteadOfHiding()
    {
        using var shown = ShownWindow.Open();
        TrayDesktop.Run(() =>
        {
            shown.Window.SummonPinned();
            shown.Window.WindowState = WindowState.Minimized;
        });

        TrayDesktop.Run(() => shown.Window.SummonFromShortcut(default, new SummonAnchor(null)));

        TrayDesktop.Run(() =>
        {
            Assert.IsTrue(shown.Window.IsVisible);
            Assert.AreEqual(WindowState.Normal, shown.Window.WindowState);
            Assert.IsTrue(shown.Window.ShowInTaskbar, "still pinned");
        });
    }

    [TestMethod]
    public void AFlyoutWhoseDialogIsUpStaysAtThePress()
    {
        using var shown = ShownWindow.Open();
        TrayDesktop.Run(() =>
        {
            shown.Window.Hide();
            shown.Window.SummonFromShortcut(default, new SummonAnchor(null));
            shown.Window.DialogUp = true;
        });

        TrayDesktop.Run(() => shown.Window.SummonFromShortcut(default, new SummonAnchor(null)));

        TrayDesktop.Run(() =>
        {
            Assert.IsTrue(shown.Window.IsVisible, "the flyout stays while its dialog is up");
            shown.Window.DialogUp = false;
        });
    }

    [TestMethod]
    public async Task AFlyoutWhoseDragIsInProgressStaysAtThePress()
    {
        var drags = new PresetDrags();
        using var shown = ShownWindow.Open(drags);
        var item = (await shown.OpenStash(shown.Picture("shortcut drag.png"))).Single();
        var drop = new TaskCompletionSource<Outcome<bool>>();
        drags.Pending = drop.Task;
        TrayDesktop.Run(() =>
        {
            shown.Window.Hide();
            shown.Window.SummonFromShortcut(default, new SummonAnchor(null));
        });
        await TrayDesktop.Get(() => shown.Listing.Settle());
        await Task.Delay(300);
        TrayDesktop.Run(() =>
        {
            shown.Press(item.Id);
            shown.MovePointer(item.Id, 12, 3);
        });
        await Task.Delay(300);

        TrayDesktop.Run(() =>
        {
            Assert.IsTrue(shown.Window.DragInProgress, "the drag is in progress");
            shown.Window.SummonFromShortcut(default, new SummonAnchor(null));
            Assert.IsTrue(shown.Window.IsVisible, "the flyout stays while its drag is in progress (G2)");
            drop.SetResult(Outcome<bool>.Of(false));
        });
    }

    [TestMethod]
    public void AfterClosingThePinnedWindowThePressShowsTheFlyout()
    {
        using var shown = ShownWindow.Open();
        TrayDesktop.Run(() =>
        {
            shown.Window.SummonPinned();
            shown.Window.Close();
            Assert.IsFalse(shown.Window.IsVisible, "Close hides it");
        });

        TrayDesktop.Run(() => shown.Window.SummonFromShortcut(default, new SummonAnchor(null)));

        TrayDesktop.Run(() =>
        {
            Assert.IsTrue(shown.Window.IsVisible, "the shortcut brings it back");
            Assert.IsFalse(shown.Window.ShowInTaskbar, "as the flyout");
        });
    }
}
