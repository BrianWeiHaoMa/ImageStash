namespace ImageStash.Store;

/// <summary>
/// G5's search rule, in one place: the query is split on spaces, a leading <c>#</c> is ignored on each word, and every
/// word must be a case-insensitive substring of the name, one tag or one layer's saved text. The G4 priority group
/// requires every word to start a word in the name or a tag. The chip's operations on the query live here too (G4, G5).
/// </summary>
public static class ItemSearch
{
    /// <summary>The words of <paramref name="query"/>; none means the query is not a search.</summary>
    public static IReadOnlyList<string> Words(string query) => Typed(query).Select(Bare).Where(word => word.Length > 0).ToList();

    public static bool IsSearch(string query) => Words(query).Count > 0;

    public static bool Matches(StashItem item, IReadOnlyList<string> words) => Matches(item, words, []);

    internal static bool Matches(StashItem item, IReadOnlyList<string> words, IReadOnlyList<string> layerText) =>
        words.All(word => InNameOrTag(item, word, Contains) || layerText.Any(text => Contains(text, word)));

    /// <summary>
    /// Whether every one of <paramref name="words"/>, a query's <see cref="Words"/>, starts a word of the item's name or
    /// of one of its tags, ignoring case as <see cref="Matches"/> does: the matches G4 lists first. A word of a text
    /// starts at the text's start and after a space, <c>-</c>, <c>_</c> or <c>.</c>, so in
    /// <c>Screenshot 2026-11-22 10.30.00</c> <c>2026</c> and <c>30</c> start one and <c>re</c> does not.
    /// </summary>
    public static bool MatchesAtWordStarts(StashItem item, IReadOnlyList<string> words) =>
        words.All(word => InNameOrTag(item, word, StartsAWord));

    /// <summary>
    /// The tags that are words of the query, the lit chips (G5): of <paramref name="tags"/>, as stored, those equal,
    /// ignoring case, to a word of it, in the order their words have in it, each once.
    /// </summary>
    public static IReadOnlyList<string> LitTags(string query, IEnumerable<string> tags)
    {
        var words = Words(query);
        if (words.Count == 0)
        {
            return [];
        }

        var spelled = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tag in tags)
        {
            spelled.TryAdd(tag, tag);
        }

        return words.Select(word => spelled.GetValueOrDefault(word)).OfType<string>().Distinct().ToList();
    }

    /// <summary>The final word being typed, without its leading #, while it has no following space and is not an existing tag.</summary>
    internal static string? UnfinishedLastWord(string query, IEnumerable<string> tags)
    {
        var word = Bare(query[(query.LastIndexOf(' ') + 1)..]);
        return word.Length > 0 && !tags.Contains(word, StringComparer.OrdinalIgnoreCase) ? word : null;
    }

    /// <summary>
    /// The query with the tag completing the unfinished final word when it contains it, or appended as a new word (G4).
    /// </summary>
    public static string WithWord(string query, string tag, IEnumerable<string> tags) =>
        WithWord(query, tag, UnfinishedLastWord(query, tags));

    internal static string WithWord(string query, string tag, string? unfinished)
    {
        if (unfinished is not null && Contains(tag, unfinished))
        {
            return query[..(query.LastIndexOf(' ') + 1)] + tag;
        }

        var kept = query.TrimEnd(' ');
        return kept.Length == 0 ? tag : $"{kept} {tag}";
    }

    /// <summary>
    /// The query without every word equal to <paramref name="tag"/>, ignoring case and a leading <c>#</c>, and the
    /// remaining words joined by single spaces. Keep an explicit trailing space so removal cannot reopen a finished word.
    /// </summary>
    public static string WithoutWord(string query, string tag)
    {
        var remaining = string.Join(' ', Typed(query).Where(word => !string.Equals(Bare(word), tag, StringComparison.OrdinalIgnoreCase)));
        return remaining.Length > 0 && query.EndsWith(' ') ? remaining + " " : remaining;
    }

    /// <summary>
    /// A tag renamed (G17): each word of <paramref name="query"/> equal to <paramref name="from"/>, ignoring case and a
    /// leading <c>#</c>, becomes <paramref name="to"/>, its <c>#</c> kept; a word holding it inside, or any other word,
    /// stays as typed, and so do the spaces. Also each word replaced, as typed before, by its place among the words.
    /// </summary>
    public static (string Query, IReadOnlyDictionary<int, string> Replaced) WithRenamedTag(string query, string from, string to)
    {
        var words = query.Split(' ');
        var replaced = new Dictionary<int, string>();
        for (var place = 0; place < words.Length; place++)
        {
            if (string.Equals(Bare(words[place]), from, StringComparison.OrdinalIgnoreCase))
            {
                replaced[place] = words[place];
                words[place] = (words[place].StartsWith('#') ? "#" : "") + to;
            }
        }

        return (string.Join(' ', words), replaced);
    }

    /// <summary>
    /// A tag rename undone: each word <see cref="WithRenamedTag"/> replaced in <paramref name="renamed"/>, back as it was
    /// typed, where its place still holds <paramref name="to"/>; every other word, one the person typed since included,
    /// stays. A search whose words have since been added or taken away, so no place can be trusted, is left as it is.
    /// </summary>
    public static string WithoutRenamedTag(string query, string renamed, IReadOnlyDictionary<int, string> replaced, string to)
    {
        var words = query.Split(' ');
        if (words.Length != renamed.Split(' ').Length)
        {
            return query;
        }

        foreach (var (place, typed) in replaced)
        {
            if (place < words.Length && string.Equals(Bare(words[place]), to, StringComparison.OrdinalIgnoreCase))
            {
                words[place] = typed;
            }
        }

        return string.Join(' ', words);
    }

    private static string[] Typed(string query) => query.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    private static string Bare(string word) => word.StartsWith('#') ? word[1..] : word;

    internal static bool Contains(string text, string word) => text.Contains(word, StringComparison.OrdinalIgnoreCase);

    private static bool InNameOrTag(StashItem item, string word, Func<string, string, bool> holds) =>
        holds(item.Name, word) || item.Tags.Any(tag => holds(tag, word));

    // Every place the word lies is tried, not only the first: "Screenshot really" holds "re" mid-word before it starts one.
    private static bool StartsAWord(string text, string word)
    {
        for (var at = text.IndexOf(word, StringComparison.OrdinalIgnoreCase); at >= 0; at = text.IndexOf(word, at + 1, StringComparison.OrdinalIgnoreCase))
        {
            if (at == 0 || text[at - 1] is ' ' or '-' or '_' or '.')
            {
                return true;
            }
        }

        return false;
    }
}
