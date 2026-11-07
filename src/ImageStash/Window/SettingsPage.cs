using ImageStash.Seams;
using ImageStash.Store;

namespace ImageStash.Window;

/// <summary>
/// The Settings page's model, F4's one separate page (G32): its rows in G32's order, each value read from the settings
/// in effect whenever it is asked, so a page opened again later shows what holds then; the four shortcuts as
/// press-to-record fields (<see cref="ShortcutRecording"/>), one recording at a time, each remap registered and written
/// by the shortcuts (G33); each other change taking effect at once and written through the one settings keeper (State:
/// Settings): Also copy, the cap and the pointer, which the next capture reads; Theme and Look, applied to every window
/// now (F10, G32); Start with Windows, set or cleared through its seam now (F1); Open folder, which asks the platform's file
/// browser for the stash folder; and Move…, which carries the stash to a chosen empty folder (G30), the row showing the
/// Move's busy state and the folder it started from until it has ended, and every other row taking no change meanwhile
/// (R9); Export stash… and Import stash…, which carry the whole stash and the settings that travel into one file and
/// back (G36), each row showing its busy state and what it did, the import's offer inside its row (F4), and every row
/// taking no change while either runs. A row's act that fails is shown under that row as the failure's sentence, unchanged,
/// until that row's next outcome; a write of the settings still refused after its retry is shown at the top until the
/// next change, which writes them again (G35). It builds no control: <see cref="SettingsWindow"/> draws it, and the
/// portable checks drive it.
/// </summary>
public sealed class SettingsPage
{
    // The shortcut rows are named as the tray menu names their acts.
    public const string OpenTitle = TrayMenu.OpenHeader;
    public const string TakeScreenshotTitle = TrayMenu.TakeScreenshotHeader;
    public const string RecordClipTitle = TrayMenu.RecordClipHeader;
    public const string StashClipboardImageTitle = TrayMenu.StashClipboardImageHeader;
    public const string AlsoCopyTitle = "Also copy it to the clipboard";
    public const string MaximumLengthTitle = "Maximum length";
    public const string ShowPointerTitle = "Show the mouse pointer";
    public const string StashFolderTitle = "Where your stash is stored";
    public const string ExportTitle = "Export your stash";
    public const string ImportTitle = "Import a stash";
    public const string StartWithWindowsTitle = "Start with Windows";
    public const string ThemeTitle = "Theme";

    /// <summary>The stash location row's second button, beside Open folder, and its automation name (G32).</summary>
    public const string MoveWords = "Move…";

    /// <summary>The Export row's button and its automation name, G32's title, so it never reads as Import files… (G12).</summary>
    public const string ExportWords = "Export stash…";

    /// <summary>The Import row's button and its automation name, G32's title (G12, G36).</summary>
    public const string ImportWords = "Import stash…";

    /// <summary>The offer's confirming button, inside the Import row, and its automation name (G36, F4).</summary>
    public const string ConfirmImportWords = "Import";

    /// <summary>The offer's cancelling button, inside the Import row, and its automation name.</summary>
    public const string CancelImportWords = "Cancel";

    /// <summary>What the page says beside the start's read failure: the keeper writes nothing then, so nothing here is kept.</summary>
    public const string NotKept = "Changes made here last until ImageStash quits; the settings file is left as it is.";

    /// <summary>The short list of recording lengths offered, in seconds (L2), 30 among them as G32's default.</summary>
    public static readonly IReadOnlyList<int> Lengths = [10, 15, 30, 60];

    /// <summary>Theme's three choices (G32, F10).</summary>
    public static readonly IReadOnlyList<(Theme Theme, string Words)> Themes = [(Theme.System, "Use system setting"), (Theme.Light, "Light"), (Theme.Dark, "Dark")];

