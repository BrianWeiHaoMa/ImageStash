using Microsoft.Data.Sqlite;

namespace ImageStash.Tests.Support;

/// <summary>A closed store taken back to what an earlier build at version 3 wrote, for the checks that such a store opens and migrates (F11, G31).</summary>
public static class VersionThreeStore
{
    /// <summary>The columns the fourth schema step adds, in its order.</summary>
    public static readonly string[] FourthStepColumns = ["added_by_capture"];

    /// <summary>
    /// Takes the closed store at <paramref name="storeFile"/> back to what version 3 created: its columns without the fourth
    /// step's, at version 3; a bare version number would make the migration add a column that exists.
    /// </summary>
    public static void Make(string storeFile)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = storeFile, Pooling = false }.ToString());
        connection.Open();
        foreach (var column in FourthStepColumns)
        {
            using var drop = connection.CreateCommand();
            drop.CommandText = $"ALTER TABLE items DROP COLUMN {column}";
            drop.ExecuteNonQuery();
        }

        using var version = connection.CreateCommand();
        version.CommandText = "PRAGMA user_version = 3";
        version.ExecuteNonQuery();
    }
}
