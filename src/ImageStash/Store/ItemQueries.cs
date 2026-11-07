using ImageStash.Layers;
using ImageStash.Media;

namespace ImageStash.Store;

/// <summary>How many uses an item has recorded and when the latest was, in UTC; 0 and null while it has none (G19, G10).</summary>
public sealed record UseHistory(int Count, DateTimeOffset? Latest);

/// <summary>
/// What Edit opens on: the item as the stash lists it; its original, where it is now, to be opened read-only (R3), with
/// the original's record; the committed layers; the layers the picture on disk was rendered from, which differ while a
/// Save has not caught up (State: A live failure); the format the finished picture is rendered in (F3); and the
/// original's content hash, which tells the editor's reads of the path whether the file there is still the original.
/// </summary>
public sealed record ItemForEdit(StashItem Item, string OriginalPath, MediaFacts OriginalFacts, LayerList Layers, LayerList RenderedLayers, ImageFormat FinishedFormat, byte[] OriginalSha256);

/// <summary>
/// The scopes and the search in G4's and G5's orders, one item by its identity, a handoff's items confirmed by identity
/// (G8), and the use history Recent is ordered by (G10). Each query is a fixed number of statements over the whole
/// stash, whatever its size (E4); the confirmation, two for each item handed in, each by its identity.
/// </summary>
internal static class ItemQueries
{
    private const string NewestAdded = "ORDER BY i.added_at DESC, i.id DESC";

    /// <summary>Every item, newest-added first; equal times put the later identity first.</summary>
    public static Outcome<IReadOnlyList<StashItem>> All(StoreContext store) =>
        Listed(store, "List the stash", $"SELECT {StashItem.Columns} FROM items i WHERE {StashItem.NotDeleted} {NewestAdded}");

    /// <summary>Every favorite, newest-added first, as All orders them (G4).</summary>
    public static Outcome<IReadOnlyList<StashItem>> Favorites(StoreContext store) =>
        Listed(store, "List favorites", $"SELECT {StashItem.Columns} FROM items i WHERE {StashItem.NotDeleted} AND i.favorite = 1 {NewestAdded}");

    /// <summary>Every item with at least one use, by its latest use, newest first; equal times put the later identity first.</summary>
    public static Outcome<IReadOnlyList<StashItem>> Recent(StoreContext store) =>
        Listed(store, "List recent items", $"""
            SELECT {StashItem.Columns} FROM items i JOIN uses u ON u.item_id = i.id
            WHERE {StashItem.NotDeleted} GROUP BY i.id ORDER BY MAX(u.used_at) DESC, i.id DESC
            """);

    /// <summary>The items matching every word of <paramref name="query"/>, newest-added first; with no words, every item.</summary>
    public static Outcome<IReadOnlyList<StashItem>> Search(StoreContext store, string query)
    {
        var words = ItemSearch.Words(query);
        var all = All(store);
        if (!all.Succeeded)
        {
            return all.Failure;
        }

        return Outcome<IReadOnlyList<StashItem>>.Of(all.Value!.Where(item => ItemSearch.Matches(item, words)).ToList());
    }

    /// <summary>
    /// The item of identity <paramref name="id"/> as <see cref="All"/> lists it, its file where it is; null when the
    /// stash does not list it (gone, marked deleted, or waiting on a step the next start must finish).
    /// </summary>
    public static Outcome<StashItem?> Item(StoreContext store, long id) =>
        store.Read("Read an item", () =>
        {
            var tags = store.Query("SELECT tag FROM tags WHERE item_id = $id ORDER BY rowid", r => r.GetString(0), ("$id", id));
            return store.Query(
                $"SELECT {StashItem.Columns} FROM items i WHERE {StashItem.NotDeleted} AND i.id = $id",
                r => StashItem.ListedFromRow(r, store, tags),
                ("$id", id)).SingleOrDefault();
        });

