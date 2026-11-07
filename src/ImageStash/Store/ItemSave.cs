using ImageStash.Files;
using ImageStash.Layers;
using ImageStash.Media;
using Microsoft.Data.Sqlite;

namespace ImageStash.Store;

/// <summary>
/// A finished picture a caller rendered for a Save: a file Media wrote at a path <see cref="StashStore.AllocateWorkPath"/>
/// gave, in the item's finished format, with the facts the render answered for it and the SHA-256 of the bytes it decoded
/// as the original (<see cref="MediaRender"/>).
/// </summary>
public sealed record RenderedPicture(string WorkPath, MediaFacts Facts, byte[] OriginalSha256);

/// <summary>
/// Save and Revert to original (State: Save, Every change; F6, F7, G26, G28, G29). A Save's first transaction commits
/// the layers, keeping the list the picture on disk was rendered from, raises the layer revision and records the steps:
/// the original moved into <c>originals\</c> when it is still in the root, then the render placed in the root under the
/// finished picture's name, replacing an earlier render, which is not kept; the steps run in that order; the last
/// transaction sets the rendered revision and the render's facts and clears the steps. A Revert's steps move the
/// original back into the root, over the render when they share a name, and otherwise delete the render, never
/// recycled. A kill or a refused step leaves the row listed on the picture on disk, for a later change or the next start
/// to finish, rendering again, or for the start to undo (<see cref="ItemSaveRecovery"/>).
/// </summary>
internal static class ItemSave
{
    public const string Action = "Save";

    public const string RevertAction = "Revert to original";

    public const string ChangedWhileRendered = "it changed while its picture was being rendered";

    private const string RevertTakesNoRender = "a revert to the original takes no rendered picture";

    /// <summary>
    /// Saves <paramref name="layers"/> over the item, the caller having rendered them into <paramref name="render"/>,
    /// and returns the item as the stash lists it once the render is its finished picture; an empty list, with no
    /// render, is Revert to original on an edited item and changes nothing on an unedited one. Before the commit a
    /// refusal changes nothing and leaves the render the caller's; a pending change of the item is finished first, or the
    /// Save refused. From the commit on, a refused step fails after it, the steps kept for the next start.
    /// </summary>
    public static Outcome<StashItem> Save(StoreContext store, StashItem item, LayerList layers, RenderedPicture? render)
    {
        var action = layers.IsEmpty ? RevertAction : Action;
        var listed = ListedRow(store, item, action);
        if (!listed.Succeeded)
        {
            return listed.Failure;
        }

        var row = listed.Value!;
        if (Mismatch(store, row, layers, render, RevertTakesNoRender) is { } mismatch)
        {
            return new Failure(action, row.Subject, mismatch);
        }

        if (row.Files.Steps.Count > 0)
        {
            // Committing over a change still pending would strand its steps and its render: finish them first, or refuse.
            if (PendingStepRecovery.FinishBeforeChange(store, row.Files, row.Name, action) is { } unfinished)
            {
                return unfinished;
            }

            // Finishing it may have moved the files and changed the row, so nothing read before it is used after.
            listed = ListedRow(store, item, action);
            if (!listed.Succeeded)
            {
                return listed.Failure;
            }

            row = listed.Value!;
            // A pending adoption finished just now has made another file the original, so a render of the one before
            // it is refused here, never placed over the adopted item (Approach).
            if (Mismatch(store, row, layers, render, RevertTakesNoRender) is { } changed)
            {
                return new Failure(action, row.Subject, changed);
            }
        }

        if (!layers.IsEmpty)
        {
            return SaveLayers(store, item, row, layers, render!);
        }

        return row.Files.OriginalInOriginals ? Revert(store, item, row) : AsListed(store, item, action);
    }

