namespace ImageStash.Editor;

/// <summary>
/// Whether leaving would discard work, and Esc asking before it does (G20): the edit differs while the layers Cancel
/// would discard are not the ones its item's finished picture was rendered from, which an Edit of it opens on. While it
/// differs, an Esc with nothing selected first has the option row say <see cref="AskingWords"/> for
/// <see cref="AskingFor"/> on the session's clock, and only an Esc while they show leaves.
/// </summary>
public sealed partial class EditorSession
{
    /// <summary>What the option row says, in place of what the mouse does, after the first Esc on a changed edit (G20, G21).</summary>
    public const string AskingWords = "Press Esc again to discard your changes";

    /// <summary>How long the row asks (L2).</summary>
    public static readonly TimeSpan AskingFor = TimeSpan.FromSeconds(2);

    private TimeProvider clock = LocalClock.Instance;
    private Action<Action> onUiThread = action => action();
    private DateTimeOffset? askedUntil;
    private ITimer? askingTimer;

    /// <summary>
    /// Whether the live layers differ from those the item's finished picture was rendered from (G20), as last read
    /// (<see cref="Edit"/>; after Save as new item, the new item's). A selection, the tool, the frame showing, Compare and
    /// what a tool remembers for its next layer are no layers. Callers put back a running drag and close the open field
    /// first, as leaving does, so a colour only being tried or a blank new text is not counted.
    /// </summary>
    public bool Differs => !Layers.Equals(Edit.RenderedLayers);

    /// <summary>The asking words while the row says them, or null; read from the clock, so they are gone on time whenever read.</summary>
    public string? Asking => askedUntil is { } until && clock.GetUtcNow() < until ? AskingWords : null;

    /// <summary>
    /// Esc with nothing selected (G20): a running drag put back, true when it leaves the edit (the edit does not differ,
    /// or the row is asking); otherwise the row starts asking, and false. While a Save runs it leaves as Cancel does,
    /// which ends nothing then.
    /// </summary>
    public bool EscapeLeaves()
    {
        if (!Accepts)
        {
            return true;
        }

        tool.Abandon();
        EndTyping();
        if (!Differs || Asking is not null)
        {
            EndAsking();
            return true;
        }

        askedUntil = clock.GetUtcNow() + AskingFor;
        askingTimer?.Dispose();
        // The row gives the words back without waiting for another input.
        askingTimer = clock.CreateTimer(_ => onUiThread(AskingTimedOut), null, AskingFor, Timeout.InfiniteTimeSpan);
        RaiseChanged();
        return false;
    }

    /// <summary>Ends the asking: another input than Esc came, so a later Esc asks again first.</summary>
    public void EndAsking()
    {
        askingTimer?.Dispose();
        askingTimer = null;
        if (askedUntil is not null)
        {
            askedUntil = null;
            RaiseChanged();
        }
    }

    private void StartLeaving(TimeProvider time, SynchronizationContext? ui)
    {
        clock = time;
        onUiThread = ui is null ? action => action() : action => ui.Post(_ => action(), null);
    }

    private void AskingTimedOut()
    {
        if (askedUntil is not { } until || closed)
        {
            return;
        }

        var left = until - clock.GetUtcNow();
        if (left > TimeSpan.Zero)
        {
            // A timer that fired early waits out the rest.
            askingTimer?.Change(left, Timeout.InfiniteTimeSpan);
        }
        else
        {
            EndAsking();
        }
    }

    private void EndLeaving()
    {
        askingTimer?.Dispose();
        askingTimer = null;
        askedUntil = null;
    }
}