    /// <summary>
    /// What Edit opens on the item of identity <paramref name="id"/>, read as <see cref="Item"/> reads it; null when the
    /// stash does not list it. A list the store holds that the layer module cannot read is the failure it names, and the
    /// item stays listed: Edit alone is refused (G35). Opens no file.
    /// </summary>
    public static Outcome<ItemForEdit?> ForEdit(StoreContext store, long id)
    {
        var read = store.Read("Read an item", () =>
        {
            var tags = store.Query("SELECT tag FROM tags WHERE item_id = $id ORDER BY rowid", r => r.GetString(0), ("$id", id));
            return store.Query(
                $"SELECT {StashItem.Columns}, {StoredFiles.Columns}, i.layers, i.previous_layers, i.layer_revision, i.original_sha256 FROM items i WHERE {StashItem.NotDeleted} AND i.id = $id",
                r =>
                {
                    if (StashItem.ListedFromRow(r, store, tags) is not { } item)
                    {
                        return null;
                    }

                    var files = StoredFiles.FromRow(r, StashItem.ColumnCount);
                    // An adopted file that has landed is the original, and so is the previous original once an Undo's picture
                    // has: its step describes it until the last transaction, the columns still the outgoing original's.
                    var adopted = AdoptionSteps.OriginalOnDisk(store.Folder, files.Steps);
                    return new EditRow(
                        item,
                        files.OriginalPath(store.Folder),
                        adopted?.Facts ?? StashItem.OriginalFacts(r),
                        r.GetString(EditRow.LayersColumn),
                        r.IsDBNull(EditRow.LayersColumn + 1) ? null : r.GetString(EditRow.LayersColumn + 1),
                        r.GetInt64(EditRow.LayersColumn + 2),
                        adopted is null ? r.GetFieldValue<byte[]>(EditRow.LayersColumn + 3) : AdoptionSteps.Sha256Of(adopted));
                },
                ("$id", id)).SingleOrDefault();
        });
        if (!read.Succeeded)
        {
            return read.Failure;
        }

        if (read.Value is not { } row)
        {
            return Outcome<ItemForEdit?>.Of(null);
        }

        var subject = Named(row.Item.Name, row.Item.FileName);
        var layers = LayerList.FromJson(row.Layers, subject);
        if (!layers.Succeeded)
        {
            return layers.Failure;
        }

        // Until the rendered revision catches up, the picture on disk is the one the previous list rendered.
        var rendered = row.Item.RenderedRevision < row.LayerRevision ? LayerList.FromJson(row.PreviousLayers, subject) : layers;
        if (!rendered.Succeeded)
        {
            return rendered.Failure;
        }

        return Outcome<ItemForEdit?>.Of(new ItemForEdit(row.Item, row.OriginalPath, row.OriginalFacts, layers.Value!, rendered.Value!, FinishedFormat.Of(row.OriginalFacts), row.OriginalSha256));
    }

    /// <summary>
    /// The items a caller holds, confirmed by identity in one transaction, since a file name never is one (G8): one
    /// outcome per item, in order, a repeated identity answered as its first. An item the stash lists comes back as it
    /// lists it now, its file where it is; one gone, marked deleted, or waiting on a step the next start must finish is
    /// refused as no longer in the stash, named as <see cref="ItemFactChange.Apply"/> names it. When the store cannot be
    /// read, whatever the reason, an item read since the store's latest commit, and, when its row had steps pending, since
    /// its latest file step that moved a file of the root or <c>originals\</c> (<see cref="StoreContext.FileSteps"/>), comes
    /// back as handed in, its file where it was read; any other is refused naming the item and why the store could not be
    /// read (G35), never guessed at. The file names of the items answered are put in hand in the same call, so none can
    /// come to another item until the caller, its handoff done, disposes the confirmation.
    /// </summary>
    public static Confirmation Confirm(StoreContext store, string action, IReadOnlyList<StashItem> items)
    {
        // Not as handed in either: once a Move copies, the files are about to be another folder's (State: Move); once an
        // import has spent the store, they are the replaced folder's (State: Import stash…).
        if (store.MoveStage != MoveStage.None || store.ImportSpent)
        {
            return new Confirmation([.. items.Select(item => Outcome<StashItem>.Fail(store.MoveRefusal(action, Named(item.Name, item.FileName))!))], store.InHand.Hold([]));
        }

        var firsts = items.DistinctBy(item => item.Id).ToList();
        var read = store.Read(action, () =>
        {
            var answers = new Dictionary<long, Outcome<StashItem>>();
            foreach (var item in firsts)
            {
                var tags = store.Query("SELECT tag FROM tags WHERE item_id = $id ORDER BY rowid", r => r.GetString(0), ("$id", item.Id));
                var rows = store.Query(
                    $"SELECT {StashItem.Columns}, i.deleted FROM items i WHERE i.id = $id",
                    r => (Listed: r.GetBoolean(StashItem.ColumnCount) ? null : StashItem.ListedFromRow(r, store, tags), Name: r.GetString(1), FinishedFile: r.GetString(4)),
                    ("$id", item.Id));
                answers[item.Id] = rows switch
                {
                    [{ Listed: { } now }] => Outcome<StashItem>.Of(now),
                    [var row] => Outcome<StashItem>.Fail(new Failure(action, Named(row.Name, row.FinishedFile), ItemFactChange.NotInStash)),
                    // A row that is gone holds no name any more, so the item is named as the caller was shown it (G35).
                    _ => Outcome<StashItem>.Fail(new Failure(action, Named(item.Name, item.FileName), ItemFactChange.NotInStash)),
                };
            }

            return answers;
        });
        var confirmed = read.Succeeded ? read.Value! : AsHandedIn(store, action, firsts, read.Failure);
        var held = store.InHand.Hold(confirmed.Values.Where(answer => answer.Succeeded).Select(answer => answer.Value!));
        return new Confirmation(items.Select(item => confirmed[item.Id]).ToList(), held);
    }

