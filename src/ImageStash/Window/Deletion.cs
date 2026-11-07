using ImageStash.Store;

namespace ImageStash.Window;

/// <summary>
/// Delete with Undo in the window (G18): the one path Del, the item menu's Delete and the preview take. The items leave
/// the grid at once, without a dialog, once Store has held copies of their files, marked them and sent the files to
/// the Recycle Bin; only then does the grid refresh and a notice say what was deleted, with an Undo that brings every
/// item of the batch back whole while the notice shows (R9, G16). The notice leaving without Undo, by its time or its
/// ×, finishes the delete. While Store works the busy state names the action and the window keeps answering (G35);
/// every failure is named in a notice of its own and the other items are still deleted. Nothing here touches a file:
/// the copies, the renames and the Recycle Bin are Store's (G29, R2). No use is recorded (G10).
/// </summary>
public sealed class Deletion(Listing listing, Notices notices)
{
    public const string Action = "Delete";

    private const string NotInStash = "it is no longer in the stash";

    private readonly SemaphoreSlim oneAtATime = new(1, 1);

    /// <summary>The items a Delete is working on now, so a second press of Del meanwhile does not ask for them again.</summary>
    private readonly HashSet<long> deleting = [];

    /// <summary>Raised whenever <see cref="Busy"/> changes.</summary>
    public event Action? Changed;

    /// <summary>The running Delete's or Undo's busy state in words ("Deleting 3 items…"), or null while none runs.</summary>
    public string? Busy { get; private set; }

    /// <summary>
    /// Del: the selection, once what was typed has arrived (G5, G7). A press while the selection is still the items a
    /// running Delete is deleting asks for nothing more, so it never deletes the item that Delete's refresh selects next.
    /// </summary>
    public async Task<Outcome> DeleteSelection()
    {
        var pressed = listing.SelectedItems;
        if (pressed.Count > 0 && pressed.All(item => deleting.Contains(item.Id)))
        {
            return Outcome.Done;
        }

        await listing.Settle();
        return await Delete(listing.SelectedItems);
    }

    /// <summary>
    /// Deletes <paramref name="items"/>, the selection as the caller took it; returns the first failure, every failure
    /// named through the notices, else done. Nothing to delete is done with nothing shown; while the stash is not open
    /// the items are refused by name and nothing is asked of Store.
    /// </summary>
    public async Task<Outcome> Delete(IReadOnlyList<StashItem> items)
    {
        var asked = items.DistinctBy(item => item.Id).Where(item => !deleting.Contains(item.Id)).ToList();
        if (asked.Count == 0)
        {
            return Outcome.Done;
        }

        if (listing.CannotStartReason is { } cannot)
        {
            return Named(new Failure(Action, Notices.Describe(asked), cannot));
        }

        var store = listing.Store!;

        deleting.UnionWith(asked.Select(item => item.Id));
        await oneAtATime.WaitAsync();
        try
        {
            SetBusy(asked.Count == 1 ? $"Deleting {Notices.Describe(asked)}…" : $"Deleting {asked.Count} items…");
            var outcomes = await store.Delete(asked.Select(item => item.Id).ToList());
            await listing.Refresh();

            // Every item Store marked comes back with Undo, a refused one too; only those whose files all went are
            // named as deleted (R9, G35).
            var batch = outcomes.Where(outcome => outcome.Value is not null).Select(outcome => outcome.Value!).ToList();
            var finished = asked.Where((_, i) => outcomes[i].Succeeded).ToList();
            var refused = asked.Where((_, i) => !outcomes[i].Succeeded && outcomes[i].Value is not null).ToList();
            if (batch.Count > 0)
            {
                notices.Raise(NoticeText(finished, refused), "Undo", () => Undo(store, batch), finished.Count > 0 ? () => Finish(store, finished) : null);
            }

            var failures = asked.Zip(outcomes).Where(pair => !pair.Second.Succeeded).Select(pair => Subject(pair.Second.Failure!, pair.First)).ToList();
            NameAll(failures);
            return failures.Count == 0 ? Outcome.Done : failures[0];
        }
        finally
        {
            deleting.ExceptWith(asked.Select(item => item.Id));
            SetBusy(null);
            oneAtATime.Release();
        }
    }

    /// <summary>
    /// What the notice says: the items whose files all went to the Recycle Bin, by name up to three, else by count; and
    /// those Store marked but could not delete, which Undo brings back too and whose own failure names why.
    /// </summary>
    private static string NoticeText(IReadOnlyList<StashItem> deleted, IReadOnlyList<StashItem> notDeleted)
    {
        var words = deleted.Count == 0 ? "Nothing was deleted." : $"Deleted {Notices.Describe(deleted)}.";
        if (notDeleted.Count > 0)
        {
            words += $" {Notices.Describe(notDeleted)} could not be deleted yet; Undo brings {(notDeleted.Count == 1 ? "it" : "them")} back.";
        }

        return words;
    }

    /// <summary>
    /// The notice's Undo: every item of the batch back from its held copies, the Recycle Bin's entries removed (G18).
    /// Every failure that came after an item was back is named in a notice of its own, as is every item that could not
    /// come back after the first, which is returned for the notices to name.
    /// </summary>
    private async Task<Outcome> Undo(StashStore store, IReadOnlyList<DeletedItem> batch)
    {
        await oneAtATime.WaitAsync();
        try
        {
            SetBusy(batch.Count == 1 ? $"Bringing back “{batch[0].Name}”…" : $"Bringing back {batch.Count} items…");
            var outcomes = await store.UndoDelete(batch);
            await listing.Refresh();
            NameAll(outcomes.Where(outcome => outcome.Succeeded).SelectMany(outcome => outcome.Value!.Named).ToList());
            var failed = outcomes.Where(outcome => !outcome.Succeeded).Select(outcome => outcome.Failure!).ToList();
            NameAll(failed.Skip(1).ToList());
            return failed.Count == 0 ? Outcome.Done : failed[0];
        }
        finally
        {
            SetBusy(null);
            oneAtATime.Release();
        }
    }

    /// <summary>
    /// The notice left without Undo: the deleted items end, their held copies deleted and their rows removed, the
    /// Recycle Bin keeping their files. An item whose files are still in the stash is not among them: it stays deleted
    /// for the next start to finish or undo.
    /// </summary>
    private async Task Finish(StashStore store, IReadOnlyList<StashItem> items)
    {
        var outcomes = await store.FinishDelete(items.Select(item => item.Id).ToList());
        NameAll(outcomes.Where(outcome => !outcome.Succeeded).Select(outcome => outcome.Failure!).ToList());
    }

    /// <summary>Each distinct failure in a notice of its own, so one cause the whole batch shares is named once (G35).</summary>
    private void NameAll(IReadOnlyList<Failure> failures)
    {
        foreach (var failure in failures.DistinctBy(failure => failure.Sentence))
        {
            notices.Raise(failure);
        }
    }

    private Outcome Named(Failure failure)
    {
        notices.Raise(failure);
        return failure;
    }

    /// <summary>A row that is gone has no name in Store, which names it by its identity; the window knows the item's name.</summary>
    private static Failure Subject(Failure failure, StashItem item) =>
        failure.Reason == NotInStash && failure.Subject == item.Id.ToString() ? failure with { Subject = item.Name.Length > 0 ? item.Name : item.FileName } : failure;

    private void SetBusy(string? busy)
    {
        Busy = busy;
        Changed?.Invoke();
    }
}
