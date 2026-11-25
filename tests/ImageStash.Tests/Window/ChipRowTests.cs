using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ImageStash.Store;
using ImageStash.Tests.Support;
using ImageStash.Window;
using static ImageStash.Tests.Support.ControlThread;

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
            var expected = new[] { ("Recent", "", "Ctrl+1"), ("Favorites", FavoriteStar.Outlined, "Ctrl+2"), ("All", "", "Ctrl+3") };
            Assert.HasCount(4, row.Children.OfType<Button>(), "the three scopes and the chip listing every tag, with no tag yet");
            var scopes = row.Children.OfType<Button>().Take(3).ToList();
            foreach (var ((name, glyph, hint), chip) in expected.Zip(scopes))
            {
                Assert.AreEqual(name, AutomationProperties.GetName(chip), "the name G4 gives it, which a screen reader reads");
                Assert.AreEqual($"{name} ({hint})", ToolTip.GetTip(chip), "the name and key in its tooltip");
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
                var row = new ChipRow(shown, listed.Organising, () => drag);
                DropOn(row, "#reaction");
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
        ControlThread.Run(() =>
        {
            var row = new ChipRow(listed.Listing, listed.Organising, () => null);
            var chip = row.Children.OfType<Button>().Single(button => button.Content as string == "#reaction");
            Assert.IsTrue(chip.IsEnabled);
            Click(chip, listed.Listing);
        });
        Assert.AreEqual("reaction", listed.Listing.Query);
        Assert.AreEqual(tagged.Id, listed.Listing.Items.Single().Id);
    }

    [TestMethod]
    public async Task EveryTagFitsAtTheRightEdgeWhenTheSearchLeavesAnExistingTagOutOfAWideRow()
    {
        using var listed = await ListedStash.Open();
        var cat = await listed.Stash.Import("one");
        var dog = await listed.Stash.Import("two");
        listed.Stash.Tag(cat.Id, "cat");
        listed.Stash.Tag(dog.Id, "dog");
        await listed.Listing.SetQuery("cat");

        ControlThread.Run(() =>
        {
            var row = Row(listed.Listing, listed.Organising);
            var size = new Size(700, 40);
            var catChip = row.Children.OfType<Button>().Single(chip => chip.Content as string == "#cat");
            catChip.Width = 50;
            var picker = row.Children.OfType<Button>().Last();
            picker.Width = 40;
            row.Measure(size);
            row.Arrange(new Rect(size));
            Assert.IsTrue(catChip.IsHitTestVisible);
            Assert.IsTrue(picker.IsHitTestVisible, "dog would fit here, but selecting it requires Every tag");
            Assert.AreEqual(size.Width, picker.Bounds.Right);
        });
    }

    [TestMethod]
    public async Task AChipUnavailableForFilteringStillTakesATileDrop()
    {
        using var listed = await ListedStash.Open();
        var store = listed.Stash.Store!;
        var cat = await listed.Stash.Import("one");
        var dog = await listed.Stash.Import("two");
        await store.AddTag([cat], "cat");
        await store.AddTag([dog], "dog");
        var shown = new Listing(listed.Failures.Add);
        await shown.StashOpened(store);
        await shown.ChooseTag("cat");

        var drag = new OwnDrag([cat]);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        listed.Organising.Changed += () =>
        {
            if (!listed.Organising.Changing)
            {
                finished.TrySetResult();
            }
        };
        ControlThread.Run(() =>
        {
            var row = new ChipRow(shown, listed.Organising, () => drag);
            row.Measure(new Size(400, 40));
            Dispatcher.UIThread.RunJobs();
            var chip = row.Children.OfType<Button>().Single(button => button.Content as string == "#dog");
            Assert.IsLessThan(1d, chip.Opacity, "the tag has no matching carrier");
            Assert.AreEqual(DragDropEffects.Copy, DropOn(row, "#dog"));
        });

        await finished.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.IsTrue(drag.TakenByChip);
        Assert.AreEqual("cat", shown.Query);
        CollectionAssert.AreEqual(new[] { "cat", "dog" }, (await store.Item(cat.Id)).Value!.Tags.ToArray());
    }

    [TestMethod]
    public async Task TwoClicksOnTheSameChipFilterAndThenRestoreTheGridWithoutMovingTheChips()
    {
        using var listed = await ListedStash.Open();
        var store = listed.Stash.Store!;
        var cat = await listed.Stash.Import("picture one");
        var dog = await listed.Stash.Import("picture two");
        await store.AddTag([cat], "cat");
        await store.AddTag([dog], "dog");
        await listed.Listing.ShowScope(Scope.All);
        var changes = listed.Listing.PersonsChanges;
        ControlThread.Run(() =>
        {
            var row = Row(listed.Listing, listed.Organising);
            var box = WindowLayout.SearchBox();
            SearchBoxQuery.Wire(box, listed.Listing, new SummonSelection());
            row.Measure(new Size(400, 40));
            Dispatcher.UIThread.RunJobs();
            var dogChip = row.Children.OfType<Button>().Single(chip => chip.Content as string == "#dog");

            Click(dogChip, listed.Listing);
            Assert.AreEqual("dog", listed.Listing.Query);
            Assert.AreEqual("dog", box.Text);
            Assert.AreEqual(3, box.CaretIndex);
            Assert.AreEqual("", box.SelectedText ?? "");
            Assert.AreEqual(changes + 1, listed.Listing.PersonsChanges);
            Assert.AreEqual(dog.Id, listed.Listing.Items.Single().Id);
            CollectionAssert.AreEqual(new[] { "#cat", "#dog" }, row.Children.OfType<Button>().Select(chip => chip.Content).OfType<string>().ToArray());
            Assert.AreSame(dogChip, row.Children.OfType<Button>().Single(chip => chip.Content as string == "#dog"));
            Click(dogChip, listed.Listing);
            Assert.AreEqual("", listed.Listing.Query);
            Assert.AreEqual("", box.Text ?? "");
            Assert.HasCount(2, listed.Listing.Items);
            Assert.AreEqual(changes + 2, listed.Listing.PersonsChanges);

            Type(box, listed.Listing, "ca");
            Assert.AreEqual("ca", listed.Listing.Query);
            Assert.AreEqual(cat.Id, listed.Listing.Items.Single().Id);
            CollectionAssert.AreEqual(new[] { "cat" }, listed.Listing.Chips.Select(chip => chip.Tag).ToArray());
            Assert.AreEqual(changes + 3, listed.Listing.PersonsChanges);
        });
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("ca")]
    public async Task NewTagsJoinTheEndOfAHeldRowOnceTheLastFilterIsClickedOff(string typed)
    {
        using var listed = await ListedStash.Open();
        var item = await listed.Stash.Import("picture");
        var other = await listed.Stash.Import("other");
        await listed.Organising.AddTag([item], "cat");
        await listed.Organising.AddTag([other], "owl");
        await listed.Listing.ShowScope(Scope.All);

        ControlThread.Run(() =>
        {
            var row = Row(listed.Listing, listed.Organising);
            var box = WindowLayout.SearchBox();
            SearchBoxQuery.Wire(box, listed.Listing, new SummonSelection());
            Type(box, listed.Listing, typed);
            row.Measure(new Size(700, 40));
            Dispatcher.UIThread.RunJobs();
            var original = typed.Length == 0 ? new[] { "#cat", "#owl" } : new[] { "#cat" };
            CollectionAssert.AreEqual(original, TagFaces());
            var cat = row.Children.OfType<Button>().Single(chip => chip.Content as string == "#cat");

            Click(cat, listed.Listing);
            Assert.AreEqual("cat", box.Text);
            FinishOnDispatcher(async () => Assert.IsTrue((await listed.Organising.AddTag([item], "dog")).Succeeded));
            CollectionAssert.AreEqual(original, TagFaces(), "a new tag waits while the held search has words");

            Click(cat, listed.Listing);
            Assert.AreEqual("", box.Text ?? "");
            CollectionAssert.AreEqual(original.Append("#dog").ToArray(), TagFaces(), "the newly made tag joins as soon as the last word is removed");
            FinishOnDispatcher(async () => Assert.IsTrue((await listed.Organising.AddTag([item], "ant")).Succeeded));
            var expanded = original.Concat(["#dog", "#ant"]).ToArray();
            CollectionAssert.AreEqual(expanded, TagFaces(), "new tags join in the order discovered");
            Assert.AreSame(cat, row.Children.OfType<Button>().First(chip => chip.Content is string));

            FinishOnDispatcher(async () => Assert.IsTrue((await listed.Organising.RemoveTag([item], "dog")).Succeeded));
            CollectionAssert.AreEqual(expanded, TagFaces(), "a tag that joined keeps its place when it dies");
            Assert.IsFalse(row.Children.OfType<Button>().Single(chip => chip.Content as string == "#dog").IsEnabled);

            FinishOnDispatcher(async () => Assert.IsTrue((await listed.Organising.RemoveTag([other], "owl")).Succeeded));
            FinishOnDispatcher(async () => Assert.IsTrue((await listed.Organising.AddTag([other], "Owl")).Succeeded));
            string[] recreated = typed.Length == 0 ? expanded : [.. expanded, "#Owl"];
            CollectionAssert.AreEqual(recreated, TagFaces(), "a recreated tag joins at the end if this row had no place for it");

            string[] TagFaces() => row.Children.OfType<Button>().Select(chip => chip.Content).OfType<string>().ToArray();
        });
    }

    [TestMethod]
    public async Task ANewTagStaysInPlaceWhenTheLastFilterIsClickedOffAndOnBeforeItsRead()
    {
        using var listed = await ListedStash.Open();
        var item = await listed.Stash.Import("picture");
        await listed.Organising.AddTag([item], "cat");
        await listed.Listing.ShowScope(Scope.All);

        ControlThread.Run(() =>
        {
            var box = WindowLayout.SearchBox();
            SearchBoxQuery.Wire(box, listed.Listing, new SummonSelection());
            Type(box, listed.Listing, "ca");
            var row = Row(listed.Listing, listed.Organising);
            row.Measure(new Size(700, 40));
            Dispatcher.UIThread.RunJobs();
            var cat = row.Children.OfType<Button>().Single(chip => chip.Content as string == "#cat");
            Click(cat, listed.Listing);
            FinishOnDispatcher(async () => Assert.IsTrue((await listed.Organising.AddTag([item], "dog")).Succeeded));

            FinishOnDispatcher(() =>
            {
                using (listed.Stash.HoldStore())
                {
                    cat.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert.AreEqual("", box.Text ?? "");
                    Assert.IsFalse(listed.Listing.Settled);
                    Assert.IsTrue(row.Children.OfType<Button>().Any(chip => chip.Content as string == "#dog"), "the new tag is visible while the scope read is pending");
                    cat.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert.AreEqual("cat", box.Text);
                }

                return listed.Listing.Settle();
            });
            CollectionAssert.AreEqual(new[] { "#cat", "#dog" },
                row.Children.OfType<Button>().Select(chip => chip.Content).OfType<string>().ToArray());
        });
    }

    private static void Click(Button chip, Listing listing) =>
        FinishOnDispatcher(() =>
        {
            chip.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            return listing.Settle();
        });

    private static void Type(TextBox box, Listing listing, string text) =>
        FinishOnDispatcher(() =>
        {
            box.Text = text;
            return listing.Settle();
        });

    private static DragDropEffects DropOn(ChipRow row, string tag)
    {
        var chip = row.Children.OfType<Button>().Single(button => button.Content as string == tag);
        using var data = new DataTransfer();
        var drop = new DragEventArgs(DragDrop.DropEvent, data, chip, new Point(1, 1), KeyModifiers.None) { DragEffects = DragDropEffects.Copy };
        chip.RaiseEvent(drop);
        return drop.DragEffects;
    }

    private static ChipRow Row(Listing listing, Organising? organising = null) =>
        new(listing, organising ?? new Organising(listing, new Notices(TimeProvider.System)), () => null);

    private static List<bool> Lit(IEnumerable<Button> chips) => chips.Select(chip => chip.Classes.Contains("accent")).ToList();

    private static List<TextBlock> Glyphs(Button chip) => chip.Content switch
    {
        TextBlock glyph => [glyph],
        Panel panel => panel.Children.OfType<TextBlock>().ToList(),
        _ => [],
    };
}
