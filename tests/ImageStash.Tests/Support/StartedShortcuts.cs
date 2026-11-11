using ImageStash.Seams;
using ImageStash.Store;
using ImageStash.Window;
using SettingsFile = ImageStash.Store.Settings;

namespace ImageStash.Tests.Support;

/// <summary>
/// F5's four shortcuts as ImageStash binds them, from these settings' texts, over <see cref="RecordingShortcuts"/>, with
/// the tray menu shown on a <see cref="RecordingTray"/>, naming what is unavailable in its tooltip, then started: each
/// act counted where it runs, and what the policy posts to the UI thread waiting in <see cref="Posted"/> until the check
/// runs it. The settings are kept in a folder the check gives, written there first, or held in memory and never written.
/// </summary>
public sealed class StartedShortcuts
{
    private static readonly TimeSpan ShortRetry = TimeSpan.FromMilliseconds(300);

    private readonly string? settingsFolder;

    private StartedShortcuts(SettingsKeeper settings, string? settingsFolder)
    {
        Settings = settings;
        this.settingsFolder = settingsFolder;
    }

    /// <param name="settingsFolder">Where the settings are written, after a quiet spell of <paramref name="quiet"/>; held in memory when null.</param>
    /// <param name="held">Combinations the seam refuses as another program's from the start.</param>
    public static StartedShortcuts Start(string open = "Ctrl+Alt+Space", string stash = "Ctrl+Alt+V", string screenshot = "Ctrl+PrtScn", string record = "Ctrl+Shift+PrtScn",
        string? settingsFolder = null, TimeSpan? quiet = null, params Shortcut[] held)
    {
        var settings = new SettingsFile { Shortcuts = new Shortcuts { Open = open, Screenshot = screenshot, Record = record, StashClipboardImage = stash } };
        if (settingsFolder is not null)
        {
            Assert.IsTrue(SettingsFile.Write(settingsFolder, settings, ShortRetry).Succeeded);
        }

        var failures = new List<Failure>();
        var keeper = new SettingsKeeper(settingsFolder ?? "", new SettingsLoad(settings, null, AsSaved: settingsFolder is not null), ShortRetry, quiet ?? TimeSpan.FromSeconds(30), failures.Add);
        var started = new StartedShortcuts(keeper, settingsFolder) { WriteFailures = failures };
        started.Menu = new TrayMenu(started.Tray, () => started.Opened++, () => started.ScreenshotsFromTray++, () => started.RecordedFromTray++, () => started.StashedFromTray++, () => started.SettingsOpened++, () => { });
        started.Shortcuts = new GlobalShortcuts(keeper, GlobalShortcuts.Bindings(() => started.Summoned++, () => started.Screenshots++, () => started.Recorded++, () => started.Stashed++),
            started.Seam, started.Menu.NameUnavailableShortcut, started.Posted.Add);
        started.Menu.Show(started.Shortcuts);
        foreach (var shortcut in held)
        {
            started.Seam.Refuse(shortcut, ShortcutReasons.HeldByAnotherProgram);
        }

        started.Shortcuts.Start();
        return started;
    }

    /// <summary>Settings held in memory and never written, with these shortcuts' texts or the defaults.</summary>
    public static SettingsKeeper SettingsInMemory(Shortcuts? texts = null) =>
        new("", new SettingsLoad(new SettingsFile { Shortcuts = texts ?? new Shortcuts() }, null, AsSaved: false), ShortRetry, TimeSpan.Zero, _ => { });

    public RecordingTray Tray { get; } = new();

    public RecordingShortcuts Seam { get; } = new();

    public SettingsKeeper Settings { get; }

    /// <summary>The settings' writes still refused after their retry.</summary>
    public List<Failure> WriteFailures { get; private init; } = [];

    public TrayMenu Menu { get; private set; } = null!;

    public GlobalShortcuts Shortcuts { get; private set; } = null!;

    /// <summary>What the policy posted to the UI thread, not yet run.</summary>
    public List<Action> Posted { get; } = [];

    public int Opened { get; set; }

    public int ScreenshotsFromTray { get; set; }

    public int RecordedFromTray { get; set; }

    public int StashedFromTray { get; set; }

    public int SettingsOpened { get; set; }

    /// <summary>Presses of the open shortcut, counted where its act runs.</summary>
    public int Summoned { get; set; }

    /// <summary>Presses of the screenshot shortcut, counted where its act runs.</summary>
    public int Screenshots { get; set; }

    /// <summary>Presses of the record shortcut, counted where its act runs.</summary>
    public int Recorded { get; set; }

    /// <summary>Presses of Stash clipboard image's shortcut, counted where its act runs.</summary>
    public int Stashed { get; set; }

    /// <summary>The settings file as it reads now, in the folder the check gave.</summary>
    public string SavedText => File.ReadAllText(Path.Combine(settingsFolder!, SettingsFile.FileName));

    /// <summary>The shortcuts the settings file holds now.</summary>
    public Shortcuts Saved => SettingsFile.Load(settingsFolder!, ShortRetry).Settings.Shortcuts;

    /// <summary>The Settings page over these shortcuts and their settings, the rest of its seams recording.</summary>
    public SettingsPage Page()
    {
        var moving = MovingStash.NeverOpened(Settings, new RecordingDialogs());
        return new(Settings, new SettingsLoad(Settings.Current, null, AsSaved: true), Shortcuts, new TestLocations(@"C:\Users\someone\AppData\Local\ImageStash\stash", @"C:\Users\someone\AppData\Local\ImageStash"),
            new RecordingStartWithWindows(), new RecordingDialogs(), moving, MovingStash.NeverOpenedTransfers(Settings, new RecordingDialogs(), moving), new ThumbnailSizing(Settings), _ => { }, @"C:\Apps\ImageStash\ImageStash.Windows.exe");
    }

    /// <summary>Runs what was posted to the UI thread, as the UI thread later would.</summary>
    public void RunPosted()
    {
        var posted = Posted.ToList();
        Posted.Clear();
        foreach (var action in posted)
        {
            action();
        }
    }
}
