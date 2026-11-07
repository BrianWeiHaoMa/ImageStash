namespace ImageStash.Window;

/// <summary>What a capture's start did to the stash window (G3).</summary>
public enum CaptureHide
{
    Nothing,
    Flyout,
    Pinned,
}

/// <summary>
/// The stash window out of the way while a capture is in progress and back as it was once it ends (G3): a showing
/// flyout hidden, a showing pinned window minimised, and what was done kept in the window's one slot, so that the end
/// brings back that and nothing else. A window already hidden or minimised, or a flyout with a dialog of its own up or a
/// drag of its own in progress, which G2 keeps up, is left so, and the slot with it; the pinned window, which that rule
/// of G2's does not keep, is minimised whatever it has open. Any summon meanwhile shows the window as a summon does and
/// forgets the slot, so the capture's end shows nothing again, and so does the user's own restore of the minimised
/// window: a minimise they make after it is theirs, not the capture's. The window does the hiding and the showing through
/// <paramref name="hide"/>, which judges by <see cref="Decide"/> and answers what it did, and <paramref name="restore"/>,
/// which brings that back. UI thread only.
/// </summary>
public sealed class CaptureHiding(Func<CaptureHide> hide, Action<CaptureHide> restore)
{
    private CaptureHide slot = CaptureHide.Nothing;

    /// <summary>What the start of a capture does to the window, as it is now (G2, G3).</summary>
    public static CaptureHide Decide(bool visible, bool pinned, bool minimised, bool dialogUp, bool dragInProgress) =>
        !visible ? CaptureHide.Nothing
        : pinned ? (minimised ? CaptureHide.Nothing : CaptureHide.Pinned)
        : dialogUp || dragInProgress ? CaptureHide.Nothing
        : CaptureHide.Flyout;

    /// <summary>At a capture's start: the window hidden or minimised as it is now, and what was done remembered.</summary>
    public void HideForCapture()
    {
        var done = hide();
        if (done != CaptureHide.Nothing)
        {
            slot = done;
        }
    }

    /// <summary>At a capture's end: what the slot remembers brought back, and the slot emptied; nothing when it is empty.</summary>
    public void ShowAfterCapture()
    {
        var hidden = slot;
        slot = CaptureHide.Nothing;
        if (hidden != CaptureHide.Nothing)
        {
            restore(hidden);
        }
    }

    /// <summary>A summon, or the user's own restore, showed the window: nothing is to be brought back.</summary>
    public void Forget() => slot = CaptureHide.Nothing;
}
