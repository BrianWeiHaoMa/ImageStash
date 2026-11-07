using System.Text.Json;
using ImageStash.Files;
using ImageStash.Media;

namespace ImageStash.Store;

/// <summary>What an item's Open with… copy was found to be when it was read (State: Open with…).</summary>
public enum OpenWithCopy
{
    /// <summary>Its bytes are those last recorded, and no rewrite of it awaits adoption.</summary>
    Unchanged,

    /// <summary>Another program still holds it open, or no file is at its path while one may be replacing it: ask again later.</summary>
    StillHeld,

    /// <summary>Another program rewrote it with a picture that decodes, which awaits adoption (G27).</summary>
    AwaitingAdoption,
}

/// <summary>An item's Open with… session, the item listed or marked deleted, and where its copy is.</summary>
public sealed record OpenWithSession(long Id, string CopyPath);

/// <summary>An adoption of a rewritten copy the start made, by the item and the revision the adoption names (G27).</summary>
public sealed record AdoptedAtStart(long Id, long Revision);

/// <summary>Every Open with… session, and the adoptions this start made, which only the first answer after the open carries.</summary>
public sealed record OpenWithSessions(IReadOnlyList<OpenWithSession> Sessions, IReadOnlyList<AdoptedAtStart> Adoptions);

/// <summary>
/// Open with…'s copies (State: Open with…; G27), each in a session <see cref="OpenWithBegin"/> began on its item and
/// recorded on its row with the copy's SHA-256. Classify reads the copy only once it opens without sharing, which fails
/// while another program still holds it: the bytes are taken
/// through that handle into <c>.work\</c> and Media reads them there, so a file replaced half way is never read as two
/// (R2, R3); a difference from the recorded hash is a rewrite, whose hash becomes the recorded one. The start classifies
/// every session before anything reads the stash, and a session ends with its item's row. A live take is one attempt
/// per call on the store's thread with the waits between attempts spent off it, and Media reads on a pooled thread, so
/// the summon's listing and the handoff's confirmation run between them (G1; Approach).
/// </summary>
internal static class OpenWithCopies
{
    public const string ClassifyAction = "Take back an Open with… edit";

    public const string NoRoot = "this stash keeps no Open with… copies";

    private const string EndAction = "Discard the Open with… copy";

    private const string ListAction = "List the Open with… sessions";

    public const string NoSession = "it has no Open with… session";

    /// <summary>
    /// What the item's copy is now, never adopting it: refused by name when the item is not listed or has no session;
    /// still held while another program holds it after the retry or no file is at its path; otherwise its bytes, read,
    /// against the recorded hash: unchanged, awaiting adoption while a rewrite still does, or a rewrite, recorded, that
    /// awaits adoption when it decodes and otherwise is refused as unreadable, naming the copy (G27, G35).
    /// </summary>
    public static async Task<Outcome<OpenWithCopy>> Classify(StoreThread thread, StoreContext store, long id)
    {
        var classified = await ClassifyLive(thread, store, id, id.ToString(), whileMissing: false);
        return classified.Succeeded ? Outcome<OpenWithCopy>.Of(classified.Value!.Copy) : classified.Failure;
    }

    /// <summary>
    /// The copy of the item's session taken into <c>.work\</c>, one attempt per call on the store's thread, again after
    /// the held-file spacing, spent off that thread, while another program holds it (and, asked
    /// <paramref name="whileMissing"/>, while no file is at its path, a replacement perhaps half way), for the store's
    /// held-file duration (State: Open with…, G1). The taken file is the caller's to read and discard.
    /// </summary>
    public static Task<Outcome<Taken>> TakeLive(StoreThread thread, StoreContext store, long id, string gone, string action, bool whileMissing) =>
        HeldFileRetry.RunSpaced(store.RetryDuration, () => thread.Run(() => TakeOnce(store, id, gone, action)), taken => NotTaken(taken, whileMissing));

    /// <summary>
    /// Every session, listed or marked, with its copy's path, and the adoptions this start made, which the first call
    /// after the open takes; none without a root.
    /// </summary>
    public static Outcome<OpenWithSessions> Sessions(StoreContext store)
    {
        if (store.OpenWithRoot is not { } root)
        {
            return Outcome<OpenWithSessions>.Of(new OpenWithSessions([], []));
        }

        var read = store.Read(ListAction, () => store.Query(
            "SELECT id, open_with_session FROM items WHERE open_with_session IS NOT NULL ORDER BY id",
            r => new OpenWithSession(r.GetInt64(0), OpenWithRecord.Decode(r.GetString(1)).CopyPath(root))));
        if (!read.Succeeded)
        {
            return read.Failure;
        }

        List<AdoptedAtStart> adoptions = [.. store.AdoptedAtStart];
        store.AdoptedAtStart.Clear();
        return Outcome<OpenWithSessions>.Of(new OpenWithSessions(read.Value!, adoptions));
    }

