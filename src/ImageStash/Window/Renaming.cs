using ImageStash.Store;

namespace ImageStash.Window;

/// <summary>
/// Rename in the window (G17, P1): the one path the preview's name field takes, which F2, a click on the name and both
/// menus' Rename open. The store commits the name and renames the item's files, " 2" while the name is taken; only then
/// does the listing reload, so the grid and the preview show the store's answer and never the typed text (R9, G28).
/// While the store works, a file another program holds retried meanwhile, the busy state names the rename (G35); a
/// refusal, or a file still held after the name was committed, is named in a notice, and success says nothing more.
/// Once its files have moved, the clipboard follows them as it follows a Save, through the handoff's guarded refresh
/// (G17, G26). Rename has no Undo and records no use (G10); nothing here touches a file (G29, R2).
/// </summary>
public sealed class Renaming(Listing listing, Notices notices, Handoff handoff)
{
    private readonly SemaphoreSlim oneAtATime = new(1, 1);

    /// <summary>Raised whenever <see cref="Busy"/> changes.</summary>
    public event Action? Changed;

    /// <summary>The running rename's busy state in words ("Renaming “cat”…"), or null while none runs.</summary>
    public string? Busy { get; private set; }

    /// <summary>
    /// Names <paramref name="item"/> <paramref name="name"/>, exactly as typed, none included (G17); the first failure,
    /// named through the notices, else done. While the stash is not open the item is refused by name and nothing is
    /// asked of the store.
    /// </summary>
    public async Task<Outcome> Rename(StashItem item, string name)
    {
        if (listing.CannotStartReason is { } refused)
        {
            return Named(new Failure(ItemRename.Action, Notices.Describe([item]), refused));
        }

        var store = listing.Store!;

        await oneAtATime.WaitAsync();
        try
        {
            SetBusy($"Renaming {Notices.Describe([item])}…");
            var renamed = await store.Rename(item, name);
            // A failure after the commit has changed the name too; the listing shows it with its file where it is.
            await listing.Refresh();
            if (!renamed.Succeeded)
            {
                // Its files may not have moved, or the steps await the next start: no refresh then (State: A live failure).
                return Named(renamed.Failure);
            }

            // The refresh names its own failure; the rename itself has succeeded.
            await handoff.RefreshAfterRename(renamed.Value!);
            return Outcome.Done;
        }
        finally
        {
            SetBusy(null);
            oneAtATime.Release();
        }
    }

    private Outcome Named(Failure failure)
    {
        notices.Raise(failure);
        return failure;
    }

    private void SetBusy(string? busy)
    {
        Busy = busy;
        Changed?.Invoke();
    }
}
