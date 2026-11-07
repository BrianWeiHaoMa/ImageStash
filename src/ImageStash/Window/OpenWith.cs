using System.ComponentModel;
using Avalonia.Controls;
using ImageStash.Files;
using ImageStash.Seams;
using ImageStash.Store;

namespace ImageStash.Window;

/// <summary>
/// Open with… in the window (G27; How the guarantees hold G27; State: Open with…). Store begins the item's session and
/// answers the path of its copy outside the stash folder; a file-system watcher is armed on that copy's folder, and only
/// then does the Open-with dialog open on the copy, the flyout staying while it is up (G2). Store classifies the copy
/// when the dialog returns, at every change in its folder, and once a second on the given clock while another program
/// still holds it or no watcher could be armed; one classification or adoption of a session runs at a time, and a
/// reason to ask that comes meanwhile asks again once it has answered. A rewrite awaiting adoption is adopted once no
/// edit of its item is open or opening (G20), else when that edit ends; the listing is read again, and then
/// <see cref="AdoptionNotifications"/> says the item was replaced, with Undo (G16, R9). Every failure after the dialog
/// is named above the tray, once per sentence until the session answers cleanly again (G35). Once the stash has opened,
/// every session Store records is watched again and each adoption its start made is said (State: "At start, every
/// recorded session's copy is classified… and watched again"; R9). A session lasts until its item is deleted for good
/// (How the guarantees hold G27): Store then deletes the copy and its folder, which the watcher reports, and a watcher's
/// error or a refusal for an item no longer listed has the part compare what it watches with what Store records, so a
/// session across a Delete's Undo is kept and an ended one let go. The part holds no store: every call goes to the one
/// the listing holds at that moment (Composition: Window). Nothing here deletes, recycles or records a use (G29, G10).
/// </summary>
public sealed class OpenWith : IDisposable
{
    /// <summary>What a failure of the dialog names.</summary>
    public const string Action = "Open with…";

    private const string WatchAction = "Watch the Open with… copy";

    /// <summary>How often a copy another program still holds, or one no watcher could be armed on, is asked about (State: "at least once a second").</summary>
    public static readonly TimeSpan AskedEvery = TimeSpan.FromSeconds(1);

    private readonly object gate = new();
    private readonly Dictionary<long, Session> sessions = [];
    private readonly AdoptionNotifications replaced;
    private readonly Listing listing;
    private readonly Notices notifications;
    private readonly Editing editing;
    private readonly IDialogsAndExplorer dialogs;
    private readonly TimeProvider clock;
    private readonly Action<Action> onUiThread;
    private readonly HashSet<string> namedListings = [];

    // The start's adoptions not yet said, their item not yet read: said at a later change of the listing.
    private readonly List<AdoptedAtStart> unsaid = [];
    private int ownRefreshes;
    // Under gate: the Open with… asked whose session Store has not answered yet.
    private int beginnings;
    private bool disposed;

    // Which store the sessions were asked of once it opened: told apart from one opened later (after a Move), never called.
    private WeakReference<StashStore>? sessionsAskedOf;

    // Each asking of Store's sessions takes the next number; a session made since an asking went out is not judged by its answer.
    private long askings;

    /// <param name="notifications">The notices above the tray, where the adoption and every later failure are said.</param>
    /// <param name="clock">The once-a-second asking's timers.</param>
    /// <param name="onUiThread">Runs an action on the UI thread: every watcher's, timer's and edit's call comes back through it.</param>
    public OpenWith(Listing listing, Notices notifications, Editing editing, IDialogsAndExplorer dialogs, TimeProvider clock, Action<Action> onUiThread)
    {
        this.listing = listing;
        this.notifications = notifications;
        this.editing = editing;
        this.dialogs = dialogs;
        this.clock = clock;
        this.onUiThread = onUiThread;
        replaced = new AdoptionNotifications(listing, notifications, editing, OwnRefresh);
        editing.Changed += EditingChanged;
        listing.Changed += ListingChanged;
    }

