using ImageStash.Store;

namespace ImageStash.Window;

/// <summary>
/// The item menu's two tag lists (G17), as their entries show. Add tag ▸ lists Type a tag… first, so a long list need
/// not be scrolled to type one, then every tag in the stash, the commonest first; Remove tag ▸ lists the tags the items
/// carry, in the same order. Each tag's entry shows the number of items in the stash that carry it, and is marked while
/// every item carries it, by the one rule a click on Add tag ▸'s entry decides by (<see cref="ItemTags.AllCarry"/>), so
/// what the mark shows is what the click undoes. A list's entry leaves the menu open for the next.
/// </summary>
public static class TagLists
{
    private static readonly ItemMenuEntry TypeATag = new(ItemMenuAction.TypeATag, "Type a tag…", null, "");

    /// <summary>
    /// Add tag ▸'s entries: Type a tag…; then a tag entry for each of <paramref name="rows"/>, in their order, each marked
    /// while every one of <paramref name="items"/> carries it, the first after a separator.
    /// </summary>
    public static IReadOnlyList<ItemMenuEntry> AddTagEntries(IReadOnlyList<TagCount> rows, IReadOnlyList<StashItem> items)
    {
        var tags = rows.Select((row, index) => TagEntry(ItemMenuAction.AddTag, row, items) with { AfterSeparator = index == 0 });
        return [TypeATag, .. tags];
    }

    /// <summary>Remove tag ▸'s entries: a tag entry for each of <paramref name="rows"/>, in their order, each marked while every one of <paramref name="items"/> carries it.</summary>
    public static IReadOnlyList<ItemMenuEntry> RemoveTagEntries(IReadOnlyList<TagCount> rows, IReadOnlyList<StashItem> items) =>
        rows.Select(row => TagEntry(ItemMenuAction.RemoveTag, row, items)).ToList();

    /// <summary>Of <paramref name="counts"/>, in their order, the tags any of <paramref name="items"/> carries: what Remove tag ▸ offers.</summary>
    public static IReadOnlyList<TagCount> Carried(IReadOnlyList<TagCount> counts, IReadOnlyList<StashItem> items) =>
        counts.Where(count => ItemTags.AnyCarry(items, count.Tag)).ToList();

    /// <summary>
    /// A click on a list's entry for <paramref name="tag"/>, decided from the store at the write: Add tag ▸'s gives it to
    /// each, or takes it off all once all carry it; Remove tag ▸'s takes it off each while any carries it, else gives it
    /// to each, as Add tag ▸'s does on an unmarked tag. A tag no item carries is refused, unless <paramref name="held"/>,
    /// the open list clicked, is the one whose own click took it off its last item.
    /// </summary>
    public static Task<Outcome> Click(Organising organising, ItemMenuAction list, IReadOnlyList<StashItem> items, string tag, OpenTagList? held = null) =>
        organising.PickTag(items, tag, whileAnyCarries: list == ItemMenuAction.RemoveTag, held);

    private static ItemMenuEntry TagEntry(ItemMenuAction list, TagCount row, IReadOnlyList<StashItem> items) =>
        new(list, row.Tag, null, "", Tag: row.Tag, Marked: ItemTags.AllCarry(items, row.Tag), Count: row.Items);
}

/// <summary>
/// One open item menu's Add tag ▸ and Remove tag ▸ (G17), on the items the menu was opened on. Add tag ▸ shows Type a
/// tag… alone until the menu's first read of the tags lands, so a tag no item carries any more is never offered and one
/// just added is; both lists take their entries and order from that read and keep them until the menu closes
/// (<see cref="OpenTagList"/>): after each click every number and mark follows the store in place, a tag made since,
/// or one the items come to carry in Remove tag ▸, joins the end, and a tag a click there took off its last item stays,
/// reading 0 and unmarked, for the next click there to give back. Remove tag ▸ shows once it has an entry, and from
/// then on stays. A read that fails is named (G35).
/// </summary>
public sealed class ItemMenuTagLists(Listing listing, Notices notices, Organising organising, IReadOnlyList<StashItem> items)
{
    private readonly IReadOnlyList<StashItem> opened = items;
    private readonly OpenTagList add = new();
    private readonly OpenTagList remove = new();
    private IReadOnlyList<StashItem> present = items;
    private int reads;

    /// <summary>Add tag ▸'s entries as they show now.</summary>
    public IReadOnlyList<ItemMenuEntry> AddTag { get; private set; } = TagLists.AddTagEntries([], items);

    /// <summary>Remove tag ▸'s entries as they show now; until the first read, the tags the items carry, as the window last counted them.</summary>
    public IReadOnlyList<ItemMenuEntry> RemoveTag { get; private set; } = TagLists.RemoveTagEntries(CarriedAsListed(listing, items), items);

    /// <summary>
    /// Reads the stash's tags and the items as the store holds them now, and takes them into both lists; the latest
    /// read alone applies. Whether this read changed what the lists show.
    /// </summary>
    public async Task<bool> Read()
    {
        if (listing.Store is not { } store)
        {
            return false;
        }

        var read = ++reads;
        // The items and the counts in one read, so a mark and a number never come from different changes.
        var loaded = await store.AllWithTagCounts();
        if (read != reads || !ReferenceEquals(listing.Store, store))
        {
            return false;
        }

        if (!loaded.Succeeded)
        {
            notices.Raise(loaded.Failure);
            return false;
        }

        var (all, counts) = loaded.Value;
        var ids = opened.Select(item => item.Id).ToHashSet();
        present = all.Where(item => ids.Contains(item.Id)).ToList();
        AddTag = TagLists.AddTagEntries(add.Rows(counts), present);
        RemoveTag = TagLists.RemoveTagEntries(remove.Rows(counts, count => ItemTags.AnyCarry(present, count.Tag)), present);
        return true;
    }

    /// <summary>A click on <paramref name="list"/>'s entry for <paramref name="tag"/>, then a read, as one act (G16).</summary>
    public async Task<Outcome> Click(ItemMenuAction list, string tag)
    {
        // The click and the reading after it are one act, so what either names is the tag's (G16).
        using var acting = UserAct.Begin(Organising.TagAct);
        var clicked = present;
        var outcome = await TagLists.Click(organising, list, clicked, tag, list == ItemMenuAction.RemoveTag ? remove : add);
        await Read();
        return outcome;
    }

    private static IReadOnlyList<TagCount> CarriedAsListed(Listing listing, IReadOnlyList<StashItem> items)
    {
        var listed = TagLists.Carried(listing.Tags, items);
        var unlisted = Organising.CarriedTags(items).Where(tag => !listed.Any(count => StringComparer.OrdinalIgnoreCase.Equals(count.Tag, tag)));
        return [.. listed, .. unlisted.Select(tag => new TagCount(tag, 0))];
    }
}
