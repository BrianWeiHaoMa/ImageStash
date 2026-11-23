using ImageStash.Seams;
using ImageStash.Store;

namespace ImageStash.Window;

/// <summary>
/// The preview's confirmation of a copy of its item, where the footer's hints are out of sight (G10, G19): once the copy's
/// write has returned, the button that names the copy, Copy beside the handoff button while there is a paste target, or
/// with none the handoff button, which then reads Copy, reads <see cref="Words"/> for <see cref="ShownFor"/>, then as
/// before. A copy of any other item confirms nothing here, and the words go when the preview closes or moves to another
/// item, so they never stand on a picture that was not copied.
/// </summary>
public sealed class PreviewCopyConfirmation
{
    /// <summary>How long the button reads <see cref="Words"/>.</summary>
    public static readonly TimeSpan ShownFor = TimeSpan.FromSeconds(1.5);

    /// <summary>What went on the clipboard, never that it arrived (G10).</summary>
    public const string Words = "Copied ✓";

    private readonly CopyConfirmation confirmation;

    /// <param name="previewed">The item the preview shows while it is open, else null.</param>
    public PreviewCopyConfirmation(Handoff handoff, Func<StashItem?> previewed, TimeProvider clock)
    {
        confirmation = new CopyConfirmation(clock, ShownFor);
        handoff.Copied += items =>
        {
            if (items is [var one] && one.Id == previewed()?.Id)
            {
                confirmation.Confirm(Words);
            }
        };
        confirmation.Changed += () => Changed?.Invoke();
    }

    /// <summary>Raised when the words show, and when they have gone.</summary>
    public event Action? Changed;

    /// <summary>The handoff button's words with <paramref name="target"/> the paste target: as the footer names Enter's handoff for one item (G19), or the words while it is the copy.</summary>
    public string HandoffWords(PasteTarget? target) => target is null && confirmation.Shown is { } words ? words : PasteTargetHolder.HandoffName(target, 1);

    /// <summary>The Copy button's words, shown beside the handoff button while there is a target.</summary>
    public string CopyWords(PasteTarget? target) => target is not null && confirmation.Shown is { } words ? words : "Copy";

    /// <summary>Lets words whose time has passed go.</summary>
    public void Sweep() => confirmation.Sweep();

    /// <summary>The preview closed or moved to another item: the words go at once.</summary>
    public void Withdraw() => confirmation.Withdraw();
}
