using ImageStash.Files;
using ImageStash.Media;
using Microsoft.Data.Sqlite;

namespace ImageStash.Store;

/// <summary>
/// The recorded sizes of a store an earlier build wrote, read again once as the store is brought up to the version from
/// which they are upright (F11, G31): a JPEG's or WebP's original was recorded at the size its codec decodes, before its
/// encoded origin was applied, so a portrait phone photo listed and was described landscape (G6, G19). Each such
/// original's header is read again, and so is the finished picture of an edited item whose facts a version-1 store never
/// recorded, all six, since the listing would otherwise fall back to the original's upright size for an unturned render;
/// and so are the facts a step left pending carries for a JPEG or WebP (an adoption's adopted file, an Undo's restored
/// original), which its finish writes over the row's at this open, after the migration.
/// The files are read before the migration's transaction, never inside it, through <see cref="PictureHeader"/> (no pixel
/// decoded, nothing hashed), and the migration writes what was read in its one transaction, so a kill before its commit
/// reads them again at the next open and one after finds the store upright. A file that cannot be read keeps its
/// recorded facts and its failure is named, and every other item opens (G35). A row the pass cannot parse, or whose
/// file names are not plain names in the folder, is left as it is for what judges the store next: an import's check of
/// the extracted store refuses it by name (G36), and nothing is read outside the folder.
/// </summary>
internal static class RecordedSizePass
{
    /// <summary>The store version from which every recorded size is upright: a store below it is read again.</summary>
    public const int UprightFrom = 3;

    /// <summary>What the pass read for one row: its original's upright size, its finished picture's facts, its pending steps with their files' upright facts.</summary>
    internal sealed record Correction(long Id, (int Width, int Height)? Original, MediaFacts? Finished, IReadOnlyList<PendingStep>? Steps);

    /// <summary>
    /// The corrections for the store at <paramref name="version"/>, which the caller has read and not yet migrated, with
    /// the failures of the files that could not be read; none for a store at or above <see cref="UprightFrom"/>, or one
    /// with no items yet. A store that cannot be read is a failure, as any open's is.
    /// </summary>
    public static Outcome<(IReadOnlyList<Correction> Corrections, IReadOnlyList<Failure> Named)> Read(StoreContext store, long version)
    {
        if (version is < 1 or >= UprightFrom)
        {
            return Outcome<(IReadOnlyList<Correction>, IReadOnlyList<Failure>)>.Of(([], []));
        }

        // A version-1 store has no finished facts: every one of them is unrecorded.
        var finishedUnrecorded = version >= 2 ? "i.finished_format IS NULL" : "1";
        var rows = HeldFileRetry.RunTransaction(StoreOpen.Action, store.Folder.StoreFile, store.RetryDuration, () => store.Query(
            $"SELECT {StoredFiles.Columns}, i.format, i.width, i.height, {finishedUnrecorded} FROM items i WHERE {StashItem.NotDeleted}",
            Parsed));
        if (!rows.Succeeded)
        {
            return rows.Failure;
        }

        var corrections = new List<Correction>();
        var named = new List<Failure>();
        foreach (var row in rows.Value!.OfType<Row>())
        {
            (int Width, int Height)? original = null;
            MediaFacts? finished = null;

            // The formats whose codec reports an encoded origin; a PNG's or GIF's recorded size is its pixels'.
            if (row.Format is ImageFormat.Jpeg or ImageFormat.WebP
                && Header(store, row.Files.OriginalPath(store.Folder), named) is { } upright
                && (upright.Width, upright.Height) != (row.Width, row.Height))
            {
                original = (upright.Width, upright.Height);
            }

            // An edited item's render is a file of its own beside its original in originals\. A Save, Revert or adoption
            // still pending writes the render's facts, or leaves them unrecorded, as it finishes or is undone at this open.
            if (row.FinishedUnrecorded && row.Files.OriginalInOriginals && row.Files.Steps.All(step => step.IsRename))
            {
                finished = Header(store, StoredFiles.PicturePath(store.Folder, row.Files.FinishedFile, row.Files.Steps), named);
            }

            var steps = UprightSteps(store, row.Files.Steps, named);
            if (original is not null || finished is not null || steps is not null)
            {
                corrections.Add(new Correction(row.Files.Id, original, finished, steps));
            }
        }

        return Outcome<(IReadOnlyList<Correction>, IReadOnlyList<Failure>)>.Of((corrections, named));
    }

