using ImageStash.Store;

namespace ImageStash.Window;

/// <summary>
/// The rows of one open list a person picks a tag from (G17, G4): Add tag ▸, Remove tag ▸, and the chip row's and the
/// tag field's lists of every tag. Its rows are the tags it opened with, in that order (<see cref="HeldOrder"/>), until
/// it closes: each shows the number of items in the stash that carry its tag now, a tag made since joins the end, and a
/// tag that lost its last item keeps its row, reading 0. A tag whose last item a pick in this list took it off is given
/// back by the next pick on it there; a tag another act emptied is not, and picking it is refused as before. A row is
/// known by its tag, so a pick acts on the tag the row shows.
/// </summary>
public sealed class OpenTagList
{
    private static readonly StringComparer SameTag = StringComparer.OrdinalIgnoreCase;
    private readonly HashSet<string> emptiedHere = new(SameTag);
    private List<TagCount>? rows;
    private Dictionary<string, TagCount> live = new(SameTag);

    /// <summary>
    /// The rows for <paramref name="counts"/>, the stash's tags as the store now counts them, the commonest first: at the
    /// first call since closing, those <paramref name="offers"/> accepts (every one when null), in that order; later, the
    /// rows held with their numbers now, then each offered tag not yet a row.
    /// </summary>
    public IReadOnlyList<TagCount> Rows(IReadOnlyList<TagCount> counts, Func<TagCount, bool>? offers = null)
    {
        live = counts.ToDictionary(count => count.Tag, SameTag);
        emptiedHere.RemoveWhere(live.ContainsKey);
        var offered = offers is null ? counts : counts.Where(offers);
        rows = rows is null
            ? offered.ToList()
            : HeldOrder.Kept(rows.Select(row => live.GetValueOrDefault(row.Tag) ?? row with { Items = 0 }), offered, row => row.Tag, SameTag);
        return rows;
    }

    /// <summary>
    /// A tag renamed to a new word, not merged (G17): its row keeps its place under the new name, and a tag this list
    /// emptied stays its to give back under that name; a row already under the new name gives way, so the tag has one row.
    /// </summary>
    internal void Renamed(string from, string to)
    {
        rows = rows is null ? null : HeldOrder.Renamed(rows, row => row.Tag, from, to, (row, tag) => row with { Tag = tag }, SameTag);
        if (emptiedHere.Remove(from))
        {
            emptiedHere.Add(to);
        }
    }

    /// <summary>Whether a pick on <paramref name="tag"/> gives it back: a pick in this list took it off its last item, and nothing has given it since.</summary>
    internal bool GivesBack(string tag) => emptiedHere.Contains(tag);

    /// <summary>A pick in this list has just left no item carrying <paramref name="tag"/>; a tag the list never showed carried is not its to give back.</summary>
    internal void LostLastItem(string tag)
    {
        if (live.ContainsKey(tag))
        {
            emptiedHere.Add(tag);
        }
    }

    /// <summary>The list closed: its next opening takes its rows anew.</summary>
    public void Close()
    {
        rows = null;
        live.Clear();
        emptiedHere.Clear();
    }
}
