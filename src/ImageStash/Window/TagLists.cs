using ImageStash.Store;

namespace ImageStash.Window;

/// <summary>
/// The item menu's two tag lists (G17), as their entries show. Add tag ▸ lists Type a tag… first, so a long list need
/// not be scrolled to type one, then every tag in the stash, the commonest first; Remove tag ▸ lists the tags the items
/// carry. An entry is marked while every item carries its tag, by the one rule a click on Add tag ▸'s entry decides by
/// (<see cref="Organising.AllCarry"/>), so what the mark shows is what the click undoes. A list's entry leaves the menu
/// open for the next; the menu reads the lists again after each.
/// </summary>
public static class TagLists
{
    private static readonly ItemMenuEntry TypeATag = new(ItemMenuAction.TypeATag, "Type a tag…", null, "");

    /// <summary>
    /// Add tag ▸'s entries: Type a tag…; then every tag of <paramref name="counts"/>, the store's order (the most carried
    /// first, then by spelling), each marked while every one of <paramref name="items"/> carries it, the first after a
    /// separator.
    /// </summary>
    public static IReadOnlyList<ItemMenuEntry> AddTagEntries(IReadOnlyList<TagCount> counts, IReadOnlyList<StashItem> items)
    {
        var tags = counts.Select((count, index) => TagEntry(ItemMenuAction.AddTag, count.Tag, items) with { AfterSeparator = index == 0 });
        return [TypeATag, .. tags];
    }

    /// <summary>
    /// Remove tag ▸'s entries: every tag any of <paramref name="items"/> carries, each once, in spelling order, each
    /// marked while every one of them carries it.
    /// </summary>
    public static IReadOnlyList<ItemMenuEntry> RemoveTagEntries(IReadOnlyList<StashItem> items) =>
        Organising.CarriedTags(items).Select(tag => TagEntry(ItemMenuAction.RemoveTag, tag, items)).ToList();

    /// <summary>
    /// A click on a list's entry for <paramref name="tag"/>, on the items the menu was opened on: Add tag ▸'s gives it
    /// to each, or takes it off all once all carry it; Remove tag ▸'s takes it off each.
    /// </summary>
    public static Task<Outcome> Click(Organising organising, ItemMenuAction list, IReadOnlyList<StashItem> items, string tag) =>
        list == ItemMenuAction.RemoveTag ? organising.RemoveTag(items, tag) : organising.ToggleTag(items, tag);

    private static ItemMenuEntry TagEntry(ItemMenuAction list, string tag, IReadOnlyList<StashItem> items) =>
        new(list, tag, null, "", Tag: tag, Marked: Organising.AllCarry(items, tag));
}
