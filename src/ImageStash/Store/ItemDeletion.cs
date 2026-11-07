using ImageStash.Files;
using ImageStash.Seams;

namespace ImageStash.Store;

/// <summary>
/// An item a Delete took out of the stash: its name as the row held it, so a later failure can name an item whose row
/// is gone, and the identities the Recycle Bin gave the files of the row that were recycled, in
/// <see cref="StoredFiles.Files"/> order; a file Windows removed for good has none (G29).
/// </summary>
public sealed record DeletedItem(long Id, string Name, IReadOnlyList<string> RecycledIdentities);

/// <summary>
/// An item a Delete's Undo put back in the stash, with every failure that came after its files were back (a Recycle
/// Bin entry left behind, a copy left in <c>.work\</c>), each for the caller to name (G35).
/// </summary>
public sealed record RestoredItem(long Id, IReadOnlyList<Failure> Named);

/// <summary>
/// Delete with Undo (G18, State: the Delete bullet). Delete copies each item's files into <c>.work\</c> as held copies,
/// marks the rows deleted with those copies recorded, then recycles the files, in that order; Undo renames the copies
/// back into place, removes the Recycle Bin's entries, then clears the marks, in that order and no other, so a kill at
/// any point leaves each item fully present or fully in the Recycle Bin (G28); Finish deletes the copies, then
/// removes the rows. The mark keeps the tags, the uses and the file names until the row is removed. A marked row with
/// held copies is finished at the next start, or undone from its copies when a recycle is still refused.
/// </summary>
internal static class ItemDeletion
{
    public const string Action = "Delete";

    private const string UndoAction = "Undo the delete";
    private const string FinishAction = "Finish the delete";
    private const string FinishAtStartAction = "Finish an unfinished delete";
    private const string UndoAtStartAction = "Undo an unfinished delete";
    private const string NotInStash = "it is no longer in the stash";