    /// <summary>
    /// Runs one Save or Revert step on disk: a move into <c>originals\</c>; a render's place, replacing what the root
    /// holds under its name; a move back into the root, replacing the render when the Revert deletes none; a delete. A
    /// file another program holds, the one replaced included, is retried before the failure is named (G35, E3).
    /// </summary>
    public static Outcome RunStep(StoreContext store, PendingStep step, IReadOnlyList<PendingStep> steps, string action, string subject)
    {
        var from = step.FromPath(store.Folder);
        var to = step.ToPath(store.Folder);
        var done = step.Step switch
        {
            PendingStep.MoveToOriginals => Move(store, action, from, to, replacing: false),
            PendingStep.PlaceRender => Move(store, action, from, to, replacing: true),
            PendingStep.MoveToRoot => Move(store, action, from, to, replacing: steps.All(s => s.Step != PendingStep.DeleteInRoot)),
            PendingStep.DeleteInRoot => store.RetryHeldFile(action, from, () => File.Delete(from)),
            _ => new Failure(action, subject, $"its step \"{step.Step}\" is not one a save or a revert takes"),
        };
        return done.Succeeded ? done : done.Failure with { Subject = subject };
    }

    /// <summary>Renames <paramref name="from"/> to <paramref name="to"/>, never copied (R2), one replacing rename when asked (S1).</summary>
    public static Outcome Move(StoreContext store, string action, string from, string to, bool replacing) =>
        store.RetryHeldFile(action, from, () => File.Move(from, to, replacing), replaced: replacing ? to : null);

    /// <summary>A Save's last transaction: the rendered revision caught up, the render's facts recorded, the steps cleared.</summary>
    public static void CompleteSave(StoreContext store, long id, MediaFacts facts) =>
        store.Execute(
            """
            UPDATE items SET rendered_revision = layer_revision, previous_layers = NULL, pending_steps = NULL,
                finished_format = $format, finished_width = $width, finished_height = $height, finished_animated = $animated,
                finished_frame_count = $frame_count, finished_duration_ms = $duration_ms
            WHERE id = $id
            """,
            ("$id", id),
            ("$format", facts.Format.ToString()),
            ("$width", facts.Width),
            ("$height", facts.Height),
            ("$animated", facts.Animated),
            ("$frame_count", facts.FrameCount),
            ("$duration_ms", facts.DurationMs));

    /// <summary>A Revert's last transaction: the rendered revision caught up, no render's facts left, the steps cleared.</summary>
    public static void CompleteRevert(StoreContext store, long id) =>
        store.Execute(
            """
            UPDATE items SET rendered_revision = layer_revision, previous_layers = NULL, pending_steps = NULL,
                finished_format = NULL, finished_width = NULL, finished_height = NULL, finished_animated = NULL,
                finished_frame_count = NULL, finished_duration_ms = NULL
            WHERE id = $id
            """,
            ("$id", id));

    /// <summary>
    /// A Save's first transaction, with its steps: a second Save keeps both names, its render replacing the earlier one;
    /// a first Save names both files from one stem (State: File names), its original moving under it into <c>originals\</c>.
    /// </summary>
    private static Outcome<StashItem> SaveLayers(StoreContext store, StashItem item, SaveRow row, LayerList layers, RenderedPicture render)
    {
        var committed = store.Change(Action, () =>
        {
            var files = row.Files;
            var finishedFile = files.FinishedFile;
            var originalFile = files.OriginalFile;
            List<PendingStep> steps = [];
            if (!files.OriginalInOriginals)
            {
                var extension = FinishedFormat.Extension(FinishedFormat.Of(row.OriginalFacts));
                var originalExtension = Path.GetExtension(files.OriginalFile);
                var stem = ItemFileNames.ChooseStem(store, files, row.Name,
                    [(extension, ItemFileNames.TakenInRoot(store, files)), (originalExtension, ItemFileNames.TakenInOriginals(store, files))]);
                finishedFile = stem + extension;
                originalFile = stem + originalExtension;
                steps.Add(new PendingStep(PendingStep.MoveToOriginals, files.OriginalFile, originalFile));
            }

            steps.Add(new PendingStep(PendingStep.PlaceRender, Path.GetFileName(render.WorkPath), finishedFile, Facts: render.Facts));
            // The rendered revision and the render's facts wait for the last transaction: until the render is placed, the
            // picture on disk is the one the previous list made (R9).
            store.Execute(
                """
                UPDATE items SET previous_layers = layers, layers = $layers, layer_revision = layer_revision + 1, original_in_originals = 1,
                    finished_file = $finished, original_file = $original, pending_steps = $steps
                WHERE id = $id
                """,
                ("$id", files.Id),
                ("$layers", layers.ToJson()),
                ("$finished", finishedFile),
                ("$original", originalFile),
                ("$steps", PendingStep.Encode(steps)));
            return steps;
        });
        if (!committed.Succeeded)
        {
            return committed.Failure;
        }

        var steps = committed.Value!;
        var finished = steps[^1].To;
        var asCommitted = item with { Name = row.Name, FileName = finished, FilePath = store.Folder.InRoot(finished), Facts = render.Facts, RenderedRevision = row.LayerRevision + 1, Edited = true };
        return RunThenComplete(store, asCommitted, steps, Action, row.Subject, () => CompleteSave(store, row.Files.Id, render.Facts));
    }

