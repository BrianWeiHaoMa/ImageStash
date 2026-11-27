using ImageStash.Store;

namespace ImageStash.Window;

/// <summary>
/// The person's place in the list when their act takes the selected items out of it: a Delete, un-favoriting in
/// Favorites, the lit chip's tag taken off, a rename out of a search, an import's Undo. Each refreshes the list
/// (<see cref="Refresh"/>), and where none of the selected items is still listed, the item now standing where the first
/// of them stood is selected, the last item when that place is past the end, rather than the list's first item, so the
/// grid stays where it was and a second Del acts on the item the person is on. Explorer and the Photos app go on to the
/// next item the same way. A scope, a search and a summon select as G1, G4 and G5 say, and items still listed stay
/// selected.
/// </summary>
public sealed partial class Listing
{
    /// <summary>
    /// The item of <paramref name="items"/> standing where the first of <paramref name="selected"/> stood in the list
    /// before (<paramref name="previousPlaces"/>), the last item when that place is past the end; null when nothing was
    /// selected there or nothing is listed now.
    /// </summary>
    private static long? ItemAtPlaceLeft(IReadOnlyList<StashItem> items, IReadOnlyDictionary<long, int> previousPlaces, IReadOnlySet<long> selected)
    {
        var places = selected.Where(previousPlaces.ContainsKey).Select(id => previousPlaces[id]).ToList();
        return places.Count == 0 || items.Count == 0 ? null : items[Math.Min(places.Min(), items.Count - 1)].Id;
    }

    /// <summary>
    /// What an Undo brought back selected where the list shows it (G18), as the window's own choice; where the list shows
    /// none of it, the scope, the search and the selection stay as the person left them.
    /// </summary>
    internal void SelectIfListed(IReadOnlyCollection<long> ids)
    {
        bool listed;
        lock (gate)
        {
            listed = ids.Any(indexOf.ContainsKey);
            if (listed)
            {
                ReplaceSelection(ids, focus: null, byImport: false, byPerson: false);
            }
        }

        if (listed)
        {
            Raise();
        }
    }
}
