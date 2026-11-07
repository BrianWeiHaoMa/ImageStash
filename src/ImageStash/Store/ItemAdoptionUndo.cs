using ImageStash.Files;
using ImageStash.Layers;
using ImageStash.Seams;

namespace ImageStash.Store;

/// <summary>
/// The Undo of an Open with… adoption (State: Open with…; G27, G28, G29, G25), from its undo record
/// (<see cref="AdoptionUndo"/>): an edited previous's picture rendered again before anything is committed; one
/// transaction restoring the layers, raising the layer revision and recording the steps: the adopted picture set aside
/// into <c>.work\</c>; the held original renamed back into the root, or the original renamed in <c>originals\</c> and the
/// render placed, under one stem from the item's current name (State: File names, P1); the set-aside picture recycled
/// last, so every step before it can go back. The last transaction writes the previous facts, hash and names and clears
/// the record. While the steps wait, the item is the adopted picture, then unlisted, then the previous picture
/// (<see cref="AdoptionSteps"/>). A later change finishes them first or is refused; the start finishes them or, while a
/// step is refused and the recycle has not run, puts the adopted picture back, its record left for the start's expiry.
/// </summary>
internal static class ItemAdoptionUndo
{
    public const string Action = "Undo an Open with… edit";

    public const string PreviousGone = "the picture it replaced is no longer in the stash";

    private const string FinishAtStartAction = "Finish an unfinished undo of an adoption";
    private const string UndoAtStartAction = "Undo an unfinished undo of an adoption";

    /// <summary>
    /// Undoes the adoption <paramref name="revision"/> names and answers the item as the stash lists it then; null, with
    /// nothing done, when no record names it, the row is gone or marked deleted, or its layers changed since (a Save or a
    /// Revert, G25). Refused by name, changing nothing, when its record cannot be read or its previous original is gone, a
    /// pending change cannot finish first, or an edited previous's picture cannot be rendered. From the commit on, the
    /// steps run as <see cref="LiveFileSteps"/> runs them; a step still refused fails after the commit, the steps kept.
    /// </summary>
    public static async Task<Outcome<StashItem?>> Undo(StoreThread thread, StoreContext store, long id, long revision)
    {
        // From the moment it is asked, as the adoption is marked (G20, G8).
        using var running = store.RunningAdoptions.Begin(id);
        while (true)
        {
            var ready = await thread.Run(() => Ready(store, id, revision));
            if (!ready.Succeeded)
            {
                return ready.Failure;
            }

            if (ready.Value is not { } undo)
            {
                return Outcome<StashItem?>.Of(null);
            }

            // Off the store's thread, as a Save's picture is rendered, so a long animation keeps no listing waiting (G1;
            // Approach); and before the commit, so a picture that cannot be made changes nothing (R9).
            var render = undo.Record.InOriginals ? await Task.Run(() => Render(store, undo)) : null;
            if (render is { Succeeded: false })
            {
                return render.Failure;
            }

            var committed = await thread.Run(() => Commit(store, undo, render?.Value));
            if (committed.Value is { } started)
            {
                var undone = await LiveFileSteps.Run(
                    thread, store, started.Item, started.Steps, started.Commits, Action, AdoptionSteps.IsUndo,
                    steps => RunSteps(store, steps, Action, started.Subject, retryHeld: false),
                    steps => Completed(store, started.Item.Id, steps, Action));
                return undone.Succeeded ? Outcome<StashItem?>.Of(undone.Value) : Outcome<StashItem?>.FailAfterCommit(undone.Failure, undone.Value);
            }

            if (render?.Value is { } unused)
            {
                await Task.Run(() => WorkFolder.Discard(store, unused.WorkPath));
            }

            if (!committed.Succeeded)
            {
                return committed.Failure;
            }

            // The row changed since it was read: read again.
        }
    }

