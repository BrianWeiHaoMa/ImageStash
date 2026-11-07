namespace ImageStash.Store;

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
    /// Runs <paramref name="change"/> on the identities of the listed items among <paramref name="items"/>, each once;
    /// one outcome per item, in order, a repeated identity answered as its first. A refused store fails every item.
    /// </summary>
    public static IReadOnlyList<Outcome> Apply(StoreContext store, string action, IReadOnlyList<StashItem> items, Action<IReadOnlyList<long>> change)
    {
        var changed = store.Change(action, () =>
        {
            var listed = new List<long>();
            var notListed = new Dictionary<long, Failure>();
            foreach (var item in items.DistinctBy(item => item.Id))
            {
                var rows = store.Query(
                    "SELECT name, finished_file, deleted, pending_steps FROM items WHERE id = $id",
                    r => (Name: r.GetString(0), FinishedFile: r.GetString(1), Deleted: r.GetBoolean(2), PendingSteps: r.IsDBNull(3) ? null : r.GetString(3)),
                    ("$id", item.Id));
                if (rows.Count == 0)
                {
                    // A row that is gone holds no name any more, so the item is named as the caller was shown it (G35).
                    notListed[item.Id] = new Failure(action, item.Name.Length > 0 ? item.Name : item.FileName, NotInStash);
                }
                else if (rows[0].Deleted || !StashItem.IsListed(store.Folder, rows[0].PendingSteps))
                {
                    notListed[item.Id] = new Failure(action, rows[0].Name.Length > 0 ? rows[0].Name : rows[0].FinishedFile, NotInStash);
                }
                else
                {
                    listed.Add(item.Id);
                }
            }

            change(listed);
            return notListed;
        });

        return items.Select(item => !changed.Succeeded ? changed.Failure : changed.Value!.TryGetValue(item.Id, out var failure) ? Outcome.Fail(failure) : Outcome.Done).ToList();
    }
}
