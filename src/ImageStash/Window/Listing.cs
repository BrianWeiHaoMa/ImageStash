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

public enum Direction
{
    Up,
    Down,
    Left,
    Right,
}

/// <summary>
/// What the stash window shows, held once: the stash's state, the scope, the query, the items in the store's order,
/// the tag chips, the selection and the focused item (Composition: Window owns the selection). The window's controls read it and
/// never keep a copy. Nothing of the stash is read until <see cref="StashOpened"/> hands over the store, so a window
/// summoned while the stash opens shows the opening and nothing else (G35, State: Every change). A load runs the
/// store's query on the store's thread and applies its result only if no later load has started, so the grid answers
/// the latest keystroke (G5, E4).
/// </summary>
public sealed class Listing
{
    private readonly object gate = new();
    private readonly Action<Failure> failed;
    private readonly List<Func<Task>> lettingGo = [];
    private StashStore? store;
    private HashSet<long> selection = [];

    /// <summary>Every tag the stash's items carry, as the latest load read it; never kept across a change (G17).</summary>
    private IReadOnlyList<TagCount> tags = [];
    private Dictionary<long, int> indexOf = [];
    private bool resetPending = true;

    /// <summary>A load that moves the selection to the first item has started and none has been applied since.</summary>
    private bool selectionResetWaiting;
    private bool loadFailed;
    private bool stashEmpty;
    private int started;
    private int applied;
    private TaskCompletionSource? settling;

    /// <param name="failed">Told of a query the store could not answer; the previous items stay showing (G35).</param>
    public Listing(Action<Failure> failed) => this.failed = failed;

    /// <summary>Raised after every change of what the window shows.</summary>
    public event Action? Changed;

    /// <summary>
    /// Raised as the listing clears the query itself (a scope chip, an import's All), before its load, so the search box
    /// empties at once and the next letter typed starts a new query (G4, G12).
    /// </summary>
    public event Action? QueryCleared;

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

    /// <summary>
    /// How many times an import has replaced the stash since the start: an act that took its item before one and reaches
    /// the store after it would reach, by that identity, another item the imported store gave it to (G36, G9).
    /// </summary>
    public int Replacements { get; private set; }

    /// <summary>The stash has been replaced by an import, which has passed its record (State: Import stash…); only <see cref="Transferring"/> tells it.</summary>
    internal void StashReplaced() => Replacements++;

    /// <summary>The stash, once it has opened; until then nothing may reach it.</summary>
    public StashStore? Store => store;

    /// <summary>The scope the user is in; while a query has words the grid shows its matches instead (G5).</summary>
    public Scope Scope { get; private set; } = Scope.Recent;

    /// <summary>The search box's text as typed, never trimmed or rewritten.</summary>
    public string Query { get; private set; } = "";

    public IReadOnlyList<StashItem> Items { get; private set; } = [];

    /// <summary>
    /// The tag chips for what the listing holds, all of them, in order (G4, G5): with no query, every tag the stash's
    /// items carry, the most carried first; while a query has words, the lit ones, then the tags that would narrow the
    /// matches further. The row shows them in order up to the first that does not fit. Loaded with the items, read fresh
    /// at every load.
    /// </summary>
    public IReadOnlyList<TagCount> Chips { get; private set; } = [];

    /// <summary>Every tag the stash's items carry, the commonest first, as the latest load read it, whatever the query (G4).</summary>
    public IReadOnlyList<TagCount> Tags => tags;

    /// <summary>
    /// The tags whose chips are lit: the existing tags that are words of the query, ignoring case and a leading
    /// <c>#</c>, in the query's order (G5). Read from every tag, not from the matches or the chips the row's width
    /// shows, so the tags new items get follow what was typed, a query matching nothing included (G11).
    /// </summary>
    public IReadOnlyList<string> LitTags => ItemSearch.LitTags(Query, tags.Select(count => count.Tag));

    public IReadOnlySet<long> Selection => selection;

    /// <summary>The selected items in the order the grid shows them, as the handoff takes them (G8).</summary>
    public IReadOnlyList<StashItem> SelectedItems => Items.Where(item => selection.Contains(item.Id)).ToList();

    /// <summary>The item the keys and <c>Shift</c>-click act from: one of the selection, or none.</summary>
    public long? Focused { get; private set; }

    public bool IsSearch => ItemSearch.IsSearch(Query);

    /// <summary>The chip that is lit: All while a query has words, else the scope (G5).</summary>
    public Scope LitChip => IsSearch ? Scope.All : Scope;

