using ImageStash.Editor;
using ImageStash.Layers;
using ImageStash.Store;

namespace ImageStash.Window;

/// <summary>Where an edit was started, which its Save and its Cancel return to (G26).</summary>
public enum EditOrigin
{
    /// <summary><c>Ctrl+E</c> in the search box or the grid, or the item menu's Edit.</summary>
    Grid,

    /// <summary>The preview's Edit, its ⋯ Edit, or <c>Ctrl+E</c> in it.</summary>
    Preview,

    /// <summary>A capture notification's Edit or Trim.</summary>
    Notification,
}

/// <summary>Where the window shows the item once an edit has closed: the grid or the preview on it; no item once it has left the stash.</summary>
public sealed record EditReturn(EditOrigin Origin, StashItem? Item);

/// <summary>
/// Editing in the window (G20, G26, G35, G16; Composition: Window calls Editor, and owns the selection, the notices and
/// every clipboard write): the one path that opens the editor, for <c>Ctrl+E</c>, the item menu's Edit and the
/// preview's, on one item, and a capture notification's Edit or Trim on its own item, and the menus' Revert to original
/// and Save as new item on an edited item. It reads
/// what Edit opens on and opens the session (an item no longer in the stash, a list the store cannot read, or an
/// original held or unreadable is refused by name, and nothing opens), remembers where Edit was started and selects the
/// item alone; one edit at a time (Approach), so opening another first leaves the open one as Cancel would. Save renders,
/// commits and publishes before the edit closes and the window returns where Edit was started, the listing read again;
/// a refused Save is named with the edit kept open; a failure after the commit is named with the edit closed on the
/// item as committed (G35, R9). After a published Save the clipboard's copy of the item is replaced when nothing has
/// changed it since, and never after a failure after the commit (G26, State: A live failure). Save as new item goes on
/// with the new item, a preview beneath moving to it (G26); from a menu it saves the item's layers as the store records
/// them as a new item and opens the editor on that. Revert to original from a menu acts at once, with a notice whose
/// Undo saves the removed layers again (G26). Cancel,
/// the back arrow, Esc with nothing selected and Discard changes leave the item as it was and put up a notice whose
/// Undo opens the discarded edit again (G20). The item leaving the stash, read again by its identity whenever the
/// listing changes, ends the edit without a notice (G16). Nothing here touches a file: the save is the session's.
/// </summary>
public sealed class Editing
{
    public const string Action = "Edit";

    public const string RevertAction = "Revert to original";

    public const string UndoRevertAction = "Undo Revert to original";

    /// <summary>Why a Revert's Undo is refused once the item's original is another picture than the one its layers were made over (G21, G27).</summary>
    public const string OriginalReplacedSinceRevert = "its original has been replaced since it was reverted";

    public const string SaveAsNewItemAction = "Save as new item";

    private const string NotInStash = "it is no longer in the stash";

    private readonly Listing listing;
    private readonly Notices notices;
    private readonly Handoff handoff;
    private readonly FinishedPictureRenderer renderer;
    private readonly ColorChoices colors;
    private readonly TimeSpan heldFileRetry;
    private int openings;

    /// <summary>The item the latest open is reading, from its read of the item until its session opens or it ends; null otherwise.</summary>
    private long? openingOn;
    private int reads;
    private readonly List<string> acting = [];

    /// <summary>A menu's Revert to original or its Undo while it runs, by its item's identity: Edit on that item opens once it has run (G20).</summary>
    private readonly List<(long Id, Task Done)> changing = [];

    /// <summary>When the latest menu change of each item began, counted by <see cref="changesBegun"/>: an open that read meanwhile opens again.</summary>
    private readonly Dictionary<long, int> lastBegun = [];
    private int changesBegun;
    private string? busy;
    private Task<Outcome>? ending;

    /// <param name="handoff">The window's one clipboard writer, which replaces its copy of a saved item (G26).</param>
    /// <param name="colors">The window's one set of recent colours, kept for the run (G21).</param>
    public Editing(Listing listing, Notices notices, Handoff handoff, FinishedPictureRenderer renderer, ColorChoices colors, TimeSpan heldFileRetry)
    {
        this.listing = listing;
        this.notices = notices;
        this.handoff = handoff;
        this.renderer = renderer;
        this.colors = colors;
        this.heldFileRetry = heldFileRetry;
        listing.Changed += () => _ = ReadAgain();
    }

    /// <summary>
    /// Raised when an edit opens, closes or goes on with a new item, when an open ends without one (refused, or overtaken
    /// by another), and when its busy state changes.
    /// </summary>
    public event Action? Changed;

