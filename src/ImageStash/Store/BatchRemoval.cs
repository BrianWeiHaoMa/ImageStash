using ImageStash.Files;
using ImageStash.Seams;

namespace ImageStash.Store;

/// <summary>
/// Removing a batch of items (an import's Undo): the rows are marked deleted, which no query returns and which keep
/// their file names reserved; the files go to the Recycle Bin (G29); then the rows are removed, in that order. A kill
/// in between is finished at the next start (State: the Delete bullet). No held copy is made, because nothing undoes
/// this removal. A row a Delete marked has its files still in place recycled with the others, and is left marked with
/// its held copies to that Delete, whose notice's Undo can still bring it back whole while it shows (G18), and whose
/// finish, or the next start, removes it.
/// </summary>
internal static class BatchRemoval
{
    public const string Action = "Remove from the stash";

    /// <summary>
    /// One outcome per identity, in order; an item whose file cannot be recycled fails alone and stays marked. A row
    /// still waiting on an import's step is removed the same way, so it cannot come back when the next start finishes
    /// it; one whose pending rename, save or revert cannot finish first fails alone, unmarked. A row a Delete marked
    /// keeps its mark and its held copies once its files have gone.
    /// </summary>
    public static IReadOnlyList<Outcome> Remove(StoreContext store, IReadOnlyList<long> ids)
    {
        var unfinished = UnfinishedChanges(store, ids);
        if (!unfinished.Succeeded)
        {
            return ids.Select(_ => (Outcome)unfinished.Failure).ToList();
        }

        var marked = store.Change(Action, () =>
        {
            var rows = new List<(StoredFiles Files, bool HeldByDelete)>();
            foreach (var id in ids.Distinct().Where(id => !unfinished.Value!.ContainsKey(id)))
            {
                rows.AddRange(store.Query(
                    $"SELECT {StoredFiles.Columns}, i.held_copies IS NOT NULL FROM items i WHERE i.id = $id",
                    r => (StoredFiles.FromRow(r, 0), r.GetBoolean(StoredFiles.ColumnCount)),
                    ("$id", id)));
                store.Execute("UPDATE items SET deleted = 1 WHERE id = $id", ("$id", id));
            }

            return rows;
        });
        if (!marked.Succeeded)
        {
            return ids.Select(_ => (Outcome)marked.Failure).ToList();
        }

        var outcomes = RecycleInPlace(store, [.. marked.Value!.Select(row => row.Files)]);
        foreach (var row in marked.Value!.Where(row => row.HeldByDelete))
        {
            // Recycled on the Delete's behalf: its notice's Undo removes these entries as it does its own.
            var made = Identities(outcomes.GetValueOrDefault(row.Files.Id) ?? []);
            if (made.Count > 0)
            {
                store.EntriesForDeleteUndo[row.Files.Id] = [.. store.EntriesForDeleteUndo.GetValueOrDefault(row.Files.Id) ?? [], .. made];
            }
        }

        var recycled = FirstRefusals(outcomes);
        // A Delete's row stays for its notice's Undo, which brings it back from its copies (G18); its finish or the
        // next start removes it, so it is still removed once.
        var removable = marked.Value!.Where(row => !row.HeldByDelete && !recycled.ContainsKey(row.Files.Id)).Select(row => row.Files.Id).ToList();
        var removed = removable.Zip(EndMarkedRows(store, removable, Action)).ToDictionary(r => r.First, r => r.Second);
        return ids.Select(id => unfinished.Value!.TryGetValue(id, out var refusedChange) ? (Outcome)refusedChange
            : recycled.TryGetValue(id, out var refusedFile) ? refusedFile : removed.GetValueOrDefault(id, Outcome.Done)).ToList();
    }