    /// <summary>Whether the last load started has been applied, or has failed.</summary>
    public bool Settled => applied == started;

    /// <summary>How many loads that move the selection to the first item have started: an arrow waiting for one is superseded by the next (G5).</summary>
    public int SelectionResets { get; private set; }

    /// <summary>The stash is open, nothing is searched, and it holds no item at all: the empty state (G2).</summary>
    public bool IsEmptyStash => State == StashState.Open && Settled && !IsSearch && Items.Count == 0 && stashEmpty;

    /// <summary>A query with words matched nothing: the no-match state, which names the query (G5).</summary>
    public bool NoMatch => State == StashState.Open && Settled && IsSearch && Items.Count == 0;

    /// <summary>
    /// The stash opened, at start or at the folder a Move switched to: every part reads it from here on, and a load of
    /// the store it replaces is abandoned (G30).
    /// </summary>
    public Task StashOpened(StashStore opened)
    {
        if (store is not null && !ReferenceEquals(store, opened))
        {
            // The items listed from the store a Move replaced name its files: none is handed over, or dragged out, again.
            lock (gate)
            {
                Items = [];
                indexOf = [];
                selection = [];
                Focused = null;
            }
        }

        store = opened;
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
        LetGoOfTheStore();
        State = StashState.Replacing;
        OpenFailure = null;
        Raise();
        EndSettling();
        return Task.WhenAll(lettingGo.Select(letGo => letGo()));
    }

    /// <summary>Nothing of the store held is listed or read from here on, and every load started is settled.</summary>
    private void LetGoOfTheStore()
    {
        lock (gate)
        {
            store = null;
            Items = [];
            indexOf = [];
            selection = [];
            Focused = null;
            tags = [];
            Chips = [];
            applied = started;
        }
    }

    /// <summary>Recent, Favorites or All: clears the search and shows that scope (G4).</summary>
    public Task ShowScope(Scope scope)
    {
        Query = "";
        Scope = scope;
        resetPending = false;
        QueryCleared?.Invoke();
        return Load(keepSelection: false);
    }

    /// <summary>The search box changed: its matches, or once it has no words, the scope the user was in (G5).</summary>
    public Task SetQuery(string text)
    {
        Query = text;
        return Load(keepSelection: false);
    }

    /// <summary>Loads what is showing again, keeping the selection where its items still exist.</summary>
    public Task Refresh() => Load(keepSelection: true);

    /// <summary>
    /// A summon shows what the listing holds, which every change has kept current, and reloads nothing, so it moves no
    /// selection (G1); only a load the store refused, while another program held it (E3), is asked for again.
    /// </summary>
    public Task RetryFailedLoad() => loadFailed ? Refresh() : Task.CompletedTask;

    /// <summary>
    /// Completes once the latest load started has been applied or has failed, however many start meanwhile, so a key
    /// acts on the selection of what was typed, not of what showed before it (G5).
    /// </summary>
    public Task Settle()
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

    /// <summary>A plain click: the item becomes the whole selection and is focused.</summary>
    public void Focus(long id) => Select([id], id);

    /// <summary>Replaces the selection; the focus goes to <paramref name="focus"/>, or to the first selected item in order.</summary>
    public void Select(IEnumerable<long> ids, long? focus)
    {
        lock (gate)
        {
            selection = ids.Where(indexOf.ContainsKey).ToHashSet();
            Focused = focus is { } f && selection.Contains(f) ? f : FirstSelected();
        }

        Raise();
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
            if (!indexOf.TryGetValue(id, out var to))
            {
                return;
            }

            if (Focused is not { } focused || !indexOf.TryGetValue(focused, out var from))
            {
                (from, Focused) = (to, id);
            }

            selection = Items.Skip(Math.Min(from, to)).Take(Math.Abs(to - from) + 1).Select(item => item.Id).ToHashSet();
        }

