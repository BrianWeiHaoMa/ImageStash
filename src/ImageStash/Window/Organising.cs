using ImageStash.Store;

namespace ImageStash.Window;

/// <summary>
/// Tags and the favorite flag in the window (G17): the one path <c>Ctrl+D</c>, the item menu, a tile dropped on a chip
/// and the preview take. Each change is one store transaction over the items as the caller took them; the listing then
/// reloads, keeping the selection where its items still exist, so the grid, the chips and the Favorites scope show the
/// store's answer. A change that succeeded says nothing more; every refusal is named in a notice, by item, as the
/// store words it, and the other items are still changed (G35). Nothing here records a use (G10) or touches a file
/// (G29). The store works on its own thread, so the window keeps answering meanwhile; changes run one at a time, each
/// after the refresh of the one before, so a toggle decides from what the change before it left.
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
    /// <c>Ctrl+D</c> in the grid: the selection, once what was typed has arrived (G5, G7), toggled as
    /// <see cref="ToggleFavorite"/> says. A press while the last one is still being written toggles again, as a held key
    /// repeats it (G17): the selection is taken before waiting behind the change before it, whose refresh may move the
    /// selection off the item pressed (out of Favorites).
    /// </summary>
    public Task<Outcome> ToggleFavoriteOfSelection() => Counted(async () =>
    {
        await listing.Settle();
        return await ToggleFavorite(listing.SelectedItems);
    });

    /// <summary>
    /// G17's toggle: while any item is not a favorite, every one becomes one; else none stays one. Each item's flag is
    /// read as the listing now shows it, or as the store holds it once the listing no longer lists it, never as the
    /// caller's copy was.
    /// </summary>
    public Task<Outcome> ToggleFavorite(IReadOnlyList<StashItem> items) => OneAtATime(() => Toggle(items));

    /// <summary>Sets the flag on every item as given, never toggling it: a drop on Favorites, the menu's entry.</summary>
    public Task<Outcome> SetFavorite(IReadOnlyList<StashItem> items, bool favorite) => OneAtATime(() => Favorite(items, favorite));

    /// <summary>Gives every item the tags <paramref name="text"/> commits, as typed; the store splits it into one tag per word.</summary>
    public Task<Outcome> AddTag(IReadOnlyList<StashItem> items, string text) =>
        OneAtATime(() => Change(items, ItemTags.AddAction, store => store.AddTag(items, text)));

    /// <summary>Takes <paramref name="tag"/>, in any spelling, off every item.</summary>
    public Task<Outcome> RemoveTag(IReadOnlyList<StashItem> items, string tag) =>
        OneAtATime(() => Change(items, ItemTags.RemoveAction, store => store.RemoveTag(items, tag)));

    /// <summary>
    /// A click on a tag Add tag ▸ lists (G17): while every item still in the stash carries <paramref name="tag"/>, it is
    /// taken off them all; else every item is given it. Decided from the items as <see cref="Present"/> reads them,
    /// never from the caller's copies, so two clicks give and take; an item gone meanwhile is handed on, and refused by name.
    /// </summary>
    public Task<Outcome> ToggleTag(IReadOnlyList<StashItem> items, string tag) => OneAtATime(async () =>
        AllCarry(await Present(items), tag)
            ? await Change(items, ItemTags.RemoveAction, store => store.RemoveTag(items, tag))
            : await Change(items, ItemTags.AddAction, store => store.AddTag(items, tag)));

    /// <summary>
    /// The items as the listing shows them now, or as the store holds them once the listing no longer lists them; an
    /// item the store no longer holds is left out.
    /// </summary>
    public async Task<IReadOnlyList<StashItem>> Present(IReadOnlyList<StashItem> items) =>
        (await Now(items)).OfType<StashItem>().ToList();

    /// <summary>Whether every item is a favorite, so the menu offers Remove from Favorites and <c>Ctrl+D</c> unsets the flag.</summary>
    public static bool AllFavorites(IReadOnlyList<StashItem> items) => items.Count > 0 && items.All(item => item.Favorite);

    /// <summary>
    /// Whether every item carries <paramref name="tag"/>, in any spelling: the mark a tag list shows, and what a click on
    /// Add tag ▸'s entry decides by (G17).
    /// </summary>
    public static bool AllCarry(IReadOnlyList<StashItem> items, string tag) =>
        items.Count > 0 && items.All(item => item.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase));

    /// <summary>
    /// The tags the items carry, each once, in spelling order ignoring case as the store compares tags: what Remove tag ▸
    /// offers and the tag field shows with its × (G17), the tag of any one item among them, not only those all carry.
    /// </summary>
    public static IReadOnlyList<string> CarriedTags(IEnumerable<StashItem> items) =>
        items.SelectMany(item => item.Tags).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();

    private Task<Outcome> OneAtATime(Func<Task<Outcome>> change) => Counted(async () =>
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

    /// <summary>A change counted in <see cref="Changing"/> from when it is asked until it is answered, so a Move waits for it.</summary>
    private async Task<Outcome> Counted(Func<Task<Outcome>> change)
    {
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

    private async Task<Outcome> Toggle(IReadOnlyList<StashItem> items)
    {
        // A press again after the last one took the item out of the listing (out of Favorites) decides from that
        // press's answer, which the store holds, so two presses bring the flag back (G17). An item the store lists no
        // more goes as handed in, which the change then refuses by name.
        var current = (await Now(items)).Select((now, i) => now ?? items[i]).ToList();
        return await Favorite(current, !AllFavorites(current));
    }

    /// <summary>Each item as the listing shows it now, else as the store lists it now; null once the store lists it no more.</summary>
    private async Task<IReadOnlyList<StashItem?>> Now(IReadOnlyList<StashItem> items)
    {
        var shown = listing.Items.ToDictionary(item => item.Id);
        var now = new List<StashItem?>();
        foreach (var item in items)
        {
            now.Add(shown.GetValueOrDefault(item.Id) ?? (listing.Store is { } store && await store.Item(item.Id) is { Value: { } stored } ? stored : null));
        }

        return now;
    }

    private Task<Outcome> Favorite(IReadOnlyList<StashItem> items, bool favorite) =>
        Change(items, favorite ? ItemFavorite.AddAction : ItemFavorite.RemoveAction, store => store.SetFavorite(items, favorite));

    /// <summary>
    /// One change over <paramref name="items"/>: nothing to change is done with nothing shown; while the stash is not
    /// open the items are refused by name and nothing is asked of the store; otherwise the store's outcomes, the
    /// listing refreshed, and each distinct failure named once, the first returned.
    /// </summary>
    private async Task<Outcome> Change(IReadOnlyList<StashItem> items, string action, Func<StashStore, Task<IReadOnlyList<Outcome>>> change)
    {
        if (items.Count == 0)
        {
            return Outcome.Done;
        }

        if (listing.CannotStartReason is { } refused)
        {
            return Named(new Failure(action, Notices.Describe(items), refused));
        }

        var store = listing.Store!;

        var outcomes = await change(store);
        await listing.Refresh();
        var failures = outcomes.Where(outcome => !outcome.Succeeded).Select(outcome => outcome.Failure!).DistinctBy(failure => failure.Sentence).ToList();
        foreach (var failure in failures)
        {
            notices.Raise(failure);
        }

        return failures.Count == 0 ? Outcome.Done : failures[0];
    }

    private Outcome Named(Failure failure)
    {
        notices.Raise(failure);
        return failure;
    }
}
