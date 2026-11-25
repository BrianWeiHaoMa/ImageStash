namespace ImageStash.Store;

/// <summary>
/// The favorite flag (F9, G17). The store sets it as asked and never toggles it: <c>Ctrl+D</c>'s toggle, with its mixed
/// selection becoming all favorites, is the window's rule, and a drop on Favorites only sets it.
/// </summary>
internal static class ItemFavorite
{
    public const string AddAction = "Add to Favorites";
    public const string RemoveAction = "Remove from Favorites";

    /// <summary>Sets the flag on each listed item; an item already so is left as it is.</summary>
    public static ItemFactResult Set(StoreContext store, IReadOnlyList<StashItem> items, bool favorite) =>
        ItemFactChange.Apply(store, favorite ? AddAction : RemoveAction,
            favorite ? "Undo adding to Favorites" : "Undo removing from Favorites", items,
            listed => [.. listed.Select(item => SetFlag(store, item, favorite)).OfType<ItemFactDelta>()]);

    /// <summary>Sets a listed item's flag and reports its previous value only if the write changes it.</summary>
    public static ItemFactDelta? SetFlag(StoreContext store, ItemFactTarget item, bool favorite)
    {
        var changed = store.Execute(
            "UPDATE items SET favorite = $favorite WHERE id = $id AND (favorite <> 0) <> $favorite",
            ("$id", item.Id), ("$favorite", favorite));
        return changed == 0 ? null : new ItemFactDelta(item, [], [], !favorite);
    }
}
