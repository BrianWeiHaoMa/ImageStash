using ImageStash.Files;
using ImageStash.Layers;
using ImageStash.Media;

namespace ImageStash.Store;

/// <summary>
/// An Open with… rewrite adopted as the item's new original and finished picture (State: Open with…; G27, G28, G29, F6,
/// F7). The copy is taken again as a classification takes it, and the taken file is the one adopted. One transaction
/// drops the layers, keeping the list until now, raises the layer revision, writes the undo record
/// (<see cref="AdoptionUndo"/>), records the adopted bytes' hash on the session with nothing awaiting, and records the
/// steps: an unedited item's one file held in <c>.work\</c>, or an edited item's render deleted, never recycled; then the
/// adopted file renamed into the root under the item's name with its own format's extension. The last transaction makes
/// the adopted file's facts and SHA-256 the row's. While the steps wait, the item is the previous picture until it leaves
/// the root, unlisted until the adopted file lands, and the adopted file after (State: A live failure, Files;
/// <see cref="AdoptionSteps"/>). A later
/// change finishes them first or is refused; the start finishes them or, while a step is still refused, puts the
/// previous picture back and rolls the row back, the rewrite awaiting adoption again. The start also adopts, at once, a
/// rewrite its classification of a copy finds (State: Open with…).
/// </summary>
internal static class ItemAdoption
{
    public const string NoRewrite = "no rewrite awaits adoption";

    // One action to the person, taking the outside edit back, whether its copy is being read or adopted.
    private const string Action = OpenWithCopies.ClassifyAction;

    private const string FinishAtStartAction = "Finish an unfinished adoption";
    private const string UndoAtStartAction = "Undo an unfinished adoption";

    /// <summary>
    /// Adopts the rewrite of the item's copy that awaits adoption and answers the item as the stash lists it then, its
    /// <see cref="StashItem.RenderedRevision"/> naming the adoption. Refused by name, changing nothing, when the item is
    /// not listed or no rewrite awaits; while the copy is still held or missing (the mark kept); when the bytes taken no
    /// longer decode (their hash recorded, the mark cleared); while a pending change of the item cannot finish first; and
    /// while an earlier adoption's record cannot be expired. From the commit on, the steps are attempted one per call on
    /// the store's thread, the waits while a file is held spent off it, so the summon's listing runs between them (G1);
    /// a step still refused after the held-file duration fails after the commit, the steps kept for a later change or the
    /// next start.
    /// </summary>
    public static async Task<Outcome<StashItem>> Adopt(StoreThread thread, StoreContext store, long id)
    {
        var gone = id.ToString();
        if (store.OpenWithRoot is not { } root)
        {
            return new Failure(Action, gone, OpenWithCopies.NoRoot);
        }

        // From the moment it is asked, so Edit's read and a handoff's confirmation of the item, which other calls between
        // its own would otherwise let through, answer once it has run (G20, G8).
        using var running = store.RunningAdoptions.Begin(id);
        while (true)
        {
            var awaiting = await thread.Run(() => AwaitingRewrite(store, id, gone));
            if (awaiting.Failure is { } refused)
            {
                return refused;
            }

            var taken = await OpenWithCopies.TakeLive(thread, store, id, gone, Action, whileMissing: false);
            if (!taken.Succeeded)
            {
                return taken.Failure;
            }

            var take = taken.Value!;
            if (take.WorkPath is not { } work)
            {
                return new Failure(Action, take.Session.File, take.Held ? HeldFileRetry.HeldReason
                    : Directory.Exists(Path.Combine(root, take.Session.Folder)) ? HeldFileRetry.MissingReason : HeldFileRetry.FolderMissingReason);
            }

            // Read off the store's thread, as a classification reads it, so a long decode keeps no listing waiting (G1).
            var read = await Task.Run(() => store.Reader(work, take.Session.File, store.RetryDuration));
            var committed = await thread.Run(() => Commit(store, id, gone, take, work, read));
            if (committed.Value is { } started)
            {
                return await LiveFileSteps.Run(
                    thread, store, started.Item, started.Steps, started.Commits, Action, AdoptionSteps.IsAdoption,
                    steps => RunSteps(store, steps, Action, started.Subject, retryHeld: false),
                    steps => Completed(store, started.Item.Id, steps, Action));
            }

            // Not the item's: discarded off the store's thread; one a scanner holds goes with the next start's clearing.
            await Task.Run(() => WorkFolder.Discard(store, work));
            if (!committed.Succeeded)
            {
                return committed.Failure;
            }

            // Another call recorded the session after this take: the take is stale, so the copy is taken again.
        }
    }

