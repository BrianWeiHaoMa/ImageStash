using ImageStash.Store;

namespace ImageStash.Window;

/// <summary>
/// Tags and the favorite flag in the window (G17): the one path <c>Ctrl+D</c>, the item menu, a tile dropped on a chip
/// and the preview take. Each change is one store transaction over the items as the caller took them; the listing then
/// reloads, keeping the selection where its items still exist, so the grid, the chips and the Favorites scope show the
/// store's answer. Changes on several items and drops on chips offer Undo of the facts that committed. Every refusal
/// is named as the store words it (G35). Changes and their Undo affect tags and favorite flags alone (G10, G29).
/// They run one at a time, each after the refresh of the one before.
/// </summary>
public sealed class Organising(Listing listing, Notices notices)
{
    private readonly SemaphoreSlim oneAtATime = new(1, 1);
    private int changing;

    /// <summary>Raised whenever <see cref="Changing"/> changes.</summary>
    public event Action? Changed;

    /// <summary>A change of tags or favorites asked and not yet answered, which a Move waits for (State: Move).</summary>
    public bool Changing => Volatile.Read(ref changing) > 0;

    /// <summary>
    /// The acts of a favorite's toggle (<c>Ctrl+D</c>, the preview's star) and of a tag's toggle from a list, for the
    /// notices they raise (G16); setting a favorite and adding or removing a tag are acts by their own actions' names.
    /// </summary>
    public const string FavoriteAct = "Favorite";

    /// <inheritdoc cref="FavoriteAct"/>
    public const string TagAct = "Tag";

    /// <summary>
    /// G17's toggle: while any item is not a favorite, every one becomes one; else none stays one. Each item's flag is
    /// read from the current stash. A missing item is handed on for the store to refuse.
    /// </summary>
    public Task<Outcome> ToggleFavorite(IReadOnlyList<StashItem> items) =>
        Change(items, FavoriteAct, FavoriteAction(!AllFavorites(items)), async (store, targets) =>
        {
            // A second press may come after the first took the item out of Favorites.
            var current = (await Now(store, targets)).Select((item, index) => item ?? targets[index]).ToArray();
            return await store.SetFavoriteWithChanges(targets, !AllFavorites(current));
        });

    /// <summary>Sets the flag on every item as given by the menu's entry.</summary>
    public Task<Outcome> SetFavorite(IReadOnlyList<StashItem> items, bool favorite) =>
        SetFavorite(items, favorite, alreadyOnChip: null);

    private Task<Outcome> SetFavorite(IReadOnlyList<StashItem> items, bool favorite, string? alreadyOnChip)
    {
        var action = FavoriteAction(favorite);
        return Change(items, action, action, (store, targets) => store.SetFavoriteWithChanges(targets, favorite), alreadyOnChip);
    }

    private static string FavoriteAction(bool favorite) => favorite ? ItemFavorite.AddAction : ItemFavorite.RemoveAction;

    /// <summary>Gives every item the tags <paramref name="text"/> commits, as typed; the store splits it into one tag per word.</summary>
    public Task<Outcome> AddTag(IReadOnlyList<StashItem> items, string text) =>
        Change(items, ItemTags.AddAction, ItemTags.AddAction, (store, targets) => store.AddTagWithChanges(targets, text));

    /// <summary>An internal drop applies Favorites or an existing tag and always answers if accepted (G17).</summary>
    internal Task<Outcome> DropOnChip(IReadOnlyList<StashItem> items, string? tag) =>
        tag is null
            ? SetFavorite(items, true, alreadyOnChip: "already in Favorites")
            : Change(items, ItemTags.AddAction, ItemTags.AddAction, (store, targets) => store.AddExistingTag(targets, tag),
                alreadyOnChip: $"already tagged #{tag}");

    /// <summary>Takes <paramref name="tag"/>, in any spelling, off every item.</summary>
    public Task<Outcome> RemoveTag(IReadOnlyList<StashItem> items, string tag) =>
        Change(items, ItemTags.RemoveAction, ItemTags.RemoveAction, (store, targets) => store.RemoveTagWithChanges(targets, tag));

