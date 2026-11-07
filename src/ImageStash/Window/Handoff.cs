using Avalonia.Controls;
using ImageStash.Media;
using ImageStash.Seams;
using ImageStash.Store;

namespace ImageStash.Window;

/// <summary>
/// The handoff's clipboard write, its paste into the target, the drag-out, and the use each records: the one place
/// that decides what goes on the clipboard (G8) and the one place that records a use (G10). Every selected item's
/// finished picture goes as a file; a single still also as its PNG bytes and a bitmap that keeps its transparency; an
/// animated item or several items as files only. A use is recorded on each item only after the write or the drop has
/// completed, never before, and one that fails records nothing (State: Tags, favorite, use). Nothing here says or
/// implies that anything was delivered (G10). A paste the target cannot take is announced by a notification above the
/// tray, raised into <paramref name="notifications"/>, since the flyout may already be hidden (G8, G16). After a Save
/// or a Rename, the clipboard's copy of the item is replaced when nothing has changed it since (G26), and after a Move
/// every item's, at its new path. While the listing holds no store, a handoff, a copy and a drag out are refused by name
/// (G35).
/// </summary>
public sealed class Handoff(Listing listing, Notices notices, Notices notifications, IClipboardAndDrops clipboard, ITarget targets, TimeProvider clock, TimeSpan heldFileRetry, TimeSpan heldClipboardRetry)
{
    public const string Action = "Copy";

    /// <summary>The refresh after a Save or a Rename, as a failure names it (G26, G35).</summary>
    public const string RefreshAction = "Update the clipboard";

    /// <summary>G8's words when the target is gone or cannot be sent input: what is on the clipboard, never that anything arrived (G10).</summary>
    public const string CopiedNotification = "Copied · press Ctrl+V";

    /// <summary>How long the target has to come to the front before the paste falls back to the notification (G8); a busy app takes a moment.</summary>
    public static readonly TimeSpan PasteBound = TimeSpan.FromSeconds(1);

    /// <summary><see cref="ItemFactChange.NotInStash"/> said of several items.</summary>
    private const string NoneInStash = "they are no longer in the stash";

    private const string DragAction = "Drag out";

    private readonly SemaphoreSlim oneWriteAtATime = new(1, 1);
    private int drags;

    /// <summary>Raised whenever <see cref="Busy"/> or <see cref="Dragging"/> changes.</summary>
    public event Action? Changed;

    /// <summary>The running write's busy state in words, or null while none runs (G35).</summary>
    public string? Busy { get; private set; }

    /// <summary>A drag out is in progress, from before it starts until it has returned: a drop may still read its files (G9).</summary>
    public bool Dragging => Volatile.Read(ref drags) > 0;

    /// <summary>
    /// Enter, <c>Ctrl+C</c>, a double-click and the item menu's first entry: puts <paramref name="items"/>, in the order given, on the clipboard with
    /// <paramref name="owner"/> as its owner, then records their uses and refreshes the listing, so Recent lists them
    /// first while the window stays (G4). A copy pressed during another runs after it, never inside it. Every failure
    /// is also raised as a notice, so a caller shows nothing twice (G35).
    /// </summary>
    public async Task<Outcome> Copy(TopLevel owner, IReadOnlyList<StashItem> items)
    {
        if (items.Count == 0)
        {
            return Outcome.Done;
        }

        await oneWriteAtATime.WaitAsync();
        try
        {
            if (Refusal(items, Action) is { } refused)
            {
                return Named(refused);
            }

            SetBusy($"Copying {Subject(items)} to the clipboard…");
            using var confirmed = await listing.Store!.Confirm(items, Action);
            var written = await Write(owner, items, confirmed);
            if (!written.Succeeded)
            {
                notices.Raise(written.Failure);
                return written;
            }

            await Record(items);
            return Outcome.Done;
        }
        finally
        {
            SetBusy(null);
            oneWriteAtATime.Release();
        }
    }

    /// <summary>
    /// A capture's copy while Also copy is on (G14): <paramref name="item"/> put on the clipboard as <see cref="Copy"/>
    /// puts it, under the same one-write-at-a-time gate, so it never lands between a handoff's write and its paste; but
    /// it records no use, since a capture is none (G10), and raises nothing in the window, which may be hidden: the
    /// failure, its subject the item, is the caller's to name (G35).
    /// </summary>
    public async Task<Outcome> CopyCapture(TopLevel owner, StashItem item)
    {
        await oneWriteAtATime.WaitAsync();
        try
        {
            SetBusy($"Copying {Subject([item])} to the clipboard…");
            using var confirmed = await listing.Store!.Confirm([item], Action);
            return await Write(owner, [item], confirmed);
        }
        finally
        {
            SetBusy(null);
            oneWriteAtATime.Release();
        }
    }

