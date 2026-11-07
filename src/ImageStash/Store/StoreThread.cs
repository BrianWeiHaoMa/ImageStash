using System.Diagnostics;

namespace ImageStash.Store;

/// <summary>
/// The one thread that owns the store's connection (Approach): every store operation runs on it, whole, and the caller
/// gets a task, so the UI thread never waits on a held file's retry. Changes and reads queue apart; while the thread is
/// idle it runs the oldest of either, so nothing a caller queued after a change answers before that change has run. While
/// a change waits between two attempts on a held file (<see cref="WaitAnsweringReads"/>), the thread answers the reads
/// queued meanwhile instead of sleeping, so the summon's listing is not held behind it (G1); changes queued meanwhile wait
/// for the waiting change's call to end, so a change never runs inside another (State: Every change).
/// </summary>
internal sealed class StoreThread : IDisposable
{
    private readonly object gate = new();
    private readonly Queue<(long Order, Action Run)> changes = new();
    private readonly Queue<(long Order, Action Run)> reads = new();
    private readonly Thread thread;
    private long queued;
    private bool ending;

    public StoreThread()
    {
        // A background thread: a process that ends mid-operation is a kill, which every store change survives (G28).
        thread = new Thread(() =>
        {
            while (Next() is { } step)
            {
                step();
            }
        })
        { IsBackground = true, Name = "ImageStash store" };
        thread.Start();
    }

    /// <summary>Whether the caller is the store's thread itself.</summary>
    public bool IsCurrent => Thread.CurrentThread == thread;

    /// <summary>Queues a change: it runs once every operation queued before it has, and nothing runs inside it.</summary>
    public Task<T> Run<T>(Func<T> operation) => Queue(changes, operation);

    /// <summary>
    /// Queues a read: it runs in arrival order while the thread is idle, or between a waiting change's attempts. A read runs
    /// its statements through <see cref="StoreContext.Read{T}"/> alone and opens no file.
    /// </summary>
    public Task<T> Read<T>(Func<T> read) => Queue(reads, read);

    /// <summary>
    /// The pause between two attempts on a held file, on the store's thread: the reads queued so far and any that arrive
    /// run, each whole, until <paramref name="spacing"/> has passed, and none starts after it. Only the store's thread
    /// answers its reads; any other thread is refused.
    /// </summary>
    public void WaitAnsweringReads(TimeSpan spacing)
    {
        if (!IsCurrent)
        {
            throw new InvalidOperationException("Only the store's thread answers its reads.");
        }

        var clock = Stopwatch.StartNew();
        while (true)
        {
            Action read;
            lock (gate)
            {
                while (reads.Count == 0)
                {
                    var left = spacing - clock.Elapsed;
                    if (left <= TimeSpan.Zero)
                    {
                        return;
                    }

                    Monitor.Wait(gate, left);
                }

                if (clock.Elapsed >= spacing)
                {
                    return;
                }

                read = reads.Dequeue().Run;
            }

            read();
        }
    }

    /// <summary>
    /// Between two steps of a long change on the store's thread (a Move's copy, file by file): the reads queued so far
    /// run, each whole, and it returns without waiting for more, so the summon's listing is not held behind the change (G1).
    /// </summary>
    public void AnswerQueuedReads()
    {
        if (!IsCurrent)
        {
            throw new InvalidOperationException("Only the store's thread answers its reads.");
        }

        int queuedSoFar;
        lock (gate)
        {
            queuedSoFar = reads.Count;
        }

        for (var i = 0; i < queuedSoFar; i++)
        {
            Action read;
            lock (gate)
            {
                read = reads.Dequeue().Run;
            }

            read();
        }
    }

    /// <summary>Lets the operations already queued finish, then ends the thread.</summary>
    public void Dispose()
    {
        lock (gate)
        {
            ending = true;
            Monitor.PulseAll(gate);
        }

        if (!IsCurrent)
        {
            thread.Join();
        }
    }

    private Task<T> Queue<T>(Queue<(long Order, Action Run)> queue, Func<T> operation)
    {
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Answer()
        {
            try
            {
                done.SetResult(operation());
            }
            catch (Exception e)
            {
                done.SetException(e);
            }
        }

        lock (gate)
        {
            if (ending)
            {
                throw new InvalidOperationException("The store is closed.");
            }

            queue.Enqueue((queued++, Answer));
            Monitor.PulseAll(gate);
        }

        return done.Task;
    }

    // The oldest queued operation of either kind, or null once the store is closing and nothing is left.
    private Action? Next()
    {
        lock (gate)
        {
            while (true)
            {
                var hasChange = changes.TryPeek(out var change);
                var hasRead = reads.TryPeek(out var read);
                if (hasChange || hasRead)
                {
                    return hasChange && (!hasRead || change.Order < read.Order) ? changes.Dequeue().Run : reads.Dequeue().Run;
                }

                if (ending)
                {
                    return null;
                }

                Monitor.Wait(gate);
            }
        }
    }
}
