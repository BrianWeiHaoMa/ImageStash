using System.Globalization;
using ImageStash.Files;
using ImageStash.Layers;

namespace ImageStash.Store;

/// <summary>
/// Save as new item (G26, State: Save as new item, Every change, File names; F6, F7, G12, G28, R9). Store copies the
/// item's original into <c>.work\</c>; one transaction inserts a second item with the same tags, not favorite, named
/// with a counter, the original's record and hash copied, the layers with equal revisions and the render's facts, and
/// the steps: the copy placed in <c>originals\</c>, then the render placed in the root, both under one stem; or, with
/// no layers, the copy alone placed in the root, the import's step. The steps run in that order and one transaction
/// clears them. No query lists the new row until every step is done, and the item it came from is left as it was. A
/// kill or a refused step leaves the start a row to clear, or to remove with every file it placed or left in
/// <c>.work\</c>, never recycled: no item ever existed.
/// </summary>
internal static class ItemSaveAsNewItem
{
    public const string Action = "Save as new item";

    private const string UndoAtStart = "Undo an unfinished save as new item";

    private const string CopyTakesNoRender = "a copy without layers takes no rendered picture";

    /// <summary>
    /// Makes a second item of <paramref name="item"/> carrying <paramref name="layers"/>, which the caller rendered into
    /// <paramref name="render"/> over this item's original in its finished format (none with an empty list), and returns
    /// it as the stash lists it once its files are in place. Before the insert a refusal changes nothing of the new item
    /// and leaves the render the caller's; a pending change of the item is finished first, or the call refused. From the
    /// insert on, a refused step fails after it, the new row unlisted, for the next start to remove.
    /// </summary>
    public static Outcome<StashItem> SaveAsNewItem(StoreContext store, StashItem item, LayerList layers, RenderedPicture? render)
    {
        var listed = ItemSave.ListedRow(store, item, Action);
        if (!listed.Succeeded)
        {
            return listed.Failure;
        }

        var row = listed.Value!;
        if (ItemSave.Mismatch(store, row, layers, render, CopyTakesNoRender) is { } mismatch)
        {
            return new Failure(Action, row.Subject, mismatch);
        }

        if (row.Files.Steps.Count > 0)
        {
            // The copy is made of the original where the item's own change left it, never while that change is pending.
            if (PendingStepRecovery.FinishBeforeChange(store, row.Files, row.Name, Action) is { } unfinished)
            {
                return unfinished;
            }

            listed = ItemSave.ListedRow(store, item, Action);
            if (!listed.Succeeded)
            {
                return listed.Failure;
            }

            row = listed.Value!;
            // As Save judges it: a pending adoption finished just now has made another file the original (Approach).
            if (ItemSave.Mismatch(store, row, layers, render, CopyTakesNoRender) is { } changed)
            {
                return new Failure(Action, row.Subject, changed);
            }
        }

        // A copy, never the item's own file: the item being edited is left as it was (G26).
        var copied = WorkFolder.CopyIn(store, row.Files.OriginalPath(store.Folder), Action);
        if (!copied.Succeeded)
        {
            return copied.Failure with { Subject = row.Subject };
        }

        var copy = copied.Value!;
        var addedAt = DateTimeOffset.FromUnixTimeMilliseconds(store.Clock.GetUtcNow().ToUnixTimeMilliseconds());
        var inserted = store.Change(Action, () => Insert(store, row, layers, render, Path.GetFileName(copy), addedAt));
        if (!inserted.Succeeded)
        {
            store.RetryHeldFile("Discard a copy", copy, () => File.Delete(copy));
            return inserted.Failure;
        }

        var (asCommitted, steps) = inserted.Value;
        foreach (var step in steps)
        {
            var from = step.FromPath(store.Folder);
            var placed = store.RetryHeldFile(Action, from, () => File.Move(from, step.ToPath(store.Folder)));
            if (!placed.Succeeded)
            {
                return ItemQueries.AfterCommit(store, asCommitted, placed.Failure with { Subject = asCommitted.Name });
            }
        }

        var cleared = store.Change(Action, () => store.Execute("UPDATE items SET pending_steps = NULL WHERE id = $id", ("$id", asCommitted.Id)));
        return ItemQueries.AfterCommit(store, asCommitted, cleared.Failure);
    }

    /// <summary>
    /// Before a change to a row whose Save as new item left its steps done but not cleared, the row being listed: the
    /// steps cleared, or the change refused by <paramref name="name"/> while they cannot be. Null once cleared, or when
    /// a step is not done: no query lists that row, and only the start removes it.
    /// </summary>
    public static Failure? FinishPending(StoreContext store, StoredFiles row, string name, string action)
    {
        if (!row.Steps.All(step => step.IsDone(store.Folder)))
        {
            return null;
        }

        var cleared = store.Change(action, () => store.Execute("UPDATE items SET pending_steps = NULL WHERE id = $id", ("$id", row.Id)));
        return cleared.Succeeded
            ? null
            : new Failure(action, ItemQueries.Named(name, row.FinishedFile), $"its earlier save as new item cannot finish yet: {cleared.Failure!.Reason}");
    }