    /// <summary>The rows among <paramref name="ids"/> whose pending rename, save or revert cannot finish first, each with its refusal.</summary>
    private static Outcome<Dictionary<long, Failure>> UnfinishedChanges(StoreContext store, IReadOnlyList<long> ids)
    {
        var rows = store.Read(Action, () => store.Query(
            $"SELECT {StoredFiles.Columns}, i.name FROM items i WHERE i.pending_steps IS NOT NULL AND i.deleted = 0",
            r => (Files: StoredFiles.FromRow(r, 0), Name: r.GetString(StoredFiles.ColumnCount))));
        if (!rows.Succeeded)
        {
            return rows.Failure;
        }

        var batch = ids.ToHashSet();
        var refused = new Dictionary<long, Failure>();
        foreach (var (files, name) in rows.Value!.Where(row => batch.Contains(row.Files.Id)))
        {
            if (PendingStepRecovery.FinishBeforeChange(store, files, name, Action) is { } failure)
            {
                refused[files.Id] = failure;
            }
        }

        return Outcome<Dictionary<long, Failure>>.Of(refused);
    }

    /// <summary>
    /// At start, a marked row's files still present are recycled and the row removed. When the Recycle Bin still
    /// refuses one while every file of the item is still in place, the removal is undone instead: the mark and every
    /// step the row carries are cleared, so the item is back with its files and nothing later recycles it. Once one of
    /// its files is gone, the item cannot be put back whole, so the row stays marked, returned by no query, for the next
    /// start to finish.
    /// </summary>
    public static Failure? FinishAtStart(StoreContext store, StoredFiles row)
    {
        var refused = FirstRefusals(RecycleInPlace(store, [row]));
        if (refused.TryGetValue(row.Id, out var failure))
        {
            if (!row.Paths(store.Folder).All(File.Exists))
            {
                return failure;
            }

            var restored = store.Change(StoreOpen.Action, () =>
                store.Execute("UPDATE items SET deleted = 0, pending_steps = NULL WHERE id = $id", ("$id", row.Id)));
            return restored.Succeeded ? failure : restored.Failure;
        }

        return EndMarkedRows(store, [row.Id], StoreOpen.Action)[0].Failure;
    }

    /// <summary>
    /// Ends marked rows, whoever ends them (the import's Undo, a delete's finish, the start), in State's order: every
    /// held copy a row records is deleted first, then a live Open with… adoption's record expires, its previous original
    /// to the Recycle Bin (G29), then its Open with… session ends with its copy (State: Open with…), then the rows are
    /// removed with their tags and uses in one transaction, so a row is removed once and its copies go with it. A copy
    /// already gone is nothing; a row one of whose copies cannot be deleted, or whose record cannot expire, stops there,
    /// named, and stays marked with what is left for the next start to end (State: A live failure). A row that is gone
    /// or not marked is left as it is. One outcome per identity, in order.
    /// </summary>
    public static IReadOnlyList<Outcome> EndMarkedRows(StoreContext store, IReadOnlyList<long> ids, string action)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var marked = store.Read(action, () => ids.Distinct().SelectMany(id => store.Query(
            "SELECT id, finished_file, held_copies, open_with_session, adoption_undo FROM items WHERE id = $id AND deleted = 1",
            r => (Id: r.GetInt64(0), FileName: r.GetString(1), Copies: r.IsDBNull(2) ? [] : HeldCopy.Decode(r.GetString(2)), Session: r.IsDBNull(3) ? null : r.GetString(3),
                Undo: r.IsDBNull(4) ? null : r.GetString(4)),
            ("$id", id))).ToList());
        if (!marked.Succeeded)
        {
            return ids.Select(_ => (Outcome)marked.Failure).ToList();
        }