    private static readonly IReadOnlyList<SettingRow> OptionRows =
    [
        new("Shortcuts", OpenTitle, "From any app. Opens at your text cursor."),
        new("Shortcuts", TakeScreenshotTitle, "Drag over a region; it is saved to the stash when you release"),
        new("Shortcuts", RecordClipTitle, "Drag over a region, then Record; the same shortcut starts and stops it"),
        new("Shortcuts", StashClipboardImageTitle, "Save the image you just copied, without opening anything"),
        new("After a screenshot or a recording", AlsoCopyTitle, "So you can paste it right away with Ctrl+V"),
        new("Recording", MaximumLengthTitle, "Recording stops on its own after this"),
        new("Recording", ShowPointerTitle, null),
        new("Library", StashFolderTitle, "A plain folder you can open. If it sits in a folder synced between PCs, use it from one PC at a time."),
        new("Library", ExportTitle, "One file with every item and the settings that travel, to take your stash to another PC"),
        new("Library", ImportTitle, "Replaces your stash and those settings with an export's; what it replaces goes to the Recycle Bin"),
        new("General", StartWithWindowsTitle, "ImageStash lives in the tray; it has no window until you summon it"),
        new("General", ThemeTitle, null),
    ];

    private readonly SettingsKeeper keeper;
    private readonly SettingsLoad load;
    private readonly ILocations locations;
    private readonly IStartWithWindows startWithWindows;
    private readonly IDialogsAndExplorer dialogs;
    private readonly Action<Theme> applyTheme;
    private readonly string executablePath;
    private readonly GlobalShortcuts shortcuts;
    private readonly Moving moving;
    private readonly Transferring transferring;
    private readonly Dictionary<string, Failure> warnings = [];
    private string? moved;

    /// <param name="load">The start's read of the settings file, whose failure the page names while nothing is written.</param>
    /// <param name="shortcuts">The global shortcuts, which the shortcut fields remap and whose changes the page follows until <see cref="Close"/>.</param>
    /// <param name="moving">Move…, whose state the page follows until <see cref="Close"/>; a Move goes on when the page closes.</param>
    /// <param name="transferring">Export stash… and Import stash…, whose state the page follows until <see cref="Close"/>; either goes on when the page closes.</param>
    /// <param name="applyTheme">Gives every window the theme at once (F10).</param>
    /// <param name="executablePath">What Start with Windows registers: the running executable (F1).</param>
    public SettingsPage(SettingsKeeper keeper, SettingsLoad load, GlobalShortcuts shortcuts, ILocations locations, IStartWithWindows startWithWindows, IDialogsAndExplorer dialogs,
        Moving moving, Transferring transferring, Action<Theme> applyTheme, string executablePath)
    {
        this.moving = moving;
        this.transferring = transferring;
        this.keeper = keeper;
        this.load = load;
        this.shortcuts = shortcuts;
        this.locations = locations;
        this.startWithWindows = startWithWindows;
        this.dialogs = dialogs;
        this.applyTheme = applyTheme;
        this.executablePath = executablePath;
        ShortcutFields =
        [
            new(ShortcutAct.Open, OpenTitle, shortcuts, EndOtherRecordings, RaiseChanged),
            new(ShortcutAct.Screenshot, TakeScreenshotTitle, shortcuts, EndOtherRecordings, RaiseChanged),
            new(ShortcutAct.Record, RecordClipTitle, shortcuts, EndOtherRecordings, RaiseChanged),
            new(ShortcutAct.StashClipboardImage, StashClipboardImageTitle, shortcuts, EndOtherRecordings, RaiseChanged),
        ];
        shortcuts.Changed += RaiseChanged;
        moving.Changed += RaiseChanged;
        transferring.Changed += RaiseChanged;
    }

    /// <summary>Raised after a row's value or warning changed, on the thread that changed it, the UI thread.</summary>
    public event Action? Changed;

    /// <summary>The rows in G32's order, each with its group; a row's title is its control's automation name.</summary>
    public IReadOnlyList<SettingRow> Rows => OptionRows;

    /// <summary>The four shortcut fields, in F5's order, each named by its row's title.</summary>
    public IReadOnlyList<ShortcutRecording> ShortcutFields { get; }

    /// <summary>The field of the row titled <paramref name="title"/>, or null for a row of another kind.</summary>
    public ShortcutRecording? ShortcutField(string title) => ShortcutFields.FirstOrDefault(field => field.Title == title);

    /// <summary>The field of <paramref name="act"/>'s shortcut, on whose row Settings opens while it is unavailable (G33).</summary>
    public ShortcutRecording ShortcutField(ShortcutAct act) => ShortcutFields.Single(field => field.Act == act);

    /// <summary>
    /// Why the settings file could not be read at start, while that holds: the defaults stand in, and the keeper writes
    /// nothing, so a change here takes effect for this run only (G35). Null when the file was read.
    /// </summary>
    public Failure? ReadFailure => load.AsSaved ? null : load.Failure;

