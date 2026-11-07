using ImageStash.Files;
using ImageStash.Layers;
using ImageStash.Media;
using ImageStash.Seams;

namespace ImageStash.Store;

/// <summary>
/// Renders an edited item's finished picture from its original and its layers into a <c>.work\</c> path, as
/// <see cref="FinishedPicture.Render"/> does, answering the facts read back from the file and the SHA-256 of the bytes it
/// decoded as the original; the store calls it, on its own thread, to render again a Save left unfinished (State: Save).
/// </summary>
public delegate Outcome<MediaRender> FinishedPictureRenderer(
    string originalPath, string itemName, LayerList layers, ImageFormat finishedFormat, string workPath, TimeSpan heldFileRetry);

/// <summary>
/// Reads a file another program may have rewritten, as <see cref="MediaDecoder.ReadPicture"/> does: the SHA-256 of its
/// bytes, with its facts when they decode; the store calls it for an Open with… copy it has taken into <c>.work\</c>, and
/// to read back a render of its own whose readback the renderer found held (<see cref="StoreContext.RenderAgain"/>).
/// </summary>
public delegate Outcome<ReadPicture> PictureReader(string path, string name, TimeSpan heldFileRetry);

/// <summary>
/// The stash: its folder and its one store, owned by one thread (Composition: Store). Every call runs on that thread
/// and returns an outcome, never an exception, for anything the operating envelope admits. A change runs in turn and
/// never inside another; a read (the listings, the tag counts, an item, its uses, the Open with… sessions) runs in turn
/// too, or between the attempts of a change waiting on a held file, so the summon's listing is not held behind it (G1),
/// seeing that change's row as a live failure at that step would leave it (State: A live failure).
/// <see cref="Confirm"/> and <see cref="ForEdit"/> are changes: the files they answer are handed out of the store, or
/// opened by the editor, so they answer only after every change asked before them has moved what it moves (G8, G20).
/// </summary>
public sealed class StashStore : IDisposable
{
    private readonly StoreThread thread;
    private readonly StoreContext store;
    // The whole-stash operation in flight, at most one; see Begin.
    private int wholeStash;
    private int disposed;

    private StashStore(StoreThread thread, StoreContext store)
    {
        this.thread = thread;
        this.store = store;
    }

    public StashFolder Folder => store.Folder;

    /// <summary>
    /// Creates or opens the stash at <paramref name="folder"/> and finishes or undoes every pending step before it
    /// returns, so nothing reads the stash first (G28). A file still held is retried for <paramref name="heldFileRetry"/>.
    /// <paramref name="renderer"/> renders a finished picture again; null means <see cref="FinishedPicture.Render"/>.
    /// <paramref name="openWithRoot"/> is the folder the Open with… copies are kept in, outside the stash folder; null
    /// means this stash keeps none. <paramref name="reader"/> reads a copy; null means <see cref="MediaDecoder.ReadPicture"/>.
    /// </summary>
    public static async Task<StoreOpening> Open(
        string folder,
        IRecycleBin recycleBin,
        TimeSpan heldFileRetry,
        TimeProvider clock,
        FinishedPictureRenderer? renderer = null,
        string? openWithRoot = null,
        PictureReader? reader = null)
    {
        var thread = new StoreThread();
        Outcome<(StoreContext Store, IReadOnlyList<Failure> Named)> opened;
        try
        {
            opened = await thread.Run(() => StoreOpen.Open(
                new StashFolder(folder), recycleBin, heldFileRetry, clock, renderer ?? FinishedPicture.Render, openWithRoot, reader ?? MediaDecoder.ReadPicture, thread));
        }
        catch (Exception e)
        {
            opened = new Failure(StoreOpen.Action, folder, e.Message);
        }

        if (!opened.Succeeded)
        {
            thread.Dispose();
            return new StoreOpening(null, [opened.Failure]);
        }

        return new StoreOpening(new StashStore(thread, opened.Value.Store), opened.Value.Named);
    }

