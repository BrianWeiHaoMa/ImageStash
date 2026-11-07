using ImageStash.Files;
using ImageStash.Media;
using ImageStash.Seams;
using ImageStash.Store;

namespace ImageStash.Window;

/// <summary>What became of one file of an import (G12, G35).</summary>
public enum ImportResultKind
{
    Added,
    AlreadyInStash,
    Skipped,
    Unreadable,
    Failed,
}

/// <summary>
/// One file of an import, by the name the user knows it by, and what became of it: <see cref="Item"/> is the item
/// added, the one its bytes already were in the stash as, or one the batch put in the stash whose last step failed;
/// <see cref="Type"/> names a skipped file's type; and <see cref="Failure"/> names why it failed or, beside another
/// result, the step after it that failed.
/// </summary>
public sealed record ImportedFile(string Name, ImportResultKind Result, StashItem? Item = null, string? Type = null, Failure? Failure = null)
{
    /// <summary>
    /// What Store's commit made of the file: already in the stash, added, or failed after the commit. Added only once
    /// every step has completed (R9). A failure after the commit is named as failed: when the file reached the root the
    /// item is in the stash, which shows it and the batch's Undo removes; when it never did, no query returns it and the
    /// next start removes it, so it has no item here.
    /// </summary>
    public static ImportedFile FromCommit(string name, Outcome<ImportResult> committed) => committed switch
    {
        { Value: { AlreadyInStash: true } found } => new(name, ImportResultKind.AlreadyInStash, found.Item, Failure: committed.Failure),
        { Succeeded: true, Value: { } added } => new(name, ImportResultKind.Added, added.Item),
        { Value: { Listed: true } placed } => new(name, ImportResultKind.Failed, placed.Item, Failure: committed.Failure),
        _ => new(name, ImportResultKind.Failed, Failure: committed.Failure),
    };

    /// <summary>Whether the batch put this file's item in the stash, so its Undo removes it (G12).</summary>
    public bool PutInStash => Item is not null && Result is ImportResultKind.Added or ImportResultKind.Failed;
}

/// <summary>
/// The window's imports (G11, G12, G13): a drop's files, a folder's own files, the files chosen with Import files…, or
/// what a paste found on the clipboard (its files, or its picture as one PNG item), become items one file at a time
/// through Store's import commit, one batch at a time. While tag chips are lit at the drop, at <c>Ctrl+O</c> or at the
/// paste, every item the batch adds gets their tags in its own insert, however long the batch waits behind another whose
/// end clears the search (G11, G12). While a batch runs its busy
/// state names it and nothing shows as added before its commit returned (G35, R9); afterwards the search is cleared,
/// All shows the new items first and selected, and a notice states the result for every file with an Undo that
/// removes everything the batch added. Sources are only ever copied (G11); nothing here writes into the stash but a
/// stream into the <c>.work\</c> path Store allocated (R2).
/// </summary>
public sealed class Import(Listing listing, Notices notices, TimeSpan heldFileRetry)
{
    public const string Action = "Add to the stash";

    /// <summary>F3's four formats, as the refusals name them (G12).</summary>
    public const string Formats = "GIF, PNG, JPG and WebP";

    /// <summary>What every pasted picture's result says beside it, added or not (G13).</summary>
    public const string StillOnly = "A pasted picture is always a still; to add an animated one, drag its file in.";

    /// <summary>Import files…'s picker: the four formats first, then every file, so a wrongly named one can be chosen and named (G12).</summary>
    public static readonly IReadOnlyList<PickerFilter> PickerFilters =
    [
        new("Pictures (GIF, PNG, JPG, WebP)", Enum.GetValues<ImageFormat>().SelectMany(ImageFormatExtensions.Of).Select(extension => "*" + extension).ToList()),
        new("All files", ["*.*"]),
    ];

    private readonly SemaphoreSlim oneBatchAtATime = new(1, 1);
    private readonly PictureImport pictures = new(heldFileRetry, failure => notices.Raise(failure));

