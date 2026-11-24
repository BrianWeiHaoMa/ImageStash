using ImageStash.Capture;
using ImageStash.Seams;

namespace ImageStash.Window;

/// <summary>
/// The tray menu's actions and the icon's left click (G34), and the tooltip, which names every failure since start
/// (G35), the one place a failure at start can be named before the window is shown (F4), a shortcut that cannot be
/// registered among them (G33), until Settings registers it. While it names none, it reads ImageStash with the open
/// shortcut beside it, so pointing at the icon teaches the gesture, but only while that shortcut would summon: never
/// under Pause, nor while its setting is no shortcut or it is unavailable. An unavailable shortcut's line is one
/// sentence of <see cref="ShortcutWords"/>: its combination, why, and the act left with no shortcut. When the platform's
/// tooltip cannot hold every line whole, the other failures give way first, oldest first, to action and subject and then
/// to the action alone; then the shortcuts' lines, to their act and combination or to the shortest form still saying
/// which shortcut, keeping each why that fits, the newer's before the older's, since the tooltip is where the tray icon
/// says which shortcut is unavailable and why (G33). Only when the other failures' actions do not fit beside the
/// shortcuts' shortest forms do lines leave, the oldest other failures first, so the platform never cuts away which
/// shortcut is unavailable; the four shortcuts' shortest forms always fit together.
/// </summary>
public sealed class TrayMenu
{
    private const string PausedToolTip = "ImageStash · shortcuts paused";
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
    // Newest first: each a failure, or an unavailable shortcut, at most one line for each act.
    private readonly List<(Failure? Failure, ShortcutProblem? Unavailable)> named = [];
    // What each shortcut entry reads now, by the header it was shown with, where that differs from the header.
    private readonly Dictionary<string, string> headers = [];
    private RecordingStage recording;
    private GlobalShortcuts? shortcuts;

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
    /// shortcut as the settings read it, whether or not it could be registered, an unavailable one's entry saying so
    /// beside it and still running its act (<see cref="ShortcutWords.EntryMarked"/>), Pause shortcuts as a toggle,
    /// Settings, which has no shortcut of its own, and Quit ImageStash, in G34's order (G2, G33, G34). From then on the
    /// entries follow the shortcuts: Pause's check mark, each act's shortcut once Settings changed it, each entry's mark
    /// while its shortcut is unavailable and no longer, and the tooltip, which names an unavailable shortcut only while
    /// that is still why it is unavailable (G33), and the open shortcut as it now reads, or not at all, the moment Pause,
    /// a remap or a registration changes it. A shortcut unavailable from the start is told before any change is raised,
    /// and marked then.
    /// </summary>
    public void Show(GlobalShortcuts shortcuts)
    {
        this.shortcuts = shortcuts;
        ShowIcon();
        tray.Show(
            [
                .. ShortcutEntries.Select(entry => new TrayMenuEntry(entry.Header, ChosenOf(entry.Act), shortcuts.ShortcutOf(entry.Act)?.Gesture, MenuClosed: entry.Act == ShortcutAct.Open ? OpenedMenuClosed : null)),
                new TrayMenuEntry(PauseHeader, shortcuts.TogglePause, Checked: shortcuts.Paused),
                new TrayMenuEntry(SettingsHeader, openSettings, MenuClosed: SettingsMenuClosed),
                new TrayMenuEntry(QuitHeader, quit),
            ],
            ToolTip(),
            open);
        ShowHeaders();
        shortcuts.Changed += () => Follow(shortcuts);
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
        named.RemoveAll(line => line.Unavailable is { } unavailable && !reasons.Contains(unavailable));
        ShowHeaders();
        ShowIcon();
        tray.SetToolTip(ToolTip());
    }