    /// <summary>
    /// At start, after every pending step is resolved and every live undo record expired, and before <c>.work\</c> is
    /// cleared (State: Every change, Open with…): the root and its marker made where missing (G30); every session of a
    /// row not marked deleted classified as <see cref="Classify"/> does, a held copy retried in place since nothing reads
    /// the stash yet and then left for later, a missing one ending its session, named (R8), and a rewrite awaiting
    /// adoption on a row with no pending step adopted at once (<see cref="ItemAdoption.AdoptAtStart"/>); then every
    /// folder under the root no session names deleted. A marked row is not in the stash, and its session ends with its
    /// row. Nothing without a root; a root that cannot be made is named and every session left as it was.
    /// </summary>
    public static IReadOnlyList<Failure> AtStart(StoreContext store)
    {
        if (store.OpenWithRoot is not { } root)
        {
            return [];
        }

        var prepared = OpenWithRoot.Prepare(root);
        if (!prepared.Succeeded)
        {
            return [prepared.Failure];
        }

        var sessions = AllRecorded(store);
        if (!sessions.Succeeded)
        {
            return [sessions.Failure];
        }

        var named = new List<Failure>();
        foreach (var (id, session, _) in sessions.Value!.Where(row => !row.Deleted))
        {
            named.AddRange(ClassifyAtStart(store, id, session));
        }

        var remaining = AllRecorded(store);
        if (!remaining.Succeeded)
        {
            return [.. named, remaining.Failure];
        }

        named.AddRange(OpenWithRoot.Sweep(store, root, [.. remaining.Value!.Select(row => row.Session.Folder)]));
        return named;
    }

    /// <summary>
    /// Ends the session of a row being removed for good (State: Open with…, "a session ends when its item is deleted"):
    /// its copy deleted, then its folder when nothing else is in it. A copy that cannot be deleted fails naming
    /// <paramref name="subject"/>, and the caller keeps the row for the next start. Without a root the copy cannot be
    /// found; the next open with one clears its folder, which no row then names.
    /// </summary>
    public static Outcome End(StoreContext store, string? session, string subject)
    {
        if (session is null || store.OpenWithRoot is not { } root)
        {
            return Outcome.Done;
        }

        var record = OpenWithRecord.Decode(session);
        var deleted = OpenWithRoot.DeleteCopy(store, root, record.Folder, record.File, EndAction);
        return deleted.Succeeded ? Outcome.Done : deleted.Failure with { Subject = subject };
    }

    /// <summary>
    /// As <see cref="Classify"/>, answering the session as the classification left it, and, asked
    /// <paramref name="whileMissing"/>, waiting out a missing copy as a held one; an item gone from the store is named
    /// <paramref name="gone"/>.
    /// </summary>
    public static async Task<Outcome<Classified>> ClassifyLive(StoreThread thread, StoreContext store, long id, string gone, bool whileMissing)
    {
        if (store.OpenWithRoot is null)
        {
            return new Failure(ClassifyAction, gone, NoRoot);
        }

        while (true)
        {
            var taken = await TakeLive(thread, store, id, gone, ClassifyAction, whileMissing);
            if (!taken.Succeeded)
            {
                return taken.Failure;
            }

            if (taken.Value!.WorkPath is not { } work)
            {
                return Outcome<Classified>.Of(new Classified(OpenWithCopy.StillHeld, taken.Value.Session, Missing: !taken.Value.Held));
            }

            // Read and discarded off the store's thread, so neither a long decode nor a scanner holding the taken file
            // keeps the listing waiting (G1); one that cannot be deleted goes with the next start's clearing.
            var read = await Task.Run(() =>
            {
                var picture = store.Reader(work, taken.Value.Session.File, store.RetryDuration);
                WorkFolder.Discard(store, work);
                return picture;
            });
            var recorded = await thread.Run(() => RecordClassified(store, id, gone, taken.Value, read));
            if (!recorded.Succeeded)
            {
                return recorded.Failure;
            }

            if (recorded.Value is { } classified)
            {
                return Outcome<Classified>.Of(classified);
            }

            // Another call recorded the session after this take: the take is stale, so the copy is taken again.
        }
    }

    private static Outcome<Taken> TakeOnce(StoreContext store, long id, string gone, string action)
    {
        var row = ReadRow(store, id, gone, action);
        if (!row.Succeeded)
        {
            return row.Failure;
        }

        if (row.Value!.Session is not { } session)
        {
            return new Failure(action, row.Value.Named, NoSession);
        }

        return Take(store, session, action);
    }