    /// <summary>Raised whenever <see cref="Busy"/> changes.</summary>
    public event Action? Changed;

    /// <summary>The running batch's busy state in words ("Adding 3 of 20 to your stash…"), or null while none runs.</summary>
    public string? Busy { get; private set; }

    /// <summary>
    /// Raised once a batch has run, before the window shows its result (the search cleared, All with the new items first
    /// and selected), so whatever covers the grid closes first (G12).
    /// </summary>
    public event Action? Finishing;

    /// <summary>
    /// A drop onto the window: its files and its folders' own files are imported after any batch already running; a
    /// drop that is only a link, or carries no file, is refused by name and nothing is fetched (N2).
    /// </summary>
    public Task<IReadOnlyList<ImportedFile>> Drop(DropContents contents)
    {
        var litTags = listing.LitTags;
        var subject = contents.Items.Count > 0 ? Describe(contents.Items) : contents.OnlyLink ? "the link" : "what was dropped";
        var refused = Refusal(subject);
        if (refused is null && contents.Items.Count == 0)
        {
            refused = new Failure(Action, subject, contents.OnlyLink
                ? "links are not taken and nothing is downloaded; drag the picture itself in"
                : contents.FilesUnavailable ? "its files could not be read from what was dragged" : "it holds no file");
        }

        if (refused is not null)
        {
            contents.Dispose();
            notices.Raise(refused);
            return Task.FromResult<IReadOnlyList<ImportedFile>>([]);
        }

        return Run(contents.Items, contents, litTags);
    }

    /// <summary>
    /// <c>Ctrl+O</c> and Import files…: the picker, while <paramref name="dialogUp"/> says a dialog is up so the flyout
    /// stays (G2), however it returns; the chosen files are imported as a drop's would be (G12).
    /// </summary>
    public async Task<IReadOnlyList<ImportedFile>> Pick(Func<Task<IReadOnlyList<string>?>> pick, Action<bool> dialogUp)
    {
        var litTags = listing.LitTags;
        if (Refusal("the files to import") is { } refused)
        {
            notices.Raise(refused);
            return [];
        }

        IReadOnlyList<string>? paths;
        dialogUp(true);
        try
        {
            paths = await pick();
        }
        catch (Exception e)
        {
            // The platform's dialog failing is a failure to name, not a crash (G35).
            notices.Raise(new Failure("Import files", "the file picker", e.Message));
            return [];
        }
        finally
        {
            dialogUp(false);
        }

        if (paths is not { Count: > 0 })
        {
            return [];
        }

        var chosen = paths.Select(path => new DroppedItem(Path.GetFileName(path), path, null, IsFolder: false)).ToList();
        // The import begins once files are chosen: a Move asked while the picker was up refuses it (State: Move).
        if (Refusal(Describe(chosen)) is { } refusedNow)
        {
            notices.Raise(refusedNow);
            return [];
        }

        return await Run(chosen, null, litTags);
    }

    /// <summary>
    /// The files a paste found on the clipboard, imported as a drop's would be (G13, G12) after any batch already
    /// running, each item getting <paramref name="litTags"/>, the tags lit when the user pasted.
    /// </summary>
    public Task<IReadOnlyList<ImportedFile>> Paste(IReadOnlyList<DroppedItem> files, IReadOnlyList<string> litTags) => Run(files, null, litTags);

    /// <summary>
    /// The picture a paste found on the clipboard, saved after any batch already running as one still PNG item, even
    /// when its bytes equal an item's original, getting <paramref name="litTags"/>: the PNG bytes as they are when they
    /// are a PNG, else the first frame they or the bitmap give, encoded as PNG (G13, F3). Its notice says that an
    /// animated picture is added by dragging its file in, and offers Undo as a drop's does (G12).
    /// </summary>
    public Task<IReadOnlyList<ImportedFile>> Paste(PastedPicture picture, IReadOnlyList<string> litTags) =>
        Batch(async store => [await pictures.Save(store, picture, litTags)], StillOnly);