    /// <summary>
    /// Each shortcut entry's words as they should read now, asked of the tray only where they changed: Record a clip as
    /// the recording reads it (G15, G34), and an entry whose shortcut is unavailable marked so beside its shortcut, by the
    /// policy's one judgment of unavailable, never by Pause or a field's recording (G33).
    /// </summary>
    private void ShowHeaders()
    {
        foreach (var entry in ShortcutEntries)
        {
            var words = entry.Act != ShortcutAct.Record ? entry.Header : recording switch
            {
                RecordingStage.Waiting => StartRecordingHeader,
                RecordingStage.Sampling => StopRecordingHeader,
                _ => RecordClipHeader,
            };
            if (shortcuts?.UnavailableReason(entry.Act) is not null)
            {
                words = ShortcutWords.EntryMarked(words);
            }

            if (words != headers.GetValueOrDefault(entry.Header, entry.Header))
            {
                headers[entry.Header] = words;
                tray.SetHeader(entry.Header, words);
            }
        }
    }

    /// <summary>
    /// The icon's red dot and Stop recording while sampling, Start recording while the region waits (G15, G34).
    /// </summary>
    public void SetRecording(RecordingStage stage)
    {
        recording = stage;
        ShowHeaders();
        ShowIcon();
    }

    private void ShowIcon() => tray.SetIcon(recording switch
    {
        RecordingStage.Sampling => TrayIconState.Recording,
        _ when shortcuts?.Paused == true => TrayIconState.Paused,
        _ when shortcuts?.FirstUnavailable is not null => TrayIconState.Unavailable,
        _ => TrayIconState.Plain,
    });

    /// <summary>Adds a failure to the tooltip, newest first, until the next start.</summary>
    public void Name(Failure failure) => Add((failure, null));

    /// <summary>Names <paramref name="outcome"/>'s failure as <see cref="Name"/> does, when it failed.</summary>
    public void NameIfFailed(Outcome outcome)
    {
        if (!outcome.Succeeded)
        {
            Name(outcome.Failure);
        }
    }

    /// <summary>
    /// Adds a shortcut that is unavailable, and why, whose reason the tooltip keeps longest, until the shortcuts no longer
    /// give that reason for it (G33); its entry is marked. The tooltip is no history: the act's earlier line, a
    /// registration that judged it again or an earlier reason, gives way to this one, so each shortcut unavailable now has
    /// exactly one line.
    /// </summary>
    public void NameUnavailableShortcut(ShortcutProblem unavailable)
    {
        named.RemoveAll(line => line.Unavailable?.Act == unavailable.Act);
        Add((null, unavailable));
        // At startup a refused registration is named without raising Changed.
        ShowIcon();
    }

    private void Add((Failure? Failure, ShortcutProblem? Unavailable) line)
    {
        named.Insert(0, line);
        if (shortcuts is not null)
        {
            ShowHeaders();
            tray.SetToolTip(ToolTip());
        }
    }

    // A failure's line, then its shorter forms, each saying what failed.
    private static readonly Func<Failure, string>[] Lines =
    [
        f => f.Sentence,
        f => $"{f.Action} failed: {f.Subject}",
        f => $"{f.Action} failed",
    ];

    // An unavailable shortcut's line, then its shorter forms, each saying which shortcut (G33).
    private static readonly Func<ShortcutProblem, string>[] ShortcutLines =
    [
        ShortcutWords.ToolTipLine,
        ShortcutWords.ToolTipLineWithoutWhy,
        ShortcutWords.ToolTipLineShortest,
    ];

    private string ToolTip()
    {
        if (shortcuts?.Paused == true)
        {
            if (named.Count == 0)
            {
                return PausedToolTip;
            }

            // Give up the paused line before any failure's words need shortening (G33).
            var whole = string.Join("\n", named.Select(line => line.Unavailable is { } problem
                ? ShortcutWords.ToolTipLine(problem)
                : line.Failure!.Sentence));
            if (PausedToolTip.Length + 1 + whole.Length <= tray.ToolTipLength)
            {
                return $"{PausedToolTip}\n{whole}";
            }
        }

        if (named.Count == 0)
        {
            return OpenShortcutSummoning() is { } open ? $"ImageStash · {open.Text}" : "ImageStash";
        }

        return NamedToolTip();
    }

