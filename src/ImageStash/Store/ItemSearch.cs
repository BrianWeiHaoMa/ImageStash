namespace ImageStash.Store;

/// <summary>
/// G5's search rule, in one place: the query is split on spaces, a leading <c>#</c> is ignored on each word, and every
/// word must be a substring of the name or of one tag, ignoring case for every script, not only ASCII. Tags are search
/// words (In use): the words a chip adds to the query and takes out of it, and which of the query's words are tags, are
/// here too (G4, G5).
/// </summary>
public static class ItemSearch
{
    /// <summary>The words of <paramref name="query"/>; none means the query is not a search.</summary>
    public static IReadOnlyList<string> Words(string query) => Typed(query).Select(Bare).Where(word => word.Length > 0).ToList();

    public static bool IsSearch(string query) => Words(query).Count > 0;

    public static bool Matches(StashItem item, IReadOnlyList<string> words) =>
        words.All(word => Contains(item.Name, word) || item.Tags.Any(tag => Contains(tag, word)));

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

    /// <summary>Whether <paramref name="tag"/> contains one of <paramref name="words"/>, a query's <see cref="Words"/>, ignoring case (G5).</summary>
    public static bool ContainsAWord(string tag, IReadOnlyList<string> words) => words.Any(word => Contains(tag, word));

    /// <summary>
    /// The query with <paramref name="tag"/> added as a word, as a chip's click adds it (G4): in place of the last word
    /// the tag contains, ignoring case and a leading <c>#</c>, the words then joined by single spaces, since that is the
    /// word being typed; else as its last word.
    /// </summary>
    public static string WithWord(string query, string tag)
    {
        var typed = Typed(query);
        var completed = Array.FindLastIndex(typed, word => Bare(word).Length > 0 && Contains(tag, Bare(word)));
        if (completed >= 0)
        {
            typed[completed] = tag;
            return string.Join(' ', typed);
        }

        var kept = query.TrimEnd(' ');
        return kept.Length == 0 ? tag : $"{kept} {tag}";
    }

    /// <summary>
    /// The query without every word equal to <paramref name="tag"/>, ignoring case and a leading <c>#</c>, and the
    /// remaining words as typed, joined by single spaces, as a lit chip's click leaves it (G4).
    /// </summary>
    public static string WithoutWord(string query, string tag) =>
        string.Join(' ', Typed(query).Where(word => !string.Equals(Bare(word), tag, StringComparison.OrdinalIgnoreCase)));

    private static string[] Typed(string query) => query.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    private static string Bare(string word) => word.StartsWith('#') ? word[1..] : word;

    private static bool Contains(string text, string word) => text.Contains(word, StringComparison.OrdinalIgnoreCase);
}
