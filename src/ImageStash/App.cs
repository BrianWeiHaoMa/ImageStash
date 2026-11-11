using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using ImageStash.Capture;
using ImageStash.Editor;
using ImageStash.Files;
using ImageStash.Look;
using ImageStash.Media;
using ImageStash.Seams;
using ImageStash.Store;
using ImageStash.Window;

namespace ImageStash;

/// <summary>
/// The composition root. The platform entry point has already held the instance and built the seams; this runs the
/// rest of the start sequence on the UI thread, in order: settings, Start with Windows, the tray, the stash window
/// (created hidden, G1), the global shortcuts, the stash, show requests.
/// </summary>
public sealed class App : Application
{
    private readonly PlatformSeams seams;
    private StashStore? store;
    private StashWindow? window;
    private StashClipboardImage? stashClipboardImage;
    private Screenshot? screenshot;
    private RecordClip? recordClip;
    private SettingsLoad? settingsLoad;
    private SettingsKeeper? keeper;
    private GlobalShortcuts? shortcuts;
    private SettingsWindow? settingsWindow;
    private Moving? moving;
    private Transferring? transferring;

    public App(PlatformSeams seams) => this.seams = seams;

    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        ToolTips.PlaceBeneathTheirControls();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var desktop = (IClassicDesktopStyleApplicationLifetime)ApplicationLifetime!;
        var trayMenu = new TrayMenu(seams.Tray, ShowWindow, TakeScreenshot, RecordClip, StashFromTray, OpenSettings, () => desktop.Shutdown())
        {
            OpenedMenuClosed = () => window?.KeepForward(),
            SettingsMenuClosed = () => settingsWindow?.KeepForward(),
        };
        var settings = ReadSettings(trayMenu);
        ApplyTheme(settings.Settings.Theme);
        Looks.Apply(this, Looks.Accent(this));
        if (PlatformSettings is { } platform)
        {
            platform.ColorValuesChanged += (_, values) => Dispatcher.UIThread.Post(() => FollowAccent(this, values));
        }

        NameFailure(trayMenu, ApplyStartWithWindows(settings, seams.StartWithWindows, Environment.ProcessPath!));
        var notices = new Notices(LocalClock.Instance);
        // Before the shortcuts, which Settings remaps through it.
        var keeper = KeepSettings(desktop, settings, notices);
        var shortcuts = this.shortcuts = Shortcuts(keeper, trayMenu);
        trayMenu.Show(shortcuts);
        var listing = CreateWindow(notices, keeper, trayMenu);
        // The + menu and the empty stash show a shortcut remapped in Settings (G2, G34).
        shortcuts.Changed += window!.ShortcutsChanged;
        // G33's check "at start", once the window the open shortcut summons exists.
        shortcuts.Start();
        OpenStash(desktop, trayMenu, settings, listing, keeper);
        ListenForShowRequests();
        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// F10: the take shown keeps the system's accent, rebuilt from <paramref name="values"/> when the platform reports
    /// a change of it; before any take is applied there is nothing to rebuild.
    /// </summary>
    public static void FollowAccent(Application app, PlatformColorValues values)
    {
        if (Looks.Current(app) is { } current && current.Accent != values.AccentColor1)
        {
            Looks.Apply(app, current.Take, values.AccentColor1);
        }
    }

    /// <summary>F10: every window takes <paramref name="theme"/> at once, at start, from Settings and after an import.</summary>
    private void ApplyTheme(Theme theme) => RequestedThemeVariant = ThemeVariantOf(theme);

    /// <summary>F10: light or dark by the system, or by the Settings choice.</summary>
    public static ThemeVariant ThemeVariantOf(Theme theme) => theme switch
    {
        Theme.Light => ThemeVariant.Light,
        Theme.Dark => ThemeVariant.Dark,
        _ => ThemeVariant.Default,
    };