    /// <summary>
    /// Deletes each listed item; one outcome per identity, in order, a repeated identity answered as the first. An item
    /// whose copy cannot be made, whose pending rename, save or revert cannot finish first, or that is no longer in the
    /// stash, fails alone and nothing of it changes; one whose file the Recycle Bin refuses fails after its mark,
    /// carrying what was recycled, its copies kept for Undo or the next start. No use is recorded (G10).
    /// </summary>
    public static IReadOnlyList<Outcome<DeletedItem>> Delete(StoreContext store, IReadOnlyList<long> ids)
    {
        var distinct = ids.Distinct().ToList();
        var read = ReadRows(store, Action, distinct);
        if (!read.Succeeded)
        {
            return ids.Select(_ => Outcome<DeletedItem>.Fail(read.Failure)).ToList();
        }

        var answers = new Dictionary<long, Outcome<DeletedItem>>();
        var held = new List<(Row Row, IReadOnlyList<HeldCopy> Copies)>();
        foreach (var (id, first) in distinct.Zip(read.Value!))
        {
            var row = first;
            if (row is { Deleted: false } && StashItem.IsListed(store.Folder, row.Files.Steps) && row.Files.Steps.Count > 0)
            {
                if (PendingStepRecovery.FinishBeforeChange(store, row.Files, row.Name, Action) is { } unfinished)
                {
                    answers[id] = unfinished;
                    continue;
                }

                // Finishing it may have moved the files and changed the row: the copies are made of the row as it is now.
                var again = ReadRows(store, Action, [id]);
                if (!again.Succeeded)
                {
                    answers[id] = again.Failure;
                    continue;
                }

                row = again.Value![0];
            }

            if (row is null || row.Deleted || !StashItem.IsListed(store.Folder, row.Files.Steps))
            {
                // The store holds no name for a row that is gone; the caller names it by the item it was given.
                answers[id] = new Failure(Action, row?.Name ?? id.ToString(), NotInStash);
                continue;
            }

            var copies = HoldCopies(store, row.Files);
            if (copies.Succeeded)
            {
                held.Add((row, copies.Value!));
            }
            else
            {
                answers[id] = copies.Failure;
            }
        }

        // One transaction marks the whole batch (E4); a listed row whose import step is done on disk drops the step. Any
        // other step was finished above, so a mark never drops a step a later start would need.
        var marked = store.Change(Action, () =>
        {
            var rows = new List<(Row Row, IReadOnlyList<HeldCopy> Copies)>();
            foreach (var (row, copies) in held)
            {
                var steps = store.Query("SELECT pending_steps FROM items WHERE id = $id AND deleted = 0", r => r.IsDBNull(0) ? null : r.GetString(0), ("$id", row.Files.Id));
                if (steps is [var only] && (only is null || PendingStep.Decode(only).All(s => s.Step == PendingStep.PlaceInRoot && s.IsDone(store.Folder))))
                {
                    store.Execute(
                        "UPDATE items SET deleted = 1, pending_steps = NULL, held_copies = $copies WHERE id = $id",
                        ("$id", row.Files.Id),
                        ("$copies", HeldCopy.Encode(copies)));
                    rows.Add((row, copies));
                }
            }

            return rows;
        });
        var markedIds = marked.Succeeded ? marked.Value!.Select(m => m.Row.Files.Id).ToHashSet() : [];
        foreach (var (row, copies) in held.Where(h => !markedIds.Contains(h.Row.Files.Id)))
        {
            Discard(store, copies);
            answers[row.Files.Id] = marked.Succeeded ? new Failure(Action, row.Name, NotInStash) : marked.Failure;
        }

        if (marked.Succeeded)
        {
            var files = marked.Value!.SelectMany(m => m.Row.Files.Paths(store.Folder).Select(path => (m.Row.Files.Id, path))).ToList();
            var recycled = BatchRemoval.RecycleFiles(store, files);
            foreach (var (row, copies) in marked.Value!)
            {
                var outcomes = recycled.GetValueOrDefault(row.Files.Id) ?? [];
                // A file Windows removed for good instead of recycling is done, as a recycled one is (G29), its held copy
                // still serving Undo; it leaves no entry for Undo to remove.
                var item = new DeletedItem(row.Files.Id, row.Name, BatchRemoval.Identities(outcomes));
                var refused = outcomes.FirstOrDefault(o => !o.Succeeded);
                if (refused is not null)
                {
                    // A refused file gone from its place all the same is back from its copy, so nothing later takes the
                    // copy for the last of it; the row stays marked as for any refused recycle.
                    PutBackUnrecycled(store, row.Files, copies, row.Files.Paths(store.Folder).Zip(outcomes));
                }

                answers[row.Files.Id] = refused is null ? Outcome<DeletedItem>.Of(item) : Outcome<DeletedItem>.FailAfterCommit(refused.Failure!, item);
            }
        }

        return ids.Select(id => answers[id]).ToList();
    }

