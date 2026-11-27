using ImageStash.Store;

namespace ImageStash.Window;

/// <summary>
/// The one home of showing new items: All, the search cleared, those items selected. An import's end and its Show call it
/// with the import's own conditions (G12); a capture card's click and its Edit (G16), and Save as new item where the view
/// cannot list the copy (G26), call it as they are asked.
/// </summary>
public sealed partial class Listing
{
    /// <summary>
    /// A capture card's click or Edit (G16), or Save as new item (G26): <paramref name="ids"/> shown on All and selected,
    /// unless the person changes the view meanwhile; false when they did, or the read failed, which is named.
    /// </summary>
    internal Task<bool> ShowNewItems(IReadOnlyList<long> ids, bool byPerson) =>
        ShowNewItems(ids, ImportBegan.In(this), Replacements, SettleTagChoices, finishView: null, byPerson);

    /// <summary>
    /// Save as new item's copy (G26): selected where the view lists it, else shown on All with no search, as an import
    /// shows what it added; the copy is the window's choice, never recorded as anything that would list it in Recent (G4).
    /// </summary>
    internal async Task ShowNewItem(long id)
    {
        if (PlaceOf(id) is not null || !await ShowNewItems([id], byPerson: false))
        {
            FocusIfListed(id);
        }
    }

    private async Task SettleTagChoices()
    {
        while (TagChoicePending)
        {
            await Settle();
        }
    }

    /// <summary>
    /// Prepares All while the person's current view and its keys stay live. Only a current result takes over (G12): an
    /// import's, when the person has not moved on since <paramref name="began"/>.
    /// </summary>
    internal async Task<bool> ShowNewItems(IReadOnlyList<long> ids, ImportBegan began, int replacements,
        Func<Task> settleChoices, Func<Task>? finishView, bool byPerson = false)
    {
        if (byPerson)
        {
            began = new ImportBegan(NextInput());
        }

        while (true)
        {
            await Settle();
            if (began.MovedOn(this) || store is not { } opened)
            {
                return false;
            }

            var view = started;
            var read = await opened.ListAll();
            var (all, counted, _) = read;
            if (!ReferenceEquals(store, opened))
            {
                return false;
            }

            await settleChoices();
            if (began.MovedOn(this) || !ReferenceEquals(store, opened))
            {
                return false;
            }

            if (!all.Succeeded)
            {
                failed(all.Failure);
                return false;
            }

            if (view != started || read.Version != opened.ListingVersion)
            {
                continue;
            }

            if (finishView is not null)
            {
                await finishView();
                finishView = null;
                await settleChoices();
            }

            lock (gate)
            {
                if (began.MovedOn(this) || !ReferenceEquals(store, opened))
                {
                    return false;
                }

                // A write can commit before its window action requests a refresh, as Delete does before recycling.
                if (view != started || read.Version != opened.ListingVersion)
                {
                    continue;
                }

                // Show can outlive the stash that issued its notice, whose item IDs a replacement may reuse.
                var selected = replacements == Replacements ? ids : [];
                CountChange(byPerson, began.Input);
                ApplyNewItems(read, selected, byPerson);
            }

            if (!counted.Succeeded)
            {
                failed(counted.Failure);
            }

            QueryChangedByWindow?.Invoke();
            Raise();
            return true;
        }
    }

    private void ApplyNewItems(StoreListing read, IReadOnlyList<long> ids, bool byPerson)
    {
        var (all, counted, version) = read;
        SetScope(Scope.All, byPerson);
        LastSelectionReset = NextInput();
        selectionResetWaiting = false;
        searchRead = null;
        loadFailed = !counted.Succeeded;
        ApplyLoadedItems(new Loaded(read, Scope.All, all.Value!.Count == 0),
            searching: false, reset: false, keepSelection: false);
        ApplyListingTags(counted, version, Query, searching: false, itemsLoaded: true);

        if (byPerson || ids.Count > 0)
        {
            ReplaceSelection(ids, focus: null, byImport: true, byPerson: false);
        }
    }
}
