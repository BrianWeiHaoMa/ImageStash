using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using ImageStash.Seams;
using ImageStash.Window;
using ImageStash.Windows.Tests.Support;
using static ImageStash.Windows.Tests.Support.ShownWindow;

namespace ImageStash.Windows.Tests.Window;

/// <summary>
/// What the footer and item menu say as the selection changes, and what the footer shows after a copy from the grid.
/// The window takes real keys and writes to the real clipboard. Relay is the paste target the check presets.
/// </summary>
[TestClass]
public sealed class FooterHintsTests
{
    private static readonly TimeSpan HintTimeout = TimeSpan.FromSeconds(5);
    private static readonly Shortcut SelectAll = Shortcut.Parse("Ctrl+A").Value!;
    private static readonly Shortcut Copy = Shortcut.Parse("Ctrl+C").Value!;

    [TestMethod]
    public async Task TheFooterAndItemMenuCountThreeSelectedPicturesAndDropTheCountWhenOneTileIsClicked()
    {
        using var pointer = TestMouse.PutBackAfterwards();
        using var shown = Open(target: new PresetTarget { Before = PresetTarget.App("Relay") });
        var items = await shown.OpenStash(shown.Picture("one.png"), shown.Picture("two.png"), shown.Picture("three.png"));
        await shown.ClickSearchInFront();
        TrayDesktop.Run(() =>
        {
            shown.Listing.Focus(items[0].Id);
            shown.Grid.Focus();
        });

        TestHotKeys.Press(SelectAll);
        await Eventually(() => TrayDesktop.Get(() => shown.FooterHints().SequenceEqual(["Enter · Paste 3 items into Relay", "Ctrl+C · Copy"])), HintTimeout);
        var several = TrayDesktop.Get(() => shown.RightClick(items[1].Id));
        CollectionAssert.AreEqual(new[] { "Paste 3 items into Relay", "Copy 3 items" }, several.Take(2).ToList());

        TrayDesktop.Run(() =>
        {
            shown.CloseMenu();
            shown.Press(items[2].Id);
            shown.Release(items[2].Id);
            CollectionAssert.AreEqual(new[] { items[2].Id }, shown.Listing.Selection.ToList());
        });
        CollectionAssert.AreEqual(new[] { "Enter · Paste into Relay", "Ctrl+C · Copy" }, TrayDesktop.Get(shown.FooterHints));
        var one = TrayDesktop.Get(() => shown.RightClick(items[2].Id));
        CollectionAssert.AreEqual(new[] { "Paste into Relay", "Copy" }, one.Take(2).ToList());
    }

    [TestMethod]
    public async Task CtrlCNamesTheCopiedPictureInTheFooterThenThePasteAndCopyHintsReturn()
    {
        using var pointer = TestMouse.PutBackAfterwards();
        using var shown = Open(target: new PresetTarget { Before = PresetTarget.App("Relay") }, noticeClock: TimeProvider.System);
        var item = (await shown.OpenStash(shown.Picture("the audacity.png"))).Single();
        await shown.ClickSearchInFront();
        TrayDesktop.Run(() => shown.Grid.Focus());
        string[] hints = ["Enter · Paste into Relay", "Ctrl+C · Copy"];
        await Eventually(() => TrayDesktop.Get(shown.FooterHints).SequenceEqual(hints), HintTimeout);

        TestHotKeys.Press(Copy);

        await Eventually(() => TrayDesktop.Get(shown.FooterHints).SequenceEqual(["✓ Copied “the audacity”"]), HintTimeout);
        CollectionAssert.AreEqual(new[] { item.FilePath }, TestClipboard.Read().Files.ToList());
        await Eventually(() => TrayDesktop.Get(shown.FooterHints).SequenceEqual(hints), HintTimeout);
    }

    [TestMethod]
    public async Task ALongNameInTheConfirmationIsCutShortBeforeTheCountNeverDrawnOverIt()
    {
        const string name = "when the deployment finishes on friday and everyone remembers the release notes";
        using var pointer = TestMouse.PutBackAfterwards();
        using var shown = Open(noticeClock: TimeProvider.System);
        var items = await shown.OpenStash(shown.Picture(name + ".png"), shown.Picture("other.png"));
        await shown.ClickSearchInFront();
        TrayDesktop.Run(() =>
        {
            shown.Listing.Focus(items.Single(item => item.Name == name).Id);
            shown.Grid.Focus();
        });

        TestHotKeys.Press(Copy);

        await Eventually(() => TrayDesktop.Get(shown.FooterHints).SequenceEqual([$"✓ Copied “{name}”"]), HintTimeout);
        TrayDesktop.Run(() =>
        {
            var texts = shown.Window.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible).ToList();
            var confirmation = texts.Single(text => text.Text!.StartsWith("✓ Copied "));
            var count = texts.Single(text => text.Text == StashStateView.CountText(2, 1));
            var confirmationRight = confirmation.TranslatePoint(new Point(confirmation.Bounds.Width, 0), shown.Window)!.Value.X;
            var countLeft = count.TranslatePoint(new Point(0, 0), shown.Window)!.Value.X;
            Assert.IsLessThanOrEqualTo(countLeft, confirmationRight, "the name is cut short with an ellipsis before the count");
        });
    }

    [TestMethod]
    public async Task WithoutATargetEnterCopiesThreeSelectedPicturesAndTheFooterConfirmsTheCount()
    {
        const ushort enter = 0x0D;
        using var pointer = TestMouse.PutBackAfterwards();
        using var shown = Open(noticeClock: TimeProvider.System);
        var items = await shown.OpenStash(shown.Picture("first.png"), shown.Picture("second.png"), shown.Picture("third.png"));
        await shown.ClickSearchInFront();
        TrayDesktop.Run(() =>
        {
            shown.Listing.Focus(items[0].Id);
            shown.Grid.Focus();
        });
        TestHotKeys.Press(SelectAll);
        await Eventually(() => TrayDesktop.Get(() => shown.Listing.Selection.Count == 3));
        string[] hints = ["Enter · Copy 3 items"];
        CollectionAssert.AreEqual(hints, TrayDesktop.Get(shown.FooterHints));

        TestHotKeys.Tap(enter);

        await Eventually(() => TrayDesktop.Get(shown.FooterHints).SequenceEqual(["✓ Copied 3 items"]), HintTimeout);
        CollectionAssert.AreEquivalent(items.Select(item => item.FilePath).ToList(), TestClipboard.Read().Files.ToList());
        await Eventually(() => TrayDesktop.Get(shown.FooterHints).SequenceEqual(hints), HintTimeout);
        var menu = TrayDesktop.Get(() => shown.RightClick(items[0].Id));
        Assert.AreEqual("Copy 3 items", menu[0]);
    }
}
