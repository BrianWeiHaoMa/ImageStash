using System.Diagnostics;
using ImageStash.Store;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>G6's justified rows at real aspect ratio from the store's facts, the 0.62–2.1 clamp with its crop, and the rows in view (G1, E4).</summary>
[TestClass]
public sealed class JustifiedRowsTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void AFullRowFillsTheWidthExactlyAndTheLastKeepsTheTargetHeight()
    {
        var items = new[] { (300, 200), (300, 200), (300, 200), (300, 200), (200, 200) }
            .Select((size, i) => TestItems.Sized(i + 1, size.Item1, size.Item2)).ToList();

        var rows = JustifiedRows.Layout(items, 600, 100, gap: 0);

        Assert.HasCount(2, rows);
        Assert.HasCount(4, rows[0].Tiles);
        Assert.AreEqual(600, rows[0].Tiles.Sum(t => t.Width), 1e-9);
        Assert.IsTrue(rows[0].Tiles.All(t => Math.Abs(t.Width - 150) < 1e-9 && t.Height == 100));
        var last = rows[1].Tiles.Single();
        Assert.AreEqual((5L, 100.0, 100.0, 100.0), (last.Id, last.Y, last.Width, last.Height));
    }

    [TestMethod]
    public void WithGapsAFullRowsTilesAndGapsSumToTheWidthAndKeepTheirRatios()
    {
        var items = TestItems.Many(40);

        var rows = JustifiedRows.Layout(items, 440, 96);

        foreach (var row in rows.Take(rows.Count - 1))
        {
            var last = row.Tiles[^1];
            Assert.AreEqual(440, last.X + last.Width, 1e-6);
            Assert.IsLessThanOrEqualTo(96 + 1e-9, row.Height);
            foreach (var tile in row.Tiles.Take(row.Tiles.Count - 1))
            {
                Assert.AreEqual(JustifiedRows.Ratio(items[tile.Index].Facts), tile.Width / tile.Height, 1e-9);
            }
        }

        Assert.AreEqual(96, rows[^1].Height);
        CollectionAssert.AreEqual(items.Select(i => i.Id).ToList(), rows.SelectMany(r => r.Tiles).Select(t => t.Id).ToList(), "tiles keep the listing's order");
        for (var r = 1; r < rows.Count; r++)
        {
            Assert.AreEqual(rows[r - 1].Y + rows[r - 1].Height + JustifiedRows.Gap, rows[r].Y, 1e-9);
        }
    }

    [TestMethod]
    public void RatiosOutsideTheRangeAreClampedAndTheirPicturesCroppedToTheTile()
    {
        Assert.AreEqual(2.1, JustifiedRows.Ratio(TestItems.Sized(1, 400, 100).Facts));
        Assert.AreEqual(0.62, JustifiedRows.Ratio(TestItems.Sized(2, 100, 400).Facts));
        Assert.AreEqual(1.5, JustifiedRows.Ratio(TestItems.Sized(3, 300, 200).Facts));

        var wide = JustifiedRows.Cover(400, 100, 210, 100);
        Assert.AreEqual((95.0, 0.0, 210.0, 100.0), (wide.X, wide.Y, wide.Width, wide.Height));
        var tall = JustifiedRows.Cover(100, 400, 62, 100);
        Assert.AreEqual(0, tall.X, 1e-9);
        Assert.AreEqual(100, tall.Width, 1e-9);
        Assert.AreEqual(100 / 0.62, tall.Height, 1e-9);
        Assert.AreEqual((400 - 100 / 0.62) / 2, tall.Y, 1e-9);
        var kept = JustifiedRows.Cover(300, 200, 150, 100);
        Assert.AreEqual((0.0, 0.0, 300.0, 200.0), (kept.X, kept.Y, kept.Width, kept.Height));
    }

    [TestMethod]
    public void TheRowsInViewAreThoseTheBandAndItsMarginTouch()
    {
        var rows = JustifiedRows.Layout(TestItems.Many(5000), 440, 96);

        var (first, last) = JustifiedRows.InView(rows, 0, 600, 600);
        var (scrolledFirst, scrolledLast) = JustifiedRows.InView(rows, 50_000, 600, 0);

        Assert.AreEqual(0, first);
        Assert.IsLessThanOrEqualTo(1200, rows[last].Y);
        Assert.IsGreaterThan(1200, rows[last + 1].Y);
        Assert.IsGreaterThanOrEqualTo(50_000, rows[scrolledFirst].Y + rows[scrolledFirst].Height);
        Assert.IsLessThan(50_000, rows[scrolledFirst - 1].Y + rows[scrolledFirst - 1].Height);
        Assert.IsLessThanOrEqualTo(50_600, rows[scrolledLast].Y);
        Assert.AreEqual((0, -1), JustifiedRows.InView([], 0, 600, 600));
    }

    [TestMethod]
    public void FiveThousandItemsAreLaidOutWellWithinTheSecond()
    {
        var items = TestItems.Many(5000);

        var clock = Stopwatch.StartNew();
        var rows = JustifiedRows.Layout(items, 1200, JustifiedRows.RowHeight(ThumbnailSize.Small));
        clock.Stop();

        TestContext.WriteLine($"5,000 items laid out in {clock.Elapsed.TotalMilliseconds:F1} ms, {rows.Count} rows");
        Assert.AreEqual(5000, rows.Sum(r => r.Tiles.Count));
        Assert.IsLessThan(100, clock.Elapsed.TotalMilliseconds);
    }

    [TestMethod]
    public void TheThreeSizesHaveThreeRowHeights()
    {
        var heights = Enum.GetValues<ThumbnailSize>().Select(JustifiedRows.RowHeight).ToList();

        Assert.HasCount(3, heights.Distinct());
        CollectionAssert.AreEqual(heights.Order().ToList(), heights);
        Assert.AreEqual(ThumbnailSize.Medium, new Settings().ThumbnailSize, "the middle size is the default");
    }

    [TestMethod]
    public async Task ASizeStepLaysTheRowsOutAtItsHeightAsksForLargerThumbnailsAndIsRememberedAcrossARestart()
    {
        using var scratch = new ScratchFolder();
        var failures = new List<Failure>();
        var keeper = new SettingsKeeper(scratch.Path, Settings.Load(scratch.Path, TestStash.ShortRetry), TestStash.ShortRetry, TimeSpan.Zero, failures.Add);
        var items = TestItems.Many(30);
        void Step(int steps) => keeper.Change(s => s with { ThumbnailSize = JustifiedRows.Step(s.ThumbnailSize, steps) });

        Step(1);
        var large = keeper.Current.ThumbnailSize;
        Step(1);
        var stillLarge = keeper.Current.ThumbnailSize;
        Step(-1);
        Step(-1);
        var small = keeper.Current.ThumbnailSize;
        Step(-1);
        await keeper.Flush();

        Assert.AreEqual((ThumbnailSize.Large, ThumbnailSize.Large, ThumbnailSize.Small), (large, stillLarge, small), "three sizes, held at the ends");
        Assert.AreEqual(ThumbnailSize.Small, keeper.Current.ThumbnailSize);
        Assert.AreEqual(ThumbnailSize.Small, Settings.Load(scratch.Path, TestStash.ShortRetry).Settings.ThumbnailSize, "a restart opens at the last size");
        Assert.IsEmpty(failures);
        var heights = Enum.GetValues<ThumbnailSize>().Select(size => JustifiedRows.Layout(items, 440, JustifiedRows.RowHeight(size)).Max(row => row.Height)).ToList();
        CollectionAssert.AreEqual(heights.Order().ToList(), heights, "the rows are laid out taller at each larger size");
        Assert.HasCount(3, heights.Distinct());
        var bounds = Enum.GetValues<ThumbnailSize>().Select(size => Thumbnails<object>.Bound(items[0].Facts, JustifiedRows.RowHeight(size), 1.5)).ToList();
        CollectionAssert.AreEqual(bounds.Order().ToList(), bounds);
        Assert.HasCount(3, bounds.Distinct(), "each size asks for its thumbnails at its own bound");
    }
}
