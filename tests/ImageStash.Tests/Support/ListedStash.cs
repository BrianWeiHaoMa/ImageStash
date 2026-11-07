using ImageStash.Capture;
using ImageStash.Editor;
using ImageStash.Media;
using ImageStash.Seams;
using ImageStash.Store;
using ImageStash.Window;

namespace ImageStash.Tests.Support;

/// <summary>
/// An open test stash as the window shows it: its listing, its notices on the stash's clock, and the window's import,
/// paste (over a clipboard the check presets, on the stash's clock), Delete, organising entry and rename path over them;
/// and the notifications above the tray, on the same clock, with Stash clipboard image over that clipboard raising its
/// capture notification there; and Take a screenshot and Record a clip over a screen, a region selection and a frame
/// the check presets, a window whose hiding and showing are recorded, settings held in memory at their defaults, the
/// window's handoff over the same clipboard and, for a recording, the stages it told the tray recorded; the window's
/// editing, rendering its Saves with the renderer the check gives; the window's Open with…, over dialogs the check
/// answers, its once-a-second asking on a clock the check steps, and the item menu over all of it; Move…, over the
/// settings file beside the stash, the folder picker the check answers and turns of the UI thread the check may hold,
/// waiting for all of it; Export stash… and Import stash… beside it, over the same seams, the imported settings put in
/// effect as the app puts them, through four global shortcuts registered over a recording seam, a recording Start with
/// Windows seam and the themes given kept; with every failure the listing and the settings were told of kept.
/// </summary>
public sealed class ListedStash : IDisposable
{
    private readonly List<RecordingStage> trayRecording = [];
    private bool handedOn;

    private ListedStash(TestStash stash, TimeProvider captureClock, ClipOpener? opener, TimeSpan? editRetry = null, TimeSpan? saveACopyRetry = null)
    {
        Stash = stash;
        Notices = new Notices(stash.Clock);
        Listing = new Listing(Failures.Add);
        Import = new Import(Listing, Notices, TestStash.ShortRetry);
        Clipboard = new RecordingClipboard(stash.Clock);
        Paste = new ClipboardPaste(Listing, Notices, Import, Clipboard, stash.Clock, TestStash.ShortRetry);
        Deletion = new Deletion(Listing, Notices);
        Organising = new Organising(Listing, Notices);
        Notifications = new Notices(stash.Clock);
        Handoff = new Handoff(Listing, Notices, Notifications, Clipboard, new RecordingTarget(), stash.Clock, TestStash.ShortRetry, TestStash.ShortRetry);
        Renaming = new Renaming(Listing, Notices, Handoff);
        Editing = new Editing(Listing, Notices, Handoff, (original, name, layers, format, work, retry) => Renderer(original, name, layers, format, work, retry), new ColorChoices(), editRetry ?? TestStash.ShortRetry);
        OpenWith = new OpenWith(Listing, Notifications, Editing, Dialogs, OpenWithClock, action => action());
        ItemMenu = new ItemMenu(Listing, Notices, Handoff, Organising, Deletion, Editing, OpenWith, Dialogs, saveACopyRetry ?? TestStash.ShortRetry);
        CaptureNotification = new CaptureNotification(Notifications, Listing, Handoff, new NoWindowShown(), TestStash.ShortRetry);
        StashClipboardImage = new StashClipboardImage(Listing, Notifications, CaptureNotification, Clipboard, stash.Clock, TestStash.ShortRetry, TestStash.ShortRetry);
        Capture = new CaptureSession(Screen, Selection, Frame, opener ?? GifEncoder.Opener(TestStash.ShortRetry), captureClock, failure => Notifications.Raise(failure));
        Settings = MovingStash.Keeper(stash, Failures);
        Hiding = new CaptureHiding(
            () =>
            {
                WindowSteps.Add("hide");
                return HidesAs;
            },
            hidden => WindowSteps.Add($"restore {hidden}"));
        // The window passes itself as the clipboard's owner; the recording seam has no use for one.
        Screenshot = new Screenshot(Listing, Notifications, CaptureNotification, Handoff, Capture, Settings, Hiding, null!, TestStash.ShortRetry);
        RecordClip = new RecordClip(Listing, Notifications, CaptureNotification, Handoff, Capture, Settings, Hiding, null!,
            recording =>
            {
                lock (trayRecording)
                {
                    trayRecording.Add(recording);
                }
            },
            TestStash.ShortRetry);
        Moving = new Moving(Listing, Notices, Settings, Dialogs, Handoff, folder =>
        {
            BeforeMovedStashOpens?.Invoke(folder);
            return Stash.OpenAt(folder);
        }, Turns.Next);
        Moving.WaitForWindow(Paste, Import, Handoff, Deletion, Renaming, Organising, Editing, ItemMenu, Notices);
        Moving.WaitForTray(Screenshot, RecordClip, StashClipboardImage, CaptureNotification, OpenWith, Notifications);
        Shortcuts = new GlobalShortcuts(Settings, GlobalShortcuts.Bindings(() => { }, () => { }, () => { }, () => { }), ShortcutsSeam, Unavailable.Add, action => action());
        Shortcuts.Start();
        Transferring = new Transferring(Listing, Notices, Settings, Dialogs, stash.RecycleBin, Handoff, OpenWith, Moving, ApplyTravelling, stash.Clock, TestStash.ShortRetry);
    }

