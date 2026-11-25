using ImageStash.Store;

namespace ImageStash.Window;

public enum Scope
{
    Recent,
    All,
    Favorites,
}

/// <summary>
/// Whether the stash can be read yet: it opens at start, after every pending step is resolved (State: Every change);
/// while an import replaces it, nothing of it is read until the imported stash has opened (State: Import stash…).
/// </summary>
public enum StashState
{
    Opening,
    Open,
    Failed,
    Replacing,
}

/// <summary>
/// What the stash window shows, held once: the stash's state, the scope, the query, the ordered items,
/// the tag chips, the selection and the focused item (Composition: Window owns the selection). The window's controls read it and
/// never keep a copy. Nothing of the stash is read until <see cref="StashOpened"/> hands over the store, so a window
/// summoned while the stash opens shows the opening and nothing else (G35, State: Every change). A load runs the
/// store's query on the store's thread and applies its result only if no later load has started, so the grid answers
/// the latest keystroke (G5, E4).
/// </summary>
public sealed partial class Listing
{
    private readonly object gate = new();
    private readonly Action<Failure> failed;
    private readonly List<Func<Task>> lettingGo = [];
    private StashStore? store;
    private HashSet<long> selection = [];

    /// <summary>The selection is the one an import's end made (G12), not since changed by the person or a load.</summary>
    private bool selectionByImport;

    private int personsChanges;
    private long inputOrder;

    // A deferred click keeps its place in the input order. Only an accepted change updates either last-change marker.
    internal long InputOrder => Interlocked.Read(ref inputOrder);
    internal long LastPersonChange { get; private set; }

    internal long NextInput() => Interlocked.Increment(ref inputOrder);

    private (long Commits, long FileSteps)? tagsVersion;
    private ListingOrder order = new();
    private StashStore? withdrawnStore;
    private Dictionary<long, int> indexOf = [];
    private bool resetPending = true;

    /// <summary>
    /// The window has shown since the start, or its scope, search or selection was acted on, which only a window that has
    /// shown is. Until then every load judges Recent or All again and selects its first item, and the first show judges
    /// once more, so it finds Recent as the stash then stands, a capture made while hidden included (G1, G4).
    /// </summary>
    private bool shownSinceStart;

    /// <summary>A load has listed the stash since it opened.</summary>
    private bool listedOnce;

    /// <summary>A load that moves the selection to the first item has started and none has been applied since.</summary>
    private bool selectionResetWaiting;
    private bool loadFailed;
    private (Scope Scope, string Query)? displayedView;
    private bool stashEmpty;
    private int started;
    private int applied;
    private TaskCompletionSource? settling;

    /// <param name="failed">Told of a query the store could not answer; the previous items stay showing (G35).</param>
    public Listing(Action<Failure> failed) => this.failed = failed;

    /// <summary>Raised after every change of what the window shows.</summary>
    public event Action? Changed;

    /// <summary>
    /// Raised when a chip choice or scope reset changes the query, so the search box reflects it immediately.
    /// </summary>
    public event Action? QueryChangedByWindow;

    public StashState State { get; private set; } = StashState.Opening;

    /// <summary>Why the stash did not open, while <see cref="State"/> is <see cref="StashState.Failed"/>.</summary>
    public Failure? OpenFailure { get; private set; }

    /// <summary>
    /// Why the stash cannot take a change, in the words every refusal of one gives with its own action (G35): it is still
    /// opening, did not open, or is being replaced by an import; null once it is open and <see cref="Store"/> holds it.
    /// </summary>
    public string? NotOpenReason => State switch
    {
        StashState.Opening => "your stash is still opening",
        StashState.Failed => "your stash could not be opened",
        StashState.Replacing => StashImport.InProgress,
        _ => null,
    };

    /// <summary>
    /// The words of the whole-stash act asked (a Move, an export or an import), from the moment it is asked until it has
    /// ended, in success or failure (State: Move, Export stash…, Import stash…): <see cref="MoveReasons.InProgress"/>,
    /// <see cref="StashExport.InProgress"/> or <see cref="StashImport.InProgress"/>; null while none is. Only
    /// <see cref="Moving"/> and <see cref="Transferring"/> set it.
    /// </summary>
    public string? WholeStashActAsked { get; internal set; }