    /// <summary>A session is being begun, a copy classified or a rewrite adopted, which a Move waits for (State: Move).</summary>
    public bool Running
    {
        get
        {
            lock (gate)
            {
                return beginnings > 0 || sessions.Values.Any(session => session.Running);
            }
        }
    }

    /// <summary>The items whose Open with… session is watched now, by a watcher or, where none could be armed, once a second.</summary>
    public IReadOnlyCollection<long> Watching
    {
        get
        {
            lock (gate)
            {
                return [.. sessions.Keys];
            }
        }
    }

    /// <summary>
    /// Open with… on <paramref name="item"/> (G27): Store begins or continues its session; the copy's folder is watched,
    /// and only then the Open-with dialog opens on the copy, owned by <paramref name="owner"/>, with
    /// <paramref name="dialogUp"/> told while it is up (G2); once it has closed, the copy is classified. A refusal to
    /// begin, or the dialog's own failure, is answered for the caller to name; the session is watched either way.
    /// </summary>
    public async Task<Outcome> Begin(TopLevel owner, StashItem item, Action<bool> dialogUp)
    {
        if (listing.CannotStartReason is { } refused)
        {
            return new Failure(Action, ItemQueries.Named(item.Name, item.FileName), refused);
        }

        // Counted from the turn the refusal passed until the store has answered, so a Move asked meanwhile waits for the
        // session's own transaction instead of closing the store under it (State: Move).
        lock (gate)
        {
            beginnings++;
        }

        Outcome<string> begun;
        try
        {
            begun = await listing.Store!.BeginOpenWith(item);
        }
        finally
        {
            lock (gate)
            {
                beginnings--;
            }
        }

        if (!begun.Succeeded)
        {
            return begun.Failure;
        }

        var copy = begun.Value!;
        var session = Watch(item.Id, copy);
        Outcome shown;
        dialogUp(true);
        try
        {
            shown = await dialogs.OpenWith(owner, copy);
        }
        catch (Exception e)
        {
            // The platform's dialog failing is a failure to name, not a crash (G35).
            shown = new Failure(Action, Path.GetFileName(copy), e.Message);
        }
        finally
        {
            dialogUp(false);
        }

        Ask(session);
        return shown;
    }

    public void Dispose()
    {
        List<Session> ended;
        lock (gate)
        {
            disposed = true;
            ended = [.. sessions.Values];
            sessions.Clear();
        }

        editing.Changed -= EditingChanged;
        listing.Changed -= ListingChanged;
        foreach (var session in ended)
        {
            session.Dispose();
        }
    }

    /// <summary>
    /// The item's session watched at <paramref name="copy"/>: kept as it is when already watched there; otherwise watched
    /// there afresh, a watch at another path (an earlier copy Store replaced) ended. Armed before the dialog opens, so a
    /// program the dialog starts is seen from its first write (How the guarantees hold G27).
    /// </summary>
    private Session Watch(long id, string copy)
    {
        Session session;
        Session? ended = null;
        lock (gate)
        {
            if (sessions.TryGetValue(id, out var kept) && SamePath(kept.CopyPath, copy))
            {
                if (kept.Watcher is not null)
                {
                    return kept;
                }

                session = kept;
            }
            else
            {
                ended = kept;
                session = new Session(id, copy, askings);
                session.Timer = clock.CreateTimer(_ => onUiThread(() => Tick(session)), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                sessions[id] = session;
            }
        }

        ended?.Dispose();
        Arm(session, named: true);
        return session;
    }

    /// <summary>
    /// A watcher on the copy's folder, any change in it a reason to ask: a program may write beside the copy, and a
    /// replacement renames through another file. One that cannot be armed is, when <paramref name="named"/>, named and
    /// the session asked once a second instead (G35); the once-a-second asking itself tries again unnamed. Whether the
    /// session is watched now.
    /// </summary>
    private bool Arm(Session session, bool named)
    {
        FileSystemWatcher? watcher = null;
        try
        {
            var made = new FileSystemWatcher(Path.GetDirectoryName(session.CopyPath)!)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
                IncludeSubdirectories = false,
            };
            watcher = made;
            made.Changed += (_, _) => onUiThread(() => Ask(session));
            made.Created += (_, _) => onUiThread(() => Ask(session));
            made.Deleted += (_, _) => onUiThread(() => Ask(session));
            made.Renamed += (_, _) => onUiThread(() => Ask(session));
            made.Error += (_, e) => onUiThread(() => Lost(session, made, e.GetException()));
            made.EnableRaisingEvents = true;
        }
        catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException or Win32Exception)
        {
            watcher?.Dispose();
            watcher = null;
            if (named)
            {
                Name(session, new Failure(WatchAction, Path.GetFileName(session.CopyPath), e.Message));
                AskEverySecond(session);
            }
        }