    /// <summary>
    /// Finishes the row's pending adoption before another change of it, or refuses that change by
    /// <paramref name="name"/> as <paramref name="action"/> with why it cannot finish yet; never undoes it. Null once
    /// finished.
    /// </summary>
    public static Failure? FinishPending(StoreContext store, StoredFiles files, string name, string action)
    {
        var subject = ItemQueries.Named(name, files.FinishedFile);
        var failure = Finish(store, files.Id, files.Steps, action, subject);
        return failure is null ? null : new Failure(action, subject, $"its earlier adoption cannot finish yet: {failure.Reason}");
    }

    /// <summary>
    /// At start, before anything reads the stash: the row's adoption finished; while a step is still refused, undone
    /// where its files can go back, unless its file has landed, when only the last transaction is left for the next
    /// start (State: Every change). Every failure is named.
    /// </summary>
    public static IReadOnlyList<Failure> FinishAtStart(StoreContext store, StoredFiles files) => FinishAtStart(store, files.Id, FinishAtStartAction);

    /// <summary>
    /// At start, before anything reads the stash (State: Open with…): the rewrite the start's classification found
    /// awaiting adoption on a listed row with no pending step, adopted at once from the file <paramref name="take"/> took
    /// into <paramref name="work"/>, with a fresh record, its steps run in place and the adoption remembered for
    /// <see cref="OpenWithCopies.Sessions"/>; a step still refused after the retries undone at once, the rewrite awaiting
    /// adoption again (State: Every change). Answers what it names.
    /// </summary>
    public static IReadOnlyList<Failure> AdoptAtStart(StoreContext store, long id, OpenWithCopies.Taken take, string work, ReadPicture read)
    {
        var committed = Commit(store, id, id.ToString(), take, work, Outcome<ReadPicture>.Of(read));
        if (committed.Value is not { } started)
        {
            // One attempt: a taken file held now is the clearing's, which follows and names it.
            store.TryHeldFileOnce(Action, work, () => File.Delete(work));
            return committed.Succeeded ? [] : [committed.Failure];
        }

        var named = FinishAtStart(store, id, Action);
        if (named.Count == 0)
        {
            store.AdoptedAtStart.Add(new AdoptedAtStart(id, started.Item.RenderedRevision));
        }

        return named;
    }

    private static IReadOnlyList<Failure> FinishAtStart(StoreContext store, long id, string action)
    {
        var read = Row.Read(store, StoreOpen.Action, id);
        if (!read.Succeeded)
        {
            return [read.Failure];
        }

        if (read.Value is not { } row)
        {
            return [];
        }

        var refused = Finish(store, row.Files.Id, row.Files.Steps, action, row.Subject);
        if (refused is null)
        {
            return [];
        }

        Failure named = refused with { Action = action, Subject = row.Subject };
        return AdoptionSteps.Landed(store.Folder, row.Files.Steps) is null ? [named, .. PutBack(store, row)] : [named];
    }

    private static Outcome AwaitingRewrite(StoreContext store, long id, string gone)
    {
        var row = OpenWithCopies.ReadRow(store, id, gone, Action);
        if (!row.Succeeded)
        {
            return row.Failure;
        }

        return row.Value!.Session is { Awaiting: true } ? Outcome.Done : new Failure(Action, row.Value.Named, NoRewrite);
    }