    /// <summary>
    /// Brings each deleted item back whole, its name, tags, favorite flag and uses never having left its row; one
    /// outcome per item, in order. The files come back from the held copies, never from the Recycle Bin (G18), and only
    /// then are the bin's entries removed and the mark cleared. A refused bin removal or a copy that cannot be deleted
    /// is named in the item's <see cref="RestoredItem.Named"/> and the item is back anyway; a file that cannot be put
    /// back fails the item, whose row stays marked for the next start.
    /// </summary>
    public static IReadOnlyList<Outcome<RestoredItem>> Undo(StoreContext store, IReadOnlyList<DeletedItem> items)
    {
        var read = ReadRows(store, UndoAction, [.. items.Select(i => i.Id).Distinct()]);
        if (!read.Succeeded)
        {
            return items.Select(_ => Outcome<RestoredItem>.Fail(read.Failure)).ToList();
        }

        var answers = new Dictionary<long, Outcome<RestoredItem>>();
        var whole = new List<(DeletedItem Item, List<Failure> Named)>();
        foreach (var (item, row) in items.DistinctBy(i => i.Id).Zip(read.Value!))
        {
            if (row is not { Deleted: true, Copies: not null })
            {
                answers[item.Id] = new Failure(UndoAction, item.Name, row is { Deleted: false } ? "it is not deleted" : NotInStash);
                continue;
            }

            // An item that cannot come back whole is not touched, so a failed Undo changes nothing.
            if (Unrecoverable(store, row.Files, row.Copies) is { } lost)
            {
                answers[item.Id] = new Failure(UndoAction, Path.GetFileName(lost.Path), "its held copy is gone");
                continue;
            }

            var (failures, back) = PutBack(store, row.Files, row.Copies, UndoAction);
            if (back)
            {
                whole.Add((item, failures));
            }
            else
            {
                answers[item.Id] = failures[0];
            }
        }

        // The entries an import's Undo made of the item's files while it was deleted go with the Delete's own.
        var identities = whole.SelectMany(w => w.Item.RecycledIdentities.Concat(store.EntriesForDeleteUndo.GetValueOrDefault(w.Item.Id) ?? [])
            .Select(identity => (w.Item.Id, identity))).ToList();
        foreach (var (item, _) in whole)
        {
            store.EntriesForDeleteUndo.Remove(item.Id);
        }

        if (identities.Count > 0)
        {
            var removed = store.RemoveRecycled(identities.Select(i => i.identity).ToList());
            foreach (var (identity, outcome) in identities.Zip(removed))
            {
                if (!outcome.Succeeded)
                {
                    whole.Single(w => w.Item.Id == identity.Id).Named.Add(outcome.Failure);
                }
            }
        }

        var cleared = whole.Count == 0 ? Outcome.Done : store.Change(UndoAction, () =>
        {
            foreach (var (item, _) in whole)
            {
                store.Execute("UPDATE items SET deleted = 0, held_copies = NULL WHERE id = $id", ("$id", item.Id));
            }
        });
        foreach (var (item, named) in whole)
        {
            answers[item.Id] = cleared.Succeeded ? Outcome<RestoredItem>.Of(new RestoredItem(item.Id, named)) : cleared.Failure;
        }

        // A repeated item finds its row unmarked by the first, as a second Undo would.
        return items.Select((item, index) => items.Take(index).Any(earlier => earlier.Id == item.Id)
            ? new Failure(UndoAction, item.Name, "it is not deleted")
            : answers[item.Id]).ToList();
    }

    /// <summary>
    /// Ends each deleted item whose Undo can no longer come: its held copies deleted, then its row removed with its tags
    /// and uses; the Recycle Bin's entries stay. One outcome per identity, in order: done for a row that is gone or not
    /// marked; a failure for a row whose files are still in the stash (a refused recycle), which stays marked for the
    /// next start.
    /// </summary>
    public static IReadOnlyList<Outcome> Finish(StoreContext store, IReadOnlyList<long> ids)
    {
        var distinct = ids.Distinct().ToList();
        var read = ReadRows(store, FinishAction, distinct);
        if (!read.Succeeded)
        {
            return ids.Select(_ => (Outcome)read.Failure).ToList();
        }

        var answers = new Dictionary<long, Outcome>();
        var ending = new List<long>();
        foreach (var (id, row) in distinct.Zip(read.Value!))
        {
            // No Undo comes once the notice has left, so the entries kept for one are no longer needed.
            store.EntriesForDeleteUndo.Remove(id);
            if (row is not { Deleted: true })
            {
                answers[id] = Outcome.Done;
            }
            else if (row.Files.Paths(store.Folder).Any(File.Exists))
            {
                // Removing the row would leave a stash file no row names (F6, G29).
                answers[id] = new Failure(FinishAction, row.Name, "its files are still in the stash");
            }
            else
            {
                ending.Add(id);
            }
        }

        foreach (var (id, outcome) in ending.Zip(BatchRemoval.EndMarkedRows(store, ending, FinishAction)))
        {
            answers[id] = outcome;
        }

        return ids.Select(id => answers[id]).ToList();
    }

