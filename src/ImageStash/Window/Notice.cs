using Avalonia.Controls;
using ImageStash.Store;

namespace ImageStash.Window;

/// <summary>A notice inside the stash window: what happened, in words shown unchanged, and an Undo when it has one.</summary>
public sealed class Notice
{
    internal Notice(string text, string? undoLabel, Func<Task<Outcome>>? undo, Func<Task>? left, Func<Notice, Notices, Control>? card, bool namesAProblem, UserAct? act)
    {
        Text = text;
        UndoLabel = undoLabel;
        UndoAction = undo;
        LeftAction = left;
        Card = card;
        NamesAProblem = namesAProblem;
        Act = act;
    }

    public string Text { get; }

    /// <summary>
    /// Whether the notice names a problem: a file skipped as unsupported, an unreadable file, a refusal or a failure
    /// (G12, G35). In the stash window such a notice stays past its time (G16); a result naming none does not.
    /// </summary>
    public bool NamesAProblem { get; }

    /// <summary>The person's act that raised the notice, by its entry point; null for one no act of theirs raised.</summary>
    public UserAct? Act { get; }

    /// <summary>Everything the card reads out as text, its buttons aside: the words a notice's time is counted from (G16).</summary>
    internal string Words => Text;

    /// <summary>The Undo button's label; null when the notice has no Undo.</summary>
    public string? UndoLabel { get; }

    internal Func<Task<Outcome>>? UndoAction { get; }

    /// <summary>What runs once the notice's time ends, or it leaves before by its × or a later act, without its Undo; never after Undo.</summary>
    internal Func<Task>? LeftAction { get; }

    /// <summary>The card the notice is drawn as above the tray, made for it and its notices; null for the notice card.</summary>
    public Func<Notice, Notices, Control>? Card { get; }

    /// <summary>How much of its time the notice has left, counted up to <see cref="RunningSince"/> while its clock runs.</summary>
    internal TimeSpan TimeLeft { get; set; }

    /// <summary>Since when the notice's clock has been running; null while it is stopped.</summary>
    internal DateTimeOffset? RunningSince { get; set; }

    internal bool Hovered { get; set; }

    internal bool FieldFocused { get; set; }

    /// <summary>Whether the notice's time has ended: its Undo, if it had one, offered no more.</summary>
    internal bool TimeEnded { get; set; }

    /// <summary>Whether a later act of the same action has taken the notice over, so it does not stay past its time.</summary>
    internal bool TakenOver { get; set; }

    /// <summary>Whether what the notice was given for leaving has started, or never will since its Undo ran.</summary>
    internal bool LeaveSpent { get; set; }
}

/// <summary>
/// The person's one act, by its entry point (a paste, a drop, Import files…, a Delete, a rename), which every notice it
/// raises carries, whichever part raises it, and what the notice starts on leaving too (G16): the notices of one act
/// stand together, and in the stash window a notice of a later act of the same action takes the place of an earlier
/// act's notices that name a problem, later meaning asked for later. An entry point opens it with <see cref="Begin"/> at
/// its start, before any wait, and every notice raised in its flow, across awaits, carries it.
/// </summary>
public sealed class UserAct
{
    private static readonly AsyncLocal<UserAct?> Acting = new();
    private static long asked;

    private UserAct(string action, long order)
    {
        Action = action;
        Order = order;
    }

    /// <summary>The entry point the act came by, the same for every act of that action.</summary>
    public string Action { get; }

    /// <summary>When the person asked for it: a later act has a larger order.</summary>
    internal long Order { get; }

    /// <summary>The act whose flow this is, if any.</summary>
    internal static UserAct? Current => Acting.Value;

    /// <summary>
    /// Begins a new act of <paramref name="action"/> for this flow until the scope is disposed; inside an act already, as
    /// when one entry point calls another or a part's search runs within an import, it is that act, whichever part raises.
    /// </summary>
    public static IDisposable Begin(string action) => Within(Acting.Value ?? new UserAct(action, Interlocked.Increment(ref asked)));

    /// <summary>Runs this flow as <paramref name="act"/>'s, none when null, until the scope is disposed.</summary>
    internal static IDisposable Within(UserAct? act)
    {
        var before = Acting.Value;
        Acting.Value = act;
        return new Scope(before);
    }

    private sealed class Scope(UserAct? before) : IDisposable
    {
        public void Dispose() => Acting.Value = before;
    }
}

