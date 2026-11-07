using ImageStash.Media;
using ImageStash.Seams;
using ImageStash.Store;

namespace ImageStash.Window;

/// <summary>
/// Stash clipboard image (G13), the clipboard's third and last reader (N9, R5): without opening a window, the clipboard
/// read once per act, image-only through the seam, so files on it do not count, and its image saved as one still PNG
/// item named like a screenshot at the moment of the act (F12, F3), through the one home of a picture's steps and the
/// import commit, so a kill leaves it absent or complete (G28); then the capture notification above the tray. The item
/// gets no tag and records no use, and the window's scope, query and selection are left as the listing's refresh keeps
/// them (G10, G11). No image, an unreadable picture, and a stash still opening or that did not open, which is refused
/// before anything is read, are this act's refusals; a clipboard another program holds past its retry and a step that
/// failed to save the picture are named as the seam or the step names them; each in a notification of its own, and
/// nothing shows as saved before its file is in place (G35, R9).
/// </summary>
public sealed class StashClipboardImage(Listing listing, Notices notifications, CaptureNotification captured, IClipboardAndDrops clipboard, TimeProvider clock, TimeSpan heldClipboardRetry, TimeSpan heldFileRetry)
{
    public const string Action = "Stash clipboard image";

    private const string Subject = "the clipboard";

    private readonly PictureImport pictures = new(heldFileRetry, failure => notifications.Raise(failure));

    private int acting;

    /// <summary>
    /// True from an act's start until its capture notification, or its refusal, has been raised: its read, stage and
    /// commit show no busy state, and a Move waits for them, then for that notification's Undo (State: Move).
    /// </summary>
    public bool InProgress => Volatile.Read(ref acting) > 0;

    /// <summary>One act: the user's, from the tray menu's entry.</summary>
    public async Task Stash()
    {
        if (listing.CannotStartReason is { } notOpen)
        {
            notifications.Raise(new Failure(Action, Subject, notOpen));
            return;
        }

        Interlocked.Increment(ref acting);
        try
        {
            // The read stays in this entry, the act's one reader of the clipboard (R5).
            // F12: the moment of the act, before any wait on a clipboard another program holds.
            var at = clock.GetLocalNow();
            var read = await clipboard.ReadImage(heldClipboardRetry);
            await SaveAndSay(at, read);
        }
        finally
        {
            Interlocked.Decrement(ref acting);
        }
    }

    /// <summary>The clipboard's image saved, and the outcome said above the tray.</summary>
    private async Task SaveAndSay(DateTimeOffset at, Outcome<ClipboardImage?> read)
    {
        if (!read.Succeeded)
        {
            notifications.Raise(read.Failure);
            return;
        }

        if (read.Value is not { } image)
        {
            // Files, words, a web address or nothing: no image, and nothing is fetched (N2).
            notifications.Raise(new Failure(Action, Subject, "it holds no image"));
            return;
        }

        var saved = await pictures.Save(listing.Store!, new PastedPicture(AutoName.Screenshot(at), image.Png, image.Frame), tags: []);
        // After any outcome: a row committed before a later step failed is listed or not as Store recorded it.
        await listing.Refresh();
        switch (saved)
        {
            case { Result: ImportResultKind.Added, Item: { } item }:
                captured.Raise(item, CaptureKind.StashedClipboardImage, onClipboard: false, lengthMs: null);
                break;
            case { Result: ImportResultKind.Unreadable }:
                notifications.Raise(new Failure(Action, saved.Name, MediaDecoder.UnreadableReason));
                if (saved.Failure is { } discarded)
                {
                    notifications.Raise(discarded);
                }

                break;
            case { Failure: { } failure }:
                // Refused before the commit, which left nothing, or failed after it, which left the item as Store
                // committed it for the next start to finish: never a success (R9), and named as the step that failed
                // names it, unchanged (What crosses every boundary).
                notifications.Raise(failure);
                break;
        }
    }
}
