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
    public static IReadOnlyList<Outcome> Set(StoreContext store, IReadOnlyList<StashItem> items, bool favorite) =>
        ItemFactChange.Apply(store, favorite ? AddAction : RemoveAction, items, listed =>
        {
            foreach (var id in listed)
            {
                store.Execute("UPDATE items SET favorite = $favorite WHERE id = $id", ("$id", id), ("$favorite", favorite));
            }
        });
}
