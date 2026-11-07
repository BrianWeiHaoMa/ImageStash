using ImageStash.Files;
using static ImageStash.Store.MoveFolders;

namespace ImageStash.Store;

/// <summary>How far a Move of the open stash has gone (State: Move); see <see cref="StoreContext.MoveStage"/>.</summary>
public enum MoveStage
{
    None,
    Copying,
    Switched,
}

/// <summary>What a Move is doing: copying the stash's files, removing the old folder's, or clearing the last record of it.</summary>
public enum MovePhase
{
    Copying,
    Removing,
    Finishing,
}

/// <summary>A Move's progress, reported on the store's thread: <paramref name="Done"/> of <paramref name="Total"/> files in <paramref name="Phase"/>.</summary>
public sealed record MoveProgress(MovePhase Phase, int Done, int Total);

/// <summary>The words of every refusal a Move gives, and of every refusal given because one is under way (G35).</summary>
public static class MoveReasons
{
    /// <summary>The one reason everything that would begin while a Move runs is refused with, by Store and by the window.</summary>
    public const string InProgress = "your stash is being moved";

    public const string AlreadyMoving = "a Move is already in progress";

    public const string NotAFolder = "it is not a folder";

    public const string NotEmpty = "it is not empty";

    public const string TheStashItself = "it is the stash folder itself";

    public const string InsideWork = "it lies inside the stash's work folder";

    public const string InsideOpenWith = "it lies inside the Open with… folder";

    public const string NotAStash = "it does not hold a stash";

    public const string HoldsTheStash = "it holds the stash folder";

    public const string InsideTheStash = "it lies inside the stash folder";
}

/// <summary>
/// Move (G30, State: Move): the whole stash copied into a chosen empty folder on the store's thread between
/// transactions, the settings switched to it in one write, the old folder removed; and, at start, what a kill left of a
/// Move finished. The settings switch is the one point of change: before it the old folder is whole and the copy
/// disposable, after it the reverse (How the guarantees hold: G30). The folders themselves are <see cref="MoveFolders"/>'.
/// </summary>
public static class StashMove
{
    public const string Action = "Move the stash";

    public const string FinishAction = "Finish moving the stash";

    // How long the store's thread spends answering reads at a time while it waits for a settings write (G1).
    private static readonly TimeSpan ReadsBetweenWaits = TimeSpan.FromMilliseconds(20);

    // How much of a file is copied between two turns at the queued reads, so a large picture holds no listing back (G1).
    private const int CopyChunk = 1 << 20;

    /// <summary>
    /// Runs the whole Move on the store's thread and answers the new folder's path; a failure before the switch leaves the
    /// settings, the old folder and the chosen one as they were; one after it carries the new folder (G35). What an earlier
    /// Move left (a partial copy, an old folder still to remove) is finished first, as a start finishes it, so its record
    /// is never written over; while it cannot be, this Move is refused with its failure.
    /// </summary>
    internal static Outcome<string> Move(StoreContext store, SettingsKeeper keeper, string destination, IProgress<MoveProgress>? progress)
    {
        if (store.MoveRefusal(Action, Subject(destination)) is { } moving)
        {
            return moving;
        }

        var old = Full(store.Folder.Root);
        Outcome Wait(Task<Outcome> written) => WaitAnsweringReads(store, written);
        Outcome Step(string action, string path, Action step) => store.RetryHeldFile(action, path, step);
        if (FinishLeftOver(keeper, old, Step, Wait).FirstOrDefault() is { } leftOver)
        {
            return leftOver;
        }

        var judged = Judge(store, destination);
        if (!judged.Succeeded)
        {
            return judged.Failure;
        }

        var chosen = judged.Value!;
        // Recorded and landed before anything is written into the chosen folder (the markers below, then the copy), so a
        // kill at any moment leaves the record the next start empties that folder by (State: Move, G28).
        var recorded = Wait(keeper.ChangeNow(s => s with { MoveCopyInProgress = chosen }));
        if (!recorded.Succeeded)
        {
            return recorded.Failure;
        }

        store.MoveStage = MoveStage.Copying;
        var spelled = JudgeSpellings(store, chosen, Step);
        if (!spelled.Succeeded)
        {
            return Abandon(store, keeper, chosen, old, spelled.Failure);
        }

        var spelledInOld = spelled.Value;
        var copied = Copy(store, old, chosen, spelledInOld, progress);
        if (!copied.Succeeded)
        {
            return Abandon(store, keeper, chosen, old, copied.Failure);
        }

        // One write: the new location, no copy in progress, and the old folder pending removal (State: Move).
        var switched = Wait(keeper.ChangeNow(s => s with { StashFolder = chosen, MoveCopyInProgress = null, PreviousStashFolderPendingRemoval = old }));
        if (!switched.Succeeded)
        {
            return Abandon(store, keeper, chosen, old, switched.Failure);
        }

        // From here this store is not the record: nothing it was asked runs, and the store file can go.
        store.MoveStage = MoveStage.Switched;
        store.Connection.Dispose();
        var removed = Empty(Action, old, chosen, partialCopy: false, Step, progress);
        if (!removed.Succeeded)
        {
            return Outcome<string>.FailAfterCommit(removed.Failure, chosen);
        }

        progress?.Report(new MoveProgress(MovePhase.Finishing, 0, 0));
        var cleared = Clear(keeper, s => s with { PreviousStashFolderPendingRemoval = null }, Wait);
        return cleared.Succeeded ? Outcome<string>.Of(chosen) : Outcome<string>.FailAfterCommit(cleared.Failure, chosen);
    }

