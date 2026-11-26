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
        { Value: { AlreadyInStash: true } found } => new(name, ImportResultKind.AlreadyInStash, found.Item, Failure: committed.Failure) { GainedTags = found.GainedTags },
        { Succeeded: true, Value: { } added } => new(name, ImportResultKind.Added, added.Item) { GainedTags = added.GainedTags },
        { Value: { Listed: true } placed } => new(name, ImportResultKind.Failed, placed.Item, Failure: committed.Failure) { GainedTags = placed.GainedTags },
        _ => new(name, ImportResultKind.Failed, Failure: committed.Failure),
    };

    /// <summary>
    /// The lit tags <see cref="Item"/> gained from this file, as the store committed them (G11): every one for an item
    /// added, and for one already in the stash those it did not carry yet, which the batch's Undo takes back (G12).
    /// </summary>
    public IReadOnlyList<string> GainedTags { get; init; } = [];

    /// <summary>Whether the batch put this file's item in the stash, so its Undo removes it (G12).</summary>
    public bool PutInStash => Item is not null && Result is ImportResultKind.Added or ImportResultKind.Failed;

    /// <summary>
    /// Whether the batch's notice names a problem for this file (G16): skipped, unreadable or failed, or already in the
    /// stash with the step after it failed; added, or already in the stash alone, is none.
    /// </summary>
    public bool NamesAProblem => Result is ImportResultKind.Skipped or ImportResultKind.Unreadable or ImportResultKind.Failed || Failure is not null;
}

/// <summary>
/// The window's imports (G11, G12, G13): a drop's files, a folder's own files, the files chosen with Import files…, or
/// what a paste found on the clipboard (its files, or its picture as one PNG item), become items one file at a time
/// through Store's import commit, one batch at a time. While tag chips are lit at the drop, at <c>Ctrl+O</c> or at the
/// paste, every item the batch adds gets their tags in its own insert, and every item a file of it already was in the
/// stash as gets those it lacks, however long the batch waits behind another whose end clears the search (G11, G12).
/// While a batch runs its busy
/// state names it and nothing shows as added before its commit returned (G35, R9); afterwards the search is cleared,
/// All shows the new items first and selected (or, when it added none, the items its files already were in the stash
/// as), unless the person moved on since their act began it (<see cref="ImportBegan"/>), and a notice counts what became
/// of the files, names every file that was not added with its reason and the tags the lit chips gave, offers an Undo
/// that takes back everything the batch did, and, where the window kept what the person left, Show. Sources are only
/// ever copied (G11); nothing here writes into the stash but a stream into the <c>.work\</c> path Store allocated (R2).
/// </summary>
public sealed class Import(Listing listing, Notices notices, TimeSpan heldFileRetry)
{
    public const string Action = "Add to the stash";

    /// <summary>The acts a drop and Import files… are, each its own action for the notices they raise (G16).</summary>
    public const string DropAct = "Drop";

    /// <inheritdoc cref="DropAct"/>
    public const string PickAct = "Import files";

    /// <summary>F3's four formats, as the refusals name them (G12).</summary>
    public const string Formats = "GIF, PNG, JPG and WebP";

    /// <summary>What every pasted picture's result says beside it, added or not (G13).</summary>
    public const string StillOnly = "A pasted picture is always a still; to add an animated one, drag its file in.";

    /// <summary>
    /// Import files…'s picker: the four formats by every name they are taken by first, then every file, so a wrongly
    /// named one, or one whose name has no extension, can be chosen (G12, F3).
    /// </summary>
    public static readonly IReadOnlyList<PickerFilter> PickerFilters =
    [
        new("Pictures (GIF, PNG, JPG, WebP)", Enum.GetValues<ImageFormat>().SelectMany(ImageFormatExtensions.Taken).Select(extension => "*" + extension).ToList()),
        new("All files", ["*.*"]),
    ];

    /// <summary>
    /// Import files…'s picker title: "Import files", naming while tag chips are lit the tags the chosen files will get,
    /// "Import files (tagged #cat)" (G11).
    /// </summary>
    public static string PickerTitle(IReadOnlyList<string> litTags) => litTags.Count == 0 ? PickAct : $"{PickAct} (tagged {Tagged(litTags)})";

    /// <summary>Tags as an import's words name them, each after a <c>#</c> as its chip shows it: "#cat #dog".</summary>
    public static string Tagged(IEnumerable<string> tags) => string.Join(" ", tags.Select(tag => "#" + tag));

    /// <summary>The results a notice's heading counts after the added files, in its order, and how it counts each (G12).</summary>
    private static readonly (ImportResultKind Kind, string Words)[] Counted =
    [
        (ImportResultKind.AlreadyInStash, "already in your stash"),
        (ImportResultKind.Skipped, "skipped"),
        (ImportResultKind.Unreadable, "unreadable"),
        (ImportResultKind.Failed, "failed"),
    ];