    /// <summary>
    /// On the store's thread, the taken file read: refused, or committed, the taken file then the row's, or null to take
    /// the copy again when the session changed since the take.
    /// </summary>
    private static Outcome<Started?> Commit(StoreContext store, long id, string gone, OpenWithCopies.Taken take, string work, Outcome<ReadPicture> read)
    {
        if (!read.Succeeded)
        {
            return read.Failure;
        }

        var listed = OpenWithCopies.ReadRow(store, id, gone, Action);
        if (!listed.Succeeded)
        {
            return listed.Failure;
        }

        if (listed.Value!.Session != take.Session)
        {
            return Outcome<Started?>.Of(null);
        }

        if (!take.Session.Awaiting)
        {
            return new Failure(Action, listed.Value.Named, NoRewrite);
        }

        // The bytes taken now are the ones adopted, whatever rewrote the copy since it was classified.
        var sha256 = Convert.ToHexString(read.Value!.Sha256);
        if (read.Value.Facts is not { } facts)
        {
            return OpenWithCopies.RecordUnreadable(store, id, take.Session, sha256);
        }

        var ready = Ready(store, id, listed.Value.Named);
        if (!ready.Succeeded)
        {
            return ready.Failure;
        }

        var row = ready.Value!;
        var held = row.Files.OriginalInOriginals ? null : WorkFolder.Allocate(store.Folder, Path.GetExtension(row.Files.OriginalFile));
        if (held is { Succeeded: false })
        {
            return held.Failure with { Action = Action, Subject = row.Subject };
        }

        var session = take.Session with { Sha256 = sha256, Awaiting = false };
        var committed = store.Change(Action, () => CommitSteps(store, row, session, work, facts, sha256, held?.Value));
        if (!committed.Succeeded)
        {
            return committed.Failure;
        }

        var place = committed.Value![^1];
        var asCommitted = row.Item! with { FileName = place.To, FilePath = store.Folder.InRoot(place.To), Facts = facts, RenderedRevision = row.LayerRevision + 1, Edited = false };
        return Outcome<Started?>.Of(new Started(asCommitted, committed.Value, row.Subject, store.Commits));
    }

    /// <summary>
    /// The row ready for the adoption's commit: a pending change of it finished first, or the adoption refused (a change
    /// over a pending step finishes it or is refused); then an earlier adoption's live record expired, its previous
    /// original to the Recycle Bin, so one record stands per row.
    /// </summary>
    private static Outcome<Row> Ready(StoreContext store, long id, string named)
    {
        var row = Listed(store, id, named);
        if (row.Succeeded && row.Value!.Files.Steps.Count > 0)
        {
            if (PendingStepRecovery.FinishBeforeChange(store, row.Value.Files, row.Value.Name, Action) is { } unfinished)
            {
                return unfinished;
            }

            row = Listed(store, id, named);
        }

        if (row.Succeeded && row.Value!.Undo is { } undo)
        {
            if (AdoptionUndo.ExpireRecord(store, id, undo, row.Value.Subject, Action).Failure is { } unexpired)
            {
                return unexpired;
            }

            row = Listed(store, id, named);
        }

        return row;
    }

    private static Outcome<Row> Listed(StoreContext store, long id, string named)
    {
        var read = Row.Read(store, Action, id);
        if (!read.Succeeded)
        {
            return read.Failure;
        }

        return read.Value is { Item: not null } row ? Outcome<Row>.Of(row) : new Failure(Action, read.Value?.Subject ?? named, ItemFactChange.NotInStash);
    }

    /// <summary>
    /// The adoption's first transaction: the adopted file's root name, chosen from the item's name with the row's own
    /// files free to it, so it may be the very name of the file it replaces (State: File names, P1); then the layers,
    /// the revision, the record, the session and the steps, the previous picture leaving the root before the adopted
    /// file enters it, so an original is never renamed over (G29). The row's facts, hash and file names wait for the
    /// last transaction: until the adopted file lands, the picture on disk is the previous one (R9).
    /// </summary>
    private static IReadOnlyList<PendingStep> CommitSteps(StoreContext store, Row row, OpenWithRecord session, string work, MediaFacts facts, string sha256, string? held)
    {
        var files = row.Files;
        var extension = ImageFormatExtensions.Of(facts.Format)[0];
        var rootFile = ItemFileNames.ChooseStem(store, files, row.Name, [(extension, ItemFileNames.TakenInRoot(store, files))]) + extension;
        var place = new PendingStep(PendingStep.PlaceAdopted, Path.GetFileName(work), rootFile, Facts: facts, Sha256: sha256);
        var leave = held is null
            ? new PendingStep(PendingStep.RemoveRender, files.FinishedFile, place.From)
            : new PendingStep(PendingStep.HoldInWork, files.FinishedFile, Path.GetFileName(held));
        var record = new AdoptionUndo(
            row.LayerRevision + 1, row.Layers, files.OriginalFile, files.OriginalInOriginals, held is null ? null : leave.To,
            row.OriginalFacts, Convert.ToHexString(row.OriginalSha256), files.FinishedFile);
        IReadOnlyList<PendingStep> steps = [leave, place];
        store.Execute(
            """
            UPDATE items SET previous_layers = layers, layers = '[]', layer_revision = layer_revision + 1, adoption_undo = $undo,
                open_with_session = $session, pending_steps = $steps
            WHERE id = $id
            """,
            ("$id", files.Id),
            ("$undo", record.Encode()),
            ("$session", session.Encode()),
            ("$steps", PendingStep.Encode(steps)));
        return steps;
    }

