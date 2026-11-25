using ImageStash.Layers;
using ImageStash.Store;

namespace ImageStash.Tests.Support;

/// <summary>Calls that save or read captions, and a drawing-heavy stash population for bulk Store query checks.</summary>
internal static class CaptionedItems
{
    public const int Count = 5000;

    private static readonly Rgba Black = new(0, 0, 0, 255);

    public static LayerList Caption(string text) => new([Bar(text)]);

    public static async Task<StashItem> Save(TestStash stash, StashItem item, LayerList layers)
    {
        var saved = await stash.SaveEdit(item, layers);
        Assert.IsTrue(saved.Succeeded, saved.Failure?.ToString());
        return saved.Value!;
    }

    public static async Task HasText(StashStore store, long id, string[] current, string[] previous)
    {
        var text = (await Read(store))[id];
        CollectionAssert.AreEqual(current, text.Current.ToArray(), "committed list");
        CollectionAssert.AreEqual(previous, text.Previous.ToArray(), "previous list");
    }

    public static async Task<IReadOnlyDictionary<long, SavedLayerText>> Read(StashStore store)
    {
        var read = await store.ReadSavedLayerText();
        Assert.IsTrue(read.Succeeded, read.Failure?.ToString());
        return read.Value!;
    }

    public static string Fill(long addedAt)
    {
        var points = Enumerable.Range(0, 1500).Select(i => new PixelPoint(i % 512, i / 8)).ToArray();
        var layers = new LayerList([
            Bar(""),
            new DrawMarkLayer(MarkKind.Pen, points, null, Black, Thickness.Thin, null),
        ]);
        return $"""
            WITH RECURSIVE numbers(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM numbers WHERE n < {Count})
            INSERT INTO items (id, name, added_at, format, width, height, animated, frame_count, duration_ms,
                original_file, finished_file, original_sha256, layers, layer_revision, rendered_revision)
            SELECT n, 'picture ' || n, {addedAt} + n, 'Png', 512, 512, 0, 1, 0,
                n || '.png', n || '.png', zeroblob(32),
                json_set('{layers.ToJson()}', '$[0].text', 'saved caption ' || n), 1, 1
            FROM numbers
            """;
    }

    private static CaptionBarLayer Bar(string text) => new(text, "Segoe UI", 12, Black, Outline.None);
}
