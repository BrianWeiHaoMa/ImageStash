using Avalonia.Controls;
using Avalonia.Input;
using ImageStash.Seams;
using ImageStash.Store;

namespace ImageStash.Window;

/// <summary>
/// The paste into the target, in its order (G8, G10, N3): the flyout hides as soon as the handoff is not refused and the
/// Enter that asked for it has come up, while the clipboard write runs on behind it; the target is brought to the front
/// and sent its one keystroke only once that write has completed; the use is recorded after the keystroke.
/// </summary>
public sealed partial class Handoff
{
    /// <summary>
    /// The paste into <paramref name="target"/>. Refused by name, in the flyout, which stays: while the listing holds no
    /// store or a whole-stash act copies, and for the items the store's confirmation refuses (G8, G35). Otherwise the write
    /// starts, and, while the Enter that asked for it is still held, the paste waits within <see cref="PasteBound"/> for its
    /// release, so no other app is given the front while its repeats would reach it as Send (N3). Enter held past the
    /// bound, or an edit open (G2), hides nothing, activates nothing and sends nothing: the copy completes, and
    /// <see cref="Fallback"/>'s notification says so and why. Else <paramref name="hide"/>'s hide, without waiting for the
    /// write, which answers whether the flyout is hidden after it. Once the write has completed, Enter, the edit and the
    /// target are asked again, since the person may have pressed Enter, opened an edit or closed the app meanwhile; then the
    /// target is brought to the front and sent one paste keystroke. A window that is gone or cannot be sent input gets the
    /// copy that is made, then that notification, never a second write. A write that fails sends nothing and records
    /// nothing: behind a hidden flyout its failure is a notification where the fallback's would stand, since a notice in
    /// the window would wait for the next summon (G8, G35). One use is recorded after the keystroke, or once the copy alone is made (G10).
    /// The notification stands on the display <paramref name="hide"/> says the stash window stood on as the paste was
    /// asked (G16). A paste that hid the flyout starts the next summon on Recent once its use is recorded, unless the
    /// window was shown again meanwhile, whose search and selection are the person's (G1). The one-at-a-time gate is held
    /// from the write through the keystroke, so nothing else ImageStash writes lands on the clipboard before the target
    /// pastes it, and so are the items' file names, so no other item comes under them before then.
    /// </summary>
    public async Task<Outcome> Paste(TopLevel owner, IReadOnlyList<StashItem> items, PasteTarget target, PasteHide hide)
    {
        using var acting = UserAct.Begin(HandOverAct);
        if (items.Count == 0)
        {
            return Outcome.Done;
        }

        var stoodOn = hide.StoodOn;
        var replacements = listing.Replacements;
        await oneWriteAtATime.WaitAsync();
        try
        {
            if (Refusal(items, Action, replacements) is { } refused)
            {
                return Named(refused);
            }

            SetBusy($"Copying {Subject(items)} to the clipboard…");
            // Held through the keystroke: the target reads the files once it pastes.
            using var confirmed = await listing.Store!.Confirm(items, Action);
            var current = Confirmed(items, confirmed);
            if (!current.Succeeded)
            {
                return Named(current.Failure);
            }

            // Decoding and encoding a large still takes a moment, which the flyout does not wait out.
            var writing = WriteCurrent(owner, current.Value!);
            if (await Kept(hide) is { } kept)
            {
                // The flyout stays, so a held Enter's repeats stay with ImageStash, and nothing starts the next summon anew (G1).
                var copied = await writing;
                if (!copied.Succeeded)
                {
                    return Named(copied.Failure);
                }

                fallback.Raise(kept, target.AppName, stoodOn);
                await Record(items);
                return NotPasted(kept, target);
            }

            var reachable = targets.Confirm(target);
            var shownBefore = hide.TimesShown();
            var hidden = hide.Hide();
            var written = await writing;
            if (!written.Succeeded)
            {
                if (hidden)
                {
                    fallback.RaiseFailure(written.Failure, stoodOn);
                    return written.Failure;
                }

                return Named(written.Failure);
            }

            if (await Kept(hide) is { } since)
            {
                fallback.Raise(since, target.AppName, stoodOn);
                await Recorded(items, hidden, shownBefore, hide);
                return NotPasted(since, target);
            }

            if (reachable.Succeeded)
            {
                reachable = targets.Confirm(target);
            }

            if (!reachable.Succeeded)
            {
                fallback.Raise(reachable.Failure!.Reason, target.AppName, stoodOn);
                await Recorded(items, hidden, shownBefore, hide);
                return reachable;
            }

            SetBusy($"Bringing {target.AppName} to the front…");
            var pasted = await targets.ActivateAndPaste(target, PasteBound);
            if (!pasted.Succeeded)
            {
                fallback.Raise(pasted.Failure!.Reason, target.AppName, stoodOn);
            }

            await Recorded(items, hidden, shownBefore, hide);
            return pasted;
        }
        finally
        {
            SetBusy(null);
            oneWriteAtATime.Release();
        }
    }