    /// <summary>Renames a tag on its carriers in the issuing stash, with no change to the query or selected items.</summary>
    internal Task<Outcome> RenameTag(StashStore issuer, string tag, string text)
    {
        const string action = ItemTags.RenameAction;
        if (RefuseTagChange(action, issuer, tag) is { } refusal)
        {
            return Task.FromResult<Outcome>(refusal);
        }

        return OneAtATime(action, async () =>
        {
            var renamed = await issuer.RenameTag(tag, text);
            await listing.Refresh();
            if (renamed.Failure is { } failure)
            {
                notices.Raise(failure with { Subject = $"#{tag}" });
            }
            else if (renamed.Value is { Items: > 0 } result)
            {
                notices.Raise(result.Merged ? $"Merged #{tag} into #{result.Tag}" : $"Renamed #{tag} to #{result.Tag}");
            }

            return renamed;
        });
    }

    /// <summary>Takes the offered tag off its listed carriers, while the whole-stash count in the offer still holds.</summary>
    internal Task<Outcome> RemoveTagFromAll(StashStore issuer, TagCount offer)
    {
        const string action = ItemTags.RemoveFromAllAction;
        if (RefuseTagChange(action, issuer, offer.Tag) is { } refusal)
        {
            return Task.FromResult<Outcome>(refusal);
        }

        return OneAtATime(action, async () =>
        {
            var removal = await issuer.RemoveTagFromAll(offer.Tag, offer.Items);
            await listing.Refresh();
            if (removal.Value is { Receipt: { } receipt, Count: var count })
            {
                OfferUndo(OrganisingNotice.TagRemoved(count.Tag, receipt.Changes.Length), receipt);
                return Outcome.Done;
            }

            var failure = removal.Failure ?? Failure.Refusal(action, $"#{offer.Tag}", removal.Value!.Count.Items switch
            {
                0 => "no item carries this tag any more",
                1 => "1 item now carries this tag. Open the tag menu again",
                var items => $"{OrganisingNotice.Items(items)} now carry this tag. Open the tag menu again",
            });
            notices.Raise(failure);
            return failure;
        });
    }

    private Failure? RefuseTagChange(string action, StashStore issuer, string tag)
    {
        var reason = listing.CannotStartReason ?? (listing.Store != issuer ? Listing.ReplacedSinceReason : null);
        if (reason is null)
        {
            return null;
        }

        using var acting = UserAct.Begin(action);
        var refusal = Failure.Refusal(action, $"#{tag}", reason);
        notices.Raise(refusal);
        return refusal;
    }

    /// <summary>
    /// A click on a tag Add tag ▸ lists (G17): while every item still in the stash carries <paramref name="tag"/>, it is
    /// taken off them all; else every item is given it. Each click reads the current memberships.
    /// An item gone meanwhile is handed on and refused by name.
    /// </summary>
    public Task<Outcome> ToggleTag(IReadOnlyList<StashItem> items, string tag) =>
        Change(items, TagAct, ItemTags.AllCarry(items, tag) ? ItemTags.RemoveAction : ItemTags.AddAction,
            (store, targets) => store.ToggleExistingTag(targets, tag));

    /// <summary>
    /// The requested items still in the stash, as of the latest read.
    /// </summary>
    public async Task<IReadOnlyList<StashItem>> Present(IReadOnlyList<StashItem> items) =>
        listing.Store is { } store ? (await Now(store, items)).OfType<StashItem>().ToList() : [];

    /// <summary>Whether every item is a favorite, so the menu offers Remove from Favorites and <c>Ctrl+D</c> unsets the flag.</summary>
    public static bool AllFavorites(IReadOnlyList<StashItem> items) => items.Count > 0 && items.All(item => item.Favorite);

    /// <summary>
    /// The tags the items carry, each once, in spelling order ignoring case as the store compares tags: what Remove tag ▸
    /// offers and the tag field shows with its × (G17), the tag of any one item among them, not only those all carry.
    /// </summary>
    public static IReadOnlyList<string> CarriedTags(IEnumerable<StashItem> items) =>
        items.SelectMany(item => item.Tags).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();

    private Task<Outcome> OneAtATime(string act, Func<Task<Outcome>> change) => Counted(act, async () =>
    {
        await oneAtATime.WaitAsync();
        try
        {
            return await change();
        }
        finally
        {
            oneAtATime.Release();
        }
    });