    /// <summary>
    /// Why a change of the stash cannot begin now, in the words every refusal of one gives with its own action (G35):
    /// <see cref="NotOpenReason"/>, else the whole-stash act's while one is asked, since from then nothing else starts
    /// until it ends (State: Move, Export stash…, Import stash…); null when one can. Every beginning of a change checks
    /// it; an Undo, a completion of what the act waits for, a handoff, <c>Ctrl+C</c> and a drag out do not (they are
    /// refused once the act has begun, <see cref="WholeStashActBegun"/>).
    /// </summary>
    public string? CannotStartReason => NotOpenReason ?? WholeStashActAsked;

    /// <summary>
    /// Set once a whole-stash act's wait has ended and its run is Store's, until it has ended: a handoff, a <c>Ctrl+C</c>
    /// copy and a drag out, which start while it only waits, are refused by name from then (State: Move, G10, G36); only
    /// <see cref="Moving"/> and <see cref="Transferring"/> set it.
    /// </summary>
    public bool WholeStashActBegun { get; internal set; }

    /// <summary>Why an act asked of an item before an import replaced the stash is refused after it (G36, G35).</summary>
    public const string ReplacedSinceReason = "your stash was replaced by an import since it was asked";

    /// <summary>A summon, a search and a scope chosen, as acts for the notices their listing raises (G16).</summary>
    public const string SummonAct = "Summon";

    /// <inheritdoc cref="SummonAct"/>
    public const string SearchAct = "Search";

    /// <inheritdoc cref="SummonAct"/>
    public const string ScopeAct = "Show a scope";

    /// <summary>
    /// How many times an import has replaced the stash since the start: an act that took its item before one and reaches
    /// the store after it would reach, by that identity, another item the imported store gave it to (G36, G9).
    /// </summary>
    public int Replacements { get; private set; }

    /// <summary>The stash has been replaced by an import, which has passed its record (State: Import stash…); only <see cref="Transferring"/> tells it.</summary>
    internal void StashReplaced()
    {
        lock (gate)
        {
            Replacements++;
            withdrawnStore = null;
            order = new ListingOrder();
            keyboardRangeAnchor = null;
        }
    }

    /// <summary>The stash, once it has opened; until then nothing may reach it.</summary>
    public StashStore? Store => store;

    /// <summary>
    /// How many times the person has changed the search, the scope or the selection since the start, or opened the
    /// preview (<see cref="FocusForPreview"/>). Only accepted actions count, and of the selection only a change: a click on
    /// the tile already alone selected is none. A call made with <c>byPerson: false</c>, an import's selection, a summon,
    /// and a load or refresh moving the selection count for nothing.
    /// </summary>
    public int PersonsChanges => personsChanges;

    /// <summary>The scope the user is in; while a query has words the grid shows its matches instead (G5).</summary>
    public Scope Scope { get; private set; } = Scope.Recent;

    /// <summary>The search box's text as typed, never trimmed or rewritten.</summary>
    public string Query { get; private set; } = "";

    public IReadOnlyList<StashItem> Items { get; private set; } = [];

    /// <summary>How many times the visible Recent order changed at a summon or scope choice.</summary>
    internal int RecentReorders { get; private set; }

    /// <summary>
    /// The tag chips in display order (G4, G5). A zero count marks an inactive place. The row fits as many
    /// whole chips as it can. <see cref="Tags"/> is the live list for pickers and import.
    /// </summary>
    public IReadOnlyList<TagCount> Chips { get; private set; } = [];

    /// <summary>Every tag the stash's items carry, the commonest first, as the latest load read it, whatever the query (G4).</summary>
    public IReadOnlyList<TagCount> Tags => order.Tags.Live;

    /// <summary>
    /// The tags whose chips are lit: the existing tags that are words of the query, ignoring case and a leading
    /// <c>#</c>, in the query's order (G5). Read from every tag, not from the matches or the chips the row's width
    /// shows, so the tags new items get follow what was typed, a query matching nothing included (G11).
    /// </summary>
    public IReadOnlyList<string> LitTags => order.Tags.Lit(Query);

