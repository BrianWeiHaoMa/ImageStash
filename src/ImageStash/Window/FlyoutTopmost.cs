namespace ImageStash.Window;

/// <summary>
/// Whether the stash window stands above every other window (G1, G2): the flyout does, like the system's own tray
/// flyouts, so it shows even when Windows refuses it the foreground; but while ImageStash's own Settings page is the
/// active window the flyout gives way to it, so the page it opened comes in front of it and a click on either brings
/// that one forward, as two ordinary windows do. The flyout is above again once it is itself activated, once the page
/// closes, and at every setting of its frame, which every summon makes. The pinned window never is.
/// </summary>
public sealed class FlyoutTopmost
{
    private bool pinned;
    private bool pageInFront;

    /// <summary>Whether the window is to stand above every other window now.</summary>
    public bool Topmost => !pinned && !pageInFront;

    /// <summary>The window's frame set, the flyout's or the pinned window's: at every summon, Pin, Unpin and Close.</summary>
    public void Framed(bool pinnedNow)
    {
        pinned = pinnedNow;
        pageInFront = false;
    }

    /// <summary>The Settings page became the active window, however it was asked for: the flyout gives way to it.</summary>
    public void PageActivated() => pageInFront = true;

    /// <summary>The stash window itself became the active window.</summary>
    public void WindowActivated() => pageInFront = false;

    /// <summary>The Settings page closed.</summary>
    public void PageClosed() => pageInFront = false;
}