    /// <summary>
    /// Revert's first transaction, with its steps: the original's name in the root chosen by the one stem rule, its own
    /// render's name being free to it, so a render of its name is replaced and one of another name deleted (G29).
    /// </summary>
    private static Outcome<StashItem> Revert(StoreContext store, StashItem item, SaveRow row)
    {
        var committed = store.Change(RevertAction, () =>
        {
            var files = row.Files;
            var extension = Path.GetExtension(files.OriginalFile);
            var rootFile = ItemFileNames.ChooseStem(store, files, row.Name, [(extension, ItemFileNames.TakenInRoot(store, files))]) + extension;
            List<PendingStep> steps = [new(PendingStep.MoveToRoot, files.OriginalFile, rootFile)];
            if (!string.Equals(rootFile, files.FinishedFile, StringComparison.OrdinalIgnoreCase))
            {
                steps.Add(new PendingStep(PendingStep.DeleteInRoot, files.FinishedFile, files.FinishedFile));
            }

            store.Execute(
                """
                UPDATE items SET previous_layers = layers, layers = '[]', layer_revision = layer_revision + 1, original_in_originals = 0,
                    finished_file = $file, original_file = $file, pending_steps = $steps
                WHERE id = $id
                """,
                ("$id", files.Id),
                ("$file", rootFile),
                ("$steps", PendingStep.Encode(steps)));
            return steps;
        });
        if (!committed.Succeeded)
        {
            return committed.Failure;
        }

        var steps = committed.Value!;
        var asCommitted = item with { Name = row.Name, FileName = steps[0].To, FilePath = store.Folder.InRoot(steps[0].To), Facts = row.OriginalFacts, RenderedRevision = row.LayerRevision + 1, Edited = false };
        return RunThenComplete(store, asCommitted, steps, RevertAction, row.Subject, () => CompleteRevert(store, row.Files.Id));
    }

    // The item it answers keeps the stamp it was handed, older than the commit, as a Rename's does.
    private static Outcome<StashItem> RunThenComplete(StoreContext store, StashItem asCommitted, IReadOnlyList<PendingStep> steps, string action, string subject, Action complete)
    {
        foreach (var step in steps)
        {
            var done = RunStep(store, step, steps, action, subject);
            if (!done.Succeeded)
            {
                return ItemQueries.AfterCommit(store, asCommitted, done.Failure);
            }
        }

        return ItemQueries.AfterCommit(store, asCommitted, store.Change(action, complete).Failure);
    }

    /// <summary>
    /// Why the render does not go with the list, or null: a list needs the picture rendered from it, an empty list none,
    /// refused as <paramref name="emptyListTakesNone"/>; and a render decoded from bytes other than <paramref name="row"/>'s
    /// original, whatever renames, Saves or Reverts put another file at the path the render opened. A render of the
    /// original's own bytes goes, whatever committed or moved meanwhile.
    /// </summary>
    public static string? Mismatch(StoreContext store, SaveRow row, LayerList layers, RenderedPicture? render, string emptyListTakesNone) => (layers.IsEmpty, render) switch
    {
        (true, not null) => emptyListTakesNone,
        (false, null) => "its layers came without the picture rendered from them",
        (false, { } given) when !store.Folder.IsWorkFile(given.WorkPath) => "its rendered picture is not in the stash's work folder",
        (false, { } given) when !File.Exists(given.WorkPath) => "its rendered picture is gone",
        // A finished picture is always a render of the original (Approach): judged by the hash of what the renderer read
        // through its own handle, since neither the layer revision nor the folder tells which file a path held then.
        (false, { } given) when !given.OriginalSha256.AsSpan().SequenceEqual(row.OriginalSha256) => ChangedWhileRendered,
        _ => null,
    };

