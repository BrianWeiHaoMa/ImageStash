using ImageStash.Store;

namespace ImageStash.Window;

/// <summary>The order of Recent and the tag row for this stash.</summary>
internal sealed class ListingOrder
{
    private IReadOnlyList<long> recent = [];
    public TagChipOrder Tags { get; } = new();

    public bool RecentReorderPending { get; private set; } = true;

    public bool RecentReadPending { get; private set; }

    public void RequestRecentOrder()
    {
        RecentReorderPending = true;
        RecentChanged();
    }

    public void RecentChanged() => RecentReadPending = true;

    public IReadOnlyList<StashItem> Recent(IReadOnlyList<StashItem> current)
    {
        if (!RecentReorderPending)
        {
            var remaining = current.ToDictionary(item => item.Id);
            var ordered = new List<StashItem>(current.Count);
            foreach (var id in recent)
            {
                if (remaining.Remove(id, out var item))
                {
                    ordered.Add(item);
                }
            }

            // A refresh can discover several arrivals. Append the older ones first.
            ordered.AddRange(current.Reverse().Where(item => remaining.ContainsKey(item.Id)));
            current = ordered;
        }

        recent = current.Select(item => item.Id).ToArray();
        RecentReorderPending = false;
        RecentReadPending = false;
        return current;
    }
}
