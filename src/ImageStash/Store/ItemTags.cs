namespace ImageStash.Store;

/// <summary>A tag and how many items the stash lists carrying it.</summary>
public sealed record TagCount(string Tag, int Items);

/// <summary>
/// The tag rule, in one place (G17, In use: "Tags are search words"): a tag is one word of the typed text, every leading
/// <c>#</c> removed, stored as typed and compared ignoring case for every script, so one tag has one spelling across the
/// stash, the one first stored. The <c>tags</c> table is the record: a tag exists only while a listed item carries it.
/// </summary>
internal static class ItemTags
{
    public const string AddAction = "Add a tag";
    public const string RemoveAction = "Remove a tag";

    private const string CountAction = "Count the tags";

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
    public static IReadOnlyList<Outcome> Add(StoreContext store, IReadOnlyList<StashItem> items, string text)
    {
        var words = Words(text);
        if (words.Count == 0)
        {
            return items.Select(_ => Outcome.Done).ToList();
        }

        return ItemFactChange.Apply(store, AddAction, items, listed =>
        {
            var tags = Spelled(store, words);
            foreach (var id in listed)
            {
                Insert(store, id, tags);
            }
        });
    }

    /// <summary>Takes the tag, in any spelling, off each listed item; an item not carrying it is left as it is.</summary>
    public static IReadOnlyList<Outcome> Remove(StoreContext store, IReadOnlyList<StashItem> items, string tag) =>
        ItemFactChange.Apply(store, RemoveAction, items, listed =>
        {
            var spellings = StoredTags(store).Where(stored => string.Equals(stored, tag, StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var id in listed)
            {
                foreach (var spelling in spellings)
                {
                    store.Execute("DELETE FROM tags WHERE item_id = $id AND tag = $tag", ("$id", id), ("$tag", spelling));
                }
            }
        });

    /// <summary>
    /// Writes an import's tags on its new row, inside the transaction that inserts it (G11, G28), and returns them as
    /// written; the tags pass through <see cref="Words"/> and the one spelling as any other tag does.
    /// </summary>
    public static IReadOnlyList<string> AddToNewItem(StoreContext store, long id, IReadOnlyList<string> tags)
    {
        var words = Words(string.Join(' ', tags));
        if (words.Count == 0)
        {
            return [];
        }

        var spelled = Spelled(store, words);
        Insert(store, id, spelled);
        return spelled;
    }

    /// <summary>
    /// Every tag a listed item carries, with how many such items carry it, by count and then by spelling ignoring case,
    /// so chips with equal counts keep their places between loads. A tag carried only by rows marked deleted, or by rows
    /// the stash does not list, is not among them (G17: it ceases to exist). One statement over the stash (E4).
    /// </summary>
    public static Outcome<IReadOnlyList<TagCount>> Counts(StoreContext store)
    {
        var rows = store.Read(CountAction, () => store.Query(
            $"SELECT t.item_id, t.tag, i.pending_steps FROM tags t JOIN items i ON i.id = t.item_id WHERE {StashItem.NotDeleted} ORDER BY t.rowid",
            r => (Id: r.GetInt64(0), Tag: r.GetString(1), PendingSteps: r.IsDBNull(2) ? null : r.GetString(2))));
        if (!rows.Succeeded)
        {
            return rows.Failure;
        }

        var listed = new Dictionary<long, bool>();
        var carriers = new Dictionary<string, (string Spelling, HashSet<long> Items)>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, tag, pendingSteps) in rows.Value!)
        {
            if (!listed.TryGetValue(id, out var isListed))
            {
                listed[id] = isListed = StashItem.IsListed(store.Folder, pendingSteps);
            }

            if (isListed)
            {
                if (!carriers.TryGetValue(tag, out var carrier))
                {
                    carriers[tag] = carrier = (tag, []);
                }

                carrier.Items.Add(id);
            }
        }

        return Outcome<IReadOnlyList<TagCount>>.Of(carriers.Values
            .Select(carrier => new TagCount(carrier.Spelling, carrier.Items.Count))
            .OrderByDescending(count => count.Items)
            .ThenBy(count => count.Tag, StringComparer.OrdinalIgnoreCase)
            .ToList());
    }

    /// <summary>
    /// The words as the tags they are: each in the spelling first stored for it across every row's tags, those of rows
    /// marked deleted included, so an item a Delete's Undo brings back and one tagged meanwhile share one; else as typed.
    /// Compared in code, ignoring case for every script, because SQLite's NOCASE folds ASCII only.
    /// </summary>
    private static IReadOnlyList<string> Spelled(StoreContext store, IReadOnlyList<string> words)
    {
        var spellings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var stored in StoredTags(store))
        {
            spellings.TryAdd(stored, stored);
        }

        return words.Select(word => spellings.TryAdd(word, word) ? word : spellings[word]).Distinct().ToList();
    }

    /// <summary>Every spelling in <c>tags</c>, marked rows included, the first stored first.</summary>
    private static List<string> StoredTags(StoreContext store) =>
        store.Query("SELECT tag FROM tags GROUP BY tag ORDER BY MIN(rowid)", r => r.GetString(0));

    private static void Insert(StoreContext store, long id, IReadOnlyList<string> tags)
    {
        foreach (var tag in tags)
        {
            store.Execute("INSERT OR IGNORE INTO tags (item_id, tag) VALUES ($id, $tag)", ("$id", id), ("$tag", tag));
        }
    }
}
