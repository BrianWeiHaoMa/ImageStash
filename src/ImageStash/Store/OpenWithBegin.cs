using ImageStash.Files;
using ImageStash.Media;

namespace ImageStash.Store;

/// <summary>
/// Begins or continues an item's Open with… session (State: Open with…; G9, G27): the finished picture copied, under
/// its file name, into a new folder of its own under the root, outside the stash folder (E2), and the session recorded
/// on the row with the SHA-256 Media reads of the copy; the copy's path is what the Open-with dialog opens.
/// </summary>
internal static class OpenWithBegin
{
    public const string Action = "Open with…";

    /// <summary>
    /// The copy's path: with no session recorded, a new one; with one, the copy classified first, so a rewrite awaiting
    /// adoption or a write still in flight is never discarded (its path answered, nothing changed) and an undecodable
    /// rewrite this read is the first to see is named; an unchanged copy is refreshed to the finished picture as it is now,
    /// in a fresh folder recorded in one transaction before the old folder is deleted, so no kill leaves a session whose
    /// recorded hash does not describe the copy its folder holds (G27, G28).
    /// </summary>
    public static async Task<Outcome<string>> Begin(StoreThread thread, StoreContext store, StashItem item)
    {
        var gone = ItemQueries.Named(item.Name, item.FileName);
        if (store.OpenWithRoot is not { } root)
        {
            return new Failure(Action, gone, OpenWithCopies.NoRoot);
        }

        while (true)
        {
            var row = await thread.Run(() => OpenWithCopies.ReadRow(store, item.Id, gone, Action));
            if (!row.Succeeded)
            {
                return row.Failure;
            }

            var session = row.Value!.Session;
            var missing = false;
            if (session is not null)
            {
                // A missing copy is waited out as a held one, so a replacement half way lands first; one still missing is
                // made again, since the dialog needs a copy to open on (G27).
                var classified = await OpenWithCopies.ClassifyLive(thread, store, item.Id, gone, whileMissing: true);
                if (!classified.Succeeded)
                {
                    return classified.Failure;
                }

                if (classified.Value!.Copy == OpenWithCopy.AwaitingAdoption || classified.Value is { Copy: OpenWithCopy.StillHeld, Missing: false })
                {
                    return Outcome<string>.Of(classified.Value.Session.CopyPath(root));
                }

                session = classified.Value.Session;
                missing = classified.Value.Missing;
            }

            // One attempt per call on the store's thread, the waits while the finished picture is held spent off it, so
            // the summon's listing and a handoff's confirmation run between them (G1, G35).
            var made = await HeldFileRetry.RunSpaced(
                store.RetryDuration,
                () => thread.Run(() => MakeCopy(store, root, item.Id, gone, session)),
                attempt => attempt.Failure?.Reason == HeldFileRetry.HeldReason);
            if (!made.Succeeded)
            {
                return made.Failure;
            }

            if (made.Value is not { } copy)
            {
                // The session changed since it was read: begin again from it.
                continue;
            }

            var read = await Task.Run(() => store.Reader(copy.Path, copy.Record.File, store.RetryDuration));
            // Hashed off the store's thread, as a classification reads, so a large copy keeps no listing waiting (G1).
            var replaced = session is null || missing ? null : await Task.Run(() => HoldIfUnchanged(session.CopyPath(root), session.Sha256));
            Outcome<bool> recorded;
            using (replaced)
            {
                recorded = await thread.Run(() => Record(store, root, item.Id, gone, session, missing, replaced, copy, read));
            }

            if (!recorded.Succeeded)
            {
                return recorded.Failure;
            }

            if (recorded.Value)
            {
                return Outcome<string>.Of(copy.Path);
            }
        }
    }

