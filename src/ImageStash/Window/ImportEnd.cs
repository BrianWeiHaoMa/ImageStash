namespace ImageStash.Window;

/// <summary>
/// The moment the person's act began an import (G12): the drop, the files chosen with Import files…, the paste, read
/// then and not when a batch queued behind another gets its turn. At the batch's end it says whether the person moved on
/// meanwhile, having changed the search, the scope or the selection, or opened the preview, as
/// <see cref="Listing.PersonsChanges"/> counts what the person asked and nothing the window did itself (an earlier
/// import's end, a summon, a load).
/// </summary>
public readonly record struct ImportBegan(int PersonsChanges)
{
    /// <summary>Read at the person's act.</summary>
    public static ImportBegan In(Listing listing) => new(listing.PersonsChanges);

    /// <summary>Whether the person has changed the search, the scope or the selection, or opened the preview, since.</summary>
    public bool MovedOn(Listing listing) => listing.PersonsChanges != PersonsChanges;
}

/// <summary>What an import's end shows, and its notice's Show shows again (G12).</summary>
public static class ImportEnd
{
    /// <summary>The import notice's button beside its Undo, offered when the window kept what the person left (G12).</summary>
    public const string Show = "Show";

    /// <summary>What a refused Show names as refused (G35).</summary>
    public const string ShownSubject = "the import's pictures";

    /// <summary>
    /// The items an import's end selects (G12): the ones it added, or, when it added none, each item it reported a file as
    /// already in the stash as, once each.
    /// </summary>
    public static IReadOnlyList<long> ItemsShown(IReadOnlyList<ImportedFile> files)
    {
        var added = files.Where(file => file.Result == ImportResultKind.Added).Select(file => file.Item!.Id).ToList();
        return added.Count > 0 ? added : files.Where(file => file.Result == ImportResultKind.AlreadyInStash).Select(file => file.Item!.Id).Distinct().ToList();
    }
}
