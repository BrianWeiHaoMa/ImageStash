namespace ImageStash;

/// <summary>
/// Why an operation did not complete, in words a user reads and every caller shows unchanged (G35):
/// what was being done, to which file or item, and why. A <see cref="Refused"/> one is a refusal: the person asked for
/// what ImageStash will not do, or cannot do now, and nothing went wrong.
/// </summary>
public sealed record Failure(string Action, string Subject, string Reason)
{
    /// <summary>
    /// True for a refusal, set where the record is made by the line governing that act ("refused", or nothing gone
    /// wrong), never read from <see cref="Reason"/>: one reason serves both, as an item no longer in the stash refuses a
    /// handoff (G8) and fails a use's record (G10).
    /// </summary>
    public bool Refused { get; init; }

    /// <summary>
    /// A refusal of <paramref name="action"/> on <paramref name="subject"/>. <paramref name="reason"/> is written as every
    /// reason is, to follow the subject: "it …", "its …" or, for several, "they …", or else a clause of its own.
    /// </summary>
    public static Failure Refusal(string action, string subject, string reason) => new(action, subject, reason) { Refused = true };

    /// <summary>
    /// The one sentence either kind is said in, the way the tooltip, the window's notices and Settings show it. A
    /// refusal: "Can't paste: the clipboard holds no picture or file.", its subject standing for the reason's leading
    /// "it" or "they" so the verb the reason chose agrees with it, else "Can't take a screenshot (the screen): your stash
    /// is still opening."; one capital, one closing period. A failure: "{Action} failed: {Subject}, {Reason}".
    /// </summary>
    public string Sentence => Refused ? RefusalSentence() : $"{Action} failed: {Subject}, {Reason}";

    private string RefusalSentence()
    {
        var act = Action.Length > 1 && char.IsUpper(Action[0]) && char.IsLower(Action[1])
            ? char.ToLowerInvariant(Action[0]) + Action[1..]
            : Action;
        var reason = Reason.TrimEnd('.');
        var said = Following("it ") is { } rest ? $"{act}: {Subject} {rest}"
            : Following("they ") is { } several ? $"{act}: {Subject} {several}"
            : Following("its ") is { } owned ? $"{act}: {Subject}'s {owned}"
            : $"{act} ({Subject}): {reason}";
        return $"Can't {said}.";

        string? Following(string pronoun) => reason.StartsWith(pronoun, StringComparison.Ordinal) ? reason[pronoun.Length..] : null;
    }
}

/// <summary>
/// What an operation that hands nothing back returns across a part or seam boundary: done, or a <see cref="Failure"/>.
/// An event the operating envelope admits (a held file, a refused write, unreadable input, a missing file) comes back
/// as a failure; an exception that reaches a caller is a defect.
/// </summary>
public class Outcome
{
    public static readonly Outcome Done = new(null);

    protected Outcome(Failure? failure) => Failure = failure;

    public Failure? Failure { get; }

    [System.Diagnostics.CodeAnalysis.MemberNotNullWhen(false, nameof(Failure))]
    public bool Succeeded => Failure is null;

    public static Outcome Fail(Failure failure) => new(failure);

    public static implicit operator Outcome(Failure failure) => Fail(failure);
}

/// <summary>An operation's value, or the <see cref="Failure"/> that stopped it.</summary>
public sealed class Outcome<T> : Outcome
{
    private Outcome(T? value, Failure? failure)
        : base(failure) => Value = value;

    /// <summary>The value when the operation succeeded; after a failure that came after a commit, what was committed.</summary>
    public T? Value { get; }

    public static Outcome<T> Of(T value) => new(value, null);

    public static new Outcome<T> Fail(Failure failure) => new(default, failure);

    /// <summary>A failure in a step after the commit: the commit stays in place and the caller gets what it committed.</summary>
    public static Outcome<T> FailAfterCommit(Failure failure, T committed) => new(committed, failure);

    public static implicit operator Outcome<T>(Failure failure) => Fail(failure);
}
