namespace ImageStash.Window;

/// <summary>
/// The window's keys in the order they were pressed while what was typed is still being searched (G5, G7, G8): an arrow
/// pressed while the listing's latest load is pending waits for that load, and for the grid to lay out its rows, then
/// moves the selection among those rows, as "the selection moves to the first match, Up and Down move it" says; every
/// key act on the selection, or on what it opens, pressed after a waiting arrow (Enter, <c>Ctrl+C</c>, <c>Ctrl+D</c>,
/// Del, <c>Ctrl+E</c>, F2, Space, Esc, <c>Ctrl+A</c>, another arrow) waits for the arrows before it and is then
/// dispatched at once, never after an earlier act has completed, so Down, Enter, Down hands over the row Down reached
/// and ends on the next. An act takes the selection it acts on as it is dispatched. A query typed, or a scope chosen,
/// after an arrow and before that arrow's load has applied supersedes it: that arrow does nothing and the new matches
/// arrive with their first selected (G5). Keys dispatched after a queued tag choice wait for its result too.
/// </summary>
/// <param name="laidOutRows">The grid's rows laid out for the items the listing holds now, a layout pass forced when owed.</param>
public sealed class PendingQueryKeys(Listing listing, Func<IReadOnlyList<GridRow>> laidOutRows)
{
    private readonly Queue<Pressed> pressed = new();
    private bool draining;
    private Task drained = Task.CompletedTask;

    /// <summary>
    /// An arrow (G5, G7): moved now while the listing is settled and nothing waits; else after what waits, once its load
    /// has applied. Completes once it, and every key waiting with it, has been dispatched.
    /// </summary>
    public Task Move(Direction direction)
    {
        if (pressed.Count == 0 && listing.Settled && !listing.TagChoicePending)
        {
            listing.MoveFocus(direction, laidOutRows());
            return Task.CompletedTask;
        }

        pressed.Enqueue(new Pressed(direction, null, listing.InputOrder));
        if (!draining)
        {
            drained = Drain();
        }

        return drained;
    }

    /// <summary>A key act, dispatched after any earlier arrow or tag choice still waiting for results.</summary>
    public void Then(Action act)
    {
        if (pressed.Count == 0 && !listing.TagChoicePending)
        {
            act();
            return;
        }

        pressed.Enqueue(new Pressed(null, act, 0));
        if (!draining)
        {
            drained = Drain();
        }
    }

    private async Task Drain()
    {
        if (draining)
        {
            return;
        }

        draining = true;
        try
        {
            while (pressed.TryPeek(out var next))
            {
                if (next.Act is { } act)
                {
                    if (listing.TagChoicePending)
                    {
                        await listing.Settle();
                    }

                    pressed.Dequeue();
                    act();
                    continue;
                }

                // A change's refresh may start after the awaited load: the arrow waits for the listing to settle again.
                while ((!listing.Settled || listing.TagChoicePending) && listing.LastSelectionReset <= next.Input)
                {
                    await listing.Settle();
                }

                pressed.Dequeue();
                if (listing.LastSelectionReset <= next.Input)
                {
                    listing.MoveFocus(next.Direction!.Value, laidOutRows());
                }
            }
        }
        finally
        {
            draining = false;
        }
    }

    /// <summary>An arrow with its place in the input order, or a key act.</summary>
    private sealed record Pressed(Direction? Direction, Action? Act, long Input);
}
