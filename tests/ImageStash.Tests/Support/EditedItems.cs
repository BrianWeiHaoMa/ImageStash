using System.Security.Cryptography;
using ImageStash.Layers;
using ImageStash.Media;
using ImageStash.Store;

namespace ImageStash.Tests.Support;

/// <summary>
/// For checks of an item the store saved with layers: the lists they save, renderers the store can be given in place of
/// the product's (one that refuses, one that holds what it wrote), and the row's columns a Save writes, read between
/// the store's calls.
/// </summary>
internal static class EditedItems
{
    /// <summary>A black box over the middle and a red box outlining an 8 × 6 picture.</summary>
    public static readonly LayerList BlackBoxAndBox = new(
    [
        new HideLayer(new PixelRect(2, 2, 3, 2), HideKind.Black, null),
        new DrawMarkLayer(MarkKind.Box, [], new PixelRect(0, 0, 8, 6), new Rgba(255, 0, 0, 255), Thickness.Thin, null),
    ]);

    /// <summary>A blue arrow across the picture, a second list.</summary>
    public static readonly LayerList Arrow = new([new DrawMarkLayer(MarkKind.Arrow, [new(0, 0), new(7, 5)], null, new Rgba(0, 0, 255, 255), Thickness.Medium, null)]);

    /// <summary>A crop to the top-left 4 × 3 with a black box in it: a render whose facts are not the original's.</summary>
    public static readonly LayerList Cropped = new([new CropLayer(new PixelRect(0, 0, 4, 3)), new HideLayer(new PixelRect(0, 0, 2, 2), HideKind.Black, null)]);

    /// <summary>The reason <see cref="Refusing"/> gives.</summary>
    public const string RefusedReason = "the check's renderer refuses to render it";

    /// <summary>A renderer that writes nothing and answers a failure, as one whose original cannot be read does.</summary>
    public static readonly FinishedPictureRenderer Refusing = (_, name, _, _, _, _) => new Failure("Save the picture", name, RefusedReason);

    /// <summary>
    /// A renderer that renders as the product does and then holds the file it wrote with Read sharing, as a scanner may
    /// (E3), so the rename that places it is refused until the check disposes what it added to <paramref name="holds"/>.
    /// </summary>
    public static FinishedPictureRenderer Holding(List<FileStream> holds) => (original, name, layers, format, work, retry) =>
    {
        var rendered = FinishedPicture.Render(original, name, layers, format, work, retry);
        if (rendered.Succeeded)
        {
            holds.Add(TestStash.Hold(work));
        }

        return rendered;
    };

    /// <summary>An open stash holding "cat", imported from still.png and saved with <see cref="BlackBoxAndBox"/>: the item as imported and as listed after the Save.</summary>
    public static async Task<(StashItem Imported, StashItem Saved)> SavedCat(TestStash stash)
    {
        await stash.OpenCleanly();
        var imported = await stash.ImportFixture("cat", "still.png");
        var saved = await stash.SaveEdit(imported, BlackBoxAndBox);
        Assert.IsTrue(saved.Succeeded, saved.Failure?.ToString());
        return (imported, saved.Value!);
    }

    /// <summary>
    /// (a) A first Save of <paramref name="fixture"/> named "cat" with <see cref="BlackBoxAndBox"/>, committed with nothing
    /// moved: its move was refused while another program held the original. The store stays open.
    /// </summary>
    public static async Task<StashItem> FirstSaveCommitted(TestStash stash, string fixture = "still.png")
    {
        var cat = await stash.ImportFixture("cat", fixture);
        var render = await stash.Render(cat, BlackBoxAndBox);
        using (TestStash.Hold(cat.FilePath))
        {
            Assert.IsFalse((await stash.Store!.Save(cat, BlackBoxAndBox, render)).Succeeded);
        }

        return cat;
    }

    /// <summary>
    /// (b) A first Save as <see cref="FirstSaveCommitted"/> makes, whose original moved into <c>originals\</c> and whose
    /// render waits in <c>.work\</c>: its place was refused while another program held the render. The store stays open.
    /// </summary>
    public static async Task<StashItem> FirstSaveMoved(TestStash stash, string fixture = "still.png")
    {
        var cat = await stash.ImportFixture("cat", fixture);
        var render = await stash.Render(cat, BlackBoxAndBox);
        using (TestStash.Hold(render.WorkPath))
        {
            Assert.IsFalse((await stash.Store!.Save(cat, BlackBoxAndBox, render)).Succeeded);
        }

        return cat;
    }

