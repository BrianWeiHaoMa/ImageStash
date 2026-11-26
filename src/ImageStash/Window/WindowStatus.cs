using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using ImageStash.Store;

namespace ImageStash.Window;

/// <summary>
/// What the stash window says around its grid: the empty stash's ways in, or the no-match state, in the grid's place
/// (G2, G5); the footer's count, its hints naming what Enter does (G8) and its busy state naming what runs (G35); and the
/// stack of the window's notices (G16). It owns those views; the window places them and says when each shows again.
/// </summary>
public sealed class WindowStatus
{
    private readonly Listing listing;
    private readonly Notices notices;
    private readonly SettingsKeeper settings;
    private readonly FooterHints footerHints;
    private readonly ScrollViewer scroller;
    private readonly Func<bool> covered;
    private readonly Action clearSearch;
    private readonly WaysInActs waysIn;
    private GlobalShortcuts? shortcuts;
    private readonly ContentControl stashState;
    private readonly StackPanel noticeStack;
    private readonly Dictionary<Notice, Control> noticeCards = [];
    private readonly WindowFooter footer;
    private readonly PausedShortcutsLine pausedShortcuts;
    private string? shownState;

    /// <summary>
    /// For the window listing <paramref name="listing"/> in <paramref name="scroller"/>, which <paramref name="covered"/>
    /// says the preview or the editor covers, whose search <paramref name="clearSearch"/> clears, and whose empty stash's
    /// rows run <paramref name="waysIn"/>.
    /// </summary>
    public WindowStatus(Listing listing, Notices notices, SettingsKeeper settings, FooterHints footerHints, ScrollViewer scroller, Func<bool> covered, Action clearSearch, WaysInActs waysIn)
    {
        this.listing = listing;
        this.notices = notices;
        this.settings = settings;
        this.footerHints = footerHints;
        this.scroller = scroller;
        this.covered = covered;
        this.clearSearch = clearSearch;
        this.waysIn = waysIn;
        stashState = new ContentControl { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(20) };
        noticeStack = new StackPanel { Spacing = 6 };
        NoticeArea = WindowLayout.NoticeArea(noticeStack);
        pausedShortcuts = new PausedShortcutsLine(() => shortcuts?.ResumePaused(), focusable: false);
        footer = new WindowFooter(pausedShortcuts);
    }

    /// <summary>The empty stash's or the no-match state's view, shown in the grid's place.</summary>
    public Control StateView => stashState;

    /// <summary>The window's notices, the newest at the bottom.</summary>
    public ScrollViewer NoticeArea { get; }

    /// <summary>The footer: the hints, the busy state and the count.</summary>
    public Border Footer => footer.View;

    /// <summary>The footer's pieces, as drawn.</summary>
    internal WindowFooter FooterParts => footer;

    /// <summary>
    /// From now on the empty stash marks a shortcut <paramref name="shortcuts"/> judge unavailable (G2, G33); its owner
    /// tells it of each change through <see cref="ShortcutsChanged"/>.
    /// </summary>
    public void FollowShortcuts(GlobalShortcuts shortcuts)
    {
        this.shortcuts = shortcuts;
        ShortcutsChanged();
    }

    /// <summary>A shortcut remapped in Settings, or one unavailable or working again: the empty stash's rows and its line say so from now on (G2).</summary>
    public void ShortcutsChanged()
    {
        pausedShortcuts.IsVisible = shortcuts?.Paused == true;
        shownState = null;
        ShowListing();
    }

    public void ShowListing()
    {
        var state = StashStateView.Key(listing);
        if (state != shownState)
        {
            shownState = state;
            stashState.Content = state is null ? null : StashStateView.For(listing, settings, clearSearch, waysIn, act => shortcuts?.UnavailableReason(act) is not null);
        }

        stashState.IsVisible = state is not null;
        // An empty scope or no-match state covers an empty grid. Keeping it visible lets the keyboard stay there.
        scroller.IsVisible = state is null || (listing.State == StashState.Open && listing.Items.Count == 0);
        footer.Count.Text = listing.State != StashState.Open ? "" : StashStateView.CountText(listing.Items.Count, listing.Selection.Count);
        ShowHints();
    }

    /// <summary>The hints for the target and the selection as they are now, or a copy's confirmation in their place (G8, G10).</summary>
    public void ShowHints()
    {
        // Enter pastes into the paste target, else copies (G8, In use); the hints show only while there is a selection,
        // or a copy's confirmation stands in for them (G10), and never under the preview or the editor.
        // One read of the confirmation, which expires by the clock, so its words and whether it shows agree.
        var confirmed = footerHints.Confirmation;
        IReadOnlyList<string> texts = confirmed is not null ? [confirmed] : footerHints.Texts;
        var shown = listing.State == StashState.Open && (listing.Selection.Count > 0 || confirmed is not null) && !covered();
        footer.ShowHints(texts, confirmed is not null, shown);
    }

    /// <summary>The busy states of what may be running, each naming its action while it runs (G35).</summary>
    public void ShowBusy(IEnumerable<string?> busyStates) => footer.ShowBusy(busyStates.OfType<string>().ToList());

    public void ShowNotices()
    {
        foreach (var gone in noticeCards.Keys.Except(notices.Shown).ToList())
        {
            noticeStack.Children.Remove(noticeCards[gone]);
            noticeCards.Remove(gone);
        }

        var raised = false;
        foreach (var notice in notices.Shown.Where(n => !noticeCards.ContainsKey(n)))
        {
            var card = NoticeCard.For(notice, notices);
            noticeCards[notice] = card;
            noticeStack.Children.Add(card);
            raised = true;
        }

        if (raised)
        {
            // The newest notice, at the bottom of the stack, is the one in view.
            Dispatcher.UIThread.Post(NoticeArea.ScrollToEnd, DispatcherPriority.Background);
        }
    }
}