    /// <summary>Finishes the row's pending Undo before another change of it, or refuses that change by name with why; never undoes it.</summary>
    public static Failure? FinishPending(StoreContext store, StoredFiles files, string name, string action)
    {
        var subject = ItemQueries.Named(name, files.FinishedFile);
        var read = Row.Read(store, action, files.Id);
        if (!read.Succeeded)
        {
            return read.Failure;
        }

        var failure = read.Value is { } row ? Finish(store, row, action).Failure : null;
        return failure is null ? null : new Failure(action, subject, $"its earlier undo of an adoption cannot finish yet: {failure.Reason}");
    }

    /// <summary>
    /// At start: the row's Undo finished; while a step is still refused, undone where its files can go back, unless the
    /// adopted picture is in the Recycle Bin, when the next start runs the last transaction (State: Every change).
    /// </summary>
    public static IReadOnlyList<Failure> FinishAtStart(StoreContext store, StoredFiles files)
    {
        var read = Row.Read(store, StoreOpen.Action, files.Id);
        if (!read.Succeeded)
        {
            return [read.Failure];
        }

        if (read.Value is not { } row)
        {
            return [];
        }

        var (refused, steps) = Finish(store, row, FinishAtStartAction);
        if (refused is null)
        {
            return [];
        }

        Failure named = refused with { Action = FinishAtStartAction, Subject = row.Subject };
        // The recycle is the one point of no return: until it has run, the adopted picture is still in .work\ to put back.
        var recycle = steps.First(step => step.Step == PendingStep.RecycleSetAside);
        return recycle.IsDone(store.Folder, steps) ? [named] : [named, .. PutBack(store, row.Files.Id, row.Subject, steps)];
    }

    /// <summary>
    /// The row the Undo of <paramref name="revision"/> acts on, with its record, or null with nothing to undo; a pending
    /// change of it finished first, or the Undo refused by name, and the row read again, which the finish may change.
    /// </summary>
    private static Outcome<Undoable?> Ready(StoreContext store, long id, long revision)
    {
        var finished = false;
        while (true)
        {
            var read = Row.Read(store, Action, id);
            if (!read.Succeeded)
            {
                return read.Failure;
            }

            if (read.Value is not { Deleted: false, Undo: { } json } row)
            {
                return Outcome<Undoable?>.Of(null);
            }

            if (AdoptionUndo.Decode(json) is not { } record)
            {
                return new Failure(Action, row.Subject, AdoptionUndo.Unreadable);
            }

            // A Save or a Revert since the adoption has raised the layers past it, and they are the only history now (G25).
            if (record.Revision != revision || row.LayerRevision != revision)
            {
                return Outcome<Undoable?>.Of(null);
            }

            if (row.Files.Steps.Count == 0)
            {
                // An expiry that recycled it but could not clear the record leaves nothing to restore, nor a restore to judge.
                return File.Exists(record.PreviousPath(store.Folder))
                    ? Outcome<Undoable?>.Of(new Undoable(row, record, json))
                    : new Failure(Action, row.Subject, PreviousGone);
            }

            if (finished)
            {
                return new Failure(Action, row.Subject, ItemFactChange.NotInStash);
            }

            if (PendingStepRecovery.FinishBeforeChange(store, row.Files, row.Name, Action) is { } unfinished)
            {
                return unfinished;
            }

            finished = true;
        }
    }

    /// <summary>An edited previous's finished picture, rendered from the record's layers over the previous original in <c>originals\</c> into <c>.work\</c>.</summary>
    private static Outcome<RenderedPicture> Render(StoreContext store, Undoable undo)
    {
        var subject = undo.Row.Subject;
        var layers = LayerList.FromJson(undo.Record.Layers, subject);
        if (!layers.Succeeded)
        {
            return layers.Failure with { Action = Action };
        }

        var format = FinishedFormat.Of(undo.Record.Facts);
        var work = WorkFolder.Allocate(store.Folder, FinishedFormat.Extension(format));
        if (!work.Succeeded)
        {
            return work.Failure with { Action = Action, Subject = subject };
        }

        var rendered = store.Renderer(undo.Record.PreviousPath(store.Folder), subject, layers.Value!, format, work.Value!, store.RetryDuration);
        if (!rendered.Succeeded)
        {
            Discard(store, work.Value!);
            return rendered.Failure with { Action = Action, Subject = subject };
        }

        return Outcome<RenderedPicture>.Of(new RenderedPicture(work.Value!, rendered.Value!.Facts, rendered.Value.OriginalSha256));
    }

