using ImageStash.Store;

namespace ImageStash.Window;

/// <summary>Which way a step of the preview goes through the grid's list: back (Left, Up) or on (Right, Down).</summary>
public enum StepWay
{
    Back,
    On,
}

/// <summary>
/// Which item a step of the preview shows (G19): while the grid's list holds the item shown, the previous or the next
/// item there, nothing past the list's first or last item. G19 says nothing of an item the list no longer holds (a rename
/// ending a search's match, its star clicked off while Favorites shows), so the steps go on from the place it last stood
/// at: on to the item now standing there, back to the one before, and where the list has shrunk past that place, on to
/// nothing and back to the list's last item; from an item that never stood in the list (Save as new item made while
/// Favorites shows), on to the first item and back to nothing; with an empty list, nothing.
/// </summary>
public static class PreviewStep
{
    /// <param name="items">The grid's list as it is now (<see cref="Listing.Items"/>), in its order.</param>
    /// <param name="shown">The identity of the item the preview shows.</param>
    /// <param name="lastPlace">Where that item last stood in the list, or null when it has not stood there since the preview showed it.</param>
    /// <returns>The item to show, or null when the step does nothing.</returns>
    public static StashItem? Of(IReadOnlyList<StashItem> items, long shown, int? lastPlace, StepWay way)
    {
        if (items.Count == 0)
        {
            return null;
        }

        for (var i = 0; i < items.Count; i++)
        {
            if (items[i].Id == shown)
            {
                var neighbour = way == StepWay.On ? i + 1 : i - 1;
                return neighbour >= 0 && neighbour < items.Count ? items[neighbour] : null;
            }
        }

        if (lastPlace is not { } place)
        {
            return way == StepWay.On ? items[0] : null;
        }

        if (place >= items.Count)
        {
            return way == StepWay.On ? null : items[^1];
        }

        return way == StepWay.On ? items[place] : place > 0 ? items[place - 1] : null;
    }
}