    /// <summary>Export stash… and Import stash… over the same seams as Move…, the imported settings put in effect as the app puts them.</summary>
    public Transferring Transferring { get; }

    /// <summary>The four global shortcuts over <see cref="ShortcutsSeam"/>, registered from the settings at the start.</summary>
    public GlobalShortcuts Shortcuts { get; }

    public RecordingShortcuts ShortcutsSeam { get; } = new();

    /// <summary>Every shortcut named unavailable, as the tray's tooltip would name it (G33).</summary>
    public List<Failure> Unavailable { get; } = [];

    /// <summary>The Run entry's seam, which the imported Start with Windows sets or clears.</summary>
    public RecordingStartWithWindows StartWithWindows { get; } = new();

    /// <summary>Every theme the app was given, in order.</summary>
    public List<Theme> Themes { get; } = [];

    /// <summary>What the Run entry is set to.</summary>
    public const string Executable = @"C:\Apps\ImageStash\ImageStash.Windows.exe";

    /// <summary>The Settings page over this window's settings, shortcuts, dialogs, Move… and transfers.</summary>
    public SettingsPage Page() =>
        new(Settings, new SettingsLoad(Settings.Current, null, AsSaved: true), Shortcuts, new TestLocations(Stash.Folder.Root, Stash.Outside("settings")),
            StartWithWindows, Dialogs, Moving, Transferring, Themes.Add, Executable);

    /// <summary>The Settings page with <paramref name="archive"/> chosen for Import stash… and offered for confirmation.</summary>
    public async Task<SettingsPage> Offered(string archive)
    {
        Dialogs.OpenFileAnswer = () => Task.FromResult<string?>(archive);
        var page = Page();
        var chosen = await page.Import(null!);
        Assert.IsTrue(chosen.Succeeded, chosen.Failure?.Sentence);
        Assert.IsNotNull(page.ImportOffer);
        return page;
    }

    /// <summary>Import stash… of <paramref name="archive"/>, offered and confirmed.</summary>
    public async Task<Outcome> ImportStash(string archive) => await (await Offered(archive)).ConfirmImport();

    private Outcome ApplyTravelling(Settings settings) => App.ApplyTravelling(settings, Themes.Add, Shortcuts, StartWithWindows, Executable);

    public TestStash Stash { get; }

    /// <summary>Move…, as the Settings page asks it, waiting for every part here as the window and the start register them.</summary>
    public Moving Moving { get; }

    /// <summary>What the check does to the folder a Move switched to before it is opened there, as another program might.</summary>
    public Action<string>? BeforeMovedStashOpens { get; set; }

    /// <summary>The UI thread's later turns, on which Move reads what it waits for: taken at once unless the check holds them.</summary>
    public UiTurns Turns { get; } = new();

    public List<Failure> Failures { get; } = [];

    public Notices Notices { get; }

    /// <summary>The notifications above the tray, which Stash clipboard image raises.</summary>
    public Notices Notifications { get; }

    /// <summary>The capture notification's entry over those notifications; its card is drawn only by a window, which these checks never show.</summary>
    public CaptureNotification CaptureNotification { get; }

