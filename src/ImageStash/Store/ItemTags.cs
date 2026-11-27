using System.Collections.Immutable;
using System.Text;

namespace ImageStash.Store;

/// <summary>A tag and how many items the stash lists carrying it.</summary>
public sealed record TagCount(string Tag, int Items);

/// <summary>The count at the write. Only an applied removal carries an Undo receipt.</summary>
internal sealed record TagRemoval(TagCount Count, ItemFactResult? Receipt);

/// <summary>
/// The resulting spelling, how many listed source carriers changed, and whether the destination was live; a rename to a
/// different word also carries its Undo receipt, each carrier's tags as they were before it (G17).
/// </summary>
internal sealed record TagRename(string Tag, int Items, bool Merged, ItemFactResult? Receipt = null);

/// <summary>
/// The tag rule, in one place (G17, In use: "Tags are search words"): a tag is one word of the typed text, every leading
/// <c>#</c> removed, stored as typed and compared ignoring case for every script, so one tag has one spelling across the
/// stash, the one first stored or set by a spelling correction. The <c>tags</c> table is the record: a tag exists only
/// while a listed item carries it.
/// </summary>
internal static class ItemTags
{
    public const string AddAction = "Add a tag";
    public const string RemoveAction = "Remove a tag";
    private const string UndoAddAction = "Undo adding a tag";
    private const string UndoRemoveAction = "Undo removing a tag";
    private const string UndoRenameAction = "Undo renaming a tag";

    private const string CountAction = "Count the tags";
    public const string RemoveFromAllAction = "Remove tag from all items";
    public const string RenameAction = "Rename tag";
    private static readonly UTF8Encoding StoredText = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>
    /// The tags <paramref name="text"/> commits, one per word, in order: split on spaces, every leading <c>#</c>
    /// removed, words left empty dropped. Unlike a search word, which drops one <c>#</c> (G5), no tag begins with one,
    /// or its chip's query could never be exactly it.
    /// </summary>
    public static IReadOnlyList<string> Words(string text) =>
        text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(word => word.TrimStart('#'))
            .Where(word => word.Length > 0)
            .ToList();

    /// <summary>
    /// Gives each listed item the tags <paramref name="text"/> commits; no words change nothing (G17: nothing requires
    /// a tag). An item already carrying a tag is left as it is.
    /// </summary>
    public static ItemFactResult Add(StoreContext store, IReadOnlyList<StashItem> items, string text) =>
        Add(store, items, Words(text));

    /// <summary>Assigns a picked or dropped tag only while it still has a carrier at the tagging write (G17).</summary>
    public static ItemFactResult AddExisting(StoreContext store, IReadOnlyList<StashItem> items, string tag) =>
        Add(store, items, [tag], () => ListedRows(store, TagRows(store)).Any(row => SameWord(row.Tag, tag))
            ? null : NoCarrier(tag));

