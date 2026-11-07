using Avalonia;
using ImageStash.Seams;

namespace ImageStash.Tests.Support;

/// <summary>
/// The Target seam for the portable checks: answers what is in front and what came before with preset readings,
/// confirms and pastes with preset outcomes and finds the caret with a preset answer, can hold a paste until told,
/// and records every confirmation, paste and caret lookup asked for, in order, into <see cref="Steps"/>, which a check
/// shares with the clipboard and the hide to see the order; <see cref="ReportLate"/> stands for a change the seam hears
/// of only after it last answered.
/// </summary>
public sealed class RecordingTarget(List<string> steps) : ITarget
{
    public RecordingTarget()
        : this([])
    {
    }

    public List<string> Steps { get; } = steps;

    public ForegroundReading InFront { get; set; }

    public PasteTarget? Before { get; set; }

    /// <summary>The next confirmations' answer; done unless a check says otherwise.</summary>
    public Outcome Confirms { get; set; } = Outcome.Done;

    /// <summary>The next pastes' answer; done unless a check says otherwise.</summary>
    public Outcome Pastes { get; set; } = Outcome.Done;

    /// <summary>While set, a paste waits for it before it answers.</summary>
    public Task? HoldPaste { get; set; }

    /// <summary>Runs at each confirmation, before it answers, so a check can look at the store or the window then.</summary>
    public Action? AtConfirm { get; set; }

    /// <summary>The targets every paste was sent to, and the bound each was given.</summary>
    public List<(PasteTarget Target, TimeSpan Bound)> Pasted { get; } = [];

    /// <summary>The next caret lookups' answer; none unless a check says otherwise.</summary>
    public Task<PixelRect?> Caret { get; set; } = Task.FromResult<PixelRect?>(null);

    /// <summary>The targets every caret lookup was asked for, and the bound each was given.</summary>
    public List<(PasteTarget Target, TimeSpan Bound)> CaretsAsked { get; } = [];

    public ForegroundReading Foreground()
    {
        Steps.Add("foreground");
        return InFront;
    }

    public PasteTarget? LastBeforeMine() => Before;

    public event Action? ReportedLate;

    /// <summary>Raises <see cref="ReportedLate"/>, as the platform's seam does on its own thread.</summary>
    public void ReportLate() => ReportedLate?.Invoke();

    public Outcome Confirm(PasteTarget target)
    {
        Steps.Add($"confirm {target.AppName}");
        AtConfirm?.Invoke();
        return Confirms;
    }

    public async Task<Outcome> ActivateAndPaste(PasteTarget target, TimeSpan bound)
    {
        Steps.Add($"activate and paste {target.AppName}");
        if (HoldPaste is { } hold)
        {
            await hold;
        }

        Pasted.Add((target, bound));
        Steps.Add($"pasted {target.AppName}");
        return Pastes;
    }

    public Task<PixelRect?> CaretOf(PasteTarget target, TimeSpan bound)
    {
        Steps.Add($"caret {target.AppName}");
        CaretsAsked.Add((target, bound));
        return Caret;
    }

    /// <summary>The target of the app Relay, the one the checks paste into.</summary>
    public static readonly PasteTarget Relay = App("Relay");

    /// <summary>A target of the app <paramref name="appName"/>, its handle made from the name so two apps differ.</summary>
    public static PasteTarget App(string appName) => new(appName.GetHashCode(), appName);
}
