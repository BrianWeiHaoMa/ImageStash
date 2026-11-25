using Microsoft.Data.Sqlite;

namespace ImageStash.Tests.Support;

/// <summary>A closed store taken back to the version before saved layer text, keeping the original layer records.</summary>
public static class VersionFourStore
{
    public const string WithoutFifthStep =
        """
        DROP TRIGGER IF EXISTS items_insert_layer_text;
        DROP TRIGGER IF EXISTS items_update_layer_text;
        DROP TRIGGER IF EXISTS items_delete_layer_text;
        DROP TABLE saved_layer_text;
        """;

    public static void Make(string storeFile)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = storeFile, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = WithoutFifthStep + " PRAGMA user_version = 4;";
        command.ExecuteNonQuery();
    }
}
