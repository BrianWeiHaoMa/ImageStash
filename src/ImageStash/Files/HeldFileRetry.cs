using System.Diagnostics;
using Microsoft.Data.Sqlite;

namespace ImageStash.Files;

/// <summary>
/// The one policy for a file another program briefly holds open (G35, E3): the file step runs again until it
/// succeeds or the duration ends, and only then is the failure named. Anything else fails at once with its own reason.
/// </summary>
public static class HeldFileRetry
{
    /// <summary>How long a held file is retried (L2): long enough for a scanner or sync client to let go of a fresh file.</summary>
    public static readonly TimeSpan Duration = TimeSpan.FromSeconds(5);

    public const string HeldReason = "another program is holding it open";

    /// <summary>The reason a step on a file that is not there fails with.</summary>
    public const string MissingReason = "the file does not exist";

    /// <summary>The reason a step on a file whose folder is not there fails with.</summary>
    public const string FolderMissingReason = "its folder does not exist";

    private static readonly TimeSpan Spacing = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Runs <paramref name="step"/> on <paramref name="path"/>, again while the file is held, for at most
    /// <paramref name="duration"/>. A step that replaces a file passes it as <paramref name="replaced"/>, which counts as
    /// held too, its refusals retried as a held file is. A failure names <paramref name="action"/> and the file's name.
    /// Between two tries the caller's thread sleeps the spacing, or spends it in <paramref name="wait"/> (the store's
    /// thread answers reads there, G1); the duration is elapsed time either way.
    /// </summary>
    public static Outcome<T> Run<T>(string action, string path, TimeSpan duration, Func<T> step, string? replaced = null, Action<TimeSpan>? wait = null)
    {
        var clock = Stopwatch.StartNew();
        var triedAfterRefusal = false;
        while (true)
        {
            try
            {
                return Outcome<T>.Of(step());
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Windows refuses a rename over a file another program holds as "access denied", whichever file is named.
                var held = IsHeld(e, path) || (replaced is not null && IsHeld(e, replaced));
                // A refusal is told from a held file by looking afterwards, so a holder that lets go in between makes a
                // held file look free: one more try at once tells them apart. A replacing step cannot tell them apart at
                // all, since a holder that keeps coming back to read looks free every time: it gets the whole duration.
                var replacing = replaced is not null && e is UnauthorizedAccessException;
                if (!held && !replacing && e is UnauthorizedAccessException && !triedAfterRefusal)
                {
                    triedAfterRefusal = true;
                    continue;
                }

                if ((!held && !replacing) || clock.Elapsed >= duration)
                {
                    return new Failure(action, Path.GetFileName(path), held ? HeldReason : ReasonFor(e));
                }
            }

            (wait ?? Thread.Sleep)(Spacing);
        }
    }

    public static Outcome Run(string action, string path, TimeSpan duration, Action step, string? replaced = null, Action<TimeSpan>? wait = null)
    {
        var outcome = Run(action, path, duration, () => { step(); return true; }, replaced, wait);
        return outcome.Succeeded ? Outcome.Done : outcome.Failure;
    }

    /// <summary>
    /// Runs <paramref name="attempt"/>, each call one try that does not retry a held file itself, again after the spacing
    /// while its answer is <paramref name="held"/>, for at most <paramref name="duration"/> from its first answer, and
    /// answers the last: the synchronous sibling of <see cref="RunSpaced{T}"/>, its spacing spent in <paramref name="wait"/>
    /// or asleep. The duration starts once the first try has answered, since one try may itself take long (a batch of
    /// files, a render) and a file it found held is owed the whole duration (G35).
    /// </summary>
    public static T RunWhileHeld<T>(TimeSpan duration, Func<T> attempt, Func<T, bool> held, Action<TimeSpan>? wait = null)
    {
        var answer = attempt();
        var clock = Stopwatch.StartNew();
        while (held(answer) && clock.Elapsed < duration)
        {
            (wait ?? Thread.Sleep)(Spacing);
            answer = attempt();
        }

        return answer;
    }

    /// <summary>
    /// Runs <paramref name="attempt"/>, each call one try, again after the same spacing while its answer is
    /// <paramref name="held"/>, for at most <paramref name="duration"/> from its first answer, and answers the last. The
    /// waits are spent off the caller's thread, so a try that runs in turn with other work (a call on the store's thread)
    /// lets that work run between two tries rather than wait out the whole duration behind them (G1); the duration starts
    /// once the first try has answered, since that try may first wait its turn behind other work and a file it found held
    /// is owed the whole duration (G35).
    /// </summary>
    public static async Task<T> RunSpaced<T>(TimeSpan duration, Func<Task<T>> attempt, Func<T, bool> held)
    {
        var answer = await attempt();
        var clock = Stopwatch.StartNew();
        while (held(answer) && clock.Elapsed < duration)
        {
            await Task.Delay(Spacing);
            answer = await attempt();
        }

        return answer;
    }

