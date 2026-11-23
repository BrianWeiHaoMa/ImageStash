using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
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
