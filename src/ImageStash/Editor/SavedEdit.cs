using ImageStash.Layers;
using ImageStash.Store;

namespace ImageStash.Editor;

/// <summary>
/// The one render-then-commit routine (Composition: Editor; State: Save, Save as new item): a list rendered over the
/// item's original into a <c>.work\</c> path Store allocated, in the finished format Store derived (F3), on a pool
/// thread, then handed to Store with the list; a render refused (E4, an unreadable or held original, a refused write)
/// or a commit refused before it is discarded, and its failure answered as named (G35). An empty list renders nothing:
/// Revert to original on an edited item, nothing at all on an unedited one, or a plain copy as a new item.
/// </summary>
public static class SavedEdit
{
    /// <summary>Saves <paramref name="layers"/> over <paramref name="item"/>, answered as Store answers its Save.</summary>
    public static Task<Outcome<StashItem>> Save(StashStore store, StashItem item, ItemForEdit edit, LayerList layers, FinishedPictureRenderer renderer, TimeSpan heldFileRetry) =>
        Saved(store, item, edit, layers, renderer, heldFileRetry, store.Save);

    /// <summary>Makes a second item of <paramref name="item"/> carrying <paramref name="layers"/>, answered as Store answers its Save as new item.</summary>
    public static Task<Outcome<StashItem>> SaveAsNew(StashStore store, StashItem item, ItemForEdit edit, LayerList layers, FinishedPictureRenderer renderer, TimeSpan heldFileRetry) =>
        Saved(store, item, edit, layers, renderer, heldFileRetry, store.SaveAsNewItem);

    private static async Task<Outcome<StashItem>> Saved(
        StashStore store, StashItem item, ItemForEdit edit, LayerList layers, FinishedPictureRenderer renderer, TimeSpan heldFileRetry,
        Func<StashItem, LayerList, RenderedPicture?, Task<Outcome<StashItem>>> commit)
    {
        if (layers.IsEmpty)
        {
            return await commit(item, LayerList.Empty, null);
        }

        var path = store.AllocateWorkPath(FinishedFormat.Extension(edit.FinishedFormat));
        if (!path.Succeeded)
        {
            return path.Failure;
        }

        var workPath = path.Value!;
        var name = ItemQueries.Named(item.Name, item.FileName);
        var rendered = await Task.Run(() => renderer(edit.OriginalPath, name, layers, edit.FinishedFormat, workPath, heldFileRetry));
        if (!rendered.Succeeded)
        {
            // A render that fails part way leaves what it wrote; one Discard cannot remove goes at the next start (State: Files).
            await store.Discard(workPath);
            return rendered.Failure;
        }

        var saved = await commit(item, layers, new RenderedPicture(workPath, rendered.Value!.Facts, rendered.Value.OriginalSha256));
        if (!saved.Succeeded && saved.Value is null)
        {
            // Refused before the commit, so the render is still the caller's.
            await store.Discard(workPath);
        }

        return saved;
    }
}