/// <summary>
/// A stack of notices (G16), oldest first, each on its own clock, which stops while the pointer is over the notice or its
/// field has focus. Above the tray (<see cref="Notices(TimeProvider)"/>) each notice lasts six seconds and its clock runs
/// whether or not the stash window shows. Inside the stash window (<see cref="InWindow"/>) a notice lasts as long as its
/// words need, six seconds or two and one more for every three and a half words, up to twenty, and its clock runs only
/// while the window shows (<see cref="WindowShows"/>), so a notice raised into a hidden window is there, its whole time
/// ahead of it, when the window shows. There, one that names a problem (<see cref="Notice.NamesAProblem"/>) stays past
/// its time, until its × or a notice of a later act of the same action (<see cref="UserAct"/>), which takes its place; one
/// still offering its Undo when taken over is not cut short, and leaves when its time ends. Undo acts only while the
/// notice still offers it (<see cref="OffersUndo"/>), for the notice's time and at most once; a notice whose time ends
/// (<see cref="Sweep"/>, <see cref="EndUndosWhileHidden"/>), stays or not, or that is closed by its × or taken over
/// runs what it was given for leaving, once, as its act's. A notice saying a change was made is raised from the
/// completed outcome, never before it (R9). The clock is the given <see cref="TimeProvider"/>; the window runs
/// <see cref="Sweep"/> from a timer. Nothing of a notice is stored (G28).
/// </summary>
public sealed class Notices
{
    /// <summary>A notice's time above the tray, and the least a notice in the stash window has (G16).</summary>
    public static readonly TimeSpan Lasting = TimeSpan.FromSeconds(6);

    /// <summary>The most a notice in the stash window has, however many its words (G16).</summary>
    public static readonly TimeSpan LastingAtMost = TimeSpan.FromSeconds(20);

    /// <summary>The action of every notice's Undo, the person's act of its own.</summary>
    public const string Undoing = "Undo";

    /// <summary>How many items a notice names before it counts them instead.</summary>
    private const int NamedAtMost = 3;

    private readonly TimeProvider clock;
    private readonly bool inWindow;
    private readonly List<Notice> shown = [];

    /// <summary>For each action, the latest of its acts that has raised a notice here.</summary>
    private readonly Dictionary<string, long> latestRaised = [];

    /// <summary>Whether the clocks run: always above the tray; in the stash window, while it shows.</summary>
    private bool clockRuns;

    /// <summary>What notices started on leaving that has not yet answered.</summary>
    private int finishing;

    /// <summary>The notifications above the tray: six seconds each, whether or not the stash window shows (G16).</summary>
    public Notices(TimeProvider clock)
        : this(clock, inWindow: false)
    {
    }

    private Notices(TimeProvider clock, bool inWindow)
    {
        this.clock = clock;
        this.inWindow = inWindow;
        clockRuns = !inWindow;
    }

    /// <summary>The stash window's notices: each as long as its words need, on time the window shows; hidden until told it shows.</summary>
    public static Notices InWindow(TimeProvider clock) => new(clock, inWindow: true);

    /// <summary>The clock the notices keep time by, which the window's other times on screen read too.</summary>
    public TimeProvider Clock => clock;

    /// <summary>Raised after every change of the notices showing.</summary>
    public event Action? Changed;

    /// <summary>
    /// Raised once something a notice started on leaving has answered, on the thread it answered on, for a wait to read
    /// <see cref="Finishing"/> again.
    /// </summary>
    public event Action? FinishAnswered;

    /// <summary>The notices showing, oldest first.</summary>
    public IReadOnlyList<Notice> Shown => shown;

    /// <summary>Whether something a notice started on leaving, by its time or its ×, has not yet answered.</summary>
    public bool Finishing => Volatile.Read(ref finishing) > 0;

    /// <param name="left">
    /// Started once, without waiting for it, when the notice's time ends, though it stays, or it is closed by
    /// <see cref="Close"/> or taken over before that; never after <see cref="Undo"/>.
    /// </param>
    /// <param name="card">The card it is drawn as above the tray, instead of the notice card.</param>
    /// <param name="namesAProblem">Whether <paramref name="text"/> names a skipped or unreadable file, a refusal or a failure.</param>
    /// <returns>
    /// The notice, carrying the act whose flow raised it; in the stash window, one naming a problem without an Undo is not
    /// shown when a later act of its action has raised a notice already, which takes its place.
    /// </returns>
    public Notice Raise(string text, string? undoLabel = null, Func<Task<Outcome>>? undo = null, Func<Task>? left = null, Func<Notice, Notices, Control>? card = null,
        bool namesAProblem = false)
    {
        var notice = new Notice(text, undo is null ? null : undoLabel, undo, left, card, namesAProblem, UserAct.Current);
        notice.TimeLeft = inWindow ? LastingFor(notice.Words) : Lasting;
        Settle(notice, clock.GetUtcNow());
        var replaced = inWindow ? TakeOver(notice) : [];
        var placeTaken = notice.TakenOver && notice.NamesAProblem && notice.UndoAction is null;
        if (!placeTaken)
        {
            shown.Add(notice);
        }
        else
        {
            replaced.Add(notice);
        }

        Changed?.Invoke();
        foreach (var gone in replaced)
        {
            Left(gone);
        }

        return notice;
    }

