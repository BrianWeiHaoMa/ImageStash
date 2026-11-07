namespace ImageStash;

/// <summary>
/// Why an operation did not complete, in words a user reads and every caller shows unchanged (G35):
/// what was being done, to which file or item, and why.
/// </summary>
public sealed record Failure(string Action, string Subject, string Reason)
{
    /// <summary>The failure as one sentence, the way the tooltip and the window's notices show it.</summary>
    public string Sentence => $"{Action} failed: {Subject}, {Reason}";
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
