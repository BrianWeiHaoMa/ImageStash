using ImageStash.Files;
using ImageStash.Media;
using ImageStash.Store;
using ImageStash.Window;

namespace ImageStash.Tests.Support;

/// <summary>
/// Open with… sessions in a <see cref="TestStash"/>: begun, classified and adopted through the store, as the window
/// will; the session and the adoption's undo record a row records, read through a second connection; the folders and
/// files of the stash's open-with root; and a copy rewritten as an outside program saving it in place would.
/// </summary>
public static class OpenedWith
{
    /// <summary>
    /// How long a wait for what a rewrite brings about allows: its classification and adoption are store calls, which in a
    /// whole parallel run on the development PC took up to 4.6 s between them in runs that passed, at the five seconds an
    /// ordinary wait allows.
    /// </summary>
    public static readonly TimeSpan RewriteAnswered = TimeSpan.FromSeconds(30);

    /// <summary>Begins a session on <paramref name="item"/>, rewrites its copy with <paramref name="rewrite"/> and requires it to await adoption; the copy's path.</summary>
    public static async Task<string> AwaitingRewrite(TestStash stash, StashItem item, byte[] rewrite)
    {
        var copy = await Begin(stash, item);
        Rewrite(copy, rewrite);
        Assert.AreEqual(OpenWithCopy.AwaitingAdoption, await Classify(stash, item.Id));
        return copy;
    }

    /// <summary>An open stash holding "cat", imported from still.png, its copy rewritten with <paramref name="rewrite"/> and awaiting adoption.</summary>
    public static async Task<(StashItem Cat, string Copy)> AwaitingCat(TestStash stash, byte[] rewrite)
    {
        await stash.OpenCleanly();
        var cat = await stash.ImportFixture("cat", "still.png");
        return (cat, await AwaitingRewrite(stash, cat, rewrite));
    }

    /// <summary>Adopts the item's rewrite and requires the item as the stash lists it then.</summary>
    public static async Task<StashItem> Adopt(TestStash stash, long id)
    {
        var adopted = await stash.Store!.AdoptRewrite(id);
        Assert.IsTrue(adopted.Succeeded, adopted.Failure?.ToString());
        return adopted.Value!;
    }

    /// <summary>
    /// An open stash holding "dog" (<see cref="Bystander"/>) and then "cat", imported from still.png and, when
    /// <paramref name="edited"/>, saved with <see cref="EditedItems.BlackBoxAndBox"/>, with <paramref name="rewrite"/>
    /// adopted from its Open with… copy: cat as listed before the adoption and after it, and the copy's path.
    /// </summary>
    public static async Task<(StashItem Before, StashItem Adopted, string Copy)> AdoptedCat(TestStash stash, byte[] rewrite, bool edited)
    {
        await stash.OpenCleanly();
        await Bystander(stash);
        var cat = await stash.ImportFixture("cat", "still.png");
        if (edited)
        {
            var saved = await stash.SaveEdit(cat, EditedItems.BlackBoxAndBox);
            Assert.IsTrue(saved.Succeeded, saved.Failure?.ToString());
            cat = saved.Value!;
        }

        var copy = await AwaitingRewrite(stash, cat, rewrite);
        return (cat, await Adopt(stash, cat.Id), copy);
    }

    /// <summary>
    /// Open with… chosen from the item menu on <paramref name="item"/>, its copy rewritten with <paramref name="rewrite"/>
    /// as an outside program saving it would, and the adoption's notification above the tray once the window has said it.
    /// </summary>
    public static async Task<Notice> ReplacedThroughTheMenu(ListedStash listed, StashItem item, byte[] rewrite)
    {
        var chosen = await listed.ItemMenu.Act(ItemMenuAction.OpenWith, null!, [item], null, () => Assert.Fail("Open with… hides nothing"), _ => { });
        Assert.IsTrue(chosen.Succeeded, chosen.Failure?.ToString());
        await RewriteWhenFree(listed.Dialogs.OpenedWith.Last(), rewrite);
        await Eventually(listed, () => listed.Notifications.Shown.Any(notice => notice.Text.StartsWith("Replaced ")));
        await Handing.Eventually(() => !listed.OpenWith.Running, RewriteAnswered);
        return listed.Notifications.Shown.Single(notice => notice.Text.StartsWith("Replaced "));
    }