    public IReadOnlySet<long> Selection => selection;

    /// <summary>The selected items in the order the grid shows them, as the handoff takes them (G8).</summary>
    public IReadOnlyList<StashItem> SelectedItems => Items.Where(item => selection.Contains(item.Id)).ToList();

    /// <summary>The item the keys move from, also the moving end of a keyboard range: one of the selection, or none.</summary>
    public long? Focused { get; private set; }

    public bool IsSearch => ItemSearch.IsSearch(Query);

    /// <summary>The chip that is lit: All while a query has words, else the scope (G5).</summary>
    public Scope LitChip => IsSearch ? Scope.All : Scope;

    /// <summary>Whether the last load started has been applied, or has failed.</summary>
    public bool Settled => applied == started;

    /// <summary>The input order of the latest accepted selection reset. A newer reset supersedes a waiting arrow (G5).</summary>
    internal long LastSelectionReset { get; private set; }

    // A refresh failure can leave an unchanged, already-listed item usable for G8.
    // Rows belonging to a different query or scope cannot answer a keyboard command.
    internal bool ShowsRequestedView => displayedView == ListingView(Scope, Query);
    internal bool ShowsPriorViewAfterFailure => Settled && loadFailed && displayedView is not null && !ShowsRequestedView;
    internal bool SelectionReadPending => !Settled && selectionResetWaiting;
    // Reaffirming a tile or opening an overlay can supersede keys without changing the import's selection count.
    internal long LastSelectionInput { get; private set; }

    internal bool HasExplicitSelectionAfter(long input) =>
        displayedView is not null && LastSelectionInput > Math.Max(LastSelectionReset, input);

    internal void SupersedeSelectionKeys() => LastSelectionInput = NextInput();

    /// <summary>The stash is open, nothing is searched, and it holds no item at all: the empty state (G2).</summary>
    public bool IsEmptyStash => State == StashState.Open && Settled && !IsSearch && Items.Count == 0 && stashEmpty;

    /// <summary>A query with words matched nothing: the no-match state, which names the query (G5).</summary>
    public bool NoMatch => State == StashState.Open && Settled && IsSearch && Items.Count == 0;

    /// <summary>
    /// The stash opened, at start or at the folder a Move switched to: every part reads it from here on, and a load of
    /// the store it replaces is abandoned (G30).
    /// </summary>
    public Task StashOpened(StashStore opened) => UseStore(opened, keepOrder: false);

    /// <summary>Move kept the items and gave their files a new location.</summary>
    internal Task StashMoved(StashStore opened) => UseStore(opened, keepOrder: true);

    private Task UseStore(StashStore opened, bool keepOrder)
    {
        lock (gate)
        {
            if (!ReferenceEquals(store ?? withdrawnStore, opened))
            {
                // A Move has changed the file paths. Clear the old listing before any caller can use them.
                Items = [];
                displayedView = null;
                indexOf = [];
                selection = [];
                selectionByImport = false;
                keyboardRangeAnchor = null;
                Focused = null;
                ClearTagCounts();
                if (!keepOrder)
                {
                    order = new ListingOrder();
                }
            }

            withdrawnStore = null;
            store = opened;
        }
        State = StashState.Open;
        OpenFailure = null;
        Raise();
        return Load(keepSelection: false);
    }

    /// <summary>
    /// The stash did not open, at start or at the folder a Move switched to: the listing lets go of any store it held and
    /// of what it showed, so every part refuses by name instead of reaching a store that is closed (G35).
    /// </summary>
    public void StashFailed(Failure failure)
    {
        LetGoOfTheStore();
        State = StashState.Failed;
        OpenFailure = failure;
        Raise();
        EndSettling();
    }

    /// <summary>
    /// Registers what a part must let go of before an import replaces the stash folder, which a file of it held open
    /// keeps from being renamed (G36): <paramref name="letGo"/> lets go and completes once nothing of it reads a file.
    /// </summary>
    public void LetGoWhenWithdrawn(Func<Task> letGo) => lettingGo.Add(letGo);