    /// <summary>
    /// At start, a Save as new item whose files are all in place has its steps cleared; any other is removed with its
    /// tags, each file it placed and each it left in <c>.work\</c> deleted first. A file that cannot be deleted leaves
    /// the row as it is, unlisted, for the next start, and is named.
    /// </summary>
    public static Failure? FinishAtStart(StoreContext store, StoredFiles row)
    {
        var folder = store.Folder;
        if (row.Steps.All(step => step.IsDone(folder)))
        {
            return store.Change(StoreOpen.Action, () => store.Execute("UPDATE items SET pending_steps = NULL WHERE id = $id", ("$id", row.Id))).Failure;
        }

        // The names are this row's, taken for every other row (ItemFileNames), so a file under one is its own.
        foreach (var path in row.Steps.SelectMany(step => new[] { step.ToPath(folder), step.FromPath(folder) }))
        {
            var deleted = store.RetryHeldFile(UndoAtStart, path, () => File.Delete(path));
            if (!deleted.Succeeded)
            {
                var name = store.Read(StoreOpen.Action, () => store.Query("SELECT name FROM items WHERE id = $id", r => r.GetString(0), ("$id", row.Id)).Single());
                return deleted.Failure with { Subject = ItemQueries.Named(name.Succeeded ? name.Value! : "", row.FinishedFile) };
            }
        }

        return store.Change(StoreOpen.Action, () => BatchRemoval.RemoveRows(store, [row.Id])).Failure;
    }

    /// <summary>
    /// Inside the insert's transaction: the new row with its counter name, both file names from one stem, the source's
    /// tags (so a kill leaves the item with its tags or not at all, G28) and the steps; answered as committed, with no
    /// stamp of the store's commits, since its files are not in place yet.
    /// </summary>
    private static (StashItem AsCommitted, IReadOnlyList<PendingStep> Steps) Insert(
        StoreContext store, SaveRow source, LayerList layers, RenderedPicture? render, string copy, DateTimeOffset addedAt)
    {
        var name = CounterName(store, source.Name);
        var originalExtension = Path.GetExtension(source.Files.OriginalFile);
        string finishedFile;
        string originalFile;
        IReadOnlyList<PendingStep> steps;
        if (render is null)
        {
            // An unedited copy is one file, both original and finished picture, placed as an import's is (F7).
            finishedFile = originalFile = ItemFileNames.Choose(store, name, originalExtension, ItemFileNames.TakenInRoot(store));
            steps = [PendingStep.Place(copy, finishedFile)];
        }
        else
        {
            // The render is in the source's finished format, over the same bytes, so the copy's is that one too.
            var extension = FinishedFormat.Extension(FinishedFormat.Of(source.OriginalFacts));
            var stem = ItemFileNames.ChooseStem(store, null, name,
                [(extension, ItemFileNames.TakenInRoot(store)), (originalExtension, ItemFileNames.TakenInOriginals(store))]);
            finishedFile = stem + extension;
            originalFile = stem + originalExtension;
            // The copy first: a kill between the two leaves at most a copy in originals\ of a row the start removes.
            steps = [new PendingStep(PendingStep.PlaceInOriginals, copy, originalFile), PendingStep.Place(Path.GetFileName(render.WorkPath), finishedFile)];
        }

        var finished = render?.Facts;
        var id = (long)store.Scalar(
            """
            INSERT INTO items (name, added_at, format, width, height, animated, frame_count, duration_ms,
                               original_file, original_in_originals, finished_file, original_sha256, layers,
                               finished_format, finished_width, finished_height, finished_animated, finished_frame_count, finished_duration_ms,
                               pending_steps)
            SELECT $name, $added_at, format, width, height, animated, frame_count, duration_ms,
                   $original, $in_originals, $finished, original_sha256, $layers,
                   $finished_format, $finished_width, $finished_height, $finished_animated, $finished_frame_count, $finished_duration_ms,
                   $steps
            FROM items WHERE id = $source
            RETURNING id
            """,
            ("$source", source.Files.Id),
            ("$name", name),
            ("$added_at", addedAt.ToUnixTimeMilliseconds()),
            ("$original", originalFile),
            ("$in_originals", render is not null),
            ("$finished", finishedFile),
            ("$layers", layers.ToJson()),
            ("$finished_format", finished?.Format.ToString()),
            ("$finished_width", finished?.Width),
            ("$finished_height", finished?.Height),
            ("$finished_animated", finished?.Animated),
            ("$finished_frame_count", finished?.FrameCount),
            ("$finished_duration_ms", finished?.DurationMs),
            ("$steps", PendingStep.Encode(steps)))!;
        var tags = ItemTags.AddToNewItem(store, id, store.Query("SELECT tag FROM tags WHERE item_id = $id ORDER BY rowid", r => r.GetString(0), ("$id", source.Files.Id)));
        var item = new StashItem(id, name, tags, false, addedAt, finishedFile, store.Folder.InRoot(finishedFile), finished ?? source.OriginalFacts, 0, Edited: !layers.IsEmpty);
        return (item, steps);
    }

    /// <summary>
    /// G26's counter ("the audacity 2"): the item's name, a space and the first number from 2 that no row's name holds,
    /// compared ignoring case, rows a Delete marked included, since its Undo can bring the name back; an unnamed item's
    /// is the number alone. Its file names then follow the one stem rule, which may add a counter of their own.
    /// </summary>
    private static string CounterName(StoreContext store, string name)
    {
        var taken = new HashSet<string>(store.Query("SELECT name FROM items", r => r.GetString(0)), StringComparer.OrdinalIgnoreCase);
        for (var counter = 2; ; counter++)
        {
            var candidate = name.Length > 0 ? $"{name} {counter.ToString(CultureInfo.InvariantCulture)}" : counter.ToString(CultureInfo.InvariantCulture);
            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }
    }
}