    /// <summary>Whether every item carries the tag, as both the menu's mark and its toggle decide.</summary>
    public static bool AllCarry(IReadOnlyList<StashItem> items, string tag) =>
        items.Count > 0 && items.All(item => item.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase));

    /// <summary>Whether any item carries the tag, as Remove tag ▸ lists it and its click decides (G17).</summary>
    public static bool AnyCarry(IReadOnlyList<StashItem> items, string tag) =>
        items.Any(item => item.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase));

    /// <summary>
    /// Takes the tag off the items while every one carries it, or with <paramref name="whileAnyCarries"/> while any does
    /// (Remove tag ▸, G17), as the store holds them at the write; else gives it to each, while it still has a carrier.
    /// </summary>
    public static ItemFactResult ToggleExisting(StoreContext store, IReadOnlyList<StashItem> items, string tag, bool whileAnyCarries = false)
    {
        bool Removing(IReadOnlyList<StashItem> carriers) =>
            whileAnyCarries ? AnyCarry(carriers, tag) : AllCarry(carriers, tag);

        // Read at the write's turn. In particular, an earlier duplicate import can still be comparing its files.
        var current = ToggleTargets(store, items);
        if (current.Failure is { } failure)
        {
            var removing = Removing(items);
            var refusal = failure with { Action = removing ? RemoveAction : AddAction, Subject = $"#{tag}" };
            return new ItemFactResult(store, removing ? UndoRemoveAction : UndoAddAction,
                [.. items.Select(_ => Outcome.Fail(refusal))], []);
        }

        return Removing(current.Value!) ? Remove(store, items, tag) : AddExisting(store, items, tag);
    }

    private static Outcome<IReadOnlyList<StashItem>> ToggleTargets(StoreContext store, IReadOnlyList<StashItem> items)
    {
        if (items.Count == 1)
        {
            var single = ItemQueries.Item(store, items[0].Id);
            return single.Succeeded
                ? Outcome<IReadOnlyList<StashItem>>.Of(single.Value is { } item ? [item] : [])
                : single.Failure;
        }

        var all = ItemQueries.All(store);
        if (!all.Succeeded)
        {
            return all.Failure;
        }

        var ids = items.Select(item => item.Id).ToHashSet();
        return Outcome<IReadOnlyList<StashItem>>.Of(all.Value!.Where(item => ids.Contains(item.Id)).ToList());
    }

    private static ItemFactResult Add(StoreContext store, IReadOnlyList<StashItem> items, IReadOnlyList<string> words, Func<string?>? refusedBecause = null)
    {
        if (words.Count == 0)
        {
            return new ItemFactResult(store, UndoAddAction, [.. items.Select(_ => Outcome.Done)], []);
        }

        return ItemFactChange.Apply(store, AddAction, UndoAddAction, items, listed =>
        {
            var tags = Spelled(store, words);
            return [.. listed.Select(item => ChangeMemberships(store, item, tags, [])).OfType<ItemFactDelta>()];
        }, refusedBecause);
    }

    /// <summary>Takes the tag, in any spelling, off each listed item; an item not carrying it is left as it is.</summary>
    public static ItemFactResult Remove(StoreContext store, IReadOnlyList<StashItem> items, string tag) =>
        ItemFactChange.Apply(store, RemoveAction, UndoRemoveAction, items, listed =>
            [.. listed.Select(item => ChangeMemberships(store, item, [], [tag])).OfType<ItemFactDelta>()]);

    public static Outcome<TagRemoval> RemoveFromAll(StoreContext store, string tag, int expectedItems)
    {
        var removed = RemoveOfferedCarriers(store, tag, expectedItems);
        return removed.Failure is { } failure ? failure with { Subject = $"#{tag}" } : removed;
    }

    private static Outcome<TagRemoval> RemoveOfferedCarriers(StoreContext store, string tag, int expectedItems) =>
        Write<TagRemoval>(store, RemoveFromAllAction, () =>
        {
            var carriers = Carriers(ListedRows(store, TagRows(store)), tag);
            var count = new TagCount(carriers.FirstOrDefault()?.Tag ?? tag, carriers.Count);
            if (count.Items != expectedItems)
            {
                return (Outcome<TagRemoval>.Of(new TagRemoval(count, null)), false);
            }

            if (count.Items == 0)
            {
                return (Failure.Refusal(RemoveFromAllAction, $"#{tag}", NoCarrier(tag)), false);
            }

            var changes = carriers.Select(row => ChangeMemberships(store, row.Item, [], [tag]))
                .OfType<ItemFactDelta>().ToImmutableArray();
            var receipt = new ItemFactResult(store, UndoRemoveAction, [.. changes.Select(_ => Outcome.Done)], changes);
            return (Outcome<TagRemoval>.Of(new TagRemoval(count, receipt)), !changes.IsEmpty);
        });

    public static Outcome<TagRename> Rename(StoreContext store, string tag, string text)
    {
        var words = Words(text);
        if (words.Count != 1)
        {
            return Failure.Refusal(RenameAction, $"#{tag}", "enter one tag word");
        }

        var requested = words[0];
        if (requested.Contains('\0'))
        {
            return Failure.Refusal(RenameAction, $"#{tag}", "the new tag contains a null character");
        }

        try
        {
            // SQLite's text binding can replace a lone surrogate and write a different word from the one requested.
            StoredText.GetByteCount(requested);
        }
        catch (EncoderFallbackException)
        {
            return Failure.Refusal(RenameAction, $"#{tag}", "the new tag contains invalid Unicode");
        }

        return Write<TagRename>(store, RenameAction, () =>
        {
            var correctingSpelling = SameWord(tag, requested);
            var rows = TagRows(store, includeDeleted: true);
            var listed = ListedRows(store, rows).ToList();
            var carriers = Carriers(listed, tag);
            if (carriers.Count == 0)
            {
                return (Failure.Refusal(RenameAction, $"#{tag}", NoCarrier(tag)), false);
            }

            if (correctingSpelling)
            {
                var wrote = CorrectSpelling(store, tag, requested, rows);
                var changedItems = Carriers(listed.Where(row => row.Tag != requested), tag).Count;
                var recased = new TagRename(requested, changedItems, false);
                return (Outcome<TagRename>.Of(recased), wrote);
            }

            // If older rows disagree, keep the spelling the tag list shows.
            var liveDestination = listed.FirstOrDefault(row => SameWord(row.Tag, requested));
            var destination = liveDestination?.Tag ?? Spellings(store, [requested])[requested];
            CorrectSpelling(store, requested, destination, rows);
            var changes = carriers.Select(carrier => ChangeMemberships(store, carrier.Item, [destination], [tag]))
                .OfType<ItemFactDelta>().ToImmutableArray();
            var receipt = new ItemFactResult(store, UndoRenameAction, [.. changes.Select(_ => Outcome.Done)], changes);
            var renamed = new TagRename(destination, carriers.Count, liveDestination is not null, receipt);
            return (Outcome<TagRename>.Of(renamed), true);
        });
    }

    private static bool CorrectSpelling(StoreContext store, string tag, string requested, List<TagMembership> rows)
    {
        // Delete Undo reveals its held row, so the same word must have the corrected spelling there too.
        var spellings = rows.Select(row => row.Tag).Where(word => SameWord(word, tag) && word != requested).Distinct();
        var wrote = false;
        foreach (var spelling in spellings)
        {
            // An older row can already carry the destination spelling, so keep that membership.
            wrote |= store.Execute(
                """
                DELETE FROM tags
                WHERE tag = $old AND item_id IN (SELECT item_id FROM tags WHERE tag = $new)
                """,
                ("$new", requested), ("$old", spelling)) > 0;
            wrote |= store.Execute("UPDATE tags SET tag = $new WHERE tag = $old", ("$new", requested), ("$old", spelling)) > 0;
        }

        return wrote;
    }

    private static Outcome<T> Write<T>(StoreContext store, string action, Func<(Outcome<T> Result, bool Wrote)> change)
    {
        var committed = store.Change(action, change, result => result.Wrote);
        return committed.Succeeded ? committed.Value.Result : committed.Failure;
    }

    private static List<TagMembership> Carriers(IEnumerable<TagMembership> listed, string tag) =>
        listed.Where(row => SameWord(row.Tag, tag)).DistinctBy(row => row.Id).ToList();

    private static bool SameWord(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static string NoCarrier(string tag) => $"no item carries #{tag} any more";

    /// <summary>Changes the requested memberships and reports just the gains and losses in this transaction.</summary>
    public static ItemFactDelta? ChangeMemberships(
        StoreContext store, ItemFactTarget item, IReadOnlyList<string> give, IReadOnlyList<string> take)
    {
        var carried = Carried(store, item.Id);
        var removed = RemoveMatching(store, item.Id, take, carried);
        var added = InsertMissing(store, item.Id, give, carried);
        return added.IsEmpty && removed.IsEmpty ? null : new ItemFactDelta(item, added, removed, null);
    }

    /// <summary>
    /// Writes <paramref name="tags"/> on a row inside the caller's transaction (G28): a new row's, in the transaction
    /// that inserts it (an import's lit tags, G11; Save as new item's copied tags, G26), or the row of the item an
    /// import's file already was in the stash as (G11). Returns the tags the item gained, as written: those it did not
    /// carry yet, which for a new row is every one. The tags pass through <see cref="Words"/> and the one spelling as any
    /// other tag does.
    /// </summary>
    public static IReadOnlyList<string> AddMissing(StoreContext store, long id, IReadOnlyList<string> tags)
    {
        var words = Words(string.Join(' ', tags));
        if (words.Count == 0)
        {
            return [];
        }

        return InsertMissing(store, id, Spelled(store, words), Carried(store, id));
    }

    /// <summary>
    /// An import's Undo of the tags it gave items already in the stash (G12), inside the caller's transaction (G28):
    /// each tag taken off its item if the item still carries it, using the same case-insensitive identity as adding it.
    /// </summary>
    public static void TakeBackImported(StoreContext store, IReadOnlyList<(long Item, IReadOnlyList<string> Tags)> given)
    {
        foreach (var (id, tags) in given)
        {
            RemoveMatching(store, id, tags, Carried(store, id));
        }
    }

    /// <summary>
    /// Every tag a listed item carries, with how many such items carry it, by count and then by spelling ignoring case,
    /// so chips with equal counts keep their places between loads. A tag carried only by rows marked deleted, or by rows
    /// the stash does not list, is not among them (G17: it ceases to exist). One statement over the stash (E4).
    /// </summary>
    public static Outcome<IReadOnlyList<TagCount>> Counts(StoreContext store)
    {
        var rows = store.Read(CountAction, () => TagRows(store));
        if (!rows.Succeeded)
        {
            return rows.Failure;
        }

        var carriers = new Dictionary<string, (string Spelling, HashSet<long> Items)>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in ListedRows(store, rows.Value!))
        {
            if (!carriers.TryGetValue(row.Tag, out var carrier))
            {
                carriers[row.Tag] = carrier = (row.Tag, []);
            }

            carrier.Items.Add(row.Id);
        }

        return Outcome<IReadOnlyList<TagCount>>.Of(carriers.Values
            .Select(carrier => new TagCount(carrier.Spelling, carrier.Items.Count))
            .OrderByDescending(count => count.Items)
            .ThenBy(count => count.Tag, StringComparer.OrdinalIgnoreCase)
            .ToList());
    }

    private sealed record TagMembership(long Id, string Tag, string? PendingSteps, bool Deleted, string Name, string FileName)
    {
        public ItemFactTarget Item => new(Id, ItemQueries.Named(Name, FileName));
    }

    private static List<TagMembership> TagRows(StoreContext store, bool includeDeleted = false) => store.Query(
        $"""
        SELECT t.item_id, t.tag, i.pending_steps, i.deleted, i.name, i.finished_file
        FROM tags t JOIN items i ON i.id = t.item_id
        {(includeDeleted ? "" : $"WHERE {StashItem.NotDeleted}")}
        ORDER BY t.rowid
        """,
        r => new TagMembership(r.GetInt64(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2),
            r.GetBoolean(3), r.GetString(4), r.GetString(5)));

    private static IEnumerable<TagMembership> ListedRows(StoreContext store, IEnumerable<TagMembership> rows)
    {
        var listed = new Dictionary<long, bool>();
        foreach (var row in rows)
        {
            if (!listed.TryGetValue(row.Id, out var isListed))
            {
                listed[row.Id] = isListed = !row.Deleted && StashItem.IsListed(store.Folder, row.PendingSteps);
            }

            if (isListed)
            {
                yield return row;
            }
        }
    }

    /// <summary>
    /// Maps tag words to the first spelling stored across every row's tags, including rows marked deleted, or the first
    /// spelling proposed for a tag without a row. A Delete's Undo and a later tag assignment therefore share a spelling.
    /// Compared in code, ignoring case for every script, because SQLite's NOCASE folds ASCII only.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Spellings(StoreContext store, IReadOnlyList<string> words)
    {
        var spellings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (words.Count == 0)
        {
            return spellings;
        }

        foreach (var stored in StoredTags(store))
        {
            spellings.TryAdd(stored, stored);
        }

        foreach (var word in words)
        {
            spellings.TryAdd(word, word);
        }

        return spellings;
    }

    private static IReadOnlyList<string> Spelled(StoreContext store, IReadOnlyList<string> words)
    {
        var spellings = Spellings(store, words);
        return words.Select(word => spellings[word]).Distinct().ToList();
    }

    /// <summary>Every spelling in <c>tags</c>, marked rows included, the first stored first.</summary>
    private static List<string> StoredTags(StoreContext store) =>
        store.Query("SELECT tag FROM tags GROUP BY tag ORDER BY MIN(rowid)", r => r.GetString(0));

    private static List<string> Carried(StoreContext store, long id) =>
        store.Query("SELECT tag FROM tags WHERE item_id = $id", r => r.GetString(0), ("$id", id));

    private static bool Delete(StoreContext store, long id, string spelling) =>
        store.Execute("DELETE FROM tags WHERE item_id = $id AND tag = $tag", ("$id", id), ("$tag", spelling)) > 0;

    private static ImmutableArray<string> RemoveMatching(StoreContext store, long id, IReadOnlyList<string> tags, IReadOnlyList<string> carried) =>
        [.. carried.Where(tag => tags.Contains(tag, StringComparer.OrdinalIgnoreCase) && Delete(store, id, tag))];

    private static ImmutableArray<string> InsertMissing(StoreContext store, long id, IReadOnlyList<string> tags, IReadOnlyList<string> carried)
    {
        if (tags.Count == 0)
        {
            return [];
        }

        var existing = carried.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var gained = ImmutableArray.CreateBuilder<string>();
        foreach (var tag in tags)
        {
            if (existing.Add(tag) &&
                store.Execute("INSERT OR IGNORE INTO tags (item_id, tag) VALUES ($id, $tag)", ("$id", id), ("$tag", tag)) > 0)
            {
                gained.Add(tag);
            }
        }

        return gained.ToImmutable();
    }
}