    /// <summary>
    /// The refusal of an import while the stash cannot take one: it is still opening, or did not open, or a Move is asked
    /// (G35, State: Move); null while it can, so the paste refuses before it reads the clipboard (N9).
    /// </summary>
    public Failure? Refusal(string subject) => listing.CannotStartReason is { } reason ? new Failure(Action, subject, reason) : null;

    /// <summary>
    /// The files a drop carries, in order: each file, and each folder's own files but not its subfolders' (G11), by
    /// name; a folder that cannot be listed is named instead.
    /// </summary>
    public static (IReadOnlyList<DroppedItem> Files, IReadOnlyList<ImportedFile> Unlisted) Expand(IReadOnlyList<DroppedItem> items)
    {
        var files = new List<DroppedItem>();
        var unlisted = new List<ImportedFile>();
        foreach (var item in items)
        {
            if (!item.IsFolder)
            {
                files.Add(item);
                continue;
            }

            if (item.LocalPath is null)
            {
                unlisted.Add(new ImportedFile(item.Name, ImportResultKind.Failed, Failure: new Failure("Read the folder", item.Name, "it is not a folder on this PC")));
                continue;
            }

            try
            {
                files.AddRange(Directory.GetFiles(item.LocalPath).Order(StringComparer.OrdinalIgnoreCase)
                    .Select(path => new DroppedItem(Path.GetFileName(path), path, null, IsFolder: false)));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                unlisted.Add(new ImportedFile(item.Name, ImportResultKind.Failed, Failure: new Failure("Read the folder", item.Name, HeldFileRetry.ReasonFor(e))));
            }
        }

        return (files, unlisted);
    }

    /// <summary>The notice for a finished batch: every file named with what became of it, the added ones after their count (G12).</summary>
    public static string NoticeText(IReadOnlyList<ImportedFile> files)
    {
        if (files.Count == 0)
        {
            return "Nothing was added: there was no file in what was dropped.";
        }

        var added = files.Where(file => file.Result == ImportResultKind.Added).Select(file => file.Name).ToList();
        var parts = new List<string> { added.Count > 0 ? $"Added {added.Count} to your stash: {Join(added)}." : "Nothing was added." };
        parts.AddRange(files.Where(file => file.Result == ImportResultKind.AlreadyInStash)
            .Select(file => $"{file.Name} was already in your stash as “{file.Item!.Name}”."));
        var skipped = files.Where(file => file.Result == ImportResultKind.Skipped).ToList();
        if (skipped.Count > 0)
        {
            parts.Add($"Skipped {Join(skipped.Select(file => $"{file.Name} ({file.Type})"))}: ImageStash keeps {Formats}.");
        }

        var unreadable = files.Where(file => file.Result == ImportResultKind.Unreadable).ToList();
        if (unreadable.Count > 0)
        {
            parts.Add($"{Join(unreadable.Select(file => file.Name))} {(unreadable.Count == 1 ? "is" : "are")} unreadable.");
        }

        parts.AddRange(files.Where(file => file.Failure is not null).Select(file => file.Failure!.Sentence.TrimEnd('.') + "."));
        return string.Join(" ", parts);
    }

    /// <param name="litTags">The tags lit at the user's act, read then, which every item added gets.</param>
    private Task<IReadOnlyList<ImportedFile>> Run(IReadOnlyList<DroppedItem> items, IDisposable? held, IReadOnlyList<string> litTags) =>
        Batch(async store =>
        {
            try
            {
                var (files, unlisted) = await Task.Run(() => Expand(items));
                List<ImportedFile> results = [.. unlisted];
                for (var i = 0; i < files.Count; i++)
                {
                    SetBusy($"Adding {i + 1} of {files.Count} to your stash…");
                    results.Add(await One(store, files[i], litTags));
                }

                return results;
            }
            finally
            {
                // Every stream has been read: the source may let go of what it handed over.
                held?.Dispose();
            }
        }, also: null);

