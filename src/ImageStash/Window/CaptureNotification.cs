using ImageStash.Media;
using ImageStash.Seams;
using ImageStash.Store;

namespace ImageStash.Window;

/// <summary>What a capture was, for the words its notification says (G13, G16).</summary>
public enum CaptureKind
{
    StashedClipboardImage,
    Screenshot,
    Recording,
}

/// <summary>A card's Edit, or its Trim, clicked: the editor asked for on the card's own item, with the Trim tool for Trim (G16).</summary>
public sealed record CaptureEdit(StashItem Item, bool Trim);

/// <summary>
/// The capture notification (G16), every capture's one entry: a notice of the notifications above the tray, so its six
/// seconds, its hover, its × and its Undo at most once are the notices' own. It is raised only for an item Store has
/// committed and placed (R9); a capture that failed before or after its commit is named by its caller as a plain
/// failure instead. It says the item is in the stash, on the clipboard only when ImageStash put it there, a recording's
/// length, and for a stashed clipboard image G13's sentence; its Undo removes the item through Store, its file to the
/// Recycle Bin (G29), and records nothing else. It never reads the clipboard (N9, R5), and a rename in its field rewrites
/// it only through the handoff's guarded refresh, as a rename in the window does (G17, G26). It is drawn as the
/// capture card, with the picture, the file's size and the name as a field, renamed through a rename path of its own
/// whose refusals are named above the tray, never only in the window, which may be hidden (G35); its Edit, or Trim for
/// a recording, asks the window that follows it for the editor on its item, and the card stays, so its Undo can still
/// close that edit; a click elsewhere on it asks for the window on that item, and the card goes when the window then
/// shows the item.
/// </summary>
public sealed class CaptureNotification(Notices notifications, Listing listing, Handoff handoff, IQuietWindows quietWindows, TimeSpan heldFileRetry)
{
    // The clipboard follows a card's rename as it follows the window's, through the one handoff (G17, G26).
    private readonly Renaming renaming = new(listing, notifications, handoff);

    /// <summary>The cards' one rename path, its refusals named above the tray; a Move waits for it (State: Move).</summary>
    public Renaming Renaming => renaming;

    /// <summary>A click into a card's name field brought the notification in front: ImageStash gained focus (G8).</summary>
    public event Action? FieldTookFocus;

    /// <summary>A card's Edit or Trim was clicked, the notification brought in front by that click (G16).</summary>
    public event Action<CaptureEdit>? EditRequested;

    /// <summary>
    /// A card was clicked outside its field and buttons, the notification brought in front by that click (G16); the
    /// handler runs the action given with the item when the window will show that item, which dismisses the card.
    /// </summary>
    public event Action<StashItem, Action>? ShowRequested;

    /// <summary>
    /// The card's button for the editor (G16): Trim for a recording, a recording of one image included, since the card
    /// names the act and not the frames; Edit for the rest.
    /// </summary>
    public static string EditLabel(CaptureKind kind) => kind == CaptureKind.Recording ? "Trim" : "Edit";

    public void Raise(StashItem item, CaptureKind kind, bool onClipboard, long? lengthMs)
    {
        var said = new List<string>();
        if (onClipboard)
        {
            said.Add("It is on the clipboard too.");
        }

        if (lengthMs is { } length)
        {
            said.Add($"It is {ClipLength.Words(length)} long.");
        }

        if (kind == CaptureKind.StashedClipboardImage)
        {
            said.Add(Import.StillOnly);
        }

        // The notice's words name the item; its card shows the name in its field instead, so a rename shows there.
        var text = string.Join(" ", [$"Saved {Notices.Describe([item])} to your stash.", .. said]);
        var trim = kind == CaptureKind.Recording;
        notifications.Raise(text, "Undo", () => Undo(item.Id),
            card: (notice, notices) => new CaptureCard(notice, notices, item, string.Join(" ", said), listing, renaming, quietWindows, heldFileRetry,
                () => FieldTookFocus?.Invoke(), EditLabel(kind), shown => EditRequested?.Invoke(new CaptureEdit(shown, trim)), (shown, dismiss) => ShowRequested?.Invoke(shown, dismiss)));
    }

    /// <summary>
    /// The card's item shown selected, with no editor (G16): All, where every item is listed, the search cleared, as a
    /// notification's Edit and an import's end show it, <paramref name="closePreview"/> run first so the grid shows it (G12).
    /// An item gone from the stash meanwhile leaves the window as it was left, as the tray opens it.
    /// </summary>
    public async Task ShowOnAll(StashItem item, Action closePreview)
    {
        if (listing.Store is not { } store || await store.Item(item.Id) is { Succeeded: true, Value: null })
        {
            return;
        }

        closePreview();
        await listing.ShowScope(Scope.All);
        listing.Focus(item.Id);
    }

    /// <summary>
    /// The capture leaves by identity through Store's removal, as an import's Undo removes its batch, whatever it was
    /// renamed or marked meanwhile; the listing then shows the stash without it, and a refusal is the undo's outcome,
    /// which the notices name (G29, G35).
    /// </summary>
    private async Task<Outcome> Undo(long id)
    {
        var removed = await listing.Store!.Remove([id]);
        await listing.Refresh();
        return removed[0];
    }
}