    private readonly SemaphoreSlim oneBatchAtATime = new(1, 1);
    private readonly PictureImport pictures = new(heldFileRetry, failure => notices.Raise(failure));
    private PendingQueryKeys? scopeKeys;

    internal void FollowScopeChoices(PendingQueryKeys keys) => scopeKeys = keys;

    /// <summary>Raised whenever <see cref="Busy"/> changes.</summary>
    public event Action? Changed;

    /// <summary>The running batch's busy state in words ("Adding 3 of 20 to your stash…"), or null while none runs.</summary>
    public string? Busy { get; private set; }

    /// <summary>
    /// Raised before the window shows a batch's result (the search cleared, All with the new items first and selected), at
    /// its end or at its notice's Show, so whatever covers the grid closes first (G12).
    /// </summary>
    public event Action? Finishing;

    internal Func<Task>? FinishPreview { get; set; }

    private Task FinishView()
    {
        Finishing?.Invoke();
        return FinishPreview?.Invoke() ?? Task.CompletedTask;
    }

    /// <summary>
    /// A drop onto the window: its files and its folders' own files are imported after any batch already running; a
    /// drop that is only a link, or carries no file, is refused by name and nothing is fetched (N2).
    /// </summary>
    public Task<IReadOnlyList<ImportedFile>> Drop(DropContents contents)
    {
        using var acting = UserAct.Begin(DropAct);
        var litTags = listing.LitTags;
        var began = ImportBegan.In(listing);
        var subject = contents.Items.Count > 0 ? Describe(contents.Items) : contents.OnlyLink ? "the link" : "what was dropped";
        var refused = Refusal(subject);
        if (refused is null && contents.Items.Count == 0)
        {
            // A link alone, or no file, is refused (N2); files that could not be read went wrong (G35).
            refused = contents.OnlyLink ? Failure.Refusal(Action, subject, "links are not taken and nothing is downloaded; drag the picture itself in")
                : contents.FilesUnavailable ? new Failure(Action, subject, "ImageStash could not read the files it carried")
                : Failure.Refusal(Action, subject, "it holds no file");
        }

        if (refused is not null)
        {
            contents.Dispose();
            notices.Raise(refused);
            return Task.FromResult<IReadOnlyList<ImportedFile>>([]);
        }

        return Run(contents.Items, contents, litTags, began);
    }

    /// <summary>
    /// <c>Ctrl+O</c> and Import files…: the picker, given its title (<see cref="PickerTitle"/>, the tags lit at the act),
    /// while <paramref name="dialogUp"/> says a dialog is up so the flyout stays (G2), however it returns; the chosen
    /// files are imported as a drop's would be (G12).
    /// </summary>
    public async Task<IReadOnlyList<ImportedFile>> Pick(Func<string, Task<IReadOnlyList<string>?>> pick, Action<bool> dialogUp)
    {
        using var acting = UserAct.Begin(PickAct);
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
            paths = await pick(PickerTitle(litTags));
        }
        catch (Exception e)
        {
            // The platform's dialog failing is a failure to name, not a crash (G35).
            notices.Raise(new Failure("Import files", "the file picker", Failure.PlatformSaid("it did not open", e.Message)));
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

        var began = ImportBegan.In(listing);
        var chosen = paths.Select(path => new DroppedItem(Path.GetFileName(path), path, null, IsFolder: false)).ToList();
        // The import begins once files are chosen: a Move asked while the picker was up refuses it (State: Move).
        if (Refusal(Describe(chosen)) is { } refusedNow)
        {
            notices.Raise(refusedNow);
            return [];
        }

        return await Run(chosen, null, litTags, began);
    }

    /// <summary>
    /// The files a paste found on the clipboard, imported as a drop's would be (G13, G12) after any batch already
    /// running, each item added, or already in the stash, getting <paramref name="litTags"/>, the tags lit when the user
    /// pasted, its end judged from <paramref name="began"/>, read then too.
    /// </summary>
    public Task<IReadOnlyList<ImportedFile>> Paste(IReadOnlyList<DroppedItem> files, IReadOnlyList<string> litTags, ImportBegan began) => Run(files, null, litTags, began);

