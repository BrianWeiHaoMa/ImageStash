namespace ImageStash.Store;

/// <summary>
/// The settings in effect while ImageStash runs, and their one writer (State: Settings). A change takes effect at once;
/// it is written off the UI thread after a short quiet spell, so a window dragged across the screen is written once,
/// not once per pixel, and changes made meanwhile go into the same write, the latest winning. <see cref="Flush"/>
/// writes at once what is still unwritten, so a Quit right after a resize keeps it (G2, G3). <see cref="ChangeNow"/>
/// writes one change at once and answers whether it landed, for a step that must not go on before it has (State: Move).
/// </summary>
public sealed class SettingsKeeper
{
    /// <summary>How long changes collect before they are written.</summary>
    public static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(250);

    private readonly object gate = new();
    private readonly string folder;
    private readonly bool writable;
    private readonly TimeSpan heldFileRetry;
    private readonly TimeSpan quiet;
    private readonly Action<Failure> failed;
    private readonly Failure? unwritable;
    private readonly CancellationTokenSource flushing = new();
    private Settings current;
    private bool scheduled;
    // Ends the quiet spell of the write scheduled, once a ChangeNow is waiting behind it.
    private CancellationTokenSource? hurry;
    private Task writes = Task.CompletedTask;

    /// <param name="load">The settings start read. While the file there could not be read or parsed, nothing is
    /// written: the defaults standing in would replace the user's own choices, the stash location among them.</param>
    /// <param name="failed">Told, on the writing thread, of a write still refused after the held-file retry (G35).</param>
    public SettingsKeeper(string folder, SettingsLoad load, TimeSpan heldFileRetry, TimeSpan quiet, Action<Failure> failed)
    {
        this.folder = folder;
        writable = load.AsSaved;
        unwritable = writable ? null : load.Failure ?? new Failure("Save settings", Settings.FileName, "the file could not be read at start, so ImageStash does not write over it");
        this.heldFileRetry = heldFileRetry;
        this.quiet = quiet;
        this.failed = failed;
        current = load.Settings;
    }

    public Settings Current
    {
        get
        {
            lock (gate)
            {
                return current;
            }
        }
    }

    public void Change(Func<Settings, Settings> change)
    {
        lock (gate)
        {
            current = change(current);
            if (!writable || scheduled)
            {
                return;
            }

            scheduled = true;
            hurry = CancellationTokenSource.CreateLinkedTokenSource(flushing.Token);
            var quietEnds = hurry;
            writes = writes.ContinueWith(_ => WriteLatest(quietEnds), TaskScheduler.Default).Unwrap();
        }
    }

    /// <summary>
    /// Writes <paramref name="change"/> applied to the settings as they are when its turn comes, after every write asked
    /// before it (a write still waiting for quiet is written at once), and answers that write's outcome.
    /// <see cref="Current"/> takes the change only once it is written, so a refused one leaves nothing to put back and no
    /// later write carries it; a change made meanwhile through <see cref="Change"/> is kept either way. While the keeper
    /// writes nothing (start could not read the file) it answers that failure and changes nothing.
    /// </summary>
    public Task<Outcome> ChangeNow(Func<Settings, Settings> change)
    {
        lock (gate)
        {
            if (unwritable is { } refused)
            {
                return Task.FromResult<Outcome>(refused);
            }

            if (scheduled)
            {
                hurry!.Cancel();
            }

            var written = writes.ContinueWith(_ => WriteNow(change), TaskScheduler.Default);
            writes = written;
            return written;
        }
    }

    /// <summary>Writes what is still unwritten without waiting for quiet; completes when every write has ended.</summary>
    public Task Flush()
    {
        flushing.Cancel();
        lock (gate)
        {
            return writes;
        }
    }

    private async Task WriteLatest(CancellationTokenSource quietEnds)
    {
        try
        {
            await Task.Delay(quiet, quietEnds.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        Settings latest;
        lock (gate)
        {
            latest = current;
            scheduled = false;
            // Nothing cancels it once scheduled is false, which is read and set under the same lock.
            quietEnds.Dispose();
        }

        var written = Settings.Write(folder, latest, heldFileRetry);
        if (!written.Succeeded)
        {
            failed(written.Failure);
        }
    }

    // Its caller names a refusal itself, so it is not told to failed as well.
    private Outcome WriteNow(Func<Settings, Settings> change)
    {
        Settings changed;
        lock (gate)
        {
            changed = change(current);
        }

        var written = Settings.Write(folder, changed, heldFileRetry);
        if (written.Succeeded)
        {
            lock (gate)
            {
                current = change(current);
            }
        }

        return written;
    }
}
