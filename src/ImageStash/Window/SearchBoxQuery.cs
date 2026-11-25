using Avalonia.Controls;

namespace ImageStash.Window;

internal static class SearchBoxQuery
{
    public static void Wire(TextBox search, Listing listing, SummonSelection selection)
    {
        selection.Follow(search);
        listing.QueryChangedByWindow += () => UpdateText(search, listing.Query, selection);

        // TextChanging runs within the edit. By the posted TextChanged event an import may have reset the query.
        search.TextChanging += (_, _) =>
        {
            var text = search.Text ?? "";
            if (text != listing.Query)
            {
                _ = listing.SetQuery(text);
            }
        };
    }

    private static void UpdateText(TextBox search, string query, SummonSelection selection)
    {
        var before = search.Text ?? "";
        if (query.Length == 0)
        {
            // Clearing the box for a scope choice ends the old selection even when it was already empty.
            search.Text = query;
            selection.Changed();
            return;
        }

        if (selection.Summons)
        {
            search.Text = query;
            search.SelectAll();
            selection.Made();
            return;
        }

        var positioned = selection.PositionedByPerson;
        var (start, end) = KeepSelection(before, query, search.SelectionStart, search.SelectionEnd, positioned);
        search.Text = query;
        search.CaretIndex = end;
        search.SelectionStart = start;
        search.SelectionEnd = end;
        if (positioned)
        {
            selection.MovedByPerson();
        }
    }

    private static (int Start, int End) KeepSelection(string before, string after, int start, int end, bool positioned)
    {
        if (!positioned && start == end && end == before.Length)
        {
            return (after.Length, after.Length);
        }

        var prefix = 0;
        var shared = Math.Min(before.Length, after.Length);
        while (prefix < shared && before[prefix] == after[prefix])
        {
            prefix++;
        }

        if (Math.Max(start, end) <= prefix)
        {
            return (start, end);
        }

        var suffix = 0;
        while (suffix < shared - prefix && before[^(suffix + 1)] == after[^(suffix + 1)])
        {
            suffix++;
        }

        if (Math.Min(start, end) >= before.Length - suffix)
        {
            var difference = after.Length - before.Length;
            return (start + difference, end + difference);
        }

        // Removing a chip can remove several words at once. A selection between them still has its original words.
        var shift = ShiftOfRetainedWords(before, after, Math.Min(start, end), Math.Max(start, end));
        return shift is { } offset ? (start + offset, end + offset) : (prefix, prefix);
    }

    private static int? ShiftOfRetainedWords(string before, string after, int start, int end)
    {
        var firstWord = start == 0 ? 0 : before.LastIndexOf(' ', start - 1) + 1;
        var pastLastWord = before.IndexOf(' ', end);
        var lastWordEnd = pastLastWord < 0 ? before.Length : pastLastWord;
        while (firstWord < lastWordEnd && before[firstWord] == ' ')
        {
            firstWord++;
        }

        var retained = before[firstWord..lastWordEnd].TrimEnd(' ');
        if (retained.Length == 0)
        {
            return null;
        }

        var words = $" {retained} ";
        var oldOccurrences = WordOccurrences(before, words).ToArray();
        var newOccurrences = WordOccurrences(after, words).ToArray();
        var selected = Array.IndexOf(oldOccurrences, firstWord);
        if (selected < 0 || oldOccurrences.Length != newOccurrences.Length)
        {
            return null;
        }

        var shift = newOccurrences[selected] - firstWord;
        // Chip removal also normalizes spacing. Keep only an exact surviving selection at the mapped position.
        return start + shift >= 0 && end + shift <= after.Length &&
            before.AsSpan(start, end - start).SequenceEqual(after.AsSpan(start + shift, end - start)) ? shift : null;
    }

    private static IEnumerable<int> WordOccurrences(string query, string words)
    {
        var padded = $" {query} ";
        for (var at = padded.IndexOf(words, StringComparison.Ordinal); at >= 0; at = padded.IndexOf(words, at + 1, StringComparison.Ordinal))
        {
            yield return at;
        }
    }
}