    /// <summary>Raised once an edit has closed, with where the window shows its item now (G26).</summary>
    public event Action<EditReturn>? Returned;

    /// <summary>Raised when Save as new item goes on with the new item, which a preview beneath then shows (G26).</summary>
    public event Action<StashItem>? Continued;

    /// <summary>Raised when an edit's Save of its item has committed, whether or not it changed the item (G27).</summary>
    public event Action<StashItem>? ItemSaved;

    /// <summary>The open edit, or null.</summary>
    public EditorSession? Session { get; private set; }

    public bool IsOpen => Session is not null;

    /// <summary>
    /// Whether the item of <paramref name="id"/> has an edit open, or one still opening, reading what it opens on: Store
    /// cannot see that read, so a change that replaces the item's original waits for both (G20).
    /// </summary>
    public bool Occupies(long id) => Session?.Item.Id == id || openingOn == id;

    /// <summary>
    /// An edit is open, or still opening, reading what it opens on, or a menu's Revert to original, its Undo or Save as
    /// new item runs: a Move waits for each, since the store it would go on with is going (State: Move, G20).
    /// </summary>
    public bool InProgress => Session is not null || openingOn is not null || Busy is not null;

    /// <summary>Where the open edit was started.</summary>
    public EditOrigin Origin { get; private set; }

    /// <summary>
    /// The running Save's busy state in words naming the item, else a menu's Revert to original, its Undo or Save as new
    /// item while one runs, or null while none runs (G35).
    /// </summary>
    public string? Busy => Session?.Busy ?? (acting.Count > 0 ? string.Join(" · ", acting) : null);

    /// <summary>
    /// Opens the editor on <paramref name="item"/>, started from <paramref name="from"/>, on the list the picture on disk
    /// was rendered from or on <paramref name="startOn"/> (the discarded edit a Cancel notice's Undo brings back); the edit
    /// open on another item is first left as Cancel would leave it. Answers the refusal, named, or done.
    /// </summary>
    public async Task<Outcome> Open(StashItem item, EditOrigin from, LayerList? startOn = null) => Named(await Opening(item, from, startOn));

    /// <summary>
    /// A capture notification's Edit or Trim (G16, G20): the editor on the card's own item, whatever the grid selects,
    /// with the Trim tool selected for Trim where the item lists it (a still, a one-image recording among them, opens on
    /// Text); beneath it All, where every item is listed, so the window a notification opened is left with the item
    /// selected (G26). An item gone from the stash meanwhile is refused by name, and nothing opens (G35).
    /// </summary>
    public async Task<Outcome> OpenFromNotification(CaptureEdit asked)
    {
        await listing.ShowScope(Scope.All);
        return Named(await Opening(asked.Item, EditOrigin.Notification, startOn: null, trim: asked.Trim));
    }

    /// <summary>
    /// What <see cref="Open"/> does, answering a refusal unnamed: the Cancel notice's Undo has the notices name it. A new
    /// edit, unlike that Undo (<paramref name="startOn"/>) or the edit Save as new item goes on to
    /// (<paramref name="begins"/> false), is refused while a Move, an export or an import is asked (State: Move, G36).
    /// </summary>
    private async Task<Outcome> Opening(StashItem item, EditOrigin from, LayerList? startOn, bool trim = false, bool begins = true)
    {
        if (begins && startOn is null && listing.WholeStashActAsked is { } asked && !Occupies(item.Id))
        {
            return new Failure(Action, Name(item), asked);
        }

        // The store cannot see a menu's change while it renders, so Edit waits for it here.
        await ChangesOf(item.Id);

        if (Session is { } open)
        {
            if (open.Item.Id == item.Id && startOn is null)
            {
                // Already open on it: the edit stays as it is, on Trim when Trim asks for it.
                SelectTrim(open, trim);
                Changed?.Invoke();
                return Outcome.Done;
            }

            if (!Cancel())
            {
                return new Failure(Action, Name(item), $"“{Name(open.Item)}” is still being saved");
            }
        }

        if (listing.Store is not { } store)
        {
            return new Failure(Action, Name(item), listing.NotOpenReason!);
        }

        var opening = ++openings;
        openingOn = item.Id;
        try
        {
            return await OpenSession(store, item, from, startOn, trim, opening);
        }
        finally
        {
            if (opening == openings)
            {
                openingOn = null;
            }

            // An open that ended without an edit of its item frees that item for a change waiting on it.
            if (Session?.Item.Id != item.Id)
            {
                Changed?.Invoke();
            }
        }
    }

