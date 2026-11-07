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
    private readonly PasteTargetHolder pasteTarget;
    private readonly ScrollViewer scroller;
    private readonly Func<bool> covered;
    private readonly Action clearSearch;
    private readonly ContentControl stashState;
    private readonly StackPanel noticeStack;
    private readonly Dictionary<Notice, Control> noticeCards = [];
    private readonly TextBlock count;
    private readonly StackPanel hints;
    private readonly StackPanel busy;
    private readonly TextBlock busyText;
    private string? shownState;

    /// <summary>
    /// For the window listing <paramref name="listing"/> in <paramref name="scroller"/>, which <paramref name="covered"/>
    /// says the preview or the editor covers, and whose search <paramref name="clearSearch"/> clears.
    /// </summary>
    public WindowStatus(Listing listing, Notices notices, SettingsKeeper settings, PasteTargetHolder pasteTarget, ScrollViewer scroller, Func<bool> covered, Action clearSearch)
    {
        this.listing = listing;
        this.notices = notices;
        this.settings = settings;
        this.pasteTarget = pasteTarget;
        this.scroller = scroller;
        this.covered = covered;
        this.clearSearch = clearSearch;
        stashState = new ContentControl { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(20) };
        noticeStack = new StackPanel { Spacing = 6 };
        NoticeArea = WindowLayout.NoticeArea(noticeStack);
        count = new TextBlock { FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, Opacity = 0.7 };
        // Enter pastes into the paste target, else copies (G8, In use); the hints show only while there is a selection.
        hints = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, VerticalAlignment = VerticalAlignment.Center };
        // G35: the running import's, copy's, delete's or rename's busy state, naming it, until it has finished.
        busyText = new TextBlock { FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center };
        busy = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center, IsVisible = false };
        Footer = WindowLayout.Footer(hints, busy, busyText, count);
    }

    /// <summary>The empty stash's or the no-match state's view, shown in the grid's place.</summary>
    public Control StateView => stashState;

    /// <summary>The window's notices, the newest at the bottom.</summary>
    public ScrollViewer NoticeArea { get; }

    /// <summary>The footer: the hints, the busy state and the count.</summary>
    public Border Footer { get; }

    /// <summary>A shortcut remapped in Settings: the empty stash lists it from now on (G2).</summary>
    public void ShortcutsChanged()
    {
        shownState = null;
        ShowListing();
    }

    public void ShowListing()
    {
        var state = StashStateView.Key(listing);
        if (state != shownState)
        {
            shownState = state;
            stashState.Content = state is null ? null : StashStateView.For(listing, settings, clearSearch);
        }

        stashState.IsVisible = state is not null;
        scroller.IsVisible = state is null;
        count.Text = listing.State != StashState.Open ? "" : StashStateView.CountText(listing.Items.Count, listing.Selection.Count);
        hints.IsVisible = listing.State == StashState.Open && listing.Selection.Count > 0 && !covered();
    }

    public void ShowHints()
    {
        hints.Children.Clear();
        foreach (var text in PasteTargetHolder.Hints(pasteTarget.Current))
        {
            hints.Children.Add(new TextBlock { Text = text, FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7 });
        }
    }

    /// <summary>The busy states of what may be running, each naming its action while it runs (G35).</summary>
    public void ShowBusy(IEnumerable<string?> busyStates)
    {
        var running = busyStates.OfType<string>().ToList();
        busy.IsVisible = running.Count > 0;
        busyText.Text = string.Join(" · ", running);
    }

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