    /// <summary>
    /// One batch, after any already running: its busy state while <paramref name="steps"/> run, then its end announced,
    /// the search cleared, All with the new items first and selected, and the notice for every file, followed by
    /// <paramref name="also"/> when given, with an Undo that removes everything the batch put in the stash (G12, R9).
    /// </summary>
    private async Task<IReadOnlyList<ImportedFile>> Batch(Func<StashStore, Task<List<ImportedFile>>> steps, string? also)
    {
        await oneBatchAtATime.WaitAsync();
        try
        {
            var store = listing.Store!;
            SetBusy("Adding to your stash…");
            var results = await steps(store);
            var added = results.Where(file => file.Result == ImportResultKind.Added).Select(file => file.Item!.Id).ToList();
            var batch = results.Where(file => file.PutInStash).Select(file => file.Item!.Id).ToList();
            Finishing?.Invoke();
            await listing.ShowScope(Scope.All);
            if (added.Count > 0)
            {
                listing.Select(added, null);
            }

            var text = also is null ? NoticeText(results) : NoticeText(results) + " " + also;
            notices.Raise(text, batch.Count > 0 ? "Undo" : null, batch.Count > 0 ? () => Undo(store, batch) : null);
            return results;
        }
        finally
        {
            SetBusy(null);
            oneBatchAtATime.Release();
        }
    }

    /// <summary>One file through the import's steps: format by name, stage, read, then Store's duplicate question and commit.</summary>
    private async Task<ImportedFile> One(StashStore store, DroppedItem file, IReadOnlyList<string> litTags)
    {
        var verdict = FormatByName.Judge(file.Name);
        if (!verdict.Supported)
        {
            return new ImportedFile(file.Name, ImportResultKind.Skipped, Type: verdict.UnsupportedType);
        }

        var staged = file.LocalPath is { } path ? await store.Stage(path) : await pictures.StageStream(store, file.Name, Path.GetExtension(file.Name), file.OpenRead!);
        if (!staged.Succeeded)
        {
            return new ImportedFile(file.Name, ImportResultKind.Failed, Failure: staged.Failure);
        }

        var read = await Task.Run(() => MediaDecoder.ReadFacts(staged.Value!, file.Name, heldFileRetry));
        if (!read.Succeeded)
        {
            var discarded = await store.Discard(staged.Value!);
            return pictures.Unread(file.Name, read.Failure, discarded.Failure);
        }

        // F12: the item carries its file's name without the extension, empty when that is all the name was (G17).
        var candidate = new ImportCandidate(staged.Value!, Path.GetFileNameWithoutExtension(file.Name), read.Value!.Facts, read.Value.Sha256, litTags);
        return ImportedFile.FromCommit(file.Name, await store.CommitImport(candidate));
    }

    /// <summary>The notice's Undo: everything the batch added leaves through Store, its files to the Recycle Bin (G12, G29).</summary>
    private async Task<Outcome> Undo(StashStore store, IReadOnlyList<long> batch)
    {
        var removed = await store.Remove(batch);
        await listing.Refresh();
        var refused = removed.Where(outcome => !outcome.Succeeded).Select(outcome => outcome.Failure!).ToList();
        foreach (var failure in refused.Skip(1))
        {
            notices.Raise(failure);
        }

        return refused.Count == 0 ? Outcome.Done : refused[0];
    }

    private void SetBusy(string? busy)
    {
        Busy = busy;
        Changed?.Invoke();
    }

    /// <summary>A drop's files as a refusal names them: by name for up to three, else by count.</summary>
    private static string Describe(IReadOnlyList<DroppedItem> items) =>
        items.Count <= 3 ? Join(items.Select(item => item.Name)) : $"{items.Count} files";

    private static string Join(IEnumerable<string> names)
    {
        var all = names.ToList();
        return all.Count <= 1 ? string.Concat(all) : string.Join(", ", all.Take(all.Count - 1)) + " and " + all[^1];
    }
}
