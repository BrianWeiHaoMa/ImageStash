using Microsoft.Data.Sqlite;

namespace ImageStash.Tests.Support;

/// <summary>A closed store taken back to what an earlier build at version 3 wrote, for the checks that such a store opens and migrates (F11, G31).</summary>
public static class VersionThreeStore
{
    /// <summary>The columns the fourth schema step adds, in its order.</summary>
    public static readonly string[] FourthStepColumns = ["added_by_capture"];

    public static string WithoutLaterSteps =>
        VersionFourStore.WithoutFifthStep + "\n" + string.Join("\n", FourthStepColumns.Select(column => $"ALTER TABLE items DROP COLUMN {column};"));

    /// <summary>
    /// Takes the closed store at <paramref name="storeFile"/> back to what version 3 created: its columns without the fourth
    /// step's, at version 3; a bare version number would make the migration add a column that exists.
    /// </summary>
    public static void Make(string storeFile)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = storeFile, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = WithoutLaterSteps + " PRAGMA user_version = 3;";
        command.ExecuteNonQuery();
    }
}