    /// <summary>
    /// An import begins to replace the stash (State: Import stash…): the listing lets go of the store and of what it
    /// showed, so nothing of the stash is listed, a load begun on it applies nothing, and every part refuses by name;
    /// every registered part lets go of what it reads of the stash's files. Completes once each has. The stash is
    /// opened again afterwards, the same one when the import changed nothing (<see cref="StashOpened"/>).
    /// </summary>
    public Task Withdraw()
    {
        LetGoOfTheStore(keepOrder: true);
        State = StashState.Replacing;
        OpenFailure = null;
        Raise();
        EndSettling();
        return Task.WhenAll(lettingGo.Select(letGo => letGo()));
    }

    /// <summary>Nothing of the store held is listed or read from here on, and every load started is settled.</summary>
    private void LetGoOfTheStore(bool keepOrder = false)
    {
        lock (gate)
        {
            // The order contains only identities and tag names. It can be resumed if the same stash reopens.
            withdrawnStore = keepOrder ? store : null;
            if (!keepOrder)
            {
                order = new ListingOrder();
            }

            store = null;
            displayedView = null;
            SupersedeSelectionKeys();
            Items = [];
            indexOf = [];
            selection = [];
            selectionByImport = false;
            keyboardRangeAnchor = null;
            Focused = null;
            ClearTagCounts();
            applied = ++started;
        }
    }

    private void ClearTagCounts()
    {
        order.Tags.ForgetRead();
        searchRead = null;
        Chips = [];
        tagsVersion = null;
    }

    /// <summary>
    /// Recent, Favorites or All: clears the search and shows that scope (G4); <paramref name="byPerson"/> false for the
    /// window's own, as an import's end and a paste's return to Recent, which are not the person's change (G12).
    /// </summary>
    public Task ShowScope(Scope scope, bool byPerson = true) => ShowScope(scope, byPerson, input: null);

    internal Task ShowScope(Scope scope, bool byPerson, long? input)
    {
        using var acting = UserAct.Begin(ScopeAct);
        CountChange(byPerson, input);
        lock (gate)
        {
            SetScope(scope, byPerson);
        }

        QueryChangedByWindow?.Invoke();
        return Load(keepSelection: false, selectionAt: input);
    }

    private void SetScope(Scope scope, bool byPerson)
    {
        var changesView = Query.Length > 0 || Scope != scope;
        Query = "";
        Scope = scope;
        resetPending = false;
        if (scope == Scope.Recent)
        {
            // A paste that hid the flyout chooses Recent with its first item selected.
            order.RequestRecentOrder();
        }

        if (byPerson)
        {
            order.Tags.RequestOrder();
        }
        else if (changesView)
        {
            order.Tags.Edited();
        }
    }

    /// <summary>The search box changed: its matches, or once it has no words, the scope the user was in (G5).</summary>
    public Task SetQuery(string text)
    {
        using var acting = UserAct.Begin(SearchAct);
        CountChange(true);
        lock (gate)
        {
            Query = text;
            order.Tags.Edited();
        }

        return Load(keepSelection: false);
    }

    /// <summary>Loads what is showing again, keeping the selection where its items still exist.</summary>
    public Task Refresh()
    {
        lock (gate)
        {
            if (Scope == Scope.Recent)
            {
                order.RecentChanged();
            }
        }

        return Load(keepSelection: true);
    }

    /// <summary>
    /// A summon applies the latest Recent and tag order and keeps the scope, search and selection (G1, G4).
    /// The first show after start judges Recent or All once more. A summon of the hidden
    /// <paramref name="flyout"/> keeps, of several items an import selected, the first alone (G1, G12), whether the import
    /// ended before the hide or while the flyout was hidden; the pinned window brought forward keeps them all.
    /// </summary>
    public Task Summoned(bool flyout) => TagChoicePending ? SummonAfterTagChoices(flyout) : SummonNow(flyout);

    private async Task SummonAfterTagChoices(bool flyout)
    {
        await Settle();
        await SummonNow(flyout);
    }

