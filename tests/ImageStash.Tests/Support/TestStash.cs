using System.Security.Cryptography;
using ImageStash.Layers;
using ImageStash.Media;
using ImageStash.Store;
using Microsoft.Data.Sqlite;

namespace ImageStash.Tests.Support;

/// <summary>
/// A stash under a fresh %TEMP% folder for one check, with a test Recycle Bin beside it and a clock the check sets.
/// <see cref="Sql"/> reaches the store file directly while the store is closed, to build the on-disk states a kill or a
/// live failure leaves.
/// </summary>
public sealed class TestStash : IDisposable
{
    public static readonly TimeSpan ShortRetry = TimeSpan.FromMilliseconds(300);

    public static readonly MediaFacts StillPng = new(ImageFormat.Png, 8, 6, false, 1, 0);

    private readonly ScratchFolder scratch = new();

    public TestStash()
    {
        Folder = new StashFolder(System.IO.Path.Combine(scratch.Path, "stash"));
        RecycleBin = new TestRecycleBin(System.IO.Path.Combine(scratch.Path, "recycled"));
        OpenWithRoot = System.IO.Path.Combine(scratch.Path, "open-with");
    }

    public StashFolder Folder { get; }

    public TestRecycleBin RecycleBin { get; }

    public SetClock Clock { get; } = new();

    public TimeSpan Retry { get; set; } = ShortRetry;

    public StashStore? Store { get; private set; }

    /// <summary>
    /// What the store renders a finished picture again with, at start or when a change finishes a pending Save; a check
    /// puts one that fails or holds what it wrote in its place, before or after the store opens.
    /// </summary>
    public FinishedPictureRenderer Renderer { get; set; } = FinishedPicture.Render;

    /// <summary>Every call the store made of <see cref="Renderer"/>, in order: the original's path, the layers and the format.</summary>
    public List<(string OriginalPath, LayerList Layers, ImageFormat Format)> RenderCalls { get; } = [];

    /// <summary>Where the store keeps its Open with… copies, beside the stash under the scratch folder; null opens a stash that keeps none.</summary>
    public string? OpenWithRoot { get; set; }

    /// <summary>What the store reads an Open with… copy with; a check puts another in its place, before or after the store opens.</summary>
    public PictureReader Reader { get; set; } = MediaDecoder.ReadPicture;

    /// <summary>Every name the store asked <see cref="Reader"/> to read under, in order, from whichever thread asked.</summary>
    public List<string> ReadCalls { get; } = [];

    /// <summary>A path outside the stash, for a source file.</summary>
    public string Outside(string name) => System.IO.Path.Combine(scratch.Path, name);

    public Task<StoreOpening> Open() => OpenAt(Folder.Root);

    /// <summary>
    /// Opens the stash at <paramref name="folder"/> as <see cref="Open"/> opens its own, with the same Recycle Bin,
    /// renderer, Open with… root and reader, and holds it as <see cref="Store"/>: the folder a Move switched to.
    /// </summary>
    public async Task<StoreOpening> OpenAt(string folder)
    {
        var opening = await StashStore.Open(
            folder,
            RecycleBin,
            Retry,
            Clock,
            (original, name, layers, format, work, retry) =>
            {
                RenderCalls.Add((original, layers, format));
                return Renderer(original, name, layers, format, work, retry);
            },
            OpenWithRoot,
            (path, name, retry) =>
            {
                lock (ReadCalls)
                {
                    ReadCalls.Add(name);
                }

                return Reader(path, name, retry);
            });
        Store = opening.Store;
        return opening;
    }

    /// <summary>Opens the stash and requires it to open with nothing named.</summary>
    public async Task<StashStore> OpenCleanly()
    {
        var opening = await Open();
        Assert.IsNotNull(opening.Store, string.Join("; ", opening.Failures));
        Assert.IsEmpty(opening.Failures, string.Join("; ", opening.Failures));
        return opening.Store;
    }