    /// <summary>
    /// The settings in effect and their one writer, whose last change is written before ImageStash exits (G2, G3); a write
    /// still refused after its retry is named in the stash window's <paramref name="notices"/> and on an open Settings page.
    /// </summary>
    private SettingsKeeper KeepSettings(IClassicDesktopStyleApplicationLifetime desktop, SettingsLoad settings, Notices notices)
    {
        var keeper = new SettingsKeeper(seams.Locations.SettingsFolder, settings, HeldFileRetry.Duration, SettingsKeeper.Quiet,
            failure => Dispatcher.UIThread.Post(() => SettingsWriteFailed(failure, notices, settingsWindow is { HasClosed: false } open ? open.Page : null)));
        settingsLoad = settings;
        this.keeper = keeper;
        desktop.Exit += (_, _) => keeper.Flush().Wait();
        return keeper;
    }

    /// <summary>
    /// The stash window, created now and kept hidden so a summon only shows it (G1), with the listing it shows, the
    /// editor it opens over its items, rendering a Save through Media (G20, G26), and the settings it remembers its
    /// sizes in (G2); and the notifications above the tray, which a paste the target cannot take raises (G8, G16), as
    /// do Stash clipboard image and its capture notification, which open no window (G13), and Open with…'s adoption of
    /// a copy another program rewrote (G27); a notification the platform will not keep out of captures is named in the
    /// tooltip (G35); and Take a screenshot and Record a clip, over one capture session on the screen, the overlay and
    /// the recording's frame and bar, hiding this window meanwhile, which its own menu starts too (G3, G14, G15), the
    /// tray's Start recording, red dot and Stop recording following the recording (G34); and Move…, which the Settings page
    /// asks, waiting for all of these, then keeping the store it opened at the new folder (G30, State: Move); and Export
    /// stash… and Import stash…, which wait as Move waits, the import's reopened store kept as Move's is and the
    /// settings it brought put in effect at once (G36).
    /// </summary>
    private Listing CreateWindow(Notices notices, SettingsKeeper keeper, TrayMenu trayMenu)
    {
        var listing = new Listing(failure => notices.Raise(failure));
        var notifications = new Notices(LocalClock.Instance);
        var notification = new Notification(notifications, seams.QuietWindows, seams.Screen, trayMenu.Name);
        var handoff = new Handoff(listing, notices, notifications, seams.ClipboardAndDrops, seams.Target, LocalClock.Instance, HeldFileRetry.Duration, HeldClipboard.Retry);
        var captured = new CaptureNotification(notifications, listing, handoff, seams.QuietWindows, HeldFileRetry.Duration);
        stashClipboardImage = new StashClipboardImage(listing, notifications, captured, seams.ClipboardAndDrops, LocalClock.Instance, HeldClipboard.Retry, HeldFileRetry.Duration);
        // The one set of recent colours, kept for the run, so every edit sees the same (G21).
        var editing = new Editing(listing, notices, handoff, FinishedPicture.Render, new ColorChoices(), HeldFileRetry.Duration);
        var openWith = new OpenWith(listing, notifications, editing, seams.DialogsAndExplorer, LocalClock.Instance, action => Dispatcher.UIThread.Post(action));
        var moving = this.moving = new Moving(listing, notices, keeper, seams.DialogsAndExplorer, handoff, OpenStore, NextTurn);
        moving.Moved += opened => store = opened;
        transferring = new Transferring(listing, notices, keeper, seams.DialogsAndExplorer, seams.RecycleBin, handoff, openWith, moving, ApplyTravelling,
            LocalClock.Instance, HeldFileRetry.Duration);
        window = new StashWindow(listing, notices, keeper, new Import(listing, notices, HeldFileRetry.Duration), handoff, editing, openWith, moving, seams);
        window.Follow(notification, captured);
        var capture = new CaptureSession(seams.Screen, new RegionOverlay(), new RecordingFrame(seams.QuietWindows, seams.Screen, trayMenu.Name),
            GifEncoder.Opener(HeldFileRetry.Duration), LocalClock.Instance, failure => notifications.Raise(failure));
        screenshot = new Screenshot(listing, notifications, captured, handoff, capture, keeper, window.CaptureHiding, window, HeldFileRetry.Duration);
        window.ScreenshotRequested += TakeScreenshot;
        // Made once: it follows the session's recording for the tray from here on.
        recordClip = new RecordClip(listing, notifications, captured, handoff, capture, keeper, window.CaptureHiding, window, trayMenu.SetRecording, HeldFileRetry.Duration);
        window.RecordRequested += RecordClip;
        window.SettingsRequested += OpenSettings;
        moving.WaitForTray(screenshot, recordClip, stashClipboardImage, captured, openWith, notifications);
        return listing;
    }