    /// <summary>
    /// (c) A first Save as <see cref="FirstSaveCommitted"/> makes, both its steps done and its last transaction not run,
    /// as a kill between them leaves it: on the closed store.
    /// </summary>
    public static async Task<StashItem> FirstSavePlaced(TestStash stash, string fixture = "still.png")
    {
        var cat = await stash.ImportFixture("cat", fixture);
        Assert.IsTrue((await stash.SaveEdit(cat, BlackBoxAndBox)).Succeeded);
        stash.Close();
        var row = Row(stash, cat.Id);
        UnfinishLastTransaction(stash, cat.Id, renderedRevision: 0, previous: LayerList.Empty, finished: null,
            new PendingStep(PendingStep.MoveToOriginals, cat.FileName, row.OriginalFile),
            new PendingStep(PendingStep.PlaceRender, GoneWorkFile(row.FinishedFile), row.FinishedFile, Facts: row.Finished));
        return cat;
    }

    /// <summary>
    /// (d) A PNG "cat" saved with <see cref="Cropped"/>, then a second Save with <see cref="Arrow"/> committed, the earlier
    /// render in the root and the new one in <c>.work\</c>: its place was refused while another program held the render.
    /// The store stays open.
    /// </summary>
    public static async Task<StashItem> SecondSaveCommitted(TestStash stash)
    {
        var cat = await stash.ImportFixture("cat", "still.png");
        var first = (await stash.SaveEdit(cat, Cropped)).Value!;
        var render = await stash.Render(first, Arrow);
        using (TestStash.Hold(render.WorkPath))
        {
            Assert.IsFalse((await stash.Store!.Save(first, Arrow, render)).Succeeded);
        }

        return first;
    }

    /// <summary>
    /// (e) A PNG "cat" saved with <see cref="Cropped"/> (a 4 × 3 render), then with <see cref="Arrow"/> (8 × 6), the second
    /// render placed and the last transaction not run, as a kill between them leaves it: on the closed store.
    /// </summary>
    public static async Task<StashItem> SecondSavePlaced(TestStash stash)
    {
        var cat = await stash.ImportFixture("cat", "still.png");
        var first = (await stash.SaveEdit(cat, Cropped)).Value!;
        var second = (await stash.SaveEdit(first, Arrow)).Value!;
        stash.Close();
        UnfinishLastTransaction(stash, cat.Id, renderedRevision: 1, previous: Cropped, finished: first.Facts,
            new PendingStep(PendingStep.PlaceRender, GoneWorkFile(second.FileName), second.FileName, Facts: second.Facts));
        return second;
    }

    /// <summary>
    /// A "cat" of <paramref name="fixture"/> saved with <see cref="BlackBoxAndBox"/>, then its Revert committed with
    /// nothing moved: the original's move back was refused while another program held it. The store stays open.
    /// </summary>
    public static async Task<StashItem> RevertCommitted(TestStash stash, string fixture)
    {
        var saved = (await stash.SaveEdit(await stash.ImportFixture("cat", fixture), BlackBoxAndBox)).Value!;
        using (TestStash.Hold(stash.Folder.InOriginals("cat" + Path.GetExtension(fixture))))
        {
            Assert.IsFalse((await stash.Store!.Save(saved, LayerList.Empty, null)).Succeeded);
        }

        return saved;
    }

    /// <summary>
    /// A WebP "cat" saved with <see cref="BlackBoxAndBox"/> (its render <c>cat.png</c>), then its Revert with the original
    /// back as <c>cat.webp</c> and the render's delete refused while another program held it. The store stays open.
    /// </summary>
    public static async Task<StashItem> RevertMoved(TestStash stash)
    {
        var saved = (await stash.SaveEdit(await stash.ImportFixture("cat", "still.webp"), BlackBoxAndBox)).Value!;
        using (TestStash.Hold(saved.FilePath))
        {
            Assert.IsFalse((await stash.Store!.Save(saved, LayerList.Empty, null)).Succeeded);
        }

        return saved;
    }

    // The row of a completed Save put back as it stood before its last transaction, on the closed store.
    private static void UnfinishLastTransaction(TestStash stash, long id, long renderedRevision, LayerList previous, MediaFacts? finished, params PendingStep[] steps) =>
        stash.Sql(
            """
            UPDATE items SET rendered_revision = $rendered, previous_layers = $previous, pending_steps = $steps,
                finished_format = $format, finished_width = $width, finished_height = $height, finished_animated = $animated,
                finished_frame_count = $frames, finished_duration_ms = $duration
            WHERE id = $id
            """,
            ("$id", id),
            ("$rendered", renderedRevision),
            ("$previous", previous.ToJson()),
            ("$steps", PendingStep.Encode(steps)),
            ("$format", finished?.Format.ToString()),
            ("$width", finished?.Width),
            ("$height", finished?.Height),
            ("$animated", finished?.Animated),
            ("$frames", finished?.FrameCount),
            ("$duration", finished?.DurationMs));

