using ImageStash.Layers;
using ImageStash.Store;

namespace ImageStash.Editor;

/// <summary>
/// How a session ends (Composition: Editor; G20, G26, G35, R9): Save rendered, committed and published inside the busy
/// state before it answers; Save as new item going on with the new item; Cancel answering the discarded layers; each
/// ending answered to the caller and, when it ends the session, raised as <see cref="Ended"/>.
/// </summary>
public sealed partial class EditorSession
{
    /// <summary>Why a Save is refused once the item's original is no longer the picture the edit shows (G21, G35).</summary>
    public const string OriginalReplaced = "its original was replaced while it was open in the editor";

    /// <summary>
    /// Saves the live layers over the item (G26): rendered in its finished format, committed and published by Store before
    /// it answers, inside <see cref="Busy"/>. Saved, failed after the commit and gone end the session; refused leaves it
    /// open on its layers. A second call while one runs answers the running one.
    /// </summary>
    public Task<EditResult> Save() => Saving(asNewItem: false);

    /// <summary>
    /// Makes a second item from a copy of the original with the live layers and the same tags (G26), and continues the
    /// session on it with undo and redo emptied; the item edited until now stays as it was. A refusal, or a failure after
    /// the new item's insert, leaves the session on the item it was on.
    /// </summary>
    public Task<EditResult> SaveAsNewItem() => Saving(asNewItem: true);

    /// <summary>
    /// Leaves the item as it was and ends the session, answering the discarded layers for the notice's Undo (G20); null,
    /// ending nothing, while a Save runs.
    /// </summary>
    public LayerList? Cancel()
    {
        ThrowIfEnded();
        if (saving is { IsCompleted: false })
        {
            return null;
        }

        tool.Abandon();
        EndTyping();
        var discarded = Layers;
        Close();
        Ended?.Invoke(new EditResult(EditEnding.Cancelled, Item, null, discarded));
        return discarded;
    }

    /// <summary>Ends the session without an ending, as when its item leaves the stash; a Save already running still answers its caller.</summary>
    public void Dispose()
    {
        if (!closed)
        {
            Close();
        }
    }

    private Task<EditResult> Saving(bool asNewItem)
    {
        ThrowIfEnded();
        if (saving is { IsCompleted: false } running)
        {
            return running;
        }

        return saving = Saved(asNewItem);
    }

    private async Task<EditResult> Saved(bool asNewItem)
    {
        tool.Abandon();
        // What is being typed is on screen, so it is what Save saves.
        EndTyping();
        var layers = Layers;
        SetBusy(asNewItem ? $"Saving “{Subject}” as a new item…" : $"Saving “{Subject}”…");
        EditResult result;
        var ending = false;
        // An estimate holding the gate stops at the frame it is on rather than finishing the clip first (G24, G26).
        estimate.Cancel();
        await gate.WaitAsync();
        try
        {
            // Read where the original is now: a refused Save may have finished an earlier pending one on the way, moving
            // it and putting that one's render where it was (State: Save), and the render must read the original.
            var read = await Reread();
            // Other bytes than the picture this edit shows and placed its layers over (an Open with… Undo that ran while
            // the edit was opening): refused, so nothing renders onto a picture the editor did not show (G21).
            var replaced = read.Value is { } now && !now.OriginalSha256.AsSpan().SequenceEqual(shownOriginal);
            result = !read.Succeeded ? new EditResult(EditEnding.Refused, null, read.Failure, null)
                : read.Value is not { } edit ? new EditResult(EditEnding.Gone, null, null, null)
                : replaced ? new EditResult(EditEnding.Refused, null, new Failure(asNewItem ? ItemSaveAsNewItem.Action : ItemSave.Action, Subject, OriginalReplaced), null)
                : asNewItem ? await Continued(await SavedEdit.SaveAsNew(store, edit.Item, edit, layers, renderer, heldFileRetry), edit.Item)
                : await Judged(await SavedEdit.Save(store, edit.Item, edit, layers, renderer, heldFileRetry), edit.Item);
            if (result.Ending == EditEnding.Refused)
            {
                await Reread();
            }

            // Closed before the gate opens, so no estimate starts for an edit that has ended.
            if (result.Ends && !closed)
            {
                Close();
                ending = true;
            }
        }
        finally
        {
            gate.Release();
        }

        SetBusy(null);
        if (ending)
        {
            Ended?.Invoke(result);
        }
        else if (!closed)
        {
            // The Save cancelled any estimate; a session going on states its layers' size again (G24).
            estimate.Ask(Layers);
        }

        return result;
    }

