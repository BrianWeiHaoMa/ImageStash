using Avalonia.Controls;
using ImageStash.Seams;
using ImageStash.Store;

namespace ImageStash.Window;

/// <summary>
/// Move… from the Settings page (G30, G32, State: Move): the folder picker; from the moment a folder is chosen until the
/// Move has ended, in success or failure, every beginning of a change refused by name (<see cref="Listing.WholeStashActAsked"/>);
/// a wait, as a busy state naming what it waits for (G35), while a capture, a recording, an edit, a running change, a
/// drag out or a notice or notification whose Undo still stands is in the way; then Store's Move, the stash opened at
/// the new folder as the start opens it, handed to the listing before the old store is closed, and the clipboard's copy
/// of items written again at their new paths when nothing has changed it since (G26). The outcome is said in the stash
/// window's notices only once Store's Move has answered, after the old folder's removal (R9); a failure is named (G35).
/// What Move waits for is registered with <see cref="WaitFor"/> by the parts that know it, and read again on a later turn
/// of the UI thread, never inside the event that woke it: a notice raises its change before its Undo runs, and that
/// Undo's first call must reach the store before the Move's does. Export stash… and Import stash… wait the same way and
/// open the imported stash the same way, through <see cref="WaitUntilNothingIsInTheWay"/> and <see cref="OpenAgain"/>
/// (State: Export stash…, Import stash…).
/// </summary>
public sealed class Moving
{
    /// <summary>The folder picker's title (G32).</summary>
    public const string PickerTitle = "Choose an empty folder for your stash";

    /// <summary>What a refusal before the picker names: there is no chosen folder yet.</summary>
    private const string StashSubject = "the stash folder";

    /// <summary>How often the wait reads its registrations again when nothing it hears from has changed (L2).</summary>
    public static readonly TimeSpan ReadAgainEvery = TimeSpan.FromMilliseconds(250);

    private readonly Listing listing;
    private readonly Notices notices;
    private readonly SettingsKeeper keeper;
    private readonly IDialogsAndExplorer dialogs;
    private readonly Handoff handoff;
    private readonly Func<string, Task<StoreOpening>> openStash;
    private readonly Func<Task> nextTurn;
    private readonly List<Func<string?>> waits = [];
    private TaskCompletionSource? woken;
    private bool picking;

    // From the asking until MoveTo returns, past the Move's end: its switch and clipboard refresh still run, and a second
    // Move asked meanwhile would be ended by this one's.
    private bool running;

    /// <param name="notices">The stash window's notices, where the outcome is said; they run whether or not the window shows (G16).</param>
    /// <param name="keeper">The one settings keeper, through which Store records the copy and switches the location.</param>
    /// <param name="handoff">The window's one clipboard writer, which writes the clipboard's items again at their new paths.</param>
    /// <param name="openStash">Opens a stash folder as the start does: its pending steps resolved, its Open with… copies classified.</param>
    /// <param name="nextTurn">Completes on a later turn of the UI thread, after everything the current one does.</param>
    public Moving(Listing listing, Notices notices, SettingsKeeper keeper, IDialogsAndExplorer dialogs, Handoff handoff, Func<string, Task<StoreOpening>> openStash, Func<Task> nextTurn)
    {
        this.listing = listing;
        this.notices = notices;
        this.keeper = keeper;
        this.dialogs = dialogs;
        this.handoff = handoff;
        this.openStash = openStash;
        this.nextTurn = nextTurn;
    }

    /// <summary>Raised whenever <see cref="Busy"/>, <see cref="Asked"/> or <see cref="From"/> changes.</summary>
    public event Action? Changed;

    /// <summary>
    /// The store opened at the new folder, or after an import, for the start to keep and close at exit; null when that
    /// folder did not open.
    /// </summary>
    public event Action<StashStore?>? Moved;

    /// <summary>The Move's busy state in words: what it waits for, or how far it has got; null while none is asked (G35).</summary>
    public string? Busy { get; private set; }

    /// <summary>True from the moment a folder is chosen until the Move has ended, in success or failure.</summary>
    public bool Asked { get; private set; }

    /// <summary>The folder the Move started from, while <see cref="Asked"/>: where the stash is until the Move has ended (R9).</summary>
    public string? From { get; private set; }

    /// <summary>Where the last Move asked carried the stash, once it has said so; null from the next asking, and after any other end.</summary>
    public string? MovedTo { get; private set; }

    /// <summary>
    /// Registers what Move must wait for: <paramref name="inProgress"/> answers it in words, or null when nothing; it is
    /// read again whenever <paramref name="changes"/> hears of a change, and at least every <see cref="ReadAgainEvery"/>.
    /// </summary>
    public void WaitFor(Func<string?> inProgress, Action<Action>? changes = null)
    {
        waits.Add(inProgress);
        changes?.Invoke(Wake);
    }