    /// <summary>
    /// Waits, as <see cref="Handing.Eventually"/> does but for <see cref="RewriteAnswered"/>, for <paramref name="condition"/>
    /// after a rewrite of a copy the window watches, moving the window's asking clock on to each once-a-second asking it
    /// arms meanwhile, as time passing would: a scanner holding the freshly rewritten copy past the store's retry (E3)
    /// leaves its classification to that asking (State: Open with…), which this clock passes only when told. The clock
    /// moves only once the window has armed it, so a check that found nothing armed before the rewrite still shows that
    /// the watcher asked first.
    /// </summary>
    public static Task Eventually(ListedStash listed, Func<bool> condition) => Handing.Eventually(() =>
    {
        if (condition())
        {
            return true;
        }

        if (listed.OpenWithClock.NextDue is not null)
        {
            listed.OpenWithClock.Advance(OpenWith.AskedEvery);
        }

        return condition();
    }, RewriteAnswered);

    /// <summary>Undoes the adoption <paramref name="revision"/> names and requires the item as the stash lists it then.</summary>
    public static async Task<StashItem> UndoAdoption(TestStash stash, long id, long revision)
    {
        var undone = await stash.Store!.UndoAdoption(id, revision);
        Assert.IsTrue(undone.Succeeded, undone.Failure?.ToString());
        Assert.IsNotNull(undone.Value, "nothing was undone");
        return undone.Value;
    }

    /// <summary>"dog", from transparent.png, with a tag and a use, imported into the open stash: an item no change of another may touch.</summary>
    public static async Task<StashItem> Bystander(TestStash stash)
    {
        var dog = await stash.ImportFixture("dog", "transparent.png");
        stash.Tag(dog.Id, "mood");
        Assert.IsEmpty((await stash.Store!.RecordUses([dog], DateTimeOffset.FromUnixTimeMilliseconds(1_000))).Value!);
        return dog;
    }

    /// <summary>
    /// The row, tags and uses of the item of <paramref name="id"/> and the stash's files whose names start with
    /// <paramref name="stem"/>, read between the store's calls or with it closed: what a change of another item leaves.
    /// </summary>
    public static string Untouched(TestStash stash, long id, string stem) =>
        string.Join(
            "\n",
            [
                WholeRow(stash, id),
                .. stash.TagRows().Where(tag => tag.Item == id).Select(tag => tag.Tag),
                .. stash.SqlQueryWhileOpen("SELECT used_at FROM uses WHERE item_id = $id", r => r.GetInt64(0).ToString(), ("$id", id)),
                .. EditedItems.FilesWithHashes(stash).Where(line => line.StartsWith(stem)),
            ]);

    /// <summary>The row of <paramref name="id"/> as <see cref="WholeRow"/> reads it, without <paramref name="columns"/>.</summary>
    public static string WholeRowBut(TestStash stash, long id, params string[] columns) =>
        string.Join(" | ", WholeRow(stash, id).Split(" | ").Where(column => !columns.Any(left => column.StartsWith(left + "="))));