        var undeleted = new Dictionary<long, Failure>();
        foreach (var row in marked.Value!)
        {
            foreach (var copy in row.Copies)
            {
                var path = store.Folder.InWork(copy.Copy);
                var deleted = store.RetryHeldFile("Discard a held copy", path, () => File.Delete(path));
                if (!deleted.Succeeded)
                {
                    // The row's other copies stay too, so an Undo that can still come finds them all.
                    undeleted.Add(row.Id, deleted.Failure with { Subject = row.FileName });
                    break;
                }
            }

            if (!undeleted.ContainsKey(row.Id) && row.Undo is not null && AdoptionUndo.ExpireRecord(store, row.Id, row.Undo, row.FileName, action).Failure is { } unexpired)
            {
                undeleted.Add(row.Id, unexpired);
            }

            if (!undeleted.ContainsKey(row.Id) && OpenWithCopies.End(store, row.Session, row.FileName).Failure is { } kept)
            {
                undeleted.Add(row.Id, kept);
            }
        }

        var ended = marked.Value!.Select(row => row.Id).Where(id => !undeleted.ContainsKey(id)).ToList();
        var removed = ended.Count == 0 ? Outcome.Done : store.Change(action, () => RemoveRows(store, ended));
        return ids.Select(id => undeleted.TryGetValue(id, out var failure) ? failure : !removed.Succeeded && ended.Contains(id) ? removed : Outcome.Done).ToList();
    }

    /// <summary>Removes the rows with their tags and uses; their identities are never handed out again.</summary>
    public static void RemoveRows(StoreContext store, IReadOnlyList<long> ids)
    {
        foreach (var id in ids)
        {
            store.Execute("DELETE FROM tags WHERE item_id = $id", ("$id", id));
            store.Execute("DELETE FROM uses WHERE item_id = $id", ("$id", id));
            store.Execute("DELETE FROM items WHERE id = $id", ("$id", id));
        }
    }

    /// <summary>
    /// Recycles <paramref name="files"/> through the seam, a held one asked again while the others keep their answer
    /// (<see cref="StoreContext.Recycle"/>): for each row, the outcomes of its files in the order given.
    /// </summary>
    public static Dictionary<long, List<Outcome<RecycledFile>>> RecycleFiles(StoreContext store, IReadOnlyList<(long Id, string Path)> files)
    {
        var byRow = new Dictionary<long, List<Outcome<RecycledFile>>>();
        if (files.Count == 0)
        {
            return byRow;
        }

        var outcomes = store.Recycle(files.Select(f => f.Path).ToList());
        foreach (var (file, outcome) in files.Zip(outcomes))
        {
            if (!byRow.TryGetValue(file.Id, out var list))
            {
                byRow[file.Id] = list = [];
            }

            list.Add(outcome);
        }

        return byRow;
    }

    /// <summary>
    /// The identities the Recycle Bin gave back among <paramref name="outcomes"/>, in order: a refused file has none, nor
    /// has one Windows removed for good, which leaves nothing in the bin to remove (G29).
    /// </summary>
    public static List<string> Identities(IEnumerable<Outcome<RecycledFile>> outcomes) =>
        [.. outcomes.Where(o => o.Succeeded).Select(o => o.Value!.RecycledIdentity).OfType<string>()];

    /// <summary>Recycles the files of <paramref name="rows"/> still on disk: for each row, the outcomes of those files.</summary>
    private static Dictionary<long, List<Outcome<RecycledFile>>> RecycleInPlace(StoreContext store, IReadOnlyList<StoredFiles> rows) =>
        RecycleFiles(store, [.. rows.SelectMany(row => row.Paths(store.Folder).Where(File.Exists).Select(path => (row.Id, path)))]);

    /// <summary>The first refusal of each row, by identity.</summary>
    private static Dictionary<long, Failure> FirstRefusals(Dictionary<long, List<Outcome<RecycledFile>>> recycled)
    {
        var refused = new Dictionary<long, Failure>();
        foreach (var (id, outcomes) in recycled)
        {
            var first = outcomes.FirstOrDefault(o => !o.Succeeded);
            if (first is not null)
            {
                refused[id] = first.Failure!;
            }
        }

        return refused;
    }
}
