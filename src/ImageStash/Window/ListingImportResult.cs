using ImageStash.Store;

namespace ImageStash.Window;

public sealed partial class Listing
{
    /// <summary>Prepares All while the person's current view and its keys stay live. Only a current result takes over (G12).</summary>
    internal async Task<bool> ShowImportResult(IReadOnlyList<long> ids, ImportBegan began, int replacements,
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
            var all = await opened.All();
            if (!ReferenceEquals(store, opened))
            {
                return false;
            }

            var (counted, version) = await opened.CountListingTags();
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

            if (view != started)
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

                // If a refresh or summon ran, its rows can be newer than the All read prepared here.
                if (view != started)
                {
                    continue;
                }

                // Show can outlive the stash that issued its notice, whose item IDs a replacement may reuse.
                var selected = replacements == Replacements ? ids : [];
                CountChange(byPerson, began.Input);
                ApplyImportResult(all, selected, counted, version, byPerson);
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

    private void ApplyImportResult(Outcome<IReadOnlyList<StashItem>> all, IReadOnlyList<long> ids,
        Outcome<IReadOnlyList<TagCount>> counted, (long Commits, long FileSteps) version, bool byPerson)
    {
        SetScope(Scope.All, byPerson);
        LastSelectionReset = NextInput();
        selectionResetWaiting = false;
        searchRead = null;
        loadFailed = !counted.Succeeded;
        ApplyLoadedItems(new Loaded(all, Scope.All, all.Value!.Count == 0),
            searching: false, reset: false, keepSelection: false);
        ApplyListingTags(counted, version, Query, searching: false, itemsLoaded: true);

        if (byPerson || ids.Count > 0)
        {
            ReplaceSelection(ids, focus: null, byImport: true, byPerson: false);
        }
    }
}
