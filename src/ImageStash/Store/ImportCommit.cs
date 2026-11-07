using ImageStash.Files;
using ImageStash.Media;

namespace ImageStash.Store;

/// <summary>
/// A staged file in <c>.work\</c> with what Media read from it, ready to become an item (F12's name), and the tags a
/// new item gets, the lit chip's (G11). <paramref name="SavedEvenIfInStash"/> marks a picture ImageStash saves from
/// the clipboard rather than a file the user brings: G12's question whether it is already in the stash is asked of a
/// file only, so a pasted picture whose bytes equal an item's original is a second item.
/// </summary>
public sealed record ImportCandidate(string StagedPath, string Name, MediaFacts Facts, byte[] Sha256, IReadOnlyList<string>? Tags = null, bool SavedEvenIfInStash = false);

/// <summary>
/// What an import commit did: added <see cref="Item"/>, or found the staged bytes already in the stash as it (G12).
/// <see cref="Listed"/> is false only for an item committed whose file never reached the root: no query returns it,
/// and the next start removes it (State: A live failure).
/// </summary>
public sealed record ImportResult(StashItem Item, bool AlreadyInStash, bool Listed = true);

/// <summary>
/// Store's steps of an import (State: the import bullet, G12, G28): the duplicate question over the staged bytes, then
/// the row committed with "place in root" pending, the rename into the root, and the step cleared, in that order, in
/// one call on the store's thread, so no other import can slip between the question and the commit.
/// </summary>
internal static class ImportCommit
{
    public const string Action = "Add to the stash";

    private const string Compare = "Look for the same picture";

    private const int ComparisonBuffer = 64 * 1024;

    /// <summary>
    /// Adds the candidate as a new item and returns it only once its file is in the root under P1's name and the step
    /// is cleared (R9); or, for a file whose bytes an item's original already holds, discards the staged file and returns
    /// that item, with a failure naming the staged copy when it cannot be deleted. A failure before the commit discards
    /// the staged file too, so it leaves nothing. A failure after the
    /// commit carries the item as committed: no query returns it, its pending step and staged file stay, and the next
    /// start removes them (State: A live failure).
    /// </summary>
    public static Outcome<ImportResult> Commit(StoreContext store, ImportCandidate candidate)
    {
        var refused = Refusal(store, candidate);
        if (refused is not null)
        {
            return refused;
        }

        var extension = ExtensionFor(candidate);
        var duplicate = candidate.SavedEvenIfInStash ? Outcome<StashItem?>.Of(null) : FindDuplicate(store, candidate, candidate.Name + extension);
        if (!duplicate.Succeeded)
        {
            Discard(store, candidate);
            return duplicate.Failure;
        }

        if (duplicate.Value is not null)
        {
            // The answer stands; a staged copy that cannot be deleted is named, and the next start's clearing of
            // .work\ removes it.
            var found = new ImportResult(duplicate.Value, AlreadyInStash: true);
            var discarded = Discard(store, candidate);
            return discarded.Succeeded ? Outcome<ImportResult>.Of(found) : Outcome<ImportResult>.FailAfterCommit(discarded.Failure with { Subject = candidate.Name + extension }, found);
        }

        var workName = Path.GetFileName(candidate.StagedPath);
        var addedAt = DateTimeOffset.FromUnixTimeMilliseconds(store.Clock.GetUtcNow().ToUnixTimeMilliseconds());
        var inserted = store.Change(Action, () =>
        {
            var fileName = ItemFileNames.Choose(store, candidate.Name, extension, ItemFileNames.TakenInRoot(store));
            var id = (long)store.Scalar(
                """
                INSERT INTO items (name, added_at, format, width, height, animated, frame_count, duration_ms,
                                   original_file, finished_file, original_sha256, pending_steps)
                VALUES ($name, $added_at, $format, $width, $height, $animated, $frame_count, $duration_ms,
                        $file, $file, $sha256, $pending)
                RETURNING id
                """,
                ("$name", candidate.Name),
                ("$added_at", addedAt.ToUnixTimeMilliseconds()),
                ("$format", candidate.Facts.Format.ToString()),
                ("$width", candidate.Facts.Width),
                ("$height", candidate.Facts.Height),
                ("$animated", candidate.Facts.Animated),
                ("$frame_count", candidate.Facts.FrameCount),
                ("$duration_ms", candidate.Facts.DurationMs),
                ("$file", fileName),
                ("$sha256", candidate.Sha256),
                ("$pending", PendingStep.Encode([PendingStep.Place(workName, fileName)])))!;
            // In the insert's own transaction, so a kill leaves the item with its tags or not at all (G28).
            var tags = ItemTags.AddToNewItem(store, id, candidate.Tags ?? []);
            return (Id: id, FileName: fileName, Tags: tags);
        });
        if (!inserted.Succeeded)
        {
            Discard(store, candidate);
            return inserted.Failure;
        }

        var (itemId, itemFile, itemTags) = inserted.Value;
        // Stamped after the insert, the last commit until the clearing, and again once its rename has put its file in place:
        // an item whose clearing is refused is listed as it is.
        var added = new ImportResult(
            new StashItem(itemId, candidate.Name, itemTags, false, addedAt, itemFile, store.Folder.InRoot(itemFile), candidate.Facts, 0, Edited: false)
            {
                CommitsWhenRead = store.Commits,
                FileStepsWhenRead = store.FileSteps,
            },
            AlreadyInStash: false);
        var renamed = store.RetryHeldFile(Action, candidate.StagedPath, () => File.Move(candidate.StagedPath, added.Item.FilePath));
        if (!renamed.Succeeded)
        {
            return Outcome<ImportResult>.FailAfterCommit(renamed.Failure with { Subject = itemFile }, added with { Listed = false });
        }

        added = added with { Item = added.Item with { FileStepsWhenRead = store.FileSteps } };
        var cleared = store.Change(Action, () => store.Execute("UPDATE items SET pending_steps = NULL WHERE id = $id", ("$id", itemId)));
        return cleared.Succeeded
            ? Outcome<ImportResult>.Of(added with { Item = added.Item with { CommitsWhenRead = store.Commits } })
            : Outcome<ImportResult>.FailAfterCommit(cleared.Failure, added);
    }

