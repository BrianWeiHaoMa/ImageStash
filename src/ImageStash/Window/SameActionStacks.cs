namespace ImageStash.Window;

/// <summary>
/// The stacks of notices that keep one history of acts (G16): the stash window's stack alone, or the notifications' stacks
/// together, the tray's and each other display's (<see cref="Notices.Beside"/>), so a later act of an action takes the
/// place of an earlier act's problem wherever either stands, while the window's notices and the notifications never take
/// one another's. It keeps, for each action, the latest of its acts that has raised a notice in any of them.
/// </summary>
internal sealed class SameActionStacks
{
    private readonly List<Notices> stacks = [];

    /// <summary>For each action, the latest of its acts that has raised a notice in one of the stacks.</summary>
    private readonly Dictionary<string, long> latestRaised = [];

    /// <summary><paramref name="stack"/> keeps this history from now on.</summary>
    internal void Add(Notices stack) => stacks.Add(stack);

    /// <summary>
    /// The same action's rule for <paramref name="notice"/>, just raised: when a later act of its action has raised a
    /// notice already, it is taken over at once; otherwise each earlier act's notice of that action naming a problem, in
    /// every stack, is: one still offering its Undo keeps it until its time ends and then leaves, and every other is
    /// returned with its stack, to be taken off and run what it was given for leaving once the stacks have changed. The
    /// notices of one act, and those of no act, never take each other's place.
    /// </summary>
    internal List<(Notices Stack, Notice Notice)> TakeOver(Notice notice)
    {
        if (notice.Act is not { } act)
        {
            return [];
        }

        if (latestRaised.TryGetValue(act.Action, out var latest) && latest > act.Order)
        {
            notice.TakenOver = true;
            return [];
        }

        latestRaised[act.Action] = act.Order;
        var replaced = new List<(Notices, Notice)>();
        foreach (var stack in stacks)
        {
            foreach (var other in stack.Shown.Where(other => other.NamesAProblem && other.Act is { } by && by.Action == act.Action && by.Order < act.Order).ToList())
            {
                other.TakenOver = true;
                if (!stack.OffersUndo(other))
                {
                    replaced.Add((stack, other));
                }
            }
        }

        return replaced;
    }
}
