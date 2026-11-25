using ImageStash.Store;

namespace ImageStash.Window;

/// <summary>
/// The one act that steps the preview to the previous or the next item of the grid's list (G19), in two halves callable
/// apart: <see cref="Next"/> decides, from <see cref="Listing.Items"/> as it is at that moment and never from a copy, which
/// item a step shows (<see cref="PreviewStep"/>); <see cref="MoveTo"/> selects that item alone in the grid and has the
/// preview show it, so Enter, <c>Ctrl+C</c>, Del, <c>Ctrl+D</c>, F2, <c>Ctrl+E</c> and Esc go on from it (G8, G7). It
/// keeps where the item shown last stood in the list, which the step goes on from once a rename or the star has taken
/// that item out of the list. It knows nothing of a name or tag commit in flight: that commit acts on the item it was
/// started on (<see cref="Preview"/>), and a step neither waits for it nor takes it along.
/// </summary>
public sealed class PreviewStepping
{
    private readonly Listing listing;
    private readonly Func<StashItem?> shown;
    private readonly Action<StashItem> show;
    private long? trackedId;
    private int? lastPlace;

    /// <param name="shown">The item the preview shows, null while it is closed.</param>
    /// <param name="show">Has the open preview show the item given (<see cref="Preview.Open"/>).</param>
    public PreviewStepping(Listing listing, Func<StashItem?> shown, Action<StashItem> show)
    {
        this.listing = listing;
        this.shown = shown;
        this.show = show;
        listing.Changed += Observe;
    }

    /// <summary>
    /// Notes where the item shown stands in the grid's list while the list holds it; called whenever the list changes,
    /// which it hears itself, and whenever the preview opens, closes or moves to another item, which its owner tells it.
    /// </summary>
    public void Observe()
    {
        if (shown() is not { } item)
        {
            (trackedId, lastPlace) = (null, null);
            return;
        }

        var place = listing.PlaceOf(item.Id);
        if (trackedId != item.Id)
        {
            (trackedId, lastPlace) = (item.Id, place);
        }
        else if (place is not null)
        {
            lastPlace = place;
        }
    }

    /// <summary>The item a step <paramref name="way"/> shows now, or null when it does nothing or the preview is closed.</summary>
    public StashItem? Next(StepWay way)
    {
        Observe();
        return listing.ShowsRequestedView && shown() is { } item ? PreviewStep.Of(listing.Items, item.Id, lastPlace, way) : null;
    }

    /// <summary>
    /// The preview moved to <paramref name="item"/>, as <see cref="Next"/> decided, as the grid's list now has it, and the
    /// item selected alone in the grid; nothing, and false, while the preview is closed or once the grid's list, the
    /// store's latest answer, no longer holds the item.
    /// </summary>
    public bool MoveTo(StashItem item)
    {
        if (shown() is null || listing.Items.FirstOrDefault(listed => listed.Id == item.Id) is not { } current)
        {
            return false;
        }

        show(current);
        // The preview's own open selects it too; selecting it again would only raise the listing's change twice.
        if (listing.Focused != item.Id || listing.Selection.Count != 1)
        {
            listing.Focus(item.Id);
        }

        Observe();
        return true;
    }

    /// <summary>Left and Up (back), Right and Down (on) in the preview outside its fields: the step decided and made at once.</summary>
    public bool Step(StepWay way) => Next(way) is { } item && MoveTo(item);
}
