using ImageStash.Files;
using ImageStash.Layers;
using ImageStash.Media;
using ImageStash.Seams;
using Microsoft.Data.Sqlite;

namespace ImageStash.Store;

/// <summary>
/// What every store operation works with, on the store's thread: the one connection, the folder, the Recycle Bin, the
/// held-file duration, the clock, the renderer that redoes an edited item's finished picture from its original and its
/// layers, the folder the Open with… copies are kept in (null when this stash keeps none), Media's reader of a copy's
/// hash and facts, and the thread itself. Every change runs as one transaction, retried whole while the store is held
/// (G35); every wait on a held file goes through <see cref="RetryHeldFile{T}"/>, <see cref="TryHeldFileOnce{T}"/> or
/// <see cref="RetryWhileHeld{T}"/>, which on the store's thread answer the queued reads between attempts (G1), and the
/// Recycle Bin seam, the renderer and the reader, called there, wait through it too (<see cref="Recycle"/>,
/// <see cref="RemoveRecycled"/>, <see cref="RenderAgain"/>, <see cref="ReadTakenCopy"/>).
/// </summary>
internal sealed record StoreContext(
    SqliteConnection Connection,
    StashFolder Folder,
    IRecycleBin RecycleBin,
    TimeSpan RetryDuration,
    TimeProvider Clock,
    FinishedPictureRenderer Renderer,
    string? OpenWithRoot,
    PictureReader Reader,
    StoreThread Thread)
{
    // How many of Change's and Read's transactions are open on the store's thread, which alone runs them.
    private int transactions;

    // Set on the store's thread alone, by the Move; read from any thread by the entries that hand out a file without queueing.
    private volatile MoveStage moveStage;

    /// <summary>
    /// How far a Move of this stash has gone (State: Move): while it copies, no change runs; once it has switched, this
    /// store is no longer the record and nothing runs, so a call queued during the copy touches neither folder.
    /// </summary>
    public MoveStage MoveStage
    {
        get => moveStage;
        set => moveStage = value;
    }

    // Set on the store's thread alone, by an import once its record has landed; read from any thread as moveStage is.
    private volatile bool importSpent;

    /// <summary>
    /// Whether an import's record has made the folder it extracted the stash (State: Import stash…): this store is then
    /// no longer the record and nothing runs, as after a Move's switch, until it is disposed.
    /// </summary>
    public bool ImportSpent
    {
        get => importSpent;
        set => importSpent = value;
    }

    /// <summary>
    /// The refusal of <paramref name="action"/> on <paramref name="subject"/> while a Move copies or has switched, or once
    /// an import has spent this store; null otherwise.
    /// </summary>
    public Failure? MoveRefusal(string action, string subject) =>
        importSpent ? new Failure(action, subject, StashImport.InProgress)
        : moveStage == MoveStage.None ? null : new Failure(action, subject, MoveReasons.InProgress);

    /// <summary>Whether nothing of this store runs any more: a Move has switched away from it, or an import has spent it.</summary>
    public bool Spent => importSpent || moveStage == MoveStage.Switched;

    /// <summary>
    /// The Recycle Bin identities a later removal (an import's Undo) was given for files of a row a Delete marked, by row,
    /// while that Delete's notice can still undo it: in memory, as the Delete's own identities are, so that its Undo
    /// removes these entries too (State: the Delete bullet).
    /// </summary>
    public Dictionary<long, List<string>> EntriesForDeleteUndo { get; } = [];

    /// <summary>
    /// How many changes this open store has committed: raised once by each <see cref="Change{T}"/> that commits, never
    /// by a read, and stamped on every item a read answers (<see cref="StashItem.CommitsWhenRead"/>). Every file step
    /// follows the commit of its row (State: Every change), so an item whose stamp still equals it has its file where it
    /// was read, or none; never another row's. In memory and afresh at each open (R8).
    /// </summary>
    public long Commits { get; private set; }

    /// <summary>
    /// How many file steps the store's thread has made that left a file of the root or <c>originals\</c> gone or
    /// replaced, each a held-file wrapper's step or recycle that succeeded, and stamped beside <see cref="Commits"/> on
    /// every item a read answers (<see cref="StashItem.FileStepsWhenRead"/>): a file step may move a file after its row's
    /// commit with no commit after it (its clearing refused while the store is held), so an item read with steps pending
    /// is where it was read only while neither count has moved since (G8). A step that moved no such file (a copy out of
    /// one, a step in <c>.work\</c> or outside the stash) leaves every item where it was read, and is not counted (G35).
    /// In memory, as Commits.
    /// </summary>
    public long FileSteps { get; private set; }

    /// <summary>The file names a copy, a paste or a drag-out holds while it hands them over, taken for every other row.</summary>
    public NamesInHand InHand { get; } = new();

    /// <summary>The items whose Open with… adoption or Undo is running, which Edit's read and a handoff's confirmation wait for.</summary>
    public RunningAdoptions RunningAdoptions { get; } = new();

    /// <summary>
    /// The adoptions of rewritten Open with… copies this start made, until the first <see cref="OpenWithCopies.Sessions"/>
    /// after the open takes them: in memory, as what this start did (G27).
    /// </summary>
    public List<AdoptedAtStart> AdoptedAtStart { get; } = [];

    /// <summary>Runs <paramref name="change"/> as one transaction; a failure names <paramref name="action"/> and the store.</summary>
    public Outcome<T> Change<T>(string action, Func<T> change)
    {
        if (MoveRefusal(action, StashFolder.StoreFileName) is { } moving)
        {
            return moving;
        }

        var changed = HeldFileRetry.RunTransaction(action, Folder.StoreFile, RetryDuration, () => InTransaction(() =>
        {
            using var transaction = Connection.BeginTransaction();
            var result = change();
            transaction.Commit();
            return result;
        }));
        // Once, after the commit: the transaction may have run, and been rolled back, several times while the store was held.
        if (changed.Succeeded)
        {
            Commits++;
        }

        return changed;
    }

    public Outcome Change(string action, Action change)
    {
        var outcome = Change(action, () => { change(); return true; });
        return outcome.Succeeded ? Outcome.Done : outcome.Failure;
    }

    /// <summary>
    /// Runs the statements of <paramref name="read"/> in one deferred transaction, so they see one state of the store;
    /// answered while a Move copies, refused once it has switched (State: Move) or an import has spent this store.
    /// </summary>
    public Outcome<T> Read<T>(string action, Func<T> read) =>
        Spent ? MoveRefusal(action, StashFolder.StoreFileName)! : HeldFileRetry.RunTransaction(action, Folder.StoreFile, RetryDuration, () => InTransaction(() =>
        {
            using var transaction = Connection.BeginTransaction(deferred: true);
            var result = read();
            transaction.Commit();
            return result;
        }));

    /// <summary>
    /// Runs <paramref name="step"/> on <paramref name="path"/>, again while the file is held, for
    /// <see cref="RetryDuration"/>, as <see cref="HeldFileRetry.Run{T}"/> does (G35); on the store's thread the reads
    /// queued meanwhile are answered between attempts instead of a sleep (G1), and changes stay queued.
    /// </summary>
    public Outcome<T> RetryHeldFile<T>(string action, string path, Func<T> step, string? replaced = null)
    {
        RefuseInsideTransaction();
        return Stepped(HeldFileRetry.Run(action, path, RetryDuration, step, replaced, Wait()), path, replaced);
    }

    public Outcome RetryHeldFile(string action, string path, Action step, string? replaced = null)
    {
        RefuseInsideTransaction();
        return Stepped(HeldFileRetry.Run(action, path, RetryDuration, step, replaced, Wait()), path, replaced);
    }

    /// <summary>One attempt at <paramref name="step"/>, for a step that leaves a held file to the next start's clearing.</summary>
    public Outcome<T> TryHeldFileOnce<T>(string action, string path, Func<T> step, string? replaced = null)
    {
        RefuseInsideTransaction();
        return Stepped(HeldFileRetry.Run(action, path, TimeSpan.Zero, step, replaced), path, replaced);
    }

    public Outcome TryHeldFileOnce(string action, string path, Action step, string? replaced = null)
    {
        RefuseInsideTransaction();
        return Stepped(HeldFileRetry.Run(action, path, TimeSpan.Zero, step, replaced), path, replaced);
    }

    /// <summary>
    /// <paramref name="attempt"/>, a call that does not retry a held file itself (the Recycle Bin seam, Media's render or
    /// reader given no retry), again while <paramref name="held"/> says its answer is a held file, for
    /// <see cref="RetryDuration"/> from its first answer (<see cref="HeldFileRetry.RunWhileHeld{T}"/>), the queued reads
    /// answered between attempts as <see cref="RetryHeldFile{T}"/> answers them.
    /// </summary>
    public T RetryWhileHeld<T>(Func<T> attempt, Func<T, bool> held)
    {
        RefuseInsideTransaction();
        return HeldFileRetry.RunWhileHeld(RetryDuration, attempt, held, Wait());
    }

    /// <summary>
    /// Recycles <paramref name="paths"/> through the Recycle Bin seam given no retry of its own, each file it answers held
    /// asked again through <see cref="RetryWhileHeld{T}"/>, so the store's thread answers reads meanwhile (G1, G35): one
    /// outcome per path, in order.
    /// </summary>
    public IReadOnlyList<Outcome<RecycledFile>> Recycle(IReadOnlyList<string> paths)
    {
        var recycled = EachWhileHeld(paths, asked => RecycleBin.Recycle(asked, TimeSpan.Zero));
        // A file removed for good is gone from the root as a recycled one is, so it counts alike, identity or none (G8).
        if (Thread.IsCurrent && paths.Where((path, i) => recycled[i].Succeeded).Any(Folder.IsRootOrOriginalsFile))
        {
            FileSteps++;
        }

        return recycled;
    }

    /// <summary>Removes the recycled entries <paramref name="identities"/> names for good, as <see cref="Recycle"/> recycles.</summary>
    public IReadOnlyList<Outcome> RemoveRecycled(IReadOnlyList<string> identities) =>
        EachWhileHeld(identities, asked => RecycleBin.Remove(asked, TimeSpan.Zero));

    /// <summary>
    /// Renders <paramref name="layers"/> over <paramref name="originalPath"/> again on the store's thread (State: Save) into
    /// a fresh <c>.work\</c> path, Media given no retry of its own: the facts read back from the written file, and that
    /// path, which the caller places or discards. While the answer is a held file it is asked again through
    /// <see cref="RetryWhileHeld{T}"/>: a render refused before it wrote anything (its original held) is made again, and one
    /// whose readback was refused once its file was written has that file read back again, never rendered again, since
    /// another fresh file is one a scanner holds again (E3, G35).
    /// </summary>
    public (Outcome<MediaFacts> Rendered, string WorkPath) RenderAgain(string originalPath, string subject, LayerList layers, ImageFormat format)
    {
        var work = WorkFolder.Allocate(Folder, FinishedFormat.Extension(format)).Value!;
        var written = false;
        var rendered = RetryWhileHeld(Attempt, IsHeld);
        return (rendered, work);

        Outcome<MediaFacts> Attempt()
        {
            if (written)
            {
                var read = Reader(work, subject, TimeSpan.Zero);
                if (!read.Succeeded)
                {
                    return read.Failure;
                }

                if (read.Value!.Facts is { } facts)
                {
                    return Outcome<MediaFacts>.Of(facts);
                }

                // Bytes that do not decode are a write stopped part way: made again at a path of its own, the partial
                // file deleted, or, while held, left to the next start's clearing of .work\ (State: Files).
                TryHeldFileOnce("Discard a render", work, () => File.Delete(work));
                work = WorkFolder.Allocate(Folder, FinishedFormat.Extension(format)).Value!;
            }

            var answer = Renderer(originalPath, subject, layers, format, work, TimeSpan.Zero);
            written = IsHeld(answer) && File.Exists(work);
            return answer.Succeeded ? Outcome<MediaFacts>.Of(answer.Value!.Facts) : answer.Failure;
        }
    }

    /// <summary>Reads an Open with… copy taken into <c>.work\</c> through Media's reader on the store's thread, asked again while the file is held, as <see cref="RenderAgain"/> renders.</summary>
    public Outcome<ReadPicture> ReadTakenCopy(string path, string name) =>
        RetryWhileHeld(() => Reader(path, name, TimeSpan.Zero), IsHeld);

    public int Execute(string sql, params (string Name, object? Value)[] parameters)
    {
        using var command = Command(sql, parameters);
        return command.ExecuteNonQuery();
    }

    public object? Scalar(string sql, params (string Name, object? Value)[] parameters)
    {
        using var command = Command(sql, parameters);
        return command.ExecuteScalar();
    }

    public List<T> Query<T>(string sql, Func<SqliteDataReader, T> row, params (string Name, object? Value)[] parameters)
    {
        using var command = Command(sql, parameters);
        using var reader = command.ExecuteReader();
        var rows = new List<T>();
        while (reader.Read())
        {
            rows.Add(row(reader));
        }

        return rows;
    }

    private T InTransaction<T>(Func<T> run)
    {
        transactions++;
        try
        {
            return run();
        }
        finally
        {
            transactions--;
        }
    }

    // A seam call given several files asks again only for those it answered held: each other keeps the answer its first
    // attempt gave, since a file recycled or an entry removed would be answered missing if asked again.
    private IReadOnlyList<TOutcome> EachWhileHeld<TOutcome>(IReadOnlyList<string> inputs, Func<IReadOnlyList<string>, IReadOnlyList<TOutcome>> call)
        where TOutcome : Outcome
    {
        if (inputs.Count == 0)
        {
            return [];
        }

        var answers = new TOutcome[inputs.Count];
        var asking = Enumerable.Range(0, inputs.Count).ToList();
        return RetryWhileHeld(
            () =>
            {
                foreach (var (index, answer) in asking.Zip(call([.. asking.Select(i => inputs[i])])))
                {
                    answers[index] = answer;
                }

                asking = [.. asking.Where(i => IsHeld(answers[i]))];
                return answers;
            },
            _ => asking.Count > 0);
    }

    // The seam and Media answer a file another program holds with this reason (IRecycleBin; HeldFileRetry), and only that is asked again.
    private static bool IsHeld(Outcome answer) => answer.Failure?.Reason == HeldFileRetry.HeldReason;

    // On the store's thread the reads queued meanwhile are answered between attempts; a caller off it (a discard under
    // Task.Run) sleeps there, as it always has, and answers none.
    private Action<TimeSpan>? Wait() => Thread.IsCurrent ? Thread.WaitAnsweringReads : null;

    // Counted on the store's thread alone, which alone stamps and compares the count; a step off it (a discard under
    // Task.Run) touches only .work\. A file of the root or originals\ is gone after a move or a delete of it and still
    // there after a copy or a read of it, and nothing but this thread creates one (E2). A rename that changes only case
    // keeps its file and is not counted: a read since its commit lists the new name already (PendingStep.Located).
    private TOutcome Stepped<TOutcome>(TOutcome outcome, string path, string? replaced)
        where TOutcome : Outcome
    {
        if (outcome.Succeeded && Thread.IsCurrent
            && (Folder.IsRootOrOriginalsFile(replaced) || (Folder.IsRootOrOriginalsFile(path) && !File.Exists(path))))
        {
            FileSteps++;
        }

        return outcome;
    }

    // A read run between attempts would begin a transaction inside this one, which the connection refuses, and would see
    // its uncommitted statements: a file step inside a transaction is a defect, so it is told loudly, never named.
    private void RefuseInsideTransaction()
    {
        if (Thread.IsCurrent && transactions > 0)
        {
            throw new InvalidOperationException("A wait on a held file was asked inside a store transaction.");
        }
    }

    // The connection hands a new command the transaction in progress, if any.
    private SqliteCommand Command(string sql, (string Name, object? Value)[] parameters)
    {
        var command = Connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        return command;
    }
}
