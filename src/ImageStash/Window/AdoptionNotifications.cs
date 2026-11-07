using ImageStash.Store;

namespace ImageStash.Window;

/// <summary>
/// The notification above the tray that a picture another program saved through Open with… replaced an item (G27,
/// G16), raised once the adoption has answered (R9): Undo restores the previous original and layers and sends the
/// adopted picture to the Recycle Bin; once it has gone without Undo, by its time or its ×, the previous original goes
/// there instead (G29). Undo and the leaving each name the adoption by its revision, so a stale one acts on nothing,
/// a stale Undo then refused by name (G35) with the previous original sent on as at the leaving; a
/// later adoption of the same item takes the earlier notification down without acting on it, since Store expired that
/// adoption with the later one, and an earlier adoption said late (the start's) is not said over a later one; adoptions
/// of two items have two notifications. An Undo after an edit's Save of the item since the adoption, even one that left
/// it without layers, is refused by name as Store refuses one after a Save that changed it; an edit of the item still open
/// is left as Cancel would before its original is replaced under it, once a Save of it that runs has ended (G27, G20). A
/// failure of either is named.
/// </summary>
public sealed class AdoptionNotifications
{
    /// <summary>Why an Undo is refused once the item has been saved, reverted or replaced again since the adoption.</summary>
    public const string ChangedSince = "it has changed since that picture replaced it";

    private readonly object gate = new();
    private readonly Dictionary<long, (Notice Notice, long Revision)> latest = [];
    // The revision each item's latest edit Save left it at: a Save with no layers moves no revision on, so one at or after
    // an adoption's is a Save since it (G27).
    private readonly Dictionary<long, long> savedAt = [];
    private readonly Listing listing;
    private readonly Notices notifications;
    private readonly Editing editing;
    private readonly Func<Task> refresh;

    /// <param name="editing">The window's edits, whose Save of an item ends its adoption's Undo and whose open edit an Undo leaves first.</param>
    /// <param name="refresh">Reads the listing again after an Undo (as after every change of the stash).</param>
    public AdoptionNotifications(Listing listing, Notices notifications, Editing editing, Func<Task> refresh)
    {
        this.listing = listing;
        this.notifications = notifications;
        this.editing = editing;
        this.refresh = refresh;
        editing.ItemSaved += Saved;
    }

    /// <summary>
    /// Says <paramref name="adopted"/> was replaced, with Undo and ×, for the adoption <paramref name="revision"/> names:
    /// the revision the adoption answered, never one read from the listing later, which a Save may have moved on.
    /// </summary>
    public void Raise(StashItem adopted, long revision)
    {
        var id = adopted.Id;
        Notice? earlier;
        lock (gate)
        {
            if (latest.TryGetValue(id, out var said) && said.Revision >= revision)
            {
                // A later adoption of the item was said already: Store expired this one with it, so its Undo would do nothing.
                return;
            }

            earlier = said.Notice;
        }

        if (earlier is not null)
        {
            notifications.Dismiss(earlier);
        }

        var notice = notifications.Raise(
            $"Replaced {Notices.Describe([adopted])} with the picture another program saved.",
            "Undo",
            () => Undo(adopted, revision),
            () => Expire(adopted, revision));
        lock (gate)
        {
            latest[id] = (notice, revision);
        }
    }

    /// <summary>An edit's Save of an item, whose adoption's Undo it ends if it came at or after that adoption (G27).</summary>
    private void Saved(StashItem item)
    {
        lock (gate)
        {
            savedAt[item.Id] = item.RenderedRevision;
        }
    }

    /// <summary>
    /// The stash these items came from was replaced by an import, whose store gives their identities to other items
    /// (G36, G27): every adoption and Save remembered is forgotten, so an imported item's adoption is said, and its Undo
    /// judged, by its own history alone.
    /// </summary>
    public void Forget()
    {
        lock (gate)
        {
            latest.Clear();
            savedAt.Clear();
        }
    }

    private bool SavedSince(long id, long revision)
    {
        lock (gate)
        {
            return savedAt.TryGetValue(id, out var saved) && saved >= revision;
        }
    }

    /// <summary>
    /// Undo: the previous original and layers back (G27); a failure is the notices' to name. One Store no longer acts on
    /// (the item saved, reverted or replaced again since, or deleted) is refused by name (G35), and, the notification
    /// having gone, its previous original goes to the Recycle Bin as at its leaving (G27, G29), as it does for one Store
    /// refuses before committing anything; one that fails after its commit leaves its steps for a later change or the
    /// next start to finish (State: A live failure).
    /// </summary>
    private async Task<Outcome> Undo(StashItem adopted, long revision)
    {
        var subject = ItemQueries.Named(adopted.Name, adopted.FileName);
        if (listing.Store is not { } store)
        {
            return new Failure(ItemAdoptionUndo.Action, subject, listing.NotOpenReason!);
        }

        // A Save already made ends the Undo, the open edit left as it is; else the edit goes first, and a Save of the item
        // it was running ends the Undo as well.
        if (!SavedSince(adopted.Id, revision))
        {
            await editing.LeaveEditOf(adopted.Id);
        }

        if (SavedSince(adopted.Id, revision))
        {
            await Expire(adopted, revision);
            await refresh();
            return new Failure(ItemAdoptionUndo.Action, subject, ChangedSince);
        }

        var undone = await store.UndoAdoption(adopted.Id, revision);
        if (undone is { Succeeded: true, Value: null })
        {
            await Expire(adopted, revision);
            var now = await store.Item(adopted.Id);
            await refresh();
            return new Failure(ItemAdoptionUndo.Action, subject, now is { Succeeded: true, Value: null } ? ItemFactChange.NotInStash : ChangedSince);
        }

        // A refusal carries no item; a failure after the commit carries the item as committed.
        if (undone is { Succeeded: false, Value: null })
        {
            await Expire(adopted, revision);
        }

        await refresh();
        return undone.Succeeded ? Outcome.Done : undone.Failure;
    }

    /// <summary>Gone without Undo: the previous original to the Recycle Bin (G27, G29); a failure named above the tray.</summary>
    private async Task Expire(StashItem adopted, long revision)
    {
        var expired = listing.Store is { } store
            ? await store.ExpireAdoption(adopted.Id, revision)
            : new Failure(AdoptionUndo.ExpireAction, ItemQueries.Named(adopted.Name, adopted.FileName), listing.NotOpenReason!);
        if (!expired.Succeeded)
        {
            notifications.Raise(expired.Failure);
        }
    }
}
