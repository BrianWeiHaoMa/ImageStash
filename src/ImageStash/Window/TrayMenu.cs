using ImageStash.Capture;
using ImageStash.Seams;

namespace ImageStash.Window;

/// <summary>
/// The tray menu's actions and the icon's left click (G34), and the tooltip, which names every failure since start
/// (G35), the one place a failure at start can be named before the window is shown (F4), a shortcut that cannot be
/// registered among them (G33), until Settings registers it. When the platform's tooltip cannot hold every failure
/// whole, the oldest lines are shortened first, to action and subject and then to the action alone, so none drops out
/// and the newest keeps its reason longest; an unavailable shortcut's line is shortened only after every other, since
/// the tooltip is where the tray icon says which shortcut is unavailable and why (G33), and of several such lines the
/// older is cut to its action before the newer loses its reason. Only when the actions alone do not fit together do
/// lines leave: the oldest other failures first, so the platform never cuts away which shortcut is unavailable; and
/// when the shortcuts' lines cut to their actions still do not fit together, as with all four held, each, oldest first,
/// names its combination alone, or, when even those do not fit, as with four long combinations remapped in Settings,
/// its entry's words.
/// </summary>
public sealed class TrayMenu
{
    public const string OpenHeader = "Open ImageStash";
    public const string TakeScreenshotHeader = "Take a screenshot";
    public const string RecordClipHeader = "Record a clip";
    public const string StartRecordingHeader = "Start recording";
    public const string StopRecordingHeader = "Stop recording";
    public const string StashClipboardImageHeader = "Stash clipboard image";
    public const string PauseHeader = "Pause shortcuts";
    public const string SettingsHeader = "Settings";
    public const string QuitHeader = "Quit ImageStash";

    private readonly ITray tray;
    private readonly Action open;
    private readonly Action takeScreenshot;
    private readonly Action recordClip;
    private readonly Action stashClipboardImage;
    private readonly Action openSettings;
    private readonly Action quit;
    private readonly List<(Failure Failure, bool ShortcutUnavailable)> named = [];
    private bool shown;

    /// <param name="open">Shows the stash window, from Open ImageStash and from a left-click on the icon alike.</param>
    /// <param name="takeScreenshot">Takes a screenshot, as its shortcut does (G14).</param>
    /// <param name="recordClip">Records a clip, or starts the one waiting or stops the one sampling, as its shortcut does (G15).</param>
    /// <param name="stashClipboardImage">Saves the clipboard's image without opening a window (G13).</param>
    /// <param name="openSettings">Opens the Settings page, or brings forward the one open (F4, G32).</param>
    public TrayMenu(ITray tray, Action open, Action takeScreenshot, Action recordClip, Action stashClipboardImage, Action openSettings, Action quit)
    {
        this.tray = tray;
        this.open = open;
        this.takeScreenshot = takeScreenshot;
        this.recordClip = recordClip;
        this.stashClipboardImage = stashClipboardImage;
        this.openSettings = openSettings;
        this.quit = quit;
    }

    /// <summary>Run once the menu has closed after Open ImageStash: what was opened comes forward again (G1, G34).</summary>
    public Action? OpenedMenuClosed { get; init; }

    /// <summary>Run once the menu has closed after Settings: the page comes forward again (G32).</summary>
    public Action? SettingsMenuClosed { get; init; }

    /// <summary>
    /// Shows the icon with Open ImageStash, Take a screenshot, Record a clip and Stash clipboard image, each beside its
    /// shortcut as the settings read it, whether or not it could be registered (the tooltip says when not), Pause
    /// shortcuts as a toggle, Settings, which has no shortcut of its own, and Quit ImageStash, in G34's order (G33, G34).
    /// From then on the entries follow the shortcuts: Pause's check mark, each act's shortcut once Settings changed it,
    /// and the tooltip, which names an unavailable shortcut only while that is still why it is unavailable (G33).
    /// </summary>
    public void Show(GlobalShortcuts shortcuts)
    {
        tray.Show(
            [
                .. ShortcutEntries.Select(entry => new TrayMenuEntry(entry.Header, ChosenOf(entry.Act), shortcuts.ShortcutOf(entry.Act)?.Gesture, MenuClosed: entry.Act == ShortcutAct.Open ? OpenedMenuClosed : null)),
                new TrayMenuEntry(PauseHeader, shortcuts.TogglePause, Checked: shortcuts.Paused),
                new TrayMenuEntry(SettingsHeader, openSettings, MenuClosed: SettingsMenuClosed),
                new TrayMenuEntry(QuitHeader, quit),
            ],
            ToolTip(),
            open);
        shortcuts.Changed += () => Follow(shortcuts);
        shown = true;
    }

    /// <summary>The entries of the acts F5's shortcuts run, in G34's order.</summary>
    private static readonly (string Header, ShortcutAct Act)[] ShortcutEntries =
    [
        (OpenHeader, ShortcutAct.Open),
        (TakeScreenshotHeader, ShortcutAct.Screenshot),
        (RecordClipHeader, ShortcutAct.Record),
        (StashClipboardImageHeader, ShortcutAct.StashClipboardImage),
    ];

    private Action ChosenOf(ShortcutAct act) => act switch
    {
        ShortcutAct.Open => open,
        ShortcutAct.Screenshot => takeScreenshot,
        ShortcutAct.Record => recordClip,
        ShortcutAct.StashClipboardImage => stashClipboardImage,
        _ => throw new ArgumentOutOfRangeException(nameof(act)),
    };