    private Task SummonNow(bool flyout)
    {
        using var acting = UserAct.Begin(SummonAct);
        bool collapsed;
        bool judge;
        bool read;
        bool chipsChanged = false;
        lock (gate)
        {
            shownSinceStart = true;
            // The first show after start: the stash may have changed since its last load, as a capture's commit does
            // before its refresh, so it is judged once more; a stash not yet listed is judged at its first load (G1).
            judge = resetPending && listedOnce;
            order.Tags.RequestOrder();
            if (Scope == Scope.Recent)
            {
                order.RequestRecentOrder();
            }

            read = store is not null && (judge || loadFailed || !Settled || Scope == Scope.Recent || tagsVersion != store.ListingVersion);
            if (store is not null && !read)
            {
                order.Tags.Counted(Tags);
                var next = order.Tags.Shown(Query, IsSearch);
                chipsChanged = !Chips.SequenceEqual(next);
                if (chipsChanged)
                {
                    Chips = next;
                }
            }

            collapsed = flyout && selectionByImport && selection.Count > 1;
            if (collapsed)
            {
                Focused = FirstSelected();
                selection = [Focused!.Value];
                selectionByImport = false;
                keyboardRangeAnchor = null;
            }
        }

        if (collapsed || chipsChanged)
        {
            Raise();
        }

        return read ? Load(keepSelection: !judge) : Task.CompletedTask;
    }

    /// <summary>
    /// Waits for pending tag choices and the latest load so keys act on the resulting selection (G5, G7).
    /// </summary>
    public Task Settle() => TagChoicePending ? SettleAfterTagChoices() : SettleRead();

    private async Task SettleAfterTagChoices()
    {
        do
        {
            await order.Tags.PendingChoice;
            await SettleRead();
        }
        while (!Settled || TagChoicePending);
    }

    private Task SettleRead()
    {
        lock (gate)
        {
            if (applied == started)
            {
                return Task.CompletedTask;
            }

            settling ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            return settling.Task;
        }
    }

    /// <summary>Where <paramref name="id"/> stands among the items listed, or null when it is not one of them.</summary>
    public int? PlaceOf(long id)
    {
        lock (gate)
        {
            return indexOf.TryGetValue(id, out var place) ? place : null;
        }
    }

    /// <summary>
    /// A plain click: the item becomes the whole selection and is focused; <paramref name="byPerson"/> false for the
    /// window's own, as the editor selecting the item it opened on or returns to (G12).
    /// </summary>
    public void Focus(long id, bool byPerson = true)
    {
        // Clicking the focused tile can cancel a queued key without changing the import's selection count.
        if (byPerson)
        {
            SupersedeSelectionKeys();
        }

        Select([id], id, byPerson);
    }

    /// <summary>Focuses an item from an overlay without clearing the selection when the search omits it.</summary>
    internal void FocusIfListed(long id)
    {
        if (PlaceOf(id) is not null)
        {
            Focus(id, byPerson: false);
        }
    }

    /// <summary>
    /// The preview opened on an item, or moved to it: the item alone selected if listed; by the person, a
    /// change of theirs even when the item already was the selection, since opening the preview is one (G12).
    /// </summary>
    public void FocusForPreview(long id, bool byPerson = true, long? input = null)
    {
        CountChange(byPerson, input);
        FocusIfListed(id);
    }

    /// <summary>Replaces the selection; the focus goes to <paramref name="focus"/>, or to the first selected item in order.</summary>
    public void Select(IEnumerable<long> ids, long? focus) => Select(ids, focus, byPerson: true);

    private void Select(IEnumerable<long> ids, long? focus, bool byPerson)
    {
        lock (gate)
        {
            ReplaceSelection(ids, focus, byImport: false, byPerson);
        }

        Raise();
    }

    private void ReplaceSelection(IEnumerable<long> ids, long? focus, bool byImport, bool byPerson)
    {
        var chosen = ids.Where(indexOf.ContainsKey).ToHashSet();
        var focused = focus is { } f && chosen.Contains(f) ? f : Items.FirstOrDefault(item => chosen.Contains(item.Id))?.Id;
        ApplySelection(chosen, focused, byImport, byPerson, input: null);
        keyboardRangeAnchor = null;
    }