    /// <summary>
    /// The stash window's parts Move waits for: a paste, an import, a copy or a drag out, a Delete, a rename, a change of
    /// tags or favorites, an edit running, open or still opening, a Save a copy… writing its file, and a notice whose Undo
    /// still stands (State: Move, G16, G18, G20).
    /// </summary>
    public void WaitForWindow(ClipboardPaste paste, Import import, Handoff copies, Deletion deletion, Renaming renaming, Organising organising, Editing editing, ItemMenu itemMenu, Notices windowNotices)
    {
        WaitFor(() => paste.Busy is null ? null : "the paste", heard => paste.Changed += heard);
        WaitFor(() => import.Busy is null ? null : "the import", heard => import.Changed += heard);
        WaitFor(() => copies.Busy is not null ? "the copy" : copies.Dragging ? "the drag" : null, heard => copies.Changed += heard);
        WaitFor(() => deletion.Busy is null ? null : "the delete", heard => deletion.Changed += heard);
        WaitFor(() => renaming.Busy is null ? null : "the rename", heard => renaming.Changed += heard);
        WaitFor(() => organising.Changing ? "the tag or favorite change" : null, heard => organising.Changed += heard);
        WaitFor(() => editing.InProgress ? "the edit" : null, heard => editing.Changed += heard);
        WaitFor(() => itemMenu.SavingACopy ? "Save a copy" : null, heard => itemMenu.Changed += heard);
        WaitFor(() => UndoStanding(windowNotices, "notice"), heard => windowNotices.Changed += heard);
    }

    /// <summary>
    /// What Move waits for beside the window: a screenshot or a recording, Stash clipboard image, a capture card's rename,
    /// an Open with… copy being classified or adopted, and a notification above the tray whose Undo still stands (State:
    /// Move, G16, G27).
    /// </summary>
    public void WaitForTray(Screenshot screenshot, RecordClip recordClip, StashClipboardImage stashClipboardImage, CaptureNotification captured, OpenWith openWith, Notices notifications)
    {
        WaitFor(() => screenshot.InProgress ? "the screenshot" : null);
        WaitFor(() => recordClip.InProgress ? "the recording" : null);
        WaitFor(() => stashClipboardImage.InProgress ? "Stash clipboard image" : null);
        WaitFor(() => captured.Renaming.Busy is null ? null : "the rename", heard => captured.Renaming.Changed += heard);
        WaitFor(() => openWith.Running ? "Open with…" : null);
        WaitFor(() => UndoStanding(notifications, "notification"), heard => notifications.Changed += heard);
    }

    /// <summary>
    /// Move… (G30): refused while the stash is not open or a Move is asked; then the folder picker owned by
    /// <paramref name="owner"/>, whose cancel changes nothing; then the wait, the Move and the switch. Answers the
    /// outcome for the page's row, the refusals before the picker named there alone, every later one also in the notices.
    /// </summary>
    public async Task<Outcome> MoveTo(TopLevel owner)
    {
        if (Asked || picking || running)
        {
            return new Failure(StashMove.Action, StashSubject, MoveReasons.InProgress);
        }

        if (listing.NotOpenReason is { } notOpen)
        {
            return new Failure(StashMove.Action, StashSubject, notOpen);
        }

        string? chosen;
        picking = true;
        try
        {
            chosen = await dialogs.PickFolder(owner, PickerTitle);
        }
        catch (Exception e)
        {
            // The platform's dialog failing is a failure to name, not a crash (G35).
            return Named(new Failure(StashMove.Action, "the folder picker", e.Message));
        }
        finally
        {
            picking = false;
        }

        if (chosen is null)
        {
            return Outcome.Done;
        }

        // The stash may have stopped being open while the picker was up.
        if (listing.Store is not { } store || listing.CannotStartReason is not null)
        {
            return Named(new Failure(StashMove.Action, MoveFolders.Subject(chosen), listing.CannotStartReason ?? listing.NotOpenReason!));
        }

        running = true;
        Asked = true;
        MovedTo = null;
        From = store.Folder.Root;
        listing.WholeStashActAsked = MoveReasons.InProgress;
        SetBusy($"Moving your stash to {chosen}…");
        try
        {
            return await Run(store, chosen);
        }
        finally
        {
            End();
            running = false;
        }
    }