    public StashClipboardImage StashClipboardImage { get; }

    public Listing Listing { get; }

    public Import Import { get; }

    public RecordingClipboard Clipboard { get; }

    public ClipboardPaste Paste { get; }

    public Deletion Deletion { get; }

    public Organising Organising { get; }

    public Renaming Renaming { get; }

    public Editing Editing { get; }

    /// <summary>The dialogs the window opens: the Open-with dialog's answer is the check's.</summary>
    public RecordingDialogs Dialogs { get; } = new();

    /// <summary>The clock Open with…'s once-a-second asking runs on, which moves only when the check steps it.</summary>
    public SteppedClock OpenWithClock { get; } = new();

    /// <summary>The window's Open with…, watching every copy it opened the dialog on until the check ends.</summary>
    public OpenWith OpenWith { get; }

    /// <summary>The item menu, whose entries and the preview's ⋯ share one path for each action.</summary>
    public ItemMenu ItemMenu { get; }

    /// <summary>What the editor's Saves render with: the product's renderer, unless the check sets another.</summary>
    public FinishedPictureRenderer Renderer { get; set; } = FinishedPicture.Render;

    /// <summary>The window's handoff, whose copies a capture's copy waits behind.</summary>
    public Handoff Handoff { get; }

    public RecordingScreen Screen { get; } = new();

    /// <summary>The overlay's stand-in: the region the user drags, or a cancel.</summary>
    public PresetRegionSelection Selection { get; } = new();

    /// <summary>The recording's frame and bar, which these checks never show.</summary>
    public PresetRecordingFrame Frame { get; } = new();

    public CaptureSession Capture { get; }

    /// <summary>The settings in effect, over a settings file beside the stash (<see cref="MovingStash.Keeper"/>).</summary>
    public SettingsKeeper Settings { get; }

    /// <summary>What the window's hide answers it did: the flyout hidden, unless a check says otherwise.</summary>
    public CaptureHide HidesAs { get; set; } = CaptureHide.Flyout;

    /// <summary>The window's hides and restores, in order: "hide", then "restore Flyout" or "restore Pinned".</summary>
    public List<string> WindowSteps { get; } = [];

    public CaptureHiding Hiding { get; }

    public Screenshot Screenshot { get; }

    public RecordClip RecordClip { get; }

    /// <summary>The clock the captures run on when the check steps it (<see cref="OpenRecording"/>); null otherwise.</summary>
    public SteppedClock? Stepped { get; private init; }

    /// <summary>What the act told the tray at each change of the recording's stage, in order: Start recording while waiting, the red dot and Stop recording while sampling.</summary>
    public List<RecordingStage> TrayRecording
    {
        get
        {
            lock (trayRecording)
            {
                return trayRecording.ToList();
            }
        }
    }

    /// <param name="storeRetry">How long the store retries a held file: <see cref="TestStash.ShortRetry"/> unless a check outlasts a hold of its own.</param>
    /// <param name="editRetry">How long Edit's open retries a held original: <see cref="TestStash.ShortRetry"/> unless a check holds one open for longer.</param>
    /// <param name="saveACopyRetry">How long Save a copy… retries a held file, as <paramref name="editRetry"/>.</param>
    public static async Task<ListedStash> Open(TimeSpan? storeRetry = null, TimeSpan? editRetry = null, TimeSpan? saveACopyRetry = null)
    {
        var stash = new TestStash { Retry = storeRetry ?? TestStash.ShortRetry };
        var listed = new ListedStash(stash, stash.Clock, opener: null, editRetry, saveACopyRetry);
        await listed.OpenStash();
        return listed;
    }

    /// <summary>
    /// Opens it as <see cref="Open(TimeSpan?, TimeSpan?)"/> does, both clocks reading local time five and a half hours ahead, with the
    /// captures on a clock the check steps, from 2026-10-20 12:00 UTC, so a recording runs with exact times; the clip
    /// written by Media's GIF encoder unless <paramref name="opener"/> gives another; the store retrying a held file for
    /// <paramref name="storeRetry"/>, <see cref="TestStash.ShortRetry"/> unless a check outlasts a hold of its own.
    /// </summary>
    public static async Task<ListedStash> OpenRecording(ClipOpener? opener = null, TimeSpan? storeRetry = null)
    {
        var stash = new TestStash { Retry = storeRetry ?? TestStash.ShortRetry };
        stash.Clock.Zone = SetClock.Ahead;
        var clock = new SteppedClock { Zone = SetClock.Ahead };
        var listed = new ListedStash(stash, clock, opener) { Stepped = clock };
        await listed.OpenStash();
        return listed;
    }

