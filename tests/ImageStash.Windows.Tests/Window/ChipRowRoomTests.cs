using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ImageStash.Look;
using ImageStash.Store;
using ImageStash.Window;
using ImageStash.Windows.Tests.Support;
using static ImageStash.Windows.Tests.Support.ShownWindow;

namespace ImageStash.Windows.Tests.Window;

/// <summary>The scope glyphs, their lit appearance in both takes, and the room for whole tag chips (G2, G4, G5, G7, G17).</summary>
[TestClass]
public sealed class ChipRowRoomTests
{
    private const string EveryTag = "Every tag";

    private static readonly (string Name, string Glyph, Scope Scope)[] Scopes =
    [
        ("Recent", "\uE823", Scope.Recent),
        ("Favorites", FavoriteStar.Outlined, Scope.Favorites),
        ("All", "\uE80A", Scope.All),
    ];

    [TestMethod]
    [DataRow("Light")]
    [DataRow("Dark")]
    public async Task ScopeGlyphChipsAreCompactAndEachLooksLitWhenClicked(string take)
    {
        using var pointer = TestMouse.PutBackAfterwards();
        TestMouse.MoveTo(TestMouse.Away);
        using var shown = Open();
        await shown.OpenStash(shown.Picture("scope glyphs.png"));
        var theme = take == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        var before = TrayDesktop.Get(() => Application.Current!.RequestedThemeVariant);
        try
        {
            TrayDesktop.Run(() => Application.Current!.RequestedThemeVariant = theme);
            await Eventually(() => TrayDesktop.Get(() => shown.Window.ActualThemeVariant == theme));
            TrayDesktop.Run(() =>
            {
                AssertScopeGlyphs(shown);
                foreach (var (name, _, _) in Scopes)
                {
                    var chip = shown.Chip(name);
                    Assert.AreEqual(name, ToolTip.GetTip(chip));
                    Assert.AreEqual(36, chip.Bounds.Width, 0.5, name);
                }
            });

            var renderedByScope = new Dictionary<Scope, RenderedWindow>();
            foreach (var (name, _, scope) in Scopes)
            {
                TrayDesktop.Run(() => RaiseClick(shown.Chip(name)));
                await Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled && shown.Listing.Scope == scope));
                TrayDesktop.Run(() => CollectionAssert.AreEqual(new[] { name },
                    Scopes.Select(face => face.Name).Where(shown.Lit).ToArray(), "only the clicked scope is lit"));
                renderedByScope.Add(scope, await RenderedWindow.Of(shown.Window));
            }

