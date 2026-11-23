using ImageStash.Store;

namespace ImageStash.Window;

/// <summary>
/// A click on the preview's ‹ or › (G19): the arrows' step (<see cref="PreviewStepping"/>) for the pointer, after a name or
/// tag typed in a field is committed as Enter would commit it there. The buttons take no focus, so the click moves no
/// focus out of the field and commits nothing by itself; its act decides the next item from the grid's list as it is,
/// then commits the draft, waits for the store's answer, and moves to the item it decided, so a rename that takes the
/// item out of a search's results never changes which item comes next. A refused commit leaves the preview on its item,
/// the refusal named where the item it names still shows (G35). Clicks run in the order made, each from the item shown
/// when its turn comes; a move whose commit answers once the preview is closed or closing, an edit opens or opening on
/// its item, or the preview has moved to another item meanwhile, is dropped, the commit standing (G2, G20). Each button
/// is available exactly where its step shows an item (<see cref="Available"/>).
/// </summary>
public sealed class PreviewStepClicks
{
    private readonly PreviewStepping stepping;
    private readonly Func<StashItem?> shown;
    private readonly Func<bool> closing;
    private readonly Editing editing;

    /// <summary>The latest click's turn, which the next click's waits for.</summary>
    private Task turn = Task.CompletedTask;

    /// <param name="shown">The item the preview shows, null while it is closed.</param>
    /// <param name="closing">Whether a close asked while a commit was in flight waits for its answer (Esc, Space, Back).</param>
    /// <param name="editing">The window's edit, which an edit opening over the preview drops a waiting move for.</param>
    public PreviewStepClicks(PreviewStepping stepping, Func<StashItem?> shown, Func<bool> closing, Editing editing)
    {
        this.stepping = stepping;
        this.shown = shown;
        this.closing = closing;
        this.editing = editing;
    }

    /// <summary>Whether the button stepping <paramref name="way"/> does anything now: where the step's rule gives an item (G19, G7).</summary>
    public bool Available(StepWay way) => stepping.Next(way) is not null;

    /// <summary>
    /// A click on the button stepping <paramref name="way"/>, after every earlier click's turn: the next item decided now,
    /// then <paramref name="commitDraft"/>, which commits what the preview's name or tag field holds as its Enter does and
    /// answers that commit with the read of its answer, or null when nothing is typed, asked until it answers null; then
    /// the move, once each commit has succeeded and nothing has closed, covered or moved the preview meanwhile.
    /// </summary>
    public Task Click(StepWay way, Func<Task<Outcome>?> commitDraft)
    {
        turn = Run(turn, way, commitDraft);
        return turn;
    }

    private async Task Run(Task earlier, StepWay way, Func<Task<Outcome>?> commitDraft)
    {
        await earlier;
        if (shown() is not { } from || !Stays(from) || stepping.Next(way) is not { } next)
        {
            return;
        }

        // Again after each answer: a name typed again while the store answered is committed too, never dropped by the move.
        while (commitDraft() is { } commit)
        {
            if (!(await commit).Succeeded || shown()?.Id != from.Id || !Stays(from))
            {
                return;
            }
        }

        stepping.MoveTo(next);
    }

    /// <summary>Whether a move from <paramref name="from"/> may still be made: no close waits and no edit opens or is open over it.</summary>
    private bool Stays(StashItem from) => !closing() && !editing.IsOpen && !editing.Occupies(from.Id);
}
