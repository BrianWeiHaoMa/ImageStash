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
            var byId = current.ToDictionary(item => item.Id);
            var held = recent.Where(byId.ContainsKey).Select(id => byId[id]);
            // A refresh can discover several arrivals. Append the older ones first.
            current = HeldOrder.Kept(held, current.Reverse(), item => item.Id);
        }

        recent = current.Select(item => item.Id).ToArray();
        RecentReorderPending = false;
        RecentReadPending = false;
        return current;
    }
}