    /// <summary>A later turn of the UI thread, after everything queued before it: when Move reads what it waits for again.</summary>
    private static Task NextTurn()
    {
        var turn = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(() => turn.SetResult(), DispatcherPriority.Background);
        return turn.Task;
    }

    /// <summary>
    /// A write of the settings still refused after its retry (G35): named in the stash window's notices, and on the Settings
    /// page while it is open, where the change may have been made with the stash window hidden.
    /// </summary>
    internal static void SettingsWriteFailed(Failure failure, Notices notices, SettingsPage? openPage)
    {
        notices.Raise(failure);
        openPage?.WriteFailed(failure);
    }

    private SettingsLoad ReadSettings(TrayMenu trayMenu)
    {
        var load = Settings.Load(seams.Locations.SettingsFolder, HeldFileRetry.Duration);
        if (load.Failure is not null)
        {
            trayMenu.Name(load.Failure);
        }

        return load;
    }

    /// <summary>
    /// Rewritten at every start, so a moved folder still starts with Windows (F1). While the settings file cannot be
    /// read, whether the setting is on is unknown, and the entry is left as it is.
    /// </summary>
    internal static Outcome ApplyStartWithWindows(SettingsLoad settings, IStartWithWindows startWithWindows, string executablePath)
    {
        if (!settings.AsSaved)
        {
            return Outcome.Done;
        }

        return StartWithWindowsRule.Apply(settings.Settings.StartWithWindows, startWithWindows, executablePath);
    }

    /// <summary>
    /// The settings that travel in an export put in effect in the running app at once, after an import replaced them (G36):
    /// every window's theme (F10), the four shortcuts bound again from them, one refused named in the tooltip as G33
    /// names it, and the Run entry set or cleared (F1); Also copy, the cap and the pointer are read by the next capture.
    /// The Run entry's refusal is answered to name.
    /// </summary>
    internal static Outcome ApplyTravelling(Settings settings, Action<Theme> applyTheme, GlobalShortcuts shortcuts, IStartWithWindows startWithWindows, string executablePath)
    {
        applyTheme(settings.Theme);
        shortcuts.BindFromSettings();
        return StartWithWindowsRule.Apply(settings.StartWithWindows, startWithWindows, executablePath);
    }

    /// <summary>
    /// After the start finished an import a kill interrupted (State: Import stash…): Theme, Start with Windows and the
    /// shortcuts were applied from <paramref name="applied"/>, read before, so when the import's record has replaced them
    /// since, <paramref name="now"/> is applied as after a live import (G36); otherwise nothing is.
    /// </summary>
    internal static Outcome ApplyFinishedImport(TravellingSettings applied, Settings now, Func<Settings, Outcome> applyTravelling) =>
        TravellingSettings.Of(now) == applied ? Outcome.Done : applyTravelling(now);

    /// <summary>The travelling settings put in effect in this app, as <see cref="ApplyTravelling"/> says.</summary>
    private Outcome ApplyTravelling(Settings settings) =>
        ApplyTravelling(settings, ApplyTheme, shortcuts!, seams.StartWithWindows, Environment.ProcessPath!);

    private static void NameFailure(TrayMenu trayMenu, Outcome outcome)
    {
        if (!outcome.Succeeded)
        {
            trayMenu.Name(outcome.Failure);
        }
    }

