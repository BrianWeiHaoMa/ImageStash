using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ImageStash.Store;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// The chip row's faces (G4, G5, G7, F10): Recent, Favorites and All as compact glyph chips called by G4's names, the
/// chip that lists every tag as a tag glyph and a chevron, and tag chips whose padding is the one the row counts by. How
/// many chips show in a laid-out width only a shown window measures: the desktop's chip row checks.
/// </summary>
[TestClass]
public sealed class ChipRowTests
{
    private const string Icons = "Segoe Fluent Icons";

    [TestMethod]
    public void TheScopeChipsAreGlyphsCalledRecentFavoritesAndAllToAScreenReaderAndInTheirTooltips()
    {
        ControlThread.Run(() =>
        {
            var row = Row(new Listing(_ => { }));
            var expected = new[] { ("Recent", ""), ("Favorites", FavoriteStar.Outlined), ("All", "") };
            Assert.HasCount(4, row.Children.OfType<Button>(), "the three scopes and the chip listing every tag, with no tag yet");
            var scopes = row.Children.OfType<Button>().Take(3).ToList();
            foreach (var ((name, glyph), chip) in expected.Zip(scopes))
            {
                Assert.AreEqual(name, AutomationProperties.GetName(chip), "the name G4 gives it, which a screen reader reads");
                Assert.AreEqual(name, ToolTip.GetTip(chip), "the name in its tooltip: its face shows no words");
                var face = chip.Content as TextBlock;
                Assert.IsNotNull(face, $"{name}: a glyph, not words");
                Assert.AreEqual(glyph, face.Text, $"{name}: its glyph (a clock, the favorite star, a grid)");
                Assert.AreEqual(Icons, face.FontFamily.Name, $"{name}: in the look's icon font (F10)");
            }
        });
    }

    [TestMethod]
    public void EveryChipIs32TallAndTakesNoFocusAndTheScopeChipsAre36Wide()
    {
        ControlThread.Run(() =>
        {
            var row = Row(new Listing(_ => { }));
            foreach (var chip in row.Children.OfType<Button>())
            {
                Assert.AreEqual(32, chip.Height, "a comfortable target (G7)");
                Assert.IsFalse(chip.Focusable, "Tab goes from the box to the grid (G7)");
            }

            CollectionAssert.AreEqual(new[] { 36.0, 36.0, 36.0 }, row.Children.OfType<Button>().Take(3).Select(chip => chip.Width).ToList(), "compact, so the row leaves room for tags");
        });
    }