        lock (gate)
        {
            // One watcher per session: a second armed meanwhile (the once-a-second asking and a comparison) is let go.
            if (watcher is not null && IsCurrent(session) && session.Watcher is null)
            {
                session.Watcher = watcher;
                return true;
            }
        }

        watcher?.Dispose();
        return false;
    }

    /// <summary>
    /// The watcher reported an error: events it dropped (its buffer overran) are a reason to ask; any other error stops
    /// it, as its folder's deletion does when the session ends, so the session is asked once a second meanwhile and
    /// what is watched is compared with what Store records.
    /// </summary>
    private void Lost(Session session, FileSystemWatcher watcher, Exception error)
    {
        if (error is not InternalBufferOverflowException)
        {
            lock (gate)
            {
                if (!ReferenceEquals(session.Watcher, watcher))
                {
                    return;
                }

                session.Watcher = null;
            }

            watcher.Dispose();
            Compare();
        }

        Ask(session);
    }

    /// <summary>The once-a-second asking: a session no watcher watches is armed again where its folder is back, then asked.</summary>
    private void Tick(Session session)
    {
        bool unwatched;
        lock (gate)
        {
            unwatched = IsCurrent(session) && session.Watcher is null;
        }

        if (unwatched)
        {
            Arm(session, named: false);
        }

        Ask(session);
    }

    /// <summary>What is watched compared with what Store records: on a watcher's error, or a refusal saying the item is no longer listed or has no session.</summary>
    private void Compare()
    {
        if (listing.Store is { } store)
        {
            _ = Settle(store);
        }
    }

    /// <summary>
    /// Every session Store records, settled against those watched (State: Open with…; How the guarantees hold G27): one
    /// not watched is armed and asked; one watched at another path is armed at the recorded one; one whose watcher went
    /// is armed again, or named and asked once a second; one Store no longer records, its item deleted for good, is let
    /// go. Each adoption the start made is said with the revision it names (R9). False when the asking failed, which is
    /// named once per sentence until one succeeds (G35).
    /// </summary>
    private async Task<bool> Settle(StashStore store)
    {
        long asked;
        lock (gate)
        {
            asked = ++askings;
        }

        var answered = await store.OpenWithSessions();
        if (!answered.Succeeded)
        {
            lock (gate)
            {
                if (!namedListings.Add(answered.Failure.Sentence))
                {
                    return false;
                }
            }

            notifications.Raise(answered.Failure);
            return false;
        }

        var recorded = answered.Value!.Sessions.ToDictionary(session => session.Id, session => session.CopyPath);
        List<Session> ended;
        List<(long Id, string CopyPath)> unwatched = [];
        List<Session> rearmed = [];
        lock (gate)
        {
            if (disposed)
            {
                return true;
            }

            namedListings.Clear();
            ended = [.. sessions.Values.Where(session => session.Born < asked && !recorded.ContainsKey(session.Id))];
            foreach (var session in ended)
            {
                sessions.Remove(session.Id);
            }

            foreach (var (id, copy) in recorded)
            {
                if (!sessions.TryGetValue(id, out var watched) || (watched.Born < asked && !SamePath(watched.CopyPath, copy)))
                {
                    unwatched.Add((id, copy));
                }
                else if (watched.Watcher is null && SamePath(watched.CopyPath, copy))
                {
                    rearmed.Add(watched);
                }
            }
        }

        foreach (var session in ended)
        {
            session.Dispose();
        }

        // Asked at once only once watched again: else the once-a-second asking does, so a refusal that compares again
        // (an item deleted with its Undo standing whose folder went) cannot ask without pause.
        foreach (var session in rearmed)
        {
            if (Arm(session, named: true))
            {
                Ask(session);
            }
        }

        foreach (var (id, copy) in unwatched)
        {
            Ask(Watch(id, copy));
        }

        await Say(store, answered.Value.Adoptions);
        return true;
    }

    /// <summary>
    /// The start's adoptions said (G27, R9), with the revision each names, never one read from the listing: only the first
    /// answer of Store's sessions carries them, so one whose item cannot be read now is kept and said at a later change
    /// of the listing, its failure named once per sentence (G35); one whose item has gone is not said.
    /// </summary>
    private async Task Say(StashStore store, IReadOnlyList<AdoptedAtStart> adoptions)
    {
        var replacements = listing.Replacements;
        List<AdoptedAtStart> due;
        lock (gate)
        {
            unsaid.AddRange(adoptions);
            due = [.. unsaid];
            unsaid.Clear();
        }

        foreach (var adoption in due)
        {
            var item = await store.Item(adoption.Id);
            // An import replaced the stash meanwhile, its store giving this identity to another item: never said (G27, G36).
            if (listing.Replacements != replacements)
            {
                continue;
            }

            // The listing let go of this store meanwhile (an import begun, a Move): said once a store is listed again.
            if (!ReferenceEquals(listing.Store, store))
            {
                lock (gate)
                {
                    unsaid.Add(adoption);
                }

                continue;
            }

            if (item.Succeeded)
            {
                if (item.Value is { } adopted)
                {
                    replaced.Raise(adopted, adoption.Revision);
                }

                continue;
            }

            bool named;
            lock (gate)
            {
                unsaid.Add(adoption);
                named = namedListings.Add(item.Failure.Sentence);
            }

            if (named)
            {
                notifications.Raise(item.Failure);
            }
        }
    }

    /// <summary>A reason to classify the session's copy: asked now, or, while a call of the session runs, once it has answered.</summary>
    private void Ask(Session session)
    {
        lock (gate)
        {
            if (!IsCurrent(session))
            {
                return;
            }

            if (session.Running)
            {
                session.AskAgain = true;
                return;
            }

            session.Running = true;
        }

        _ = Round(session);
    }

    /// <summary>The once-a-second asking started for a session no call of which is running.</summary>
    private void AskEverySecond(Session session)
    {
        lock (gate)
        {
            if (IsCurrent(session) && !session.Running)
            {
                session.Timer!.Change(AskedEvery, Timeout.InfiniteTimeSpan);
            }
        }
    }

    /// <summary>One classification of the session's copy, and the adoption it may lead to; then whatever asked meanwhile.</summary>
    private async Task Round(Session session)
    {
        var then = Then.Rest;
        try
        {
            then = await Classify(session);
        }
        finally
        {
            Ended(session, then);
        }
    }

    private async Task<Then> Classify(Session session)
    {
        // Before the store's absence, which an import's replacement makes for a while and which is no failure to name.
        if (listing.WholeStashActAsked is not null)
        {
            return WhileMoving(session);
        }

        if (listing.Store is not { } store)
        {
            return Failed(session, new Failure(OpenWithCopies.ClassifyAction, Path.GetFileName(session.CopyPath), listing.NotOpenReason!));
        }

        var classified = await store.ClassifyOpenWith(session.Id);
        lock (gate)
        {
            // Watched at another path since, or no longer: the answer belongs to a watch that has ended.
            if (!IsCurrent(session))
            {
                return Then.Rest;
            }
        }

        if (!classified.Succeeded)
        {
            return Failed(session, classified.Failure);
        }

        switch (classified.Value)
        {
            case OpenWithCopy.StillHeld:
                return Then.AskEverySecond;
            case OpenWithCopy.AwaitingAdoption:
                Answered(session);
                return await AdoptOnceNoEdit(store, session);
            default:
                Answered(session);
                return Then.Rest;
        }
    }

    /// <summary>
    /// The rewrite adopted unless an edit of the item is open or still opening: then it waits for that edit to end
    /// (State: "once no edit is open on the item"; G20), when <see cref="EditingChanged"/> asks again.
    /// </summary>
    private async Task<Then> AdoptOnceNoEdit(StashStore store, Session session)
    {
        lock (gate)
        {
            session.AwaitingEdit = true;
        }

        if (editing.Occupies(session.Id))
        {
            return Then.Rest;
        }

        lock (gate)
        {
            session.AwaitingEdit = false;
        }

        if (listing.WholeStashActAsked is not null)
        {
            return WhileMoving(session);
        }

        var adopted = await store.AdoptRewrite(session.Id);
        await OwnRefresh();
        if (adopted.Succeeded)
        {
            replaced.Raise(adopted.Value!, adopted.Value!.RenderedRevision);
            return Then.Rest;
        }

        // The copy held or missing again since the classification, refused before anything changed: asked again as a held
        // copy is, the rewrite still awaiting. A step refused after the commit is a failure to name (G35).
        return adopted.Value is null && adopted.Failure.Reason is HeldFileRetry.HeldReason or HeldFileRetry.MissingReason or HeldFileRetry.FolderMissingReason
            ? Then.AskEverySecond
            : Failed(session, adopted.Failure);
    }

    /// <summary>
    /// A refusal ends the round, to be asked again at the next change or listing change. The item no longer listed, or
    /// its session gone, is nothing the person asked for and is not named; any other failure is, once per sentence.
    /// </summary>
    private Then Failed(Session session, Failure failure)
    {
        lock (gate)
        {
            session.AfterFailure = true;
        }

        if (failure.Reason is ItemFactChange.NotInStash or OpenWithCopies.NoSession)
        {
            // Deleted with its Undo still standing, or for good: Store's record says which.
            Compare();
            return Then.Wait;
        }

        Name(session, failure);
        return Then.Rest;
    }

    /// <summary>
    /// After an import has replaced the stash (G36, G27): the start's adoptions not yet said, and every item's adoption
    /// history, are forgotten, since the imported store gives their identities to other items; the imported store's
    /// sessions are watched again at the listing's first change with it, as after a Move.
    /// </summary>
    public void ForgetReplacedStash()
    {
        lock (gate)
        {
            unsaid.Clear();
            namedListings.Clear();
        }

        replaced.Forget();
    }

    /// <summary>
    /// A classification or adoption asked while a Move, an export or an import is asked is put off, unnamed: the stash
    /// opened afterwards classifies every copy and adopts what awaits as a start does, and the listing's change asks
    /// again (State: Move, G27).
    /// </summary>
    private Then WhileMoving(Session session)
    {
        lock (gate)
        {
            session.AfterFailure = true;
        }

        return Then.Wait;
    }

    /// <summary>The session answered without failing: a failure named before is named again should it recur.</summary>
    private void Answered(Session session)
    {
        lock (gate)
        {
            session.Named.Clear();
        }
    }

    private void Name(Session session, Failure failure)
    {
        lock (gate)
        {
            if (!session.Named.Add(failure.Sentence))
            {
                return;
            }
        }

        notifications.Raise(failure);
    }

    /// <summary>
    /// A round has answered: asked again at once if a reason came meanwhile, else once a second while the copy is held
    /// or no watcher watches it, else at the next change.
    /// </summary>
    private void Ended(Session session, Then then)
    {
        bool again;
        lock (gate)
        {
            session.Running = false;
            again = session.AskAgain && IsCurrent(session);
            session.AskAgain = false;
        }

        if (again)
        {
            Ask(session);
            return;
        }

        lock (gate)
        {
            // Changed only while current, so never after the session's timer is disposed; the timer's clock takes no lock of this part's.
            if (IsCurrent(session))
            {
                var everySecond = then == Then.AskEverySecond || (then == Then.Rest && session.Watcher is null);
                session.Timer!.Change(everySecond ? AskedEvery : Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            }
        }
    }

    /// <summary>An edit opened, closed or ended its opening: a rewrite that waited for its item's edit is asked about again.</summary>
    private void EditingChanged()
    {
        List<Session> freed;
        lock (gate)
        {
            freed = [.. sessions.Values.Where(session => session.AwaitingEdit && !editing.Occupies(session.Id))];
            foreach (var session in freed)
            {
                session.AwaitingEdit = false;
            }
        }

        foreach (var session in freed)
        {
            onUiThread(() => Ask(session));
        }
    }

    /// <summary>
    /// A change of the listing other than this part's own reading: a session whose last round failed is asked again. The
    /// first change with a store open (raised before its first load) has every recorded session watched again.
    /// </summary>
    private void ListingChanged()
    {
        if (listing.Store is { } store)
        {
            if (FirstChangeWith(store))
            {
                _ = WatchedAgain(store);
            }
            else if (Unsaid())
            {
                _ = Say(store, []);
            }
        }

        if (Volatile.Read(ref ownRefreshes) > 0)
        {
            return;
        }

        List<Session> failed;
        lock (gate)
        {
            failed = [.. sessions.Values.Where(session => session.AfterFailure)];
            foreach (var session in failed)
            {
                session.AfterFailure = false;
            }
        }

        foreach (var session in failed)
        {
            onUiThread(() => Ask(session));
        }
    }

    private bool Unsaid()
    {
        lock (gate)
        {
            return unsaid.Count > 0;
        }
    }

    /// <summary>Whether <paramref name="store"/> is not yet the store whose sessions were asked for, which it now becomes.</summary>
    private bool FirstChangeWith(StashStore store)
    {
        lock (gate)
        {
            if (disposed || (sessionsAskedOf?.TryGetTarget(out var asked) == true && ReferenceEquals(asked, store)))
            {
                return false;
            }

            sessionsAskedOf = new WeakReference<StashStore>(store);
            return true;
        }
    }

    /// <summary>
    /// The start's sessions watched again and its adoptions said, asked once, since only the first answer carries the
    /// adoptions; a failed asking is asked again at the next change of the listing.
    /// </summary>
    private async Task WatchedAgain(StashStore store)
    {
        if (!await Settle(store))
        {
            lock (gate)
            {
                sessionsAskedOf = null;
            }
        }
    }

    /// <summary>The listing read again after this part's own change (as after every change of the stash), which is no reason to ask again.</summary>
    private async Task OwnRefresh()
    {
        Interlocked.Increment(ref ownRefreshes);
        try
        {
            await listing.Refresh();
        }
        finally
        {
            Interlocked.Decrement(ref ownRefreshes);
        }
    }

    private bool IsCurrent(Session session) => !disposed && sessions.TryGetValue(session.Id, out var current) && ReferenceEquals(current, session);

    private static bool SamePath(string watched, string copy) => string.Equals(watched, copy, StringComparison.OrdinalIgnoreCase);

    /// <summary>What a round leaves the session to: the next change (and, unwatched, the timer), the timer, or only the next change.</summary>
    private enum Then
    {
        Rest,
        AskEverySecond,
        Wait,
    }

    /// <summary>One item's session as this part watches it: its copy's path, its watcher and timer, and where its asking stands.</summary>
    /// <param name="born">How many askings of Store's sessions had gone out when it was made.</param>
    private sealed class Session(long id, string copyPath, long born) : IDisposable
    {
        public long Id => id;

        public string CopyPath => copyPath;

        public long Born => born;

        public FileSystemWatcher? Watcher { get; set; }

        public ITimer? Timer { get; set; }

        /// <summary>A classification or adoption of the session runs.</summary>
        public bool Running { get; set; }

        /// <summary>A reason to ask came while one ran.</summary>
        public bool AskAgain { get; set; }

        /// <summary>A rewrite awaits adoption until the item's edit ends.</summary>
        public bool AwaitingEdit { get; set; }

        /// <summary>The last round failed: the next listing change asks again.</summary>
        public bool AfterFailure { get; set; }

        /// <summary>The failures named since the session last answered without one.</summary>
        public HashSet<string> Named { get; } = [];

        public void Dispose()
        {
            Watcher?.Dispose();
            Timer?.Dispose();
        }
    }
}