    public void Close()
    {
        Store?.Dispose();
        Store = null;
    }

    public async Task<StoreOpening> Reopen()
    {
        Close();
        return await Open();
    }

    /// <summary>Bytes that differ from every other seed's.</summary>
    public static byte[] Bytes(int seed, int length = 64)
    {
        var bytes = new byte[length];
        new Random(seed).NextBytes(bytes);
        return bytes;
    }

    public string Source(string name, byte[] bytes)
    {
        var path = Outside(System.IO.Path.Combine("sources", name));
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    /// <summary>Stages a source file holding <paramref name="bytes"/> into the open stash.</summary>
    public async Task<string> Stage(string fileName, byte[] bytes)
    {
        var staged = await Store!.Stage(Source(fileName, bytes));
        Assert.IsTrue(staged.Succeeded, staged.Failure?.ToString());
        return staged.Value!;
    }

    public static ImportCandidate Candidate(string stagedPath, string name, byte[] bytes, MediaFacts? facts = null) =>
        new(stagedPath, name, facts ?? StillPng, SHA256.HashData(bytes));

    /// <summary>
    /// Imports an item named <paramref name="name"/> from a file with <paramref name="extension"/>, of the format that
    /// extension names, and requires it to be added. Without <paramref name="bytes"/> each import's bytes are new. The
    /// source has a plain name of its own, since an item's name may hold what a file name cannot.
    /// </summary>
    public async Task<StashItem> Import(string name, byte[]? bytes = null, string extension = ".png")
    {
        bytes ??= Guid.NewGuid().ToByteArray();
        var format = extension switch { ".gif" => ImageFormat.Gif, ".jpg" or ".jpeg" => ImageFormat.Jpeg, ".webp" => ImageFormat.WebP, _ => ImageFormat.Png };
        var staged = await Stage("source" + extension, bytes);
        var committed = await Store!.CommitImport(Candidate(staged, name, bytes, StillPng with { Format = format }));
        Assert.IsTrue(committed.Succeeded, committed.Failure?.ToString());
        Assert.IsFalse(committed.Value!.AlreadyInStash, $"{name} is already in the stash");
        return committed.Value.Item;
    }

    /// <summary>
    /// Imports the fixture <paramref name="fixture"/> as an item named <paramref name="name"/>, as the product's import
    /// does: staged under its own extension, with the facts and the hash Media reads from it.
    /// </summary>
    public Task<StashItem> ImportFixture(string name, string fixture) => ImportPicture(name, fixture, MediaFixtures.Bytes(fixture));

    /// <summary>
    /// Imports <paramref name="bytes"/>, a picture a check made, as an item named <paramref name="name"/>, as the product's
    /// import does: staged under <paramref name="fileName"/>'s extension, with the facts and the hash Media reads from it.
    /// </summary>
    public async Task<StashItem> ImportPicture(string name, string fileName, byte[] bytes)
    {
        var committed = await ImportOrFind(name, fileName, bytes);
        Assert.IsFalse(committed.AlreadyInStash, $"{name} is already in the stash");
        return committed.Item;
    }

    /// <summary>Imports <paramref name="bytes"/> as <see cref="ImportPicture"/> does, answering the item added or the one already in the stash (G12).</summary>
    public async Task<ImportResult> ImportOrFind(string name, string fileName, byte[] bytes)
    {
        var staged = await Stage("source" + System.IO.Path.GetExtension(fileName), bytes);
        var read = MediaDecoder.ReadFacts(staged, fileName, ShortRetry);
        Assert.IsTrue(read.Succeeded, read.Failure?.ToString());
        var committed = await Store!.CommitImport(new ImportCandidate(staged, name, read.Value!.Facts, read.Value.Sha256));
        Assert.IsTrue(committed.Succeeded, committed.Failure?.ToString());
        return committed.Value!;
    }

    /// <summary>
    /// Renders <paramref name="layers"/> over the item's original into a fresh <c>.work\</c> path in the format Edit is
    /// given, through <see cref="FinishedPicture.Render"/> as the editor will, not through <see cref="Renderer"/>.
    /// </summary>
    public async Task<RenderedPicture> Render(StashItem item, LayerList layers)
    {
        var edit = (await Store!.ForEdit(item.Id)).Value;
        Assert.IsNotNull(edit, $"{item.Name} is not listed");
        var path = Store.AllocateWorkPath(FinishedFormat.Extension(edit.FinishedFormat)).Value!;
        var rendered = FinishedPicture.Render(edit.OriginalPath, item.Name, layers, edit.FinishedFormat, path, ShortRetry);
        Assert.IsTrue(rendered.Succeeded, rendered.Failure?.ToString());
        return new RenderedPicture(path, rendered.Value!.Facts, rendered.Value.OriginalSha256);
    }

    /// <summary>Renders <paramref name="layers"/> over the item as <see cref="Render"/> does and saves them.</summary>
    public async Task<Outcome<StashItem>> SaveEdit(StashItem item, LayerList layers) => await Store!.Save(item, layers, await Render(item, layers));

    /// <summary>Gives the item a tag, through a second connection between the store's calls.</summary>
    public void Tag(long id, string tag) =>
        SqlWhileOpen("INSERT INTO tags (item_id, tag) VALUES ($id, $tag)", ("$id", id), ("$tag", tag));

    /// <summary>Every row of <c>tags</c>, marked rows' included, by item and then by tag, read between the store's calls.</summary>
    public List<(long Item, string Tag)> TagRows() =>
        SqlQueryWhileOpen("SELECT item_id, tag FROM tags ORDER BY item_id, tag", r => (r.GetInt64(0), r.GetString(1)));

    /// <summary>
    /// Puts <paramref name="count"/> items straight into the closed store in one transaction, each a distinct small PNG
    /// of one of a few shapes written into the stash folder under its name, with its real hash, named from a few
    /// reaction words and added a second apart, every ninth tagged "mood": the stash then opens and works as one that
    /// imports filled, in a fraction of the time.
    /// </summary>
    public void AddPictures(int count)
    {
        Assert.IsNull(Store, "close the store before reaching its file directly");
        string[] words = ["cat", "dance", "lab coat", "abs", "nope", "wow", "yes", "facepalm", "shrug", "clap"];
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Folder.StoreFile, Pooling = false }.ToString());
        connection.Open();
        using var transaction = connection.BeginTransaction();
        using var item = connection.CreateCommand();
        item.Transaction = transaction;
        item.CommandText =
            """
            INSERT INTO items (name, added_at, format, width, height, animated, frame_count, duration_ms, original_file, finished_file, original_sha256)
            VALUES ($name, $added, 'Png', $width, $height, 0, 1, 0, $file, $file, $sha256) RETURNING id
            """;
        using var tag = connection.CreateCommand();
        tag.Transaction = transaction;
        tag.CommandText = "INSERT INTO tags (item_id, tag) VALUES ($id, 'mood')";
        var start = Clock.Now.ToUnixTimeMilliseconds();
        for (var i = 1; i <= count; i++)
        {
            var (width, height) = (8 + i % 7 * 4, 6 + i % 5 * 3);
            var bytes = MediaFixtures.DistinctPng(i, width, height);
            var name = $"{words[i % words.Length]} {i}";
            File.WriteAllBytes(System.IO.Path.Combine(Folder.Root, name + ".png"), bytes);
            item.Parameters.Clear();
            item.Parameters.AddWithValue("$name", name);
            item.Parameters.AddWithValue("$added", start + i * 1000L);
            item.Parameters.AddWithValue("$width", width);
            item.Parameters.AddWithValue("$height", height);
            item.Parameters.AddWithValue("$file", name + ".png");
            item.Parameters.AddWithValue("$sha256", SHA256.HashData(bytes));
            var id = (long)item.ExecuteScalar()!;
            if (i % 9 == 0)
            {
                tag.Parameters.Clear();
                tag.Parameters.AddWithValue("$id", id);
                tag.ExecuteNonQuery();
            }
        }