    /// <summary>A failure, naming its action, subject and reason (G35); it has no Undo, and names a problem.</summary>
    public Notice Raise(Failure failure) => Raise(failure.Sentence, namesAProblem: true);

    /// <summary>
    /// A notice's time in the stash window for <paramref name="words"/> (G16): six seconds, or two seconds and one more for
    /// every three and a half words when that is longer, up to twenty; a word is what stands between spaces.
    /// </summary>
    public static TimeSpan LastingFor(string words)
    {
        var count = words.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        var reading = TimeSpan.FromSeconds(2 + count / 3.5);
        return reading < Lasting ? Lasting : reading > LastingAtMost ? LastingAtMost : reading;
    }

    /// <summary>Items as a notice names them: by name for up to three, else by count.</summary>
    public static string Describe(IReadOnlyList<StashItem> items)
    {
        if (items.Count > NamedAtMost)
        {
            return $"{items.Count} items";
        }

        var names = items.Select(Name).ToList();
        return names.Count == 1 ? names[0] : string.Join(", ", names.Take(names.Count - 1)) + " and " + names[^1];
    }

    /// <summary>
    /// The one test of whether <paramref name="notice"/> still offers its Undo: it shows, its Undo has not run, and its
    /// time has not ended, though a notice naming a problem stays past it (G16, G18). Move, Export stash… and Import
    /// stash… wait while one does; <see cref="Undo"/> acts only while it does. Not read from the clock: a notice whose
    /// time ran out moments ago still counts until a sweep ends it, so Move's wait ends it through the same transition
    /// and waits for what that starts.
    /// </summary>
    public bool OffersUndo(Notice notice) => notice.UndoAction is not null && !notice.TimeEnded && shown.Contains(notice);

    /// <summary>
    /// Removes every notice whose time is up, one whose clock a hide or a hover stopped after its time ran out included:
    /// stopping a clock never gives back time already spent.
    /// </summary>
    public void Sweep()
    {
        var now = clock.GetUtcNow();
        TimeUp(shown.Where(notice => !notice.TimeEnded && TimeLeftAt(notice, now) <= TimeSpan.Zero).ToList());
    }

    /// <summary>
    /// The stash window began or stopped showing, as <see cref="WindowShowing"/> judges it: the notices' clocks run only
    /// while it shows, and once it stops no pointer is over any notice (G16). Only the stash window's notices are told.
    /// </summary>
    public void WindowShows(bool shows)
    {
        if (!inWindow)
        {
            throw new InvalidOperationException("The notifications above the tray keep their time whether or not the stash window shows.");
        }

        if (clockRuns == shows)
        {
            return;
        }

        clockRuns = shows;
        // One moment for every notice, so none is charged for the time the others take.
        var now = clock.GetUtcNow();
        foreach (var notice in shown)
        {
            notice.Hovered &= shows;
            Settle(notice, now);
        }
    }

    /// <summary>
    /// For Move…, Export stash… and Import stash…, which never wait on a clock that is not running (State: Move): while
    /// the stash window does not show, the time of every notice that still offers its Undo ends now, as at its time, so
    /// what it was given for leaving starts and <see cref="Finishing"/> covers it until it has answered. With the window
    /// showing, and above the tray, nothing changes.
    /// </summary>
    public void EndUndosWhileHidden()
    {
        if (!clockRuns)
        {
            TimeUp(shown.Where(OffersUndo).ToList());
        }
    }

    /// <summary>The pointer came over the notice, or left it.</summary>
    public void Hover(Notice notice, bool over)
    {
        notice.Hovered = over;
        Settle(notice, clock.GetUtcNow());
    }

    /// <summary>The notice's field took the focus, or lost it.</summary>
    public void FieldFocus(Notice notice, bool focused)
    {
        notice.FieldFocused = focused;
        Settle(notice, clock.GetUtcNow());
    }

    /// <summary>
    /// The card's ×: the notice leaves at once, a notice staying past its time too, running what it was given for leaving
    /// as its time's end does, unless that end already ran it.
    /// </summary>
    public void Close(Notice notice)
    {
        if (shown.Remove(notice))
        {
            Changed?.Invoke();
            Left(notice);
        }
    }

