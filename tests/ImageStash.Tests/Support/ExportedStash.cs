using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using ImageStash.Store;
using Microsoft.Data.Sqlite;
using static ImageStash.Tests.Support.EditedItems;

namespace ImageStash.Tests.Support;

/// <summary>
/// The archives the export and import checks need (G36, State: Export stash…, Import stash…): a stash filled with every
/// kind of item an export carries, its export, an archive's entries with their hashes, and archives that are not
/// exports, or come from a newer or an earlier build, made from scratch or rewritten from a real one.
/// </summary>
public static class ExportedStash
{
    /// <summary>
    /// What <see cref="Fill"/> left in the stash: an unedited still, an edited item (its original in <c>originals\</c>), a
    /// first Save left pending with its render in <c>.work\</c>, and an animation with an Open with… copy recorded.
    /// </summary>
    public sealed record Filled(StashItem Plain, StashItem Edited, StashItem Pending, StashItem Opened);

    /// <summary>
    /// Fills the open stash as the export's first check asks: the four items of <see cref="Filled"/>, "mood" on two of
    /// them, two favorites and a recorded use.
    /// </summary>
    public static async Task<Filled> Fill(TestStash stash)
    {
        var store = stash.Store!;
        var plain = await stash.ImportFixture("plain", "photo.jpg");
        var saved = await stash.SaveEdit(await stash.ImportFixture("edited", "still.png"), Arrow);
        Assert.IsTrue(saved.Succeeded, saved.Failure?.ToString());
        var pending = await FirstSaveMoved(stash, "still.webp");
        var opened = await stash.ImportFixture("opened", "animated.gif");
        Assert.AreEqual("", TestStash.Failures(await store.AddTag([plain, saved.Value!], "mood")));
        Assert.AreEqual("", TestStash.Failures(await store.SetFavorite([plain, opened], true)));
        Assert.IsTrue((await store.RecordUses([plain], stash.Clock.Now)).Succeeded);
        await OpenedWith.Begin(stash, opened);
        return new Filled(plain, saved.Value!, pending, opened);
    }

    /// <summary>Exports the open stash to <paramref name="name"/> outside it and requires the archive.</summary>
    public static async Task<string> Export(TestStash stash, SettingsKeeper keeper, string name = "export.zip")
    {
        var archive = stash.Outside(name);
        var exported = await stash.Store!.Export(keeper, archive);
        Assert.IsTrue(exported.Succeeded, exported.Failure?.ToString());
        return archive;
    }

    /// <summary>
    /// An export at <paramref name="archive"/> of another stash holding an item named after each of
    /// <paramref name="names"/>, imported in that order (so their identities count from 1), each an 8 × 6 PNG of its own
    /// solid colour (<see cref="ColourOf"/>), its settings changed by <paramref name="settings"/>: the file an Import
    /// stash… check chooses.
    /// </summary>
    public static async Task<string> Of(string archive, Func<Settings, Settings>? settings, params string[] names)
    {
        using var source = new TestStash();
        await source.OpenCleanly();
        for (var i = 0; i < names.Length; i++)
        {
            await source.ImportPicture(names[i], names[i] + ".png", MediaFixtures.DistinctPng(ColourOf(i), 8, 6));
        }

        var keeper = MovingStash.Keeper(source, []);
        if (settings is not null)
        {
            keeper.Change(settings);
        }

        var exported = await source.Store!.Export(keeper, archive);
        Assert.IsTrue(exported.Succeeded, exported.Failure?.ToString());
        await keeper.Flush();
        source.Close();
        return archive;
    }

    /// <summary>The seed of <see cref="MediaFixtures.DistinctPng"/> <see cref="Of"/> gives its <paramref name="index"/>th item: its red is 0x30 plus 0x10 for each item before it.</summary>
    public static int ColourOf(int index) => 0x30 + 0x10 * index;

    /// <summary>Every entry of the archive by its name, with the SHA-256 of its bytes.</summary>
    public static Dictionary<string, string> Entries(string archive)
    {
        using var zip = ZipFile.OpenRead(archive);
        return zip.Entries.ToDictionary(entry => entry.FullName, entry => Hash(Bytes(entry)));
    }

    /// <summary>The bytes of the archive's entry <paramref name="name"/>.</summary>
    public static byte[] Entry(string archive, string name)
    {
        using var zip = ZipFile.OpenRead(archive);
        return Bytes(zip.GetEntry(name) ?? throw new AssertFailedException($"the archive has no {name}"));
    }