    /// <summary>
    /// At start, before the stash at <paramref name="stashFolder"/> is opened (State: Move): the partial copy of a copy in
    /// progress deleted, a pending removal finished, each field cleared once its folder is dealt with or found not to be a
    /// stash. A folder still refused keeps its field for the next start; every failure is answered for the tray.
    /// </summary>
    public static Task<IReadOnlyList<Failure>> FinishAtStart(SettingsKeeper keeper, string stashFolder, TimeSpan heldFileRetry) =>
        Task.Run<IReadOnlyList<Failure>>(() => FinishLeftOver(
            keeper,
            Full(stashFolder),
            (action, path, step) => HeldFileRetry.Run(action, path, heldFileRetry, step),
            written => written.GetAwaiter().GetResult()));

    // What the settings say an earlier Move left, finished against the stash at stash; the failures named, empty when
    // nothing was left or all of it is finished. An import finishes it first too, since the folders it makes beside the
    // stash may lie inside an old folder still to remove.
    internal static List<Failure> FinishLeftOver(SettingsKeeper keeper, string stash, FileStep step, Func<Task<Outcome>, Outcome> wait)
    {
        var settings = keeper.Current;
        var named = new List<Failure>();
        var clearCopy = settings.MoveCopyInProgress is { } copy && FinishNamed(copy, stash, partialCopy: true, step, named);
        var clearRemoval = settings.PreviousStashFolderPendingRemoval is { } previous && FinishNamed(previous, stash, partialCopy: false, step, named);
        if (clearCopy || clearRemoval)
        {
            var cleared = Clear(
                keeper,
                s => s with
                {
                    MoveCopyInProgress = clearCopy ? null : s.MoveCopyInProgress,
                    PreviousStashFolderPendingRemoval = clearRemoval ? null : s.PreviousStashFolderPendingRemoval,
                },
                wait);
            if (!cleared.Succeeded)
            {
                named.Add(cleared.Failure);
            }
        }

        return named;
    }

    // Whether the folder a field names is dealt with, so the field can go; a failure to name is added either way.
    private static bool FinishNamed(string named, string stash, bool partialCopy, FileStep step, List<Failure> failures)
    {
        string folder;
        try
        {
            folder = Full(named);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            failures.Add(new Failure(FinishAction, named, MoveReasons.NotAFolder));
            return true;
        }

        var finished = Empty(FinishAction, folder, stash, partialCopy, step, progress: null);
        if (finished.Succeeded)
        {
            return true;
        }

        failures.Add(finished.Failure);
        // A folder that is not a stash is left as it is and forgotten; one still held is tried again at the next start.
        return finished.Failure.Reason is MoveReasons.NotAStash or MoveReasons.TheStashItself or MoveReasons.HoldsTheStash or MoveReasons.InsideTheStash;
    }

