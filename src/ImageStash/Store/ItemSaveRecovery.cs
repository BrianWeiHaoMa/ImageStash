using ImageStash.Files;
using ImageStash.Layers;

namespace ImageStash.Store;

/// <summary>
/// A Save or a Revert to original that a kill or a refused step left pending (State: Save, Every change, A live
/// failure). A later change of the item finishes it first: a Save by rendering the committed list again over the
/// original where it is, recording that fresh render on its place step while the step is not done, moving the original
/// into <c>originals\</c> if it is still in the root, placing the fresh render and running the last transaction; a
/// Revert by running its steps not yet done and its last transaction. While that cannot complete the change is refused
/// and the row kept as it is. The start finishes it the same way and, while it cannot, undoes it where its files can go
/// back; every failure is named.
/// </summary>
internal static class ItemSaveRecovery
{
    private const string FinishSaveAtStart = "Finish an unfinished save";
    private const string UndoSaveAtStart = "Undo an unfinished save";
    private const string FinishRevertAtStart = "Finish an unfinished revert";
    private const string UndoRevertAtStart = "Undo an unfinished revert";

    /// <summary>
    /// Finishes the row's pending Save or Revert before another change of it, or refuses that change by
    /// <paramref name="name"/> as <paramref name="action"/> with why it cannot finish yet; never undoes it. Null once
    /// finished, or when the row is gone, which the caller's read again refuses.
    /// </summary>
    public static Failure? FinishPending(StoreContext store, StoredFiles files, string name, string action)
    {
        var read = SaveRow.Read(store, action, files.Id);
        if (!read.Succeeded)
        {
            return read.Failure;
        }

        if (read.Value is not { } row)
        {
            return null;
        }

        var save = IsSave(row);
        var failure = save ? FinishSave(store, row, action).Failure : FinishRevert(store, row, action);
        return failure is null
            ? null
            : new Failure(action, ItemQueries.Named(name, files.FinishedFile), $"its earlier {(save ? "save" : "revert to the original")} cannot finish yet: {failure.Reason}");
    }

    /// <summary>
    /// At start, before anything reads the stash: the row's Save, rendered again, or its Revert, finished; while that
    /// cannot complete, undone where its files can go back, else left as it is, listed on the picture on disk, for the
    /// next start (State: Every change). Every failure is named.
    /// </summary>
    public static IReadOnlyList<Failure> FinishAtStart(StoreContext store, StoredFiles files)
    {
        var read = SaveRow.Read(store, StoreOpen.Action, files.Id);
        if (!read.Succeeded)
        {
            return [read.Failure];
        }

        if (read.Value is not { } row)
        {
            return [];
        }

        if (IsSave(row))
        {
            var (failure, steps) = FinishSave(store, row, FinishSaveAtStart);
            return failure is null ? [] : [failure with { Action = FinishSaveAtStart, Subject = row.Subject }, .. UndoSave(store, row, steps)];
        }

        var refused = FinishRevert(store, row, FinishRevertAtStart);
        return refused is null ? [] : [refused with { Action = FinishRevertAtStart, Subject = row.Subject }, .. UndoRevert(store, row)];
    }

    private static bool IsSave(SaveRow row) => row.Files.Steps.Any(step => step.Step == PendingStep.PlaceRender);