    /// <summary>
    /// Opens the stash where Settings says, after the tray, so ImageStash shows it is alive while a held file is
    /// retried, and on the store's own thread, so the tray answers meanwhile; first finishing what a Move killed at any
    /// moment left (a partial copy deleted, an old folder's removal finished) and what an import's record left (the import
    /// finished, the stash staying closed while it cannot be), so the folder Settings names is the one whole stash (G28,
    /// State: Move, Import stash…). Nothing can reach the stash until it has opened and resolved its pending steps
    /// (G28); what could not be finished, opened or resolved is named in the tooltip (G35).
    /// </summary>
    private void OpenStash(IClassicDesktopStyleApplicationLifetime desktop, TrayMenu trayMenu, SettingsLoad settings, Listing listing, SettingsKeeper keeper)
    {
        var folder = StashLocation(settings, seams.Locations);
        if (!folder.Succeeded)
        {
            trayMenu.Name(folder.Failure);
            listing.StashFailed(folder.Failure);
            return;
        }

        _ = OpenAtStart(folder.Value!, trayMenu, settings, listing, keeper);
        // The store the field holds at exit: the start's, or the one a Move opened since.
        desktop.Exit += (_, _) => store?.Dispose();
    }

    private async Task OpenAtStart(string folder, TrayMenu trayMenu, SettingsLoad settings, Listing listing, SettingsKeeper keeper)
    {
        foreach (var failure in await StashMove.FinishAtStart(keeper, folder, HeldFileRetry.Duration))
        {
            trayMenu.Name(failure);
        }

        foreach (var failure in await StashImport.FinishAtStart(keeper, seams.RecycleBin, folder, HeldFileRetry.Duration))
        {
            trayMenu.Name(failure);
        }

        // Theme, Start with Windows and the shortcuts were applied from the file read before the import was finished.
        NameFailure(trayMenu, ApplyFinishedImport(TravellingSettings.Of(settings.Settings), keeper.Current, ApplyTravelling));

        // An import still recorded may have renamed the stash away: opening its folder would make an empty stash there.
        if (keeper.Current.ImportPending is not null)
        {
            var unfinished = new Failure(StashImport.FinishAction, Path.GetFileName(folder), TransferReasons.Unfinished);
            trayMenu.Name(unfinished);
            listing.StashFailed(unfinished);
            return;
        }

        await Opened(await OpenStore(folder), trayMenu, settings, listing);
    }

    /// <summary>
    /// A stash folder opened as the start opens it, on the store's own thread: its pending steps resolved, and its Open
    /// with… copies, kept beside the settings outside the stash folder (Composition: Store, G30), classified; Move opens
    /// the folder it switched to the same way.
    /// </summary>
    private Task<StoreOpening> OpenStore(string folder) =>
        StashStore.Open(folder, seams.RecycleBin, HeldFileRetry.Duration, LocalClock.Instance, FinishedPicture.Render, OpenWithRoot.Under(seams.Locations.SettingsFolder), MediaDecoder.ReadPicture);

    /// <summary>
    /// Hands the opened stash to the window, which until now showed the opening and nothing of the stash (State: Every
    /// change), or names why it did not open there; on the first run with an empty stash, opens the window pinned (G2).
    /// </summary>
    private async Task Opened(StoreOpening opening, TrayMenu trayMenu, SettingsLoad settings, Listing listing)
    {
        store = NameOpening(opening, trayMenu);
        if (store is null)
        {
            listing.StashFailed(opening.Failures[0]);
            return;
        }

        await listing.StashOpened(store);
        if (await OpensPinnedAtStart(settings, store))
        {
            window!.SummonPinned();
        }
    }

    /// <summary>
    /// G2: the window opens pinned at start only on the first run, and only when the stash holds nothing, asked of the
    /// store itself, so a query typed into a window summoned during the opening does not hide that it is empty.
    /// </summary>
    internal static async Task<bool> OpensPinnedAtStart(SettingsLoad settings, StashStore store) =>
        settings.FirstRun && await store.All() is { Succeeded: true, Value.Count: 0 };

    /// <summary>
    /// Where Settings puts the stash, or the default location. While the settings file cannot be read, where the stash
    /// is is unknown, and it stays closed: one opened at the default instead would take imports that the next start,
    /// reading the real location, would not show (G28).
    /// </summary>
    internal static Outcome<string> StashLocation(SettingsLoad settings, ILocations locations) =>
        settings.AsSaved
            ? Outcome<string>.Of(settings.Settings.StashFolder ?? locations.DefaultStashFolder)
            : new Failure("Open the stash", Settings.FileName, "the settings could not be read, so where the stash is is unknown");