    /// <summary>
    /// Takes the notice down without running its Undo or what it was given for leaving: a later notice has replaced it,
    /// and the change its buttons would act on is no longer there to act on.
    /// </summary>
    public void Dismiss(Notice notice)
    {
        if (shown.Remove(notice))
        {
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// Runs the notice's Undo if the notice still offers it once every notice whose time is up has left, so it never
    /// acts past its time whenever the sweep comes; it leaves as the action starts, its words with it since the change
    /// they report is being undone, so a second press does nothing; a failure the action reports is raised as a notice of
    /// its own (G35). The Undo is the person's act of its own: what it raises carries it, and stays as any failure does.
    /// </summary>
    public async Task Undo(Notice notice)
    {
        using var acting = UserAct.Begin(Undoing);
        Sweep();
        if (!OffersUndo(notice))
        {
            return;
        }

        shown.Remove(notice);
        notice.LeaveSpent = true;
        Changed?.Invoke();
        var outcome = await notice.UndoAction!();
        if (!outcome.Succeeded)
        {
            Raise(outcome.Failure);
        }
    }

    /// <summary>
    /// The one transition of a notice's time ending, whatever ends it: its Undo is offered no more; in the stash window a
    /// notice naming a problem that no later act took over stays, its words and its ×, and every other leaves; each runs
    /// what it was given for leaving, once.
    /// </summary>
    private void TimeUp(IReadOnlyList<Notice> due)
    {
        if (due.Count == 0)
        {
            return;
        }

        foreach (var notice in due)
        {
            notice.TimeEnded = true;
        }

        shown.RemoveAll(notice => due.Contains(notice) && !(inWindow && notice.NamesAProblem && !notice.TakenOver));
        Changed?.Invoke();
        foreach (var notice in due)
        {
            Left(notice);
        }
    }

    /// <summary>
    /// The same action's rule for a notice just raised in the stash window (G16): when a later act of its action has
    /// raised a notice already, it is taken over at once; otherwise each earlier act's notice of that action naming a
    /// problem is: one still offering its Undo keeps it until its time ends and then leaves, and every other is taken off
    /// and returned, to run what it was given for leaving once the stack has changed. The notices of one act, and those
    /// of no act, never take each other's place.
    /// </summary>
    private List<Notice> TakeOver(Notice notice)
    {
        if (notice.Act is not { } act)
        {
            return [];
        }

        if (latestRaised.TryGetValue(act.Action, out var latest) && latest > act.Order)
        {
            notice.TakenOver = true;
            return [];
        }

        latestRaised[act.Action] = act.Order;
        var earlier = shown.Where(other => other.NamesAProblem && other.Act is { } by && by.Action == act.Action && by.Order < act.Order).ToList();
        foreach (var other in earlier)
        {
            other.TakenOver = true;
        }

        var replaced = earlier.Where(other => !OffersUndo(other)).ToList();
        shown.RemoveAll(replaced.Contains);
        return replaced;
    }

    /// <summary>
    /// Runs or stops the notice's clock as it should be now: running while the clocks run and neither the pointer is over
    /// it nor its field has focus, each hold ending on its own and time held for both counted once (G16); stopped, its
    /// time left is what it was when it stopped.
    /// </summary>
    private void Settle(Notice notice, DateTimeOffset now)
    {
        var runs = clockRuns && !notice.Hovered && !notice.FieldFocused;
        if (runs)
        {
            notice.RunningSince ??= now;
        }
        else if (notice.RunningSince is not null)
        {
            notice.TimeLeft = TimeLeftAt(notice, now);
            notice.RunningSince = null;
        }
    }

    /// <summary>What is left of the notice's time at <paramref name="now"/>; while its clock is stopped, what was left when it stopped.</summary>
    private static TimeSpan TimeLeftAt(Notice notice, DateTimeOffset now) =>
        notice.RunningSince is { } since ? notice.TimeLeft - (now - since) : notice.TimeLeft;

    /// <summary>An item by its name, or by its file's name when it has none (G17: nothing requires a name).</summary>
    private static string Name(StashItem item) => item.Name.Length > 0 ? $"“{item.Name}”" : item.FileName;

    /// <summary>
    /// A notice whose time ended, or taken off the stack without its Undo: what it was given for leaving starts, once,
    /// as the act that raised it, so what that names is the act's, whatever ends the notice.
    /// </summary>
    private void Left(Notice notice)
    {
        if (notice.LeaveSpent)
        {
            return;
        }

        notice.LeaveSpent = true;
        if (notice.LeftAction is { } left)
        {
            using var acting = UserAct.Within(notice.Act);
            _ = Finish(left);
        }
    }

    /// <summary>What the notice was given for leaving, counted in <see cref="Finishing"/> until it has answered.</summary>
    private async Task Finish(Func<Task> left)
    {
        Interlocked.Increment(ref finishing);
        try
        {
            await left();
        }
        finally
        {
            Interlocked.Decrement(ref finishing);
            FinishAnswered?.Invoke();
        }
    }
}