    [TestMethod]
    public void AScopeChipsClickClearsTheSearchAndShowsItsScopeWhichIsThenTheLitChip()
    {
        ControlThread.Run(() =>
        {
            var listing = new Listing(_ => { });
            var row = Row(listing);
            var chips = row.Children.OfType<Button>().Take(3).ToList();
            foreach (var (name, scope) in new[] { ("Favorites", Scope.Favorites), ("All", Scope.All), ("Recent", Scope.Recent) })
            {
                _ = listing.SetQuery("cat food");
                CollectionAssert.AreEqual(new[] { false, false, true }, Lit(chips), "while a query is typed, the All chip is lit (G5)");

                chips.Single(chip => AutomationProperties.GetName(chip) == name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                Assert.AreEqual(("", scope), (listing.Query, listing.Scope), $"{name}: the search cleared and that scope shown (G4)");
                CollectionAssert.AreEqual(chips.Select(chip => AutomationProperties.GetName(chip) == name).ToList(), Lit(chips), $"{name} alone is lit");
            }
        });
    }

    [TestMethod]
    public void ALitScopeChipsGlyphTakesTheChipsWordsColourSoItTurnsWithTheLitGround()
    {
        ControlThread.Run(() =>
        {
            var row = Row(new Listing(_ => { }));
            foreach (var chip in row.Children.OfType<Button>().Take(3).Append(row.Children.OfType<Button>().Last()))
            {
                foreach (var glyph in Glyphs(chip))
                {
                    // A colour of its own would stay put while the look dresses the lit chip's words (F10, light and dark).
                    Assert.IsFalse(glyph.IsSet(TextBlock.ForegroundProperty), $"{AutomationProperties.GetName(chip)}: no colour of its own");
                }
            }
        });
    }

    [TestMethod]
    public void TheChipListingEveryTagShowsATagGlyphAndAChevronAndIsCalledEveryTag()
    {
        ControlThread.Run(() =>
        {
            var everyTag = Row(new Listing(_ => { })).Children.OfType<Button>().Last();
            Assert.AreEqual("Every tag", AutomationProperties.GetName(everyTag));
            Assert.AreEqual("Every tag", ToolTip.GetTip(everyTag));
            Assert.IsNotInstanceOfType<string>(everyTag.Content, "no longer the words …");
            var glyphs = Glyphs(everyTag);
            CollectionAssert.AreEqual(new[] { "", "" }, glyphs.Select(glyph => glyph.Text).ToList(), "a tag, then a chevron: it opens the tags");
            Assert.IsTrue(glyphs.All(glyph => glyph.FontFamily.Name == Icons), "in the look's icon font (F10)");
        });
    }

    [TestMethod]
    public async Task ATagChipsPaddingIsTheOneTheRowCountsItsNarrowestChipBy()
    {
        using var listed = await ListedStash.Open();
        var item = await listed.Stash.Import("cat picture");
        listed.Stash.Tag(item.Id, "cat");
        await listed.Listing.ShowScope(Scope.All);
        Assert.IsNotEmpty(listed.Listing.Chips, "the listing holds the tag");

        ControlThread.Run(() =>
        {
            var row = Row(listed.Listing, listed.Organising);
            var tagChip = row.Children.OfType<Button>().Single(chip => chip.Content as string == "#cat");
            Assert.AreEqual(10, tagChip.Padding.Left, "tighter than the 14 it had, so the row holds more tags");
            Assert.AreEqual(tagChip.Padding.Left, tagChip.Padding.Right);
            Assert.AreEqual(ChipRow.NarrowestChip, tagChip.Padding.Left + tagChip.Padding.Right,
                "the row measures a tag chip and counts how many to make by the padding the chip is built with");
        });
    }

    [TestMethod]
    public async Task TheFirstOverflowDoesNotChangeWhichTagPlacesFit()
    {
        using var listed = await ListedStash.Open();
        var item = await listed.Stash.Import("a picture with three tags");
        await listed.Organising.AddTag([item], "one two");
        await listed.Listing.ShowScope(Scope.All);
        var before = Places(listed.Listing, overflow: false);
        // Use a separate view of the same store to measure the new layout on the control thread.
        var after = new Listing(listed.Failures.Add);
        await after.StashOpened(listed.Stash.Store!);
        await after.ShowScope(Scope.All);
        await listed.Stash.Store!.AddTag([item], "z");
        await after.Refresh();
        CollectionAssert.AreEqual(before, Places(after, overflow: true));

        static Rect[] Places(Listing listing, bool overflow)
        {
            Rect[] places = [];
            ControlThread.Run(() =>
            {
                var row = Row(listing);
                var size = new Size(294, 40);
                // Supply measured chip widths because the portable test thread has no theme or font manager.
                row.Measure(size);
                Dispatcher.UIThread.RunJobs();
                var tags = row.Children.OfType<Button>().Where(chip => chip.Content is string).ToArray();
                foreach (var chip in tags)
                {
                    chip.Width = (string)chip.Content! == "#z" ? 36 : 50;
                }

                var picker = row.Children.OfType<Button>().Last();
                picker.Width = 40;
                row.Measure(size);
                row.Arrange(new Rect(size));
                Assert.AreEqual(overflow, picker.IsHitTestVisible);
                CollectionAssert.AreEqual(new[] { "#one", "#two" }, tags.Where(chip => chip.IsHitTestVisible).Select(chip => (string)chip.Content!).ToArray());
                places = tags.Take(2).Select(chip => chip.Bounds).ToArray();
            });
            return places;
        }
    }

    [TestMethod]
    public async Task ADropOnAChipWhoseLastCarrierIsGoneCannotCreateTheTag()
    {
        using var listed = await ListedStash.Open();
        var store = listed.Stash.Store!;
        var carrier = await listed.Stash.Import("only carrier");
        var dropped = await listed.Stash.Import("dropped");
        Assert.IsTrue((await store.AddTag([carrier], "reaction")).Single().Succeeded);
        await listed.Listing.ShowScope(Scope.All);
        // Keep the control's read at the drop boundary while Organising refreshes its listing on completion.
        var shown = new Listing(listed.Failures.Add);
        await shown.StashOpened(store);
        await shown.ShowScope(Scope.All);
        // The other tagging action has committed, with its listing refresh still to come.
        Assert.IsTrue((await store.RemoveTag([carrier], "reaction")).Single().Succeeded);
        var drag = new OwnDrag([dropped]);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnChanged()
        {
            if (!listed.Organising.Changing)
            {
                finished.TrySetResult();
            }
        }

        listed.Organising.Changed += OnChanged;
        try
        {
            ControlThread.Run(() =>
            {
                var row = new ChipRow(shown, listed.Organising, () => drag, _ => { });
                var chip = row.Children.OfType<Button>().Single(button => button.Content as string == "#reaction");
                using var data = new DataTransfer();
                chip.RaiseEvent(new DragEventArgs(DragDrop.DropEvent, data, chip, new Point(1, 1), KeyModifiers.None)
                    { DragEffects = DragDropEffects.Copy });
            });
            await finished.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            listed.Organising.Changed -= OnChanged;
        }

        Assert.IsTrue(drag.TakenByChip, "the internal drop is consumed and records no outside use");
        Assert.IsEmpty((await store.TagCounts()).Value!, "the stale chip cannot recreate the tag");
        Assert.AreEqual("Add a tag failed: dropped, no item carries #reaction any more", listed.Notices.Shown.Single().Text);
        Assert.IsEmpty(listed.Listing.LitTags);
        Assert.AreEqual(new TagCount("reaction", 0), listed.Listing.Chips.Single(), "its place remains inactive");
        Assert.IsTrue((await listed.Organising.AddTag([dropped], "reaction")).Succeeded);
        Assert.AreEqual("reaction", (await store.Item(dropped.Id)).Value!.Tags.Single(), "deliberately entering the tag can create it");
        ControlThread.Run(() =>
        {
            var row = Row(listed.Listing, listed.Organising);
            var chip = row.Children.OfType<Button>().Single(button => button.Content as string == "#reaction");
            Assert.IsTrue(chip.IsEnabled, "the revived tag can be used again");
        });
    }

    [TestMethod]
    public async Task ALiveChipCanStartAWholeStashSearchFromAnEmptyScope()
    {
        using var listed = await ListedStash.Open();
        var tagged = await listed.Stash.Import("a picture");
        await listed.Organising.AddTag([tagged], "reaction");
        await listed.Listing.ShowScope(Scope.Favorites);
        Assert.IsEmpty(listed.Listing.Items);
        var query = "";
        ControlThread.Run(() =>
        {
            var row = new ChipRow(listed.Listing, listed.Organising, () => null, text => query = text);
            var chip = row.Children.OfType<Button>().Single(button => button.Content as string == "#reaction");
            Assert.IsTrue(chip.IsEnabled);
            chip.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        });
        Assert.AreEqual("reaction", query);
        Assert.AreEqual(tagged.Id, (await listed.Stash.Store!.Search(query)).Value!.Single().Id);
    }

    private static ChipRow Row(Listing listing, Organising? organising = null) =>
        new(listing, organising ?? new Organising(listing, new Notices(TimeProvider.System)), () => null, _ => { });

    private static List<bool> Lit(IEnumerable<Button> chips) => chips.Select(chip => chip.Classes.Contains("accent")).ToList();

    private static List<TextBlock> Glyphs(Button chip) => chip.Content switch
    {
        TextBlock glyph => [glyph],
        Panel panel => panel.Children.OfType<TextBlock>().ToList(),
        _ => [],
    };
}