    /// <summary>Why an action on an item failed while the store could not be read, as <paramref name="unread"/> says it (G35).</summary>
    public static string Unreadable(Failure unread) => $"the stash could not be read ({unread.Subject}: {unread.Reason})";

    private static Dictionary<long, Outcome<StashItem>> AsHandedIn(StoreContext store, string action, IReadOnlyList<StashItem> firsts, Failure unread)
    {
        var unreadable = Unreadable(unread);
        return firsts.ToDictionary(item => item.Id, item => item.CommitsWhenRead == store.Commits && (!item.StepsPendingWhenRead || item.FileStepsWhenRead == store.FileSteps)
            ? Outcome<StashItem>.Of(item)
            : Outcome<StashItem>.Fail(new Failure(action, Named(item.Name, item.FileName), unreadable)));
    }

    /// <summary>The uses recorded on the item of identity <paramref name="id"/>, in one statement over its own uses (E4).</summary>
    public static Outcome<UseHistory> UseHistory(StoreContext store, long id) =>
        store.Read("Read an item's uses", () => store.Query(
            "SELECT COUNT(*), MAX(used_at) FROM uses WHERE item_id = $id",
            r => new UseHistory(r.GetInt32(0), r.IsDBNull(1) ? null : DateTimeOffset.FromUnixTimeMilliseconds(r.GetInt64(1))),
            ("$id", id)).Single());

    /// <summary>
    /// Records one use at <paramref name="at"/> on each item, and nothing else (G10): on an item a Delete holds while its
    /// notice shows too, so its Undo brings it back with the use (G18). Answers the items no longer in the stash at all,
    /// which get none, for the caller to name (the store has no name for a row that is gone); the others keep theirs.
    /// </summary>
    public static Outcome<IReadOnlyList<StashItem>> RecordUses(StoreContext store, IReadOnlyList<StashItem> items, DateTimeOffset at)
    {
        var gone = new List<StashItem>();
        var recorded = store.Change("Record a use", () =>
        {
            // A transaction retried whole counts again.
            gone.Clear();
            foreach (var item in items.DistinctBy(item => item.Id))
            {
                if (store.Query("SELECT 1 FROM items WHERE id = $id", _ => true, ("$id", item.Id)).Count == 0)
                {
                    gone.Add(item);
                    continue;
                }

                store.Execute("INSERT INTO uses (item_id, used_at) VALUES ($id, $at)", ("$id", item.Id), ("$at", at.ToUnixTimeMilliseconds()));
            }
        });

        return recorded.Succeeded ? Outcome<IReadOnlyList<StashItem>>.Of(gone) : recorded.Failure;
    }

    private static Outcome<IReadOnlyList<StashItem>> Listed(StoreContext store, string action, string sql)
    {
        var items = store.Read(action, () => StashItem.Load(store, sql));
        return items.Succeeded ? Outcome<IReadOnlyList<StashItem>>.Of(items.Value!) : items.Failure;
    }

    /// <summary>
    /// After a change's commit: the item as the stash lists it now, its file where it is, or as committed when the store
    /// cannot be read; with the failure that stopped a later step, or that read's, if any.
    /// </summary>
    public static Outcome<StashItem> AfterCommit(StoreContext store, StashItem asCommitted, Failure? failure)
    {
        var read = Item(store, asCommitted.Id);
        var recorded = read.Succeeded && read.Value is { } listed ? listed : asCommitted;
        var named = failure ?? (read.Succeeded ? null : read.Failure);
        return named is null ? Outcome<StashItem>.Of(recorded) : Outcome<StashItem>.FailAfterCommit(named, recorded);
    }

    /// <summary>The name a failure gives an item: its name, or its file's when it has none (G35, G17).</summary>
    public static string Named(string name, string fileName) => name.Length > 0 ? name : fileName;

    /// <summary>A row <see cref="ForEdit"/> read, its layer lists still as the store holds them.</summary>
    private sealed record EditRow(StashItem Item, string OriginalPath, MediaFacts OriginalFacts, string Layers, string? PreviousLayers, long LayerRevision, byte[] OriginalSha256)
    {
        /// <summary>Where <c>i.layers</c> is in <see cref="ForEdit"/>'s query, after the item's and the files' columns.</summary>
        public const int LayersColumn = StashItem.ColumnCount + StoredFiles.ColumnCount;
    }
}