    /// <summary><see cref="Opening"/>'s read and the session's opening, for the open numbered <paramref name="opening"/>.</summary>
    private async Task<Outcome> OpenSession(StashStore store, StashItem item, EditOrigin from, LayerList? startOn, bool trim, int opening)
    {
        var begun = changesBegun;
        var read = await Read(store, item, Action);
        if (opening != openings)
        {
            return Outcome.Done;
        }

        if (!read.Succeeded)
        {
            return read.Failure;
        }

        var edit = read.Value!;
        var opened = await EditorSession.Open(store, edit, renderer, colors, heldFileRetry, startOn);
        if (opening != openings || IsOpen)
        {
            // Another opening went ahead meanwhile.
            opened.Value?.Dispose();
            return Outcome.Done;
        }

        if (!opened.Succeeded)
        {
            return opened.Failure;
        }

        // A menu's change of the item begun while this open read it: opened again once that change has run (G20).
        if (lastBegun.TryGetValue(item.Id, out var at) && at > begun)
        {
            opened.Value!.Dispose();
            await ChangesOf(item.Id);
            return opening == openings ? await OpenSession(store, item, from, startOn, trim, opening) : Outcome.Done;
        }

        Session = opened.Value!;
        Origin = from;
        busy = null;
        // Before the editor first shows, so it opens on the tool asked for.
        SelectTrim(Session, trim);
        Session.Changed += OnSessionChanged;
        listing.Focus(edit.Item.Id);
        Changed?.Invoke();
        return Outcome.Done;
    }

    /// <summary>
    /// <c>Ctrl+E</c> (G20, G7): the editor on <paramref name="previewItem"/>, the open preview's item, whatever the grid
    /// beneath selects; else on the selection once what was typed has arrived, when it is one item; with several,
    /// nothing, named in no notice.
    /// </summary>
    public async Task<Outcome> OpenOnSelection(StashItem? previewItem)
    {
        if (previewItem is not null)
        {
            return await Open(previewItem, EditOrigin.Preview);
        }

        await listing.Settle();
        return listing.SelectedItems is [var one] ? await Open(one, EditOrigin.Grid) : Outcome.Done;
    }

    /// <summary>Save (G26): the edit closes once the store has published it, and the window returns where Edit was started.</summary>
    public Task<Outcome> Save() => Ending(asNewItem: false);

    /// <summary>Save as new item (G26): the edit goes on with the new item.</summary>
    public Task<Outcome> SaveAsNewItem() => Ending(asNewItem: true);

    /// <summary>
    /// Cancel (G20): the item left as it was, the window back where Edit was started, and a notice whose Undo opens the
    /// discarded edit again; false, changing nothing, while a Save runs. Raised for every Cancel, whether or not the
    /// list changed.
    /// </summary>
    public bool Cancel()
    {
        if (Session is not { } session)
        {
            return true;
        }

        if (session.Cancel() is not { } discarded)
        {
            return false;
        }

        var (item, origin) = (session.Item, Origin);
        Close(session);
        ReturnTo(origin, item);
        notices.Raise($"Discarded the edit of “{Name(item)}”.", "Undo", () => Opening(item, origin, discarded));
        return true;
    }

    /// <summary>
    /// Leaves the open edit of item <paramref name="id"/> as Cancel would, before a change replaces the original that edit
    /// shows (an Open with… adoption's Undo, G27, G20); while its Save or Save as new item runs, once that has ended: then
    /// the edit is closed, on the new item, or, refused, still on this one and left.
    /// </summary>
    public async Task LeaveEditOf(long id)
    {
        while (Session?.Item.Id == id && !Cancel())
        {
            if (ending is not { IsCompleted: false } running)
            {
                return;
            }

            await running;
        }
    }

    private Task<Outcome> Ending(bool asNewItem)
    {
        if (Session is not { IsOpen: true } session)
        {
            return Task.FromResult(Outcome.Done);
        }

        // A second press while one runs asks for nothing more.
        if (ending is { IsCompleted: false } running)
        {
            return running;
        }

        return ending = Saved(session, asNewItem);
    }

