using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using ImageStash.Seams;
using ImageStash.Windows.Seams;

namespace ImageStash.Windows.Tests.Support;

/// <summary>
/// The dialogs seam with the save and folder pickers answered by the check, the import picker counted and cancelled, and each file
/// the Open-with dialog was asked on, each file revealed and each folder opened kept, so no picker, dialog or File
/// Explorer window opens.
/// </summary>
public sealed class PresetDialogs : IDialogsAndExplorer
{
    /// <summary>What the save picker answers: the chosen path, or null for a cancel.</summary>
    public string? Saves { get; set; }

    public List<string> Revealed { get; } = [];

    /// <summary>How many times the import picker was asked for; each answered as cancelled, since the checks hand an import its files themselves.</summary>
    public int OpenedImportPickers { get; private set; }

    public Task<IReadOnlyList<string>?> OpenFiles(TopLevel owner, string title, IReadOnlyList<PickerFilter> filters)
    {
        OpenedImportPickers++;
        return Task.FromResult<IReadOnlyList<string>?>(null);
    }

    public Task<string?> SaveFile(TopLevel owner, string title, string suggestedFileName, PickerFilter fileType) => Task.FromResult(Saves);

    /// <summary>What the folder picker answers: the chosen folder, or null for a cancel.</summary>
    public string? PickedFolder { get; set; }

    public Task<string?> PickFolder(TopLevel owner, string title) => Task.FromResult(PickedFolder);

    /// <summary>The titles the one-file picker was shown with, in order.</summary>
    public List<string> FilePickers { get; } = [];

    /// <summary>What the one-file picker answers: the chosen file, or null for a cancel.</summary>
    public string? PickedFile { get; set; }

    public Task<string?> OpenFile(TopLevel owner, string title, IReadOnlyList<PickerFilter> filters)
    {
        FilePickers.Add(title);
        return Task.FromResult(PickedFile);
    }

    /// <summary>The files the Open-with dialog was asked on, in order.</summary>
    public List<string> OpenedWith { get; } = [];

    /// <summary>What the Open-with dialog answers once closed: done at once, unless a check holds it up with a task it completes later.</summary>
    public Func<Task<Outcome>>? OpenWithAnswer { get; set; }

    public Task<Outcome> OpenWith(TopLevel owner, string path)
    {
        OpenedWith.Add(path);
        return OpenWithAnswer?.Invoke() ?? Task.FromResult(Outcome.Done);
    }

    public Outcome Reveal(string path)
    {
        Revealed.Add(path);
        return Outcome.Done;
    }

    /// <summary>The folders the file browser was asked to open, in order; none is opened.</summary>
    public List<string> OpenedFolders { get; } = [];

    public Outcome OpenFolder(string folder)
    {
        OpenedFolders.Add(folder);
        return Outcome.Done;
    }
}

/// <summary>
/// The real clipboard seam with the native drag replaced by a preset ending, which the seam's own check exercises:
/// keeps the files of each drag and runs a check of its own while the drag is in progress.
/// </summary>
public sealed class PresetDrags : IClipboardAndDrops
{
    private readonly WindowsClipboardAndDrops real = new();

    public List<IReadOnlyList<string>> Dragged { get; } = [];

    public Action? During { get; set; }

    public Outcome<bool> Ends { get; set; } = Outcome<bool>.Of(true);

    /// <summary>While set, the drag stays in progress until this completes, with its ending.</summary>
    public Task<Outcome<bool>>? Pending { get; set; }

    public DropContents ReadDrop(IDataTransfer data) => real.ReadDrop(data);

    public Task<Outcome> Write(TopLevel owner, ClipboardContent content, TimeSpan heldClipboardRetry) => real.Write(owner, content, heldClipboardRetry);

    public Task<Outcome> WriteText(TopLevel owner, string action, string text, TimeSpan heldClipboardRetry) => real.WriteText(owner, action, text, heldClipboardRetry);

    public IReadOnlyList<long> LastWritten => real.LastWritten;

    public void ForgetWritten() => real.ForgetWritten();

    public Task<Outcome<bool>> WriteAgain(long savedItem, ClipboardContent content, TimeSpan heldClipboardRetry) => real.WriteAgain(savedItem, content, heldClipboardRetry);

    public Task<Outcome<ClipboardReading>> Read(TimeSpan heldClipboardRetry) => real.Read(heldClipboardRetry);

    public Task<Outcome<ClipboardImage?>> ReadImage(TimeSpan heldClipboardRetry) => real.ReadImage(heldClipboardRetry);

    public Task<Outcome<bool>> StartDrag(TopLevel owner, PointerPressedEventArgs press, IReadOnlyList<string> filePaths)
    {
        Dragged.Add(filePaths);
        During?.Invoke();
        return Pending ?? Task.FromResult(Ends);
    }
}

/// <summary>A Start with Windows seam that keeps what it was asked and answers with the check's refusal, so the Run key is untouched.</summary>
public sealed class PresetStartWithWindows : IStartWithWindows
{
    public List<string> Calls { get; } = [];