    /// <summary>
    /// After a published Save of <paramref name="saved"/> (G26, How G26 holds): when ImageStash's latest write put it on
    /// the clipboard, the items of that write, any deleted since left out, written again as they are now through the one
    /// content rule, under the one-write-at-a-time gate, so it never lands between a handoff's write and its paste. The
    /// seam writes only while nothing has changed the clipboard since that write; a changed clipboard is no failure. It
    /// records no use (G10). A failure is named, its subject the items (G35).
    /// </summary>
    public Task<Outcome> RefreshAfterSave(StashItem saved) => RefreshWritten(saved);

    /// <summary>
    /// After a Rename of <paramref name="renamed"/> whose files have moved (G17, How G26 holds): the items of ImageStash's
    /// latest write, when it put this item there, written again at their new paths exactly as after a Save, only while
    /// nothing has changed the clipboard since that write, recording no use (G10).
    /// </summary>
    public Task<Outcome> RefreshAfterRename(StashItem renamed) => RefreshWritten(renamed);

    /// <summary>
    /// After a Move has switched the stash (G30, G26): the items of ImageStash's latest write, read by identity from the
    /// store the listing now holds, any deleted since left out, written again at their new paths through the one content
    /// rule, only while nothing has changed the clipboard since that write, as after a Save.
    /// </summary>
    public Task<Outcome> RefreshAfterMove() => RefreshWritten(null);

    /// <summary>
    /// After an import has replaced the stash (G36, G26): the clipboard keeps what it holds, but its write is forgotten,
    /// since the imported store gives that write's identities to other items, whose Save must not write the clipboard
    /// with a picture ImageStash never put there.
    /// </summary>
    public void ForgetClipboardWrite() => clipboard.ForgetWritten();

    /// <summary>The refresh after a Save or a Rename of <paramref name="saved"/>, or after a Move when it is null: every item of the latest write.</summary>
    private async Task<Outcome> RefreshWritten(StashItem? saved)
    {
        await oneWriteAtATime.WaitAsync();
        try
        {
            // Read inside the gate: a copy that ran before it may have put other items there since the Save was asked for.
            var written = clipboard.LastWritten;
            if ((saved is null ? written.Count == 0 : !written.Contains(saved.Id)) || listing.Store is not { } store)
            {
                return Outcome.Done;
            }

            var subject = saved is null ? "the clipboard" : Subject([saved]);
            SetBusy(saved is null ? "Updating the clipboard…" : $"Updating {subject} on the clipboard…");
            var present = new List<StashItem>();
            foreach (var id in written.Distinct())
            {
                var read = await store.Item(id);
                if (!read.Succeeded)
                {
                    // Named as the refresh, so the user knows the clipboard still holds the picture from before the Save.
                    return Named(new Failure(RefreshAction, subject, ItemQueries.Unreadable(read.Failure)));
                }

                if (read.Value is { } item)
                {
                    present.Add(item);
                }
            }

            using var confirmed = await store.Confirm(present, RefreshAction);
            // Only an item gone from the stash since is left out; one the store could not confirm stops the refresh, named.
            if (confirmed.Items.FirstOrDefault(answer => !answer.Succeeded && answer.Failure.Reason != ItemFactChange.NotInStash) is { } unconfirmed)
            {
                return Named(unconfirmed.Failure!);
            }

            var current = confirmed.Items.Where(answer => answer.Succeeded).Select(answer => answer.Value!).ToList();
            if (saved is null ? current.Count == 0 : current.All(item => item.Id != saved.Id))
            {
                return Outcome.Done;
            }

            var content = await Content(current);
            if (!content.Succeeded)
            {
                return Named(content.Failure);
            }

            var again = await clipboard.WriteAgain(saved?.Id ?? current[0].Id, content.Value!, heldClipboardRetry);
            return again.Succeeded ? Outcome.Done : Named(again.Failure with { Action = RefreshAction, Subject = Subject(current) });
        }
        finally
        {
            SetBusy(null);
            oneWriteAtATime.Release();
        }
    }