    private async Task<Outcome> Saved(EditorSession session, bool asNewItem)
    {
        var origin = Origin;
        var result = await (asNewItem ? session.SaveAsNewItem() : session.Save());
        switch (result.Ending)
        {
            case EditEnding.Refused:
                return Named(result.Failure!);
            case EditEnding.Continued:
                await listing.Refresh();
                listing.Focus(result.Item!.Id);
                Continued?.Invoke(result.Item);
                Changed?.Invoke();
                return Outcome.Done;
            case EditEnding.Gone:
                Close(session);
                await listing.Refresh();
                Returned?.Invoke(new EditReturn(EditOrigin.Grid, null));
                return Outcome.Done;
            default:
                // Saved, or failed after the commit: closed on the item as the store has it, listed on the picture on disk.
                Close(session);
                ItemSaved?.Invoke(result.Item!);
                if (result.Failure is { } failure)
                {
                    notices.Raise(failure);
                }

                await listing.Refresh();
                ReturnTo(origin, result.Item!);
                if (result.Failure is { } failed)
                {
                    return failed;
                }

                await handoff.RefreshAfterSave(result.Item!);
                return Outcome.Done;
        }
    }

    /// <summary>
    /// Revert to original from a menu (G26): every layer of <paramref name="item"/> removed at once, nothing opened, then,
    /// once the store has published the change, a notice whose Undo saves the removed layers again, rendered, and the
    /// clipboard's copy of the item replaced (R9). A refusal or a failure after the commit is named, with no notice to
    /// undo it (G35).
    /// </summary>
    public async Task<Outcome> RevertToOriginal(StashItem item)
    {
        if (listing.CannotStartReason is { } refused)
        {
            return Named(new Failure(RevertAction, Name(item), refused));
        }

        var store = listing.Store!;

        StashItem reverted;
        LayerList removed;
        byte[] under;
        var words = Acting($"Reverting “{Name(item)}” to its original…");
        var change = ChangeBegun(item.Id);
        try
        {
            var read = await Read(store, item, RevertAction);
            // Reverted meanwhile, by another click: nothing to remove, nothing to undo.
            if (read is { Succeeded: true, Value.Layers.IsEmpty: true })
            {
                return Outcome.Done;
            }

            var saved = read.Succeeded ? await store.Save(read.Value!.Item, LayerList.Empty, null) : Outcome<StashItem>.Fail(read.Failure);
            await listing.Refresh();
            if (!saved.Succeeded)
            {
                return Named(saved.Failure);
            }

            (reverted, removed, under) = (saved.Value!, read.Value!.Layers, read.Value.OriginalSha256);
        }
        finally
        {
            Acted(words);
            ChangeEnded(item.Id, change);
        }

        notices.Raise($"Reverted “{Name(reverted)}” to its original.", "Undo", () => UndoRevert(reverted, removed, under));
        await handoff.RefreshAfterSave(reverted);
        return Outcome.Done;
    }

    /// <summary>
    /// Save as new item from a menu (G26): a second item of <paramref name="item"/> carrying its layers as the store
    /// records them, rendered, with its tags, not favorite, named with a counter; then the editor opened on it, started
    /// from <paramref name="from"/>, a preview beneath moving to it. A refusal, or a failure once the new item is
    /// inserted, is named and nothing opens (G35).
    /// </summary>
    public async Task<Outcome> SaveAsNewItem(StashItem item, EditOrigin from)
    {
        if (listing.CannotStartReason is { } refused)
        {
            return Named(new Failure(SaveAsNewItemAction, Name(item), refused));
        }

        var store = listing.Store!;

        Outcome<StashItem> made;
        var words = Acting($"Saving “{Name(item)}” as a new item…");
        try
        {
            var read = await Read(store, item, SaveAsNewItemAction);
            made = read.Succeeded ? await SavedEdit.SaveAsNew(store, read.Value!.Item, read.Value, read.Value.Layers, renderer, heldFileRetry) : read.Failure;
            await listing.Refresh();
        }
        finally
        {
            Acted(words);
        }

        if (!made.Succeeded)
        {
            return Named(made.Failure);
        }

        // Begun before any Move was asked, so it goes on to its edit as a Move waits.
        var opened = Named(await Opening(made.Value!, from, startOn: null, begins: false));
        if (Session?.Item is { } continued && continued.Id == made.Value!.Id)
        {
            Continued?.Invoke(continued);
        }

        return opened;
    }

