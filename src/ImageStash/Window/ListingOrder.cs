using ImageStash.Store;

namespace ImageStash.Window;

/// <summary>The order of Recent and of the unfiltered tag row between summons and scope choices.</summary>
internal sealed class ListingOrder
{
    private IReadOnlyList<long> recent = [];
    private bool takeTagOrder = true;

    public IReadOnlyList<TagCount> TagPlaces { get; private set; } = [];

    public bool RecentReorderPending { get; private set; } = true;

    public bool RecentReadPending { get; private set; }

    public void RequestRecentOrder()
    {
        RecentReorderPending = true;
        RecentChanged();
    }

    public void RecentChanged() => RecentReadPending = true;

    public void RequestTagOrder() => takeTagOrder = true;

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

    public void CountedTags(IReadOnlyList<TagCount> current)
    {
        TagPlaces = takeTagOrder ? current : KeepTagPlaces(current);
        takeTagOrder = false;
    }

    private IReadOnlyList<TagCount> KeepTagPlaces(IReadOnlyList<TagCount> current)
    {
        var remaining = current.ToDictionary(count => count.Tag, StringComparer.OrdinalIgnoreCase);
        var ordered = new List<TagCount>(Math.Max(TagPlaces.Count, current.Count));
        foreach (var place in TagPlaces)
        {
            // Keep even the old spelling until the next reorder so reviving a tag cannot change the slot's width.
            var carriers = remaining.Remove(place.Tag, out var count) ? count.Items : 0;
            ordered.Add(place with { Items = carriers });
        }

        ordered.AddRange(current.Where(count => remaining.ContainsKey(count.Tag)));
        return ordered;
    }
}