    private string NamedToolTip()
    {
        // The lines shown, newest first, by their index in named; how far each is shortened, an index into its forms.
        var kept = Enumerable.Range(0, named.Count).ToList();
        var shortened = new int[named.Count];
        string Line(int i) => named[i].Unavailable is { } unavailable ? ShortcutLines[shortened[i]](unavailable) : Lines[shortened[i]](named[i].Failure!);
        string Shortest(int i) => named[i].Unavailable is { } unavailable ? ShortcutLines[^1](unavailable) : Lines[^1](named[i].Failure!);
        string Text() => string.Join("\n", kept.Select(Line));
        int AllShortest() => kept.Sum(i => Shortest(i).Length) + kept.Count - 1;

        // Past every shortening the platform would cut the text's end, an unavailable shortcut's line with it: the
        // oldest other failures leave instead, so the tooltip still says which shortcut is unavailable (G33).
        for (var i = named.Count - 1; i >= 0 && AllShortest() > tray.ToolTipLength; i--)
        {
            if (named[i].Unavailable is null)
            {
                kept.Remove(i);
            }
        }

        var oldestFirst = Enumerable.Reverse(kept).ToList();
        for (var step = 1; step < Lines.Length; step++)
        {
            foreach (var i in oldestFirst.Where(i => named[i].Unavailable is null).TakeWhile(_ => Text().Length > tray.ToolTipLength))
            {
                shortened[i] = step;
            }
        }

        // Then the shortcut lines, three forms each: of the ways that fit, the one keeping the newest why that fits, and
        // every older one that fits beside it, since a why gives way only when the tooltip cannot hold it (G33), then
        // likewise acts beside their combinations; four shortest forms always fit together. The newest four are weighed,
        // which keeps the ways few; an older line, named again by a later registration, keeps its shortest form.
        var shortcutLines = kept.Where(i => named[i].Unavailable is not null).ToList();
        foreach (var older in shortcutLines.Skip(ShortcutEntries.Length))
        {
            shortened[older] = ShortcutLines.Length - 1;
        }

        shortcutLines = shortcutLines.Take(ShortcutEntries.Length).ToList();
        int[]? best = null;
        int[]? bestScore = null;
        var ways = (int)Math.Pow(ShortcutLines.Length, shortcutLines.Count);
        for (var way = 0; way < ways; way++)
        {
            var forms = new int[shortcutLines.Count];
            for (int j = 0, rest = way; j < forms.Length; j++, rest /= ShortcutLines.Length)
            {
                forms[j] = rest % ShortcutLines.Length;
                shortened[shortcutLines[j]] = forms[j];
            }

            var score = ShorteningScore(forms);
            if (Text().Length <= tray.ToolTipLength && (bestScore is null || score.AsSpan().SequenceCompareTo(bestScore) > 0))
            {
                (best, bestScore) = (forms, score);
            }
        }

        for (var j = 0; j < shortcutLines.Count; j++)
        {
            shortened[shortcutLines[j]] = best?[j] ?? ShortcutLines.Length - 1;
        }

        return Text();
    }

    /// <summary>
    /// How much of the shortcut lines a way of shortening them keeps, compared element by element: which keep their why,
    /// the newest first, then which keep their act and combination, the newest first.
    /// </summary>
    private static int[] ShorteningScore(int[] formsNewestFirst) =>
    [
        .. formsNewestFirst.Select(form => form == 0 ? 1 : 0),
        .. formsNewestFirst.Select(form => form <= 1 ? 1 : 0),
    ];

    /// <summary>The open shortcut while a press of it would summon the window (G33); null otherwise, so the tooltip never offers a key that does nothing.</summary>
    private Shortcut? OpenShortcutSummoning() => shortcuts is null ? null : ActingShortcut.Of(shortcuts, ShortcutAct.Open);
}
