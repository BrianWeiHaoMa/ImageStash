using Avalonia;
using Avalonia.Controls;
using ImageStash.Seams;
using ImageStash.Store;
using ImageStash.Window;
using ImageStash.Windows.Tests.Support;

namespace ImageStash.Windows.Tests.Window;

/// <summary>
/// The summon through the real stash window with the preview open (G1): from the tray and from the shortcut the flyout
/// is placed by the summon's origin and shows its content as it was left, the query, the selection and the preview on
/// its item kept and focus inside the preview, never in the box it covers; a pinned window, minimised, comes forward
/// where it is the same way, its animation held while minimised; with the preview closed the summon keeps the scope, the
/// search and the selection too, focus in the box with its text selected, so typing replaces it, and a first summon
/// finds Recent or All; the preview's facts state the times as they are at the summon, not as they were when it was left (G19).
/// These summons show the window on the primary display for a moment.
/// </summary>
[TestClass]
public sealed class SummonTests
{
    [TestMethod]
    public async Task ATraySummonWithThePreviewOpenShowsItAsItWasLeftAboveTheTrayAndWithItClosedTheBoxTakesFocusAsItWasLeft()
    {
        using var shown = ShownWindow.Open();
        var items = await shown.OpenStash(shown.Picture("summon one.png"), shown.Picture("summon two.png"), shown.Picture("other three.png"));
        TrayDesktop.Run(() => shown.Search.Text = "summon");
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled && shown.Listing.Items.Count == 2));
        // The second match, so a load that selected the first item would show as a change.
        var left = TrayDesktop.Get(() => shown.Listing.Items[1]);
        TrayDesktop.Run(() => shown.OpenPreview(left));

        TrayDesktop.Run(shown.Deactivate);
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => !shown.Window.IsVisible));
        TrayDesktop.Run(shown.Window.SummonFromTray);
        await TrayDesktop.Get(() => shown.Listing.Settle());

        TrayDesktop.Run(() =>
        {
            var expected = Summon.AboveTrayIcon(null, Summon.DisplaysOf(shown.Window.Screens), WindowSize.FlyoutDefault);
            Assert.IsTrue(shown.Window.IsVisible);
            Assert.AreEqual(expected.Position, shown.Window.Position, "placed by the tray's summon, here with no icon to stand above (G1)");
            Assert.AreEqual(left.Id, shown.Preview.Item?.Id, "the preview open on the same item");
            Assert.AreSame(shown.PreviewPicture, shown.Focused, "focus inside it");
            Assert.IsFalse(shown.Search.IsFocused, "not in the box it covers");
            Assert.AreEqual("summon", shown.Search.Text, "the query as it was left");
            Assert.AreEqual("summon", shown.Listing.Query);
            CollectionAssert.AreEqual(new[] { left.Id }, shown.Listing.Selection.ToList(), "and the selection");
        });

        TrayDesktop.Run(() => ShownWindow.Press(shown.PreviewPicture, Avalonia.Input.Key.Escape));
        TrayDesktop.Run(shown.Window.Hide);
        TrayDesktop.Run(shown.Window.SummonFromTray);
        await TrayDesktop.Get(() => shown.Listing.Settle());

        TrayDesktop.Run(() =>
        {
            Assert.IsTrue(shown.Search.IsFocused, "with the preview closed, the box focused");
            Assert.AreEqual("summon", shown.Listing.Query, "the search kept (G1)");
            Assert.AreEqual("summon", shown.Search.SelectedText, "and selected, so that typing replaces it (G1)");
            CollectionAssert.AreEqual(new[] { left.Id }, shown.Listing.Selection.ToList(), "the selection kept");
            Assert.HasCount(2, shown.Listing.Items, "the matches");
        });
    }

    [TestMethod]
    public async Task AScopeAndATileLeftAreThereAtEverySummonFromTheTrayTheShortcutAndPinned()
    {
        using var shown = ShownWindow.Open();
        var items = await shown.OpenStash(shown.Picture("kept one.png"), shown.Picture("kept two.png"), shown.Picture("kept three.png"), shown.Picture("kept other.png"));
        await shown.Listing.Store!.SetFavorite(items.Take(3).ToList(), true);
        await TrayDesktop.Get(() => shown.Listing.ShowScope(Scope.Favorites));
        var third = TrayDesktop.Get(() => shown.Listing.Items[2].Id);
        TrayDesktop.Run(() => shown.Listing.Focus(third));
        void Kept(string how)
        {
            Assert.AreEqual(Scope.Favorites, shown.Listing.LitChip, $"{how}: the scope it was left on (In use, G1)");
            CollectionAssert.AreEqual(new[] { third }, shown.Listing.Selection.ToList(), $"{how}: the third tile still selected");
            Assert.IsTrue(shown.Search.IsFocused, $"{how}: the box focused");
        }

        TrayDesktop.Run(() => ShownWindow.Press(shown.Search, Avalonia.Input.Key.Escape));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => !shown.Window.IsVisible));
        TrayDesktop.Run(shown.Window.SummonFromTray);
        TrayDesktop.Run(() => Kept("from the tray"));

        TrayDesktop.Run(shown.Window.Hide);
        TrayDesktop.Run(() => shown.Window.SummonFromShortcut(new ForegroundReading(false, null), new SummonAnchor(null)));
        TrayDesktop.Run(() => Kept("from the shortcut"));

        TrayDesktop.Run(shown.Window.Hide);
        TrayDesktop.Run(shown.Window.SummonPinned);
        TrayDesktop.Run(() => Kept("pinned"));
    }

    [TestMethod]
    public async Task ALetterTypedAfterASummonReplacesTheQueryItKeptAndItsFirstMatchIsHandedOver()
    {
        using var shown = ShownWindow.Open();
        var items = await shown.OpenStash(shown.Picture("carrot.png"), shown.Picture("cat one.png"), shown.Picture("cat two.png"), shown.Picture("dog.png"));
        TrayDesktop.Run(() => shown.Search.Text = "ca");
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled && shown.Listing.Items.Count == 3));
        var second = TrayDesktop.Get(() => shown.Listing.Items[1].Id);
        TrayDesktop.Run(() => shown.Listing.Focus(second));
        TrayDesktop.Run(shown.Deactivate);
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => !shown.Window.IsVisible));

        TrayDesktop.Run(() => shown.Window.SummonFromShortcut(new ForegroundReading(false, null), new SummonAnchor(null)));
        TrayDesktop.Run(() =>
        {
            Assert.AreEqual("ca", shown.Search.SelectedText);
            CollectionAssert.AreEqual(new[] { second }, shown.Listing.Selection.ToList(), "no reload moved it");
            ShownWindow.Type(shown.Search, "d");
        });
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled && shown.Listing.Query == "d"));

        var dog = items.Single(item => item.Name == "dog");
        TrayDesktop.Run(() =>
        {
            Assert.AreEqual("d", shown.Search.Text, "typing replaced it (G1)");
            CollectionAssert.AreEqual(new[] { dog.Id }, shown.Listing.Selection.ToList(), "its first match selected (G5)");
            ShownWindow.Press(shown.Search, Avalonia.Input.Key.Enter);
        });
        await ShownWindow.Eventually(() => shown.Uses() == 1);
        CollectionAssert.AreEqual(new[] { dog.FilePath }, TestClipboard.Read().Files.ToList(), "G8");
    }

    [TestMethod]
    public async Task TheSearchClearedByEscIsNotBroughtBackAndTheScopeTheUserWasInIs()
    {
        using var shown = ShownWindow.Open();
        await shown.OpenStash(shown.Picture("esc one.png"), shown.Picture("esc two.png"));
        await TrayDesktop.Get(() => shown.Listing.ShowScope(Scope.Recent));
        TrayDesktop.Run(() => shown.Search.Text = "esc");
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled && shown.Listing.Query == "esc"));

        TrayDesktop.Run(() => ShownWindow.Press(shown.Search, Avalonia.Input.Key.Escape));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled && shown.Listing.Query == ""));
        TrayDesktop.Run(shown.Window.Hide);
        TrayDesktop.Run(shown.Window.SummonFromTray);

        TrayDesktop.Run(() =>
        {
            Assert.AreEqual("", shown.Search.Text ?? "");
            Assert.AreEqual(Scope.Recent, shown.Listing.LitChip);
        });
    }

    [TestMethod]
    public async Task TheShortcutHidesTheFlyoutWithThePreviewOpenAndAgainShowsItAtThePointerAsItWasLeft()
    {
        using var shown = ShownWindow.Open();
        var items = await shown.OpenStash(shown.Picture("shortcut kept one.png"), shown.Picture("shortcut kept two.png"));
        var left = TrayDesktop.Get(() => shown.Listing.Items[1]);
        TrayDesktop.Run(() => shown.OpenPreview(left));
        var area = TrayDesktop.Get(() => shown.Window.Screens.Primary!.WorkingArea);
        var pointer = new PixelPoint(area.X + 40, area.Y + 40);

        TrayDesktop.Run(() => shown.Window.SummonFromShortcut(new ForegroundReading(true, null), new SummonAnchor(pointer)));
        TrayDesktop.Run(() => Assert.IsFalse(shown.Window.IsVisible, "the shortcut hides the showing flyout, the preview open in it (G2)"));

        TrayDesktop.Run(() => shown.Window.SummonFromShortcut(new ForegroundReading(false, null), new SummonAnchor(pointer)));
        await TrayDesktop.Get(() => shown.Listing.Settle());

        TrayDesktop.Run(() =>
        {
            Assert.IsTrue(shown.Window.IsVisible);
            Assert.AreEqual(pointer, shown.Window.Position, "placed by the shortcut's summon, at the pointer (G1)");
            Assert.AreEqual(left.Id, shown.Preview.Item?.Id, "the preview open on the same item");
            Assert.AreSame(shown.PreviewPicture, shown.Focused, "focus inside it, not in the box");
            CollectionAssert.AreEqual(new[] { left.Id }, shown.Listing.Selection.ToList(), "the selection as it was left, not the first item");
        });
    }

    [TestMethod]
    public async Task AFlyoutHiddenByAnotherAppWithThePreviewOpenComesBackFromTheShortcutBesideTheCaretAsItWasLeft()
    {
        using var shown = ShownWindow.Open();
        await shown.OpenStash(shown.Picture("caret kept one.png"), shown.Picture("caret kept two.png"));
        var left = TrayDesktop.Get(() => shown.Listing.Items[1]);
        TrayDesktop.Run(() => shown.OpenPreview(left));
        TrayDesktop.Run(shown.Deactivate);
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => !shown.Window.IsVisible));
        var area = TrayDesktop.Get(() => shown.Window.Screens.Primary!.WorkingArea);
        var scaling = TrayDesktop.Get(() => shown.Window.Screens.Primary!.Scaling);
        var caret = new PixelRect(area.X + 300, area.Bottom - 60, 1, 16);

        TrayDesktop.Run(() => shown.Window.SummonFromShortcut(new ForegroundReading(false, null), new SummonAnchor(new PixelPoint(area.X + 40, area.Y + 40), caret)));
        await TrayDesktop.Get(() => shown.Listing.Settle());

        TrayDesktop.Run(() =>
        {
            var window = shown.Window;
            var outer = new PixelRect(window.Position, PixelSize.FromSize(window.FrameSize!.Value, window.RenderScaling));
            Assert.IsTrue(window.IsVisible);
            Assert.AreEqual(caret.X - (int)Math.Round(Summon.CaretInset * scaling), outer.X, "beside the caret, not at the pointer (G1)");
            Assert.IsLessThanOrEqualTo(2, Math.Abs(caret.Y - (int)Math.Round(Summon.CaretGap * scaling) - outer.Bottom), $"its bottom {outer.Bottom} just above the caret's line at {caret.Y}");
            Assert.AreEqual(left.Id, shown.Preview.Item?.Id, "the preview open on the same item");
            Assert.AreSame(shown.PreviewPicture, shown.Focused, "focus inside it, the box not focused again");
        });
    }

    [TestMethod]
    public async Task APinnedWindowMinimisedWithThePreviewOpenComesForwardWhereItIsAsItWasLeftItsAnimationHeldMeanwhile()
    {
        using var shown = ShownWindow.Open();
        var items = await shown.OpenStash(shown.Fixture("long.gif", "pinned dancing.gif"), shown.Picture("pinned still.png"));
        var animated = items.Single(item => item.Name == "pinned dancing");
        TrayDesktop.Run(shown.Window.SummonPinned);
        await TrayDesktop.Get(() => shown.Listing.Settle());
        TrayDesktop.Run(() => shown.Search.Text = "pinned");
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled && shown.Listing.Items.Count == 2));
        TrayDesktop.Run(() => shown.OpenPreview(animated));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.PreviewPicture.Image is not null));
        var where = TrayDesktop.Get(() => shown.Window.Position);

        TrayDesktop.Run(() => shown.Window.WindowState = WindowState.Minimized);
        var heldOn = TrayDesktop.Get(() => shown.PreviewPicture.Image);
        await Task.Delay(700);
        TrayDesktop.Run(() => Assert.AreSame(heldOn, shown.PreviewPicture.Image, "minimised, its animation plays nothing (G6)"));

        TrayDesktop.Run(shown.Window.SummonFromTray);
        await TrayDesktop.Get(() => shown.Listing.Settle());

        TrayDesktop.Run(() =>
        {
            Assert.AreEqual(WindowState.Normal, shown.Window.WindowState, "brought forward");
            Assert.AreEqual(where, shown.Window.Position, "where it is (G1)");
            Assert.AreEqual(animated.Id, shown.Preview.Item?.Id, "as it was left: the preview open on its item");
            Assert.AreEqual("pinned", shown.Listing.Query, "the query kept");
            Assert.AreSame(shown.PreviewPicture, shown.Focused, "focus inside the preview");
        });
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => !ReferenceEquals(heldOn, shown.PreviewPicture.Image)));
    }

    [TestMethod]
    public async Task ASummonWithThePreviewOpenStatesHowRecentlyItsItemWasUsedAsItIsNowNotAsItWasWhenLeft()
    {
        using var shown = ShownWindow.Open();
        var item = (await shown.OpenStash(shown.Picture("used then.png"))).Single();
        TrayDesktop.Run(() => shown.OpenPreview(item));
        TrayDesktop.Run(() => ShownWindow.Press(shown.PreviewPicture, Avalonia.Input.Key.C, Avalonia.Input.KeyModifiers.Control));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Preview.Facts.Count == 2 && shown.Preview.Facts[1].EndsWith("Used once, just now")));

        TrayDesktop.Run(shown.Deactivate);
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => !shown.Window.IsVisible));
        ((StoppedClock)shown.Notices.Clock).Advance(TimeSpan.FromMinutes(150));
        TrayDesktop.Run(shown.Window.SummonFromTray);

        // Brought forward as it was left, the preview on its item, saying what is true at this summon (G1, G19).
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Preview.Facts.Count == 2 && shown.Preview.Facts[1].EndsWith("Used once, 2 hr ago")));
        TrayDesktop.Run(() => Assert.AreEqual(item.Id, shown.Preview.Item?.Id));
    }
}
