using ImageStash.Store;

namespace ImageStash.Window;

/// <summary>
/// What the footer says on its left, as the window draws it: Enter's handoff, naming the paste target's app or Copy and
/// how many items while several are selected, with <c>Ctrl+C</c>'s copy beside it while there is a target (G8, G9); and
/// for two seconds after a copy's write has returned, "✓ Copied" and what went on the clipboard in their place, then the
/// hints as they are by then (G10). Read whenever the footer is drawn, so the count follows the selection; the window's
/// one timer runs <see cref="Sweep"/>.
/// </summary>
public sealed class FooterHints
{
    /// <summary>How long a copy's confirmation stands in for the hints.</summary>
    public static readonly TimeSpan ConfirmedFor = TimeSpan.FromSeconds(2);

    private readonly Listing listing;
    private readonly PasteTargetHolder pasteTarget;
    private readonly CopyConfirmation confirmation;

    public FooterHints(Listing listing, PasteTargetHolder pasteTarget, Handoff handoff, TimeProvider clock)
    {
        this.listing = listing;
        this.pasteTarget = pasteTarget;
        confirmation = new CopyConfirmation(clock, ConfirmedFor);
        handoff.Copied += items => confirmation.Confirm(Copied(items));
        // A copy, paste or refresh starting: its busy state names it, and an earlier copy's confirmation gives way (G35).
        handoff.Changed += () =>
        {
            if (handoff.Busy is not null)
            {
                confirmation.Withdraw();
            }
        };
        confirmation.Changed += () => Changed?.Invoke();
    }

    /// <summary>Raised when a copy's confirmation shows, and when it has gone.</summary>
    public event Action? Changed;

    /// <summary>The copy's confirmation while it shows, or null while the hints do.</summary>
    public string? Confirmation => confirmation.Shown;

    /// <summary>The footer's words now: the copy's confirmation, or the hints for the target and the selection as they are.</summary>
    public IReadOnlyList<string> Texts => confirmation.Shown is { } confirmed ? [confirmed] : PasteTargetHolder.Hints(pasteTarget.Current, listing.Selection.Count);

    /// <summary>Lets a confirmation whose two seconds have passed give way to the hints.</summary>
    public void Sweep() => confirmation.Sweep();

    /// <summary>What went on the clipboard, never that it arrived (G10): one item by its name, or its file's name when it has none; several by count.</summary>
    public static string Copied(IReadOnlyList<StashItem> items) =>
        items.Count == 1 ? $"✓ Copied “{Handoff.Subject(items)}”" : $"✓ Copied {Handoff.Subject(items)}";
}
