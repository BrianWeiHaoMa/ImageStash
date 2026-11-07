using ImageStash.Media;
using ImageStash.Store;

namespace ImageStash.Tests.Support;

/// <summary>Items as the store would list them, built without a stash, for checks of what the grid does with their facts.</summary>
public static class TestItems
{
    /// <summary>An item of this picture size, its file the fixture <paramref name="fixture"/>.</summary>
    public static StashItem Sized(long id, int width, int height, bool animated = false, string fixture = "still.png") =>
        new(id, $"item {id}", [], false, DateTimeOffset.UnixEpoch.AddMinutes(id), fixture, MediaFixtures.Path(fixture),
            new MediaFacts(ImageFormat.Png, width, height, animated, animated ? 3 : 1, animated ? 600 : 0), 0, Edited: false);

    /// <summary><paramref name="count"/> items of a few different shapes, identities 1 upwards.</summary>
    public static IReadOnlyList<StashItem> Many(int count) =>
        Enumerable.Range(1, count).Select(i => Sized(i, 120 + i % 7 * 40, 100 + i % 5 * 30)).ToList();
}