    /// <summary>Until the frame shows the released region waiting to be recorded.</summary>
    public Task WhileWaiting() => Handing.Eventually(() => Frame.Calls.Contains("show") && Capture.RecordingWaiting);

    /// <summary>Until the frame shows its recording look and the recording samples.</summary>
    public Task WhileSampling() => Handing.Eventually(() => Frame.Calls.Contains("record") && Capture.RecordingSampling);

    /// <summary>Until the region waits, then Record pressed on the bar, then until the recording samples.</summary>
    public async Task RecordFromTheWait()
    {
        await WhileWaiting();
        Frame.PressRecord();
        await WhileSampling();
    }

    /// <summary>Each tick of the recording the frame was given the readings of, up to <paramref name="untilMs"/>, the first at once, each frame written before the next.</summary>
    public void StepRecording(long untilMs) => SteppedRecorder.StepKeepingUp(Stepped!, (Recorder)Frame.Readings!, untilMs);

    private async Task OpenStash()
    {
        await Stash.OpenCleanly();
        await Listing.StashOpened(Stash.Store!);
    }

    /// <summary>Opens it as <see cref="Open(TimeSpan?, TimeSpan?)"/> does, its clock reading local time in <paramref name="zone"/>.</summary>
    public static async Task<ListedStash> Open(TimeZoneInfo zone)
    {
        var listed = await Open();
        listed.Stash.Clock.Zone = zone;
        return listed;
    }

    /// <summary>
    /// Ends this window as the process ending ends it (its Open with… let go, the store closed), does
    /// <paramref name="whileClosed"/> to the folder as another program would, and starts again over the same folder as
    /// the app starts: a fresh window over the settings file as it reads then, an import's record finished first, the
    /// settings it brought put in effect, and, unless the record still stands, its listing told of the store the start
    /// opened, after <paramref name="beforeListed"/>; while it stands, the listing names the import unfinished and
    /// nothing opens. With <paramref name="mustOpen"/>, the stash must open. The new one owns the folder from then on;
    /// disposing this one leaves it.
    /// </summary>
    public async Task<ListedStash> Restart(Action<TestStash>? whileClosed = null, Action<TestStash>? beforeListed = null, bool mustOpen = true)
    {
        OpenWith.Dispose();
        Settings.Flush().GetAwaiter().GetResult();
        Stash.Close();
        handedOn = true;
        whileClosed?.Invoke(Stash);
        var started = new ListedStash(Stash, Stash.Clock, opener: null);
        var applied = TravellingSettings.Of(started.Settings.Current);
        started.StartFailures.AddRange(await StashImport.FinishAtStart(started.Settings, Stash.RecycleBin, Stash.Folder.Root, Stash.Retry));
        App.ApplyFinishedImport(applied, started.Settings.Current, started.ApplyTravelling);
        if (started.Settings.Current.ImportPending is not null)
        {
            Assert.IsFalse(mustOpen, "an import's record still stands: " + string.Join("; ", started.StartFailures));
            started.Listing.StashFailed(new Failure(StashImport.FinishAction, System.IO.Path.GetFileName(Stash.Folder.Root), TransferReasons.Unfinished));
            return started;
        }

        var opening = await Stash.Open();
        if (opening.Store is null)
        {
            Assert.IsFalse(mustOpen, string.Join("; ", opening.Failures));
            started.Listing.StashFailed(opening.Failures[0]);
            return started;
        }

        beforeListed?.Invoke(Stash);
        await started.Listing.StashOpened(opening.Store);
        return started;
    }

    /// <summary>What finishing an import's record at the last <see cref="Restart"/> named.</summary>
    public List<Failure> StartFailures { get; } = [];