    /// <summary>
    /// A change counted in <see cref="Changing"/> from when it is asked until it is answered, so a Move waits for it, as
    /// the person's <paramref name="act"/>, which the notices it raises carry (G16).
    /// </summary>
    private async Task<Outcome> Counted(string act, Func<Task<Outcome>> change)
    {
        using var acting = UserAct.Begin(act);
        Interlocked.Increment(ref changing);
        Changed?.Invoke();
        try
        {
            return await change();
        }
        finally
        {
            Interlocked.Decrement(ref changing);
            Changed?.Invoke();
        }
    }

    /// <summary>The latest facts for each target, with a null entry for a missing item.</summary>
    private async Task<IReadOnlyList<StashItem?>> Now(StashStore store, IReadOnlyList<StashItem> items)
    {
        // A scope or search can supersede a change's refresh before the newer listing has arrived.
        var (commits, fileSteps) = store.ListingVersion;
        var shown = listing.Items.ToDictionary(item => item.Id);
        var current = items.Select(item => shown.GetValueOrDefault(item.Id)).ToArray();
        if (current.All(item => item is not null && item.CommitsWhenRead == commits
            && (!item.StepsPendingWhenRead || item.FileStepsWhenRead == fileSteps)))
        {
            return current;
        }

        if (items.Count == 1)
        {
            var read = await store.Item(items[0].Id);
            return read.Succeeded ? [read.Value] : current;
        }

        var all = await store.All();
        if (all.Value is not { } found)
        {
            return current;
        }

        var stored = found.ToDictionary(item => item.Id);
        return items.Select(item => stored.GetValueOrDefault(item.Id)).ToArray();
    }

    /// <summary>
    /// Binds the gesture's targets and stash before waiting. A transfer lets already accepted changes finish.
    /// </summary>
    private Task<Outcome> Change(IReadOnlyList<StashItem> items, string act, string refusalAction,
        Func<StashStore, IReadOnlyList<StashItem>, Task<ItemFactResult>> write, string? alreadyOnChip = null)
    {
        var targets = items.DistinctBy(item => item.Id).ToArray();
        if (targets.Length == 0)
        {
            return Task.FromResult(Outcome.Done);
        }

        if (listing.CannotStartReason is { } refused)
        {
            using var acting = UserAct.Begin(act);
            var failure = Failure.Refusal(refusalAction, Notices.Describe(targets), refused);
            notices.Raise(failure);
            return Task.FromResult<Outcome>(failure);
        }

        var store = listing.Store!;
        return OneAtATime(act, async () =>
        {
            var result = await write(store, targets);
            await listing.Refresh();
            var failures = Failures(result);
            foreach (var failure in failures)
            {
                notices.Raise(failure);
            }

            var words = OrganisingNotice.For(result, targets.Length, alreadyOnChip);
            if (words is not null)
            {
                OfferUndo(words, result);
            }

            return FirstFailure(failures);
        });
    }

    private void OfferUndo(string words, ItemFactResult result) => notices.Raise(words, "Undo", () => Undo(result));

    private Task<Outcome> Undo(ItemFactResult result) => OneAtATime(Notices.Undoing, async () =>
    {
        // A transfer can wait for an offered Undo. The Store checks that this result belongs to its own open stash.
        var reversed = listing.Store is { } store
            ? await store.UndoItemFacts(result)
            : result.RefuseUndo(listing.NotOpenReason ?? "the stash is closed");
        await listing.Refresh();
        var failures = Failures(reversed);
        foreach (var failure in failures.Skip(1))
        {
            notices.Raise(failure);
        }

        // The notice stack raises the returned refusal.
        return FirstFailure(failures);
    });

    // Store shares a transaction-wide refusal across its outcomes. Two items can have the same displayed name.
    private static List<Failure> Failures(ItemFactResult result) =>
        result.Outcomes.Select(outcome => outcome.Failure).OfType<Failure>()
            .Distinct<Failure>(ReferenceEqualityComparer.Instance).ToList();

    private static Outcome FirstFailure(List<Failure> failures) => failures.Count == 0 ? Outcome.Done : failures[0];
}