    /// <summary>A fresh path in <c>.work\</c> carrying <paramref name="extension"/>, for a caller that writes a stream.</summary>
    public Outcome<string> AllocateWorkPath(string extension) =>
        store.MoveRefusal("Make room for a new file", extension) is { } moving ? moving : WorkFolder.Allocate(store.Folder, extension);

    /// <summary>Copies <paramref name="source"/> into <c>.work\</c>, leaving the source as it was, and returns the copy's path.</summary>
    public Task<Outcome<string>> Stage(string source) =>
        thread.Run(() => store.MoveRefusal("Copy into the stash", Path.GetFileName(source)) is { } moving ? moving : WorkFolder.Stage(store, source));

    /// <summary>Deletes a staged file the import will not add, such as one Media found unreadable.</summary>
    public Task<Outcome> Discard(string stagedPath) =>
        thread.Run(() => store.MoveRefusal("Discard a copy", Path.GetFileName(stagedPath)) is { } moving ? moving : WorkFolder.Discard(store, stagedPath));

    /// <summary>
    /// Writes what <paramref name="openRead"/> gives into a fresh <c>.work\</c> path carrying <paramref name="extension"/>,
    /// created new, on a pool thread, and returns that path (R2). A write that fails part way leaves nothing behind and is
    /// named as <paramref name="action"/> on <paramref name="subject"/> with its reason; a partial file that cannot be
    /// deleted is named through <paramref name="leftOver"/> at once and left to the next start's clearing of <c>.work\</c>.
    /// </summary>
    public async Task<Outcome<string>> WriteWork(string extension, Func<Task<Stream>> openRead, string action, string subject, Action<Failure> leftOver)
    {
        if (store.MoveRefusal(action, subject) is { } moving)
        {
            return moving;
        }

        var path = AllocateWorkPath(extension);
        if (!path.Succeeded)
        {
            return new Failure(action, subject, "its name does not end in a file extension");
        }

        try
        {
            await Task.Run(async () =>
            {
                await using var source = await openRead();
                await using var target = new FileStream(path.Value!, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                await source.CopyToAsync(target);
            });
            return path;
        }
        catch (Exception e)
        {
            // The stream is another program's, or the caller's own bytes, and opens once, and no file of anyone's is held
            // open on this path, so there is nothing to retry (G35): whatever it throws is this write's failure. A write
            // refused before its file was made left nothing to delete.
            if (File.Exists(path.Value!) && (await Discard(path.Value!)).Failure is { } partial)
            {
                leftOver(partial);
            }

            return new Failure(action, subject, HeldFileRetry.ReasonFor(e));
        }
    }

    /// <summary>
    /// Adds the staged file as an item, returning once its file is in place (G28, R9); or, for a file whose bytes are already
    /// an item's original, discards it and returns that item (G12).
    /// </summary>
    public Task<Outcome<ImportResult>> CommitImport(ImportCandidate candidate) =>
        // Refused before its staged file is looked at, which a Move's switch has left in a folder that is no longer the stash.
        thread.Run(() => store.MoveRefusal(ImportCommit.Action, candidate.Name) is { } moving ? moving : ImportCommit.Commit(store, candidate));

    public Task<Outcome<IReadOnlyList<StashItem>>> All() => thread.Read(() => ItemQueries.All(store));

    public Task<Outcome<IReadOnlyList<StashItem>>> Recent() => thread.Read(() => ItemQueries.Recent(store));

    public Task<Outcome<IReadOnlyList<StashItem>>> Search(string query) => thread.Read(() => ItemQueries.Search(store, query));

    public Task<Outcome<IReadOnlyList<StashItem>>> Favorites() => thread.Read(() => ItemQueries.Favorites(store));

    /// <summary>The item of that identity as the stash lists it, or null when the stash does not list it.</summary>
    public Task<Outcome<StashItem?>> Item(long id) => thread.Read(() => ItemQueries.Item(store, id));

    /// <summary>
    /// What Edit opens on the item of that identity: its original and the layers, or null when the stash does not list
    /// it; a list the store holds that cannot be read fails, naming the item, and the item stays listed (G35). Queued as a
    /// change, never answered between a waiting change's attempts, nor while the item's Open with… adoption or Undo runs:
    /// the editor opens the original it answers, which that change may be about to move (G20).
    /// </summary>
    public Task<Outcome<ItemForEdit?>> ForEdit(long id) =>
        store.MoveRefusal("Read an item", StashFolder.StoreFileName) is { } moving
            ? Task.FromResult<Outcome<ItemForEdit?>>(moving)
            : OnceAdoptionsHaveRun([id], () => ItemQueries.ForEdit(store, id));

    /// <summary>
    /// The items a caller holds, confirmed by identity in one read, before they are handed over (G8): each as the stash
    /// lists it now, or refused by name as no longer in the stash; while the store cannot be read, each unchanged since it
    /// was read as handed in, and any other refused naming it and why. One outcome per item, in order, failures naming
    /// <paramref name="action"/>; the answered items' file names stay in hand until the confirmation is disposed. Queued
    /// as <see cref="ForEdit"/> is, so it never hands over a file a waiting change is about to move.
    /// </summary>
    public Task<Confirmation> Confirm(IReadOnlyList<StashItem> items, string action) =>
        store.MoveStage != MoveStage.None || store.ImportSpent
            ? Task.FromResult(ItemQueries.Confirm(store, action, items))
            : OnceAdoptionsHaveRun([.. items.Select(item => item.Id)], () => ItemQueries.Confirm(store, action, items));

    /// <summary>
    /// Holds the file names of items a drag-out carries, as it starts and without waiting on the store's thread, until
    /// the hold is disposed; refused, naming <paramref name="action"/> and the item, when one of those names has gone to
    /// a file since that item was read (<see cref="NamesInHand.HoldUnlessChosenSince"/>).
    /// </summary>
    public Outcome<IDisposable> HoldWhileDragged(IReadOnlyList<StashItem> items, string action) =>
        items.Count > 0 && store.MoveRefusal(action, ItemQueries.Named(items[0].Name, items[0].FileName)) is { } moving
            ? moving
            : store.InHand.HoldUnlessChosenSince(items, action);

    /// <summary>Every tag the stash's items carry, with how many carry it, the most carried first (G4).</summary>
    public Task<Outcome<IReadOnlyList<TagCount>>> TagCounts() => thread.Read(() => ItemTags.Counts(store));

    /// <summary>
    /// Gives each item the tags <paramref name="text"/> commits, one per word (G17); one outcome per item, in order, an
    /// item no longer in the stash named as it was handed in.
    /// </summary>
    public Task<IReadOnlyList<Outcome>> AddTag(IReadOnlyList<StashItem> items, string text) => thread.Run(() => ItemTags.Add(store, items, text));

    /// <summary>Takes <paramref name="tag"/>, in any spelling, off each item; one outcome per item, in order.</summary>
    public Task<IReadOnlyList<Outcome>> RemoveTag(IReadOnlyList<StashItem> items, string tag) => thread.Run(() => ItemTags.Remove(store, items, tag));

    /// <summary>Sets the favorite flag on each item as given, never toggling it; one outcome per item, in order.</summary>
    public Task<IReadOnlyList<Outcome>> SetFavorite(IReadOnlyList<StashItem> items, bool favorite) => thread.Run(() => ItemFavorite.Set(store, items, favorite));

    /// <summary>Records one use at <paramref name="at"/> on each item, after the handoff, copy or drag has completed (G10); answers the items no longer in the stash, which got none.</summary>
    public Task<Outcome<IReadOnlyList<StashItem>>> RecordUses(IReadOnlyList<StashItem> items, DateTimeOffset at) => thread.Run(() => ItemQueries.RecordUses(store, items, at));

    /// <summary>How often and how recently the item of that identity was used (G19).</summary>
    public Task<Outcome<UseHistory>> UseHistory(long id) => thread.Read(() => ItemQueries.UseHistory(store, id));

    /// <summary>
    /// Names the item <paramref name="name"/> and its files after it (G17, P1), returning it as recorded once its files
    /// carry the name; a file refused after the commit fails after it, the next start finishing or undoing the rename.
    /// </summary>
    public Task<Outcome<StashItem>> Rename(StashItem item, string name) => thread.Run(() => ItemRename.Rename(store, item, name));

    /// <summary>
    /// Saves <paramref name="layers"/> over the item (State: Save): the caller rendered them over the item's original in
    /// its <see cref="ItemForEdit.FinishedFormat"/> into <paramref name="render"/>, at a path <see cref="AllocateWorkPath"/>
    /// gave; returns the item as listed once that render is its finished picture, the original kept in <c>originals\</c>.
    /// An empty list, with no render, is Revert to original on an edited item and changes nothing on an unedited one. A
    /// refusal before the commit leaves the render the caller's to <see cref="Discard"/>; a step refused after it fails
    /// after the commit, the item listed on the picture on disk and the next start finishing or undoing the Save.
    /// </summary>
    public Task<Outcome<StashItem>> Save(StashItem item, LayerList layers, RenderedPicture? render) => thread.Run(() => ItemSave.Save(store, item, layers, render));

    /// <summary>
    /// Makes a second item of <paramref name="item"/> (G26): a copy of its original that Store makes, carrying
    /// <paramref name="layers"/>, rendered by the caller as for <see cref="Save"/> (no render with an empty list), with
    /// the same tags, not favorite, named with a counter; returns it as listed once its files are in place, the item
    /// itself left as it was. A refusal before the insert leaves the render the caller's to <see cref="Discard"/>; a step
    /// refused after it fails after the commit, the new item unlisted and removed at the next start.
    /// </summary>
    public Task<Outcome<StashItem>> SaveAsNewItem(StashItem item, LayerList layers, RenderedPicture? render) =>
        thread.Run(() => ItemSaveAsNewItem.SaveAsNewItem(store, item, layers, render));

    /// <summary>Removes the items, their files to the Recycle Bin; one outcome per identity, in order.</summary>
    public Task<IReadOnlyList<Outcome>> Remove(IReadOnlyList<long> ids) => thread.Run(() => BatchRemoval.Remove(store, ids));

    /// <summary>
    /// Deletes the items (G18): their files held as copies in <c>.work\</c>, the rows marked, the files to the Recycle
    /// Bin; one outcome per identity, in order, carrying what Undo needs.
    /// </summary>
    public Task<IReadOnlyList<Outcome<DeletedItem>>> Delete(IReadOnlyList<long> ids) => thread.Run(() => ItemDeletion.Delete(store, ids));

    /// <summary>Brings deleted items back whole from their held copies and removes their Recycle Bin entries; one outcome per item, in order.</summary>
    public Task<IReadOnlyList<Outcome<RestoredItem>>> UndoDelete(IReadOnlyList<DeletedItem> items) => thread.Run(() => ItemDeletion.Undo(store, items));

    /// <summary>Ends deleted items once their Undo can no longer come: the held copies deleted, the rows removed; one outcome per identity, in order.</summary>
    public Task<IReadOnlyList<Outcome>> FinishDelete(IReadOnlyList<long> ids) => thread.Run(() => ItemDeletion.Finish(store, ids));

    /// <summary>
    /// Begins an Open with… session on the item, or continues its session (G27): answers the path of the copy of its
    /// finished picture, outside the stash folder, for the Open-with dialog; a copy another program rewrote or still holds
    /// is answered as it is, an unchanged one refreshed to the finished picture as it is now.
    /// </summary>
    public Task<Outcome<string>> BeginOpenWith(StashItem item) => OpenWithBegin.Begin(thread, store, item);

    /// <summary>
    /// Reads the item's Open with… copy once it opens without sharing (State: Open with…): unchanged, still held, or a
    /// rewrite that awaits adoption; a rewrite that does not decode is refused as unreadable, naming the copy. Never adopts.
    /// </summary>
    public Task<Outcome<OpenWithCopy>> ClassifyOpenWith(long id) => OpenWithCopies.Classify(thread, store, id);

    /// <summary>
    /// Adopts the rewrite of the item's Open with… copy that awaits adoption (G27): the bytes taken from the copy now
    /// become the item's original and finished picture under its name with their own format's extension, its layers
    /// dropped, the previous original kept for the adoption's Undo until it expires. Answers the item as listed then,
    /// whose <see cref="StashItem.RenderedRevision"/> names the adoption; a rewrite that no longer decodes is refused as
    /// unreadable and not adopted. A step refused after the commit fails after it, the next start finishing or undoing it.
    /// </summary>
    public Task<Outcome<StashItem>> AdoptRewrite(long id) => ItemAdoption.Adopt(thread, store, id);

    /// <summary>
    /// Undoes the adoption <paramref name="revision"/> names while its notification shows (G27, G16): the previous original
    /// and layers back, the finished picture rendered again where layers exist, the adopted picture to the Recycle Bin and
    /// the record cleared; answers the item as listed then, or null, with nothing done, when no live adoption has that
    /// revision or the item was saved or reverted since (G25). A step refused after the commit fails after it, the next
    /// start finishing or undoing it.
    /// </summary>
    public Task<Outcome<StashItem?>> UndoAdoption(long id, long revision) => ItemAdoptionUndo.Undo(thread, store, id, revision);

    /// <summary>
    /// Ends the Undo of the adoption <paramref name="revision"/> names, once its notification has gone (G27, G29): the
    /// previous original to the Recycle Bin and the record cleared; done, with nothing to do, when no record names it.
    /// </summary>
    public Task<Outcome> ExpireAdoption(long id, long revision) => thread.Run(() => AdoptionUndo.Expire(store, id, revision));

    /// <summary>Every item's Open with… session with its copy's path, and, to the first call after the open, the adoptions the start made.</summary>
    public Task<Outcome<OpenWithSessions>> OpenWithSessions() => thread.Read(() => OpenWithCopies.Sessions(store));

    /// <summary>
    /// Moves the stash to <paramref name="destination"/>, an empty folder (G30, State: Move), and answers its full path:
    /// the store and every file copied on the store's thread, the settings switched in one write through
    /// <paramref name="keeper"/>, the old folder removed. Runs once no Open with… adoption or Undo is running, so none is
    /// split by it; from its copy on, every call of this store that would change or hand out a file is refused with
    /// <see cref="MoveReasons.InProgress"/>, and after the switch every call is, until the store is disposed: the stash is
    /// then the one at the answered path. A failure before the switch changes nothing; one after it fails after the
    /// commit, carrying that path. <paramref name="progress"/> is told on the store's thread.
    /// </summary>
    public async Task<Outcome<string>> Move(SettingsKeeper keeper, string destination, IProgress<MoveProgress>? progress = null)
    {
        if (Begin(WholeStash.Move) is { } running)
        {
            return new Failure(StashMove.Action, MoveFolders.Subject(destination), running);
        }

        try
        {
            return await OnceAdoptionsHaveRun(null, () => StashMove.Move(store, keeper, destination, progress));
        }
        finally
        {
            Volatile.Write(ref wholeStash, (int)WholeStash.None);
        }
    }

    /// <summary>
    /// Writes the whole stash and the settings that travel (from <paramref name="keeper"/>) into one archive at
    /// <paramref name="archivePath"/>, outside the stash folder, written beside it and renamed over it (G36, R2), on the
    /// store's thread between transactions, the queued reads answered meanwhile (G1); nothing in the stash changes, and a
    /// change asked meanwhile runs after it. Runs once no Open with… adoption or Undo is running, as a Move does.
    /// <paramref name="progress"/> is told on the store's thread.
    /// </summary>
    public async Task<Outcome> Export(SettingsKeeper keeper, string archivePath, IProgress<TransferProgress>? progress = null)
    {
        if (Begin(WholeStash.Export) is { } running)
        {
            return new Failure(StashExport.Action, StashArchive.Subject(archivePath), running);
        }

        try
        {
            return await OnceAdoptionsHaveRun(null, () => StashExport.Export(store, keeper, archivePath, progress));
        }
        finally
        {
            Volatile.Write(ref wholeStash, (int)WholeStash.None);
        }
    }

    /// <summary>
    /// Replaces the stash at its location, and the settings that travel, with the archive at <paramref name="archivePath"/>
    /// (G36, State: Import stash…), the replaced folder to the Recycle Bin. A failure with no value changed nothing and
    /// this store is still the stash. Once the import's record has landed this store is spent, every call of it refused
    /// until it is disposed, and the value is the stash folder: the caller opens it as the start does after success, and
    /// after a failure carrying it only once <see cref="StashImport.FinishAtStart"/> has finished the import
    /// (<see cref="Settings.ImportPending"/> null). <paramref name="progress"/> is told on the store's thread.
    /// </summary>
    public async Task<Outcome<string>> Import(SettingsKeeper keeper, string archivePath, IProgress<TransferProgress>? progress = null)
    {
        if (Begin(WholeStash.Import) is { } running)
        {
            return new Failure(StashImport.Action, StashArchive.Subject(archivePath), running);
        }

        try
        {
            return await OnceAdoptionsHaveRun(null, () => StashImport.Import(store, keeper, archivePath, progress));
        }
        finally
        {
            Volatile.Write(ref wholeStash, (int)WholeStash.None);
        }
    }

    // Takes the one place of a whole-stash operation (a Move, an export or an import), or answers why it cannot: the
    // running one's words, so one asked while another runs is refused at submission and nothing is queued to run after it.
    private string? Begin(WholeStash operation) =>
        (WholeStash)Interlocked.CompareExchange(ref wholeStash, (int)operation, (int)WholeStash.None) switch
        {
            WholeStash.None => null,
            WholeStash.Move => MoveReasons.AlreadyMoving,
            WholeStash.Export => StashExport.InProgress,
            _ => StashImport.InProgress,
        };

    // A call whose caller opens or hands out the files it answers, or that moves them all: queued as a change, and asked
    // again once the Open with… adoptions and Undos of these items (null: of any item) running when it came up have ended
    // (RunningAdoptions), since other calls run between their own (G20, G8, State: Move).
    private async Task<T> OnceAdoptionsHaveRun<T>(IReadOnlyList<long>? ids, Func<T> read)
    {
        while (true)
        {
            var (running, answer) = await thread.Run(() => (ids is null ? store.RunningAdoptions.OfAny() : store.RunningAdoptions.Of(ids)) is { } changes ? (changes, default(T)) : (null, read()));
            if (running is null)
            {
                return answer!;
            }

            await running;
        }
    }

    /// <summary>
    /// Finishes the calls already queued on the store's thread, then closes the store; an entry still between its own
    /// calls (an Open with… entry, or Edit's read or a confirmation waiting on an adoption) then fails with the store closed.
    /// A second call does nothing: after a Move both the window's switch and the exit may close the old store.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 1)
        {
            return;
        }

        thread.Run(() => { store.Connection.Dispose(); return true; });
        thread.Dispose();
    }

    private enum WholeStash
    {
        None,
        Move,
        Export,
        Import,
    }
}

/// <summary>
/// The stash as start opened it: the store, or null when it could not open, with the failures to name: the one that
/// kept it closed, or each pending step still refused while every other item opened.
/// </summary>
public sealed record StoreOpening(StashStore? Store, IReadOnlyList<Failure> Failures);
