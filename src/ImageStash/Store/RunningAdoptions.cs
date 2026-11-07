namespace ImageStash.Store;

/// <summary>
/// The items whose Open with… adoption, or its Undo, is running, from the moment it is asked until it answers. Such a
/// change runs as several calls on the store's thread (its take, its commit, each attempt at its file steps), and
/// other calls run between them; the reads that open or hand out the file it may be about to replace, Edit's read and a
/// handoff's confirmation, wait for it instead (G20, G8), as they wait in the store's queue behind every other change.
/// In memory and afresh at each open (R8).
/// </summary>
internal sealed class RunningAdoptions
{
    private readonly object gate = new();
    private readonly Dictionary<long, List<TaskCompletionSource>> running = [];

    /// <summary>Marks a change of item <paramref name="id"/> running until the returned mark is disposed.</summary>
    public IDisposable Begin(long id)
    {
        var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (gate)
        {
            if (!running.TryGetValue(id, out var changes))
            {
                running[id] = changes = [];
            }

            changes.Add(ended);
        }

        return new Mark(this, id, ended);
    }

    /// <summary>A task that completes once every change of these items running now has ended; null when none is running.</summary>
    public Task? Of(IEnumerable<long> ids)
    {
        lock (gate)
        {
            var changes = ids.Distinct().SelectMany(id => running.TryGetValue(id, out var of) ? of : []).Select(change => change.Task).ToList();
            return changes.Count == 0 ? null : Task.WhenAll(changes);
        }
    }

    /// <summary>A task that completes once every change running now, of any item, has ended; null when none is running.</summary>
    public Task? OfAny()
    {
        lock (gate)
        {
            return running.Count == 0 ? null : Of(running.Keys.ToList());
        }
    }

    private void End(long id, TaskCompletionSource ended)
    {
        lock (gate)
        {
            if (running.TryGetValue(id, out var changes) && changes.Remove(ended) && changes.Count == 0)
            {
                running.Remove(id);
            }
        }

        ended.TrySetResult();
    }

    private sealed class Mark(RunningAdoptions owner, long id, TaskCompletionSource ended) : IDisposable
    {
        public void Dispose() => owner.End(id, ended);
    }
}
