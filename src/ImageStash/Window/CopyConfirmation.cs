namespace ImageStash.Window;

/// <summary>
/// A copy's confirmation where the hint that promised it sits (G10): words given once the copy's write has returned,
/// never before (R9), shown for <paramref name="shownFor"/> on <paramref name="clock"/>; a later copy's words replace them
/// and show as long from then. <see cref="Shown"/> reads the clock, so the words are gone on time whenever they are read;
/// <see cref="Sweep"/>, run from the window's one timer, raises <see cref="Changed"/> once they have gone, so the window
/// draws what they stood in for, as it is by then.
/// </summary>
public sealed class CopyConfirmation(TimeProvider clock, TimeSpan shownFor)
{
    private string? words;
    private DateTimeOffset until;

    /// <summary>Raised when words are confirmed, and when they have gone or are withdrawn.</summary>
    public event Action? Changed;

    /// <summary>The words while they show, or null.</summary>
    public string? Shown => words is not null && clock.GetUtcNow() < until ? words : null;

    /// <summary>Shows <paramref name="confirmed"/> from now, in place of any words still showing.</summary>
    public void Confirm(string confirmed)
    {
        words = confirmed;
        until = clock.GetUtcNow() + shownFor;
        Changed?.Invoke();
    }

    /// <summary>Lets words whose time has passed go.</summary>
    public void Sweep()
    {
        if (words is not null && clock.GetUtcNow() >= until)
        {
            words = null;
            Changed?.Invoke();
        }
    }

    /// <summary>Takes the words away at once: what they confirmed is no longer in view.</summary>
    public void Withdraw()
    {
        if (words is not null)
        {
            words = null;
            Changed?.Invoke();
        }
    }
}