    private void ApplySelection(HashSet<long> chosen, long? focused, bool byImport, bool byPerson, long? input)
    {
        CountChange(byPerson && (!chosen.SetEquals(selection) || focused != Focused), input);
        selection = chosen;
        selectionByImport = byImport;
        Focused = focused;
    }

    /// <summary><c>Ctrl</c>-click (G17): adds or removes one item and leaves the rest of the selection as it was.</summary>
    public void Toggle(long id)
    {
        lock (gate)
        {
            if (!indexOf.TryGetValue(id, out var index))
            {
                return;
            }

            CountChange(true);
            SupersedeSelectionKeys();
            selectionByImport = false;
            keyboardRangeAnchor = null;
            if (selection.Add(id))
            {
                Focused = id;
            }
            else
            {
                selection.Remove(id);
                if (Focused == id)
                {
                    Focused = NearestSelected(index);
                }
            }
        }

        Raise();
    }

    /// <summary><c>Shift</c>-click (G17): the selection becomes the run of items from the focused one to this one.</summary>
    public void ExtendTo(long id)
    {
        lock (gate)
        {
            if (!indexOf.ContainsKey(id))
            {
                return;
            }

            // Pointer range selection keeps the start focused, including when it follows a keyboard range.
            var anchor = RangeAnchorOr(id);
            ApplyRange(anchor, id, focus: anchor);
            SupersedeSelectionKeys();
            keyboardRangeAnchor = null;
        }

        Raise();
    }

    /// <summary><c>Ctrl+A</c> in the grid (G17).</summary>
    public void SelectAll() => SelectAll(input: null);

    internal void SelectAll(long? input)
    {
        lock (gate)
        {
            var all = Items.Select(item => item.Id).ToHashSet();
            CountChange(!all.SetEquals(selection), input);
            selection = all;
            selectionByImport = false;
            keyboardRangeAnchor = null;
            Focused = Focused is { } f && selection.Contains(f) ? f : FirstSelected();
        }

        Raise();
    }

    /// <summary>
    /// The one item whose animation plays in the grid (G6): the hovered one when it is animated, else the focused one
    /// when it is; hover is the more recent act, and leaving the tile hands play back to the selection.
    /// </summary>
    public static long? PlayingItem(StashItem? hovered, StashItem? focused) =>
        hovered is { Facts.Animated: true } ? hovered.Id : focused is { Facts.Animated: true } ? focused.Id : null;

    private long? FirstSelected() => Items.FirstOrDefault(item => selection.Contains(item.Id))?.Id;

    /// <summary>Counts one of the person's changes (<see cref="PersonsChanges"/>) when <paramref name="changed"/>.</summary>
    private void CountChange(bool changed, long? at = null)
    {
        ActedOn();
        if (changed)
        {
            lock (gate)
            {
                LastPersonChange = Math.Max(LastPersonChange, at ?? NextInput());
                personsChanges++;
            }
        }
    }

    /// <summary>
    /// The window's scope, search or selection acted on, which only a shown window is (G1): a stash listed already stays
    /// as listed, a judging load still in flight giving way, and one not yet listed is judged at its first load.
    /// </summary>
    private void ActedOn()
    {
        lock (gate)
        {
            shownSinceStart = true;
            if (listedOnce)
            {
                resetPending = false;
            }
        }
    }

    /// <summary>The selected item nearest <paramref name="index"/> in order; of two as near, the later.</summary>
    private long? NearestSelected(int index) =>
        selection.Count == 0 ? null : selection.MinBy(id => (Math.Abs(indexOf[id] - index), indexOf[id] < index))!;