    /// <summary>
    /// The Undo committed, or refused, or null to read the row again when it changed since it was read; a render made over
    /// bytes other than the previous original's is refused by name.
    /// </summary>
    private static Outcome<Started?> Commit(StoreContext store, Undoable undo, RenderedPicture? render)
    {
        var now = Ready(store, undo.Row.Files.Id, undo.Record.Revision);
        if (!now.Succeeded)
        {
            return now.Failure;
        }

        if (now.Value is not { } ready || ready.Json != undo.Json)
        {
            return Outcome<Started?>.Of(null);
        }

        var row = ready.Row;
        if (render is not null && !render.OriginalSha256.AsSpan().SequenceEqual(Convert.FromHexString(ready.Record.Sha256)))
        {
            return new Failure(Action, row.Subject, ItemSave.ChangedWhileRendered);
        }

        var aside = WorkFolder.Allocate(store.Folder, Path.GetExtension(row.Files.FinishedFile));
        if (!aside.Succeeded)
        {
            return aside.Failure with { Action = Action, Subject = row.Subject };
        }

        var committed = store.Change(Action, () => CommitSteps(store, ready, render, Path.GetFileName(aside.Value!)));
        if (!committed.Succeeded)
        {
            return committed.Failure;
        }

        var steps = committed.Value!;
        var landing = PendingStep.Restoring(steps)!;
        var asCommitted = row.Item! with { FileName = landing.To, FilePath = store.Folder.InRoot(landing.To), Facts = landing.Facts!, RenderedRevision = row.LayerRevision + 1, Edited = render is not null };
        return Outcome<Started?>.Of(new Started(asCommitted, steps, row.Subject, store.Commits));
    }

    /// <summary>
    /// The Undo's first transaction: one stem from the item's current name, the row's own files and its own record's
    /// previous original free to it (P1); the restored layers, the revision and the steps, the adopted picture leaving the
    /// root before the previous one enters it (G29). The facts, hash, names and record wait for the last transaction (R9).
    /// </summary>
    private static IReadOnlyList<PendingStep> CommitSteps(StoreContext store, Undoable undo, RenderedPicture? render, string aside)
    {
        var (row, record) = (undo.Row, undo.Record);
        var files = row.Files;
        var originalExtension = Path.GetExtension(record.OriginalFile);
        var setAside = new PendingStep(PendingStep.SetAsideAdopted, files.FinishedFile, aside);
        var recycle = new PendingStep(PendingStep.RecycleSetAside, aside, aside);
        IReadOnlyList<PendingStep> steps;
        if (render is null)
        {
            var restored = ItemFileNames.ChooseStem(store, files, row.Name, [(originalExtension, ItemFileNames.TakenInRoot(store, files))]) + originalExtension;
            steps = [setAside, new PendingStep(PendingStep.RestoreHeld, record.HeldCopy!, restored, Facts: record.Facts, Sha256: record.Sha256), recycle];
        }
        else
        {
            var finishedExtension = Path.GetExtension(render.WorkPath);
            var inOriginals = ItemFileNames.TakenInOriginals(store, files);
            inOriginals.Remove(record.OriginalFile);
            var stem = ItemFileNames.ChooseStem(store, files, row.Name, [(finishedExtension, ItemFileNames.TakenInRoot(store, files)), (originalExtension, inOriginals)]);
            // Written even when the stem is unchanged, so through every window the step says where the original is.
            var original = new PendingStep(PendingStep.RenameInOriginals, record.OriginalFile, stem + originalExtension, Facts: record.Facts, Sha256: record.Sha256);
            var place = new PendingStep(PendingStep.PlaceRender, Path.GetFileName(render.WorkPath), stem + finishedExtension, Facts: render.Facts, Sha256: record.Sha256);
            steps = [setAside, original, place, recycle];
        }

        store.Execute(
            "UPDATE items SET layers = $layers, previous_layers = '[]', layer_revision = layer_revision + 1, pending_steps = $steps WHERE id = $id",
            ("$id", files.Id),
            ("$layers", record.Layers),
            ("$steps", PendingStep.Encode(steps)));
        return steps;
    }