    /// <summary>
    /// Enter, a double-click and the item menu's first entry: with a paste target, the paste into it; with none, the copy (G8).
    /// </summary>
    public Task<Outcome> HandOver(TopLevel owner, IReadOnlyList<StashItem> items, PasteTarget? target, Action hideFlyout) =>
        target is null ? Copy(owner, items) : Paste(owner, items, target, hideFlyout);

    /// <summary>
    /// The paste into <paramref name="target"/>, in How G8 holds's order: the items onto the clipboard and their uses
    /// recorded, exactly as <see cref="Copy"/>; then the seam confirms the window still exists and may be sent input;
    /// then <paramref name="hideFlyout"/>; then the target is brought to the front and sent one paste keystroke. A write
    /// that fails is named and nothing else happens. A window that is gone or cannot be sent input gets the copy that is
    /// already made, the flyout hidden as a paste hides it, so the app the user came from is in front for its
    /// <c>Ctrl+V</c>, and then the notification "Copied · press Ctrl+V", never a second write or a second use. The
    /// one-at-a-time gate is held from the write through the keystroke, so nothing else ImageStash
    /// writes lands on the clipboard before the target pastes it, and so are the items' file names, so no other item
    /// comes under them before then.
    /// </summary>
    public async Task<Outcome> Paste(TopLevel owner, IReadOnlyList<StashItem> items, PasteTarget target, Action hideFlyout)
    {
        if (items.Count == 0)
        {
            return Outcome.Done;
        }

        await oneWriteAtATime.WaitAsync();
        try
        {
            if (Refusal(items, Action) is { } refused)
            {
                return Named(refused);
            }

            SetBusy($"Copying {Subject(items)} to the clipboard…");
            // Held through the keystroke: the target reads the files once it pastes.
            using var confirmed = await listing.Store!.Confirm(items, Action);
            var written = await Write(owner, items, confirmed);
            if (!written.Succeeded)
            {
                notices.Raise(written.Failure);
                return written;
            }

            await Record(items);
            var reachable = targets.Confirm(target);
            if (!reachable.Succeeded)
            {
                hideFlyout();
                notifications.Raise(CopiedNotification);
                return reachable;
            }

            hideFlyout();
            SetBusy($"Bringing {target.AppName} to the front…");
            var pasted = await targets.ActivateAndPaste(target, PasteBound);
            if (!pasted.Succeeded)
            {
                notifications.Raise(CopiedNotification);
            }

            return pasted;
        }
        finally
        {
            SetBusy(null);
            oneWriteAtATime.Release();
        }
    }

    /// <summary>
    /// A drag of <paramref name="items"/> out of the window as files (G9), started by <paramref name="drag"/> with
    /// their files in order. <paramref name="dragging"/> says a drag is in progress from before it starts until it has
    /// returned, so the flyout stays meanwhile (G2); <paramref name="dragEnded"/> then lets the window judge its hide
    /// rule once more, since a focus lost to another app during the drag was judged while it was in progress. A drop
    /// that landed records one use on each item, as a copy does; a drag cancelled or taken by nothing records nothing;
    /// one that failed is named and records nothing (G10, G35). The items' file names are held from its start until it
    /// has returned, so no other item comes under them while a drop can still take them; an item whose name may have
    /// gone to another item since it was shown is refused by name, nothing dragged, and the listing read again (G9).
    /// </summary>
    public async Task<Outcome> DragOut(IReadOnlyList<StashItem> items, Func<IReadOnlyList<string>, Task<Outcome<bool>>> drag, Action<bool> dragging, Action dragEnded)
    {
        if (items.Count == 0)
        {
            return Outcome.Done;
        }

        if (Refusal(items, DragAction) is { } refused)
        {
            return Named(refused);
        }

        // Taken at once, without the store's thread: the drag starts while the button is still down.
        var held = listing.Store!.HoldWhileDragged(items, DragAction);
        if (!held.Succeeded)
        {
            notices.Raise(held.Failure);
            await listing.Refresh();
            return held.Failure;
        }

        Outcome<bool> dropped;
        Interlocked.Increment(ref drags);
        Changed?.Invoke();
        dragging(true);
        try
        {
            dropped = await drag(items.Select(item => item.FilePath).ToList());
        }
        finally
        {
            dragging(false);
            held.Value!.Dispose();
            Interlocked.Decrement(ref drags);
            Changed?.Invoke();
        }

        dragEnded();
        if (!dropped.Succeeded)
        {
            notices.Raise(dropped.Failure);
            return dropped.Failure;
        }

        if (dropped.Value)
        {
            await Record(items);
        }

        return Outcome.Done;
    }