    /// <summary>What every call answers while set; done while null.</summary>
    public Failure? Refusal { get; set; }

    public Outcome Set(string executablePath)
    {
        Calls.Add($"set {executablePath}");
        return Refusal is { } refused ? refused : Outcome.Done;
    }

    public Outcome Clear()
    {
        Calls.Add("clear");
        return Refusal is { } refused ? refused : Outcome.Done;
    }
}

/// <summary>The real fonts seam with one family of the check's own added to its list, so a list read past the seam shows.</summary>
public sealed class PresetFonts : IFonts
{
    private readonly WindowsFonts real = new();

    /// <summary>A name no PC installs, listed after the real families.</summary>
    public string Listed { get; } = "Zz Family Only The Seam Names";

    public IReadOnlyList<string> Families() => [.. real.Families(), Listed];

    public bool IsInstalled(string family) => real.IsInstalled(family);
}

/// <summary>
/// A paste target seam whose answers the check presets: what was in front before ImageStash, the confirmation and the
/// paste, each paste kept with the target it was sent to; nothing reaches a real window.
/// </summary>
public sealed class PresetTarget : ITarget
{
    public PasteTarget? Before { get; set; }

    public Outcome Confirms { get; set; } = Outcome.Done;

    /// <summary>Runs as each paste is asked for, so a check can look at the window then.</summary>
    public Action? DuringPaste { get; set; }

    public List<PasteTarget> Pasted { get; } = [];

    /// <summary>What is in front, for a check that has the window read it after a late report; unset, nothing reads it.</summary>
    public ForegroundReading? InFront { get; set; }

    /// <summary>A target of the app <paramref name="appName"/>, its handle made from the name so two apps differ.</summary>
    public static PasteTarget App(string appName) => new(appName.GetHashCode(), appName);

    public ForegroundReading Foreground() => InFront ?? throw new NotSupportedException("The checks hand the window the press's reading themselves.");

    public PasteTarget? LastBeforeMine() => Before;

    public event Action? ReportedLate;

    /// <summary>Raises <see cref="ReportedLate"/>, as the platform's seam does on its own thread.</summary>
    public void ReportLate() => ReportedLate?.Invoke();

    public Outcome Confirm(PasteTarget target) => Confirms;

    public Task<Outcome> ActivateAndPaste(PasteTarget target, TimeSpan bound)
    {
        DuringPaste?.Invoke();
        Pasted.Add(target);
        return Task.FromResult(Outcome.Done);
    }

    public Task<PixelRect?> CaretOf(PasteTarget target, TimeSpan bound) => throw new NotSupportedException("The checks hand the window the press's anchor themselves.");
}

/// <summary>
/// The real seam, but with the tray icon in the middle of the primary display, so a notification shows above it: an
/// unactivated Windows draws its watermark over every window in the bottom-right corner, and into every grab. The icon
/// stays below the display's centre line, or the notification would stand below it instead (<see cref="DisplayPlaces.TrayIcon"/>).
/// </summary>
public sealed class TrayIconMidScreen(WindowsQuietWindows real) : IQuietWindows
{
    /// <summary>Where the icon is said to be, in physical pixels, on the primary display as the Screen seam reports it now.</summary>
    public static PixelRect Icon => DisplayPlaces.TrayIcon(DisplayPlaces.Primary());

    public void ShowWithoutActivating(Avalonia.Controls.Window window) => real.ShowWithoutActivating(window);

    public Outcome ShowOnly(Avalonia.Controls.Window window, IReadOnlyList<PixelRect> parts) => real.ShowOnly(window, parts);

    public Outcome<IDisposable> TakeActivation(Avalonia.Controls.Window window) => real.TakeActivation(window);

    public void RoundCorners(Avalonia.Controls.Window window) => real.RoundCorners(window);

    public PixelRect? TrayIconRectangle() => Icon;

    public PixelPoint? PointerPosition() => real.PointerPosition();

    public PixelRect? FrontWindowRectangle() => real.FrontWindowRectangle();
}

/// <summary>A desktop where neither the tray icon nor the pointer can be found, so a summon falls back to centring.</summary>
public sealed class NoTrayIcon : IQuietWindows
{
    public void ShowWithoutActivating(Avalonia.Controls.Window window)
    {
        window.ShowActivated = false;
        window.Show();
    }

    public Outcome ShowOnly(Avalonia.Controls.Window window, IReadOnlyList<PixelRect> parts) => throw new NotSupportedException("A summon shows its window whole.");

    /// <summary>The window activated as Avalonia activates one; its hold gives nothing back.</summary>
    public Outcome<IDisposable> TakeActivation(Avalonia.Controls.Window window)
    {
        window.Activate();
        return Outcome<IDisposable>.Of(new MemoryStream());
    }

    /// <summary>Its windows keep their square corners: what this stand-in stands in for is the tray icon alone.</summary>
    public void RoundCorners(Avalonia.Controls.Window window)
    {
    }

    public PixelRect? TrayIconRectangle() => null;

    public PixelPoint? PointerPosition() => null;

    public PixelRect? FrontWindowRectangle() => null;
}