            TrayDesktop.Run(() =>
            {
                foreach (var (name, _, scope) in Scopes)
                {
                    var place = RenderedWindow.Place(shown.Window, shown.Chip(name));
                    var rest = renderedByScope[scope == Scope.All ? Scope.Recent : Scope.All];
                    Assert.IsTrue(PixelsDiffer(place, rest, renderedByScope[scope]), $"{name} looks different when lit in {take}");
                    Assert.IsGreaterThan(4, GlyphInk(place, rest, shown.Window.RenderScaling), $"{name}'s glyph shows on its ground at rest in {take}");
                    Assert.IsGreaterThan(4, GlyphInk(place, renderedByScope[scope], shown.Window.RenderScaling), $"{name}'s glyph shows on the lit ground in {take}");
                }
            });
        }
        finally
        {
            TrayDesktop.Run(() => Application.Current!.RequestedThemeVariant = before);
        }
    }

    [TestMethod]
    public async Task AtLeastFourOfSixThreeLetterTagsFitBesideEveryTagInTheDefaultFlyout()
    {
        using var shown = Open();
        var item = (await shown.OpenStash(shown.Picture("six tags.png"))).Single();
        await shown.Tag([item], "ant bee cat dog MWW WWM");

        TrayDesktop.Run(() =>
        {
            shown.Window.UpdateLayout();
            Assert.AreEqual(460, shown.Window.ClientSize.Width, 0.5, "the default flyout width");
            Assert.AreEqual(640, shown.Window.ClientSize.Height, 0.5, "the default flyout height");
            Assert.HasCount(6, shown.Listing.Tags);
            var chips = AssertWholeChipRowWithEveryTag(shown);
            Assert.IsGreaterThanOrEqualTo(4, chips.Length - Scopes.Length - 1, "at least four tag chips fit (G4)");
        });
    }

    [TestMethod]
    public async Task TheThreeCommonestTagsFitAndEveryTagEndsTheRowEvenAtMinimumWidth()
    {
        using var shown = Open();
        var items = await shown.OpenStash(shown.Picture("common tags.png"), shown.Picture("more common tags.png"));
        await shown.Tag(items, "cat food tired");
        await shown.Tag([items[0]], string.Join(' ', Enumerable.Range(1, 12).Select(i => $"reaction-tag-{i:00}")));

        TrayDesktop.Run(() =>
        {
            var chips = AssertWholeChipRowWithEveryTag(shown);
            CollectionAssert.AreEqual(new[] { "#cat", "#food", "#tired" }, chips.Skip(Scopes.Length).Take(3).Select(ChipLabel).ToArray());
            shown.Window.Width = WindowSize.Minimum.Width;
            shown.Window.Height = WindowSize.Minimum.Height;
        });
        await Eventually(() => TrayDesktop.Get(() => Math.Abs(shown.Window.ClientSize.Width - WindowSize.Minimum.Width) <= 0.5
            && Math.Abs(shown.Window.ClientSize.Height - WindowSize.Minimum.Height) <= 0.5));
        TrayDesktop.Run(() =>
        {
            WindowMinimum.AssertAt(shown.Window.ClientSize, "the flyout at its minimum");
            var chips = AssertWholeChipRowWithEveryTag(shown);
            CollectionAssert.AreEqual(new[] { "#cat", "#food", "#tired" }, chips.Skip(Scopes.Length).Take(3).Select(ChipLabel).ToArray(), "the three commonest still show at the minimum width");
        });
    }

    [TestMethod]
    public async Task UnderANoMatchQueryTheChipListingEveryTagStaysWhileTheRowCouldNotHoldEveryTag()
    {
        using var shown = Open();
        var item = (await shown.OpenStash(shown.Picture("just too many.png"))).Single();
        var tags = new[] { "angry", "cat", "cute", "dog", "food", "funny", "sleepy", "tired", "wow" };
        await shown.Tag([item], string.Join(' ', tags));
        TrayDesktop.Run(() => shown.ChipRow.Measure(new Size(3000, 40)));
        await Eventually(() => TrayDesktop.Get(() => shown.ChipRow.Children.OfType<Button>().Count(chip => ChipLabel(chip).StartsWith('#')) == tags.Length));

        // The row every tag needs, measured from its own chips: the scopes, the separator and the nine tag chips.
        var (full, offset) = TrayDesktop.Get(() =>
        {
            shown.ChipRow.Measure(new Size(3000, 40));
            var everyTag = shown.Chip(EveryTag);
            var needed = shown.ChipRow.Children.Where(child => !ReferenceEquals(child, everyTag)).ToList();
            return (needed.Sum(child => child.DesiredSize.Width) + 6 * (needed.Count - 1), shown.Window.ClientSize.Width - shown.ChipRow.Bounds.Width);
        });

        // A little short of it, more than a chip's rounding and less than the separator and the chips' edges.
        var width = full - 12;
        TrayDesktop.Run(() => shown.Window.Width = width + offset);
        await Eventually(() => TrayDesktop.Get(() =>
        {
            shown.Window.UpdateLayout();
            return Math.Abs(shown.ChipRow.Bounds.Width - width) <= 0.5;
        }));
        TrayDesktop.Run(() => Assert.AreEqual(EveryTag, shown.ShownChips()[^1], "the row cannot hold every tag (G4)"));

        TrayDesktop.Run(() => shown.Search.Text = "zzzzmissing");
        await TrayDesktop.Get(() => shown.Listing.Settle());
        TrayDesktop.Run(() => CollectionAssert.AreEqual(new[] { "Recent", "Favorites", "All", EveryTag }, shown.ShownChips(),
            "no match shows no tag chip, and the row that could not hold every tag still ends with the chip listing them (G4)"));
    }

    [TestMethod]
    public async Task AWidePinnedRowUsesAllOfItsRoomForTags()
    {
        using var shown = Open();
        var item = (await shown.OpenStash(shown.Picture("sixty single letters.png"))).Single();
        const string letters = "abcdefghijklmnopqrstuvwxyzαβγδεζηθικλμνξοπρστυφχψωабвгдежзий";
        var tags = letters.OrderBy(letter => letter.ToString(), StringComparer.OrdinalIgnoreCase).Select(letter => "#" + letter).ToArray();
        await shown.Tag([item], string.Join(' ', letters.Select(letter => letter.ToString())));
        var flyoutTags = TrayDesktop.Get(() => shown.ShownChips().Count(label => label.StartsWith('#')));

        TrayDesktop.Run(() =>
        {
            shown.Settings.Change(settings => settings with { PinnedSize = new WindowSize(1100, 640) });
            var pin = shown.Window.GetVisualDescendants().OfType<ToggleButton>()
                .Single(button => AutomationProperties.GetName(button) == "Keep open");
            RaiseClick(pin);
        });
        await Eventually(() => TrayDesktop.Get(() => shown.Window.ShowInTaskbar && shown.Window.ClientSize.Width > 1000));
        // A layout pass at the new width queues the extra tag buttons for a later dispatcher turn.
        await Dispatcher.UIThread.InvokeAsync(shown.Window.UpdateLayout, DispatcherPriority.Background);

        TrayDesktop.Run(() =>
        {
            Assert.HasCount(60, shown.Listing.Tags);
            var chips = AssertWholeChipRowWithEveryTag(shown);
            var visibleTags = chips.Skip(Scopes.Length).SkipLast(1).ToArray();
            Assert.IsGreaterThan(flyoutTags, visibleTags.Length, "the pinned row holds more than the flyout");
            Assert.IsLessThan(tags.Length, visibleTags.Length, "there are tags left over at this width");
            CollectionAssert.AreEqual(tags.Take(visibleTags.Length).ToArray(), visibleTags.Select(ChipLabel).ToArray());

            var last = visibleTags[^1];
            var gap = last.Bounds.Left - visibleTags[^2].Bounds.Right;
            var next = shown.Chip(tags[visibleTags.Length]);
            var room = chips[^1].Bounds.Left - last.Bounds.Right - 2 * gap;
            Assert.IsGreaterThan(room, next.DesiredSize.Width, $"{ChipLabel(next)} would need {next.DesiredSize.Width:F2} units, with {room:F2} free");
        });
    }

    [TestMethod]
    public async Task EveryTagHasTwoGlyphsAndStaysOutOfTheRowWhileAllTagsFit()
    {
        using var shown = Open();
        var item = (await shown.OpenStash(shown.Picture("every tag glyphs.png"))).Single();
        await shown.Tag([item], "cat");
        TrayDesktop.Run(() =>
        {
            Assert.DoesNotContain(EveryTag, shown.ShownChips());
            Assert.Contains("#cat", shown.ShownChips());
            var chip = shown.Chip(EveryTag);
            Assert.IsFalse(chip.IsHitTestVisible, "the hidden chip cannot be reached");
            Assert.IsGreaterThanOrEqualTo(shown.ChipRow.Bounds.Width, chip.Bounds.Left, "the hidden chip is beyond the clipped edge");
        });

        await shown.Tag([item], string.Join(' ', Enumerable.Range(1, 12).Select(i => $"reaction-tag-{i:00}")));
        TrayDesktop.Run(() =>
        {
            var chip = AssertWholeChipRowWithEveryTag(shown)[^1];
            Assert.AreEqual(EveryTag, AutomationProperties.GetName(chip));
            Assert.AreEqual(EveryTag, ToolTip.GetTip(chip));
            Assert.IsInstanceOfType<StackPanel>(chip.Content);
            var face = (StackPanel)chip.Content!;
            Assert.HasCount(2, face.Children);
            AssertGlyph(face.Children[0], "\uE8EC");
            AssertGlyph(face.Children[1], "\uE70D");
        });
    }

    [TestMethod]
    public async Task FavoritesRingsWhileTheWindowsOwnTilesAreOverItAndADropSetsTheirFlags()
    {
        using var pointer = TestMouse.PutBackAfterwards();
        TestMouse.MoveTo(TestMouse.Away);
        var drags = new PresetDrags();
        using var shown = Open(drags);
        var items = await shown.OpenStash(shown.Picture("ring first.png"), shown.Picture("ring second.png"), shown.Picture("ring other.png"));
        var dragged = items.Take(2).ToArray();
        var data = await shown.Files(dragged.Select(item => item.FilePath).ToArray());
        var target = await shown.DropTargetAt("Favorites");
        var chip = TrayDesktop.Get(() => shown.Chip("Favorites"));
        var presenter = TrayDesktop.Get(() => chip.GetVisualDescendants().OfType<ContentPresenter>()
            .Single(content => content.Name == "PART_ContentPresenter"));
        var rest = TrayDesktop.Get(() => (presenter.BorderThickness, presenter.BorderBrush));
        var accent = TrayDesktop.Get(() =>
        {
            Assert.IsTrue(chip.TryFindResource(LookKeys.AccentRing, chip.ActualThemeVariant, out var brush));
            return brush;
        });
        var effects = new List<DragDropEffects>();
        var over = rest;
        var after = rest;
        drags.During = () =>
        {
            effects.Add(RaiseDrag(target, DragDrop.DragEnterEvent, data));
            effects.Add(RaiseDrag(target, DragDrop.DragOverEvent, data));
            over = (presenter.BorderThickness, presenter.BorderBrush);
            effects.Add(RaiseDrag(target, DragDrop.DropEvent, data));
            after = (presenter.BorderThickness, presenter.BorderBrush);
        };

        ChipRowTests.Drag(shown, dragged);
        await Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled && shown.Listing.Items.Count(item => item.Favorite) == dragged.Length));
        CollectionAssert.AreEqual(new[] { DragDropEffects.Copy, DragDropEffects.Copy, DragDropEffects.Copy }, effects);
        Assert.AreEqual(new Thickness(2), over.BorderThickness, "the drag ring is two units thick");
        Assert.AreEqual(accent, over.BorderBrush, "the drag ring uses the take's accent");
        Assert.AreEqual(rest, after, "the drop clears the ring");
        CollectionAssert.AreEquivalent(dragged.Select(item => item.FilePath).ToArray(), drags.Dragged.Single().ToArray());
        TrayDesktop.Run(() => CollectionAssert.AreEquivalent(dragged.Select(item => item.Id).ToArray(),
            shown.Listing.Items.Where(item => item.Favorite).Select(item => item.Id).ToArray(), "only the dragged tiles become favorites (G17)"));
    }

    internal static void AssertScopeGlyphs(ShownWindow shown)
    {
        var shownChips = shown.ShownChips();
        CollectionAssert.AreEqual(Scopes.Select(face => face.Name).ToArray(), shownChips.Take(Scopes.Length).ToArray());
        foreach (var (name, glyph, _) in Scopes)
        {
            var chip = shown.Chip(name);
            Assert.AreEqual(name, AutomationProperties.GetName(chip));
            AssertGlyph(chip.Content, glyph);
            AssertChipHeightAndFocus(chip);
        }
    }

    private static void AssertGlyph(object? content, string glyph)
    {
        Assert.IsInstanceOfType<TextBlock>(content);
        var text = (TextBlock)content!;
        Assert.AreEqual("Segoe Fluent Icons", text.FontFamily.Name);
        Assert.AreEqual(glyph, text.Text);
    }

    private static Button[] AssertWholeChipRowWithEveryTag(ShownWindow shown)
    {
        shown.Window.UpdateLayout();
        var row = shown.ChipRow;
        // Use reachability here because ShownChips filters out clipped chips before returning their labels.
        var chips = row.Children.OfType<Button>().Where(chip => chip.IsHitTestVisible).ToArray();
        CollectionAssert.AreEqual(Scopes.Select(face => face.Name).ToArray(), chips.Take(Scopes.Length).Select(ChipLabel).ToArray());
        Assert.AreSame(shown.Chip(EveryTag), chips[^1], "Every tag ends the row");
        Assert.AreEqual(row.Bounds.Width, chips[^1].Bounds.Right, 0.5, "Every tag is at the right edge");
        var right = 0.0;
        foreach (var chip in chips)
        {
            var label = ChipLabel(chip);
            var bounds = chip.Bounds;
            AssertChipHeightAndFocus(chip);
            Assert.IsGreaterThanOrEqualTo(right - 0.5, bounds.Left, $"{label} follows the previous chip without overlap");
            Assert.IsLessThanOrEqualTo(row.Bounds.Width + 0.5, bounds.Right, $"{label} fits horizontally");
            Assert.IsGreaterThanOrEqualTo(-0.5, bounds.Top, $"{label} starts inside the row");
            Assert.IsLessThanOrEqualTo(row.Bounds.Height + 0.5, bounds.Bottom, $"{label} finishes inside the row");
            Assert.AreEqual(chip.DesiredSize.Width, bounds.Width, 0.5, $"{label} has its full width");
            if (label.StartsWith('#'))
            {
                Assert.AreEqual(new Thickness(10, 0), chip.Padding, label);
            }
            right = bounds.Right;
        }
        return chips;
    }

    private static void AssertChipHeightAndFocus(Button chip)
    {
        var label = ChipLabel(chip);
        Assert.AreEqual(32, chip.Bounds.Height, 0.5, label);
        Assert.IsFalse(chip.Focusable, $"{label} takes no focus (G7)");
    }

    /// <summary>The pixels of the glyph's square, at the chip's middle, that differ from the chip's ground beside it.</summary>
    private static int GlyphInk(Rect place, RenderedWindow rendered, double scaling)
    {
        var (middleX, middleY) = ((int)place.Center.X, (int)place.Center.Y);
        var ground = rendered.At((int)Math.Ceiling(place.Left + 4 * scaling), middleY);
        var half = (int)(6 * scaling);
        var ink = 0;
        for (var y = middleY - half; y <= middleY + half; y++)
        {
            for (var x = middleX - half; x <= middleX + half; x++)
            {
                ink += rendered.At(x, y) != ground ? 1 : 0;
            }
        }

        return ink;
    }

    private static bool PixelsDiffer(Rect place, RenderedWindow rest, RenderedWindow lit)
    {
        for (var y = (int)Math.Ceiling(place.Top); y < (int)place.Bottom; y++)
        {
            for (var x = (int)Math.Ceiling(place.Left); x < (int)place.Right; x++)
            {
                if (rest.At(x, y) != lit.At(x, y))
                {
                    return true;
                }
            }
        }
        return false;
    }
}