    /// <summary>The last write of the settings that was still refused after its retry, until the next change writes them again (G35).</summary>
    public Failure? WriteFailure { get; private set; }

    public bool AlsoCopyCapture => keeper.Current.AlsoCopyCapture;

    public int RecordingMaxSeconds => keeper.Current.RecordingMaxSeconds;

    /// <summary>
    /// The lengths offered, shortest first: <see cref="Lengths"/>, and the value in effect when the list lacks it, so a
    /// length another build or a hand wrote is shown as it is and rewritten only when the user picks another.
    /// </summary>
    public IReadOnlyList<int> RecordingLengths =>
        Lengths.Contains(RecordingMaxSeconds) ? Lengths : Lengths.Append(RecordingMaxSeconds).Order().ToList();

    public bool RecordPointer => keeper.Current.RecordPointer;

    /// <summary>
    /// Where the stash is: the folder Settings names, else the default location (F6); while a Move is asked, the folder it
    /// started from, since the settings take the new one the moment the switch is written, before the old folder is
    /// removed (R9).
    /// </summary>
    public string StashFolder => moving.Asked ? moving.From! : keeper.Current.StashFolder ?? locations.DefaultStashFolder;

    /// <summary>A Move is asked.</summary>
    public bool MoveAsked => moving.Asked;

    /// <summary>A Move, an export or an import is asked: every control of the page but its window's Close takes no change until it has ended.</summary>
    public bool WholeStashActAsked => moving.Asked || transferring.Asked;

    /// <summary>The line under the stash folder's path: the Move's busy state while one is asked, else what the last one said on success, or null.</summary>
    public string? MoveLine => moving.Asked ? moving.Busy : moved;

    /// <summary>The line under the Export row: the export's busy state while one is asked, else where the last one wrote its file, or null (G36).</summary>
    public string? ExportLine => transferring.ExportLine;

    /// <summary>The line under the Import row: the import's busy state while one is asked, else what the last one said on success, or null (G36).</summary>
    public string? ImportLine => transferring.ImportLine;

    /// <summary>The offer's sentence inside the Import row, with Import and Cancel beside it, while a chosen file waits for confirmation; else null (G36, F4).</summary>
    public string? ImportOffer => transferring.Offer is { } offer ? Transferring.OfferWords(offer) : null;

    public bool StartWithWindows => keeper.Current.StartWithWindows;

    public Theme Theme => keeper.Current.Theme;

    /// <summary>A length in the words the list shows it in.</summary>
    public static string LengthWords(int seconds) => seconds == 1 ? "1 second" : $"{seconds} seconds";

    /// <summary>The failure of the row's last act, or a shortcut field's warning, shown under the row, or null.</summary>
    public Failure? WarningOf(string title) => ShortcutField(title) is { } field ? field.Warning : warnings.GetValueOrDefault(title);

    /// <summary>Ends the recording in progress, if any, with nothing changed: the window lost the user's attention.</summary>
    public void CancelRecording()
    {
        foreach (var field in ShortcutFields)
        {
            field.Cancel();
        }
    }

    /// <summary>The page's window closed: a recording in progress ends unchanged, and the page stops following the shortcuts.</summary>
    public void Close()
    {
        CancelRecording();
        shortcuts.Changed -= RaiseChanged;
        moving.Changed -= RaiseChanged;
        transferring.Changed -= RaiseChanged;
    }

    /// <summary>Whether the next screenshot and recording are also put on the clipboard (G14, G15).</summary>
    public void SetAlsoCopyCapture(bool on)
    {
        Change(s => s with { AlsoCopyCapture = on });
        Changed?.Invoke();
    }

    /// <summary>The next recording's cap, one of <see cref="RecordingLengths"/> (G15).</summary>
    public void SetRecordingMaxSeconds(int seconds)
    {
        if (!RecordingLengths.Contains(seconds))
        {
            throw new ArgumentOutOfRangeException(nameof(seconds), seconds, "The page offers no such length.");
        }

        Change(s => s with { RecordingMaxSeconds = seconds });
        Changed?.Invoke();
    }

    /// <summary>Whether the next recording draws the pointer (G15).</summary>
    public void SetRecordPointer(bool on)
    {
        Change(s => s with { RecordPointer = on });
        Changed?.Invoke();
    }