    /// <summary>
    /// Before another change and at start: an edited previous's render not yet placed made again, as a Save's is (State:
    /// Save), then the steps not done, a held file retried, and the last transaction; the failure, or null, with the steps.
    /// </summary>
    private static (Failure? Failure, IReadOnlyList<PendingStep> Steps) Finish(StoreContext store, Row row, string action)
    {
        var (unrendered, steps) = RenderAgain(store, row, action);
        return unrendered is not null ? (unrendered, steps) : (RunSteps(store, steps, action, row.Subject, retryHeld: true) ?? Completed(store, row.Files.Id, steps, action), steps);
    }

    /// <summary>
    /// An edited previous's render not yet placed, made again over the original where its steps have it and recorded on
    /// its place step before it lands, so the step describes the render that does; the one it replaces then discarded.
    /// </summary>
    private static (Failure? Failure, IReadOnlyList<PendingStep> Steps) RenderAgain(StoreContext store, Row row, string action)
    {
        var folder = store.Folder;
        var steps = row.Files.Steps;
        if (steps.FirstOrDefault(step => step.Step == PendingStep.PlaceRender) is not { } place || place.IsDone(folder))
        {
            return (null, steps);
        }

        var original = steps.First(step => step.Step == PendingStep.RenameInOriginals);
        var layers = LayerList.FromJson(row.Layers, row.Subject);
        if (!layers.Succeeded)
        {
            return (layers.Failure, steps);
        }

        var (rendered, fresh) = store.RenderAgain(original.Located(folder), row.Subject, layers.Value!, FinishedFormat.Of(original.Facts!));
        if (!rendered.Succeeded)
        {
            Discard(store, fresh);
            return (rendered.Failure, steps);
        }

        var recorded = place with { From = Path.GetFileName(fresh), Facts = rendered.Value! };
        IReadOnlyList<PendingStep> now = [.. steps.Select(step => ReferenceEquals(step, place) ? recorded : step)];
        var rerecorded = store.Change(action, () => store.Execute(
            "UPDATE items SET pending_steps = $steps WHERE id = $id", ("$id", row.Files.Id), ("$steps", PendingStep.Encode(now))));
        if (!rerecorded.Succeeded)
        {
            Discard(store, fresh);
            return (rerecorded.Failure, steps);
        }

        Discard(store, place.FromPath(folder));
        return (null, now);
    }

    /// <summary>
    /// Runs the steps not yet done, in the order the row records them, a held file retried in place when
    /// <paramref name="retryHeld"/>, else tried once (a caller that spaces its attempts); null once all are done.
    /// </summary>
    private static Failure? RunSteps(StoreContext store, IReadOnlyList<PendingStep> steps, string action, string subject, bool retryHeld)
    {
        foreach (var step in steps)
        {
            // Judged as each step comes, since the set aside's and the recycle's done rules read the step that brings the previous picture back.
            if (step.IsDone(store.Folder, steps))
            {
                continue;
            }

            var done = RunStep(store, step, action, subject, retryHeld);
            if (!done.Succeeded)
            {
                return done.Failure;
            }
        }

        return null;
    }

    /// <summary>
    /// Runs one step on disk, a held file retried when <paramref name="retryHeld"/> (G35): renames never over a file, since the
    /// adopted picture has left the root first and anything else at their names is no file of this item's (G29); the
    /// recycle through the Recycle Bin, never a delete (G29).
    /// </summary>
    private static Outcome RunStep(StoreContext store, PendingStep step, string action, string subject, bool retryHeld)
    {
        var from = step.FromPath(store.Folder);
        var to = step.ToPath(store.Folder);
        var done = step.Step switch
        {
            PendingStep.SetAsideAdopted or PendingStep.RestoreHeld or PendingStep.RenameInOriginals or PendingStep.PlaceRender =>
                retryHeld ? store.RetryHeldFile(action, from, () => File.Move(from, to)) : store.TryHeldFileOnce(action, from, () => File.Move(from, to)),
            PendingStep.RecycleSetAside => Recycled(retryHeld ? store.Recycle([from])[0] : store.RecycleBin.Recycle([from], TimeSpan.Zero)[0]),
            _ => new Failure(action, subject, $"its step \"{step.Step}\" is not one an undo of an adoption takes"),
        };
        return done.Succeeded ? done : done.Failure with { Action = action, Subject = subject };
    }