    /// <summary>
    /// The picture a paste found on the clipboard, saved after any batch already running as one still PNG item, even
    /// when its bytes equal an item's original, getting <paramref name="litTags"/>: the PNG bytes as they are when they
    /// are a PNG, else the first frame they or the bitmap give, encoded as PNG (G13, F3). Its notice says that an
    /// animated picture is added by dragging its file in, and offers Undo as a drop's does (G12); its end is judged from
    /// <paramref name="began"/>, read at the paste.
    /// </summary>
    public Task<IReadOnlyList<ImportedFile>> Paste(PastedPicture picture, IReadOnlyList<string> litTags, ImportBegan began) =>
        Batch(async store => [await pictures.Save(store, picture, litTags, byCapture: false)], StillOnly, began);

    /// <summary>
    /// The refusal of an import while the stash cannot take one: it is still opening, or did not open, or a Move is asked
    /// (G35, State: Move); null while it can, so the paste refuses before it reads the clipboard (N9).
    /// </summary>
    public Failure? Refusal(string subject) => listing.CannotStartReason is { } reason ? Failure.Refusal(Action, subject, reason) : null;

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

    /// <summary>
    /// The notice for a finished batch (G12): its heading counts the files added, already in the stash, skipped,
    /// unreadable and failed ("Added 38 · 1 already in your stash · 1 skipped", or "Added 3 to your stash" when nothing
    /// else happened), the added count naming the tags the lit chips gave the items added ("Added 3 to your stash, tagged
    /// #cat"); its details name every file that was not added with its reason, a duplicate by its item's name with the
    /// tags its item gained ("…as “cat”. This import gave it #funny."), a failure beside a duplicate included, and then the added
    /// items by name when they are three or fewer. A file the batch put in the stash whose last step failed is counted
    /// and named as failed, never as added. Its details still name the tags the store committed to the listed item (R9).
    /// </summary>
    public static (string Heading, string Details) NoticeWords(IReadOnlyList<ImportedFile> files)
    {
        if (files.Count == 0)
        {
            return ("Nothing was added: there was no file in what was dropped.", "");
        }

        var addedFiles = files.Where(file => file.Result == ImportResultKind.Added).ToList();
        var added = addedFiles.Select(file => file.Item!).ToList();
        // Compare the lit words as Store does since a spelling correction can land between file commits.
        var addedTags = addedFiles.Count == 0 ? [] : addedFiles[0].GainedTags
            .Where(tag => addedFiles.All(file => file.GainedTags.Contains(tag, StringComparer.OrdinalIgnoreCase))).ToList();
        var tagged = addedTags.Count > 0 ? $", tagged {Tagged(addedTags)}" : "";
        var counted = new List<string> { added.Count > 0 ? $"Added {added.Count}{tagged}" : "Nothing was added" };
        foreach (var (kind, words) in Counted)
        {
            var count = files.Count(file => file.Result == kind);
            if (count > 0)
            {
                counted.Add($"{count} {words}");
            }
        }

        var heading = added.Count > 0 && counted.Count == 1 ? $"Added {added.Count} to your stash{tagged}" : string.Join(" · ", counted);
        var parts = new List<string>();
        parts.AddRange(files.Where(file => file.Result == ImportResultKind.AlreadyInStash)
            .Select(file => $"{file.Name} was already in your stash as {Notices.Describe([file.Item!])}." + GivenTagWords(file, "it")));
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

        parts.AddRange(files.Where(file => file.Failure is not null).Select(FailureWords));
        if (added.Count is > 0 and <= Notices.NamedAtMost)
        {
            parts.Add($"Added {Notices.Describe(added)}.");
        }

        return (heading, string.Join(" ", parts));
    }

    /// <summary>
    /// A file's failure as its sentence names it, after the file's own name when that sentence names something else, as
    /// the store a refused write left behind ("still.png: Couldn't add to the stash: stash.db is …"): every file not
    /// added is named (G12).
    /// </summary>
    private static string FailureWords(ImportedFile file)
    {
        var sentence = file.Failure!.Sentence;
        var named = file.Failure.Subject == file.Name ? sentence : $"{file.Name}: {sentence}";
        return named + (file.Result == ImportResultKind.Failed ? GivenTagWords(file, "the picture") : "");
    }

    private static string GivenTagWords(ImportedFile file, string subject) =>
        file.GainedTags.Count > 0 ? $" This import gave {subject} {Tagged(file.GainedTags)}." : "";

