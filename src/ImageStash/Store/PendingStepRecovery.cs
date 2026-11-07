namespace ImageStash.Store;

/// <summary>
/// At every start, before anything reads the stash, each change a kill or a live failure left unfinished is finished
/// or undone from what is on disk (State: Every change, G28), by the change that owns it. A step still refused after
/// the retries is undone when the undo can complete, and otherwise stays on its row for the next start, returned by no
/// query unless it is a rename's, a Save's, a Revert's, an adoption's or its Undo's, whose row lists the picture on disk
/// while there is one; either way it is named and every other item opens (G35). A change made while the store runs
/// finishes a pending one first, or is refused.
/// </summary>
internal static class PendingStepRecovery
{
    public static Outcome<IReadOnlyList<Failure>> Resolve(StoreContext store)
    {
        var rows = store.Read(StoreOpen.Action, () => store.Query(
            $"SELECT {StoredFiles.Columns}, i.deleted, i.held_copies FROM items i WHERE i.pending_steps IS NOT NULL OR i.deleted = 1 ORDER BY i.id",
            r => new UnfinishedRow(
                StoredFiles.FromRow(r, 0),
                r.GetBoolean(StoredFiles.ColumnCount),
                r.IsDBNull(StoredFiles.ColumnCount + 1) ? null : HeldCopy.Decode(r.GetString(StoredFiles.ColumnCount + 1)))));
        if (!rows.Succeeded)
        {
            return rows.Failure;
        }

        var named = new List<Failure>();
        foreach (var row in rows.Value!)
        {
            named.AddRange(row switch
            {
                { Deleted: true, HeldCopies: not null } => ItemDeletion.FinishAtStart(store, row.Files, row.HeldCopies),
                { Deleted: true } => Named(BatchRemoval.FinishAtStart(store, row.Files)),
                // Before every other test: an adoption places its file as an import does, and is never finished as one;
                // an adoption's Undo places an edited picture's render as a Save does, and is never finished as one.
                _ when AdoptionSteps.IsAdoption(row.Files.Steps) => ItemAdoption.FinishAtStart(store, row.Files),
                _ when AdoptionSteps.IsUndo(row.Files.Steps) => ItemAdoptionUndo.FinishAtStart(store, row.Files),
                // Before the import's test: an edited item's second Save carries one step, which is not an import's.
                _ when IsSave(row.Files.Steps) || IsRevert(row.Files.Steps) => ItemSaveRecovery.FinishAtStart(store, row.Files),
                // Before the import's test too, since a Save as new item places its render as an import places its file;
                // one without layers carries the import's step alone and is finished as an import.
                _ when IsSaveAsNewItem(row.Files.Steps) => Named(ItemSaveAsNewItem.FinishAtStart(store, row.Files)),
                _ when row.Files.Steps.All(s => s.Step == PendingStep.PlaceInRoot) => Named(ImportCommit.FinishAtStart(store, row.Files.Id, row.Files.Steps)),
                _ when row.Files.Steps.All(s => s.IsRename) => ItemRename.FinishAtStart(store, row.Files),
                // A step this build does not write, or steps of two kinds, which it never writes on one row: the row
                // stays as it is, returned by no query while an unknown step is on it.
                _ => [],
            });
        }

        return Outcome<IReadOnlyList<Failure>>.Of(named);
    }

    /// <summary>
    /// Before a change to a row's files or names (Rename, Delete, an import's Undo, Save, Save as new item, an Open
    /// with… adoption, its Undo or its expiry): the change still pending on it is finished by the change that owns its
    /// steps, or the new change is refused by <paramref name="name"/> while it cannot finish; it is never undone, which
    /// only a start does. Null when nothing was pending or it finished; the caller then reads the row again, which the finish
    /// may have changed.
    /// </summary>
    public static Failure? FinishBeforeChange(StoreContext store, StoredFiles row, string name, string action) =>
        AdoptionSteps.IsAdoption(row.Steps) ? ItemAdoption.FinishPending(store, row, name, action)
        : AdoptionSteps.IsUndo(row.Steps) ? ItemAdoptionUndo.FinishPending(store, row, name, action)
        : IsSave(row.Steps) || IsRevert(row.Steps) ? ItemSaveRecovery.FinishPending(store, row, name, action)
        : IsSaveAsNewItem(row.Steps) ? ItemSaveAsNewItem.FinishPending(store, row, name, action)
        : ItemRename.FinishPending(store, row, name, action);

    /// <summary>
    /// Before Export writes (State: Export stash…): a row no query lists, whose import or Save as new item a live failure
    /// stopped before its file reached the root and which no later change can reach, is finished or removed as the next
    /// start would, so the archive carries no stopped change. Null when done, or when the row is of another kind.
    /// </summary>
    public static Failure? UndoUnlisted(StoreContext store, StoredFiles row) =>
        // An adoption places its file as an import does, and is never taken for one (as Resolve orders them).
        AdoptionSteps.IsAdoption(row.Steps) || AdoptionSteps.IsUndo(row.Steps) ? null
        : IsSaveAsNewItem(row.Steps) ? ItemSaveAsNewItem.FinishAtStart(store, row)
        : row.Steps.Count > 0 && row.Steps.All(s => s.Step == PendingStep.PlaceInRoot) ? ImportCommit.FinishAtStart(store, row.Id, row.Steps)
        : null;

    private static bool IsSave(IReadOnlyList<PendingStep> steps) => steps.All(s => s.IsSave) && steps.Any(s => s.Step == PendingStep.PlaceRender);

    private static bool IsRevert(IReadOnlyList<PendingStep> steps) => steps.All(s => s.IsRevert) && steps.Any(s => s.Step == PendingStep.MoveToRoot);

    private static bool IsSaveAsNewItem(IReadOnlyList<PendingStep> steps) =>
        steps.All(s => s.Step is PendingStep.PlaceInOriginals or PendingStep.PlaceInRoot) && steps.Any(s => s.Step == PendingStep.PlaceInOriginals);

    private static IReadOnlyList<Failure> Named(Failure? failure) => failure is null ? [] : [failure];

    private sealed record UnfinishedRow(StoredFiles Files, bool Deleted, IReadOnlyList<HeldCopy>? HeldCopies);
}