    /// <summary>
    /// Opens the copy without sharing, which any other handle on it refuses, in one attempt (its callers space their
    /// attempts), and writes its bytes through that handle into a fresh <c>.work\</c> file, closing the
    /// handle at once so the outside program can write it again. Not taken (no work path) while it is still held, or
    /// when no file is at its path; another failure names the copy's file.
    /// </summary>
    private static Outcome<Taken> Take(StoreContext store, OpenWithRecord session, string action)
    {
        var copy = session.CopyPath(store.OpenWithRoot!);
        var allocated = WorkFolder.Allocate(store.Folder, Path.GetExtension(copy));
        if (!allocated.Succeeded)
        {
            return new Failure(action, session.File, allocated.Failure.Reason);
        }

        var work = allocated.Value!;
        var taken = store.TryHeldFileOnce(action, copy, () =>
        {
            using var source = new FileStream(copy, FileMode.Open, FileAccess.Read, FileShare.None);
            using var target = new FileStream(work, FileMode.Create, FileAccess.Write, FileShare.None);
            source.CopyTo(target);
        });
        if (taken.Succeeded)
        {
            return Outcome<Taken>.Of(new Taken(session, work, Held: false));
        }

        // A take that failed part way leaves a partial file, deleted in one attempt here or at the next start's clearing.
        store.TryHeldFileOnce(action, work, () => File.Delete(work));
        return taken.Failure.Reason switch
        {
            HeldFileRetry.HeldReason => Outcome<Taken>.Of(new Taken(session, null, Held: true)),
            HeldFileRetry.MissingReason or HeldFileRetry.FolderMissingReason => Outcome<Taken>.Of(new Taken(session, null, Held: false)),
            _ => taken.Failure,
        };
    }

    /// <summary>Whether a take is to be tried again: the copy still held, or, asked <paramref name="whileMissing"/>, not at its path.</summary>
    private static bool NotTaken(Outcome<Taken> taken, bool whileMissing) => taken.Value is { WorkPath: null } notTaken && (notTaken.Held || whileMissing);

    private static Outcome<Classified?> RecordClassified(StoreContext store, long id, string gone, Taken taken, Outcome<ReadPicture> read)
    {
        if (!read.Succeeded)
        {
            return read.Failure;
        }

        var row = ReadRow(store, id, gone, ClassifyAction);
        if (!row.Succeeded)
        {
            return row.Failure;
        }

        if (row.Value!.Session != taken.Session)
        {
            return Outcome<Classified?>.Of(null);
        }

        return Recorded(store, id, taken.Session, read.Value!);
    }

    /// <summary>
    /// What the copy's bytes, read, make of the session: unchanged, or awaiting adoption while the mark stands, for the
    /// recorded hash; otherwise a rewrite, whose hash is recorded in one transaction, the mark set when it decodes and
    /// cleared when it does not, which is refused naming the copy as unreadable (G27, G35).
    /// </summary>
    private static Outcome<Classified?> Recorded(StoreContext store, long id, OpenWithRecord session, ReadPicture read)
    {
        var sha256 = Convert.ToHexString(read.Sha256);
        if (sha256 == session.Sha256)
        {
            return Outcome<Classified?>.Of(new Classified(session.Awaiting ? OpenWithCopy.AwaitingAdoption : OpenWithCopy.Unchanged, session, Missing: false));
        }

        if (read.Facts is null)
        {
            return RecordUnreadable(store, id, session, sha256);
        }

        var rewritten = session with { Sha256 = sha256, Awaiting = true };
        var changed = store.Change(ClassifyAction, () =>
            store.Execute("UPDATE items SET open_with_session = $session WHERE id = $id", ("$session", rewritten.Encode()), ("$id", id)));
        return changed.Succeeded ? Outcome<Classified?>.Of(new Classified(OpenWithCopy.AwaitingAdoption, rewritten, Missing: false)) : changed.Failure;
    }

    /// <summary>
    /// A rewrite whose bytes, of hash <paramref name="sha256"/>, do not decode is not adopted (G27): the hash becomes the
    /// recorded one and the mark is cleared, in one transaction, and it is refused naming the copy as unreadable (G35).
    /// </summary>
    public static Failure RecordUnreadable(StoreContext store, long id, OpenWithRecord session, string sha256)
    {
        var unmarked = session with { Sha256 = sha256, Awaiting = false };
        var changed = store.Change(ClassifyAction, () =>
            store.Execute("UPDATE items SET open_with_session = $session WHERE id = $id", ("$session", unmarked.Encode()), ("$id", id)));
        return changed.Succeeded ? new Failure(ClassifyAction, session.File, MediaDecoder.UnreadableReason) : changed.Failure!;
    }