    // A .work\ name no file has: the render a step names once it has been placed.
    private static string GoneWorkFile(string like) => Guid.NewGuid().ToString("N") + Path.GetExtension(like);

    /// <summary>The columns of the item's row a Save, a Revert and their recovery write, read between the store's calls.</summary>
    public static SavedRow Row(TestStash stash, long id) =>
        stash.SqlQueryWhileOpen(
            """
            SELECT layers, previous_layers, layer_revision, rendered_revision, original_in_originals, finished_file, original_file, pending_steps,
                   format, width, height, animated, frame_count, duration_ms,
                   finished_format, finished_width, finished_height, finished_animated, finished_frame_count, finished_duration_ms
            FROM items WHERE id = $id
            """,
            r => new SavedRow(
                r.GetString(0),
                r.IsDBNull(1) ? null : r.GetString(1),
                r.GetInt64(2),
                r.GetInt64(3),
                r.GetBoolean(4),
                r.GetString(5),
                r.GetString(6),
                r.IsDBNull(7) ? [] : PendingStep.Decode(r.GetString(7)),
                new MediaFacts(Enum.Parse<ImageFormat>(r.GetString(8)), r.GetInt32(9), r.GetInt32(10), r.GetBoolean(11), r.GetInt32(12), r.GetInt64(13)),
                r.IsDBNull(14) ? null : new MediaFacts(Enum.Parse<ImageFormat>(r.GetString(14)), r.GetInt32(15), r.GetInt32(16), r.GetBoolean(17), r.GetInt32(18), r.GetInt64(19))),
            ("$id", id)).Single();

    /// <summary>The store's commits as the item's read now carries them; a read commits nothing.</summary>
    public static async Task<long> Commits(StashStore store, long id) => (await store.Item(id)).Value!.CommitsWhenRead;

    /// <summary>The item as the stash lists it now, which must be listed.</summary>
    public static async Task<StashItem> Listed(StashStore store, long id)
    {
        var read = await store.Item(id);
        Assert.IsTrue(read.Succeeded, read.Failure?.ToString());
        Assert.IsNotNull(read.Value, "the item is not listed");
        return read.Value;
    }

    /// <summary>What Edit opens on the item now, which must be listed and readable.</summary>
    public static async Task<ItemForEdit> ForEdit(StashStore store, long id)
    {
        var read = await store.ForEdit(id);
        Assert.IsTrue(read.Succeeded, read.Failure?.ToString());
        Assert.IsNotNull(read.Value, "the item is not listed");
        return read.Value;
    }

    /// <summary>The files in <c>originals\</c>, by name.</summary>
    public static string[] OriginalFiles(TestStash stash) => Directory.GetFiles(stash.Folder.Originals).Select(Path.GetFileName).ToArray()!;

    /// <summary>The row's files as the store locates them now, from its row read between the store's calls.</summary>
    public static IReadOnlyList<string> StoredPaths(TestStash stash, long id) =>
        stash.SqlQueryWhileOpen($"SELECT {StoredFiles.Columns} FROM items i WHERE i.id = $id", r => StoredFiles.FromRow(r, 0), ("$id", id)).Single().Paths(stash.Folder);

    /// <summary>Every file in the stash folder but the store's, with a hash of its bytes, read while the store is open.</summary>
    public static List<string> FilesWithHashes(TestStash stash) =>
    [
        .. Directory.GetFiles(stash.Folder.Root, "*", SearchOption.AllDirectories)
            .Where(path => !Path.GetFileName(path).StartsWith(StashFolder.StoreFileName))
            .Order()
            .Select(path => $"{Path.GetRelativePath(stash.Folder.Root, path)} {Hash(path)}"),
    ];

    public static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    public static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}

/// <summary>A row's columns as <see cref="EditedItems.Row"/> reads them, its steps decoded.</summary>
internal sealed record SavedRow(
    string Layers,
    string? PreviousLayers,
    long LayerRevision,
    long RenderedRevision,
    bool OriginalInOriginals,
    string FinishedFile,
    string OriginalFile,
    IReadOnlyList<PendingStep> Steps,
    MediaFacts Original,
    MediaFacts? Finished);