    /// <summary>Names every failure the opening reports and hands back the store, or null when it did not open.</summary>
    internal static StashStore? NameOpening(StoreOpening opening, TrayMenu trayMenu)
    {
        foreach (var failure in opening.Failures)
        {
            trayMenu.Name(failure);
        }

        return opening.Store;
    }

    /// <summary>
    /// The four global shortcuts, read from the settings (F5), which Settings remaps: the open shortcut, whose press
    /// reaches the window from the Shortcuts seam's thread through the one summon, with the window in front at the press
    /// (G1, G8); Take a screenshot and Record a clip, whose acts run on the UI thread (G14, G15); and Stash clipboard
    /// image, whose act runs on the UI thread and opens no window (G13). A shortcut that cannot be registered is named in
    /// the tooltip and stops nothing else (G33).
    /// </summary>
    private GlobalShortcuts Shortcuts(SettingsKeeper settings, TrayMenu trayMenu)
    {
        var summon = new ShortcutSummon(
            seams.Target,
            seams.QuietWindows,
            () => window?.PasteTarget.Current,
            inFront => Dispatcher.UIThread.Post(() => window?.PasteTarget.FromPress(inFront)),
            anchor => Dispatcher.UIThread.Post(() => window?.SummonFromShortcut(anchor)));
        return new GlobalShortcuts(settings, GlobalShortcuts.Bindings(summon.Pressed, TakeScreenshot, RecordClip, StashFromShortcut),
            seams.Shortcuts, trayMenu.NameUnavailableShortcut, action => Dispatcher.UIThread.Post(action));
    }

    private void ListenForShowRequests() =>
        seams.OneInstance.ListenForShowRequests(() => Dispatcher.UIThread.Post(ShowWindow));

    /// <summary>A left-click on the tray icon, Open ImageStash, and a second launch all summon the window alike (G3, G34).</summary>
    private void ShowWindow() => window?.SummonFromTray();

    /// <summary>
    /// Take a screenshot from its shortcut, on the UI thread, the tray menu and the window's own menu: one act, which names
    /// its own failures (G14, G34).
    /// </summary>
    private void TakeScreenshot() => _ = screenshot?.Take();

    /// <summary>
    /// Record a clip from its shortcut, on the UI thread, the tray menu and the window's own menu: one act, which starts the
    /// recording while its region waits, stops it while it samples, and names its own failures (G15, G34).
    /// </summary>
    private void RecordClip() => _ = recordClip?.StartOrStop();

    /// <summary>
    /// The tray menu's Settings and the stash window's button: the one page, brought forward while open, else opened afresh
    /// over the settings in effect, their one keeper and the shortcuts, Theme and Look applied to every window at once (F4, F10, G32,
    /// G34), the window following it (G2); on the row of the first shortcut that is unavailable, while one is (G33).
    /// </summary>
    private void OpenSettings()
    {
        if (keeper is not { } settings || settingsLoad is not { } load || shortcuts is not { } bound || moving is not { } move || transferring is not { } transfer || window is not { } stash)
        {
            return;
        }

        var open = settingsWindow;
        settingsWindow = SettingsWindow.Open(open, () => new SettingsPage(settings, load, bound, seams.Locations, seams.StartWithWindows, seams.DialogsAndExplorer,
            move, transfer, stash.ThumbnailSizing, ApplyTheme, Environment.ProcessPath!),
            bound.FirstUnavailable);
        if (!ReferenceEquals(open, settingsWindow))
        {
            window?.Follow(settingsWindow);
        }
    }

    /// <summary>The tray menu's Stash clipboard image: the act, which names its own failures and opens no window (G13, G34).</summary>
    private void StashFromTray() => _ = stashClipboardImage?.Stash();

    /// <summary>A press of Stash clipboard image's shortcut, on the UI thread: the same act (F5, G13).</summary>
    private void StashFromShortcut() => _ = stashClipboardImage?.Stash();
}