    private static IReadOnlyList<Failure> ClassifyAtStart(StoreContext store, long id, OpenWithRecord session)
    {
        // Retried in place, since nothing reads the stash yet; a missing copy too, so a replacement half way at the moment
        // of the start lands before its session is ended (State: classified "the same way").
        var taken = HeldFileRetry.RunSpaced(store.RetryDuration, () => Task.FromResult(Take(store, session, ClassifyAction)), t => NotTaken(t, whileMissing: true))
            .GetAwaiter().GetResult();
        if (!taken.Succeeded)
        {
            return [taken.Failure];
        }

        if (taken.Value!.Held)
        {
            return [];
        }

        if (taken.Value.WorkPath is not { } work)
        {
            // Nothing is left to adopt: the session ends, and the clearing after this deletes its folder (R8).
            var ended = store.Change(StoreOpen.Action, () => store.Execute("UPDATE items SET open_with_session = NULL WHERE id = $id", ("$id", id)));
            return [ended.Succeeded ? new Failure(ClassifyAction, session.File, HeldFileRetry.MissingReason) : ended.Failure];
        }

        var read = store.ReadTakenCopy(work, session.File);
        var classified = read.Succeeded ? Recorded(store, id, session, read.Value!) : read.Failure;
        // A row whose change the start could neither finish nor undo keeps its rewrite awaiting for a later adoption.
        var settled = classified.Value is { Copy: OpenWithCopy.AwaitingAdoption }
            ? store.Read(StoreOpen.Action, () => store.Query("SELECT 1 FROM items WHERE id = $id AND pending_steps IS NULL", _ => true, ("$id", id)).Count == 1)
            : Outcome<bool>.Of(false);
        if (settled.Value)
        {
            // The taken file is the one adopted, as a live adoption's take is.
            return ItemAdoption.AdoptAtStart(store, id, taken.Value with { Session = classified.Value!.Session }, work, read.Value!);
        }

        WorkFolder.Discard(store, work);
        return [.. new[] { classified.Failure, settled.Failure }.OfType<Failure>()];
    }

    /// <summary>
    /// The row of identity <paramref name="id"/> as the stash lists it, with its session; refused by name as no longer in
    /// the stash when it is gone (named <paramref name="gone"/>), marked deleted, or not listed.
    /// </summary>
    public static Outcome<SessionRow> ReadRow(StoreContext store, long id, string gone, string action)
    {
        var read = store.Read(action, () => store.Query(
            $"SELECT {StashItem.Columns}, i.deleted, i.open_with_session FROM items i WHERE i.id = $id",
            r => (Item: r.GetBoolean(StashItem.ColumnCount) ? null : StashItem.ListedFromRow(r, store, []),
                Named: ItemQueries.Named(r.GetString(1), r.GetString(4)),
                Session: r.IsDBNull(StashItem.ColumnCount + 1) ? null : OpenWithRecord.Decode(r.GetString(StashItem.ColumnCount + 1))),
            ("$id", id)));
        if (!read.Succeeded)
        {
            return read.Failure;
        }

        return read.Value! switch
        {
            [{ Item: { } item } row] => Outcome<SessionRow>.Of(new SessionRow(item, row.Named, row.Session)),
            [var row] => new Failure(action, row.Named, ItemFactChange.NotInStash),
            _ => new Failure(action, gone, ItemFactChange.NotInStash),
        };
    }

    /// <summary>Every row's session, marked rows' too, with whether the row is marked deleted.</summary>
    private static Outcome<List<(long Id, OpenWithRecord Session, bool Deleted)>> AllRecorded(StoreContext store) =>
        store.Read(StoreOpen.Action, () => store.Query(
            "SELECT id, open_with_session, deleted FROM items WHERE open_with_session IS NOT NULL ORDER BY id",
            r => (r.GetInt64(0), OpenWithRecord.Decode(r.GetString(1)), r.GetBoolean(2))));

    /// <summary>A copy taken into <c>.work\</c> at <see cref="WorkPath"/>; or not taken: still <see cref="Held"/>, or missing.</summary>
    internal sealed record Taken(OpenWithRecord Session, string? WorkPath, bool Held);

    /// <summary>What a classification found, with the session as it left it; when still held, whether the copy was in fact missing.</summary>
    internal sealed record Classified(OpenWithCopy Copy, OpenWithRecord Session, bool Missing);

    /// <summary>A listed item, the name a failure gives it, and its session, if any.</summary>
    internal sealed record SessionRow(StashItem Item, string Named, OpenWithRecord? Session);
}

/// <summary>
/// An item's Open with… session as its row records it (<c>open_with_session</c>, State: Open with…): the session's folder
/// under the root, the copy's file name in it, the SHA-256 last recorded for the copy, and whether a rewrite of it
/// awaits adoption.
/// </summary>
internal sealed record OpenWithRecord(string Folder, string File, string Sha256, bool Awaiting)
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public string CopyPath(string root) => Path.Combine(root, Folder, File);

    public string Encode() => JsonSerializer.Serialize(this, Json);

    public static OpenWithRecord Decode(string json) => JsonSerializer.Deserialize<OpenWithRecord>(json, Json)!;
}
