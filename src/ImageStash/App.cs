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
/// (created hidden, G1), the global shortcuts, the window shown as the kind of start asks and the stash, show requests.
/// </summary>
public sealed class App : Application
{
    private readonly PlatformSeams seams;
    private readonly IReadOnlyList<string> args;
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

    /// <param name="args">The start's command-line arguments, which tell the start at sign-in from a launch by hand (G3).</param>
    public App(PlatformSeams seams, IReadOnlyList<string> args)
    {
        this.seams = seams;
        this.args = args;
    }

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

        trayMenu.NameIfFailed(ApplyStartWithWindows(settings, seams.StartWithWindows, Environment.ProcessPath!));
        var notices = Notices.InWindow(LocalClock.Instance);
        // Before the shortcuts, which Settings remaps through it.
        var keeper = KeepSettings(desktop, settings, notices);
        // Before the shortcuts too, which tell them a shortcut that became unavailable (G33, G16).
        var notifications = new Notices(LocalClock.Instance);
        var shortcutNotifications = new ShortcutNotifications(notifications, act => OpenSettingsOn(act));
        var shortcuts = this.shortcuts = Shortcuts(keeper, trayMenu, shortcutNotifications);
        trayMenu.Show(shortcuts);
        shortcutNotifications.Follow(shortcuts);
        var listing = CreateWindow(notices, notifications, keeper, trayMenu);
        // The + menu and the empty stash show a shortcut remapped in Settings, and one unavailable marked so (G2, G34).
        window!.FollowShortcuts(shortcuts);
        // G33's check "at start", once the window the open shortcut summons exists and the notifications are drawn.
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
    private Listing CreateWindow(Notices notices, Notices notifications, SettingsKeeper keeper, TrayMenu trayMenu)
    {
        var listing = new Listing(failure => notices.Raise(failure));
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
        var capture = new CaptureSession(seams.Screen, new RegionOverlay(), new RecordingFrame(seams.QuietWindows, seams.Screen, trayMenu.Name, LocalClock.Instance),
            GifEncoder.Opener(HeldFileRetry.Duration), LocalClock.Instance, failure => notifications.Raise(failure));
        screenshot = new Screenshot(listing, notifications, captured, handoff, capture, keeper, window.CaptureHiding, window, HeldFileRetry.Duration);
        window.ScreenshotRequested += TakeScreenshot;
        // Made once: it follows the session's recording for the tray from here on.
        recordClip = new RecordClip(listing, notifications, captured, handoff, capture, keeper, window.CaptureHiding, window, trayMenu.SetRecording, HeldFileRetry.Duration);
        // The bar names the record shortcut while it would act (G33); the shortcuts are made before the window.
        recordClip.FollowShortcuts(shortcuts!);
        window.RecordRequested += RecordClip;
        window.SettingsRequested += OpenSettings;
        window.StashClipboardImageRequested += StashFromTray;
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

    /// <summary>The travelling settings put in effect in this app, as <see cref="ApplyTravelling"/> says.</summary>
    private Outcome ApplyTravelling(Settings settings) =>
        ApplyTravelling(settings, ApplyTheme, shortcuts!, seams.StartWithWindows, Environment.ProcessPath!);

    /// <summary>
    /// The start's last steps once the window exists (G2, G3): the window shown as the kind of start asks, through the
    /// tray's own summon (G3, G34), and the stash opened where Settings says, its store kept from the moment it opens.
    /// </summary>
    private void OpenStash(IClassicDesktopStyleApplicationLifetime desktop, TrayMenu trayMenu, SettingsLoad settings, Listing listing, SettingsKeeper keeper)
    {
        var start = new StartOpening(trayMenu, listing, seams.Locations, seams.RecycleBin, HeldFileRetry.Duration, OpenStore, ApplyTravelling,
            () => Dispatcher.UIThread.Post(ShowWindow), () => window!.SummonPinned());
        _ = start.Run(args, settings, keeper, opened => store = opened);
        // The store the field holds at exit: the start's, or the one a Move opened since.
        desktop.Exit += (_, _) => store?.Dispose();
    }

    /// <summary>
    /// A stash folder opened as the start opens it, on the store's own thread: its pending steps resolved, and its Open
    /// with… copies, kept beside the settings outside the stash folder (Composition: Store, G30), classified; Move opens
    /// the folder it switched to the same way.
    /// </summary>
    private Task<StoreOpening> OpenStore(string folder) =>
        StashStore.Open(folder, seams.RecycleBin, HeldFileRetry.Duration, LocalClock.Instance, FinishedPicture.Render, OpenWithRoot.Under(seams.Locations.SettingsFolder), MediaDecoder.ReadPicture);

    /// <summary>
    /// The four global shortcuts, read from the settings (F5), which Settings remaps: the open shortcut, whose press
    /// reaches the window from the Shortcuts seam's thread through the one summon, with the window in front at the press
    /// (G1, G8); Take a screenshot and Record a clip, whose acts run on the UI thread (G14, G15); and Stash clipboard
    /// image, whose act runs on the UI thread and opens no window (G13). A shortcut that cannot be registered is named in
    /// the tooltip, its entry marked, and said in a notification above the tray, and stops nothing else (G33, G16).
    /// </summary>
    private GlobalShortcuts Shortcuts(SettingsKeeper settings, TrayMenu trayMenu, ShortcutNotifications shortcutNotifications)
    {
        var summon = new ShortcutSummon(
            seams.Target,
            seams.QuietWindows,
            () => window?.PasteTarget.Current,
            inFront => Dispatcher.UIThread.Post(() => window?.PasteTarget.FromPress(inFront)),
            anchor => Dispatcher.UIThread.Post(() => window?.SummonFromShortcut(anchor)));
        return new GlobalShortcuts(settings, GlobalShortcuts.Bindings(summon.Pressed, TakeScreenshot, RecordClip, StashFromShortcut),
            seams.Shortcuts, problem =>
            {
                trayMenu.NameUnavailableShortcut(problem);
                shortcutNotifications.Tell(problem);
            }, action => Dispatcher.UIThread.Post(action));
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
    private void OpenSettings() => OpenSettingsOn(shortcuts?.FirstUnavailable);

    /// <summary>The page as <see cref="OpenSettings"/> opens it, on <paramref name="row"/>'s shortcut field when one is given: a notification's Choose another (G33).</summary>
    private void OpenSettingsOn(ShortcutAct? row)
    {
        if (keeper is not { } settings || settingsLoad is not { } load || shortcuts is not { } bound || moving is not { } move || transferring is not { } transfer || window is not { } stash)
        {
            return;
        }

        var open = settingsWindow;
        settingsWindow = SettingsWindow.Open(open, () => new SettingsPage(settings, load, bound, seams.Locations, seams.StartWithWindows, seams.DialogsAndExplorer,
            move, transfer, stash.ThumbnailSizing, ApplyTheme, Environment.ProcessPath!),
            row);
        if (!ReferenceEquals(open, settingsWindow))
        {
            window?.Follow(settingsWindow);
        }
    }

    /// <summary>The tray menu's Stash clipboard image, and the empty stash's: the act, which names its own failures and opens no window (G13, G34).</summary>
    private void StashFromTray() => _ = stashClipboardImage?.Stash();

    /// <summary>A press of Stash clipboard image's shortcut, on the UI thread: the same act (F5, G13).</summary>
    private void StashFromShortcut() => _ = stashClipboardImage?.Stash();
}