    /// <summary>
    /// Copies the finished picture as the stash lists it now into a new session folder, while the row's session is still
    /// <paramref name="expected"/> (else null, nothing made), in one attempt; a failure, a held file's among them, deletes
    /// what it made and names the item.
    /// </summary>
    private static Outcome<MadeCopy?> MakeCopy(StoreContext store, string root, long id, string gone, OpenWithRecord? expected)
    {
        var row = OpenWithCopies.ReadRow(store, id, gone, Action);
        if (!row.Succeeded)
        {
            return row.Failure;
        }

        if (row.Value!.Session != expected)
        {
            return Outcome<MadeCopy?>.Of(null);
        }

        var item = row.Value.Item;
        var prepared = OpenWithRoot.Prepare(root);
        if (!prepared.Succeeded)
        {
            return new Failure(Action, row.Value.Named, $"the folder for its copy cannot be made: {prepared.Failure.Reason}");
        }

        var folder = OpenWithRoot.MakeSessionFolder(root, Action, row.Value.Named);
        if (!folder.Succeeded)
        {
            return folder.Failure;
        }

        // The finished picture on disk under its file name (G9, P1, F3), read and never written (R3).
        var record = new OpenWithRecord(folder.Value!, item.FileName, "", Awaiting: false);
        var path = record.CopyPath(root);
        var copied = store.TryHeldFileOnce(Action, item.FilePath, () => File.Copy(item.FilePath, path, overwrite: true));
        if (copied.Succeeded)
        {
            // A copy keeps its source's attributes, and the outside program must be able to write it back (G27).
            copied = store.TryHeldFileOnce(Action, path, () => File.SetAttributes(path, FileAttributes.Normal));
        }

        if (!copied.Succeeded)
        {
            OpenWithRoot.DeleteUnrecorded(store, root, folder.Value!);
            return copied.Failure with { Subject = row.Value.Named };
        }

        return Outcome<MadeCopy?>.Of(new MadeCopy(record, path));
    }

    /// <summary>
    /// Records the session made, with the hash Media read of the copy, in one transaction, then deletes the folder of the
    /// session it replaces: true once recorded; false, the new folder deleted, when the session changed since the copy was
    /// made or the copy it replaces is no longer as it was classified, still missing when <paramref name="missing"/> and
    /// otherwise <paramref name="replaced"/>, held since its bytes were found unchanged (another program wrote it
    /// meanwhile, which a classification must read before anything replaces it, G27). A failure deletes the new folder,
    /// or leaves it to the next open's clearing.
    /// </summary>
    private static Outcome<bool> Record(
        StoreContext store, string root, long id, string gone, OpenWithRecord? expected, bool missing, FileStream? replaced, MadeCopy made, Outcome<ReadPicture> read)
    {
        var row = OpenWithCopies.ReadRow(store, id, gone, Action);
        Outcome<bool> answer = !row.Succeeded ? row.Failure
            : !read.Succeeded ? new Failure(Action, row.Value!.Named, read.Failure.Reason)
            : row.Value!.Session != expected || (expected is not null && (missing ? File.Exists(expected.CopyPath(root)) : replaced is null)) ? Outcome<bool>.Of(false)
            : Commit(store, id, made.Record with { Sha256 = Convert.ToHexString(read.Value!.Sha256) });
        if (answer is not { Succeeded: true, Value: true })
        {
            OpenWithRoot.DeleteUnrecorded(store, root, made.Record.Folder);
            return answer;
        }

        // Only now is the copy it replaces no session's, so a kill before this left that session whole (G28). It was held
        // against every other open until now, renames included, so a save the outside program tried meanwhile, in place
        // or by replacement, failed there rather than landing here to be deleted unread. It is let go only to be deleted at
        // once: a file held without sharing cannot be deleted by its path, and this assembly has no delete through a handle.
        if (expected is not null)
        {
            if (replaced is not null)
            {
                replaced.Dispose();
                store.TryHeldFileOnce(Action, expected.CopyPath(root), () => File.Delete(expected.CopyPath(root)));
            }

            OpenWithRoot.DeleteUnrecorded(store, root, expected.Folder);
        }

        return answer;
    }

    /// <summary>
    /// The copy a refresh replaces, opened without sharing and so held against every other open, a rename or a delete of
    /// it included, until disposed, while the bytes read through that handle still have the SHA-256
    /// <paramref name="sha256"/> its classification found (State: Open with…, "a difference from the recorded hash is a
    /// rewrite"); null when they differ or it cannot be opened alone.
    /// </summary>
    private static FileStream? HoldIfUnchanged(string copy, string sha256)
    {
        FileStream? held = null;
        try
        {
            held = new FileStream(copy, FileMode.Open, FileAccess.Read, FileShare.None);
            if (Convert.ToHexString(MediaDecoder.Sha256Of(held)) == sha256)
            {
                return held;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Another program has it open, or it is gone: not as classified, so it is classified again.
        }

        held?.Dispose();
        return null;
    }

    private static Outcome<bool> Commit(StoreContext store, long id, OpenWithRecord session)
    {
        var changed = store.Change(Action, () =>
            store.Execute("UPDATE items SET open_with_session = $session WHERE id = $id", ("$session", session.Encode()), ("$id", id)));
        return changed.Succeeded ? Outcome<bool>.Of(true) : changed.Failure;
    }

    private sealed record MadeCopy(OpenWithRecord Record, string Path);
}
