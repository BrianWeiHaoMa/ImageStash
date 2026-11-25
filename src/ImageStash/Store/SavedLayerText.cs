using System.Text;
using System.Text.Json;
using ImageStash.Layers;
using Microsoft.Data.Sqlite;

namespace ImageStash.Store;

/// <summary>Whole strings from each of an item's two saved layer lists, in list order (G5, State: The store).</summary>
internal readonly record struct SavedLayerText(IReadOnlyList<string> Current, IReadOnlyList<string> Previous)
{
    // These version-5 definitions also identify a readable projection in a staged archive. Keep them verbatim.
    // Separate rows leave the item row's space for the original layer lists and its other facts.
    private const string CreateTable = """
        CREATE TABLE saved_layer_text (
            item_id INTEGER NOT NULL,
            list_slot INTEGER NOT NULL,
            strings TEXT NOT NULL,
            PRIMARY KEY (item_id, list_slot)
        ) WITHOUT ROWID
        """;

    private const string ProjectedRows = """
        SELECT id, 0, imagestash_layer_text(typeof(layers), CAST(layers AS BLOB))
        FROM items WHERE layers <> '[]'
        UNION ALL
        SELECT id, 1, imagestash_layer_text(typeof(previous_layers), CAST(previous_layers AS BLOB))
        FROM items WHERE previous_layers IS NOT NULL AND previous_layers <> '[]'
        """;

    private const string Backfill = "INSERT INTO saved_layer_text\n" + ProjectedRows;

    private static readonly string[] Triggers =
    [
        """
        CREATE TRIGGER items_insert_layer_text AFTER INSERT ON items
            WHEN NEW.layers <> '[]' OR (NEW.previous_layers IS NOT NULL AND NEW.previous_layers <> '[]')
        BEGIN
            INSERT INTO saved_layer_text
                SELECT NEW.id, 0, imagestash_layer_text(typeof(NEW.layers), CAST(NEW.layers AS BLOB)) WHERE NEW.layers <> '[]'
                UNION ALL
                SELECT NEW.id, 1, imagestash_layer_text(typeof(NEW.previous_layers), CAST(NEW.previous_layers AS BLOB))
                WHERE NEW.previous_layers IS NOT NULL AND NEW.previous_layers <> '[]';
        END
        """,
        """
        CREATE TRIGGER items_update_layer_text AFTER UPDATE OF layers, previous_layers ON items
            WHEN NEW.layers IS NOT OLD.layers OR NEW.previous_layers IS NOT OLD.previous_layers
        BEGIN
            DELETE FROM saved_layer_text WHERE item_id = NEW.id AND
                ((list_slot = 0 AND NEW.layers IS NOT OLD.layers) OR (list_slot = 1 AND NEW.previous_layers IS NOT OLD.previous_layers));
            INSERT INTO saved_layer_text
                SELECT NEW.id, 0, imagestash_layer_text(typeof(NEW.layers), CAST(NEW.layers AS BLOB))
                WHERE NEW.layers IS NOT OLD.layers AND NEW.layers <> '[]'
                UNION ALL
                SELECT NEW.id, 1, imagestash_layer_text(typeof(NEW.previous_layers), CAST(NEW.previous_layers AS BLOB))
                WHERE NEW.previous_layers IS NOT OLD.previous_layers AND NEW.previous_layers IS NOT NULL AND NEW.previous_layers <> '[]';
        END
        """,
        """
        CREATE TRIGGER items_delete_layer_text AFTER DELETE ON items
        BEGIN
            DELETE FROM saved_layer_text WHERE item_id = OLD.id;
        END
        """,
    ];

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static string Migration => string.Join(";\n", [CreateTable, Backfill, .. Triggers]) + ";";

    // The version-5 step and triggers use this function on the same connection as their source writes.
    internal static void RegisterProjection(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA encoding";
        Encoding encoding = command.ExecuteScalar() switch
        {
            "UTF-16le" => new UnicodeEncoding(bigEndian: false, byteOrderMark: false, throwOnInvalidBytes: true),
            "UTF-16be" => new UnicodeEncoding(bigEndian: true, byteOrderMark: false, throwOnInvalidBytes: true),
            _ => StrictUtf8,
        };
        connection.CreateFunction<string, byte[]?, string>("imagestash_layer_text",
            (storageClass, bytes) => Project(storageClass, bytes, encoding), isDeterministic: true);
    }

    internal static Outcome<IReadOnlyDictionary<long, SavedLayerText>> ReadSnapshot(StoreContext store) =>
        store.Read("List the stash", () => Read(store));

    /// <summary>One read of both sources for each unmarked row, without reading the layer lists or opening picture files.</summary>
    internal static IReadOnlyDictionary<long, SavedLayerText> Read(StoreContext store) =>
        store.Query(
            $"""
            SELECT i.id, committed.strings, previous.strings
            FROM items i
            LEFT JOIN saved_layer_text committed ON committed.item_id = i.id AND committed.list_slot = 0
            LEFT JOIN saved_layer_text previous ON previous.item_id = i.id AND previous.list_slot = 1
            WHERE {StashItem.NotDeleted}
            """,
            row => (Id: row.GetInt64(0), Text: new SavedLayerText(Strings(row, 1), Strings(row, 2))))
        .ToDictionary(row => row.Id, row => row.Text);

    internal static bool HasSchema(StoreContext store)
    {
        var definitions = store.Query(
            """
            SELECT sql FROM sqlite_schema WHERE sql IS NOT NULL AND
                ((type = 'table' AND name = 'saved_layer_text')
                    OR (type = 'trigger' AND tbl_name IN ('items', 'saved_layer_text'))
                    OR (type = 'index' AND tbl_name = 'saved_layer_text'))
            """,
            row => row.GetString(0).ReplaceLineEndings("\n"));
        return definitions.Count == 1 + Triggers.Length
            && definitions.Contains(CreateTable.ReplaceLineEndings("\n"))
            && Triggers.All(trigger => definitions.Contains(trigger.ReplaceLineEndings("\n")));
    }

    internal static void RefreshStaged(StoreContext store)
    {
        var differs = (long)store.Scalar($"""
            WITH expected(item_id, list_slot, strings) AS MATERIALIZED ({ProjectedRows})
            SELECT EXISTS (SELECT * FROM expected EXCEPT SELECT * FROM saved_layer_text)
                OR EXISTS (SELECT * FROM saved_layer_text EXCEPT SELECT * FROM expected)
            """)! != 0;
        if (differs)
        {
            store.Execute($"DELETE FROM saved_layer_text;\n{Backfill};");
        }
    }

    private static string Project(string storageClass, byte[]? bytes, Encoding encoding)
    {
        if (storageClass != "text" || bytes is null)
        {
            return "[]";
        }

        // Read bytes in the database's own encoding. The provider's string reader can truncate at a NUL or
        // replace invalid bytes before validation.
        Outcome<LayerList> read;
        try
        {
            read = LayerList.FromJson(encoding.GetString(bytes), "");
        }
        catch (DecoderFallbackException)
        {
            return "[]";
        }

        if (!read.Succeeded)
        {
            return "[]";
        }

        var strings = new List<string>();
        foreach (var layer in read.Value!.Layers)
        {
            switch (layer)
            {
                case TextLayer text:
                    strings.Add(text.Text);
                    break;
                case CaptionBarLayer bar:
                    strings.Add(bar.Text);
                    break;
            }
        }

        return JsonSerializer.Serialize(strings);
    }

    private static string[] Strings(SqliteDataReader row, int index)
    {
        var json = row.IsDBNull(index) ? "[]" : row.GetString(index);
        if (json is "[]")
        {
            return [];
        }

        try
        {
            var strings = JsonSerializer.Deserialize<string[]>(json);
            return strings is not null && Array.TrueForAll(strings, value => value is not null) ? strings : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