    private static Outcome Recycled(Outcome<RecycledFile> recycled) => recycled.Succeeded ? Outcome.Done : recycled.Failure;

    private static Failure? Completed(StoreContext store, long id, IReadOnlyList<PendingStep> steps, string action) =>
        store.Change(action, () => Complete(store, id, steps)).Failure;

    /// <summary>The last transaction: the previous picture's names, its original's record and hash, an edited one's render facts; the record cleared.</summary>
    private static void Complete(StoreContext store, long id, IReadOnlyList<PendingStep> steps)
    {
        var original = steps.First(step => step.Step is PendingStep.RestoreHeld or PendingStep.RenameInOriginals);
        var landing = PendingStep.Restoring(steps)!;
        var render = landing.Step == PendingStep.PlaceRender ? landing.Facts : null;
        var facts = original.Facts!;
        store.Execute(
            """
            UPDATE items SET format = $format, width = $width, height = $height, animated = $animated, frame_count = $frame_count,
                duration_ms = $duration_ms, original_sha256 = $sha256, finished_file = $finished, original_file = $original,
                original_in_originals = $in_originals, finished_format = $finished_format, finished_width = $finished_width,
                finished_height = $finished_height, finished_animated = $finished_animated, finished_frame_count = $finished_frame_count,
                finished_duration_ms = $finished_duration_ms, previous_layers = NULL, rendered_revision = layer_revision,
                adoption_undo = NULL, pending_steps = NULL
            WHERE id = $id
            """,
            ("$id", id),
            ("$format", facts.Format.ToString()),
            ("$width", facts.Width),
            ("$height", facts.Height),
            ("$animated", facts.Animated),
            ("$frame_count", facts.FrameCount),
            ("$duration_ms", facts.DurationMs),
            ("$sha256", AdoptionSteps.Sha256Of(original)),
            ("$finished", landing.To),
            ("$original", original.To),
            ("$in_originals", render is not null),
            ("$finished_format", render?.Format.ToString()),
            ("$finished_width", render?.Width),
            ("$finished_height", render?.Height),
            ("$finished_animated", render?.Animated),
            ("$finished_frame_count", render?.FrameCount),
            ("$finished_duration_ms", render?.DurationMs));
    }