        Raise();
    }

    /// <summary><c>Ctrl+A</c> in the grid (G17).</summary>
    public void SelectAll()
    {
        lock (gate)
        {
            selection = Items.Select(item => item.Id).ToHashSet();
            Focused = Focused is { } f && selection.Contains(f) ? f : FirstSelected();
        }

        Raise();
    }

    /// <summary>
    /// An arrow key (G7): Left and Right go to the previous and next item in order; Up and Down to the tile of the
    /// adjacent row under the focused tile's centre, or the nearest tile of that row. The item reached becomes the whole
    /// selection. With nothing focused, the first item is.
    /// </summary>
    public void MoveFocus(Direction direction, IReadOnlyList<GridRow> rows)
    {
        long target;
        lock (gate)
        {
            if (Items.Count == 0)
            {
                return;
            }

            if (Focused is not { } focused || !indexOf.TryGetValue(focused, out var index))
            {
                target = Items[0].Id;
            }
            else
            {
                target = direction switch
                {
                    Direction.Left => Items[Math.Max(0, index - 1)].Id,
                    Direction.Right => Items[Math.Min(Items.Count - 1, index + 1)].Id,
                    // Rows laid out for items no longer listed move nothing.
                    _ => Vertical(focused, direction == Direction.Up ? -1 : 1, rows) is { } reached && indexOf.ContainsKey(reached) ? reached : focused,
                };
            }
        }

        Select([target], target);
    }

    /// <summary>
    /// The one item whose animation plays in the grid (G6): the hovered one when it is animated, else the focused one
    /// when it is; hover is the more recent act, and leaving the tile hands play back to the selection.
    /// </summary>
    public static long? PlayingItem(StashItem? hovered, StashItem? focused) =>
        hovered is { Facts.Animated: true } ? hovered.Id : focused is { Facts.Animated: true } ? focused.Id : null;

    private static long? Vertical(long focused, int step, IReadOnlyList<GridRow> rows)
    {
        for (var r = 0; r < rows.Count; r++)
        {
            if (rows[r].Tiles.FirstOrDefault(tile => tile.Id == focused) is not { } from)
            {
                continue;
            }

            if (r + step < 0 || r + step >= rows.Count)
            {
                return null;
            }

            var next = rows[r + step].Tiles;
            var centre = from.CentreX;
            return (next.FirstOrDefault(tile => tile.X <= centre && centre < tile.X + tile.Width)
                ?? next.MinBy(tile => Math.Abs(tile.CentreX - centre)))?.Id;
        }

        return null;
    }

    private long? FirstSelected() => Items.FirstOrDefault(item => selection.Contains(item.Id))?.Id;

    /// <summary>The selected item nearest <paramref name="index"/> in order; of two as near, the later.</summary>
    private long? NearestSelected(int index) =>
        selection.Count == 0 ? null : selection.MinBy(id => (Math.Abs(indexOf[id] - index), indexOf[id] < index))!;

    private async Task Load(bool keepSelection)
    {
        int load;
        lock (gate)
        {
            load = ++started;
            selectionResetWaiting |= !keepSelection;
            SelectionResets += keepSelection ? 0 : 1;
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

        var (query, scope, reset) = (Query, Scope, resetPending);
        // A load whose store a Move has replaced, or let go of, is abandoned after each await: that store is closed (G30).
        var loaded = await Run(opened, query, scope, reset, () => ReferenceEquals(store, opened));
        var counted = loaded is null || !ReferenceEquals(store, opened) ? null : await opened.TagCounts();
        lock (gate)
        {
            // Superseded, or abandoned, its store let go of while the tags were counted: the load that replaced it, or
            // StashFailed or Withdraw, settles the listing.
            if (load != started || loaded is null || counted is null || !ReferenceEquals(store, opened))
            {
                return;
            }

            applied = load;
            loadFailed = !loaded.Items.Succeeded || !counted.Succeeded;
            if (loaded.Items.Succeeded)
            {
                if (reset && !ItemSearch.IsSearch(query))
                {
                    Scope = loaded.Scope;
                    resetPending = false;
                }

                stashEmpty = loaded.StashEmpty ?? stashEmpty;
                // A later load that keeps the selection, such as a change's refresh, never undoes an earlier one's move
                // to the first match it superseded (G5).
                Apply(loaded.Items.Value!, keepSelection && !selectionResetWaiting);
                selectionResetWaiting = false;
            }

            tags = counted.Value ?? tags;
            Chips = ChipsFor(query, tags, Items);
        }

        if (!loaded.Items.Succeeded)
        {
            failed(loaded.Items.Failure);
        }
        else if (!counted.Succeeded)
        {
            failed(counted.Failure);
        }

        Raise();
        EndSettling();
    }

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
        Items = items;
        indexOf = new Dictionary<long, int>(items.Count);
        for (var i = 0; i < items.Count; i++)
        {
            indexOf[items[i].Id] = i;
        }

        var kept = keepSelection ? selection.Where(indexOf.ContainsKey).ToHashSet() : [];
        if (kept.Count > 0)
        {
            selection = kept;
            Focused = Focused is { } f && kept.Contains(f) ? f : FirstSelected();
        }
        else
        {
            selection = items.Count > 0 ? [items[0].Id] : [];
            Focused = items.Count > 0 ? items[0].Id : null;
        }
    }

    /// <summary>
    /// The chips for <paramref name="query"/> over every tag in <paramref name="counts"/>, the stash's order: all of them
    /// while it has no word; else the tags that are its words, then the tags that contain one of its words without being
    /// one, in the stash's order, so a tag being typed stays in reach, then the tags carried by some of
    /// <paramref name="matches"/> and not by all, which alone would narrow them, the most carried among them first, each
    /// with that count, ties in the stash's order; each tag once, in the first of the three it belongs to (G5).
    /// </summary>
    public static IReadOnlyList<TagCount> ChipsFor(string query, IReadOnlyList<TagCount> counts, IReadOnlyList<StashItem> matches)
    {
        if (!ItemSearch.IsSearch(query))
        {
            return counts;
        }

        var lit = ItemSearch.LitTags(query, counts.Select(count => count.Tag));
        var words = ItemSearch.Words(query);
        var typing = counts.Where(count => !lit.Contains(count.Tag) && ItemSearch.ContainsAWord(count.Tag, words)).ToList();
        // A set, so the narrowing group's test stays linear in the stash's tags however many are being typed (G1, E4).
        var typed = typing.Select(count => count.Tag).ToHashSet();
        var carried = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var tag in matches.SelectMany(item => item.Tags))
        {
            carried[tag] = carried.GetValueOrDefault(tag) + 1;
        }

        var narrowing = counts
            .Where(count => !lit.Contains(count.Tag) && !typed.Contains(count.Tag)
                && carried.GetValueOrDefault(count.Tag) is > 0 and var carriers && carriers < matches.Count)
            .Select(count => new TagCount(count.Tag, carried[count.Tag]))
            .OrderByDescending(count => count.Items);
        return [.. lit.Select(tag => counts.First(count => count.Tag == tag)), .. typing, .. narrowing];
    }

    /// <summary>The items a load shows, the scope it resolved, and whether it learned that the stash holds nothing.</summary>
    private sealed record Loaded(Outcome<IReadOnlyList<StashItem>> Items, Scope Scope, bool? StashEmpty);

    /// <summary>
    /// The query for <paramref name="scope"/> or <paramref name="query"/>, asked of <paramref name="store"/> once or
    /// twice; null once <paramref name="current"/> says that store is no longer the listing's, so no second call reaches
    /// a store a Move has closed (G30).
    /// </summary>
    private static async Task<Loaded?> Run(StashStore store, string query, Scope scope, bool reset, Func<bool> current)
    {
        if (ItemSearch.IsSearch(query))
        {
            var matches = await store.Search(query);
            return new Loaded(matches, scope, matches.Succeeded && matches.Value!.Count > 0 ? false : null);
        }

        if (scope == Scope.All && !reset)
        {
            var all = await store.All();
            return new Loaded(all, Scope.All, all.Succeeded ? all.Value!.Count == 0 : null);
        }

        if (scope == Scope.Favorites && !reset)
        {
            var favorites = await store.Favorites();
            if (!favorites.Succeeded || favorites.Value!.Count > 0)
            {
                return new Loaded(favorites, Scope.Favorites, favorites.Succeeded ? false : null);
            }

            if (!current())
            {
                return null;
            }

            // No favorites: a state of its own while the stash holds items, the empty state while it holds none (G2).
            var held = await store.All();
            return new Loaded(favorites, Scope.Favorites, held.Succeeded ? held.Value!.Count == 0 : null);
        }

        var recent = await store.Recent();
        if (!recent.Succeeded || recent.Value!.Count > 0)
        {
            return new Loaded(recent, Scope.Recent, recent.Succeeded ? false : null);
        }

        if (!current())
        {
            return null;
        }

        // Recent is empty: the first show after start opens on All instead (G1), and either way the empty state needs to
        // know whether the stash holds anything at all.
        var everything = await store.All();
        var empty = everything.Succeeded ? everything.Value!.Count == 0 : (bool?)null;
        return reset ? new Loaded(everything, Scope.All, empty) : new Loaded(recent, Scope.Recent, empty);
    }

    private void Raise() => Changed?.Invoke();
}
