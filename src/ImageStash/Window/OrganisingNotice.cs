using ImageStash.Store;

namespace ImageStash.Window;

internal static class OrganisingNotice
{
    public static string? For(ItemFactResult result, int targets, string? alreadyOnChip)
    {
        var changes = result.Changes;
        if (changes.IsEmpty)
        {
            var accepted = result.Outcomes.Count(outcome => outcome.Succeeded);
            return alreadyOnChip is not null && accepted > 0 ? $"{Items(accepted)} {alreadyOnChip}" : null;
        }

        if (targets < 2 && alreadyOnChip is null)
        {
            return null;
        }

        var items = Items(changes.Length);
        var first = changes[0];
        if (first.PreviousFavorite is { } favorite)
        {
            return favorite ? $"Removed {items} from Favorites" : $"Added {items} to Favorites";
        }

        if (!first.RemovedTags.IsEmpty)
        {
            return $"Removed #{first.RemovedTags[0]} from {items}";
        }

        var added = changes.SelectMany(change => change.AddedTags).GroupBy(tag => tag).ToArray();
        if (added.All(tag => tag.Count() == changes.Length))
        {
            return $"Tagged {items} {string.Join(" ", added.Select(tag => "#" + tag.Key))}";
        }

        return $"Updated tags on {items} · {string.Join(", ", added.Select(tag => $"#{tag.Key} added to {Items(tag.Count())}"))}";
    }

    private static string Items(int count) => count == 1 ? "1 item" : $"{count} items";
}
