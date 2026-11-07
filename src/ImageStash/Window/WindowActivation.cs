using Avalonia.Controls;
using Avalonia.Threading;
using ImageStash.Seams;

namespace ImageStash.Window;

/// <summary>
/// What each activation and deactivation of the stash window means for the paste target and for the flyout's hiding
/// (G2, G8): ImageStash gaining focus from another app sets the target to the window that had focus just before, while
/// the window's own showings (a summon, the pin, the unpin, the return after a capture) are absorbed; the flyout hides
/// once focus has gone to another app, unless a dialog, a drag or an edit of its own keeps it, or focus went to
/// ImageStash's own notification or Settings page; Close unpins and hides. It owns the window's activation state, which
/// the window changes only through the transitions named for each event, and the hide itself. UI thread only.
/// </summary>
public sealed class WindowActivation
{
    private readonly Avalonia.Controls.Window window;
    private readonly WindowChrome chrome;
    private readonly Editing editing;
    private readonly Notices notices;
    private readonly PasteTargetHolder pasteTarget;
    private readonly ITarget targets;
    private readonly PressOnce pasteKey;
    private readonly Func<bool> dialogUp;
    private readonly Func<bool> dragInProgress;
    private bool notificationHasFocus;
    private SettingsWindow? settingsPage;

    /// <summary>Set when focus went to another app while an edit kept the flyout up; the edit's end then hides it (G2).</summary>
    private bool focusLeftDuringEdit;
    private bool ownActivationExpected;
    private bool ownShowingRuns;
    private int ownShowings;
    private bool activationUnread;

    /// <summary>
    /// Follows <paramref name="window"/>'s activation, deactivation and Close from now on; <paramref name="dialogUp"/> and
    /// <paramref name="dragInProgress"/> say whether a dialog the window opened is up and a drag it started is in progress.
    /// </summary>
    public WindowActivation(Avalonia.Controls.Window window, WindowChrome chrome, Editing editing, Notices notices, PasteTargetHolder pasteTarget, ITarget targets, PressOnce pasteKey, Func<bool> dialogUp, Func<bool> dragInProgress)
    {
        this.window = window;
        this.chrome = chrome;
        this.editing = editing;
        this.notices = notices;
        this.pasteTarget = pasteTarget;
        this.targets = targets;
        this.pasteKey = pasteKey;
        this.dialogUp = dialogUp;
        this.dragInProgress = dragInProgress;

        // Judged once the events have settled: a change of the window's own frame deactivates it for a moment without
        // focus going to another app, and only that loss hides the flyout (G2).
        window.Deactivated += (_, _) =>
        {
            // V let go in another app is never seen here.
            pasteKey.Released();
            focusLeftDuringEdit = editing.IsOpen;
            Dispatcher.UIThread.Post(HideIfFocusLeft, DispatcherPriority.Background);
        };
        // G8: ImageStash gaining focus any other way sets the target to the window that had focus just before, read at
        // the next idle for the footer, or sooner by the handoff or the item menu that comes first; the one activation the
        // window's own shortcut summon, pin or unpin causes is not such a gain, and any later one is.
        window.Activated += (_, _) =>
        {
            focusLeftDuringEdit = false;
            if (ownShowingRuns)
            {
                ownActivationExpected = false;
                return;
            }

            if (ownActivationExpected)
            {
                ownActivationExpected = false;
                return;
            }

            activationUnread = true;
            Dispatcher.UIThread.Post(ReadActivation, DispatcherPriority.Background);
        };
        window.Closing += (_, e) =>
        {
            // Close, from the title bar or Alt+F4, unpins and hides (G2); only the lifetime's shutdown, Quit's, closes it (G3).
            if (e.CloseReason == WindowCloseReason.WindowClosing)
            {
                e.Cancel = true;
                HideWindow();
                chrome.Apply(pinnedNow: false);
                // A window closed while minimised would show minimised at the next summon; hidden, this changes nothing on screen.
                window.WindowState = WindowState.Normal;
            }
        };
    }

    /// <summary>A summon from the tray, or the first run's pinned one: the target is the window that had focus just before (G8).</summary>
    public void ReadTargetAtSummon() => pasteTarget.FromActivation(targets);

    /// <summary>A capture starts: its windows are ImageStash's own, so no late report reads the target again across them (G8, G3).</summary>
    public void CaptureStarted() => pasteTarget.Keep();

    /// <summary>
    /// The tray menu that ran Open ImageStash has closed: since the window before the stash window was that menu, there
    /// is no paste target, and the activation it caused is not read (G8, G34).
    /// </summary>
    public void TrayMenuClosed()
    {
        activationUnread = false;
        pasteTarget.None();
    }

