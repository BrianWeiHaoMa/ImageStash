using ImageStash.Files;

namespace ImageStash.Store;

/// <summary>
/// Rename (G17, P1, State: the Rename bullet, File names). One transaction commits the new name, the files' new names
/// and a rename step for each file whose name changes, each keeping the name before; then the files are renamed, never
/// copied or rewritten (G29, R2); then one transaction clears the steps, in that order, so a kill leaves the next start
/// a row to finish, or to undo to its previous name (G28). While a step waits, the row is listed with its files where
/// they are (State: A live failure). Rename has no Undo.
/// </summary>
internal static class ItemRename
{
    public const string Action = "Rename";

    private const string FinishAtStartAction = "Finish an unfinished rename";
    private const string UndoAtStartAction = "Undo an unfinished rename";

    /// <summary>
    /// Names the item <paramref name="name"/>, any text, none included (G17), and returns it as recorded, its files
    /// named for it with " 2" while taken. An item the stash does not list is refused by name; a rename, save or revert
    /// of it still pending is finished first, or the new one is refused. Then the same name again changes nothing more.
    /// A file refused after the commit fails after it, carrying the item with its file where it is, for the next start.
    /// </summary>
    public static Outcome<StashItem> Rename(StoreContext store, StashItem item, string name)
    {
        var listed = ListedRow(store, item);
        if (!listed.Succeeded)
        {
            return listed.Failure;
        }

        var row = listed.Value!;
        if (row.Files.Steps.Count > 0)
        {
            // Committing over a change still pending would leave steps whose names no longer match the row, and answering
            // the name it committed would show success before its file carries it (R9).
            if (PendingStepRecovery.FinishBeforeChange(store, row.Files, row.Name, Action) is { } unfinished)
            {
                return unfinished;
            }

            // Finishing it may have moved the files, so the names are chosen from the row as it is now.
            listed = ListedRow(store, item);
            if (!listed.Succeeded)
            {
                return listed.Failure;
            }

            row = listed.Value!;
        }

        if (name == row.Name)
        {
            return Recorded(store, item);
        }

        var committed = store.Change(Action, () => Commit(store, row, name));
        if (!committed.Succeeded)
        {
            return committed.Failure;
        }

        var (steps, finishedFile) = committed.Value;
        // It keeps the stamp it was handed, older than this commit: its file may not carry the new name yet, so a handoff
        // made while the store cannot be read refuses it rather than trust its path.
        var asCommitted = item with { Name = name, FileName = finishedFile, FilePath = store.Folder.InRoot(finishedFile) };
        // The finished picture first: it is the file a destination and File Explorer see (P1).
        foreach (var step in steps)
        {
            var renamed = Move(store, step.FromPath(store.Folder), step.ToPath(store.Folder), Action);
            if (!renamed.Succeeded)
            {
                return ItemQueries.AfterCommit(store, asCommitted, renamed.Failure);
            }
        }

        var cleared = steps.Count == 0 ? Outcome.Done : store.Change(Action, () => store.Execute("UPDATE items SET pending_steps = NULL WHERE id = $id", ("$id", item.Id)));
        return ItemQueries.AfterCommit(store, asCommitted, cleared.Failure);
    }

    /// <summary>The item's row when the stash lists it; otherwise refused by name as no longer in the stash.</summary>
    private static Outcome<Row> ListedRow(StoreContext store, StashItem item)
    {
        var read = store.Read(Action, () => store.Query(
            $"SELECT {StoredFiles.Columns}, i.name, i.deleted FROM items i WHERE i.id = $id",
            r => new Row(StoredFiles.FromRow(r, 0), r.GetString(StoredFiles.ColumnCount), r.GetBoolean(StoredFiles.ColumnCount + 1)),
            ("$id", item.Id)).SingleOrDefault());
        if (!read.Succeeded)
        {
            return read.Failure;
        }

        if (read.Value is not { } row)
        {
            // A row that is gone holds no name any more, so the item is named as the caller was shown it (G35).
            return new Failure(Action, Subject(item.Name, item.FileName), ItemFactChange.NotInStash);
        }

        return row.Deleted || !StashItem.IsListed(store.Folder, row.Files.Steps)
            ? new Failure(Action, Subject(row.Name, row.Files.FinishedFile), ItemFactChange.NotInStash)
            : Outcome<Row>.Of(row);
    }

    /// <summary>
    /// A change to a row's files over a rename still pending (another Rename, Delete, an import's Undo) first finishes
    /// it: each file not yet renamed is renamed and the steps cleared; while a file is still refused, the change is
    /// refused by <paramref name="name"/> and nothing changes, as every change over a pending step finishes it or is
    /// refused. So a mark never keeps a rename step, which an earlier build would misread. Null when nothing is pending.
    /// </summary>
    public static Failure? FinishPending(StoreContext store, StoredFiles row, string name, string action)
    {
        if (!row.Steps.Any(step => step.IsRename))
        {
            return null;
        }

        foreach (var step in row.Steps.Where(step => step.IsRename && !step.IsDone(store.Folder)))
        {
            var finished = Move(store, step.FromPath(store.Folder), step.ToPath(store.Folder), action);
            if (!finished.Succeeded)
            {
                return new Failure(action, Subject(name, row.FinishedFile), $"its earlier rename cannot finish yet: {finished.Failure.Reason}");
            }
        }

        return store.Change(action, () => store.Execute("UPDATE items SET pending_steps = NULL WHERE id = $id", ("$id", row.Id))).Failure;
    }