    /// <summary>The wait, Store's Move, and the switch to the folder it answers.</summary>
    private async Task<Outcome> Run(StashStore store, string chosen)
    {
        await WaitUntilNothingIsInTheWay(words => SetBusy($"Waiting for {words} before moving your stash…"));
        // In the same turn as the last reading found nothing in the way: from here the copy is Store's, and a handoff, a
        // copy or a drag out, which would hand over files the copy is about to leave behind, is refused (State: Move).
        listing.WholeStashActBegun = true;
        SetBusy($"Moving your stash to {chosen}…");
        var live = true;
        // Told on the UI thread, where a report posted after the Move has answered is no longer shown.
        var progress = new Progress<MoveProgress>(told =>
        {
            if (live)
            {
                SetBusy(Progress(chosen, told));
            }
        });
        Outcome<string> moved;
        try
        {
            moved = await store.Move(keeper, chosen, progress);
        }
        finally
        {
            live = false;
        }

        if (moved.Value is not { } folder)
        {
            // Nothing changed: the old folder is still the stash, and whatever waited asks again.
            End();
            _ = listing.Refresh();
            return Named(moved.Failure!);
        }

        var opened = await OpenAgain(store, folder, answered: End);
        if (opened.Succeeded)
        {
            await handoff.RefreshAfterMove();
        }

        if (!moved.Succeeded)
        {
            // Switched, but the old folder is not yet all gone: named, and the next start finishes its removal (G35).
            return Named(moved.Failure!);
        }

        if (!opened.Succeeded)
        {
            return opened.Failure;
        }

        MovedTo = folder;
        notices.Raise(MovedWords(folder));
        return Outcome.Done;
    }

    /// <summary>
    /// The stash at <paramref name="folder"/> opened as the start opens it, and <paramref name="answered"/> run once the
    /// opening has answered (a Move ends there; an import ends once the stash is listed); handed to the listing before
    /// <paramref name="old"/> is closed, so every part reading the listing's store follows it (G30, G36). A folder that
    /// does not open is named, and the listing lets go of the old store, so every part refuses by name; the settings
    /// already name the folder, and the next start opens it again.
    /// </summary>
    internal async Task<Outcome> OpenAgain(StashStore old, string folder, Action answered)
    {
        StoreOpening opening;
        try
        {
            opening = await openStash(folder);
        }
        catch (Exception e)
        {
            opening = new StoreOpening(null, [new Failure("Open the stash", MoveFolders.Subject(folder), e.Message)]);
        }

        answered();
        foreach (var failure in opening.Failures)
        {
            notices.Raise(failure);
        }

        if (opening.Store is { } fresh)
        {
            Moved?.Invoke(fresh);
            var listed = listing.StashOpened(fresh);
            old.Dispose();
            await listed;
            return Outcome.Done;
        }

        LetGo(old, opening.Failures[0]);
        return opening.Failures[0];
    }

    /// <summary>
    /// No stash opens: the listing names <paramref name="why"/> where it names a stash that did not open, the start keeps
    /// no store, and <paramref name="old"/>, which a Move switched away from or an import spent, is closed (G35).
    /// </summary>
    internal void LetGo(StashStore old, Failure why)
    {
        Moved?.Invoke(null);
        listing.StashFailed(why);
        old.Dispose();
    }

    /// <summary>
    /// Until no registration answers, read on a later turn each time, <paramref name="waiting"/> told what is in the way
    /// in words for the act's busy state: Move's own wait, and an export's and an import's (State: Export stash…, Import
    /// stash…), never two at once, since each refuses to begin while another is asked.
    /// </summary>
    internal async Task WaitUntilNothingIsInTheWay(Action<string> waiting)
    {
        while (true)
        {
            var wake = woken = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await nextTurn();
            if (InTheWay() is not { } words)
            {
                woken = null;
                return;
            }

            waiting(words);
            await Task.WhenAny(wake.Task, Task.Delay(ReadAgainEvery));
        }
    }

    /// <summary>Every registration's words, in the order registered, joined as a sentence names them; null when none answers.</summary>
    private string? InTheWay()
    {
        var words = waits.Select(inProgress => inProgress()).OfType<string>().Distinct().ToList();
        return words.Count switch
        {
            0 => null,
            1 => words[0],
            _ => string.Join(", ", words.Take(words.Count - 1)) + " and " + words[^1],
        };
    }

    /// <summary>What a Move that has ended in success says (R9).</summary>
    public static string MovedWords(string folder) => $"Moved your stash to {folder}.";

    private void Wake() => woken?.TrySetResult();

    /// <summary>The notices whose Undo still stands, in words: "the Undo of a notice", "the Undo of 2 notices"; null when none.</summary>
    private static string? UndoStanding(Notices shown, string kind) =>
        shown.Shown.Count(notice => notice.UndoLabel is not null) switch
        {
            0 => null,
            1 => $"the Undo of a {kind}",
            var n => $"the Undo of {n} {kind}s",
        };

    private static string Progress(string chosen, MoveProgress told) => told.Phase switch
    {
        MovePhase.Copying => $"Moving your stash to {chosen}: copied {told.Done} of {told.Total} files…",
        MovePhase.Removing => $"Moving your stash to {chosen}: removing the old folder, {told.Done} of {told.Total} files…",
        _ => $"Moving your stash to {chosen}: finishing…",
    };

    /// <summary>The Move has ended: changes may begin again, and the page shows the settings' folder.</summary>
    private void End()
    {
        if (!Asked)
        {
            return;
        }

        Asked = false;
        From = null;
        listing.WholeStashActAsked = null;
        listing.WholeStashActBegun = false;
        SetBusy(null);
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