    /// <summary>The edit ended: when it kept the flyout up after focus went to another app (G2), the flyout goes.</summary>
    public void EditEnded()
    {
        if (focusLeftDuringEdit)
        {
            focusLeftDuringEdit = false;
            Dispatcher.UIThread.Post(HideIfFocusLeft, DispatcherPriority.Background);
        }
    }

    /// <summary>The last dialog the window opened has closed, or the drag it started has ended: the flyout judges at the next idle where focus went, which may be the program it started (G2).</summary>
    public void FocusMayHaveLeft() => Dispatcher.UIThread.Post(HideIfFocusLeft, DispatcherPriority.Background);

    /// <summary>
    /// The Settings page, once opened: focus with it has not gone to another app, so the flyout stays, as it does for a
    /// notification's field, and judges again where focus went when the page loses it (G2).
    /// </summary>
    public void SettingsPageOpened(SettingsWindow page)
    {
        settingsPage = page;
        page.Deactivated += (_, _) => Dispatcher.UIThread.Post(HideIfFocusLeft, DispatcherPriority.Background);
    }

    /// <summary>
    /// The target as it stands once the latest activation has been read: input queued behind the click that activated
    /// the window is handled before the read posted for the next idle, and must use the app activated from (G8).
    /// </summary>
    public PasteTarget? TargetNow()
    {
        ReadActivation();
        return pasteTarget.Current;
    }

    private void ReadActivation()
    {
        if (activationUnread)
        {
            activationUnread = false;
            pasteTarget.FromActivation(targets);
        }
    }

    /// <summary>
    /// Enter's paste hides the flyout before the target is brought to the front; a pinned window stays, and so does a
    /// flyout whose dialog was opened, or whose drag was started, while the paste's write was retried (G2).
    /// </summary>
    public void HideForPaste()
    {
        if (Summon.FlyoutHides(chrome.Pinned, dialogUp(), dragInProgress(), editing.IsOpen))
        {
            HideWindow();
        }
    }

    /// <summary>
    /// Runs one of the window's own showings whose activation is not ImageStash gaining focus from another app, so it
    /// does not read the target again (G8). Every activation Avalonia raises while the showing runs is absorbed, since a
    /// restore and the activation after it raise two (a pinned window shown again after a capture); a showing that
    /// <paramref name="activates"/> also leaves a mark that absorbs the one activation Windows grants after it, lapsing at
    /// the next idle when none comes. One that does not leaves none, so a click from another app right after it reads the
    /// target as any activation does.
    /// </summary>
    public void WithOwnActivation(Action show, bool activates = true)
    {
        var showing = ++ownShowings;
        var insideAnother = ownShowingRuns;
        ownActivationExpected = activates;
        ownShowingRuns = true;
        pasteTarget.Keep();
        try
        {
            show();
        }
        finally
        {
            ownShowingRuns = insideAnother;
            Dispatcher.UIThread.Post(() =>
            {
                if (showing == ownShowings)
                {
                    ownActivationExpected = false;
                }
            }, DispatcherPriority.Background);
        }
    }

    /// <summary>
    /// A click into the field of ImageStash's notification above the tray brought it in front: ImageStash gaining focus
    /// any other way, so the target is the window that had focus just before (G8), and the activation coming back here
    /// once the edit ends is read again too; and focus the window lost to its own notification is not focus lost to
    /// another app, so the flyout stays (G2).
    /// </summary>
    public void NotificationTookFocus()
    {
        notificationHasFocus = true;
        pasteTarget.FromActivation(targets);
    }

    /// <summary>The notification lost the activation: the flyout judges again where focus went (G2).</summary>
    public void NotificationLostFocus()
    {
        notificationHasFocus = false;
        Dispatcher.UIThread.Post(HideIfFocusLeft, DispatcherPriority.Background);
    }

    /// <summary>
    /// The flyout hides once focus has gone to another app, unless a dialog or a drag of its own is up (G2); focus with
    /// ImageStash's own notification or Settings page has not gone to another app.
    /// </summary>
    private void HideIfFocusLeft()
    {
        if (window.IsVisible && !window.IsActive && !notificationHasFocus && settingsPage is not { IsActive: true } && Summon.FlyoutHides(chrome.Pinned, dialogUp(), dragInProgress(), editing.IsOpen))
        {
            HideWindow();
        }
    }

    /// <summary>The window's one hide, whatever asks for it: no notice stays hovered and no tag list stays up (G16, F4).</summary>
    public void HideWindow()
    {
        notices.EndHovers();
        // A window that hides while not the active one keeps its popups: no list is left behind (F4).
        foreach (var open in window.OpenedPopups.OfType<TagPicker>().ToList())
        {
            open.Close();
        }

        window.Hide();
    }
}
