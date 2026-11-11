using ImageStash.Store;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// The grid's thumbnail size in effect (G6, G32): stepped through the five sizes, held at each end, each change told once
/// to whoever follows it and remembered across a restart; a size chosen, told once, and the size in effect chosen again,
/// told to nobody; a size the grid lacks refused.
/// </summary>
[TestClass]
public sealed class ThumbnailSizingTests
{
    [TestMethod]
    public async Task FromSmallFourStepsReachTheLargestAFifthChangesNothingAndFourBackReachSmallEachStepToldOnceAndRemembered()
    {
        using var scratch = new ScratchFolder();
        var failures = new List<Failure>();
        var keeper = new SettingsKeeper(scratch.Path, Settings.Load(scratch.Path, TestStash.ShortRetry), TestStash.ShortRetry, TimeSpan.Zero, failures.Add);
        var sizing = new ThumbnailSizing(keeper);
        sizing.Choose(ThumbnailSize.Small);
        var told = new List<ThumbnailSize>();
        sizing.Changed += () => told.Add(sizing.Current);

        for (var step = 0; step < 5; step++)
        {
            sizing.Step(1);
        }

        await keeper.Flush();
        var afterRestartUp = Settings.Load(scratch.Path, TestStash.ShortRetry).Settings.ThumbnailSize;
        for (var step = 0; step < 5; step++)
        {
            sizing.Step(-1);
        }

        await keeper.Flush();

        CollectionAssert.AreEqual(
            new[] { ThumbnailSize.Medium, ThumbnailSize.Large, ThumbnailSize.ExtraLarge, ThumbnailSize.Huge, ThumbnailSize.ExtraLarge, ThumbnailSize.Large, ThumbnailSize.Medium, ThumbnailSize.Small },
            told,
            "four steps up and four down, each told once; the fifth each way, held at the end, changes nothing (G6)");
        var heights = told.Take(4).Prepend(ThumbnailSize.Small).Select(JustifiedRows.RowHeight).ToList();
        CollectionAssert.AreEqual(heights.Order().Distinct().ToList(), heights, "each step up lays the rows out taller than the last");
        Assert.AreEqual(ThumbnailSize.Huge, afterRestartUp, "the largest, read again as a start reads the settings (G6: remembered)");
        Assert.AreEqual(ThumbnailSize.Small, Settings.Load(scratch.Path, TestStash.ShortRetry).Settings.ThumbnailSize, "and the smallest");
        Assert.IsEmpty(failures);
    }

    [TestMethod]
    public async Task AChosenSizeIsToldOnceTheSizeInEffectChosenAgainToNobodyAndASizeTheGridLacksIsRefused()
    {
        using var scratch = new ScratchFolder();
        var keeper = new SettingsKeeper(scratch.Path, Settings.Load(scratch.Path, TestStash.ShortRetry), TestStash.ShortRetry, TimeSpan.Zero, _ => { });
        var sizing = new ThumbnailSizing(keeper);
        var told = 0;
        sizing.Changed += () => told++;

        sizing.Choose(ThumbnailSize.ExtraLarge);
        sizing.Choose(ThumbnailSize.ExtraLarge);

        Assert.AreEqual((1, ThumbnailSize.ExtraLarge, ThumbnailSize.ExtraLarge), (told, sizing.Current, keeper.Current.ThumbnailSize), "told once, in effect and kept");
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => sizing.Choose((ThumbnailSize)(-1)));
        Assert.AreEqual((1, ThumbnailSize.ExtraLarge), (told, sizing.Current), "the refusal changed nothing");
        await keeper.Flush();
        Assert.AreEqual(ThumbnailSize.ExtraLarge, Settings.Load(scratch.Path, TestStash.ShortRetry).Settings.ThumbnailSize, "written before the folder goes");
    }
}
