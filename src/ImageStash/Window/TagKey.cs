using ImageStash.Store;

namespace ImageStash.Window;

/// <summary>
/// <c>Ctrl+T</c>'s act (G7, G17). From the search box or the grid it opens the tag field ("Tags of …") on the selection
/// as the key is dispatched, after any arrow still waiting for what was typed (<see cref="PendingQueryKeys"/>), never on
/// the focused item alone, so Enter there tags every selected item; with nothing selected, or with the field already
/// open, it does nothing. In the preview it moves focus to the preview's own tag box, whatever the grid beneath selects
/// (a rename can end the search's match while the preview still shows its item), and the "Tags of …" field never opens
/// over it. The item menu's Add tags… opens the same field on the items the menu was opened on.
/// </summary>
/// <param name="previewOpen">Whether the preview is open.</param>
/// <param name="focusPreviewTags">Moves focus to the preview's tag box.</param>
/// <param name="fieldOpen">Whether the "Tags of …" field is open.</param>
/// <param name="openField">Opens the "Tags of …" field on the items given, with focus in its box.</param>
public sealed class TagKey(Listing listing, Func<bool> previewOpen, Action focusPreviewTags, Func<bool> fieldOpen, Action<IReadOnlyList<StashItem>> openField)
{
    /// <summary>The act: the items the "Tags of …" field opened on, or none when it opened on none.</summary>
    public IReadOnlyList<StashItem> Press()
    {
        if (previewOpen())
        {
            focusPreviewTags();
            return [];
        }

        // Taken now, before anything waits, so an item selected later is never given the words (G17).
        var items = listing.SelectedItems;
        if (fieldOpen() || items.Count == 0)
        {
            return [];
        }

        openField(items);
        return items;
    }
}
