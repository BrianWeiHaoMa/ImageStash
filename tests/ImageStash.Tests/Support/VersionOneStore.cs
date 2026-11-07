using Microsoft.Data.Sqlite;

namespace ImageStash.Tests.Support;

/// <summary>A closed store taken back to what an earlier build at version 1 wrote, for the checks that an older store opens and migrates (F11, G31).</summary>
public static class VersionOneStore
{
    /// <summary>The columns the second schema step adds, in its order.</summary>
    public static readonly string[] SecondStepColumns =
        ["finished_format", "finished_width", "finished_height", "finished_animated", "finished_frame_count", "finished_duration_ms"];

    /// <summary>
    /// Takes the closed store at <paramref name="storeFile"/> back to what version 1 created: its columns without the second
    /// step's, at version 1; a bare version number would make the migration add columns that exist.
    /// </summary>
    public static void Make(string storeFile)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = storeFile, Pooling = false }.ToString());
        connection.Open();
        foreach (var column in SecondStepColumns)
        {
            Execute(connection, $"ALTER TABLE items DROP COLUMN {column}");
        }

        Execute(connection, "PRAGMA user_version = 1");
        using var last = connection.CreateCommand();
        last.CommandText = "SELECT name FROM pragma_table_info('items') ORDER BY cid DESC LIMIT 1";
        Assert.AreEqual("adoption_undo", last.ExecuteScalar());
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
