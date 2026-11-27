using ImageStash.Store;

namespace ImageStash.Window;

/// <summary>The words of the search a tag rename replaced, each as typed before by its place, and the tag they became.</summary>
internal sealed record RenamedSearchWords(string Renamed, IReadOnlyDictionary<int, string> Replaced, string To);

/// <summary>
/// A tag renamed to a different word follows into what holds its name (G17, G4, G5): the search's words, so its chip
/// stays lit and every item that carried it is still listed; the chips' held places, where the renamed chip keeps its
/// place under its new name, or after a merge the source's place is left as a tag that lost its last item is and the
/// destination counts on its own place, joining the end when it had none; and the open lists of every tag, whose row
/// keeps its place. The change is the window's, not typed by the person: the chips keep their places and the selected
/// items still listed stay selected, a selected item no longer listed giving way as a refresh gives way (G5).
/// </summary>
public sealed partial class Listing
{
    /// <summary>A tag renamed to a new word, from and to, for the open lists of every tag to rename their row in place.</summary>
    internal event Action<string, string>? TagRenamedInLists;

    /// <summary>
    /// The listing after the store renamed <paramref name="from"/> to <paramref name="to"/>, merged into an existing
    /// tag when <paramref name="merged"/>: what the search had replaced, for the rename's Undo, or null when its words
    /// held no such tag.
    /// </summary>
    internal async Task<RenamedSearchWords?> TagRenamed(string from, string to, bool merged)
    {
        RenamedSearchWords? search;
        lock (gate)
        {
            var (query, replaced) = ItemSearch.WithRenamedTag(Query, from, to);
            search = replaced.Count > 0 ? new RenamedSearchWords(query, replaced, to) : null;
            Query = query;
            order.Tags.Renamed(from, to, merged);
        }

        await Relist(search is not null, merged ? null : (from, to));
        return search;
    }

    /// <summary>
    /// The listing after a rename's Undo gave each carrier back its tags: the words <paramref name="search"/> replaced
    /// back where they still stand, and a renamed place or row back under its old name; after a merge both places stand.
    /// </summary>
    internal Task TagRenameUndone(string from, string to, bool merged, RenamedSearchWords? search)
    {
        var queryChanged = false;
        lock (gate)
        {
            if (search is not null)
            {
                var query = ItemSearch.WithoutRenamedTag(Query, search.Renamed, search.Replaced, search.To);
                queryChanged = query != Query;
                Query = query;
            }

            if (!merged)
            {
                order.Tags.Renamed(to, from, merged: false);
            }
        }

        return Relist(queryChanged, merged ? null : (to, from));
    }

    /// <summary>What a rename changed shown: the search box told of a query the window changed, the open lists of a renamed row, then a refresh.</summary>
    private Task Relist(bool queryChanged, (string From, string To)? renamedRow)
    {
        if (queryChanged)
        {
            QueryChangedByWindow?.Invoke();
        }

        if (renamedRow is var (from, to))
        {
            TagRenamedInLists?.Invoke(from, to);
        }

        return Refresh();
    }
}
