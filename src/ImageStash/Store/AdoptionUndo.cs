using System.Text.Json;
using System.Text.Json.Serialization;
using ImageStash.Media;

namespace ImageStash.Store;

/// <summary>
/// An Open with… adoption's undo record (<c>adoption_undo</c>, JSON, one per row; State: Open with…): the revision the
/// adoption raised the layers to, which names it; the layer list the row held before; the previous original's file
/// name, whether it lies in <c>originals\</c>, and for an unedited item the name of that file held in <c>.work\</c>;
/// the previous original's record and its SHA-256 as upper-case hex; and the previous finished picture's file name.
/// While it stands the previous original waits, held or in <c>originals\</c>, for the adoption's Undo
/// (<see cref="ItemAdoptionUndo"/>); its expiry, when the notification has gone or at the next start, sends that file
/// to the Recycle Bin and clears the record (G27, G29).
/// </summary>
internal sealed record AdoptionUndo(long Revision, string Layers, string OriginalFile, bool InOriginals, string? HeldCopy, MediaFacts Facts, string Sha256, string FinishedFile)
{
    public const string ExpireAction = "Recycle the picture an Open with… edit replaced";

    public const string Unreadable = "its record of an Open with… edit cannot be read";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public string Encode() => JsonSerializer.Serialize(this, Json);

    /// <summary>The record <paramref name="json"/> holds, or null when it cannot be read, which never throws (G35).</summary>
    public static AdoptionUndo? Decode(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<AdoptionUndo>(json, Json) is { Layers: not null, OriginalFile: not null, Facts: not null, Sha256: not null, FinishedFile: not null } record
                && (record.InOriginals || record.HeldCopy is not null)
                ? record
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Where the previous original waits: its copy held in <c>.work\</c>, or its file in <c>originals\</c>.</summary>
    public string PreviousPath(StashFolder folder) => HeldCopy is { } held ? folder.InWork(held) : folder.InOriginals(OriginalFile);

    /// <summary>
    /// Expires the adoption <paramref name="revision"/> names once its notification has gone (G27): done when no record
    /// names it or the row is gone; otherwise a pending change of the row is finished first, or the expiry refused by
    /// name, so a record is never cleared under a change the start may still need to undo, and the record read again,
    /// since an Undo finished so has put the previous original back as the item's; then as <see cref="ExpireRecord"/>.
    /// A marked row's record expires as a listed one's.
    /// </summary>
    public static Outcome Expire(StoreContext store, long id, long revision)
    {
        var finished = false;
        while (true)
        {
            var read = store.Read(ExpireAction, () => store.Query(
                $"SELECT {StoredFiles.Columns}, i.name, i.adoption_undo FROM items i WHERE i.id = $id",
                r => (Files: StoredFiles.FromRow(r, 0), Name: r.GetString(StoredFiles.ColumnCount), Undo: r.IsDBNull(StoredFiles.ColumnCount + 1) ? null : r.GetString(StoredFiles.ColumnCount + 1)),
                ("$id", id)).SingleOrDefault());
            if (!read.Succeeded)
            {
                return read.Failure;
            }

            if (read.Value is not { Undo: { } json } row)
            {
                return Outcome.Done;
            }

            var subject = ItemQueries.Named(row.Name, row.Files.FinishedFile);
            if (Decode(json) is not { } record)
            {
                return new Failure(ExpireAction, subject, Unreadable);
            }

            if (record.Revision != revision)
            {
                return Outcome.Done;
            }

            if (finished || row.Files.Steps.Count == 0)
            {
                return Recycle(store, id, record, subject, ExpireAction);
            }

            if (PendingStepRecovery.FinishBeforeChange(store, row.Files, row.Name, ExpireAction) is { } unfinished)
            {
                return unfinished;
            }

            finished = true;
        }
    }

    /// <summary>
    /// Expires the record <paramref name="json"/> a row holds, whatever adoption it names (a second adoption, a row
    /// removed for good): the previous original recycled, never deleted (G29), then the record cleared in one
    /// transaction; a kill between leaves the record for the next start, which finds the file gone and clears it. A
    /// refused recycle, or a record that cannot be read, is named as <paramref name="action"/> on
    /// <paramref name="subject"/> and leaves the record as it is.
    /// </summary>
    public static Outcome ExpireRecord(StoreContext store, long id, string json, string subject, string action) =>
        Decode(json) is { } record ? Recycle(store, id, record, subject, action) : new Failure(action, subject, Unreadable);

    /// <summary>
    /// At start, after every pending step is resolved and before the sessions are classified: every record on a row
    /// with no pending step expired (State: Open with…, "or the next start"); a row whose adoption the start could
    /// neither finish nor undo keeps its record, which its next start still needs. Every failure is named.
    /// </summary>
    public static IReadOnlyList<Failure> ExpireAtStart(StoreContext store)
    {
        var rows = store.Read(StoreOpen.Action, () => store.Query(
            "SELECT id, name, finished_file, adoption_undo FROM items WHERE adoption_undo IS NOT NULL AND pending_steps IS NULL ORDER BY id",
            r => (Id: r.GetInt64(0), Subject: ItemQueries.Named(r.GetString(1), r.GetString(2)), Json: r.GetString(3))));
        if (!rows.Succeeded)
        {
            return [rows.Failure];
        }

        return [.. rows.Value!.Select(row => ExpireRecord(store, row.Id, row.Json, row.Subject, ExpireAction).Failure).OfType<Failure>()];
    }

    /// <summary>
    /// The previous originals records keep in <c>originals\</c>, the row's own included, which no file may be moved over
    /// while the record stands (G29); a record that cannot be read names none here and is named where it is read.
    /// </summary>
    public static IEnumerable<string> KeptInOriginals(StoreContext store) =>
        store.Query("SELECT adoption_undo FROM items WHERE adoption_undo IS NOT NULL", r => Decode(r.GetString(0)))
            .Where(record => record is { InOriginals: true })
            .Select(record => record!.OriginalFile);

    private static Outcome Recycle(StoreContext store, long id, AdoptionUndo record, string subject, string action)
    {
        var previous = record.PreviousPath(store.Folder);
        if (File.Exists(previous))
        {
            var recycled = store.Recycle([previous])[0];
            if (!recycled.Succeeded)
            {
                return recycled.Failure! with { Action = action, Subject = subject };
            }
        }

        return store.Change(action, () => store.Execute("UPDATE items SET adoption_undo = NULL WHERE id = $id", ("$id", id)));
    }
}