    /// <summary>
    /// At start, a marked row with held copies (State: the Delete bullet, Every change): every file of the row in place
    /// without its copy (an Undo interrupted after its renames) is copied again first, so that a refusal can always be
    /// undone from the copies; then every file still in place is recycled and, with nothing refused, the row is ended,
    /// leaving the item fully in the Recycle Bin. A refused copy or recycle undoes the delete instead: the files are put
    /// back from the copies and the mark cleared, or, while a file cannot be put back, the row stays marked, returned by
    /// no query, for the next start. Every failure is named.
    /// </summary>
    public static IReadOnlyList<Failure> FinishAtStart(StoreContext store, StoredFiles row, IReadOnlyList<HeldCopy> copies)
    {
        var named = new List<Failure>();
        foreach (var file in row.Files(store.Folder))
        {
            var copy = HeldCopy.Of(copies, file);
            if (copy is not null && File.Exists(file.Path) && !File.Exists(store.Folder.InWork(copy.Copy)))
            {
                var again = CopyAgain(store, file.Path, store.Folder.InWork(copy.Copy));
                if (!again.Succeeded)
                {
                    named.Add(again.Failure);
                    break;
                }
            }
        }

        IReadOnlyList<Outcome<RecycledFile>> recycled = [];
        if (named.Count == 0)
        {
            var present = row.Files(store.Folder).Where(f => File.Exists(f.Path)).Select(f => (row.Id, f.Path)).ToList();
            recycled = BatchRemoval.RecycleFiles(store, present).GetValueOrDefault(row.Id) ?? [];
            named.AddRange(recycled.Where(o => !o.Succeeded).Select(o => o.Failure!));
            if (named.Count == 0)
            {
                var ended = BatchRemoval.EndMarkedRows(store, [row.Id], StoreOpen.Action)[0];
                return ended.Succeeded ? [] : [ended.Failure];
            }
        }

        var (failures, back) = PutBack(store, row, copies, UndoAtStartAction);
        named.AddRange(failures);
        if (back)
        {
            // Rolled back whole, the files the start itself recycled leave no entry behind in the bin, as Undo's do not.
            var made = BatchRemoval.Identities(recycled);
            if (made.Count > 0)
            {
                named.AddRange(store.RemoveRecycled(made).Where(o => !o.Succeeded).Select(o => o.Failure!));
            }

            var cleared = store.Change(StoreOpen.Action, () =>
                store.Execute("UPDATE items SET deleted = 0, held_copies = NULL WHERE id = $id", ("$id", row.Id)));
            if (!cleared.Succeeded)
            {
                named.Add(cleared.Failure);
            }
        }

        return named;
    }

    /// <summary>
    /// Copies every file of the row into <c>.work\</c>; a copy that fails after the retry deletes the copies already
    /// made and names the file, so nothing of the row is held.
    /// </summary>
    private static Outcome<IReadOnlyList<HeldCopy>> HoldCopies(StoreContext store, StoredFiles row)
    {
        var copies = new List<HeldCopy>();
        foreach (var file in row.Files(store.Folder))
        {
            var copied = WorkFolder.CopyIn(store, file.Path, Action);
            if (!copied.Succeeded)
            {
                Discard(store, copies);
                return copied.Failure;
            }

            copies.Add(new HeldCopy(file.Column, Path.GetFileName(copied.Value!)));
        }

        return Outcome<IReadOnlyList<HeldCopy>>.Of(copies);
    }

    /// <summary>
    /// Makes the held copy of <paramref name="source"/> again under the name the row records for it: written at a fresh
    /// path first, then renamed, so a kill during the copy leaves only a file no row records, never a partial copy under
    /// a recorded name that a later start would put back as the item's file (G28).
    /// </summary>
    private static Outcome CopyAgain(StoreContext store, string source, string recorded)
    {
        var fresh = WorkFolder.CopyIn(store, source, FinishAtStartAction);
        if (!fresh.Succeeded)
        {
            return fresh.Failure;
        }

        var renamed = store.RetryHeldFile(FinishAtStartAction, fresh.Value!, () => File.Move(fresh.Value!, recorded));
        if (!renamed.Succeeded)
        {
            WorkFolder.Discard(store, fresh.Value!);
            return renamed.Failure with { Subject = Path.GetFileName(source) };
        }

        return Outcome.Done;
    }