    /// <summary>Whether the file at <paramref name="path"/> holds <paramref name="bytes"/>: a Recycle Bin's choice of what to refuse.</summary>
    public static bool Holds(string path, byte[] bytes) => File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes);

    /// <summary>
    /// The bytes of every file in the test Recycle Bin, as hashes; a file a scanner holds for a moment as it arrives (E3)
    /// is read once let go, as the product retries a held file.
    /// </summary>
    public static List<string> Recycled(TestStash stash) =>
        Directory.Exists(stash.RecycleBin.Folder) ? [.. Directory.GetFiles(stash.RecycleBin.Folder).Select(HashOnceLetGo).Order()] : [];

    private static string HashOnceLetGo(string path)
    {
        var hashed = HeldFileRetry.Run("Read the test Recycle Bin", path, HeldFileRetry.Duration, () => EditedItems.Hash(path));
        Assert.IsTrue(hashed.Succeeded, hashed.Failure?.Sentence);
        return hashed.Value!;
    }

    /// <summary>The adoption's undo record the row of <paramref name="id"/> holds, or null, read between the store's calls or with it closed.</summary>
    internal static AdoptionUndo? Undo(TestStash stash, long id) =>
        stash.SqlQueryWhileOpen("SELECT adoption_undo FROM items WHERE id = $id", r => r.IsDBNull(0) ? null : AdoptionUndo.Decode(r.GetString(0)), ("$id", id)).Single();

    /// <summary>The row's <c>original_sha256</c> as upper-case hex, read between the store's calls or with it closed.</summary>
    public static string OriginalHash(TestStash stash, long id) =>
        stash.SqlQueryWhileOpen("SELECT original_sha256 FROM items WHERE id = $id", r => Convert.ToHexString(r.GetFieldValue<byte[]>(0)), ("$id", id)).Single();

    /// <summary>Every column of the row of <paramref name="id"/> as text, to tell that nothing of it changed; read between the store's calls or with it closed.</summary>
    public static string WholeRow(TestStash stash, long id) =>
        stash.SqlQueryWhileOpen(
            "SELECT * FROM items WHERE id = $id",
            r => string.Join(" | ", Enumerable.Range(0, r.FieldCount).Select(i => $"{r.GetName(i)}={(r.IsDBNull(i) ? "NULL" : r.GetValue(i) is byte[] bytes ? Convert.ToHexString(bytes) : r.GetValue(i))}")),
            ("$id", id)).Single();

    /// <summary>
    /// A reader that reads as the product does and then holds the file it read with Read sharing, as a scanner may (E3),
    /// so an adoption's place of that file is refused until the check disposes what it added to <paramref name="holds"/>.
    /// </summary>
    public static PictureReader Holding(List<FileStream> holds) => (path, name, retry) =>
    {
        var read = MediaDecoder.ReadPicture(path, name, retry);
        holds.Add(TestStash.Hold(path));
        return read;
    };

    /// <summary>Begins a session on <paramref name="item"/> and requires its copy's path.</summary>
    public static async Task<string> Begin(TestStash stash, StashItem item)
    {
        var begun = await stash.Store!.BeginOpenWith(item);
        Assert.IsTrue(begun.Succeeded, begun.Failure?.ToString());
        return begun.Value!;
    }

    /// <summary>Classifies the item's copy and requires an answer.</summary>
    public static async Task<OpenWithCopy> Classify(TestStash stash, long id)
    {
        var classified = await stash.Store!.ClassifyOpenWith(id);
        Assert.IsTrue(classified.Succeeded, classified.Failure?.ToString());
        return classified.Value;
    }

    /// <summary>The session the row of <paramref name="id"/> records, or null, read between the store's calls or with it closed.</summary>
    internal static OpenWithRecord? Record(TestStash stash, long id) =>
        stash.SqlQueryWhileOpen("SELECT open_with_session FROM items WHERE id = $id", r => r.IsDBNull(0) ? null : OpenWithRecord.Decode(r.GetString(0)), ("$id", id)).Single();

    /// <summary>The session folder that holds <paramref name="copy"/>, by name.</summary>
    public static string FolderOf(string copy) => Path.GetFileName(Path.GetDirectoryName(copy)!);

    /// <summary>The folders directly under the root, by name, in order.</summary>
    public static string[] Folders(TestStash stash) => Directory.GetDirectories(stash.OpenWithRoot!).Select(Path.GetFileName).Order().ToArray()!;

    /// <summary>The files directly in the root, by name.</summary>
    public static string[] RootFiles(TestStash stash) => Directory.GetFiles(stash.OpenWithRoot!).Select(Path.GetFileName).ToArray()!;

    /// <summary>Writes <paramref name="bytes"/> over the copy, as an outside program saving in place does.</summary>
    public static void Rewrite(string copy, byte[] bytes) => File.WriteAllBytes(copy, bytes);

    /// <summary>
    /// <see cref="Rewrite"/> while a window watches the copy: tried again for up to five seconds while the copy is held,
    /// as it is for a moment each time the window has it read without sharing, as a program saving again would. What the
    /// rewrite brings about is waited for with <see cref="Eventually"/>.
    /// </summary>
    public static async Task RewriteWhenFree(string copy, byte[] bytes)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (true)
        {
            try
            {
                Rewrite(copy, bytes);
                return;
            }
            catch (IOException e) when (e.HResult == SharingViolation && DateTime.UtcNow < deadline)
            {
                await Task.Delay(10);
            }
        }
    }

    /// <summary>
    /// Deletes the session's folder with its copy, as another program might behind ImageStash's back, tried again for up
    /// to five seconds while a window's classification holds the copy for a moment.
    /// </summary>
    public static async Task DeleteFolderWhenFree(string folder)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (true)
        {
            try
            {
                Directory.Delete(folder, recursive: true);
                return;
            }
            catch (IOException e) when (e.HResult == SharingViolation && DateTime.UtcNow < deadline)
            {
                await Task.Delay(10);
            }
        }
    }

    private const int SharingViolation = unchecked((int)0x80070020);

    /// <summary>200 bytes that are no picture.</summary>
    public static byte[] Garbage(int seed = 1) => TestStash.Bytes(seed, 200);
}