    /// <summary>
    /// Runs the steps not yet done, a held file retried for the held-file duration, then the last transaction; null once
    /// finished. Before another change and at start, as every change's finish runs.
    /// </summary>
    private static Failure? Finish(StoreContext store, long id, IReadOnlyList<PendingStep> steps, string action, string subject) =>
        RunSteps(store, steps, action, subject, retryHeld: true) ?? Completed(store, id, steps, action);

    /// <summary>
    /// Runs the steps not yet done, in the order the row records them, a held file retried in place when
    /// <paramref name="retryHeld"/>, else tried once (a caller that spaces its attempts); null once all are done.
    /// </summary>
    private static Failure? RunSteps(StoreContext store, IReadOnlyList<PendingStep> steps, string action, string subject, bool retryHeld)
    {
        foreach (var step in steps.Where(step => !step.IsDone(store.Folder)))
        {
            var done = RunStep(store, step, action, subject, retryHeld);
            if (!done.Succeeded)
            {
                return done.Failure;
            }
        }

        return null;
    }

    private static Failure? Completed(StoreContext store, long id, IReadOnlyList<PendingStep> steps, string action) =>
        store.Change(action, () => Complete(store, id, steps.First(step => step.Step == PendingStep.PlaceAdopted))).Failure;

    /// <summary>
    /// Runs one step on disk, a held file retried when <paramref name="retryHeld"/> before its failure is named (G35, E3): the
    /// hold and the place are plain renames, never over a file, since whatever is at their names is no file of this
    /// item's (G29); the render's removal deletes it, never recycled.
    /// </summary>
    private static Outcome RunStep(StoreContext store, PendingStep step, string action, string subject, bool retryHeld)
    {
        var from = step.FromPath(store.Folder);
        var to = step.ToPath(store.Folder);
        var done = step.Step switch
        {
            PendingStep.HoldInWork or PendingStep.PlaceAdopted => Attempt(() => File.Move(from, to)),
            PendingStep.RemoveRender => Attempt(() => File.Delete(from)),
            _ => new Failure(action, subject, $"its step \"{step.Step}\" is not one an adoption takes"),
        };
        return done.Succeeded ? done : done.Failure with { Subject = subject };

        Outcome Attempt(Action onDisk) => retryHeld ? store.RetryHeldFile(action, from, onDisk) : store.TryHeldFileOnce(action, from, onDisk);
    }

    /// <summary>The last transaction: the adopted file the item's one file, its original's record and hash, unedited (F7).</summary>
    private static void Complete(StoreContext store, long id, PendingStep place)
    {
        var facts = place.Facts!;
        store.Execute(
            """
            UPDATE items SET format = $format, width = $width, height = $height, animated = $animated, frame_count = $frame_count,
                duration_ms = $duration_ms, original_sha256 = $sha256, finished_file = $file, original_file = $file, original_in_originals = 0,
                finished_format = NULL, finished_width = NULL, finished_height = NULL, finished_animated = NULL,
                finished_frame_count = NULL, finished_duration_ms = NULL,
                previous_layers = NULL, rendered_revision = layer_revision, pending_steps = NULL
            WHERE id = $id
            """,
            ("$id", id),
            ("$format", facts.Format.ToString()),
            ("$width", facts.Width),
            ("$height", facts.Height),
            ("$animated", facts.Animated),
            ("$frame_count", facts.FrameCount),
            ("$duration_ms", facts.DurationMs),
            ("$sha256", AdoptionSteps.Sha256Of(place)),
            ("$file", place.To));
    }