    /// <summary>
    /// What a stash folder holding exactly the archive's files would show to <see cref="MovingStash.Tree"/>: every file but
    /// the manifest and the settings, with its hash, and the folder each lies in.
    /// </summary>
    public static List<string> TreeOf(string archive)
    {
        var files = Entries(archive).Where(entry => entry.Key is not ("manifest.json" or "settings.json")).ToList();
        return
        [
            .. files.Where(entry => entry.Key.Contains('/')).Select(entry => entry.Key[..entry.Key.IndexOf('/')] + Path.DirectorySeparatorChar).Distinct(),
            .. files.Select(entry => entry.Key.Replace('/', Path.DirectorySeparatorChar) + " " + entry.Value),
        ];
    }

    /// <summary>A ZIP at <paramref name="path"/> holding exactly <paramref name="entries"/>, in order.</summary>
    public static string Zip(string path, params (string Name, byte[] Bytes)[] entries)
    {
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            foreach (var (name, bytes) in entries)
            {
                using var to = zip.CreateEntry(name).Open();
                to.Write(bytes);
            }
        }

        return path;
    }

    /// <summary>
    /// The archive rewritten at <paramref name="path"/>: each change's entry replaced by its bytes, added when the archive
    /// lacks it, or left out when its bytes are null; every other entry as it was, in order.
    /// </summary>
    public static string Rewritten(string archive, string path, params (string Name, byte[]? Bytes)[] changes)
    {
        using var from = ZipFile.OpenRead(archive);
        var entries = from.Entries.Select(entry => (entry.FullName, Bytes: (byte[]?)Bytes(entry))).ToList();
        foreach (var (name, bytes) in changes)
        {
            var at = entries.FindIndex(entry => entry.FullName == name);
            if (at < 0)
            {
                entries.Add((name, bytes));
            }
            else
            {
                entries[at] = (name, bytes);
            }
        }

        return Zip(path, [.. entries.Where(entry => entry.Bytes is not null).Select(entry => (entry.FullName, entry.Bytes!))]);
    }

    /// <summary>A manifest's bytes; the store's version defaults to this build's.</summary>
    public static byte[] Manifest(string format = "imagestash-export", int formatVersion = 1, long? storeVersion = null, int items = 0) =>
        Json(new { format, formatVersion, storeVersion = storeVersion ?? StoreSchema.CurrentVersion, build = "1.0.0.0", items });

    /// <summary>A value as camelCase JSON, the names and shapes <c>settings.json</c> uses.</summary>
    public static byte[] Json<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, Settings.Json);

    /// <summary>The archive rewritten at <paramref name="path"/> with its store taken back to what an earlier build at version 1 wrote.</summary>
    public static string WithVersionOneStore(string archive, string path)
    {
        var store = path + ".stash.db";
        File.WriteAllBytes(store, Entry(archive, "stash.db"));
        VersionOneStore.Make(store);
        var bytes = File.ReadAllBytes(store);
        File.Delete(store);
        return Rewritten(archive, path, ("stash.db", bytes));
    }

    /// <summary>The archive rewritten at <paramref name="path"/> with <paramref name="sql"/> run on its store, as no ImageStash wrote it.</summary>
    public static string WithStore(string archive, string path, string sql)
    {
        var store = path + ".stash.db";
        File.WriteAllBytes(store, Entry(archive, "stash.db"));
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = store, Pooling = false }.ToString()))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        var bytes = File.ReadAllBytes(store);
        File.Delete(store);
        return Rewritten(archive, path, ("stash.db", bytes));
    }

    /// <summary>A SQLite file at this build's store version holding none of the stash's tables.</summary>
    public static byte[] StoreWithoutTables(string scratchPath)
    {
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = scratchPath, Pooling = false }.ToString()))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = $"CREATE TABLE notes (text TEXT); PRAGMA user_version = {StoreSchema.CurrentVersion}";
            command.ExecuteNonQuery();
        }

        var bytes = File.ReadAllBytes(scratchPath);
        File.Delete(scratchPath);
        return bytes;
    }

    /// <summary>A store file's <c>user_version</c>, read through a connection that cannot write.</summary>
    public static long StoreVersion(string storeFile)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = storeFile, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version";
        return (long)command.ExecuteScalar()!;
    }

    /// <summary>The folders an import may leave beside the stash, by name: extracted and replaced ones.</summary>
    public static string[] Beside(TestStash stash) =>
        [.. Directory.GetDirectories(Path.GetDirectoryName(stash.Folder.Root)!).Select(Path.GetFileName)
            .Where(name => name!.StartsWith(Path.GetFileName(stash.Folder.Root) + ".", StringComparison.OrdinalIgnoreCase)).Order()!];

    /// <summary>Told synchronously on the store's thread, as an export or an import reports.</summary>
    public sealed class TransferReported(Action<TransferProgress> report) : IProgress<TransferProgress>
    {
        public void Report(TransferProgress value) => report(value);
    }

    private static byte[] Bytes(ZipArchiveEntry entry)
    {
        using var read = entry.Open();
        using var bytes = new MemoryStream();
        read.CopyTo(bytes);
        return bytes.ToArray();
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}
