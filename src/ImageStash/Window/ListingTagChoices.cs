using ImageStash.Store;

namespace ImageStash.Window;

public sealed partial class Listing
{
    private (string Query, Task<Loaded?> Result)? searchRead;

    internal bool TagChoicePending => !order.Tags.PendingChoice.IsCompleted;

    internal int TagChoiceRevision => order.Tags.Revision;

    // The held chip order can use its last read. Every tag needs the current store version.
    internal bool PickerTagsStale => store is { } opened && tagsVersion != opened.ListingVersion;

    internal bool PickerTagsNeedRetry => loadFailed && PickerTagsStale;

    internal TagCount? LiveTag(string tag) => order.Tags.LiveTag(tag);

    internal bool CanFilterTag(string tag, bool searching, bool lit) =>
        order.Tags.FilterAvailability(tag, Query, searching, lit) ?? !Settled;

    private void ApplyListingTags(Outcome<IReadOnlyList<TagCount>> counted, (long Commits, long FileSteps) version,
        string query, bool searching, bool itemsLoaded)
    {
        if (counted.Value is { } currentTags)
        {
            tagsVersion = version;
            order.Tags.Counted(currentTags);
        }

        Chips = order.Tags.Shown(query, searching, rank: itemsLoaded && counted.Succeeded);
    }

    /// <summary>Chooses a visible chip or a live tag from Every tag. A lit chip can be removed while its read is pending.</summary>
    internal Task ChooseTag(string tag, bool fromPicker = false)
    {
        var chips = order.Tags;
        var revision = chips.Revision;
        var lit = LitTags.Contains(tag, StringComparer.OrdinalIgnoreCase);
        if (lit || !(TagChoicePending || (!fromPicker && IsSearch && !Settled)))
        {
            return ApplyTagChoice(tag, chips, revision, fromPicker, pending: null);
        }

        var available = fromPicker ? true : chips.FilterAvailability(tag, Query, IsSearch, lit: false);
        if (available == false)
        {
            return Task.CompletedTask;
        }

        var earlierRead = available is null && searchRead is { } read && read.Query == Query ? read.Result : null;
        var fragment = ItemSearch.UnfinishedLastWord(Query, Tags.Select(count => count.Tag));
        var pending = new PendingTagChoice(NextInput(), earlierRead, fragment);
        return chips.PendingChoice = ChooseTagAfterRead(chips.PendingChoice, tag, chips, revision, fromPicker, pending);
    }

    private async Task ChooseTagAfterRead(Task earlier, string tag, TagChipOrder chips, int revision, bool fromPicker, PendingTagChoice pending)
    {
        // A lit chip can be removed immediately. A queued choice still needs a carrier in the search it was clicked on.
        await earlier;
        if (pending.EarlierRead is not null
            && ((await pending.EarlierRead)?.Items.Value is not { } matches
                || !matches.Any(item => item.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase))))
        {
            return;
        }

        while (!Settled && chips == order.Tags && revision == chips.Revision)
        {
            await SettleRead();
        }

        await ApplyTagChoice(tag, chips, revision, fromPicker, pending);
    }

    private Task ApplyTagChoice(string tag, TagChipOrder chips, int revision, bool fromPicker, PendingTagChoice? pending)
    {
        using var acting = UserAct.Begin(SearchAct);
        lock (gate)
        {
            if (chips != order.Tags || revision != chips.Revision)
            {
                return Task.CompletedTask;
            }

            if (chips.LiveTag(tag) is not { } live)
            {
                if (fromPicker)
                {
                    failed(new Failure(SearchAct, $"#{tag}", "no item carries this tag any more"));
                }

                return Task.CompletedTask;
            }

            var lit = LitTags.Contains(live.Tag);
            if (!fromPicker && (chips.FilterAvailability(tag, Query, IsSearch, lit) != true
                || (pending is not null && !Chips.Any(place => string.Equals(place.Tag, tag, StringComparison.OrdinalIgnoreCase)))))
            {
                return Task.CompletedTask;
            }

            var fragment = ItemSearch.UnfinishedLastWord(Query, Tags.Select(count => count.Tag));
            if (pending is not null && !string.Equals(fragment, pending.Fragment, StringComparison.OrdinalIgnoreCase))
            {
                // An immediate removal must not expose a new word for an earlier click to complete.
                fragment = null;
            }

            Query = lit ? ItemSearch.WithoutWord(Query, live.Tag) : ItemSearch.WithWord(Query, live.Tag, fragment);
            chips.Chosen(Chips);
            if (!IsSearch)
            {
                // Publish the arrivals before a later click can supersede this scope read.
                Chips = chips.Shown(Query, searching: false);
            }
        }

        CountChange(true, pending?.Input);
        QueryChangedByWindow?.Invoke();
        return Load(keepSelection: false, showPendingQuery: true, selectionAt: pending?.Input);
    }

    private sealed record PendingTagChoice(long Input, Task<Loaded?>? EarlierRead, string? Fragment);
}