    private async Task Load(bool keepSelection, bool showPendingQuery = false, long? selectionAt = null)
    {
        int load;
        string query;
        Scope scope;
        bool reset;
        bool searching;
        bool readCoveredRecent;
        lock (gate)
        {
            load = ++started;
            selectionResetWaiting |= !keepSelection;
            if (!keepSelection)
            {
                LastSelectionReset = Math.Max(LastSelectionReset, selectionAt ?? NextInput());
                keyboardRangeAnchor = null;
            }
            (query, scope, reset) = (Query, Scope, resetPending);
            searching = ItemSearch.IsSearch(query);
            // Search can cover Recent. Read its membership on refresh so successive arrivals still join in order.
            readCoveredRecent = scope == Scope.Recent && searching && order.RecentReadPending;
        }

        if (store is not { } opened)
        {
            // Nothing is read before the stash has opened; StashOpened loads what the model then holds.
            lock (gate)
            {
                applied = started;
            }

            Raise();
            EndSettling();
            return;
        }

        // Keep the search read available to a click that follows it, even if toggling a lit chip supersedes the display.
        var reading = Run(opened, query, scope, reset, () => ReferenceEquals(store, opened));
        searchRead = searching ? (query, reading) : null;
        if (showPendingQuery)
        {
            Raise();
        }

        // A load whose store a Move has replaced, or let go of, is abandoned after each await: that store is closed (G30).
        var loaded = await reading;
        if (loaded is null || !ReferenceEquals(store, opened))
        {
            return;
        }

        var coveredRecent = readCoveredRecent ? await opened.Recent() : null;
        if (!ReferenceEquals(store, opened))
        {
            return;
        }

        var (_, counted, version) = loaded.Read;
        bool readAgain;
        lock (gate)
        {
            // Superseded, or abandoned, its store let go of while the listing was read: the load that replaced it, or
            // StashFailed or Withdraw, settles the listing.
            if (load != started || !ReferenceEquals(store, opened))
            {
                return;
            }

            // A judgment of the first show that the person's act overtook lists what they acted on instead (G1, G7).
            var judgedTooLate = reset && !resetPending && loaded.Items.Succeeded && !searching;
            // A capture can join Recent after the read but before the first show settles (G1).
            var changedWhileJudging = reset && resetPending && loaded.Items.Succeeded && version != opened.ListingVersion;
            readAgain = judgedTooLate || changedWhileJudging;
            if (judgedTooLate)
            {
                // Its move to the first item gives way with it, so the load listing what they acted on keeps their selection.
                selectionResetWaiting = false;
            }

            if (!readAgain)
            {
                applied = load;
                loadFailed = !loaded.Items.Succeeded || !counted.Succeeded || coveredRecent is { Succeeded: false };
                if (loaded.Items.Succeeded)
                {
                    ApplyLoadedItems(loaded, searching, reset, keepSelection);
                    if (searching)
                    {
                        order.Tags.Matched(query, Items);
                    }
                }

                if (coveredRecent?.Value is { } recent)
                {
                    order.Recent(recent);
                }

                ApplyListingTags(counted, version, query, searching, loaded.Items.Succeeded);
            }
        }

        if (readAgain)
        {
            await Load(keepSelection: true);
            return;
        }

        if (!loaded.Items.Succeeded)
        {
            failed(loaded.Items.Failure);
        }
        else if (!counted.Succeeded)
        {
            failed(counted.Failure);
        }
        else if (coveredRecent is { Succeeded: false } missed)
        {
            failed(missed.Failure);
        }

        Raise();
        EndSettling();
    }

    private void ApplyLoadedItems(Loaded loaded, bool searching, bool reset, bool keepSelection)
    {
        listedOnce = true;
        if (reset && !searching)
        {
            Scope = loaded.Scope;
            resetPending = !shownSinceStart;
        }

        stashEmpty = loaded.StashEmpty ?? stashEmpty;
        var items = loaded.Items.Value!;
        if (loaded.Scope == Scope.Recent && !searching)
        {
            if (reset)
            {
                order.RequestRecentOrder();
            }

            if (order.RecentReorderPending && !Items.Select(item => item.Id).SequenceEqual(items.Select(item => item.Id)))
            {
                RecentReorders++;
            }

            items = order.Recent(items);
        }

        // A refresh that supersedes a search or first-show reset keeps its move to the first item.
        displayedView = ListingView(Scope, Query);
        // A newer personal selection on a displayed tile survives a pending reset if that tile is still listed.
        var choseSinceReset = selectionResetWaiting && Math.Max(LastPersonChange, LastSelectionInput) > LastSelectionReset;
        Apply(items, !reset && (choseSinceReset || (keepSelection && !selectionResetWaiting)));
        selectionResetWaiting = false;
    }