        transaction.Commit();
    }

    /// <summary>
    /// Makes <paramref name="item"/> an edited item on the closed store, as a Save leaves one: its file moved under
    /// originals\ as the original, and a finished picture holding <paramref name="finished"/> in the root under its name.
    /// </summary>
    public void MakeEdited(StashItem item, byte[] finished)
    {
        Assert.IsNull(Store, "close the store before reaching its file directly");
        File.Move(item.FilePath, Folder.InOriginals(item.FileName));
        File.WriteAllBytes(item.FilePath, finished);
        Sql("UPDATE items SET original_in_originals = 1 WHERE id = $id", ("$id", item.Id));
    }

    /// <summary>Copies the stash folder, its store included, into <paramref name="folder"/>, which must be new or empty, while the store is closed.</summary>
    public void CopyTo(string folder)
    {
        Assert.IsNull(Store, "close the store before copying it");
        Assert.IsFalse(Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any(), $"{folder} is not empty");
        foreach (var directory in Directory.GetDirectories(Folder.Root, "*", SearchOption.AllDirectories).Prepend(Folder.Root))
        {
            Directory.CreateDirectory(System.IO.Path.Combine(folder, System.IO.Path.GetRelativePath(Folder.Root, directory)));
        }

        foreach (var file in Directory.GetFiles(Folder.Root, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, System.IO.Path.Combine(folder, System.IO.Path.GetRelativePath(Folder.Root, file)));
        }
    }

    public async Task<IReadOnlyList<StashItem>> All()
    {
        var all = await Store!.All();
        Assert.IsTrue(all.Succeeded, all.Failure?.ToString());
        return all.Value!;
    }

    /// <summary>Runs SQL against the store file with the store closed, as a kill would leave it.</summary>
    public void Sql(string sql, params (string Name, object? Value)[] parameters) => SqlQuery(sql, r => 0, parameters);

    /// <summary>
    /// Holds the open store as another program can (E3): a second connection's exclusive transaction, which keeps every
    /// read and change of the store out until the returned connection is disposed, so each is retried and then named.
    /// </summary>
    public SqliteConnection HoldStore()
    {
        var holder = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Folder.StoreFile, Pooling = false }.ToString());
        holder.Open();
        using var exclusive = holder.CreateCommand();
        exclusive.CommandText = "BEGIN EXCLUSIVE";
        exclusive.ExecuteNonQuery();
        return holder;
    }

    /// <summary>Runs SQL through a second connection while the store is open and between its calls, which hold no transaction.</summary>
    public void SqlWhileOpen(string sql, params (string Name, object? Value)[] parameters)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Folder.StoreFile, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        command.ExecuteNonQuery();
    }

    public List<T> SqlQuery<T>(string sql, Func<SqliteDataReader, T> row, params (string Name, object? Value)[] parameters)
    {
        Assert.IsNull(Store, "close the store before reaching its file directly");
        return SqlQueryWhileOpen(sql, row, parameters);
    }

    /// <summary>Reads the store through a second connection, while it is closed or between its calls, which hold no transaction.</summary>
    public List<T> SqlQueryWhileOpen<T>(string sql, Func<SqliteDataReader, T> row, params (string Name, object? Value)[] parameters)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Folder.StoreFile, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        using var reader = command.ExecuteReader();
        var rows = new List<T>();
        do
        {
            while (reader.Read())
            {
                rows.Add(row(reader));
            }
        }
        while (reader.NextResult());
        return rows;
    }

    /// <summary>
    /// Items hold their tags in a list, which a record compares by reference, so tags are compared apart; and each read
    /// carries the store's commits and file steps at that read, which any change to any item raises, and whether its row had
    /// steps pending then, so those are set aside.
    /// </summary>
    public static void AssertSameItem(StashItem expected, StashItem? actual)
    {
        Assert.IsNotNull(actual);
        CollectionAssert.AreEqual(expected.Tags.ToList(), actual.Tags.ToList());
        Assert.AreEqual(
            expected with { Tags = actual.Tags, CommitsWhenRead = actual.CommitsWhenRead, FileStepsWhenRead = actual.FileStepsWhenRead, StepsPendingWhenRead = actual.StepsPendingWhenRead },
            actual);
    }

    /// <summary>
    /// The path in .work\ of the held copy a deleted row records for its file in <paramref name="column"/>
    /// (<c>finished_file</c> or <c>original_file</c>), read through a second connection between the store's calls.
    /// </summary>
    public string HeldCopy(long id, string column = "finished_file")
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Folder.StoreFile, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT held_copies FROM items WHERE id = $id";
        command.Parameters.AddWithValue("$id", id);
        var copies = ImageStash.Store.HeldCopy.Decode((string)command.ExecuteScalar()!);
        return Folder.InWork(copies.Single(c => c.File == column).Copy);
    }

    /// <summary>In the open stash, an item gone from the store, one a Delete marked, and one listed, imported in that order.</summary>
    public async Task<(StashItem Gone, StashItem Marked, StashItem Listed)> GoneMarkedAndListed()
    {
        var gone = await Import("gone");
        var marked = await Import("marked");
        var listed = await Import("listed");
        Assert.IsTrue((await Store!.Remove([gone.Id])).Single().Succeeded);
        Assert.IsTrue((await Store!.Delete([marked.Id])).Single().Succeeded);
        return (gone, marked, listed);
    }

    /// <summary>Every file in the stash folder but the store's with a hash of its bytes, then every row of <c>uses</c>, read with the store closed.</summary>
    public List<string> FilesAndUses() =>
        [
            .. Directory.GetFiles(Folder.Root, "*", SearchOption.AllDirectories)
                .Where(path => !System.IO.Path.GetFileName(path).StartsWith(StashFolder.StoreFileName))
                .Order()
                .Select(path => $"{System.IO.Path.GetRelativePath(Folder.Root, path)} {Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))}"),
            .. SqlQuery("SELECT item_id, used_at FROM uses ORDER BY rowid", r => $"use {r.GetInt64(0)} {r.GetInt64(1)}"),
        ];

    /// <summary>The failures among <paramref name="outcomes"/>, joined, for an assertion's message.</summary>
    public static string Failures(IEnumerable<Outcome> outcomes) => string.Join("; ", outcomes.Select(o => o.Failure).OfType<Failure>());

    /// <summary>Holds a file open for reading with Read sharing only, as a scanner may (E3): it reads, and a rename of it is refused.</summary>
    public static FileStream Hold(string path) => new(path, FileMode.Open, FileAccess.Read, FileShare.Read);

    /// <summary>The first file written into <c>.work\</c>, held as <see cref="Hold"/> holds one, once its writer has let go of it.</summary>
    public async Task<FileStream> HoldTheWorkFileOnceWritten()
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (true)
        {
            foreach (var path in Directory.GetFiles(Folder.Work))
            {
                try
                {
                    return Hold(path);
                }
                catch (IOException)
                {
                    // Still being written.
                }
            }

            Assert.IsLessThan(deadline, DateTime.UtcNow, "nothing was written into .work");
            await Task.Delay(1);
        }
    }

    public string[] WorkFiles() => Directory.GetFiles(Folder.Work).Select(System.IO.Path.GetFileName).ToArray()!;

    public string[] RootFiles() => Directory.GetFiles(Folder.Root).Select(System.IO.Path.GetFileName).Where(n => !n!.StartsWith(StashFolder.StoreFileName)).ToArray()!;

    public void Dispose()
    {
        Close();
        scratch.Dispose();
    }
}