    /// <summary>
    /// At start, an import whose file reached the root has its step cleared; one whose file did not is removed with its
    /// <c>.work\</c> file. When that file cannot be deleted, the row stays as it is for the next start, and is named.
    /// </summary>
    public static Failure? FinishAtStart(StoreContext store, long id, IReadOnlyList<PendingStep> steps)
    {
        var inPlace = steps.All(s => s.IsDone(store.Folder));
        if (inPlace)
        {
            var cleared = store.Change(StoreOpen.Action, () => store.Execute("UPDATE items SET pending_steps = NULL WHERE id = $id", ("$id", id)));
            return cleared.Failure;
        }

        foreach (var step in steps)
        {
            var staged = store.Folder.InWork(step.From);
            var deleted = store.RetryHeldFile("Undo an unfinished import", staged, () => File.Delete(staged));
            if (!deleted.Succeeded)
            {
                return deleted.Failure with { Subject = step.To };
            }
        }

        var removed = store.Change(StoreOpen.Action, () => BatchRemoval.RemoveRows(store, [id]));
        return removed.Failure;
    }

    /// <summary>
    /// The extension F3 gives the format Media read (State: File names): the file's own, lowercased, when it is one of
    /// that format's (<c>.jpeg</c> stays <c>.jpeg</c>), and otherwise the format's, so a PNG named <c>cat.jpg</c>
    /// becomes <c>cat.png</c>. The bytes stay as they came (F3).
    /// </summary>
    private static string ExtensionFor(ImportCandidate candidate)
    {
        var own = Path.GetExtension(candidate.StagedPath).ToLowerInvariant();
        var extensions = ImageFormatExtensions.Of(candidate.Facts.Format);
        return extensions.Contains(own) ? own : extensions[0];
    }