    /// <summary>
    /// Runs one whole store transaction on the open store at <paramref name="storePath"/>, again while the store is
    /// held, for at most <paramref name="duration"/>: SQLite reports it busy or locked, or cannot write its journal while
    /// another program holds that file. Any other store error fails at once with its own reason. The transaction must
    /// leave nothing behind when it throws, so running it again is safe.
    /// </summary>
    public static Outcome<T> RunTransaction<T>(string action, string storePath, TimeSpan duration, Func<T> transaction) =>
        RunOnStore(action, storePath, duration, transaction, opening: false);

    /// <summary>Opens the store's connection, again while another program holds the store file or its journal.</summary>
    public static Outcome<T> OpenStore<T>(string action, string storePath, TimeSpan duration, Func<T> open) =>
        RunOnStore(action, storePath, duration, open, opening: true);

    private static Outcome<T> RunOnStore<T>(string action, string storePath, TimeSpan duration, Func<T> work, bool opening)
    {
        var clock = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                return Outcome<T>.Of(work());
            }
            catch (SqliteException e)
            {
                var held = IsStoreHeld(e, storePath, opening);
                if (!held || clock.Elapsed >= duration)
                {
                    return new Failure(action, Path.GetFileName(storePath), held ? HeldReason : StoreReasonFor(e));
                }
            }

            Thread.Sleep(Spacing);
        }
    }

    /// <summary>The words for a store error that is not a held store.</summary>
    public static string StoreReasonFor(SqliteException e) => e.SqliteErrorCode switch
    {
        SqliteFull => "the disk is full",
        SqliteReadOnly => "Windows refused to let ImageStash write to it",
        SqliteCorrupt or SqliteNotADatabase => "it is damaged or is not an ImageStash store",
        SqliteCantOpen => "it cannot be opened",
        SqliteIoError => "Windows could not read or write it",
        _ => e.Message,
    };

    private const int SqliteBusy = 5;
    private const int SqliteLocked = 6;
    private const int SqliteReadOnly = 8;
    private const int SqliteIoError = 10;
    private const int SqliteCorrupt = 11;
    private const int SqliteFull = 13;
    private const int SqliteCantOpen = 14;
    private const int SqliteNotADatabase = 26;

    // SQLite has already retried a sharing violation for about 1.4 s (S2) before it reports one of these. A store file
    // held elsewhere refuses the open (probed: code 14); a held journal refuses its creation or the commit's delete of
    // it. Once the store is open, its own connection holds the store file, so only the journal can tell.
    private static bool IsStoreHeld(SqliteException e, string storePath, bool opening) => e.SqliteErrorCode switch
    {
        SqliteBusy or SqliteLocked => true,
        SqliteCantOpen or SqliteIoError => (opening && OpenElsewhere(storePath)) || OpenElsewhere(storePath + "-journal"),
        _ => false,
    };

    /// <summary>The words for a file failure that is not a held file.</summary>
    public static string ReasonFor(Exception e) => e switch
    {
        FileNotFoundException => MissingReason,
        DirectoryNotFoundException => FolderMissingReason,
        UnauthorizedAccessException => "Windows refused access to it",
        IOException io when (io.HResult & 0xFFFF) is ErrorDiskFull or ErrorHandleDiskFull => "the disk is full",
        _ => e.Message,
    };

    private const int ErrorSharingViolation = 32;
    private const int ErrorLockViolation = 33;
    private const int ErrorHandleDiskFull = 39;
    private const int ErrorDiskFull = 112;

    /// <summary>
    /// Whether <paramref name="e"/>, or the exception it wraps, is an open, read or write another program's hold on the
    /// file refused (a sharing or lock violation), which is retried as a held file is (G35, E3).
    /// </summary>
    public static bool IsHeldRefusal(Exception e) =>
        e is IOException io && (io.HResult & 0xFFFF) is ErrorSharingViolation or ErrorLockViolation
        || e.InnerException is { } inner && IsHeldRefusal(inner);

    private static bool IsHeld(Exception e, string path) => e switch
    {
        IOException io => IsHeldRefusal(io),
        // Replacing a file another program holds open is refused as "access denied", not as a sharing violation
        // (probed on Windows 11 build 26200 for every share mode), so a refusal counts as held while the file is.
        UnauthorizedAccessException => OpenElsewhere(path),
        _ => false,
    };

    /// <summary>
    /// Whether another program holds the file at <paramref name="path"/> open (E3): an open asking for no sharing is
    /// refused as a sharing or lock violation, whatever sharing the holder allowed. A folder, a missing file and a refusal
    /// of access answer no, so a refused step is named at once rather than retried as held (G35).
    /// </summary>
    public static bool OpenElsewhere(string path)
    {
        try
        {
            using var probe = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
            return false;
        }
        catch (IOException io)
        {
            return (io.HResult & 0xFFFF) is ErrorSharingViolation or ErrorLockViolation;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Whether any file inside <paramref name="folder"/>, at any depth, is <see cref="OpenElsewhere"/>: Windows refuses a
    /// folder's rename or removal as access denied while one is, so only this tells that from a refusal of the folder
    /// itself. A missing folder, or one that cannot be walked, answers no.
    /// </summary>
    public static bool HoldsAnOpenFile(string folder)
    {
        try
        {
            return Directory.Exists(folder) && Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Any(OpenElsewhere);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