    /// <summary>
    /// The Revert notice's Undo: the removed layers saved over the item again through the one save routine, rendered from
    /// its original where it is now, then the clipboard's copy replaced (G26); an item no longer in the stash, or one whose
    /// original is no longer <paramref name="under"/>, the picture those layers were made over (an Open with… adoption
    /// since, G27), refused by name, which the notices name (G21, G35).
    /// </summary>
    private async Task<Outcome> UndoRevert(StashItem item, LayerList removed, byte[] under)
    {
        if (listing.Store is not { } store)
        {
            return new Failure(UndoRevertAction, Name(item), listing.NotOpenReason!);
        }

        Outcome<StashItem> saved;
        var words = Acting($"Bringing back the edit of “{Name(item)}”…");
        var change = ChangeBegun(item.Id);
        try
        {
            var read = await Read(store, item, UndoRevertAction);
            saved = !read.Succeeded ? read.Failure
                : !read.Value!.OriginalSha256.AsSpan().SequenceEqual(under) ? new Failure(UndoRevertAction, Name(item), OriginalReplacedSinceRevert)
                : await SavedEdit.Save(store, read.Value.Item, read.Value, removed, renderer, heldFileRetry);
            await listing.Refresh();
        }
        finally
        {
            Acted(words);
            ChangeEnded(item.Id, change);
        }

        if (!saved.Succeeded)
        {
            return saved.Failure;
        }

        await handoff.RefreshAfterSave(saved.Value!);
        return Outcome.Done;
    }

    /// <summary>The window back where Edit was started, the item selected there.</summary>
    private void ReturnTo(EditOrigin origin, StashItem item)
    {
        listing.Focus(item.Id);
        Returned?.Invoke(new EditReturn(origin, item));
    }

    /// <summary>
    /// The edit's item read again by its identity, as the listing changes: gone from the stash, it ends the edit without a
    /// notice (G16); one merely out of the scope or the query stays. A Save running decides the edit's end itself.
    /// </summary>
    private async Task ReadAgain()
    {
        if (Session is not { IsOpen: true, Busy: null } session || listing.Store is not { } store)
        {
            return;
        }

        var read = ++reads;
        var found = await store.Item(session.Item.Id);
        if (read != reads || !ReferenceEquals(Session, session) || session.Busy is not null)
        {
            return;
        }

        if (found is { Succeeded: true, Value: null })
        {
            Close(session);
            Returned?.Invoke(new EditReturn(EditOrigin.Grid, null));
        }
    }

    /// <summary>Trim's tool, where the session lists it (an animated item, G20); otherwise, or while a Save runs, the tool stays as it is.</summary>
    private static void SelectTrim(EditorSession session, bool trim)
    {
        if (trim && session.Busy is null && session.Tools.OfType<TrimTool>().FirstOrDefault() is { } trimTool)
        {
            session.Tool = trimTool;
        }
    }

    private void Close(EditorSession session)
    {
        session.Changed -= OnSessionChanged;
        session.Dispose();
        if (ReferenceEquals(Session, session))
        {
            Session = null;
            busy = null;
            Changed?.Invoke();
        }
    }

    /// <summary>The session's own changes reach the window only as its busy state.</summary>
    private void OnSessionChanged()
    {
        if (Session?.Busy != busy)
        {
            busy = Session?.Busy;
            Changed?.Invoke();
        }
    }

    private Outcome Named(Failure failure)
    {
        notices.Raise(failure);
        return failure;
    }

    private Outcome Named(Outcome outcome) => outcome.Succeeded ? outcome : Named(outcome.Failure);

    /// <summary>What Edit opens on <paramref name="item"/>, read again by its identity; one no longer in the stash refused by name for <paramref name="action"/>.</summary>
    private async Task<Outcome<ItemForEdit>> Read(StashStore store, StashItem item, string action)
    {
        var read = await store.ForEdit(item.Id);
        return !read.Succeeded ? read.Failure : read.Value is { } edit ? Outcome<ItemForEdit>.Of(edit) : new Failure(action, Name(item), NotInStash);
    }

    /// <summary>A menu's change begun, its busy state shown beside any other's (G35); the words, for <see cref="Acted"/>.</summary>
    private string Acting(string words)
    {
        acting.Add(words);
        Changed?.Invoke();
        return words;
    }

    private void Acted(string words)
    {
        acting.Remove(words);
        Changed?.Invoke();
    }

    /// <summary>A menu's change of the item of <paramref name="id"/> begun, which Edit on that item waits for; ended by <see cref="ChangeEnded"/>.</summary>
    private TaskCompletionSource ChangeBegun(long id)
    {
        var change = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        changing.Add((id, change.Task));
        lastBegun[id] = ++changesBegun;
        return change;
    }

    private void ChangeEnded(long id, TaskCompletionSource change)
    {
        changing.Remove((id, change.Task));
        change.SetResult();
    }

    /// <summary>Once every menu change of the item of <paramref name="id"/> running now has run.</summary>
    private async Task ChangesOf(long id)
    {
        while (changing.FirstOrDefault(change => change.Id == id).Done is { } running)
        {
            await running;
        }
    }

    private static string Name(StashItem item) => ItemQueries.Named(item.Name, item.FileName);
}