    /// <summary>Every window takes the theme now, and it is written (F10).</summary>
    public void SetTheme(Theme theme)
    {
        applyTheme(theme);
        Change(s => s with { Theme = theme });
        Changed?.Invoke();
    }

    /// <summary>
    /// Sets or clears the Run entry now (F1) and writes the choice whatever the entry answered: the settings are the
    /// record and the entry follows them at every start (State: Settings), so a refusal is named on the row and the next
    /// start applies the choice again.
    /// </summary>
    public Outcome SetStartWithWindows(bool on)
    {
        var outcome = StartWithWindowsRule.Apply(on, startWithWindows, executablePath);
        Change(s => s with { StartWithWindows = on });
        Show(StartWithWindowsTitle, outcome);
        return outcome;
    }

    /// <summary>Opens the stash folder in the platform's file browser, whether or not the stash opened (G32).</summary>
    public Outcome OpenFolder()
    {
        var outcome = dialogs.OpenFolder(StashFolder);
        Show(StashFolderTitle, outcome);
        return outcome;
    }

    /// <summary>
    /// Move… (G30, G32): a shortcut recording ended first, then the picker owned by <paramref name="owner"/>, the wait and
    /// the Move; its success said under the path, its refusal or failure on the row as every row's is (G35).
    /// </summary>
    public async Task<Outcome> Move(Avalonia.Controls.TopLevel owner)
    {
        CancelRecording();
        moved = null;
        var outcome = await moving.MoveTo(owner);
        moved = outcome.Succeeded && moving.MovedTo is { } folder ? Moving.MovedWords(folder) : null;

        Show(StashFolderTitle, outcome);
        return outcome;
    }

    /// <summary>
    /// Export stash… (G36): a shortcut recording ended first, then the save picker owned by <paramref name="owner"/>, the
    /// wait and the export; where the file is said under the row once written, a refusal or failure on the row (G35).
    /// </summary>
    public async Task<Outcome> Export(Avalonia.Controls.TopLevel owner)
    {
        CancelRecording();
        var outcome = await transferring.Export(owner);
        Show(ExportTitle, outcome);
        return outcome;
    }

    /// <summary>
    /// Import stash… (G36): a shortcut recording ended first, then the one-file picker owned by <paramref name="owner"/>
    /// and the file's summary; the offer shown inside the row (<see cref="ImportOffer"/>), a refusal on the row (G35).
    /// </summary>
    public async Task<Outcome> Import(Avalonia.Controls.TopLevel owner)
    {
        CancelRecording();
        var outcome = await transferring.ChooseArchive(owner);
        Show(ImportTitle, outcome);
        return outcome;
    }

    /// <summary>The offer's Import: the replacement, its success said under the row, a failure on the row (G36, G35).</summary>
    public async Task<Outcome> ConfirmImport()
    {
        var outcome = await transferring.ConfirmImport();
        Show(ImportTitle, outcome);
        return outcome;
    }

    /// <summary>The offer's Cancel: the row at rest, nothing changed.</summary>
    public void CancelImport() => transferring.CancelImport();

    /// <summary>A write of the settings refused after its retry, which the keeper names (G35): shown at the top of the page.</summary>
    public void WriteFailed(Failure failure)
    {
        WriteFailure = failure;
        Changed?.Invoke();
    }

    /// <summary>A change through the one keeper, which writes every setting again, so an earlier refused write is no longer the last.</summary>
    private void Change(Func<Settings, Settings> change)
    {
        keeper.Change(change);
        WriteFailure = null;
    }

    /// <summary>The page holds one recording at a time: a field beginning ends any other's first.</summary>
    private void EndOtherRecordings(ShortcutRecording beginning)
    {
        foreach (var field in ShortcutFields.Where(field => field != beginning))
        {
            field.Cancel();
        }
    }

    private void RaiseChanged() => Changed?.Invoke();

    private void Show(string title, Outcome outcome)
    {
        if (outcome.Succeeded)
        {
            warnings.Remove(title);
        }
        else
        {
            warnings[title] = outcome.Failure;
        }

        Changed?.Invoke();
    }
}

/// <summary>One row of the Settings page: its group's heading, its title, which names its control, and the line under the title.</summary>
public sealed record SettingRow(string Group, string Title, string? Description);
