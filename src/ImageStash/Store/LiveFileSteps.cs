using ImageStash.Files;

namespace ImageStash.Store;

/// <summary>
/// The file steps of a change committed while the store is open that no window waits on (an Open with… adoption, which
/// a watcher asks for, and its Undo): one attempt per call on the store's thread, again while a file is held after the
/// held-file spacing, spent off that thread, for the store's held-file duration (G35), so the summon's listing runs
/// between the attempts (G1), while Edit's read and a handoff's confirmation of the item wait for the whole change
/// (<see cref="RunningAdoptions"/>). Until anything has committed since the change's commit, its steps are the row's
/// as committed (<see cref="StoreContext.Commits"/>); after, the row is read: a change another has finished meanwhile
/// is not run again, and one it recorded again is run as recorded now.
/// </summary>
internal static class LiveFileSteps
{
    /// <summary>
    /// Runs the change committed as <paramref name="asCommitted"/> with <paramref name="steps"/> once the store had made
    /// <paramref name="commits"/> commits: each attempt <paramref name="runSteps"/>, one try at the steps not done
    /// answering the refusal that stopped them, then <paramref name="complete"/>, the last transaction; steps the row
    /// holds are this change's while <paramref name="owns"/> says so. Then the item as the stash lists it, or as committed
    /// with the failure that stopped a step.
    /// </summary>
    public static async Task<Outcome<StashItem>> Run(
        StoreThread thread,
        StoreContext store,
        StashItem asCommitted,
        IReadOnlyList<PendingStep> steps,
        long commits,
        string action,
        Func<IReadOnlyList<PendingStep>, bool> owns,
        Func<IReadOnlyList<PendingStep>, Failure?> runSteps,
        Func<IReadOnlyList<PendingStep>, Failure?> complete)
    {
        var attempted = await HeldFileRetry.RunSpaced(
            store.RetryDuration,
            () => thread.Run(() => Attempt(store, asCommitted.Id, steps, commits, action, owns, runSteps, complete)),
            attempt => attempt.Held);
        return await thread.Run(() => ItemQueries.AfterCommit(store, asCommitted, attempted.Failure));
    }

    private static Attempted Attempt(
        StoreContext store,
        long id,
        IReadOnlyList<PendingStep> steps,
        long commits,
        string action,
        Func<IReadOnlyList<PendingStep>, bool> owns,
        Func<IReadOnlyList<PendingStep>, Failure?> runSteps,
        Func<IReadOnlyList<PendingStep>, Failure?> complete)
    {
        if (store.Commits != commits)
        {
            var now = store.Read(action, () => store.Query(
                "SELECT pending_steps FROM items WHERE id = $id", r => r.IsDBNull(0) ? null : r.GetString(0), ("$id", id)).SingleOrDefault());
            if (!now.Succeeded)
            {
                return new(now.Failure, Held: false);
            }

            if (now.Value != PendingStep.Encode(steps))
            {
                var recorded = now.Value is null ? null : PendingStep.Decode(now.Value);
                if (recorded is null || !owns(recorded))
                {
                    return new(null, Held: false);
                }

                steps = recorded;
            }
        }

        if (runSteps(steps) is { } refused)
        {
            return new(refused, Held: refused.Reason == HeldFileRetry.HeldReason);
        }

        return new(complete(steps), Held: false);
    }

    /// <summary>What one attempt at the steps left: the failure that stopped it, if any, and whether a file it needs is still held.</summary>
    private sealed record Attempted(Failure? Failure, bool Held);
}