    private static (Scope Scope, string Query) ListingView(Scope scope, string query) =>
        ItemSearch.IsSearch(query) ? (Scope.All, query) : (scope, "");

    /// <summary>Lets every <see cref="Settle"/> go once the latest load has been applied and shown.</summary>
    private void EndSettling()
    {
        TaskCompletionSource? waiting;
        lock (gate)
        {
            if (applied != started)
            {
                return;
            }

            (waiting, settling) = (settling, null);
        }

        waiting?.TrySetResult();
    }

    private void Apply(IReadOnlyList<StashItem> items, bool keepSelection)
    {
        var previousFocus = Focused;
        var choseNothing = keepSelection && Items.Count > 0 && selection.Count == 0;
        Items = items;
        indexOf = new Dictionary<long, int>(items.Count);
        for (var i = 0; i < items.Count; i++)
        {
            indexOf[items[i].Id] = i;
        }

        var kept = keepSelection ? selection.Where(indexOf.ContainsKey).ToHashSet() : [];
        if (kept.Count > 0 || choseNothing)
        {
            selection = kept;
            Focused = Focused is { } f && kept.Contains(f) ? f : FirstSelected();
        }
        else
        {
            selection = items.Count > 0 ? [items[0].Id] : [];
            selectionByImport = false;
            Focused = items.Count > 0 ? items[0].Id : null;
        }

        KeepRangeAfterRefresh(keepSelection, previousFocus);
    }

    /// <summary>The items a load shows, the scope it resolved, and whether it learned that the stash holds nothing.</summary>
    private sealed record Loaded(StoreListing Read, Scope Scope, bool? StashEmpty)
    {
        public Outcome<IReadOnlyList<StashItem>> Items => Read.Items;
    }

    /// <summary>
    /// The query for <paramref name="scope"/> or <paramref name="query"/>, asked of <paramref name="store"/> once or
    /// twice; null once <paramref name="current"/> says that store is no longer the listing's, so no second call reaches
    /// a store a Move has closed (G30).
    /// </summary>
    private static async Task<Loaded?> Run(StashStore store, string query, Scope scope, bool reset, Func<bool> current)
    {
        if (ItemSearch.IsSearch(query))
        {
            var matches = await store.ListSearch(query);
            return new Loaded(matches, scope, matches.Items.Value is { Count: > 0 } ? false : null);
        }

        if (reset)
        {
            var (initial, showRecent) = await store.ListInitial();
            return new Loaded(initial, showRecent ? Scope.Recent : Scope.All, initial.Items.Succeeded ? initial.Items.Value!.Count == 0 : null);
        }

        if (scope == Scope.All)
        {
            var all = await store.ListAll();
            return new Loaded(all, Scope.All, all.Items.Succeeded ? all.Items.Value!.Count == 0 : null);
        }

        if (scope == Scope.Favorites)
        {
            var favorites = await store.ListFavorites();
            if (!favorites.Items.Succeeded || favorites.Items.Value!.Count > 0)
            {
                return new Loaded(favorites, Scope.Favorites, favorites.Items.Succeeded ? false : null);
            }

            if (!current())
            {
                return null;
            }

            // No favorites: a state of its own while the stash holds items, the empty state while it holds none (G2).
            var held = await store.All();
            return new Loaded(favorites, Scope.Favorites, held.Succeeded ? held.Value!.Count == 0 : null);
        }

        var recent = await store.ListRecent();
        if (!recent.Items.Succeeded || recent.Items.Value!.Count > 0)
        {
            return new Loaded(recent, Scope.Recent, recent.Items.Succeeded ? false : null);
        }

        if (!current())
        {
            return null;
        }

        // An empty Recent is distinct from an empty stash (G2).
        var allWhenRecentEmpty = await store.All();
        return new Loaded(recent, Scope.Recent, allWhenRecentEmpty.Succeeded ? allWhenRecentEmpty.Value!.Count == 0 : null);
    }

    private void Raise() => Changed?.Invoke();
}
