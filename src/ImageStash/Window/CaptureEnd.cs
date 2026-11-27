using Avalonia.Controls;
using ImageStash.Media;
using ImageStash.Seams;
using ImageStash.Store;

namespace ImageStash.Window;

/// <summary>
/// What became of a capture's file inside the capture's span: <see cref="Saved"/> as the import commit answered, and
/// whether this capture's own write put the item on the clipboard (<see cref="Copied"/>).
/// </summary>
public sealed record EndedCapture(ImportedFile Saved, bool Copied);

/// <summary>
/// The end of a capture, the one home of its order for a screenshot and a recording's clip (G14, G15, G16). Inside the
/// capture's span: the file the capture wrote committed as one item through Store's import commit, even when its bytes
/// equal an item's and with no tag (G11, G12, G28); then, while Also copy is on, that item on the clipboard as a copy
/// puts it, recording no use (G8, G10), as soon as it is committed and its file is in place, so the write waits neither
/// for the stash to be listed again nor for the window to come back (R9); then the stash listed again, the window's
/// scope, search and selection kept. Once the caller has put the window back (G3): the capture notification on the
/// display the region was on, saying it is on the clipboard only when this capture's write succeeded (G16, R9), or the
/// failure named above the tray, never only in the window, which may be hidden (G35).
/// </summary>
public sealed class CaptureEnd(Listing listing, Notices notifications, CaptureNotification captured, Handoff handoff, SettingsKeeper settings, TopLevel owner, TimeSpan heldFileRetry)
{
    private readonly PictureImport pictures = new(heldFileRetry, failure => notifications.Raise(failure));

    /// <summary>
    /// The steps inside the capture's span for the picture the capture wrote whole at <paramref name="workPath"/>, a
    /// <c>.work\</c> path Store allocated, under its auto name <paramref name="name"/> (F12): committed, copied while Also
    /// copy is on, then listed again. A recording's clip is committed with <paramref name="written"/>, the facts and the
    /// content hash its encoder took as it wrote it; a screenshot, with none, is read back from its file.
    /// </summary>
    public async Task<EndedCapture> Save(string name, string workPath, MediaRead? written)
    {
        var saved = written is null
            ? await pictures.SaveStaged(listing.Store!, name, workPath)
            : await pictures.SaveWritten(listing.Store!, name, workPath, written);
        var copied = saved is { Result: ImportResultKind.Added, Item: { } item } && await CopiedIfAsked(item);
        // After any outcome: a row committed before a later step failed is listed or not as Store recorded it.
        await listing.Refresh();
        return new EndedCapture(saved, copied);
    }

    /// <summary>
    /// What <paramref name="ended"/> saved said, as <paramref name="action"/>: the capture notification of
    /// <paramref name="kind"/>, with a clip's <paramref name="lengthMs"/>, for an item added, on <paramref name="on"/>,
    /// the display the region was on (G16); otherwise the failure, above the tray whatever display the region was on.
    /// </summary>
    public void Say(EndedCapture ended, string action, CaptureKind kind, long? lengthMs, Display on)
    {
        var saved = ended.Saved;
        switch (saved)
        {
            case { Result: ImportResultKind.Added, Item: { } item }:
                captured.Raise(item, kind, ended.Copied, lengthMs, on);
                break;
            case { Result: ImportResultKind.Unreadable }:
                notifications.Raise(new Failure(action, saved.Name, MediaDecoder.UnreadableReason));
                if (saved.Failure is { } discarded)
                {
                    notifications.Raise(discarded);
                }

                break;
            case { Failure: { } failure }:
                // Refused before the commit, which left nothing, or failed after it, which left the item as Store
                // committed it for the next start to finish: no copy and no capture notification (R9), and nothing
                // discarded, since a committed step still names the file (What crosses every boundary).
                notifications.Raise(failure);
                break;
        }
    }

    /// <summary>
    /// The saved item onto the clipboard while Also copy is on (G14, G15): whether it is there now. Its facts decide the
    /// content as for every copy: a clip of several images as a file only, a still with its bitmap (G8). A write refused
    /// is named above the tray, and the capture notification then says in the stash only (G16, G35).
    /// </summary>
    private async Task<bool> CopiedIfAsked(StashItem item)
    {
        if (!settings.Current.AlsoCopyCapture)
        {
            return false;
        }

        var copied = await handoff.CopyCapture(owner, item);
        if (!copied.Succeeded)
        {
            notifications.Raise(copied.Failure);
        }

        return copied.Succeeded;
    }
}