    /// <summary>
    /// At start, an Undo that cannot finish before its recycle has run is undone (State: Every change): the previous
    /// picture renamed back into <c>.work\</c> under its step's name, the original back to its name in <c>originals\</c>,
    /// the adopted picture back into the root, each move read by the steps' own done rules; then the row rolled back to
    /// the adoption, the record left for the start's expiry; only then a fresh render discarded, since a place step whose
    /// file is gone reads as landed. A file that cannot go back leaves the row as it is, named, for the next start, with
    /// every file where it was, so a restored picture stays listed rather than unlisted (G28).
    /// </summary>
    private static IReadOnlyList<Failure> PutBack(StoreContext store, long id, string subject, IReadOnlyList<PendingStep> steps)
    {
        var folder = store.Folder;
        var landing = PendingStep.Restoring(steps)!;
        var aside = steps.First(step => step.Step == PendingStep.SetAsideAdopted);
        List<(string From, string To)> moves = [];
        if (landing.IsDone(folder))
        {
            moves.Add((landing.ToPath(folder), landing.FromPath(folder)));
        }

        if (steps.FirstOrDefault(step => step.Step == PendingStep.RenameInOriginals) is { } original
            && !string.Equals(original.From, original.To, StringComparison.Ordinal) && original.IsDone(folder))
        {
            moves.Add((original.ToPath(folder), original.FromPath(folder)));
        }

        if (File.Exists(aside.ToPath(folder)))
        {
            moves.Add((aside.ToPath(folder), aside.FromPath(folder)));
        }

        // Each file opened alone first, retried while held, and kept open, sharing only its renames, until the row is rolled
        // back: one still held leaves every file where it is (G35), and none can come to be held between its check and its
        // move, which would strand the files already moved.
        List<FileStream> holds = [];
        try
        {
            foreach (var (from, _) in moves)
            {
                var opened = store.RetryHeldFile(UndoAtStartAction, from, () => new FileStream(from, FileMode.Open, FileAccess.Read, FileShare.Delete));
                if (!opened.Succeeded)
                {
                    return [opened.Failure with { Subject = subject }];
                }

                holds.Add(opened.Value!);
            }

            for (var i = 0; i < moves.Count; i++)
            {
                if (Named(ItemSave.Move(store, UndoAtStartAction, moves[i].From, moves[i].To, replacing: false)) is { } stays)
                {
                    // Refused all the same: the files already moved go forward again, so the row is left as it was.
                    for (var back = i - 1; back >= 0; back--)
                    {
                        ItemSave.Move(store, UndoAtStartAction, moves[back].To, moves[back].From, replacing: false);
                    }

                    return [stays];
                }
            }

            var rolledBack = store.Change(UndoAtStartAction, () => store.Execute(
                "UPDATE items SET layers = '[]', previous_layers = NULL, layer_revision = rendered_revision, pending_steps = NULL WHERE id = $id",
                ("$id", id)));
            if (!rolledBack.Succeeded)
            {
                return [rolledBack.Failure];
            }
        }
        finally
        {
            holds.ForEach(hold => hold.Dispose());
        }

        if (landing.Step == PendingStep.PlaceRender)
        {
            Discard(store, landing.FromPath(folder));
        }

        return [];

        Failure? Named(Outcome outcome) => outcome.Failure is { } failure ? failure with { Subject = subject } : null;
    }

    // One attempt: a file held now is the clearing's at the next start, which names it.
    private static void Discard(StoreContext store, string path) => store.TryHeldFileOnce(Action, path, () => File.Delete(path));

    /// <summary>A committed Undo: the item as committed, its steps as the row records them, its name, and the store's commits once it committed.</summary>
    private sealed record Started(StashItem Item, IReadOnlyList<PendingStep> Steps, string Subject, long Commits);

    /// <summary>A row an Undo acts on, with its record as decoded and as stored.</summary>
    private sealed record Undoable(Row Row, AdoptionUndo Record, string Json);

    /// <summary>A row as the Undo reads it: the listed item, its files, name, mark, committed layers, their revision, and its record as stored.</summary>
    private sealed record Row(StashItem? Item, StoredFiles Files, string Name, bool Deleted, string Layers, long LayerRevision, string? Undo)
    {
        private const int After = StashItem.ColumnCount + StoredFiles.ColumnCount;

        public string Subject => ItemQueries.Named(Name, Files.FinishedFile);

        /// <summary>The row of identity <paramref name="id"/>, marked deleted or not, or null when it is gone.</summary>
        public static Outcome<Row?> Read(StoreContext store, string action, long id) =>
            store.Read(action, () =>
            {
                var tags = store.Query("SELECT tag FROM tags WHERE item_id = $id ORDER BY rowid", r => r.GetString(0), ("$id", id));
                return store.Query(
                    $"SELECT {StashItem.Columns}, {StoredFiles.Columns}, i.deleted, i.layers, i.adoption_undo FROM items i WHERE i.id = $id",
                    r => new Row(
                        r.GetBoolean(After) ? null : StashItem.ListedFromRow(r, store, tags),
                        StoredFiles.FromRow(r, StashItem.ColumnCount),
                        r.GetString(1),
                        r.GetBoolean(After),
                        r.GetString(After + 1),
                        StashItem.RecordedLayerRevision(r),
                        r.IsDBNull(After + 2) ? null : r.GetString(After + 2)),
                    ("$id", id)).SingleOrDefault();
            });
    }
}
