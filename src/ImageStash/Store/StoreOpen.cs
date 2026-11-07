using ImageStash.Files;
using ImageStash.Seams;
using Microsoft.Data.Sqlite;

namespace ImageStash.Store;

/// <summary>
/// Opening the stash at start (State: The store, Every change): create the folders, read the version through a
/// connection that cannot write and refuse a newer store, open the one connection and read the version again before
/// anything that could write, set and verify the DELETE journal, read an older store's recorded sizes again and bring it
/// up, finish or undo every pending step, classify the Open with… copies, then clear <c>.work\</c> of what no row refers
/// to, in that order.
/// </summary>
internal static class StoreOpen
{
    public const string Action = "Open the stash";

    private const int SqliteReadOnlyRollback = 776;

    private static readonly Failure Newer = new(Action, StashFolder.StoreFileName, "it was written by a newer ImageStash, which this one cannot read");

    /// <summary>
    /// The opened store with the failures named while resolving its pending steps; or the failure that kept it closed,
    /// with nothing written.
    /// </summary>
    public static Outcome<(StoreContext Store, IReadOnlyList<Failure> Named)> Open(
        StashFolder folder, IRecycleBin recycleBin, TimeSpan retryDuration, TimeProvider clock, FinishedPictureRenderer renderer, string? openWithRoot, PictureReader reader, StoreThread thread)
    {
        var created = folder.Create();
        if (!created.Succeeded)
        {
            return created.Failure;
        }

        // Read through a connection that cannot write, so refusing a newer store leaves it as it was; closing a writable
        // one would checkpoint a WAL store into it (probed).
        var version = VersionBeforeOpening(folder, retryDuration);
        if (!version.Succeeded)
        {
            return version.Failure;
        }

        if (version.Value > StoreSchema.CurrentVersion)
        {
            return Newer;
        }

        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = folder.StoreFile,
            Mode = SqliteOpenMode.ReadWriteCreate,
            // One connection for the store's life, closed when the store is (Approach); a pooled one would keep the file
            // open after the store closes.
            Pooling = false,
            // The library waits out a busy store itself for this many whole seconds (0 waits forever); the shortest wait
            // leaves the held-file duration to HeldFileRetry, which retries the whole transaction.
            DefaultTimeout = 1,
        }.ToString());
        Outcome<(StoreContext, IReadOnlyList<Failure>)> opened;
        try
        {
            opened = OpenConnected(new StoreContext(connection, folder, recycleBin, retryDuration, clock, renderer, openWithRoot, reader, thread));
        }
        catch
        {
            connection.Dispose();
            throw;
        }

        if (!opened.Succeeded)
        {
            connection.Dispose();
        }

        return opened;
    }

    /// <summary>
    /// The store's version read without writing: 0 for a store not created yet, null when a hot journal must be rolled
    /// back before it can be read, which only a writable connection does.
    /// </summary>
    private static Outcome<long?> VersionBeforeOpening(StashFolder folder, TimeSpan retryDuration)
    {
        if (!File.Exists(folder.StoreFile))
        {
            return Outcome<long?>.Of(0);
        }

        return HeldFileRetry.OpenStore(Action, folder.StoreFile, retryDuration, () =>
        {
            using var reader = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = folder.StoreFile,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false,
                DefaultTimeout = 1,
            }.ToString());
            reader.Open();
            using var command = reader.CreateCommand();
            command.CommandText = "PRAGMA user_version";
            try
            {
                return (long?)command.ExecuteScalar();
            }
            catch (SqliteException e) when (e.SqliteExtendedErrorCode == SqliteReadOnlyRollback)
            {
                return null;
            }
        });
    }

    /// <summary>
    /// The store file of a stash extracted into <paramref name="staging"/> by an import (State: Import stash…), opened as
    /// <see cref="Open"/> opens one up to and including the migration, on <paramref name="live"/>'s thread with its retry,
    /// none of its pending steps resolved and nothing created: its version read first through a connection that cannot
    /// write and a newer one refused, the journal set and read back, an older one brought up to this build's in one
    /// transaction (G31); then <paramref name="read"/> answered once in one transaction, so a store without the stash's
    /// tables or columns is refused with <paramref name="notAStore"/>, and the connection closed. Failures name
    /// <see cref="Action"/> and the store file; a file whose recorded size could not be read again refuses the store by
    /// that file's name and reason, since nothing would read it again once the import commits the version (G35, G36).
    /// </summary>
    public static Outcome<T> OpenStaged<T>(StoreContext live, StashFolder staging, string notAStore, Func<StoreContext, T> read)
    {
        if (!File.Exists(staging.StoreFile))
        {
            return new Failure(Action, StashFolder.StoreFileName, notAStore);
        }

        var version = VersionBeforeOpening(staging, live.RetryDuration);
        if (!version.Succeeded)
        {
            return version.Failure;
        }

        if (version.Value > StoreSchema.CurrentVersion)
        {
            return Newer;
        }

        // Every build's open leaves its store at version 1 or above; at 0 (an empty file) the migration would make an empty stash of it.
        if (version.Value < 1)
        {
            return new Failure(Action, StashFolder.StoreFileName, notAStore);
        }

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = staging.StoreFile,
            // The file is there; a connection that may create one would stand up an empty store if it went meanwhile.
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false,
            DefaultTimeout = 1,
        }.ToString());
        // The live store's thread, retry and seams, over the staged file: nothing else of it is touched here.
        var staged = live with { Connection = connection, Folder = staging };
        var migrated = ConnectAndMigrate(staged);
        if (!migrated.Succeeded)
        {
            return migrated.Failure;
        }

        if (migrated.Value!.FirstOrDefault() is { } unread)
        {
            return new Failure(Action, StashFolder.StoreFileName, $"{unread.Subject} in it could not be read again: {unread.Reason}");
        }

        var answered = HeldFileRetry.RunTransaction(Action, staging.StoreFile, live.RetryDuration, () =>
        {
            try
            {
                return (Answered: true, Value: read(staged));
            }
            catch (SqliteException e) when (e.SqliteErrorCode == SqliteError)
            {
                // No such table or column: a SQLite file, but not a stash's store.
                return (Answered: false, Value: default(T)!);
            }
        });
        return !answered.Succeeded ? answered.Failure
            : answered.Value.Answered ? Outcome<T>.Of(answered.Value.Value) : new Failure(Action, StashFolder.StoreFileName, notAStore);
    }

    private const int SqliteError = 1;

    private static Outcome<(StoreContext, IReadOnlyList<Failure>)> OpenConnected(StoreContext store)
    {
        var migrated = ConnectAndMigrate(store);
        if (!migrated.Succeeded)
        {
            return migrated.Failure;
        }

        var named = PendingStepRecovery.Resolve(store);
        if (!named.Succeeded)
        {
            return named.Failure;
        }

        // After Resolve, which finishes or undoes an adoption, and before the sessions are classified, so a record
        // expires at the next start whatever adoption made it (State: Open with…).
        var expired = AdoptionUndo.ExpireAtStart(store);
        // After Resolve, which ends a marked row with its session, and before Clean, which clears what a take left.
        var classified = OpenWithCopies.AtStart(store);
        var cleaned = WorkFolder.Clean(store);
        if (!cleaned.Succeeded)
        {
            return cleaned.Failure;
        }

        IReadOnlyList<Failure> failures = [.. migrated.Value!, .. named.Value!, .. expired, .. classified, .. cleaned.Value!];
        return Outcome<(StoreContext, IReadOnlyList<Failure>)>.Of((store, failures));
    }

    // The store's connection opened, its version read again, its journal set and verified, an older store's recorded sizes
    // read again and the store brought up: everything an open does before the pending steps are resolved. Answers the
    // failures of the files whose sizes could not be read again.
    private static Outcome<IReadOnlyList<Failure>> ConnectAndMigrate(StoreContext store)
    {
        var connected = HeldFileRetry.OpenStore(Action, store.Folder.StoreFile, store.RetryDuration, () => { store.Connection.Open(); return true; });
        if (!connected.Succeeded)
        {
            return connected.Failure;
        }

        // Read again before any statement that could write, the journal pragma included: a hot journal is rolled back by
        // this connection's first read, and only then is the version known.
        var version = HeldFileRetry.RunTransaction(Action, store.Folder.StoreFile, store.RetryDuration, () => (long)store.Scalar("PRAGMA user_version")!);
        if (!version.Succeeded)
        {
            return version.Failure;
        }

        if (version.Value > StoreSchema.CurrentVersion)
        {
            return Newer;
        }

        // Set and read back at every open: a store another program left in WAL mode is switched back (S2).
        var journal = HeldFileRetry.RunTransaction(Action, store.Folder.StoreFile, store.RetryDuration, () => (string?)store.Scalar("PRAGMA journal_mode = DELETE"));
        if (!journal.Succeeded)
        {
            return journal.Failure;
        }

        if (!string.Equals(journal.Value, "delete", StringComparison.OrdinalIgnoreCase))
        {
            return new Failure(Action, StashFolder.StoreFileName, $"its journal mode stays {journal.Value} instead of delete");
        }

        if (version.Value >= StoreSchema.CurrentVersion)
        {
            return Outcome<IReadOnlyList<Failure>>.Of([]);
        }

        // Read between the version read and the migration, so no file is read inside its transaction (RecordedSizePass).
        var sizes = RecordedSizePass.Read(store, version.Value);
        if (!sizes.Succeeded)
        {
            return sizes.Failure;
        }

        var migrated = StoreSchema.Migrate(store, version.Value, sizes.Value.Corrections);
        return migrated.Succeeded ? Outcome<IReadOnlyList<Failure>>.Of(sizes.Value.Named) : migrated.Failure;
    }
}