    /// <summary>
    /// Puts the row's files back from its held copies (R2): a copy whose file is not in place is renamed back; one whose
    /// file is still in place, because its recycle was refused, is deleted, never renamed over the file, which Windows
    /// refuses while another program holds it. Every failure, named by the item's file, and whether every file of the
    /// row is now in place.
    /// </summary>
    private static (List<Failure> Failures, bool Back) PutBack(StoreContext store, StoredFiles row, IReadOnlyList<HeldCopy> copies, string action)
    {
        var failures = new List<Failure>();
        foreach (var file in row.Files(store.Folder))
        {
            var name = Path.GetFileName(file.Path);
            var copy = HeldCopy.Of(copies, file) is { } held ? store.Folder.InWork(held.Copy) : null;
            if (File.Exists(file.Path))
            {
                if (copy is not null && File.Exists(copy))
                {
                    var deleted = store.RetryHeldFile(action, copy, () => File.Delete(copy));
                    if (!deleted.Succeeded)
                    {
                        // The item is back regardless; a copy no row records goes with the next start's clearing.
                        failures.Add(deleted.Failure with { Subject = name });
                    }
                }
            }
            else if (copy is not null && File.Exists(copy))
            {
                var renamed = store.RetryHeldFile(action, copy, () => File.Move(copy, file.Path));
                if (!renamed.Succeeded)
                {
                    failures.Insert(0, renamed.Failure with { Subject = name });
                }
            }
            else
            {
                failures.Insert(0, new Failure(action, name, "its held copy is gone"));
            }
        }

        return (failures, row.Files(store.Folder).All(f => File.Exists(f.Path)));
    }

    /// <summary>
    /// After a live recycle the Recycle Bin refused, renames back from its copy every file whose recycle failed and that
    /// is no longer in place, so a refusal leaves the item whole for Undo or the next start (G28). A rename that cannot be
    /// made leaves the copy for Undo or the next start.
    /// </summary>
    private static void PutBackUnrecycled(StoreContext store, StoredFiles row, IReadOnlyList<HeldCopy> copies, IEnumerable<(string Path, Outcome<RecycledFile> Outcome)> recycled)
    {
        foreach (var (path, outcome) in recycled)
        {
            var copy = row.Files(store.Folder).FirstOrDefault(f => f.Path == path) is { } file && HeldCopy.Of(copies, file) is { } held ? store.Folder.InWork(held.Copy) : null;
            if (!outcome.Succeeded && copy is not null && !File.Exists(path) && File.Exists(copy))
            {
                store.RetryHeldFile(Action, copy, () => File.Move(copy, path));
            }
        }
    }

    /// <summary>The first file of the row neither in place nor held as a copy on disk, which nothing can bring back; or null.</summary>
    private static StoredFile? Unrecoverable(StoreContext store, StoredFiles row, IReadOnlyList<HeldCopy> copies) =>
        row.Files(store.Folder).FirstOrDefault(file =>
            !File.Exists(file.Path) && !(HeldCopy.Of(copies, file) is { } held && File.Exists(store.Folder.InWork(held.Copy))));

    private static void Discard(StoreContext store, IEnumerable<HeldCopy> copies)
    {
        // A copy that cannot be deleted is recorded by no row, so the next start's clearing of .work\ removes it.
        foreach (var copy in copies)
        {
            WorkFolder.Discard(store, store.Folder.InWork(copy.Copy));
        }
    }

    /// <summary>Each identity's row as one read sees them, or null for a row that is gone.</summary>
    private static Outcome<List<Row?>> ReadRows(StoreContext store, string action, IReadOnlyList<long> ids) =>
        store.Read(action, () => ids.Select(id => store.Query(
            $"SELECT {StoredFiles.Columns}, i.name, i.deleted, i.held_copies FROM items i WHERE i.id = $id",
            r => new Row(
                StoredFiles.FromRow(r, 0),
                r.GetString(StoredFiles.ColumnCount),
                r.GetBoolean(StoredFiles.ColumnCount + 1),
                r.IsDBNull(StoredFiles.ColumnCount + 2) ? null : HeldCopy.Decode(r.GetString(StoredFiles.ColumnCount + 2))),
            ("$id", id)).SingleOrDefault()).ToList());

    private sealed record Row(StoredFiles Files, string Name, bool Deleted, IReadOnlyList<HeldCopy>? Copies);
}
