using ImageStash.Store;

namespace ImageStash.Window;

/// <summary>The unfiltered tag order and the places held through a series of chip choices.</summary>
internal sealed class TagChipOrder
{
    private static readonly StringComparer SameTag = StringComparer.OrdinalIgnoreCase;
    private IReadOnlyList<TagCount> unfiltered = [];
    private IReadOnlyList<TagCount>? held;
    private IReadOnlyList<TagCount>? typed;
    private bool takeUnfilteredOrder = true;
    private Dictionary<string, TagCount> live = new(SameTag);
    private Dictionary<string, int> carried = new(SameTag);
    private string? matchedQuery;
    private int matchCount;

    public IReadOnlyList<TagCount> Live { get; private set; } = [];
    public int Revision { get; private set; }
    public Task PendingChoice { get; set; } = Task.CompletedTask;

    public void Edited()
    {
        held = null;
        typed = null;
        ClearPendingChoices();
    }

    public void RequestOrder()
    {
        takeUnfilteredOrder = true;
        Edited();
    }

    public void Chosen(IReadOnlyList<TagCount> shown) => held = shown;

    public void ForgetRead()
    {
        Live = [];
        live.Clear();
        matchedQuery = null;
        carried.Clear();
        matchCount = 0;
        ClearPendingChoices();
    }

    public TagCount? LiveTag(string tag) => live.GetValueOrDefault(tag);

    public IReadOnlyList<string> Lit(string query) => ItemSearch.LitTags(query, live.Keys);

    public bool? FilterAvailability(string tag, string query, bool searching, bool lit)
    {
        if (!live.ContainsKey(tag))
        {
            return false;
        }

        if (!searching || lit || held is null)
        {
            return true;
        }

        return matchedQuery == query ? carried.ContainsKey(tag) : null;
    }

    public void Counted(IReadOnlyList<TagCount> counts)
    {
        Live = counts;
        live = counts.ToDictionary(count => count.Tag, SameTag);
        unfiltered = takeUnfilteredOrder ? counts : KeepPlaces(unfiltered, appendNew: true);
        takeUnfilteredOrder = false;
    }

    public void Matched(string query, IReadOnlyList<StashItem> matches)
    {
        matchedQuery = query;
        matchCount = matches.Count;
        carried.Clear();
        foreach (var tag in matches.SelectMany(item => item.Tags))
        {
            carried[tag] = carried.GetValueOrDefault(tag) + 1;
        }
    }

    public IReadOnlyList<TagCount> Shown(string query, bool searching, bool rank = true)
    {
        if (held is not null)
        {
            return KeepPlaces(held, appendNew: false);
        }

        if (!searching)
        {
            return unfiltered;
        }

        if (typed is not null)
        {
            return KeepPlaces(typed, appendNew: false);
        }

        var ranked = Ranked(query);
        if (rank)
        {
            typed = ranked;
        }

        return ranked;
    }

    private void ClearPendingChoices()
    {
        Revision++;
        PendingChoice = Task.CompletedTask;
    }

    private IReadOnlyList<TagCount> KeepPlaces(IReadOnlyList<TagCount> places, bool appendNew)
    {
        // Keep the old spelling as well as the position, so a revival cannot change the slot's width.
        var kept = places.Select(place => place with { Items = LiveTag(place.Tag)?.Items ?? 0 }).ToList();
        if (appendNew)
        {
            var names = places.Select(place => place.Tag).ToHashSet(SameTag);
            kept.AddRange(Live.Where(count => !names.Contains(count.Tag)));
        }

        return kept;
    }

    private IReadOnlyList<TagCount> Ranked(string query)
    {
        var lit = Lit(query);
        var used = lit.ToHashSet(SameTag);
        var fragment = ItemSearch.UnfinishedLastWord(query, live.Keys);
        var completing = fragment is null ? [] : Live.Where(count => ItemSearch.Contains(count.Tag, fragment) && used.Add(count.Tag)).ToList();
        var narrowing = matchedQuery == query
            ? Live.Where(count => !used.Contains(count.Tag) && carried.GetValueOrDefault(count.Tag) is > 0 and var n && n < matchCount)
                .Select(count => count with { Items = carried[count.Tag] })
                .OrderByDescending(count => count.Items)
            : Enumerable.Empty<TagCount>();
        return [.. lit.Select(tag => live[tag]), .. completing, .. narrowing];
    }
}
