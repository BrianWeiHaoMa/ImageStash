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
    /// The one sentence either kind is said in, the way the tooltip, the window's notices, notifications, an import's
    /// notice, Settings and the stash's opening state show it: "Can't" for a refusal, "Couldn't" for a failure, then the
    /// act, the subject and the reason, each once. The subject stands for the reason's leading "it" or "they", so the verb
    /// the reason chose agrees with it ("Can't paste: the clipboard holds no picture or file.", "Couldn't rename: cat.png
    /// is held open by another program."), or owns what its leading "its" names; a reason that is a clause of its own keeps
    /// the subject beside the act ("Couldn't take a screenshot (the screen): there is not enough memory to hold it.").
    /// One capital, one closing period. A failure for a held file ends by saying to try again (G35's retry has already
    /// run), and no other gains a next step.
    /// </summary>
    public string Sentence
    {
        get
        {
            var reason = Reason.Trim().TrimEnd('.').TrimEnd();
            var said = Subject.Length == 0 ? $"{Act}: {reason}"
                : Following(reason, "it ") is { } rest ? $"{Act}: {Subject} {rest}"
                : Following(reason, "they ") is { } several ? $"{Act}: {Subject} {several}"
                : Following(reason, "its ") is { } owned ? $"{Act}: {Subject}'s {owned}"
                : $"{Act} ({Subject}): {reason}";
            var sentence = $"{Opening} {said}.";
            // A held file named inside a composed reason ("its earlier rename cannot finish yet: …") counts too.
            return !Refused && reason.Contains(Files.HeldFileRetry.HeldReason, StringComparison.Ordinal) ? $"{sentence} {TryAgain}" : sentence;
        }
    }

    /// <summary>What a held file's failure ends with: the file was retried for L2's time, so a moment later it is likely free.</summary>
    public const string TryAgain = "Try again in a moment.";

    /// <summary>
    /// The tray tooltip's forms of this record, whole first, each shorter one still saying what failed or was refused
    /// (G33): the sentence, then the act with its subject, then the act alone, <see cref="ToolTipFormCount"/> in all.
    /// </summary>
    public IReadOnlyList<string> ToolTipForms => [Sentence, Subject.Length == 0 ? $"{Opening} {Act}" : $"{Opening} {Act}: {Subject}", $"{Opening} {Act}"];

    /// <summary>
    /// A reason carrying the platform's own words (an exception's message, Windows' text for an error): a plain
    /// <paramref name="lead"/> first, so the sentence says what went wrong before any of Windows' or .NET's words, which
    /// follow in brackets without their closing period.
    /// </summary>
    public static string PlatformSaid(string lead, string platformWords) => $"{lead} ({platformWords.Trim().TrimEnd('.').TrimEnd()})";

    public const int ToolTipFormCount = 3;

    /// <summary>
    /// Several records of one kind at their shortest, in one line when the tooltip cannot hold a line for each: one
    /// opening, then each act once, newest first ("Couldn't open the stash, read settings or save settings").
    /// </summary>
    public static string ToolTipActs(IReadOnlyList<Failure> oneKindNewestFirst)
    {
        var acts = oneKindNewestFirst.Select(failure => failure.Act).Distinct().ToList();
        var listed = acts.Count == 1 ? acts[0] : $"{string.Join(", ", acts[..^1])} or {acts[^1]}";
        return $"{oneKindNewestFirst[0].Opening} {listed}";
    }

    private string Opening => Refused ? "Can't" : "Couldn't";

    // The act loses its first capital only: a name the product writes with capitals inside it keeps them ("undo Revert
    // to original", "add to Favorites").
    private string Act => Action.Length > 1 && char.IsUpper(Action[0]) && char.IsLower(Action[1])
        ? char.ToLowerInvariant(Action[0]) + Action[1..]
        : Action;

    private static string? Following(string reason, string pronoun) =>
        reason.StartsWith(pronoun, StringComparison.Ordinal) ? reason[pronoun.Length..] : null;
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
