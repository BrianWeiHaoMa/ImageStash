using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using ImageStash.Window;
using ImageStash.Windows.Tests.Support;

namespace ImageStash.Windows.Tests.Window;

/// <summary>
/// What replaces the grid through the real window (G2, G5, G35, F5): while the stash opens, the opening named with its
/// progress and nothing of the stash; once it has opened empty, the empty stash listing every way in, Take a
/// screenshot, Record a clip and Stash the clipboard image each with its shortcut as the settings hold it when the empty
/// stash is shown, or the tray menu while that is not a shortcut; a query matching nothing, named with a Clear that
/// empties the search.
/// </summary>
[TestClass]
public sealed class StashStateViewTests
{
    [TestMethod]
    [DataRow("shift+prtscn", "Shift+PrtScn", "alt+prtscn", "Alt+PrtScn", "Ctrl+Shift+V", "Ctrl+Shift+V")]
    [DataRow("Ctrl+PrtScn", "Ctrl+PrtScn", "Ctrl+Shift+PrtScn", "Ctrl+Shift+PrtScn", "nonsense", "from the tray menu")]
    [DataRow("nonsense", "from the tray menu", "Ctrl+Shift+PrtScn", "Ctrl+Shift+PrtScn", "Ctrl+Alt+V", "Ctrl+Alt+V")]
    [DataRow("Ctrl+PrtScn", "Ctrl+PrtScn", "", "from the tray menu", "Ctrl+Alt+V", "Ctrl+Alt+V")]
    public async Task TheEmptyStashNamesTheCaptureShortcutsAsTheSettingsHoldThem(string screenshot, string screenshotShownAs, string record, string recordShownAs, string stash, string stashShownAs)
    {
        using var shown = ShownWindow.Open();
        // Changed after the window was made and before the stash opens empty, as when Settings changed them meanwhile.
        TrayDesktop.Run(() => shown.Settings.Change(s => s with { Shortcuts = s.Shortcuts with { Screenshot = screenshot, Record = record, StashClipboardImage = stash } }));
        await shown.OpenStash();

        var lines = TrayDesktop.Get(() => LinesUnder(shown, "Your stash is empty"));

        CollectionAssert.AreEqual(new[] { "Your stash is empty", "Drop images and GIFs here, from a browser, a chat or File Explorer", "Paste a copied image    Ctrl+V", $"Take a screenshot    {screenshotShownAs}", $"Record a clip    {recordShownAs}", $"Stash the clipboard image    {stashShownAs}", "Import files    Ctrl+O" }, lines,
            $"each shortcut as the settings read it when the empty stash is shown, else the tray menu (G2, F5): {string.Join(" | ", lines)}");
    }

    [TestMethod]
    public async Task AShortcutRemappedInSettingsShowsInTheEmptyStashAndThePlusMenuAtOnce()
    {
        using var shown = ShownWindow.Open();
        await shown.OpenStash();
        Assert.Contains("Take a screenshot    Ctrl+PrtScn", TrayDesktop.Get(() => LinesUnder(shown, "Your stash is empty")));
        var remapped = new KeyGesture(Key.F8, KeyModifiers.Control | KeyModifiers.Shift);

        // As a rebind in Settings writes the settings and tells the window.
        TrayDesktop.Run(() =>
        {
            shown.Settings.Change(s => s with { Shortcuts = s.Shortcuts with { Screenshot = "Ctrl+Shift+F8", Record = "Ctrl+Shift+F9" } });
            shown.Window.ShortcutsChanged();
        });

        var lines = TrayDesktop.Get(() => LinesUnder(shown, "Your stash is empty"));
        CollectionAssert.IsSubsetOf(new[] { "Take a screenshot    Ctrl+Shift+F8", "Record a clip    Ctrl+Shift+F9" }, lines, $"the empty stash lists the new shortcuts at once (G2): {string.Join(" | ", lines)}");
        try
        {
            TrayDesktop.Run(() => shown.AddButton.Flyout!.ShowAt(shown.AddButton));
            var entries = TrayDesktop.Get(() => shown.AddMenu().ToDictionary(entry => (string)entry.Header!, entry => entry.InputGesture));
            Assert.AreEqual(remapped, entries["Take a screenshot"], "and the + menu at its next opening (G34)");
            Assert.AreEqual(new KeyGesture(Key.F9, KeyModifiers.Control | KeyModifiers.Shift), entries["Record a clip"]);
        }
        finally
        {
            TrayDesktop.Run(() => shown.AddButton.Flyout!.Hide());
        }
    }

