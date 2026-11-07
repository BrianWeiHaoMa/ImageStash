using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.VisualTree;
using ImageStash.Seams;
using ImageStash.Store;
using ImageStash.Window;
using ImageStash.Windows.Tests.Support;
using static ImageStash.Windows.Tests.Support.ShownTagLists;
using static ImageStash.Windows.Tests.Support.ShownWindow;

namespace ImageStash.Windows.Tests.Window;

/// <summary>
/// The tag list a click opens, on the chip row's last chip in the real stash window: shown only when the row cannot hold
/// its chips, whole and last (G4); a popup of the window, never a window of its own (F4), listing every tag, the
/// commonest first, the lit ones marked, whatever the query (G4); a pick adding or taking out its word, the list staying
/// open and focus in the box (G4, G7); Esc closing it and nothing else (G2); a real press outside closing it and acting
/// as it would have (G7); sixty tags scrolling inside the display (G4); standing as the item menu's Add tag ▸ list does,
/// its bar at its edge and its words as clear of the bar, and as wide from the moment it opens as its widest name needs,
/// by the words' own width, a name wider than the list trimmed with its tip. Its entries are found as the user sees them.
/// </summary>
[TestClass]
public sealed class TagPickerTests
{
    private const string EveryTag = "…";

    [TestMethod]
    public async Task ARowThatHoldsItsChipsHasNoLastChipAndOneThatCannotEndsWithItWhole()
    {
        using var shown = Open();
        var item = (await shown.OpenStash(shown.Picture("few tags.png"))).Single();
        await shown.Tag([item], "dog cat");
        TrayDesktop.Run(() => CollectionAssert.DoesNotContain(shown.ShownChips(), EveryTag, "every chip fits (G4)"));

        var (_, tags) = await TwelveLong(shown, item);

        TrayDesktop.Run(() =>
        {
            var chips = shown.ShownChips();
            Assert.AreEqual(EveryTag, chips[^1], "it ends the row, itself whole (G4)");
            Assert.IsLessThan(3 + 1 + tags.Count + 2, chips.Count, "the row does not hold them all");
            Assert.IsLessThanOrEqualTo(shown.ChipRow.Bounds.Width, shown.Chip(EveryTag).Bounds.Right);
            Assert.IsFalse(shown.Chip(EveryTag).Focusable, "Tab goes from the box to the grid (G7)");
        });
    }

    [TestMethod]
    public async Task ItsClickListsEveryTagTheCommonestFirstInAPopupOfTheWindowLeavingFocusInTheBox()
    {
        using var shown = Open();
        var (item, tags) = await TwelveLong(shown);
        await TrayDesktop.Get(() => shown.Listing.Store!.AddTag([item], "extra"));
        var other = (await shown.AddTo("second carrier.png")).Single();
        await shown.Tag([other], tags[^1]);

        TrayDesktop.Run(() =>
        {
            shown.Search.Focus();
            RaiseClick(shown.Chip(EveryTag));
        });

        TrayDesktop.Run(() =>
        {
            var picker = shown.ChipRow.Picker;
            Assert.IsTrue(picker.IsOpen);
            Assert.AreEqual(tags[^1], picker.Entries[0].Tag, "the commonest first (G4)");
            CollectionAssert.AreEquivalent(tags.Append("extra").ToList(), picker.Entries.Select(entry => entry.Tag).ToList(), "every existing tag");
            Assert.IsFalse(picker.Entries.Any(entry => entry.Marked), "none lit");
            Assert.IsNotNull(TopLevel.GetTopLevel(picker.Child!), "a popup's own root");
            Assert.AreNotSame(shown.Window, TopLevel.GetTopLevel(picker.Child!), "not the window's tree, and no window of its own (F4): a PopupRoot");
            Assert.IsInstanceOfType<Avalonia.Controls.Primitives.PopupRoot>(TopLevel.GetTopLevel(picker.Child!));
            Assert.IsTrue(shown.Search.IsFocused, "focus stays in the box (G4)");
        });
        TrayDesktop.Run(() => RaiseClick(shown.Chip(EveryTag)));
        TrayDesktop.Run(() => Assert.IsFalse(shown.ChipRow.Picker.IsOpen, "its chip again closes it"));
    }