    private void Follow(GlobalShortcuts shortcuts)
    {
        tray.SetChecked(PauseHeader, shortcuts.Paused);
        var reasons = ShortcutEntries.Select(entry => shortcuts.UnavailableReason(entry.Act)).ToList();
        foreach (var entry in ShortcutEntries)
        {
            tray.SetGesture(entry.Header, shortcuts.ShortcutOf(entry.Act)?.Gesture);
        }

        // A shortcut registered since, or unavailable for another reason now, is no longer what its line says.
        if (named.RemoveAll(line => line.ShortcutUnavailable && !reasons.Contains(line.Failure)) > 0)
        {
            tray.SetToolTip(ToolTip());
        }
    }

    /// <summary>
    /// While a recording's region waits, the plain icon and Record a clip reading Start recording; while it samples, the
    /// icon with its red dot and the entry reading Stop recording; otherwise the plain icon and Record a clip (G15, G34).
    /// </summary>
    public void SetRecording(RecordingStage stage)
    {
        tray.SetHeader(RecordClipHeader, stage switch
        {
            RecordingStage.Waiting => StartRecordingHeader,
            RecordingStage.Sampling => StopRecordingHeader,
            _ => RecordClipHeader,
        });
        tray.SetRecording(stage == RecordingStage.Sampling);
    }

    /// <summary>Adds a failure to the tooltip, newest first, until the next start.</summary>
    public void Name(Failure failure) => Add(failure, shortcutUnavailable: false);

    /// <summary>
    /// Adds a shortcut that is unavailable, and why, whose reason the tooltip keeps longest, until the shortcuts no longer
    /// give that reason for it (G33).
    /// </summary>
    public void NameUnavailableShortcut(Failure failure) => Add(failure, shortcutUnavailable: true);

    private void Add(Failure failure, bool shortcutUnavailable)
    {
        named.Insert(0, (failure, shortcutUnavailable));
        if (shown)
        {
            tray.SetToolTip(ToolTip());
        }
    }

    private static readonly Func<Failure, string>[] Lines =
    [
        f => f.Sentence,
        f => $"{f.Action} failed: {f.Subject}",
        f => $"{f.Action} failed",
    ];

    private string ToolTip()
    {
        if (named.Count == 0)
        {
            return "ImageStash";
        }

        // The lines shown, newest first, by their index in named; how far each is shortened, an index into Lines, or for
        // an unavailable shortcut one past it, its shortest form, and two past it, its entry's words.
        var kept = Enumerable.Range(0, named.Count).ToList();
        var shortened = new int[named.Count];
        string Line(int i) => shortened[i] < Lines.Length ? Lines[shortened[i]](named[i].Failure)
            : shortened[i] == Lines.Length ? Shortest(named[i].Failure)
            : ByEntry(named[i].Failure);
        string Text() => string.Join("\n", kept.Select(Line));
        int ByActions() => kept.Sum(i => Lines[^1](named[i].Failure).Length) + kept.Count - 1;

        // Past every shortening the platform would cut the text's end, an unavailable shortcut's line with it: the
        // oldest other failures leave instead, so the tooltip still says which shortcut is unavailable (G33).
        for (var i = named.Count - 1; i >= 0 && ByActions() > tray.ToolTipLength; i--)
        {
            if (!named[i].ShortcutUnavailable)
            {
                kept.Remove(i);
            }
        }

        var oldestFirst = Enumerable.Reverse(kept).ToList();
        for (var step = 1; step < Lines.Length; step++)
        {
            foreach (var i in oldestFirst.Where(i => !named[i].ShortcutUnavailable).TakeWhile(_ => Text().Length > tray.ToolTipLength))
            {
                shortened[i] = step;
            }
        }

        // Then the shortcut lines, oldest first, each as far as it goes before a newer one loses its reason: every
        // other failure is cut to its action before the newest shortcut's why gives way (G33).
        foreach (var i in oldestFirst.Where(i => named[i].ShortcutUnavailable))
        {
            for (var step = 1; step < Lines.Length && Text().Length > tray.ToolTipLength; step++)
            {
                shortened[i] = step;
            }
        }

        // Last, when even the shortcuts' actions do not fit together: each, oldest first, by its shortest form, so the
        // platform cuts none of them away (G33).
        foreach (var i in oldestFirst.Where(i => named[i].ShortcutUnavailable).TakeWhile(_ => Text().Length > tray.ToolTipLength))
        {
            shortened[i] = Lines.Length;
        }

        // And when even those do not fit together, as with four long combinations remapped in Settings: each, oldest
        // first, by its entry's words, which together always fit (G33).
        foreach (var i in oldestFirst.Where(i => named[i].ShortcutUnavailable).TakeWhile(_ => Text().Length > tray.ToolTipLength))
        {
            shortened[i] = Lines.Length + 1;
        }

        return Text();
    }

    /// <summary>An unavailable shortcut named by the words of its entry in the menu, or by its shortest line when it has none.</summary>
    private static string ByEntry(Failure unavailable) =>
        ShortcutEntries.FirstOrDefault(entry => GlobalShortcuts.RegistrationAction(entry.Act) == unavailable.Action) is { Header: { } header }
            ? $"{header} unavailable"
            : Shortest(unavailable);

    /// <summary>
    /// An unavailable shortcut's shortest line, which still says which shortcut: its combination as the settings spell it,
    /// or its action when that is the shorter.
    /// </summary>
    private static string Shortest(Failure unavailable) =>
        new[] { $"{unavailable.Subject} is unavailable", Lines[^1](unavailable) }.MinBy(line => line.Length)!;
}
