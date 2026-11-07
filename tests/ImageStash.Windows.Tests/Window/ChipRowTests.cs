using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ImageStash.Seams;
using ImageStash.Store;
using ImageStash.Window;
using ImageStash.Windows.Tests.Support;

namespace ImageStash.Windows.Tests.Window;

/// <summary>
/// The chip row of the stash window laid out by the real desktop platform at the flyout's size, off every display and
/// never activated: Recent · Favorites · All and the tags carried by the most items on one line of whole chips, the last
/// chip for every tag ending a row that cannot hold them (G4); a chip's click adding its tag to the box's words, the lit
/// chip's taking it out, the row following with the lit chips and the tags that narrow (G4, G5); tiles of the window's own drag dropped on a tag chip or Favorites applying it with no use and nothing imported, the
/// flyout staying (G17, G10, G11, G2); another program's drag over a chip being the window's drop, tagged by the lit
/// chip (G11).
/// </summary>
[TestClass]
public sealed class ChipRowTests
{
    [TestMethod]
    public async Task TheRowReadsTheScopesThenTheTagsAndAChipsClickSearchesItsTagWhileTheLitChipsClickReturnsToTheScope()
    {
        using var shown = ShownWindow.Open();
        var items = await shown.OpenStash(shown.Picture("dog.png"), shown.Picture("tagged one.png"), shown.Picture("tagged pair.png"), shown.Picture("plain cat.png"));
        await shown.Tag([items[1], items[2]], "Dog");
        await shown.Tag([items[3]], "cat");

        TrayDesktop.Run(() =>
        {
            CollectionAssert.AreEqual(new[] { "Recent", "Favorites", "All", "#Dog", "#cat" }, shown.ShownChips(), "the scopes, then the tags by how many items carry them (G4)");
            Assert.IsTrue(shown.ChipRow.Children.OfType<Button>().All(chip => !chip.Focusable), "Tab goes from the box to the grid (G7)");
            ShownWindow.RaiseClick(shown.Chip("Favorites"));
        });
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled && shown.Listing.Scope == Scope.Favorites));
        TrayDesktop.Run(() =>
        {
            Assert.IsTrue(shown.Lit("Favorites"));
            ShownWindow.RaiseClick(shown.Chip("#Dog"));
        });
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled && shown.Listing.Query == "Dog"));

        TrayDesktop.Run(() =>
        {
            Assert.AreEqual("Dog", shown.Search.Text, "the tag as stored, in the box (G4)");
            CollectionAssert.AreEquivalent(items.Take(3).Select(item => item.Id).ToList(), shown.Listing.Items.Select(item => item.Id).ToList(), "the query's matches, the item named dog among them (G5)");
            Assert.IsTrue(shown.Lit("All"), "All lit while a query is typed (G5)");
            Assert.IsTrue(shown.Lit("#Dog"), "and the chip whose tag is a word of the query (G5)");
            Assert.IsFalse(shown.Lit("Favorites"));
            CollectionAssert.AreEqual(new[] { "Recent", "Favorites", "All", "#Dog" }, shown.ShownChips(), "the lit chip; cat is on no match (G5)");
            ShownWindow.RaiseClick(shown.Chip("#Dog"));
        });
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled && shown.Listing.Query == ""));

        TrayDesktop.Run(() =>
        {
            Assert.AreEqual("", shown.Search.Text ?? "", "the lit chip's click clears the search (G4)");
            Assert.AreEqual(Scope.Favorites, shown.Listing.LitChip, "back to the scope the user was in (G5)");
            Assert.IsTrue(shown.Lit("Favorites"));
            Assert.IsFalse(shown.Lit("#Dog"));
            Assert.IsTrue(shown.Window.GetVisualDescendants().OfType<TextBlock>().Any(text => text.IsEffectivelyVisible && text.Text == "No favorites yet"), "an empty Favorites says so, in words of its own (G2)");
        });
    }

    [TestMethod]
    public async Task ChipsAddWordsThatNarrowTogetherAndALitChipTakesItsWordOutTheRowFollowing()
    {
        using var shown = ShownWindow.Open();
        var items = await shown.OpenStash(shown.Picture("a.png"), shown.Picture("b.png"), shown.Picture("c.png"), shown.Picture("d.png"));
        await shown.Tag([items.Single(i => i.Name == "a")], "dog cat");
        await shown.Tag([items.Single(i => i.Name == "b")], "dog fun");
        await shown.Tag([items.Single(i => i.Name == "c")], "dog");
        await shown.Tag([items.Single(i => i.Name == "d")], "cat fun");
        await TrayDesktop.Get(() => shown.Listing.ShowScope(Scope.All));
        string[] scopes = ["Recent", "Favorites", "All"];
        TrayDesktop.Run(() => CollectionAssert.AreEqual(scopes.Concat(["#dog", "#cat", "#fun"]).ToList(), shown.ShownChips()));

        TrayDesktop.Run(() => ShownWindow.RaiseClick(shown.Chip("#cat")));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled && shown.Listing.Query == "cat"));
        TrayDesktop.Run(() =>
        {
            Assert.AreEqual("cat", shown.Search.Text);
            CollectionAssert.AreEqual(scopes.Concat(["#cat", "#dog", "#fun"]).ToList(), shown.ShownChips(), "the lit one, then those that narrow (G5)");
            Assert.IsTrue(shown.Lit("#cat") && !shown.Lit("#dog"));
            ShownWindow.RaiseClick(shown.Chip("#dog"));
        });
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled && shown.Listing.Query == "cat dog"));
        TrayDesktop.Run(() =>
        {
            Assert.AreEqual("cat dog", shown.Search.Text, "a second chip adds a word, never replaces (G4)");
            CollectionAssert.AreEqual(scopes.Concat(["#cat", "#dog"]).ToList(), shown.ShownChips(), "both lit, nothing narrows further");
            Assert.IsTrue(shown.Lit("#cat") && shown.Lit("#dog"));
            Assert.HasCount(1, shown.Listing.Items);
            ShownWindow.RaiseClick(shown.Chip("#cat"));
        });
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled && shown.Listing.Query == "dog"));
        TrayDesktop.Run(() =>
        {
            Assert.AreEqual("dog", shown.Search.Text, "a lit chip's click takes its word out (G4)");
            CollectionAssert.AreEqual(scopes.Concat(["#dog", "#cat", "#fun"]).ToList(), shown.ShownChips(), "the tie by the stash's count, then spelling");
            ShownWindow.RaiseClick(shown.Chip("All"));
        });
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled && shown.Listing.Query == ""));
        TrayDesktop.Run(() => Assert.AreEqual("", shown.Search.Text ?? "", "a scope clears the search (G4)"));
    }

    [TestMethod]
    public async Task AChipClickedAfterASummonSelectedTheBoxsTextAddsToItAndLeavesNothingSelected()
    {
        using var shown = ShownWindow.Open();
        var item = (await shown.OpenStash(shown.Picture("chip after summon.png"))).Single();
        await shown.Tag([item], "dog cat");
        TrayDesktop.Run(() => shown.Search.Text = "dog");
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled && shown.Listing.Query == "dog"));
        await shown.Tag([(await shown.AddTo("chip other.png")).Single()], "dog");
        TrayDesktop.Run(() => shown.Search.Text = "dog");
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled && shown.ShownChips().Contains("#cat")));
        TrayDesktop.Run(() =>
        {
            shown.Search.Focus();
            shown.Search.SelectAll();
            ShownWindow.RaiseClick(shown.Chip("#cat"));
        });
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled && shown.Listing.Query == "dog cat"));

        TrayDesktop.Run(() =>
        {
            Assert.AreEqual("", shown.Search.SelectedText, "nothing selected");
            Assert.AreEqual(7, shown.Search.CaretIndex, "the caret at the end (G4)");
            ShownWindow.Type(shown.Search, "x");
            Assert.AreEqual("dog catx", shown.Search.Text, "the next letter goes on the end");
        });
    }

    [TestMethod]
    public async Task ChipsMadeForAWiderRowKeepTheLitOnesLit()
    {
        using var shown = ShownWindow.Open();
        var items = await shown.OpenStash(shown.Picture("wide one.png"), shown.Picture("wide two.png"));
        await shown.Tag(items, "dog");
        await shown.Tag([items[0]], string.Join(" ", Enumerable.Range(1, 90).Select(i => $"t{i:00}")));
        TrayDesktop.Run(() => shown.Search.Text = "dog");
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled && shown.Listing.LitTags.Contains("dog")));
        var made = TrayDesktop.Get(() => shown.ChipRow.Children.OfType<Button>().Count());

        // The width a wider window offers the row, which makes more chips once this pass has ended.
        TrayDesktop.Run(() => shown.ChipRow.Measure(new Size(3000, 40)));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.ChipRow.Children.OfType<Button>().Count() > made));

        TrayDesktop.Run(() => Assert.IsTrue(shown.Lit("#dog"), "G5: lit while its tag is a word of the query"));
    }

    [TestMethod]
    public async Task TwelveLongTagsLeaveOneLineOfWholeChipsAndNothingAfterTheFirstThatDoesNotFit()
    {
        using var shown = ShownWindow.Open();
        var item = (await shown.OpenStash(shown.Picture("many tags.png"))).Single();
        var tags = Enumerable.Range(1, 12).Select(i => $"reaction-tag-{i:00}").ToList();
        await shown.Tag([item], string.Join(" ", tags));

        TrayDesktop.Run(() =>
        {
            var shownChips = shown.ShownChips();
            var row = shown.ChipRow;
            var named = shownChips.Where(chip => chip != "…").ToList();
            Assert.IsGreaterThan(3, named.Count, "some tag chips fit");
            Assert.IsLessThan(15, named.Count, "not all twelve fit at the flyout's width");
            Assert.AreEqual("…", shownChips[^1], "the chip that lists every tag ends the row (G4)");
            CollectionAssert.AreEqual(new[] { "Recent", "Favorites", "All" }.Concat(tags.Select(tag => "#" + tag)).Take(named.Count).ToList(), named, "in order, from the first");
            Assert.IsLessThanOrEqualTo(40, row.Bounds.Height, "one line");
            foreach (var chip in row.Children.OfType<Button>().Where(chip => !shownChips.Contains((string)chip.Content!)))
            {
                Assert.IsFalse(chip.IsHitTestVisible, $"{chip.Content} cannot be reached");
                Assert.IsGreaterThanOrEqualTo(row.Bounds.Width, chip.Bounds.X, $"{chip.Content} lies beyond the row's clipped edge");
            }
        });
    }

    [TestMethod]
    public async Task ATagAfterOneThatDoesNotFitIsAbsentThoughItWouldFit()
    {
        using var shown = ShownWindow.Open();
        var items = await shown.OpenStash(shown.Picture("fit one.png"), shown.Picture("fit pair.png"), shown.Picture("fit three!.png"));
        var wide = "an-elephant-sized-tag-name-much-too-wide-for-what-is-left-of-the-row";
        await shown.Tag(items, "cat");
        await shown.Tag(items.Take(2).ToList(), wide);
        await shown.Tag([items[0]], "z");

        TrayDesktop.Run(() =>
        {
            var row = shown.ChipRow;
            CollectionAssert.AreEqual(new[] { "Recent", "Favorites", "All", "#cat", "…" }, shown.ShownChips());
            var room = row.Bounds.Width - shown.Chip("#cat").Bounds.Right - 6 - shown.Chip("…").DesiredSize.Width - 6;
            Assert.IsLessThanOrEqualTo(room, shown.Chip("#z").DesiredSize.Width, "#z alone would fit after #cat");
            Assert.IsGreaterThan(room, shown.Chip("#" + wide).DesiredSize.Width, "the wide tag does not");
        });
    }

    [TestMethod]
    public async Task FiveThousandItemsEachWithATagOfItsOwnAreSummonedFromATypedQueryWellWithinTheSecondWithOnlyTheChipsThatCanFit()
    {
        using var shown = ShownWindow.Open();
        var own = (await shown.OpenStash(shown.Picture("own tag.png"))).Single();
        // E4 admits 5,000 items and bounds no item's tags: here 5,000 tags, each carried by one item.
        shown.AddPictures(5000, i => $"w{i:0000}");
        await Dispatcher.UIThread.InvokeAsync(() => shown.Listing.Refresh());
        TrayDesktop.Run(() => shown.Search.Text = "w0001");
        await TrayDesktop.Get(() => shown.Listing.Settle());
        TrayDesktop.Run(() => CollectionAssert.AreEqual(new[] { "Recent", "Favorites", "All", "#w0001", "…" }, shown.ShownChips(), "the query's one tag (G5), and the chip listing every tag, which the row cannot hold (G4)"));

        // The paste moment leaves the query typed; the next summon keeps it and its one chip (G1).
        var clock = System.Diagnostics.Stopwatch.StartNew();
        TrayDesktop.Run(() =>
        {
            shown.Window.Hide();
            shown.Window.SummonFromShortcut(new ForegroundReading(false, PresetTarget.App("Relay")), new SummonAnchor(null));
        });
        await TrayDesktop.Get(() => shown.Listing.Settle());
        var (keptChips, keptQuery, keptText) = TrayDesktop.Get(() => (shown.ShownChips(), shown.Search.Text, shown.Search.SelectedText));
        var summoned = clock.Elapsed;

        // A scope's click then shows every tag: the row's cost at its fullest.
        var scoped = System.Diagnostics.Stopwatch.StartNew();
        TrayDesktop.Run(() => ShownWindow.RaiseClick(shown.Chip("All")));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled && shown.Listing.Query == ""));
        var (shownChips, made, selected) = TrayDesktop.Get(() => (shown.ShownChips(), shown.ChipRow.Children.OfType<Button>().Count(), shown.Listing.Selection.ToList()));
        var full = scoped.Elapsed;
        TrayDesktop.Run(() => shown.Window.Hide());

        CollectionAssert.AreEqual(new[] { "Recent", "Favorites", "All", "#w0001", "…" }, keptChips, "the query's one tag still (G1, G5)");
        Assert.AreEqual("w0001", keptQuery);
        Assert.AreEqual("w0001", keptText, "all of it selected (G1)");
        Assert.IsLessThan(500, summoned.TotalMilliseconds, $"well within G1's second: {summoned.TotalMilliseconds:F0} ms");
        CollectionAssert.AreEqual(new[] { own.Id }, selected, "All, its first item selected (G4)");
        CollectionAssert.AreEqual(new[] { "Recent", "Favorites", "All", "#w0001" }, shownChips.Take(4).ToList(), "the tags in G4's order, as many as fit");
        Assert.AreEqual("…", shownChips[^1], "and the chip that lists every tag");
        Assert.IsLessThan(1000, full.TotalMilliseconds, $"every tag's row within a second: {full.TotalMilliseconds:F0} ms");
        Assert.IsLessThan(3 + 40, made, $"only the chips that could fit are made, not one per tag: {made}");
    }

    [TestMethod]
    public async Task UnderAQueryWhoseFewChipsFitTheChipListingEveryTagStillEndsTheRowThatCouldNotHoldThemAll()
    {
        using var shown = ShownWindow.Open();
        var items = await shown.OpenStash(shown.Picture("every one.png"), shown.Picture("every pair.png"));
        await shown.Tag([items[0]], "cat");
        await shown.Tag([items[1]], string.Join(' ', Enumerable.Range(0, 12).Select(i => $"a-tag-long-enough-to-fill-the-row-{i}")));

        TrayDesktop.Run(() => shown.Search.Text = "cat");
        await TrayDesktop.Get(() => shown.Listing.Settle());
        await Task.Delay(100);
        TrayDesktop.Run(() => CollectionAssert.AreEqual(new[] { "Recent", "Favorites", "All", "#cat", "…" }, shown.ShownChips(), "the row could not hold every tag (G4)"));

        TrayDesktop.Run(() => shown.Search.Text = "nothing-matches-this");
        await TrayDesktop.Get(() => shown.Listing.Settle());
        await Task.Delay(100);
        TrayDesktop.Run(() => CollectionAssert.AreEqual(new[] { "Recent", "Favorites", "All", "…" }, shown.ShownChips(), "no match shows no tag, and every tag is still one click away (G4)"));
    }

    [TestMethod]
    public async Task TheChipListingEveryTagSitsAtTheRowsRightEdgeAndStaysPutAsTheChipsBeforeItComeAndGo()
    {
        using var shown = ShownWindow.Open();
        var items = await shown.OpenStash(shown.Picture("edge one.png"), shown.Picture("edge pair.png"));
        await shown.Tag([items[0]], "cat");
        await shown.Tag([items[1]], string.Join(' ', Enumerable.Range(0, 12).Select(i => $"a-tag-long-enough-to-fill-the-row-{i}")));
        Rect Place() => TrayDesktop.Get(() =>
        {
            shown.Window.UpdateLayout();
            return shown.Chip("…").Bounds;
        });

        var first = Place();
        TrayDesktop.Run(() => Assert.AreEqual(shown.ChipRow.Bounds.Width, first.Right, 0.5, "at the row's right edge (G4)"));

        TrayDesktop.Run(() => ShownWindow.RaiseClick(shown.Chip("#cat")));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled && shown.Listing.Query == "cat"));
        TrayDesktop.Run(() => CollectionAssert.AreEqual(new[] { "Recent", "Favorites", "All", "#cat", "…" }, shown.ShownChips(), "a lit chip and nothing that narrows: the row has room, and the chip still ends it (G4)"));
        Assert.AreEqual(first, Place(), "a chip lit and the others gone leave it where it was (G4)");

        TrayDesktop.Run(() => ShownWindow.RaiseClick(shown.Chip("All")));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled && shown.Listing.Query == ""));
        await shown.Tag(items, "new");
        TrayDesktop.Run(() => Assert.AreEqual("#new", shown.ShownChips()[3], "a chip enters first, the commonest"));
        Assert.AreEqual(first, Place(), "a chip come leaves it where it was (G4)");

        Assert.IsTrue((await TrayDesktop.Get(() => shown.Listing.Store!.RemoveTag(items, "new"))).All(outcome => outcome.Succeeded));
        await TrayDesktop.Get(() => shown.Listing.Refresh());
        TrayDesktop.Run(() => CollectionAssert.DoesNotContain(shown.ShownChips(), "#new"));
        Assert.AreEqual(first, Place(), "and gone again (G4)");

        TrayDesktop.Run(() => shown.Window.Width += 200);
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.ChipRow.Bounds.Width) > first.Right + 150);
        var wider = Place();
        TrayDesktop.Run(() => Assert.AreEqual(shown.ChipRow.Bounds.Width, wider.Right, 0.5, "at the wider row's right edge"));
    }

    [TestMethod]
    public async Task UnderATypedWordTheTagsContainingItStayAndAChipOrAPickCompletesTheWord()
    {
        using var shown = ShownWindow.Open();
        var items = await shown.OpenStash(shown.Picture("p1.png"), shown.Picture("p2.png"), shown.Picture("p3.png"), shown.Picture("q1.png"));
        await shown.Tag([items[0]], "joy joyous alpha");
        await shown.Tag([items[1]], "joy alpha");
        await shown.Tag([items[2]], "joy work");
        await shown.Tag([items[3]], "alpha");
        string[] scopes = ["Recent", "Favorites", "All"];

        TrayDesktop.Run(() => shown.Search.Text = "jo");
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled && shown.Listing.Query == "jo"));
        TrayDesktop.Run(() =>
        {
            CollectionAssert.AreEqual(new[] { "joy", "joyous", "alpha", "work" }, shown.Listing.Chips.Select(chip => chip.Tag).ToList(), "the tags being typed, then those that narrow (G5)");
            CollectionAssert.AreEqual(scopes.Concat(["#joy", "#joyous"]).ToList(), shown.ShownChips().Take(5).ToList(), "first in the row");
            Assert.IsTrue(shown.Lit("All") && !shown.Lit("#joy") && !shown.Lit("#joyous"));
            ShownWindow.RaiseClick(shown.Chip("#joy"));
        });
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled && shown.Listing.Query == "joy"));
        TrayDesktop.Run(() =>
        {
            Assert.AreEqual("joy", shown.Search.Text, "the chip completes the typed word, not jo joy (G4)");
            Assert.AreEqual("", shown.Search.SelectedText ?? "");
            Assert.IsTrue(shown.Lit("#joy"));
            CollectionAssert.AreEqual(new[] { "joy", "joyous", "alpha", "work" }, shown.Listing.Chips.Select(chip => chip.Tag).ToList(), "joy lit, joyous still in reach (G5)");
            ShownWindow.RaiseClick(shown.Chip("#alpha"));
        });
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled && shown.Listing.Query == "joy alpha"));
        TrayDesktop.Run(() => Assert.AreEqual("joy alpha", shown.Search.Text, "a tag containing no word of the query is added (G4)"));

        TrayDesktop.Run(() => shown.Search.Text = "alpha jo");
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled && shown.Listing.Query == "alpha jo"));
        TrayDesktop.Run(() => ShownWindow.RaiseClick(shown.Chip("#joyous")));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled && shown.Listing.Query == "alpha joyous"));
        TrayDesktop.Run(() => Assert.AreEqual("alpha joyous", shown.Search.Text, "the last word it contains is the one it takes the place of (G4)"));
    }

    [TestMethod]
    public async Task AWiderWindowShowsTheFurtherTagChipsThatNowFit()
    {
        using var shown = ShownWindow.Open();
        var item = (await shown.OpenStash(shown.Picture("sixty tags.png"))).Single();
        var tags = Enumerable.Range(1, 60).Select(i => $"t{i:00}").ToList();
        await shown.Tag([item], string.Join(" ", tags));
        var madeNarrow = TrayDesktop.Get(() => shown.ChipRow.Children.OfType<Button>().Count());
        Assert.IsLessThan(3 + 60, madeNarrow, "not a chip for every tag at the flyout's width");

        TrayDesktop.Run(() => shown.Window.Width = 1600);
        await ShownWindow.Eventually(() => TrayDesktop.Get(shown.ShownChips).Count > madeNarrow);

        var wide = TrayDesktop.Get(shown.ShownChips).Where(chip => chip != "…").ToList();
        CollectionAssert.AreEqual(new[] { "Recent", "Favorites", "All" }.Concat(tags.Select(tag => "#" + tag)).Take(wide.Count).ToList(), wide, "still in order, from the first (G4)");
        Assert.IsLessThan(3 + 60, wide.Count, "and still up to the first that does not fit");
    }

    [TestMethod]
    public async Task TilesDraggedOntoATagChipOrFavoritesGetItWithNoUseNothingImportedAndTheFlyoutStaying()
    {
        var drags = new PresetDrags();
        using var shown = ShownWindow.Open(drags);
        var items = await shown.OpenStash(shown.Picture("drag a.png"), shown.Picture("drag b.png"), shown.Picture("drag tagged.png"));
        var (a, b, tagged) = (items[0], items[1], items[2]);
        await shown.Tag([tagged], "reaction");
        var data = await shown.Files(a.FilePath, b.FilePath);
        var noticesBefore = TrayDesktop.Get(() => shown.Notices.Shown.Count);
        var (recent, all, reaction, favorites) = (await shown.DropTargetAt("Recent"), await shown.DropTargetAt("All"), await shown.DropTargetAt("#reaction"), await shown.DropTargetAt("Favorites"));
        var effects = new List<DragDropEffects>();
        drags.During = () =>
        {
            // An import ending meanwhile selects its items: the drop takes the items the drag carries all the same.
            shown.Listing.Select([tagged.Id], tagged.Id);
            effects.Add(ShownWindow.RaiseDrag(recent, DragDrop.DragEnterEvent, data));
            effects.Add(ShownWindow.RaiseDrag(all, DragDrop.DragEnterEvent, data));
            effects.Add(ShownWindow.RaiseDrag(reaction, DragDrop.DragEnterEvent, data));
            effects.Add(ShownWindow.RaiseDrag(reaction, DragDrop.DragOverEvent, data));
            effects.Add(ShownWindow.RaiseDrag(reaction, DragDrop.DropEvent, data));
        };
        // The seam reports the drop taken as a copy, as the shell does for any target that took it.
        drags.Ends = Outcome<bool>.Of(true);

        Drag(shown, a, b);
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled && Carrying(shown, "reaction").Count == 3));
        await Task.Delay(300);

        CollectionAssert.AreEqual(new[] { DragDropEffects.None, DragDropEffects.None, DragDropEffects.Copy, DragDropEffects.Copy, DragDropEffects.Copy }, effects,
            "Recent and All take nothing of the window's own drag; a tag chip takes it as a copy");
        CollectionAssert.AreEquivalent(new[] { a.FilePath, b.FilePath }, drags.Dragged.Single().ToList());
        TrayDesktop.Run(() =>
        {
            CollectionAssert.AreEquivalent(new[] { a.Id, b.Id, tagged.Id }, Carrying(shown, "reaction"), "the dragged items got the tag (G17)");
            Assert.HasCount(3, shown.Listing.Items, "nothing imported (G11)");
            Assert.HasCount(noticesBefore, shown.Notices.Shown, "nothing said");
            Assert.IsFalse(shown.Window.DragInProgress, "the drag ended");
            Assert.IsTrue(shown.Window.IsVisible, "the flyout stays (G2)");
        });
        Assert.AreEqual(0, shown.Uses(), "a tile dropped on the window's own chip is no use (G10)");

        await shown.Listing.Store!.SetFavorite([b], true);
        effects.Clear();
        drags.During = () =>
        {
            effects.Add(ShownWindow.RaiseDrag(favorites, DragDrop.DragEnterEvent, data));
            effects.Add(ShownWindow.RaiseDrag(favorites, DragDrop.DropEvent, data));
        };
        Drag(shown, a, b);
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled && shown.Listing.Items.Count(item => item.Favorite) == 2));

        CollectionAssert.AreEqual(new[] { DragDropEffects.Copy, DragDropEffects.Copy }, effects);
        TrayDesktop.Run(() => CollectionAssert.AreEquivalent(new[] { a.Id, b.Id }, shown.Listing.Items.Where(item => item.Favorite).Select(item => item.Id).ToList(),
            "both favorites, the one already a favorite staying one (G17)"));
        Drag(shown, a, b);
        await Task.Delay(500);
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled));
        TrayDesktop.Run(() => CollectionAssert.AreEquivalent(new[] { a.Id, b.Id }, shown.Listing.Items.Where(item => item.Favorite).Select(item => item.Id).ToList(),
            "dropped again, still both: a drop sets the flag, never toggles it (G17)"));
        Assert.AreEqual(0, shown.Uses());

        drags.During = null;
        Drag(shown, a);
        await ShownWindow.Eventually(() => shown.Uses() == 1);
        TrayDesktop.Run(() => _ = shown.Listing.ShowScope(Scope.Recent));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled && shown.Listing.Scope == Scope.Recent));
        TrayDesktop.Run(() => Assert.AreEqual(a.Id, shown.Listing.Items[0].Id, "the next drag landed elsewhere is a use: no chip's take outlives its drag (G10)"));
    }

    [TestMethod]
    public async Task AnotherProgramsDragOverALitTagChipIsTheWindowsDropAndItsItemsGetTheTag()
    {
        using var shown = ShownWindow.Open();
        var item = (await shown.OpenStash(shown.Picture("external tagged.png"))).Single();
        await shown.Tag([item], "reaction");
        TrayDesktop.Run(() => shown.Search.Text = "reaction");
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled && shown.Listing.LitTags.Contains("reaction")));
        var data = await shown.Files(shown.Picture("external newcomer.png"));
        var reaction = await shown.DropTargetAt("#reaction");

        var entered = TrayDesktop.Get(() => ShownWindow.RaiseDrag(reaction, DragDrop.DragEnterEvent, data));

        Assert.AreEqual(DragDropEffects.Copy, entered, "the window's drop takes it as a copy (G11)");
        TrayDesktop.Run(() => Assert.IsTrue(shown.Window.GetVisualDescendants().OfType<TextBlock>().Any(text => text.IsEffectivelyVisible && text.Text == "New items get the tag #reaction."),
            "the drop card says so (G11)"));
        TrayDesktop.Run(() => ShownWindow.RaiseDrag(reaction, DragDrop.DropEvent, data));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled && shown.Listing.Items.Count == 2));

        TrayDesktop.Run(() =>
        {
            var newcomer = shown.Listing.Items.Single(listed => listed.Id != item.Id);
            CollectionAssert.AreEqual(new[] { "reaction" }, newcomer.Tags.ToList(), "while a tag chip is lit, new items get that tag (G11)");
            Assert.AreEqual("", shown.Listing.Query, "and the search is cleared after the import (G12)");
        });
    }

    /// <summary>
    /// The window's own drag of <paramref name="dragged"/>, started from a press on the first of them in the window in
    /// front, as the user's press leaves it, so the flyout's hide rule judged at the drag's end finds focus still there.
    /// </summary>
    private static void Drag(ShownWindow shown, params StashItem[] dragged) => TrayDesktop.Run(() =>
    {
        shown.Activate();
        shown.Listing.Select(dragged.Select(item => item.Id), dragged[0].Id);
        shown.Press(dragged[0].Id);
        shown.MovePointer(dragged[0].Id, 20, 0);
    });

    private static List<long> Carrying(ShownWindow shown, string tag) =>
        shown.Listing.Items.Where(item => item.Tags.Contains(tag)).Select(item => item.Id).ToList();
}