    /// <summary>
    /// Renders the committed list again (State: Save: "rendered again and finished") and finishes the Save; null when
    /// it is finished, else the failure, with the steps as the row now holds them.
    /// </summary>
    private static (Failure? Failure, IReadOnlyList<PendingStep> Steps) FinishSave(StoreContext store, SaveRow row, string action)
    {
        var folder = store.Folder;
        var steps = row.Files.Steps;
        var place = steps.First(step => step.Step == PendingStep.PlaceRender);
        var layers = LayerList.FromJson(row.Layers, row.Subject);
        if (!layers.Succeeded)
        {
            return (layers.Failure, steps);
        }

        // The original's hash is not judged: on the store's own thread nothing moves the row's files while it renders (E2).
        var (rendered, fresh) = store.RenderAgain(row.Files.OriginalPath(folder), row.Subject, layers.Value!, FinishedFormat.Of(row.OriginalFacts));
        if (!rendered.Succeeded)
        {
            DiscardQuietly(fresh);
            return (rendered.Failure, steps);
        }

        var facts = rendered.Value!;

        // A render that is already the picture on disk is left as its step records it: the root holds a render of the
        // committed list, which its facts describe, and re-recording it would make that render read as unplaced. The fresh
        // render, at a path no step names, then replaces it with the same facts. A place step that reads done while an
        // earlier start's undo has put the original back no longer describes a render in the root, so it is recorded again.
        var named = false;
        if (StoredFiles.ShowsChangedPicture(folder, steps) is not true)
        {
            // Recorded before it lands, so the step's done rule and facts describe the render that does.
            var recorded = place with { From = Path.GetFileName(fresh), Facts = facts };
            IReadOnlyList<PendingStep> now = [.. steps.Select(step => ReferenceEquals(step, place) ? recorded : step)];
            var rerecorded = store.Change(action, () => store.Execute(
                "UPDATE items SET pending_steps = $steps WHERE id = $id", ("$id", row.Files.Id), ("$steps", PendingStep.Encode(now))));
            if (!rerecorded.Succeeded)
            {
                DiscardQuietly(fresh);
                return (rerecorded.Failure, steps);
            }

            DiscardQuietly(place.FromPath(folder));
            (steps, place, named) = (now, recorded, true);
        }

        if (steps.FirstOrDefault(step => step.Step == PendingStep.MoveToOriginals) is { } move && !move.IsDone(folder))
        {
            var moved = ItemSave.RunStep(store, move, steps, action, row.Subject);
            if (!moved.Succeeded)
            {
                return (Unplaced(moved.Failure), steps);
            }
        }

        var placed = ItemSave.Move(store, action, fresh, folder.InRoot(place.To), replacing: true);
        if (!placed.Succeeded)
        {
            return (Unplaced(placed.Failure with { Subject = row.Subject }), steps);
        }

        var completed = store.Change(action, () => ItemSave.CompleteSave(store, row.Files.Id, facts));
        return (completed.Failure, steps);

        // A fresh render no step names is the item's no longer; one a step names stays for the next attempt.
        Failure Unplaced(Failure failure)
        {
            if (!named)
            {
                DiscardQuietly(fresh);
            }

            return failure;
        }
    }

    /// <summary>Runs the Revert's steps not yet done, in order, then its last transaction; null when finished.</summary>
    private static Failure? FinishRevert(StoreContext store, SaveRow row, string action)
    {
        var steps = row.Files.Steps;
        foreach (var step in steps.Where(step => !step.IsDone(store.Folder)))
        {
            var done = ItemSave.RunStep(store, step, steps, action, row.Subject);
            if (!done.Succeeded)
            {
                return done.Failure;
            }
        }

        return store.Change(action, () => ItemSave.CompleteRevert(store, row.Files.Id)).Failure;
    }