    /// <summary>The item's row when the stash lists it; otherwise refused as <paramref name="action"/>, by name, as no longer in the stash.</summary>
    public static Outcome<SaveRow> ListedRow(StoreContext store, StashItem item, string action)
    {
        var read = SaveRow.Read(store, action, item.Id);
        if (!read.Succeeded)
        {
            return read.Failure;
        }

        if (read.Value is not { } row)
        {
            // A row that is gone holds no name any more, so the item is named as the caller was shown it (G35).
            return new Failure(action, ItemQueries.Named(item.Name, item.FileName), ItemFactChange.NotInStash);
        }

        return row.Deleted || !StashItem.IsListed(store.Folder, row.Files.Steps)
            ? new Failure(action, row.Subject, ItemFactChange.NotInStash)
            : Outcome<SaveRow>.Of(row);
    }

    private static Outcome<StashItem> AsListed(StoreContext store, StashItem item, string action)
    {
        var read = ItemQueries.Item(store, item.Id);
        if (!read.Succeeded)
        {
            return read.Failure;
        }

        return read.Value is { } listed ? Outcome<StashItem>.Of(listed) : new Failure(action, ItemQueries.Named(item.Name, item.FileName), ItemFactChange.NotInStash);
    }
}

/// <summary>
/// A row as a Save, a Revert or their recovery reads it: where its files are, its name, whether it is marked deleted, the
/// committed list and the one the picture on disk was rendered from, both revisions, the original's record, which F3's
/// format rule reads (<see cref="FinishedFormat"/>), and the original's content hash, which a render must answer; once an
/// Open with… adoption's file, or its Undo's previous picture, has landed, the record and hash its step carries until the
/// last transaction writes them.
/// </summary>
internal sealed record SaveRow(StoredFiles Files, string Name, bool Deleted, string Layers, string? PreviousLayers, long LayerRevision, long RenderedRevision, MediaFacts OriginalFacts, byte[] OriginalSha256)
{
    /// <summary>The name a failure gives the item: its name, or its file's when it has none.</summary>
    public string Subject => ItemQueries.Named(Name, Files.FinishedFile);

    /// <summary>The row of identity <paramref name="id"/>, marked deleted or not, or null when it is gone.</summary>
    public static Outcome<SaveRow?> Read(StoreContext store, string action, long id) =>
        store.Read(action, () => store.Query(
            $"SELECT {StashItem.Columns}, {StoredFiles.Columns}, i.deleted, i.layers, i.previous_layers, i.original_sha256 FROM items i WHERE i.id = $id",
            r => FromRow(r, store.Folder),
            ("$id", id)).SingleOrDefault());

    private static SaveRow FromRow(SqliteDataReader row, StashFolder folder)
    {
        const int After = StashItem.ColumnCount + StoredFiles.ColumnCount;
        var files = StoredFiles.FromRow(row, StashItem.ColumnCount);
        var adopted = AdoptionSteps.OriginalOnDisk(folder, files.Steps);
        return new SaveRow(
            files,
            row.GetString(1),
            row.GetBoolean(After),
            row.GetString(After + 1),
            row.IsDBNull(After + 2) ? null : row.GetString(After + 2),
            StashItem.RecordedLayerRevision(row),
            StashItem.RecordedRenderedRevision(row),
            adopted?.Facts ?? StashItem.OriginalFacts(row),
            adopted is null ? row.GetFieldValue<byte[]>(After + 3) : AdoptionSteps.Sha256Of(adopted));
    }
}