    // The chosen folder as its full path, or why a Move cannot keep a stash whole there, judged by its path alone:
    // nothing is written anywhere until it is chosen.
    private static Outcome<string> Judge(StoreContext store, string destination)
    {
        string chosen;
        try
        {
            chosen = Full(destination);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return new Failure(Action, destination, MoveReasons.NotAFolder);
        }

        var subject = Subject(chosen);
        if (!Directory.Exists(chosen))
        {
            return new Failure(Action, subject, MoveReasons.NotAFolder);
        }

        if (Same(chosen, store.Folder.Root))
        {
            return new Failure(Action, subject, MoveReasons.TheStashItself);
        }

        // .work\ is cleared at every start, so a stash there would not survive the next one.
        if (Same(chosen, store.Folder.Work) || Inside(chosen, store.Folder.Work))
        {
            return new Failure(Action, subject, MoveReasons.InsideWork);
        }

        if (store.OpenWithRoot is { } openWith && (Same(chosen, openWith) || Inside(chosen, openWith)))
        {
            return new Failure(Action, subject, MoveReasons.InsideOpenWith);
        }

        try
        {
            if (Directory.EnumerateFileSystemEntries(chosen).Any())
            {
                return new Failure(Action, subject, MoveReasons.NotEmpty);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new Failure(Action, subject, HeldFileRetry.ReasonFor(e));
        }

        return Outcome<string>.Of(chosen);
    }

    // The chosen folder's spelling under the old folder when it lies inside it under another spelling (a short name, a
    // link), which comparing paths misses, or the same refusals Judge gives by path; by markers written into the chosen
    // folder, so only once the copy in progress names it.
    private static Outcome<string?> JudgeSpellings(StoreContext store, string chosen, FileStep step)
    {
        var subject = Subject(chosen);
        var inOld = SpelledUnder(Action, chosen, store.Folder.Root, step);
        if (!inOld.Succeeded)
        {
            return inOld.Failure;
        }

        if (inOld.Value is { } spelled && (Same(spelled, store.Folder.Work) || Inside(spelled, store.Folder.Work)))
        {
            return new Failure(Action, subject, MoveReasons.InsideWork);
        }

        if (store.OpenWithRoot is { } root)
        {
            var inOpenWith = SpelledUnder(Action, chosen, root, step);
            if (!inOpenWith.Succeeded)
            {
                return inOpenWith.Failure;
            }

            if (inOpenWith.Value is not null)
            {
                return new Failure(Action, subject, MoveReasons.InsideOpenWith);
            }
        }

        return inOld;
    }

    // The store file first, whole because no transaction runs beside it on this thread, then every other file of the old
    // folder, the chosen folder skipped when it lies inside; the queued reads answered between chunks (G1).
    private static Outcome Copy(StoreContext store, string old, string chosen, string? spelledInOld, IProgress<MoveProgress>? progress)
    {
        var skip = spelledInOld ?? chosen;
        List<string> files;
        List<string> folders;
        try
        {
            files = [.. FilesIn(old, skip).OrderBy(file => IsStoreFile(old, file) ? 0 : 1)];
            folders = [.. FoldersIn(old, skip)];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new Failure(Action, Subject(old), HeldFileRetry.ReasonFor(e));
        }

        progress?.Report(new MoveProgress(MovePhase.Copying, 0, files.Count));
        try
        {
            foreach (var folder in folders)
            {
                Directory.CreateDirectory(Path.Combine(chosen, Path.GetRelativePath(old, folder)));
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new Failure(Action, Subject(chosen), HeldFileRetry.ReasonFor(e));
        }

        for (var i = 0; i < files.Count; i++)
        {
            var file = files[i];
            var target = Path.Combine(chosen, Path.GetRelativePath(old, file));
            var copied = store.RetryHeldFile(Action, file, () => CopyFile(store, file, target));
            if (!copied.Succeeded)
            {
                return copied;
            }

            progress?.Report(new MoveProgress(MovePhase.Copying, i + 1, files.Count));
        }

        return Outcome.Done;
    }

    // One file in chunks, the queued reads answered between them; a try again after a held file starts the copy afresh.
    // The source is shared for writing too, as the store's own connection holds the store file so.
    private static void CopyFile(StoreContext store, string source, string target)
    {
        using var from = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var to = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None);
        var chunk = new byte[CopyChunk];
        int read;
        while ((read = from.Read(chunk)) > 0)
        {
            to.Write(chunk, 0, read);
            store.Thread.AnswerQueuedReads();
        }
    }

    // A copy or a switch that failed: the partial copy deleted, the chosen folder left as it was found, the record of the
    // copy cleared, and this store the stash again (G30). A partial copy that cannot be deleted keeps its record, so the
    // next start, or the next Move, deletes it.
    private static Outcome<string> Abandon(StoreContext store, SettingsKeeper keeper, string chosen, string old, Failure failure)
    {
        var deleted = Empty(Action, chosen, old, partialCopy: true, (action, path, step) => store.RetryHeldFile(action, path, step), progress: null);
        if (deleted.Succeeded)
        {
            Clear(keeper, s => s with { MoveCopyInProgress = null }, written => WaitAnsweringReads(store, written));
        }

        store.MoveStage = MoveStage.None;
        return failure;
    }

    // Writes a field's clearing at once; when that is refused, Current still drops it and a later write carries it, the
    // next start clearing it meanwhile (State: Move).
    private static Outcome Clear(SettingsKeeper keeper, Func<Settings, Settings> change, Func<Task<Outcome>, Outcome> wait)
    {
        var cleared = wait(keeper.ChangeNow(change));
        if (!cleared.Succeeded)
        {
            keeper.Change(change);
        }

        return cleared;
    }

    // A settings write the store's thread waits for, a held file's retry among it, answering the queued reads meanwhile (G1);
    // an import's record and last write wait so too.
    internal static Outcome WaitAnsweringReads(StoreContext store, Task<Outcome> written)
    {
        while (!written.IsCompleted)
        {
            store.Thread.WaitAnsweringReads(ReadsBetweenWaits);
        }

        return written.GetAwaiter().GetResult();
    }
}