    [TestMethod]
    public async Task WhileTheStashOpensTheWindowSaysSoWithItsProgressAndOnceOpenedEmptyShowsTheEmptyStash()
    {
        using var shown = ShownWindow.Open();
        TrayDesktop.Run(shown.Window.SummonFromTray);

        TrayDesktop.Run(() =>
        {
            CollectionAssert.AreEqual(new[] { "Opening your stash…" }, LinesUnder(shown, "Opening your stash…"), "the opening named (G35)");
            var view = (Panel)Shown(shown, "Opening your stash…").Parent!;
            Assert.IsTrue(view.Children.OfType<ProgressBar>().Single().IsIndeterminate, "with its progress");
            Assert.IsNull(ShownOrNull(shown, "Your stash is empty"), "not the empty stash while it is still opening");
            Assert.IsFalse(shown.Window.GetVisualDescendants().OfType<ThumbnailGrid>().Any(grid => grid.IsEffectivelyVisible), "and nothing of the stash");
        });

        await shown.OpenStash();

        TrayDesktop.Run(() =>
        {
            Assert.IsNotNull(ShownOrNull(shown, "Your stash is empty"), "the empty stash once it has opened (G2)");
            Assert.IsNull(ShownOrNull(shown, "Opening your stash…"));
        });
    }

    [TestMethod]
    public async Task NoMatchNamesTheQueryApartFromTheEmptyStashAndItsClearEmptiesTheSearch()
    {
        using var shown = ShownWindow.Open();
        await shown.OpenStash(shown.Picture("state cat.png"));
        TrayDesktop.Run(() => shown.Search.Text = "zebra");
        await TrayDesktop.Get(() => shown.Listing.Settle());

        var clear = TrayDesktop.Get(() =>
        {
            var view = (Panel)Shown(shown, "Nothing matches “zebra”").Parent!;
            Assert.IsNull(ShownOrNull(shown, "Your stash is empty"), "distinct from the empty stash (G5)");
            Assert.IsFalse(shown.Grid.IsEffectivelyVisible, "in place of the grid");
            return view.Children.OfType<Button>().Single(button => button.Content as string == "Clear search");
        });
        TrayDesktop.Run(() => ShownWindow.RaiseClick(clear));
        await TrayDesktop.Get(() => shown.Listing.Settle());

        TrayDesktop.Run(() =>
        {
            Assert.AreEqual("", shown.Search.Text, "Clear empties the search box (G5)");
            Assert.IsNull(ShownOrNull(shown, "Nothing matches “zebra”"));
            Assert.IsTrue(shown.Grid.IsEffectivelyVisible, "the grid again, with the item");
            Assert.HasCount(1, shown.Listing.Items);
        });
    }

    private static TextBlock? ShownOrNull(ShownWindow shown, string text) =>
        shown.Window.GetVisualDescendants().OfType<TextBlock>().SingleOrDefault(block => block.IsEffectivelyVisible && block.Text == text);

    private static TextBlock Shown(ShownWindow shown, string text) => ShownOrNull(shown, text) ?? throw new AssertFailedException($"“{text}” is not shown");

    /// <summary>The words of the lines of the view that shows <paramref name="text"/>, in order.</summary>
    private static List<string> LinesUnder(ShownWindow shown, string text) =>
        ((Panel)Shown(shown, text).Parent!).Children.OfType<TextBlock>().Select(block => block.Text ?? "").ToList();
}