    /// <summary>
    /// At start, an adoption that cannot finish is undone where its files can go back (State: Every change): the held
    /// original renamed back into the root, or the render made again from the previous layers over the original where
    /// the row keeps it and renamed under its name (the same list over the same original, so the render's facts still
    /// describe it); then the row rolled back, the rewrite awaiting adoption again; only then the adopted file discarded,
    /// since a place step whose file is gone reads as landed at the next start. A file that cannot go back, or a render
    /// that cannot be made, leaves the row as it is, unlisted, for the next start.
    /// </summary>
    private static IReadOnlyList<Failure> PutBack(StoreContext store, Row row)
    {
        var folder = store.Folder;
        var steps = row.Files.Steps;
        if (steps.FirstOrDefault(step => step.Step is PendingStep.HoldInWork or PendingStep.RemoveRender) is { } leave && leave.IsDone(folder))
        {
            var back = leave.Step == PendingStep.HoldInWork
                ? ItemSave.Move(store, UndoAtStartAction, leave.ToPath(folder), leave.FromPath(folder), replacing: false)
                : RenderAgain(store, row, leave.FromPath(folder));
            if (!back.Succeeded)
            {
                return [back.Failure with { Action = UndoAtStartAction, Subject = row.Subject }];
            }
        }

        var rolledBack = store.Change(UndoAtStartAction, () => store.Execute(
            """
            UPDATE items SET layers = previous_layers, previous_layers = NULL, layer_revision = rendered_revision, pending_steps = NULL,
                adoption_undo = NULL, open_with_session = $session
            WHERE id = $id
            """,
            ("$id", row.Files.Id),
            ("$session", row.Session is null ? null : (row.Session with { Awaiting = true }).Encode())));
        if (!rolledBack.Succeeded)
        {
            return [rolledBack.Failure];
        }

        // One attempt: a file held now is the clearing's, which follows and names it.
        var adopted = steps.First(step => step.Step == PendingStep.PlaceAdopted).FromPath(folder);
        store.TryHeldFileOnce(UndoAtStartAction, adopted, () => File.Delete(adopted));
        return [];
    }

    private static Outcome RenderAgain(StoreContext store, Row row, string to)
    {
        var layers = LayerList.FromJson(row.PreviousLayers, row.Subject);
        if (!layers.Succeeded)
        {
            return layers.Failure;
        }

        var (rendered, fresh) = store.RenderAgain(row.Files.OriginalPath(store.Folder), row.Subject, layers.Value!, FinishedFormat.Of(row.OriginalFacts));
        var placed = rendered.Succeeded ? ItemSave.Move(store, UndoAtStartAction, fresh, to, replacing: false) : rendered.Failure;
        if (!placed.Succeeded)
        {
            store.TryHeldFileOnce(UndoAtStartAction, fresh, () => File.Delete(fresh));
        }

        return placed;
    }

    /// <summary>A committed adoption: the item as committed, its steps as the row records them, its name, and the store's commits once it committed.</summary>
    private sealed record Started(StashItem Item, IReadOnlyList<PendingStep> Steps, string Subject, long Commits);

    /// <summary>
    /// A row as the adoption reads it: the item when the stash lists it, where its files are, its name, its layer lists
    /// and revision, its original's record and hash as the columns hold them, its session, and its undo record as stored.
    /// </summary>
    private sealed record Row(StashItem? Item, StoredFiles Files, string Name, string Layers, string? PreviousLayers, long LayerRevision, MediaFacts OriginalFacts, byte[] OriginalSha256, OpenWithRecord? Session, string? Undo)
    {
        private const int After = StashItem.ColumnCount + StoredFiles.ColumnCount;

        public string Subject => ItemQueries.Named(Name, Files.FinishedFile);

        /// <summary>The row of identity <paramref name="id"/>, marked deleted or not, or null when it is gone.</summary>
        public static Outcome<Row?> Read(StoreContext store, string action, long id) =>
            store.Read(action, () =>
            {
                var tags = store.Query("SELECT tag FROM tags WHERE item_id = $id ORDER BY rowid", r => r.GetString(0), ("$id", id));
                return store.Query(
                    $"SELECT {StashItem.Columns}, {StoredFiles.Columns}, i.deleted, i.layers, i.previous_layers, i.original_sha256, i.open_with_session, i.adoption_undo FROM items i WHERE i.id = $id",
                    r => new Row(
                        r.GetBoolean(After) ? null : StashItem.ListedFromRow(r, store, tags),
                        StoredFiles.FromRow(r, StashItem.ColumnCount),
                        r.GetString(1),
                        r.GetString(After + 1),
                        r.IsDBNull(After + 2) ? null : r.GetString(After + 2),
                        StashItem.RecordedLayerRevision(r),
                        StashItem.OriginalFacts(r),
                        r.GetFieldValue<byte[]>(After + 3),
                        r.IsDBNull(After + 4) ? null : OpenWithRecord.Decode(r.GetString(After + 4)),
                        r.IsDBNull(After + 5) ? null : r.GetString(After + 5)),
                    ("$id", id)).SingleOrDefault();
            });
    }
}