    /// <param name="litTags">The tags lit at the user's act, read then, which every item added or already in the stash gets.</param>
    /// <param name="began">Read at the user's act, which the batch's end is judged from.</param>
    private Task<IReadOnlyList<ImportedFile>> Run(IReadOnlyList<DroppedItem> items, IDisposable? held, IReadOnlyList<string> litTags, ImportBegan began) =>
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
        }, also: null, began);

    /// <summary>
    /// One batch, after any already running: its busy state while <paramref name="steps"/> run, then its result shown
    /// (<see cref="Listing.ShowImportResult"/>), or, when the person moved on since <paramref name="began"/>, their search, scope,
    /// selection and preview kept over what the stash now holds; and the notice for every file, followed by
    /// <paramref name="also"/> when given, with an Undo, offered when the batch put an item in the stash or gave a tag to
    /// one already there, that takes back both, and Show when the window was kept (G12, R9).
    /// </summary>
    private async Task<IReadOnlyList<ImportedFile>> Batch(Func<StashStore, Task<List<ImportedFile>>> steps, string? also, ImportBegan began)
    {
        await oneBatchAtATime.WaitAsync();
        try
        {
            var store = listing.Store!;
            SetBusy("Adding to your stash…");
            var results = await steps(store);
            var shown = ImportEnd.ItemsShown(results);
            var batch = results.Where(file => file.PutInStash).Select(file => file.Item!.Id).ToList();
            IReadOnlyList<(long Item, IReadOnlyList<string> Tags)> given = results.Where(file => file.Result == ImportResultKind.AlreadyInStash && file.GainedTags.Count > 0)
                .Select(file => (file.Item!.Id, file.GainedTags)).ToList();
            var undoes = batch.Count > 0 || given.Count > 0;
            await SettleScopeAndTagChoices();

            var replacements = listing.Replacements;
            var kept = !await listing.ShowImportResult(shown, began, replacements, SettleScopeAndTagChoices, FinishView);
            if (kept)
            {
                // What the person left stays, its list reading what the stash now holds (G5); Show brings the result up.
                await listing.Refresh();
            }

            var show = kept ? new NoticeButton(ImportEnd.Show, () => ShowAgain(shown, replacements)) : null;

            var (heading, details) = NoticeWords(results);
            var text = also is null ? details : $"{details} {also}".TrimStart();
            notices.Raise(text, undoes ? "Undo" : null, undoes ? () => Undo(store, batch, given) : null, namesAProblem: results.Any(file => file.NamesAProblem), heading: heading,
                button: show);
            return results;
        }
        finally
        {
            SetBusy(null);
            oneBatchAtATime.Release();
        }
    }

    private async Task SettleScopeAndTagChoices()
    {
        // Only an accepted choice counts. An import's own scope reset must wait for one ordered behind an earlier key.
        while (scopeKeys?.PendingScopeChoice is { IsCompleted: false } || listing.TagChoicePending)
        {
            if (scopeKeys?.PendingScopeChoice is { IsCompleted: false } choosing)
            {
                await choosing;
            }
            else if (listing.TagChoicePending)
            {
                await listing.Settle();
            }
        }
    }

    /// <summary>
    /// The notice's Show: the batch's result shown again, as the person's change, over what the stash now holds; refused
    /// by name while the stash cannot take a change (a Move, an export or an import of it asked, G36, G35), and, once an
    /// import has replaced the stash, whose items take these identities again, none of <paramref name="items"/> selected.
    /// </summary>
    private async Task ShowAgain(IReadOnlyList<long> items, int replacements)
    {
        using var acting = UserAct.Begin(ImportEnd.Show);
        if (listing.CannotStartReason is { } reason)
        {
            notices.Raise(Failure.Refusal(ImportEnd.Show, ImportEnd.ShownSubject, reason));
            return;
        }

        await listing.ShowImportResult(items, ImportBegan.In(listing), replacements, SettleScopeAndTagChoices, FinishView, byPerson: true);
    }

    /// <summary>
    /// One file through the import's steps: format by name, or for a name with no extension by its first bytes, stage,
    /// read, then Store's duplicate question and commit.
    /// </summary>
    private async Task<ImportedFile> One(StashStore store, DroppedItem file, IReadOnlyList<string> litTags)
    {
        var verdict = FormatByName.Judge(file.Name);
        var staged = verdict switch
        {
            { ToCheck: true } => await FileWithoutExtension.Stage(store, file, pictures, heldFileRetry),
            { Supported: false } => null,
            _ => file.LocalPath is { } path ? await store.Stage(path) : await pictures.StageStream(store, file.Name, Path.GetExtension(file.Name), file.OpenRead!),
        };
        if (staged is null)
        {
            return new ImportedFile(file.Name, ImportResultKind.Skipped, Type: verdict.UnsupportedType);
        }

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

    /// <summary>
    /// The notice's Undo: the tags the batch gave items already in the stash come off them, and everything the batch
    /// added leaves through Store, its files to the Recycle Bin (G12, G29); the listing then shows what the store holds,
    /// a tag no item carries any more gone from the chips (G17).
    /// </summary>
    private async Task<Outcome> Undo(StashStore store, IReadOnlyList<long> batch, IReadOnlyList<(long Item, IReadOnlyList<string> Tags)> given)
    {
        var removed = await store.UndoImport(batch, given);
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
