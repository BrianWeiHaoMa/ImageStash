using Avalonia.Controls;
using ImageStash.Store;

namespace ImageStash.Window;

/// <summary>A notice inside the stash window: what happened, in words shown unchanged, and an Undo when it has one.</summary>
public sealed class Notice
{
    internal Notice(string text, string? undoLabel, Func<Task<Outcome>>? undo, Func<Task>? left, Func<Notice, Notices, Control>? card, DateTimeOffset leavesAt)
    {
        Text = text;
        UndoLabel = undoLabel;
        UndoAction = undo;
        LeftAction = left;
        Card = card;
        LeavesAt = leavesAt;
    }

    public string Text { get; }

    /// <summary>The Undo button's label; null when the notice has no Undo.</summary>
    public string? UndoLabel { get; }

    internal Func<Task<Outcome>>? UndoAction { get; }

    /// <summary>What runs once the notice has left without its Undo, by its time or its ×; never after Undo.</summary>
    internal Func<Task>? LeftAction { get; }

    /// <summary>The card the notice is drawn as above the tray, made for it and its notices; null for the notice card.</summary>
    public Func<Notice, Notices, Control>? Card { get; }

    internal DateTimeOffset LeavesAt { get; set; }

    internal bool Hovered { get; set; }

    internal bool FieldFocused { get; set; }

    /// <summary>
    /// Since when the pointer has been over the notice or its field has had focus, whichever came first; while either
    /// lasts the notice's clock does not run.
    /// </summary>
    internal DateTimeOffset? HeldSince { get; set; }
}

/// <summary>
/// The window's stack of notices (G16): each stays six seconds from when it is raised, on its own clock, whether or not
/// the window is visible, and longer by as long as the pointer is over it or its field has focus; Undo acts while the
/// notice shows, at most once; a notice that leaves without it, by its time or its ×, runs what it was given for that,
/// once. A notice saying a change was made is raised from the completed outcome, never before it (R9). The clock is the
/// given <see cref="TimeProvider"/>; the window runs <see cref="Sweep"/> from a timer.
/// </summary>
public sealed class Notices(TimeProvider clock)
{
    public static readonly TimeSpan Lasting = TimeSpan.FromSeconds(6);

    /// <summary>How many items a notice names before it counts them instead.</summary>
    private const int NamedAtMost = 3;

    private readonly List<Notice> shown = [];

    /// <summary>The clock the notices keep time by, which the window's other times on screen read too.</summary>
    public TimeProvider Clock => clock;

    /// <summary>Raised after every change of the notices showing.</summary>
    public event Action? Changed;

    /// <summary>The notices showing, oldest first.</summary>
    public IReadOnlyList<Notice> Shown => shown;

    /// <param name="left">
    /// Started once the notice has left without its Undo, by <see cref="Sweep"/> or <see cref="Close"/>, which do not
    /// wait for it; never after <see cref="Undo"/>.
    /// </param>
    /// <param name="card">The card it is drawn as above the tray, instead of the notice card.</param>
    public Notice Raise(string text, string? undoLabel = null, Func<Task<Outcome>>? undo = null, Func<Task>? left = null, Func<Notice, Notices, Control>? card = null)
    {
        var notice = new Notice(text, undo is null ? null : undoLabel, undo, left, card, clock.GetUtcNow() + Lasting);
        shown.Add(notice);
        Changed?.Invoke();
        return notice;
    }

    /// <summary>A failure, naming its action, subject and reason (G35); it has no Undo.</summary>
    public Notice Raise(Failure failure) => Raise(failure.Sentence);

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

    /// <summary>Removes every notice whose time is up.</summary>
    public void Sweep()
    {
        var now = clock.GetUtcNow();
        var due = shown.Where(notice => notice.HeldSince is null && notice.LeavesAt <= now).ToList();
        if (due.Count == 0)
        {
            return;
        }

        shown.RemoveAll(due.Contains);
        Changed?.Invoke();
        foreach (var notice in due)
        {
            Left(notice);
        }
    }

    /// <summary>The pointer came over the notice, or left it.</summary>
    public void Hover(Notice notice, bool over)
    {
        notice.Hovered = over;
        Held(notice);
    }

    /// <summary>The notice's field took the focus, or lost it.</summary>
    public void FieldFocus(Notice notice, bool focused)
    {
        notice.FieldFocused = focused;
        Held(notice);
    }

    /// <summary>
    /// The clock stops while the pointer is over the notice or its field has focus, and runs on, by the time it was
    /// held, once neither lasts: each reason ends on its own, and time held for both counts once (G16).
    /// </summary>
    private void Held(Notice notice)
    {
        var now = clock.GetUtcNow();
        if (notice.Hovered || notice.FieldFocused)
        {
            notice.HeldSince ??= now;
        }
        else if (notice.HeldSince is { } since)
        {
            notice.LeavesAt += now - since;
            notice.HeldSince = null;
        }
    }

    /// <summary>The window hid: no pointer is over any notice.</summary>
    public void EndHovers()
    {
        foreach (var notice in shown)
        {
            Hover(notice, over: false);
        }
    }

    /// <summary>The card's ×: the notice leaves at once, as if its time were up.</summary>
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
    /// Runs the notice's Undo if the notice still shows; it leaves as the action starts, so a second press does
    /// nothing, and a failure the action reports is raised as a notice of its own (G35).
    /// </summary>
    public async Task Undo(Notice notice)
    {
        if (notice.UndoAction is not { } undo || !shown.Remove(notice))
        {
            return;
        }

        Changed?.Invoke();
        var outcome = await undo();
        if (!outcome.Succeeded)
        {
            Raise(outcome.Failure);
        }
    }

    /// <summary>An item by its name, or by its file's name when it has none (G17: nothing requires a name).</summary>
    private static string Name(StashItem item) => item.Name.Length > 0 ? $"“{item.Name}”" : item.FileName;

    /// <summary>A notice just taken off the stack without its Undo; it is never on it again, so this runs once.</summary>
    private static void Left(Notice notice)
    {
        if (notice.LeftAction is { } left)
        {
            _ = left();
        }
    }
}