    private async Task<Outcome> Write(TopLevel owner, IReadOnlyList<StashItem> items, Confirmation confirmation)
    {
        // The items as the store has them now, by identity, since a file name never is one: a Delete, an import's Undo
        // or a Rename since the menu opened or the selection was read changes whether, and where, each one's file is (G8).
        // One read for them all, so a held store costs one wait. While it cannot be read, an item unchanged since its read
        // still goes, so the handoff stands as G10 has it; any other is refused, and the whole handoff with it (G35).
        // Their file names stay in hand while the confirmation is held, so no other item comes under them meanwhile.
        var confirmed = confirmation.Items;
        var refused = items.Zip(confirmed).Where(answer => !answer.Second.Succeeded).DistinctBy(answer => answer.First.Id).ToList();
        if (refused.Count > 0)
        {
            // One read answers them all, so they share one reason: the stash no longer holds them, or could not be read.
            var first = refused[0].Second.Failure!;
            return refused.Count == 1
                ? first
                : new Failure(Action, Subject(refused.ConvertAll(answer => answer.First)), first.Reason == ItemFactChange.NotInStash ? NoneInStash : first.Reason);
        }

        var current = confirmed.Select(answer => answer.Value!).ToList();
        var content = await Content(current);
        if (!content.Succeeded)
        {
            return content.Failure;
        }

        var written = await clipboard.Write(owner, content.Value!, heldClipboardRetry);
        // G35 names what the user acted on, the item, rather than the clipboard.
        return written.Succeeded ? written : written.Failure with { Subject = Subject(current) };
    }

    /// <summary>
    /// The one rule for what a write carries (G8): every item's finished picture as a file, in order, and for a single
    /// still also its PNG bytes and its bitmap, which keeps its transparency; with the items' identities, which the seam
    /// remembers for the refresh after a Save (G26).
    /// </summary>
    private async Task<Outcome<ClipboardContent>> Content(IReadOnlyList<StashItem> current)
    {
        ClipboardStill? still = null;
        if (current is [{ Facts.Animated: false } one])
        {
            var read = await Task.Run(() => ClipboardStill.Read(one.FilePath, Subject(current), heldFileRetry));
            if (!read.Succeeded)
            {
                return read.Failure;
            }

            still = read.Value;
        }

        return Outcome<ClipboardContent>.Of(new ClipboardContent(current.Select(item => item.FilePath).ToList(), still?.Png, still?.Frame, current.Select(item => item.Id).ToList()));
    }

    private Outcome Named(Failure failure)
    {
        notices.Raise(failure);
        return failure;
    }

    /// <summary>
    /// The refusal of a handoff, a copy or a drag out of <paramref name="items"/> as <paramref name="action"/>: while the
    /// listing holds no store (it is opening, did not open at start or after a Move, or an import replaces it), or once
    /// a Move's copy, an export or an import has begun (State: Move, G36); null otherwise.
    /// </summary>
    private Failure? Refusal(IReadOnlyList<StashItem> items, string action) =>
        listing.Store is null ? new Failure(action, Subject(items), listing.NotOpenReason!)
        : listing.WholeStashActBegun ? new Failure(action, Subject(items), listing.WholeStashActAsked!)
        : null;

    /// <summary>
    /// One use on each item at this moment, in one transaction, then the listing again with its selection kept. A use
    /// that cannot be recorded is named, and the copy or drag-out stays done: the clipboard keeps what was written, and
    /// the drop has landed (G10, G35). An item that left the stash meanwhile is named, and the others' uses still show (G4).
    /// </summary>
    private async Task Record(IReadOnlyList<StashItem> items)
    {
        var recorded = await listing.Store!.RecordUses(items, clock.GetUtcNow());
        if (!recorded.Succeeded)
        {
            notices.Raise(recorded.Failure);
            return;
        }

        if (recorded.Value is { Count: > 0 } gone)
        {
            notices.Raise(new Failure("Record a use", Subject(gone), gone.Count == 1 ? ItemFactChange.NotInStash : NoneInStash));
        }

        await listing.Refresh();
    }

    /// <summary>The items as a failure names them: one by its name, or its file's name when it has none; several by count.</summary>
    private static string Subject(IReadOnlyList<StashItem> items) => items switch
    {
        [{ Name.Length: > 0 } one] => one.Name,
        [var unnamed] => unnamed.FileName,
        _ => $"{items.Count} items",
    };

    private void SetBusy(string? busy)
    {
        Busy = busy;
        Changed?.Invoke();
    }
}