    private async Task<EditResult> Judged(Outcome<StashItem> saved, StashItem item) =>
        saved.Succeeded ? new EditResult(EditEnding.Saved, saved.Value, null, null)
        : saved.Value is { } committed ? new EditResult(EditEnding.FailedAfterCommit, committed, saved.Failure, null)
        : await Gone(item) ? new EditResult(EditEnding.Gone, null, null, null)
        : new EditResult(EditEnding.Refused, null, saved.Failure, null);

    private async Task<EditResult> Continued(Outcome<StashItem> saved, StashItem item)
    {
        if (!saved.Succeeded)
        {
            // A new item whose step failed after its insert is not listed, and the next start removes it; this one is as it was.
            return saved.Value is null && await Gone(item) ? new EditResult(EditEnding.Gone, null, null, null) : new EditResult(EditEnding.Refused, null, saved.Failure, null);
        }

        var made = saved.Value!;
        var reread = await store.ForEdit(made.Id);
        if (reread.Value is not { } next)
        {
            // The new item is in the stash, but the session cannot go on with it.
            return new EditResult(EditEnding.FailedAfterCommit, made, reread.Failure ?? new Failure("Save as new item", ItemQueries.Named(made.Name, made.FileName), "it is no longer in the stash"), null);
        }

        // G25: after Save, the layers are the only history, and the stacks never belonged to the new item.
        Edit = next;
        shownOriginal = next.OriginalSha256;
        undo.Clear();
        redo.Clear();
        RaiseChanged();
        return new EditResult(EditEnding.Continued, next.Item, null, null);
    }

    private async Task<bool> Gone(StashItem item) => (await store.Item(item.Id)) is { Succeeded: true, Value: null };

    /// <summary>What Edit opens on the session's item now, kept as <see cref="Edit"/> while the stash lists it.</summary>
    internal async Task<Outcome<ItemForEdit?>> Reread()
    {
        var read = await store.ForEdit(Item.Id);
        if (read.Value is { } edit)
        {
            Edit = edit;
        }

        return read;
    }

    private void SetBusy(string? busy)
    {
        Busy = busy;
        RaiseChanged();
    }


    private void Close()
    {
        closed = true;
        textField = null;
        estimate.Dispose();
        EndFrames();
        undo.Clear();
        redo.Clear();
    }

    private void ThrowIfEnded()
    {
        if (closed)
        {
            throw new InvalidOperationException("The edit has ended.");
        }
    }
}

/// <summary>How a Save, a Save as new item or a Cancel came out (Composition: Editor).</summary>
public enum EditEnding
{
    /// <summary>Rendered, committed and published: the session has ended on the item as listed.</summary>
    Saved,

    /// <summary>Refused before the commit (E4, a held or unreadable original, a refused write, the store held): named, the session open on its layers.</summary>
    Refused,

    /// <summary>Committed, then a step refused: named, the session ended, the item as committed and listed on the picture on disk.</summary>
    FailedAfterCommit,

    /// <summary>The item is no longer in the stash: the session ended, nothing to name (G16).</summary>
    Gone,

    /// <summary>Left as it was: the session ended with the discarded layers for the notice's Undo (G20).</summary>
    Cancelled,

    /// <summary>Save as new item made the new item, and the session goes on with it (G26).</summary>
    Continued,
}

/// <summary>
/// An ending: the item it leaves (saved, as committed, the new one, or the cancelled one), the failure to name, and for
/// a Cancel the discarded layers.
/// </summary>
public sealed record EditResult(EditEnding Ending, StashItem? Item, Failure? Failure, LayerList? Discarded)
{
    /// <summary>Whether the session has ended with it.</summary>
    public bool Ends => Ending is EditEnding.Saved or EditEnding.FailedAfterCommit or EditEnding.Gone or EditEnding.Cancelled;
}