    /// <summary>A row of the query in <see cref="Read"/>, or null when its values or names are not what a build writes.</summary>
    private static Row? Parsed(SqliteDataReader r)
    {
        try
        {
            var files = StoredFiles.FromRow(r, 0);
            var names = files.Steps.SelectMany(step => new[] { step.From, step.To }).Append(files.FinishedFile).Append(files.OriginalFile);
            return names.All(StashArchive.IsPlainFileName) && Enum.TryParse<ImageFormat>(r.GetString(StoredFiles.ColumnCount), out var format) && Enum.IsDefined(format)
                ? new Row(files, format, r.GetInt32(StoredFiles.ColumnCount + 1), r.GetInt32(StoredFiles.ColumnCount + 2), r.GetBoolean(StoredFiles.ColumnCount + 3))
                : null;
        }
        catch (Exception e) when (e is not SqliteException)
        {
            // A value of the wrong type, or steps that do not decode.
            return null;
        }
    }

    /// <summary>
    /// Writes <paramref name="corrections"/> inside the migration's transaction: each original's upright size, and each
    /// finished picture's facts where none are recorded yet.
    /// </summary>
    public static void Write(StoreContext store, IReadOnlyList<Correction> corrections)
    {
        foreach (var (id, original, finished, steps) in corrections)
        {
            if (steps is not null)
            {
                store.Execute("UPDATE items SET pending_steps = $steps WHERE id = $id", ("$id", id), ("$steps", PendingStep.Encode(steps)));
            }

            if (original is var (width, height))
            {
                store.Execute("UPDATE items SET width = $width, height = $height WHERE id = $id", ("$id", id), ("$width", width), ("$height", height));
            }

            if (finished is not null)
            {
                store.Execute(
                    """
                    UPDATE items SET finished_format = $format, finished_width = $width, finished_height = $height,
                        finished_animated = $animated, finished_frame_count = $frame_count, finished_duration_ms = $duration_ms
                    WHERE id = $id AND finished_format IS NULL
                    """,
                    ("$id", id),
                    ("$format", finished.Format.ToString()),
                    ("$width", finished.Width),
                    ("$height", finished.Height),
                    ("$animated", finished.Animated),
                    ("$frame_count", finished.FrameCount),
                    ("$duration_ms", finished.DurationMs));
            }
        }
    }

    private sealed record Row(StoredFiles Files, ImageFormat Format, int Width, int Height, bool FinishedUnrecorded);

    /// <summary>
    /// <paramref name="steps"/> with the facts each carries for a JPEG or WebP made those of its file upright, or null when
    /// none changes; a step's file is where a rename has it, else where the step has put it or will take it from.
    /// </summary>
    private static List<PendingStep>? UprightSteps(StoreContext store, IReadOnlyList<PendingStep> steps, List<Failure> named)
    {
        List<PendingStep>? turned = null;
        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            if (step.Facts is not { Format: ImageFormat.Jpeg or ImageFormat.WebP } facts)
            {
                continue;
            }

            var path = step.IsRename ? step.Located(store.Folder) : step.IsDone(store.Folder) ? step.ToPath(store.Folder) : step.FromPath(store.Folder);
            if (Header(store, path, named) is { } upright && (upright.Width, upright.Height) != (facts.Width, facts.Height))
            {
                turned ??= [.. steps];
                turned[i] = step with { Facts = facts with { Width = upright.Width, Height = upright.Height } };
            }
        }

        return turned;
    }

    /// <summary>
    /// The facts of the file at <paramref name="path"/>, or null when it is not there, which the pending step that moves
    /// it settles at this open (State: Every change), or cannot be read, whose failure is added to <paramref name="named"/>.
    /// </summary>
    private static MediaFacts? Header(StoreContext store, string path, List<Failure> named)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var read = PictureHeader.Read(path, Path.GetFileName(path), store.RetryDuration);
        if (!read.Succeeded)
        {
            named.Add(read.Failure);
        }

        return read.Value;
    }
}
