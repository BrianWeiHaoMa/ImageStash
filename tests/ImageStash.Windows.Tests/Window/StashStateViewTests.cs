using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using ImageStash.Store;
using ImageStash.Window;
using ImageStash.Windows.Tests.Support;
using static ImageStash.Windows.Tests.Support.ShownWindow;

namespace ImageStash.Windows.Tests.Window;

/// <summary>
/// What replaces the grid through the real window (G2, G5, G35, F5): while the stash opens, the opening named with its
/// progress and nothing of the stash; once it has opened empty, the empty stash listing every way in as a row, Take a
/// screenshot, Record a clip and Stash the clipboard image each with its shortcut's key caps as the settings hold it when
/// the empty stash is shown, or the tray menu while that is not a shortcut, and the line naming the open shortcut; a
/// real click on a row runs its act and leaves the focus in the search box (G7), a drop released over a row is taken as
/// one on the window (G11), and at the window's minimum size every row, cap and the line lie inside the view (L2); a
/// query matching nothing, named with a Clear that empties the search.
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

        var (lines, ways) = TrayDesktop.Get(() => (ShownEmptyStash.Lines(shown), ShownEmptyStash.Ways(shown)));

        CollectionAssert.AreEqual(new[] { "Your stash is empty", "Drop images and GIFs here, from a browser, a chat or File Explorer", OpenLine("Ctrl+Alt+Space") }, lines, string.Join(" | ", lines));
        CollectionAssert.AreEqual(new[] { "Paste a copied image · Ctrl+V", $"Take a screenshot · {screenshotShownAs}", $"Record a clip · {recordShownAs}", $"Stash the clipboard image · {stashShownAs}", "Import files · Ctrl+O" }, ways,
            $"each shortcut as the settings read it when the empty stash is shown, else the tray menu (G2, F5): {string.Join(" | ", ways)}");
    }

    [TestMethod]
    public async Task AShortcutRemappedInSettingsShowsInTheEmptyStashAndThePlusMenuAtOnce()
    {
        using var shown = ShownWindow.Open();
        await shown.OpenStash();
        Assert.Contains("Take a screenshot · Ctrl+PrtScn", TrayDesktop.Get(() => ShownEmptyStash.Ways(shown)));
        var remapped = new KeyGesture(Key.F8, KeyModifiers.Control | KeyModifiers.Shift);

        // As a rebind in Settings writes the settings and tells the window.
        TrayDesktop.Run(() =>
        {
            shown.Settings.Change(s => s with { Shortcuts = s.Shortcuts with { Screenshot = "Ctrl+Shift+F8", Record = "Ctrl+Shift+F9", Open = "Ctrl+Alt+F7" } });
            shown.Window.ShortcutsChanged();
        });

        var (ways, line) = TrayDesktop.Get(() => (ShownEmptyStash.Ways(shown), ShownEmptyStash.Lines(shown)[^1]));
        CollectionAssert.IsSubsetOf(new[] { "Take a screenshot · Ctrl+Shift+F8", "Record a clip · Ctrl+Shift+F9" }, ways, $"the empty stash lists the new shortcuts at once (G2): {string.Join(" | ", ways)}");
        Assert.AreEqual(OpenLine("Ctrl+Alt+F7"), line, "and its line the new open shortcut");
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
    public async Task ARealClickOnARowRunsItsActOnceAndLeavesTheFocusInTheSearchBox()
    {
        using var shown = ShownWindow.Open();
        using var pointer = TestMouse.PutBackAfterwards();
        await shown.OpenStash();
        var asked = new List<string>();
        TrayDesktop.Run(() =>
        {
            shown.Window.ScreenshotRequested += () => asked.Add("screenshot");
            shown.Window.StashClipboardImageRequested += () => asked.Add("stash clipboard image");
            shown.Window.SummonPinned();
        });
        await Eventually(() => TrayDesktop.Get(() => shown.Window.IsActive && shown.Search.IsFocused));
        await Task.Delay(200);

        foreach (var (words, act) in new[] { ("Stash the clipboard image", "stash clipboard image"), ("Take a screenshot", "screenshot") })
        {
            asked.Clear();
            TestMouse.Click(TestMouse.Centre(TrayDesktop.Get(() => ShownEmptyStash.Row(shown, words))));
            await Eventually(() => TrayDesktop.Get(() => asked.Count > 0));
            TrayDesktop.Run(() =>
            {
                CollectionAssert.AreEqual(new[] { act }, asked, $"a click on {words} asks for its act once, as the tray menu's or the + menu's entry does (G13, G14)");
                Assert.IsTrue(shown.Search.IsFocused, $"after a click on {words} the focus is where it was (G7)");
                Assert.IsFalse(ShownEmptyStash.Row(shown, words).IsFocused);
            });
        }
    }

    [TestMethod]
    public async Task ADropReleasedOverARowIsTakenAsOneOnTheWindow()
    {
        using var shown = ShownWindow.Open();
        await shown.OpenStash();
        var data = await shown.Files(shown.Picture("dropped on a row.png"));

        TrayDesktop.Run(() =>
        {
            var row = ShownEmptyStash.Row(shown, "Record a clip");
            RaiseDrag(row, DragDrop.DragEnterEvent, data);
            RaiseDrag(row, DragDrop.DragOverEvent, data);
            RaiseDrag(row, DragDrop.DropEvent, data);
        });

        await Eventually(() => TrayDesktop.Get(() => shown.Listing.Items.Any(item => item.Name == "dropped on a row")));
        await TrayDesktop.Get(() => shown.Listing.Settle());
        TrayDesktop.Run(() =>
        {
            Assert.IsNull(ShownEmptyStash.View(shown), "the empty stash gives way to the grid with its first item (G2, G11)");
            Assert.IsTrue(shown.Grid.IsEffectivelyVisible);
        });
    }

    [TestMethod]
    public async Task AtTheWindowsMinimumEveryRowItsCapsAndTheLineLieInsideTheView()
    {
        using var shown = ShownWindow.Open();
        TrayDesktop.Run(() => shown.Settings.Change(s => s with { Shortcuts = s.Shortcuts with { Record = "Ctrl+Alt+Shift+F12" } }));
        await shown.OpenStash();
        TrayDesktop.Run(() =>
        {
            shown.Window.Width = 1;
            shown.Window.Height = 1;
        });
        await Eventually(() => TrayDesktop.Get(() =>
        {
            shown.Window.UpdateLayout();
            return Math.Abs(shown.Window.Bounds.Height - WindowSize.Minimum.Height) < 1;
        }));

        TrayDesktop.Run(() =>
        {
            shown.Window.UpdateLayout();
            WindowMinimum.AssertAt(shown.Window.Bounds.Size, "the window at its minimum");
            var view = ShownEmptyStash.View(shown)!;
            // The space between the chip row and the footer, which the grid and the state views share.
            var body = view.GetVisualAncestors().OfType<Panel>().First(panel => panel.Children.OfType<ScrollViewer>().Any());
            var controls = new List<Control>(view.Children.OfType<TextBlock>());
            foreach (var row in ShownEmptyStash.Rows(shown))
            {
                controls.Add(row);
                controls.AddRange(ShownEmptyStash.Caps(row));
            }

            Assert.HasCount(3 + 5 + 2 + 2 + 4 + 3 + 2, controls, "the three lines, the five rows and their caps");
            foreach (var control in controls)
            {
                var at = control.TranslatePoint(default, body)!.Value;
                Assert.IsTrue(control.IsEffectivelyVisible && at.X >= -0.5 && at.Y >= -0.5 && at.X + control.Bounds.Width <= body.Bounds.Width + 0.5 && at.Y + control.Bounds.Height <= body.Bounds.Height + 0.5,
                    $"{(control as TextBlock)?.Text ?? Avalonia.Automation.AutomationProperties.GetName(control)} at {new Rect(at, control.Bounds.Size)} inside the {body.Bounds.Size} space between the chips and the footer (L2)");
            }
        });
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

    private static string OpenLine(string open) => $"Open ImageStash from any app with {open}, or from its icon in the tray. Closing this window keeps it running.";

    /// <summary>The words of the lines of the view that shows <paramref name="text"/>, in order.</summary>
    private static List<string> LinesUnder(ShownWindow shown, string text) =>
        ((Panel)Shown(shown, text).Parent!).Children.OfType<TextBlock>().Select(block => block.Text ?? "").ToList();
}