    /// <summary>The uses the stash has recorded, read between the store's calls.</summary>
    public long Uses() => Stash.SqlQueryWhileOpen("SELECT COUNT(*) FROM uses", row => row.GetInt64(0)).Single();

    /// <summary>The chips <paramref name="listing"/> shows, each as its tag and the items carrying it, in the row's order.</summary>
    public static List<(string, int)> Chips(Listing listing) => listing.Chips.Select(chip => (chip.Tag, chip.Items)).ToList();

    /// <summary>
    /// Closes the stash, does <paramref name="whileClosed"/> to its folder as a second program or a kill would, and
    /// opens it again as start does, the listing showing it; returns what the opening named.
    /// </summary>
    public async Task<StoreOpening> Reopen(Action<TestStash>? whileClosed = null)
    {
        Stash.Close();
        whileClosed?.Invoke(Stash);
        var opening = await Stash.Open();
        Assert.IsNotNull(opening.Store, string.Join("; ", opening.Failures));
        await Listing.StashOpened(opening.Store);
        return opening;
    }

    public static DroppedItem File(string path) => new(System.IO.Path.GetFileName(path), path, null, IsFolder: false);

    public static DropContents Contents(IReadOnlyList<string> paths) => new(paths.Select(File).ToList(), false, false, []);

    public void Dispose()
    {
        OpenWith.Dispose();
        // A change a check made just before it ended is still being written beside the stash, which would hold the file the
        // scratch folder's deletion meets.
        Settings.Flush().GetAwaiter().GetResult();
        if (!handedOn)
        {
            Stash.Dispose();
        }
    }
}

/// <summary>Quiet windows for the portable checks, which show no window: asked to show or activate one, they fail the check.</summary>
public sealed class NoWindowShown : IQuietWindows
{
    public void ShowWithoutActivating(Avalonia.Controls.Window window) => throw new NotSupportedException("The portable checks show no window.");

    public Outcome ShowOnly(Avalonia.Controls.Window window, IReadOnlyList<Avalonia.PixelRect> parts) => throw new NotSupportedException("The portable checks show no window.");

    public Outcome<IDisposable> TakeActivation(Avalonia.Controls.Window window) => throw new NotSupportedException("The portable checks show no window.");

    public void RoundCorners(Avalonia.Controls.Window window) => throw new NotSupportedException("The portable checks show no window.");

    public Avalonia.PixelRect? TrayIconRectangle() => null;

    public Avalonia.PixelPoint? PointerPosition() => null;

    public Avalonia.PixelRect? FrontWindowRectangle() => null;
}

/// <summary>
/// The UI thread's later turns for the portable checks, which have no UI thread: each is taken a moment later, as a
/// posting runs after the stretch that posted it, and what runs on it until its first wait runs under one lock with what
/// the check does through <see cref="Act{T}"/>, so the two never interleave, as on a UI thread; while the check holds the
/// turns, none is taken until it lets them go.
/// </summary>
public sealed class UiTurns
{
    private readonly object gate = new();
    private readonly object turn = new();
    private TaskCompletionSource? held;

    public Task Next()
    {
        Task? waitFor;
        lock (gate)
        {
            waitFor = held?.Task;
        }

        // Its continuation runs inline as it completes, inside the turn's lock.
        var taken = new TaskCompletionSource();
        _ = Task.Run(async () =>
        {
            await Task.Delay(1);
            if (waitFor is not null)
            {
                await waitFor;
            }

            lock (turn)
            {
                taken.SetResult();
            }
        });
        return taken.Task;
    }

    /// <summary>Does <paramref name="act"/>'s synchronous stretch as one turn of the UI thread would, never beside a turn Move takes.</summary>
    public T Act<T>(Func<T> act)
    {
        lock (turn)
        {
            return act();
        }
    }

    /// <summary>Does <paramref name="act"/> as one turn of the UI thread would, never beside a turn Move takes.</summary>
    public void Act(Action act)
    {
        lock (turn)
        {
            act();
        }
    }

    public void Hold()
    {
        lock (gate)
        {
            held ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    public void Release()
    {
        TaskCompletionSource? letGo;
        lock (gate)
        {
            (letGo, held) = (held, null);
        }

        letGo?.TrySetResult();
    }
}