    /// <summary>
    /// Why the window keeps the paste from the target now, or null: Enter still held past <see cref="PasteBound"/>, its
    /// release waited for meanwhile (N3), or an edit open (G2).
    /// </summary>
    private async Task<string?> Kept(PasteHide hide) =>
        !await LetGoInTime(Task.WhenAll(hide.OriginatingRelease, hide.CurrentRelease)) || !hide.CurrentRelease.IsCompleted
            ? PasteFallback.EnterHeld
            : hide.RefusalBeforePaste?.Invoke();

    /// <summary>The paste's outcome when <see cref="Kept"/> kept it: Enter held is said by the notification alone; an open edit declines it in the window too (G35).</summary>
    private Outcome NotPasted(string reason, PasteTarget target) => reason == PasteFallback.EnterHeld
        ? new Failure(PasteTargetReasons.Action, target.AppName, reason)
        : Named(Failure.Refusal(PasteTargetReasons.Action, target.AppName, reason));

    /// <summary>Whether <paramref name="letGo"/> completes within <see cref="PasteBound"/> on the handoff's clock: at once when it already has.</summary>
    private async Task<bool> LetGoInTime(Task letGo)
    {
        if (letGo.IsCompleted)
        {
            return true;
        }

        using var bound = new CancellationTokenSource();
        var first = await Task.WhenAny(letGo, Task.Delay(PasteBound, clock, bound.Token));
        await bound.CancelAsync();
        return first == letGo;
    }

    /// <summary>
    /// The paste's use, then, when it left the flyout <paramref name="hidden"/> and nothing has shown the window since
    /// <paramref name="shownBefore"/>, Recent with no search and its first item, the one just handed over, selected alone,
    /// for the next summon (G1, G4); the pinned window, a flyout a dialog or drag of its own kept up, or one the person
    /// summoned meanwhile, as it is.
    /// </summary>
    private async Task Recorded(IReadOnlyList<StashItem> items, bool hidden, int shownBefore, PasteHide hide)
    {
        await Record(items);
        if (hidden && hide.TimesShown() == shownBefore)
        {
            await listing.ShowScope(Scope.Recent, byPerson: false);
        }
    }
}

/// <summary>
/// The window's part in a paste into a target (G8): <see cref="Hide"/> hides the flyout as the paste hides it and answers
/// whether it is hidden after (G1, G2); <see cref="HeldKeys"/> are the keys the window holds down, an Enter among which the
/// paste waits to be let go before any other app is given the front (N3); with none, nothing is held.
/// </summary>
public sealed record PasteHide(Func<bool> Hide, PressOnce? HeldKeys = null)
{
    internal Task OriginatingRelease { get; init; } = Task.CompletedTask;
    internal Func<string?>? RefusalBeforePaste { get; init; }

    /// <summary>How many times the stash window has been shown; a change after the hide is a summon the person made (G1).</summary>
    internal Func<int> TimesShown { get; init; } = () => 0;

    /// <summary>The display the stash window stood on as the paste was asked; null when it is not known (G16).</summary>
    internal Display? StoodOn { get; init; }

    internal Task CurrentRelease => HeldKeys?.LetGo(Key.Enter) ?? Task.CompletedTask;

    // A release from a later press cannot validate the press whose release was never seen.
    internal PasteHide ForEnterPress() => this with { OriginatingRelease = CurrentRelease };
}
