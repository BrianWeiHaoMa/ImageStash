using ImageStash.Layers;

namespace ImageStash.Editor;

/// <summary>
/// The footer's estimated file size (G24) for a session's live layers: asked of Media off the UI thread a short pause
/// after a change, one estimate at a time and never beside a Save, which holds the same gate. An answer shows only while
/// its list is the live one, and a failure, E4's refusal among them, leaves no size rather than a wrong one, once a
/// second read where the original is now has failed too, since a rename may have moved it under the first.
/// An estimate running when the layers change again, or when a Save asks for the gate, is cancelled and stops at the
/// frame it is on, so the next estimate, or the Save, goes ahead without waiting for the rest of the clip (G24, G26).
/// </summary>
internal sealed class SizeEstimate(EditorSession session, SizeEstimator estimator, SemaphoreSlim gate, TimeSpan heldFileRetry) : IDisposable
{
    // L2: how long the layers rest before their size is asked, so a run of quick changes asks once.
    private static readonly TimeSpan Pause = TimeSpan.FromMilliseconds(200);

    private readonly object guard = new();
    private readonly CancellationTokenSource ended = new();
    private LayerList? wanted;
    private LayerList? measured;
    private long? bytes;
    private bool running;

    // The list whose failed read was last measured again: only the run loop, one at a time, reads or writes it.
    private LayerList? retried;

    // The estimate the estimator is running now, cancelled when it is no longer wanted.
    private CancellationTokenSource? measuring;

    /// <summary>Whether an estimate is waiting or running.</summary>
    public bool Asking
    {
        get
        {
            lock (guard)
            {
                return running;
            }
        }
    }

    /// <summary>The size known for <paramref name="live"/>, or null.</summary>
    public long? BytesFor(LayerList live)
    {
        lock (guard)
        {
            return measured is not null && measured.Equals(live) ? bytes : null;
        }
    }

    /// <summary>Asks for the size of <paramref name="layers"/>, once any estimate already running has stopped, which it is asked to do.</summary>
    public void Ask(LayerList layers)
    {
        lock (guard)
        {
            wanted = layers;
            if (running)
            {
                measuring?.Cancel();
                return;
            }

            running = true;
        }

        _ = Run();
    }

    /// <summary>
    /// For a Save, which takes the gate next: the estimate running is cancelled and one waiting is dropped, so the gate is
    /// let go once the estimator leaves the frame it is on; the layers have no size until they are asked again.
    /// </summary>
    public void Cancel()
    {
        lock (guard)
        {
            wanted = null;
            measuring?.Cancel();
        }
    }

    /// <summary>Stops asking; an answer still coming is dropped.</summary>
    public void Dispose()
    {
        ended.Cancel();
        Cancel();
    }

    private async Task Run()
    {
        try
        {
            while (true)
            {
                await Task.Delay(Pause, ended.Token);
                await gate.WaitAsync(ended.Token);
                LayerList? layers;
                Outcome<long> answer;
                string readFrom;
                using var call = new CancellationTokenSource();
                try
                {
                    lock (guard)
                    {
                        // Dropped by a Save while this waited: nothing to measure until the layers are asked again.
                        (layers, wanted) = (ended.IsCancellationRequested ? null : wanted, null);
                        measuring = layers is null ? null : call;
                        running = layers is not null;
                    }

                    if (layers is null)
                    {
                        return;
                    }

                    // Where the original is now: a rename or a Revert notice's Undo may have moved it under the open edit.
                    await session.Reread();
                    var edit = session.Edit;
                    var name = session.Subject;
                    readFrom = edit.OriginalPath;
                    answer = await Task.Run(() => estimator(edit.OriginalPath, edit.OriginalSha256, name, layers, edit.FinishedFormat, heldFileRetry, call.Token));
                }
                finally
                {
                    lock (guard)
                    {
                        measuring = null;
                    }

                    gate.Release();
                }

                // A read can fail because a rename moved the original under it (the item renamed while the edit is open),
                // perhaps away and back again, which says nothing of this list's size: a failed read is measured once more
                // where the original is now, and again whenever the original has moved since (G24). A refusal fails twice.
                var measureAgain = false;
                if (!answer.Succeeded && !call.IsCancellationRequested && (await session.Reread()).Value is { } now)
                {
                    measureAgain = !ReferenceEquals(retried, layers) || !string.Equals(now.OriginalPath, readFrom, StringComparison.Ordinal);
                    retried = layers;
                }

                bool again;
                lock (guard)
                {
                    // A cancelled estimate's answer is not this list's size, whatever it says. Any other is, and is kept even
                    // while a drag shows another list, so a drag that puts this one back states its size (G24); it shows only
                    // for this list (BytesFor).
                    if (measureAgain)
                    {
                        wanted ??= layers;
                    }
                    else if (!call.IsCancellationRequested)
                    {
                        (measured, bytes) = (layers, answer.Succeeded ? answer.Value : null);
                    }

                    again = wanted is not null && !ended.IsCancellationRequested;
                    running = again;
                }

                if (ended.IsCancellationRequested)
                {
                    return;
                }

                session.RaiseChanged();
                if (!again)
                {
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            lock (guard)
            {
                running = false;
            }
        }
    }
}
