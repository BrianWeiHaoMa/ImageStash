using System.Collections.Immutable;

namespace ImageStash.Store;

internal readonly record struct ItemFactTarget(long Id, string Name);

/// <summary>The facts one item's row gained or lost in one committed act.</summary>
internal sealed record ItemFactDelta(
    ItemFactTarget Item,
    ImmutableArray<string> AddedTags,
    ImmutableArray<string> RemovedTags,
    bool? PreviousFavorite);

/// <summary>
/// One outcome per requested item, in input order, and one delta per item whose facts actually changed.
/// IDs belong to the open store that issued the result; a replacement stash can reuse them.
/// </summary>
internal sealed class ItemFactResult(
    StoreContext issuer, string undoAction, ImmutableArray<Outcome> outcomes, ImmutableArray<ItemFactDelta> changes)
{
    public ImmutableArray<Outcome> Outcomes { get; } = outcomes;
    public ImmutableArray<ItemFactDelta> Changes { get; } = changes;
    internal string UndoAction { get; } = undoAction;

    internal bool IssuedBy(StoreContext store) => ReferenceEquals(issuer, store);

    internal ItemFactResult RefuseUndo(string reason) => new(issuer, UndoAction,
        [.. Changes.Select(change => Outcome.Fail(Failure.Refusal(UndoAction, change.Item.Name, reason)))], []);
}

/// <summary>
/// A change to the facts of items that touches no file (State: "Tags, favorite, use: one transaction each"): the whole
/// batch is one transaction, inside which every identity is judged, so a kill leaves all of it or none (G28). An item
/// the stash does not list (gone, marked deleted, or waiting on a step the next start must finish) is refused by name
/// and the others are changed (G35). A pending step stays as it is: nothing on disk changes, so there is none to finish.
/// </summary>
internal static class ItemFactChange
{
    public const string NotInStash = "it is no longer in the stash";

    /// <summary>
    /// Runs <paramref name="change"/> on the listed items among <paramref name="items"/>, each once. A repeated
    /// identity is answered as its first. A refused store fails every item and returns no deltas.
    /// </summary>
    public static ItemFactResult Apply(
        StoreContext store, string action, string undoAction, IReadOnlyList<StashItem> items,
        Func<IReadOnlyList<ItemFactTarget>, ImmutableArray<ItemFactDelta>> change, Func<string?>? refusedBecause = null) =>
        Apply(store, action, undoAction, items.Select(item => new ItemFactTarget(item.Id, item.NameOrFileName)).ToArray(), change, refusedBecause);

    public static ItemFactResult Apply(
        StoreContext store, string action, string undoAction, IReadOnlyList<ItemFactTarget> items,
        Func<IReadOnlyList<ItemFactTarget>, ImmutableArray<ItemFactDelta>> change, Func<string?>? refusedBecause = null)
    {
        var distinct = items.DistinctBy(item => item.Id).ToList();
        var committed = store.Change(action, () =>
        {
            if (refusedBecause?.Invoke() is { } reason)
            {
                var refused = distinct.ToDictionary(item => item.Id, item => Failure.Refusal(action, item.Name, reason));
                return (Failures: refused, Changes: ImmutableArray<ItemFactDelta>.Empty);
            }

            var (listed, failures) = Listed(store, action, distinct);
            return (Failures: failures, Changes: change(listed));
        });

        if (!committed.Succeeded)
        {
            return new ItemFactResult(store, undoAction, [.. items.Select(_ => Outcome.Fail(committed.Failure))], []);
        }

        var (notListed, changes) = committed.Value;
        return new ItemFactResult(store, undoAction,
            [.. items.Select(item => notListed.TryGetValue(item.Id, out var failure) ? Outcome.Fail(failure) : Outcome.Done)],
            changes);
    }

    private static (List<ItemFactTarget> Items, Dictionary<long, Failure> Failures) Listed(
        StoreContext store, string action, IReadOnlyList<ItemFactTarget> targets)
    {
        var listed = new List<ItemFactTarget>();
        var notListed = new Dictionary<long, Failure>();
        foreach (var item in targets)
        {
            var rows = store.Query(
                "SELECT name, finished_file, deleted, pending_steps FROM items WHERE id = $id",
                r => (Name: r.GetString(0), FinishedFile: r.GetString(1), Deleted: r.GetBoolean(2), PendingSteps: r.IsDBNull(3) ? null : r.GetString(3)),
                ("$id", item.Id));
            if (rows.Count == 0)
            {
                // A row that is gone holds no name any more, so the item is named as the caller was shown it (G35).
                notListed[item.Id] = Failure.Refusal(action, item.Name, NotInStash);
                continue;
            }

            var row = rows[0];
            var name = ItemQueries.Named(row.Name, row.FinishedFile);
            if (row.Deleted || !StashItem.IsListed(store.Folder, row.PendingSteps))
            {
                notListed[item.Id] = Failure.Refusal(action, name, NotInStash);
            }
            else
            {
                listed.Add(new ItemFactTarget(item.Id, name));
            }
        }

        return (listed, notListed);
    }
}