    [TestMethod]
    public async Task APickAddsItsWordAndThatChipThenLitTheListOpenWithItMarkedAndAgainTakesItOut()
    {
        using var shown = Open();
        var (_, tags) = await TwelveLong(shown);
        var twelfth = tags[^1];
        TrayDesktop.Run(() =>
        {
            shown.Search.Focus();
            RaiseClick(shown.Chip(EveryTag));
        });

        TrayDesktop.Run(() => RaiseClick(Entry(shown.ChipRow.Picker, twelfth)));
        await Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled && shown.Listing.Query == twelfth));

        TrayDesktop.Run(() =>
        {
            Assert.AreEqual(twelfth, shown.Search.Text, "G4: the word in the box");
            Assert.AreEqual(twelfth.Length, shown.Search.CaretIndex, "its caret at the end");
            Assert.IsTrue(shown.Search.IsFocused, "focus still in the box");
            Assert.IsTrue(shown.Lit("#" + twelfth), "that chip lit");
            Assert.AreEqual("#" + twelfth, shown.ShownChips()[3], "first among the tag chips");
            Assert.IsTrue(shown.ChipRow.Picker.IsOpen, "the list still open");
            Assert.IsTrue(shown.ChipRow.Picker.Entries.Single(entry => entry.Tag == twelfth).Marked, "with it marked");
        });

        TrayDesktop.Run(() => RaiseClick(Entry(shown.ChipRow.Picker, twelfth)));
        await Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled && shown.Listing.Query == ""));

        TrayDesktop.Run(() =>
        {
            Assert.IsTrue(shown.ChipRow.Picker.IsOpen, "still open, for the next");
            Assert.IsFalse(shown.ChipRow.Picker.Entries.Any(entry => entry.Marked));
        });
    }

    [TestMethod]
    public async Task APickUnderWhichTheFewChipsFitKeepsTheListOpenAndItsChipInTheRow()
    {
        using var shown = Open();
        var (_, tags) = await TwelveLong(shown);
        var lone = (await shown.AddTo("lone carrier.png")).Single();
        await shown.Tag([lone], "lone");
        TrayDesktop.Run(() => RaiseClick(shown.Chip(EveryTag)));

        TrayDesktop.Run(() => RaiseClick(Entry(shown.ChipRow.Picker, "lone")));
        await Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled && shown.Listing.Query == "lone"));

        TrayDesktop.Run(() =>
        {
            Assert.AreEqual(EveryTag, shown.ShownChips()[^1], "one lit chip fits, and the row could not hold every tag (G4)");
            Assert.IsTrue(shown.ChipRow.Picker.IsOpen, "the list stays until Esc or a press elsewhere (G4)");
            Assert.HasCount(tags.Count + 1, shown.ChipRow.Picker.Entries);
        });
    }

    [TestMethod]
    public async Task APickUnderATypedWordItContainsTakesThatWordsPlace()
    {
        using var shown = Open();
        var (_, tags) = await TwelveLong(shown);
        var twelfth = tags[^1];
        TrayDesktop.Run(() => shown.Search.Text = "tag-12");
        await Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled && shown.Listing.Query == "tag-12"));
        TrayDesktop.Run(() => RaiseClick(shown.Chip(EveryTag)));

        TrayDesktop.Run(() => RaiseClick(Entry(shown.ChipRow.Picker, twelfth)));
        await Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled && shown.Listing.Query == twelfth));

        TrayDesktop.Run(() =>
        {
            Assert.AreEqual(twelfth, shown.Search.Text, "the pick completes the typed word, as a chip's click does (G4)");
            Assert.IsTrue(shown.ChipRow.Picker.Entries.Single(entry => entry.Tag == twelfth).Marked);
        });
    }

    [TestMethod]
    public async Task UnderAQueryItsListIsStillEveryTagTheLitOnesMarked()
    {
        using var shown = Open();
        var (_, tags) = await TwelveLong(shown);
        await shown.AddTo("reaction second.png");
        TrayDesktop.Run(() => shown.Search.Text = "reaction");
        await Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled && shown.Listing.Items.Count == 2 && shown.ShownChips().Contains(EveryTag)));

        TrayDesktop.Run(() => RaiseClick(shown.Chip(EveryTag)));

        TrayDesktop.Run(() =>
        {
            var picker = shown.ChipRow.Picker;
            Assert.HasCount(tags.Count, picker.Entries, "every existing tag, under any query (G4)");
            Assert.IsFalse(picker.Entries.Any(entry => entry.Marked), "no word of the query is a tag");
            shown.Search.Text = "reaction " + tags[0];
        });
        await Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled && shown.ChipRow.Picker.Entries.Any(entry => entry.Marked)));

        TrayDesktop.Run(() => CollectionAssert.AreEqual(new[] { tags[0] }, shown.ChipRow.Picker.Entries.Where(entry => entry.Marked).Select(entry => entry.Tag).ToList()));
    }

    [TestMethod]
    public async Task EscClosesTheListAndNothingElseTheSearchKeptAndTheFlyoutUp()
    {
        using var shown = Open();
        await TwelveLong(shown);
        TrayDesktop.Run(() => shown.Search.Text = "reaction");
        await Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled && shown.Listing.Query == "reaction"));
        TrayDesktop.Run(() =>
        {
            shown.Search.Focus();
            shown.ChipRow.Picker.Open(shown.Chip(EveryTag), [new TagPickerEntry("kept", false)], _ => { });
            Assert.IsTrue(shown.ChipRow.Picker.IsOpen);
        });

        TrayDesktop.Run(() => Press(shown.Search, Key.Escape));

        TrayDesktop.Run(() =>
        {
            Assert.IsFalse(shown.ChipRow.Picker.IsOpen, "the list closes (G2)");
            Assert.AreEqual("reaction", shown.Search.Text, "the search not cleared");
            Assert.IsTrue(shown.Window.IsVisible, "the flyout not hidden");
        });
        TrayDesktop.Run(() => Press(shown.Search, Key.Escape));
        TrayDesktop.Run(() => Assert.AreEqual("", shown.Search.Text ?? "", "Esc again goes on as G2 says"));
    }

    [TestMethod]
    public async Task HidingTheWindowClosesTheListThoughTheWindowWasNotActive()
    {
        using var shown = Open();
        await TwelveLong(shown);
        TrayDesktop.Run(() => RaiseClick(shown.Chip(EveryTag)));

        TrayDesktop.Run(() => shown.Window.SummonFromShortcut(new ForegroundReading(true, null), new SummonAnchor(null)));

        TrayDesktop.Run(() =>
        {
            Assert.IsFalse(shown.Window.IsVisible);
            Assert.IsFalse(shown.ChipRow.Picker.IsOpen, "no list left behind (F4)");
        });
    }

    [TestMethod]
    public async Task SixtyTagsScrollInsideTheDisplayAndEscStillClosesTheListAlone()
    {
        using var shown = Open();
        var item = (await shown.OpenStash(shown.Picture("sixty.png"))).Single();
        var tags = Enumerable.Range(1, 60).Select(i => $"tag-{i:00}").ToList();
        await shown.Tag([item], string.Join(" ", tags));

        TrayDesktop.Run(() => RaiseClick(shown.Chip(EveryTag)));
        await Eventually(() => TrayDesktop.Get(() => List(shown).Viewport.Height > 0));

        TrayDesktop.Run(() =>
        {
            var list = List(shown);
            var scaling = shown.Window.RenderScaling;
            var area = shown.Window.Screens.ScreenFromVisual(shown.Window)!.WorkingArea;
            Assert.IsGreaterThan(list.Viewport.Height, list.Extent.Height, "it scrolls (G4)");
            Assert.IsLessThan(area.Height / scaling, list.Bounds.Height, "inside the display");
            Assert.HasCount(60, shown.ChipRow.Picker.Entries);
            list.ScrollToEnd();
        });
        await Eventually(() => TrayDesktop.Get(() => ((Control)shown.ChipRow.Picker.Child!).GetVisualDescendants().OfType<Button>().Any(button => AutomationProperties.GetName(button) == tags[^1])));
        TrayDesktop.Run(() => Press(shown.Search, Key.Escape));
        TrayDesktop.Run(() => Assert.IsFalse(shown.ChipRow.Picker.IsOpen));
    }

    [TestMethod]
    public async Task ThousandsOfTagsMakeOnlyTheEntriesNearTheViewSoTheListOpensAtOnce()
    {
        using var shown = Open();
        var item = (await shown.OpenStash(shown.Picture("thousands.png"))).Single();
        await shown.Tag([item], string.Join(" ", Enumerable.Range(1, 3000).Select(i => $"t{i:0000}")) + " zz-the-widest-of-the-thousands");

        var clock = System.Diagnostics.Stopwatch.StartNew();
        TrayDesktop.Run(() => RaiseClick(shown.Chip(EveryTag)));
        await Eventually(() => TrayDesktop.Get(() => List(shown).Viewport.Height > 0));
        var opened = clock.ElapsedMilliseconds;

        TrayDesktop.Run(() =>
        {
            Assert.HasCount(3001, shown.ChipRow.Picker.Entries);
            var card = (Control)shown.ChipRow.Picker.Child!;
            Assert.IsGreaterThanOrEqualTo(1, card.GetVisualDescendants().OfType<VirtualizingStackPanel>().Count(), "a panel that makes its entries as they show");
            Assert.IsLessThan(300, card.GetVisualDescendants().OfType<Button>().Count(), "only those near the view are made (E4)");
        });
        Assert.IsLessThan(1000, opened, $"opened in {opened} ms");
        await AsWideAtOpenAsAtTheEnd(shown, "zz-the-widest-of-the-thousands");
    }

    [TestMethod]
    public async Task TheListIsAsWideAtOpenAsItsWidestNameListedLastNeedsAndShowsItWhole()
    {
        using var shown = Open();
        var (item, tags) = await TwelveLong(shown);
        const string Widest = "the-widest-reaction-of-them-all";
        var other = (await shown.AddTo("widest carrier.png")).Single();
        await shown.Tag([other], string.Join(" ", tags) + " " + Widest);

        TrayDesktop.Run(() =>
        {
            shown.Search.Focus();
            RaiseClick(shown.Chip(EveryTag));
        });

        TrayDesktop.Run(() => Assert.AreEqual(Widest, shown.ChipRow.Picker.Entries[^1].Tag, "carried by one item, it lists last (G4)"));
        await AsWideAtOpenAsAtTheEnd(shown, Widest);
        TrayDesktop.Run(() =>
        {
            Assert.IsGreaterThan(WidthOf(Words(Card(shown), tags[^1])) + 60, WidthOf(Words(Card(shown), Widest)), "wider than every other name by sixty pixels");
            Assert.IsInstanceOfType<PopupRoot>(TopLevel.GetTopLevel(Card(shown)), "still a popup of the window (F4)");
            RaiseClick(Entry(shown.ChipRow.Picker, Widest));
        });
        await Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled && shown.Listing.Query == Widest));
        TrayDesktop.Run(() => Assert.IsTrue(shown.Search.IsFocused, "a pick leaves focus in the box (G7)"));
    }

    [TestMethod]
    public async Task ANameOfWideLettersSetsTheWidthOverALongerNameOfNarrowLetters()
    {
        using var shown = Open();
        var (item, tags) = await TwelveLong(shown);
        var narrow = new string('i', 40);
        var wide = new string('W', 12);
        var other = (await shown.AddTo("letters carrier.png")).Single();
        await shown.Tag([other], string.Join(" ", tags));
        await shown.Tag([item], narrow + " " + wide);

        TrayDesktop.Run(() => RaiseClick(shown.Chip(EveryTag)));

        TrayDesktop.Run(() => CollectionAssert.AreEquivalent(new[] { narrow, wide }, shown.ChipRow.Picker.Entries.Skip(tags.Count).Select(entry => entry.Tag).ToList(), "both carried once, so after the twelve"));
        await AsWideAtOpenAsAtTheEnd(shown, wide);
        TrayDesktop.Run(() =>
        {
            Card(shown).GetVisualDescendants().OfType<Button>().Single(button => AutomationProperties.GetName(button) == narrow).BringIntoView();
            Card(shown).UpdateLayout();
            Assert.IsGreaterThan(WidthOf(Words(Card(shown), narrow)), WidthOf(Words(Card(shown), wide)), "the shorter name is the wider one");
        });
    }

    [TestMethod]
    public async Task ANameWiderThanTheListIsTrimmedAtTheWidestListWithItsTipAndEndsAsClearOfTheBar()
    {
        using var shown = Open();
        var (item, _) = await TwelveLong(shown);
        var longest = "zz-a-reaction-name-far-too-long-to-show-whole-in-any-list-" + new string('x', 20);
        await shown.Tag([item], longest);

        TrayDesktop.Run(() => RaiseClick(shown.Chip(EveryTag)));
        await AsWideAtOpenAsAtTheEnd(shown, longest);

        TrayDesktop.Run(() =>
        {
            var card = Card(shown);
            var words = Words(card, longest);
            Assert.AreEqual(320, card.Bounds.Width, 0.5, "the list at its widest");
            Assert.IsTrue(words.TextLayout.TextLines.Any(line => line.HasCollapsed), "the name trimmed with its ellipsis");
            Assert.AreEqual(longest, ToolTip.GetTip(words), "its tip names it whole");
            Assert.AreEqual(19, Measures(card, List(shown), words).WordsToBar, 1, "its words end as clear of the bar as an untrimmed name's");
        });
    }

    [TestMethod]
    public async Task TheListsBarAndWordsStandWhereTheItemMenusListsStand()
    {
        using var shown = Open();
        var (item, tags) = await TwelveLong(shown);
        const string Widest = "the-widest-reaction-of-them-all";
        var other = (await shown.AddTo("measures carrier.png")).Single();
        await shown.Tag([other], string.Join(" ", tags) + " " + Widest);
        TrayDesktop.Run(() =>
        {
            shown.Listing.Select([item.Id], item.Id);
            shown.RightClick(item.Id);
            shown.Submenu("Add tag");
        });
        var list = TrayDesktop.Get(() => shown.MenuEntry("Add tag"));
        await Eventually(() => TrayDesktop.Get(() => list.IsSubMenuOpen && Labels(list).Count == tags.Count + 2 && Scroller(list).Extent.Height > Scroller(list).Viewport.Height));
        var menu = TrayDesktop.Get(() =>
        {
            var scroller = Scroller(list);
            scroller.UpdateLayout();
            var words = list.Items.OfType<MenuItem>().SelectMany(entry => entry.GetVisualDescendants().OfType<TextBlock>().Where(text => text.Text == (string)entry.Header!)).MaxBy(WidthOf)!;
            Assert.AreEqual(Widest, words.Text, "the menu's widest entry");
            return Measures(TopLevel.GetTopLevel(scroller)!, scroller, words);
        });
        TrayDesktop.Run(shown.CloseMenu);

        TrayDesktop.Run(() => RaiseClick(shown.Chip(EveryTag)));
        await AsWideAtOpenAsAtTheEnd(shown, Widest);

        var picker = TrayDesktop.Get(() => Measures(Card(shown), List(shown), Words(Card(shown), Widest)));
        Assert.AreEqual(menu.BarToEdge, picker.BarToEdge, 1, $"the bar as near the list's edge as the menu's: {picker} against {menu}");
        Assert.AreEqual(menu.WordsToBar, picker.WordsToBar, 1, $"the widest words as clear of the bar as the menu's: {picker} against {menu}");
    }

    /// <summary>
    /// The open list's card is as wide after its first layout as once scrolled to its end with <paramref name="widest"/>
    /// made, which shows whole while the list's widest leaves it room.
    /// </summary>
    private static async Task AsWideAtOpenAsAtTheEnd(ShownWindow shown, string widest)
    {
        await Eventually(() => TrayDesktop.Get(() => List(shown).Viewport.Height > 0 && Card(shown).Bounds.Width > 0));
        var atOpen = TrayDesktop.Get(() => Card(shown).Bounds.Width);
        TrayDesktop.Run(() => List(shown).ScrollToEnd());
        await Eventually(() => TrayDesktop.Get(() => Card(shown).GetVisualDescendants().OfType<Button>().Any(button => AutomationProperties.GetName(button) == widest)));
        TrayDesktop.Run(() =>
        {
            var card = Card(shown);
            card.UpdateLayout();
            Assert.AreEqual(atOpen, card.Bounds.Width, 0.5, "as wide at open as once its widest name has scrolled into view");
            var words = Words(card, widest);
            var needed = WidthOf(words);
            if (card.Bounds.Width < 320)
            {
                Assert.IsLessThanOrEqualTo(words.Bounds.Width + 0.5, needed, $"{widest} shows whole");
                Assert.IsFalse(words.TextLayout.TextLines.Any(line => line.HasCollapsed), $"{widest} is not trimmed");
                if (card.Bounds.Width > 180.5)
                {
                    Assert.IsLessThan(1, words.Bounds.Width - needed, $"the list no wider than {widest} needs: its words' room {words.Bounds.Width} for {needed}");
                }
            }
            else
            {
                Assert.IsGreaterThan(words.Bounds.Width, needed, $"the list at its widest only for a name wider than it holds, not {widest}");
            }
        });
    }

    /// <summary>
    /// In <paramref name="root"/>'s units: from the scroller's vertical bar's right edge to the root's right edge, and from
    /// the right edge of <paramref name="words"/> to the bar's left edge.
    /// </summary>
    private static (double BarToEdge, double WordsToBar) Measures(Visual root, ScrollViewer scroller, TextBlock words)
    {
        var bar = Bar(scroller);
        var barLeft = bar.TranslatePoint(default, root)!.Value.X;
        var wordsRight = words.TranslatePoint(new Point(words.Bounds.Width, 0), root)!.Value.X;
        return (root.Bounds.Width - (barLeft + bar.Bounds.Width), barLeft - wordsRight);
    }

    /// <summary>The words of the entry for <paramref name="tag"/>, once made.</summary>
    private static TextBlock Words(Control card, string tag) =>
        card.GetVisualDescendants().OfType<Button>().Single(button => AutomationProperties.GetName(button) == tag).GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text == tag);

    /// <summary>The width the words need to show whole, in their own font.</summary>
    private static double WidthOf(TextBlock words)
    {
        using var layout = new TextLayout(words.Text, new Typeface(words.FontFamily, words.FontStyle, words.FontWeight, words.FontStretch), words.FontSize, null);
        return layout.WidthIncludingTrailingWhitespace;
    }

    private static Control Card(ShownWindow shown) => (Control)shown.ChipRow.Picker.Child!;

    [TestMethod]
    public async Task ARealPressOnATileClosesTheListAndSelectsTheTileAsAPlainClickDoes()
    {
        using var pointer = TestMouse.PutBackAfterwards();
        using var shown = Open();
        var (item, _) = await TwelveLong(shown);
        var other = (await shown.AddTo("press target.png")).Single();
        TrayDesktop.Run(() => RaiseClick(shown.Chip(EveryTag)));
        var at = TrayDesktop.Get(() => shown.TileOnScreen(other.Id));
        TrayDesktop.Run(() => shown.Listing.Select([item.Id], item.Id));

        TestMouse.Click(at);
        await Eventually(() => TrayDesktop.Get(() => !shown.ChipRow.Picker.IsOpen));
        await Eventually(() => TrayDesktop.Get(() => shown.Listing.Selection.Contains(other.Id)));

        TrayDesktop.Run(() => Assert.IsTrue(shown.Grid.IsFocused, "the press acted as it would have (G7)"));
    }

    /// <summary>A stash whose item carries twelve long tags, or <paramref name="carrier"/> does: more than the flyout's row holds.</summary>
    private static async Task<(StashItem Item, List<string> Tags)> TwelveLong(ShownWindow shown, StashItem? carrier = null)
    {
        var item = carrier ?? (await shown.OpenStash(shown.Picture("many tags.png"))).Single();
        var tags = Enumerable.Range(1, 12).Select(i => $"reaction-tag-{i:00}").ToList();
        await shown.Tag([item], string.Join(" ", tags));
        await Eventually(() => TrayDesktop.Get(() => shown.ShownChips().Contains(EveryTag)));
        return (item, tags);
    }

    private static ScrollViewer List(ShownWindow shown) => ((Control)shown.ChipRow.Picker.Child!).GetVisualDescendants().OfType<ScrollViewer>().First();
}