    /// <summary>
    /// The listed item whose original has the candidate's hash and the same bytes (G12: equal hashes are compared byte for
    /// byte), the original where it is now: a row whose pending Open with… adoption adopts bytes of that hash, or whose
    /// Undo has brought back an original of that hash, is a candidate too, since its column keeps the outgoing original's
    /// hash until that change's last transaction.
    /// </summary>
    private static Outcome<StashItem?> FindDuplicate(StoreContext store, ImportCandidate candidate, string subject)
    {
        var candidates = store.Read(Compare, () =>
        {
            var tags = StashItem.AllTags(store);
            return store.Query(
                $"SELECT {StashItem.Columns}, {StoredFiles.Columns}, i.original_sha256 = $sha256 FROM items i WHERE {StashItem.NotDeleted} AND (i.original_sha256 = $sha256 OR i.pending_steps IS NOT NULL) ORDER BY i.id",
                r => (Item: StashItem.ListedFromRow(r, store, tags.GetValueOrDefault(r.GetInt64(0)) ?? []), Files: StoredFiles.FromRow(r, StashItem.ColumnCount), SameHash: r.GetBoolean(StashItem.ColumnCount + StoredFiles.ColumnCount)),
                ("$sha256", candidate.Sha256))
                .Where(row => row.Item is not null && (row.SameHash || AdoptionSteps.MakeOriginal(store.Folder, row.Files.Steps, candidate.Sha256)))
                .ToList();
        });
        if (!candidates.Succeeded)
        {
            return candidates.Failure;
        }

        foreach (var (item, files, _) in candidates.Value!)
        {
            var same = SameBytes(store, files.OriginalPath(store.Folder), candidate.StagedPath);
            if (!same.Succeeded)
            {
                return same.Failure with { Subject = subject };
            }

            if (same.Value)
            {
                return Outcome<StashItem?>.Of(item!);
            }
        }

        return Outcome<StashItem?>.Of(null);
    }

    /// <summary>Deletes the staged file the import will not add; one that cannot be deleted now goes with the next start's clearing of <c>.work\</c>.</summary>
    private static Outcome Discard(StoreContext store, ImportCandidate candidate) =>
        store.RetryHeldFile("Discard a copy", candidate.StagedPath, () => File.Delete(candidate.StagedPath));

    private static Failure? Refusal(StoreContext store, ImportCandidate candidate)
    {
        var subject = Path.GetFileName(candidate.StagedPath);
        if (!store.Folder.IsWorkFile(candidate.StagedPath))
        {
            return new Failure(Action, subject, "it is not in the stash's work folder");
        }

        if (!File.Exists(candidate.StagedPath))
        {
            return new Failure(Action, subject, "the copy to add is gone");
        }

        if (candidate.Sha256.Length != 32)
        {
            return new Failure(Action, subject, "its content hash is not a SHA-256");
        }

        return null;
    }

    /// <summary>
    /// Whether two files hold the same bytes, read to the end of both, never open for writing (R3). A file held or
    /// locked part way is compared again from the start, from fresh streams, for the held-file duration (G35).
    /// </summary>
    private static Outcome<bool> SameBytes(StoreContext store, string original, string staged) =>
        store.RetryHeldFile(Compare, staged, () =>
        {
            using var a = OpenToRead(original);
            using var b = OpenToRead(staged);
            return a.Length == b.Length && SameToTheEnd(a, b);
        });

    private static bool SameToTheEnd(Stream a, Stream b)
    {
        var left = new byte[ComparisonBuffer];
        var right = new byte[ComparisonBuffer];
        while (true)
        {
            var read = a.ReadAtLeast(left, left.Length, throwOnEndOfStream: false);
            if (b.ReadAtLeast(right, right.Length, throwOnEndOfStream: false) != read || !left.AsSpan(0, read).SequenceEqual(right.AsSpan(0, read)))
            {
                return false;
            }

            if (read < left.Length)
            {
                return true;
            }
        }
    }

    private static FileStream OpenToRead(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, ComparisonBuffer);
}
