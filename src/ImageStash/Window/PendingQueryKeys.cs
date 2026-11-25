using ImageStash.Store;

namespace ImageStash.Window;

/// <summary>
/// Keyboard movement and acts on its selection resolve in press order (G5, G7). A later accepted selection or
/// filter cancels unresolved input. Operations that have acquired their items start immediately and can finish
/// while the person moves on. Listing remains the owner of the displayed selection.
/// </summary>
public sealed class PendingQueryKeys(Listing listing, Func<IReadOnlyList<GridRow>> laidOutRows,
    Func<Direction, long, bool>? moveInPreview = null)
{
    private readonly Queue<Pressed> pressed = new();
    private bool draining;
    private Task drained = Task.CompletedTask;
    private long lastSelectionKey;

    internal Task PendingScopeChoice { get; private set; } = Task.CompletedTask;

    public Task Move(Direction direction) => Dispatch(input =>
    {
        if (moveInPreview?.Invoke(direction, input) != true)
        {
            listing.MoveFocus(direction, laidOutRows(), input);
        }
    }, WaitFor.Layout);

    internal async Task ChooseScope(Scope scope, Func<bool> browsing)
    {
        var reading = Task.CompletedTask;
        var decided = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        PendingScopeChoice = decided.Task;
        await Dispatch(input =>
        {
            if (browsing())
            {
                reading = listing.ShowScope(scope, byPerson: true, input);
            }
        }, WaitFor.ScopeChoice, decided);
        await reading;
    }

    /// <summary>Starts the act with the selected and focused items before the next queued key can move either.</summary>
    internal async Task<Outcome> ActOnSelection(Func<KeySelection, Task<Outcome>> act, bool allowExplicitTile = false,
        Func<Outcome>? viewUnavailable = null)
    {
        var finished = Task.FromResult(Outcome.Done);
        await WithSelection(selection => finished = act(selection), allowExplicitTile,
            viewUnavailable is null ? null : () => finished = Task.FromResult(viewUnavailable()));
        return await finished;
    }

    internal Task WithSelection(Action<KeySelection> apply, bool allowExplicitTile = false, Action? viewUnavailable = null)
    {
        // A tile chosen during a scope load already has an identity, even if the arriving scope will omit it.
        // After a failed read, a fresh handoff can take the selection still on screen unless a newer move needed the failed view.
        var takesShownSelection = (listing.SelectionReadPending && listing.HasExplicitSelectionAfter(lastSelectionKey)) ||
            (allowExplicitTile && listing.ShowsPriorViewAfterFailure && lastSelectionKey <= listing.LastSelectionReset);
        (IReadOnlyList<StashItem> Items, StashItem? Focused)? chosen =
            takesShownSelection ? ReadSelection() : null;
        var waitFor = chosen is not null ? WaitFor.EarlierKeys
            : allowExplicitTile ? WaitFor.ExplicitTileOrSelection : WaitFor.Selection;
        return Dispatch(input =>
        {
            var selection = chosen ?? ReadSelection();
            apply(new KeySelection(selection.Items, selection.Focused, input));
        }, waitFor, viewUnavailable: viewUnavailable);
    }

    internal Task WithCurrentView(Action<long> act) => Dispatch(act, WaitFor.CurrentView);

    private (IReadOnlyList<StashItem> Items, StashItem? Focused) ReadSelection()
    {
        var items = listing.SelectedItems;
        return (items, items.FirstOrDefault(item => item.Id == listing.Focused));
    }

    /// <summary>An ordered command that has no item target, such as Escape or the notice's Undo.</summary>
    public void Then(Action act) => _ = Dispatch(_ => act(), WaitFor.EarlierKeys);

    // A new search can supersede a scope without cancelling a separate request to import or paste.
    internal void AfterScopeChoice(Action act) => _ = Dispatch(_ => act(), WaitFor.AfterScopeChoice);

    private Task Dispatch(Action<long> act, WaitFor waitFor, TaskCompletionSource? decided = null, Action? viewUnavailable = null)
    {
        var input = listing.NextInput();
        if (waitFor is WaitFor.Layout or WaitFor.CurrentView or WaitFor.ScopeChoice)
        {
            lastSelectionKey = input;
        }

        var next = new Pressed(act, waitFor, input, lastSelectionKey, listing.Store, listing.Replacements, decided, viewUnavailable);
        if (!draining && Ready(next))
        {
            Run(next);
            return Task.CompletedTask;
        }

        pressed.Enqueue(next);
        if (!draining)
        {
            drained = Drain();
        }

        return drained;
    }

    private async Task Drain()
    {
        draining = true;
        try
        {
            while (pressed.TryPeek(out var next))
            {
                while (Current(next) && !Ready(next))
                {
                    await listing.Settle();
                }

                pressed.Dequeue();
                Run(next);
            }
        }
        finally
        {
            draining = false;
        }
    }

    // At the front of the queue, a scope can replace a pending tag choice and recover from a failed listing.
    private bool Ready(Pressed next) => next.WaitFor == WaitFor.ScopeChoice || !listing.TagChoicePending && (next.WaitFor switch
    {
        WaitFor.Layout => listing.Settled,
        // During a selection-preserving refresh, an act takes the selection the person can still see.
        WaitFor.CurrentView or WaitFor.Selection or WaitFor.ExplicitTileOrSelection => !listing.SelectionReadPending,
        _ => true,
    });

    private bool Current(Pressed next) =>
        ReferenceEquals(next.Store, listing.Store) && next.Replacements == listing.Replacements &&
        (next.WaitFor == WaitFor.AfterScopeChoice ||
            next.Input >= listing.LastSelectionReset && next.Input >= listing.LastPersonChange && next.Input >= listing.LastSelectionInput);

    private void Run(Pressed next)
    {
        try
        {
            if (!Current(next))
            {
                return;
            }

            if (HasSelectionFor(next))
            {
                next.Act(next.Input);
            }
            else if (listing.ShowsPriorViewAfterFailure)
            {
                next.ViewUnavailable?.Invoke();
            }
        }
        finally
        {
            next.Decided?.SetResult();
        }
    }

    private bool HasSelectionFor(Pressed next) =>
        next.WaitFor is WaitFor.EarlierKeys or WaitFor.ScopeChoice or WaitFor.AfterScopeChoice || listing.ShowsRequestedView ||
        (next.WaitFor == WaitFor.ExplicitTileOrSelection && listing.HasExplicitSelectionAfter(next.SelectionKey));

    private sealed record Pressed(Action<long> Act, WaitFor WaitFor, long Input, long SelectionKey, StashStore? Store, int Replacements,
        TaskCompletionSource? Decided, Action? ViewUnavailable);

    private enum WaitFor { EarlierKeys, Selection, ExplicitTileOrSelection, CurrentView, Layout, ScopeChoice, AfterScopeChoice }
}

internal sealed record KeySelection(IReadOnlyList<StashItem> Items, StashItem? Focused, long Input);