    /// <summary>
    /// At start, a Save that cannot finish is undone where its files can go back: a first Save's original is moved
    /// back into the root first, over a render of its own name in one replacing rename, then a render of another name in
    /// the root is deleted, never recycled, then the row rolled back, so a refusal or a kill between them leaves the
    /// original where the steps say it is; a second Save with nothing placed is rolled back. A second Save whose render
    /// has replaced the earlier one has nothing to put back: it stays listed as saved on that render, and the next start
    /// finishes it (State: Every change). A file that cannot go back leaves the row as it is.
    /// </summary>
    private static IReadOnlyList<Failure> UndoSave(StoreContext store, SaveRow row, IReadOnlyList<PendingStep> steps)
    {
        var folder = store.Folder;
        var place = steps.First(step => step.Step == PendingStep.PlaceRender);
        var placed = place.IsDone(folder);
        if (steps.FirstOrDefault(step => step.Step == PendingStep.MoveToOriginals) is not { } move)
        {
            return placed ? [] : RollBackSave(store, row, previousFile: null);
        }

        // The render's name in the root is this Save's, taken for every other row (ItemFileNames), so a file under it is
        // this Save's render, placed now or left there by an earlier start's undo; it is not kept (G29).
        var shared = string.Equals(place.To, move.From, StringComparison.OrdinalIgnoreCase);
        var render = folder.InRoot(place.To);
        if (move.IsDone(folder))
        {
            var back = ItemSave.Move(store, UndoSaveAtStart, move.ToPath(folder), move.FromPath(folder), replacing: shared && File.Exists(render));
            if (!back.Succeeded)
            {
                return [back.Failure with { Subject = row.Subject }];
            }
        }

        if (!shared && File.Exists(render))
        {
            var deleted = store.RetryHeldFile(UndoSaveAtStart, render, () => File.Delete(render));
            if (!deleted.Succeeded)
            {
                return [deleted.Failure with { Subject = row.Subject }];
            }
        }

        return RollBackSave(store, row, move.From);
    }

    /// <summary>
    /// The row back to the facts it kept for the Save: the list the picture on disk came from, the revision it was
    /// rendered for, and, after a first Save, its one name in the root again.
    /// </summary>
    private static IReadOnlyList<Failure> RollBackSave(StoreContext store, SaveRow row, string? previousFile)
    {
        var rolledBack = store.Change(StoreOpen.Action, () => store.Execute(
            """
            UPDATE items SET layers = previous_layers, layer_revision = rendered_revision, previous_layers = NULL, pending_steps = NULL,
                finished_file = COALESCE($file, finished_file), original_file = COALESCE($file, original_file),
                original_in_originals = CASE WHEN $file IS NULL THEN original_in_originals ELSE 0 END
            WHERE id = $id
            """,
            ("$id", row.Files.Id),
            ("$file", previousFile)));
        return rolledBack.Succeeded ? [] : [rolledBack.Failure];
    }

    /// <summary>
    /// At start, a Revert that cannot finish is undone: its original, if it is back in the root while the render is
    /// still there, moved into <c>originals\</c> again, then the row returned to the list, revision, names and flag it
    /// had. An original that has replaced a render of its own name, or whose render is deleted, has nothing to go back
    /// to: the row stays listed on it and the next start runs its last transaction. A file that cannot go back leaves the
    /// row as it is.
    /// </summary>
    private static IReadOnlyList<Failure> UndoRevert(StoreContext store, SaveRow row)
    {
        var folder = store.Folder;
        var move = row.Files.Steps.First(step => step.Step == PendingStep.MoveToRoot);
        var delete = row.Files.Steps.FirstOrDefault(step => step.Step == PendingStep.DeleteInRoot);
        if (move.IsDone(folder))
        {
            if (delete is null || delete.IsDone(folder))
            {
                return [];
            }

            var back = ItemSave.Move(store, UndoRevertAtStart, move.ToPath(folder), move.FromPath(folder), replacing: false);
            if (!back.Succeeded)
            {
                return [back.Failure with { Subject = row.Subject }];
            }
        }

        var rolledBack = store.Change(StoreOpen.Action, () => store.Execute(
            """
            UPDATE items SET layers = previous_layers, layer_revision = rendered_revision, previous_layers = NULL, pending_steps = NULL,
                finished_file = $finished, original_file = $original, original_in_originals = 1
            WHERE id = $id
            """,
            ("$id", row.Files.Id),
            ("$finished", delete?.From ?? move.To),
            ("$original", move.From)));
        return rolledBack.Succeeded ? [] : [rolledBack.Failure];
    }

    // A superseded or unplaced render nothing of the item depends on: one held open stays for the next start's clearing
    // of .work\, unnamed, and is not waited for.
    private static void DiscardQuietly(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }
}