    /// <summary>
    /// At start, a row whose rename a kill or a held file left pending (State: Every change): every step not done is
    /// done, and the steps cleared. While one is still refused after the retries, the rename is undone instead: each
    /// file already renamed goes back, then the row takes its previous name and file names again; a file that cannot go
    /// back leaves the row as it is, listed with its files where they are, for the next start. Every failure is named.
    /// </summary>
    public static IReadOnlyList<Failure> FinishAtStart(StoreContext store, StoredFiles row)
    {
        var named = new List<Failure>();
        foreach (var step in row.Steps.Where(step => !step.IsDone(store.Folder)))
        {
            var finished = Move(store, step.FromPath(store.Folder), step.ToPath(store.Folder), FinishAtStartAction);
            if (!finished.Succeeded)
            {
                named.Add(finished.Failure);
            }
        }

        if (named.Count == 0)
        {
            var cleared = store.Change(StoreOpen.Action, () => store.Execute("UPDATE items SET pending_steps = NULL WHERE id = $id", ("$id", row.Id)));
            return cleared.Succeeded ? [] : [cleared.Failure];
        }

        var back = true;
        foreach (var step in row.Steps.Reverse().Where(step => step.IsDone(store.Folder)))
        {
            var undone = Move(store, step.ToPath(store.Folder), step.FromPath(store.Folder), UndoAtStartAction);
            if (!undone.Succeeded)
            {
                named.Add(undone.Failure);
                back = false;
            }
        }

        if (back)
        {
            var finishedFile = row.Steps.FirstOrDefault(step => step.Step == PendingStep.RenameInRoot)?.From ?? row.FinishedFile;
            var originalFile = row.OriginalInOriginals
                ? row.Steps.FirstOrDefault(step => step.Step == PendingStep.RenameInOriginals)?.From ?? row.OriginalFile
                : finishedFile;
            var rolledBack = store.Change(StoreOpen.Action, () => store.Execute(
                "UPDATE items SET name = COALESCE($name, name), finished_file = $finished, original_file = $original, pending_steps = NULL WHERE id = $id",
                ("$id", row.Id),
                ("$name", row.Steps.Select(step => step.PreviousName).FirstOrDefault(previous => previous is not null)),
                ("$finished", finishedFile),
                ("$original", originalFile)));
            if (!rolledBack.Succeeded)
            {
                named.Add(rolledBack.Failure);
            }
        }

        return named;
    }

    /// <summary>
    /// Inside the commit's transaction: the files' new names, one stem for both of an edited item's files, avoiding
    /// every name another row or file holds but none of the row's own, so a change of case renames in place; then the
    /// row's name, file names and steps, a done step left by an earlier change cleared with them.
    /// </summary>
    private static (IReadOnlyList<PendingStep> Steps, string FinishedFile) Commit(StoreContext store, Row row, string name)
    {
        var files = row.Files;
        var extension = Path.GetExtension(files.FinishedFile);
        var originalExtension = Path.GetExtension(files.OriginalFile);
        List<(string, IReadOnlySet<string>)> places = [(extension, ItemFileNames.TakenInRoot(store, files))];
        if (files.OriginalInOriginals)
        {
            places.Add((originalExtension, ItemFileNames.TakenInOriginals(store, files)));
        }

        var stem = ItemFileNames.ChooseStem(store, files, name, places);
        var finishedFile = stem + extension;
        var originalFile = files.OriginalInOriginals ? stem + originalExtension : finishedFile;
        var steps = new List<PendingStep>();
        if (finishedFile != files.FinishedFile)
        {
            steps.Add(new PendingStep(PendingStep.RenameInRoot, files.FinishedFile, finishedFile, row.Name));
        }

        if (files.OriginalInOriginals && originalFile != files.OriginalFile)
        {
            steps.Add(new PendingStep(PendingStep.RenameInOriginals, files.OriginalFile, originalFile, row.Name));
        }

        store.Execute(
            "UPDATE items SET name = $name, finished_file = $finished, original_file = $original, pending_steps = $steps WHERE id = $id",
            ("$id", files.Id),
            ("$name", name),
            ("$finished", finishedFile),
            ("$original", originalFile),
            ("$steps", steps.Count == 0 ? null : PendingStep.Encode(steps)));
        return (steps, finishedFile);
    }

    private static Outcome Move(StoreContext store, string from, string to, string action) =>
        store.RetryHeldFile(action, from, () => File.Move(from, to));

    /// <summary>The item as the store now lists it, or as the caller was shown it when the store cannot be read.</summary>
    private static Outcome<StashItem> Recorded(StoreContext store, StashItem item)
    {
        var read = ItemQueries.Item(store, item.Id);
        if (!read.Succeeded)
        {
            return read.Failure;
        }

        return read.Value is { } recorded ? Outcome<StashItem>.Of(recorded) : new Failure(Action, Subject(item.Name, item.FileName), ItemFactChange.NotInStash);
    }

    private static string Subject(string name, string fileName) => name.Length > 0 ? name : fileName;

    private sealed record Row(StoredFiles Files, string Name, bool Deleted);
}
