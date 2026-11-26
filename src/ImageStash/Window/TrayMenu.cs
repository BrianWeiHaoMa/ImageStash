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
/// to the action alone. The shortcuts keep each why that fits, the newer's before the older's (G33). While paused the
/// tooltip reserves room for the paused words, and can shorten reasons or group unavailable shortcuts under one label.
/// Only when the other failures' actions do not fit beside the shortcuts' shortest forms do lines leave, the oldest
/// other failures first, so the platform never cuts away which shortcut is unavailable.
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

    private enum ShortcutTipForm { Whole, CompactWhy, ActAndCombination, Shortest }

    private static readonly ShortcutTipForm[] RunningForms = [ShortcutTipForm.Whole, ShortcutTipForm.ActAndCombination, ShortcutTipForm.Shortest];
    private static readonly ShortcutTipForm[] PausedForms = [ShortcutTipForm.Whole, ShortcutTipForm.CompactWhy, ShortcutTipForm.ActAndCombination, ShortcutTipForm.Shortest];

    private string ToolTip()
    {
        var paused = shortcuts?.Paused == true;
        if (named.Count == 0)
        {
            if (paused)
            {
                return PausedToolTip;
            }

            return OpenShortcutSummoning() is { } open ? $"ImageStash · {open.Text}" : "ImageStash";
        }

        var budget = tray.ToolTipLength - (paused ? PausedToolTip.Length + 1 : 0);
        var failures = NamedToolTip(budget, paused);
        if (!paused)
        {
            return failures;
        }

        return failures.Length == 0 ? PausedToolTip : $"{PausedToolTip}\n{failures}";
    }

    private string NamedToolTip(int budget, bool paused)
    {
        // The lines shown, newest first, by their index in named; how far each is shortened, an index into its forms.
        var shortcutForms = paused ? PausedForms : RunningForms;
        var kept = Enumerable.Range(0, named.Count).ToList();
        var shortened = new int[named.Count];

        // While running, the shortcut lines share a label only where otherwise another failure would leave or nothing fits.
        string Text(bool shortest = false, bool groupShortcuts = false)
        {
            var lines = kept.Select(i => NamedLine(i, shortest ? null : shortened[i], shortcutForms)).ToArray();
            var text = string.Join('\n', lines.Select(line => line.Text));
            if (text.Length > budget)
            {
                lines = GroupShortestFailureLines(lines);
                text = string.Join('\n', lines.Select(line => line.Text));
            }

            return (paused || groupShortcuts) && text.Length > budget ? GroupShortestShortcutLines(lines, text) : text;
        }

        // Past every shortening the platform would cut the text's end, an unavailable shortcut's line with it: the
        // oldest other failures leave instead, so the tooltip still says which shortcut is unavailable (G33).
        for (var i = named.Count - 1; i >= 0 && Text(shortest: true, groupShortcuts: true).Length > budget; i--)
        {
            if (named[i].Unavailable is null)
            {
                kept.Remove(i);
            }
        }

        var oldestFirst = Enumerable.Reverse(kept).ToList();
        for (var step = 1; step < Failure.ToolTipFormCount; step++)
        {
            foreach (var i in oldestFirst.Where(i => named[i].Unavailable is null).TakeWhile(_ => Text().Length > budget))
            {
                shortened[i] = step;
            }
        }

        // Then the shortcut lines: of the ways that fit, keep the newest why that fits, and
        // every older one that fits beside it, since a why gives way only when the tooltip cannot hold it (G33), then
        // likewise acts beside their combinations; four shortest forms always fit together. The newest four are weighed,
        // which keeps the ways few; an older line, named again by a later registration, keeps its shortest form.
        var shortcutLines = kept.Where(i => named[i].Unavailable is not null).ToList();
        foreach (var older in shortcutLines.Skip(ShortcutEntries.Length))
        {
            shortened[older] = shortcutForms.Length - 1;
        }

        shortcutLines = shortcutLines.Take(ShortcutEntries.Length).ToList();
        int[]? best = null;
        int[]? bestScore = null;
        var ways = (int)Math.Pow(shortcutForms.Length, shortcutLines.Count);
        for (var way = 0; way < ways; way++)
        {
            var forms = new int[shortcutLines.Count];
            for (int j = 0, rest = way; j < forms.Length; j++, rest /= shortcutForms.Length)
            {
                forms[j] = rest % shortcutForms.Length;
                shortened[shortcutLines[j]] = forms[j];
            }

            var score = ShorteningScore(forms.Select(form => shortcutForms[form]));
            if (Text().Length <= budget && (bestScore is null || score.AsSpan().SequenceCompareTo(bestScore) > 0))
            {
                (best, bestScore) = (forms, score);
            }
        }

        for (var j = 0; j < shortcutLines.Count; j++)
        {
            shortened[shortcutLines[j]] = best?[j] ?? shortcutForms.Length - 1;
        }

        return Text(groupShortcuts: best is null);
    }

    private (string Text, ShortcutProblem? Shortest, Failure? ShortestFailure) NamedLine(int index, int? formIndex, ShortcutTipForm[] shortcutForms)
    {
        var (failure, problem) = named[index];
        if (problem is null)
        {
            var failureForm = formIndex ?? (Failure.ToolTipFormCount - 1);
            return (failure!.ToolTipForms[failureForm], null, failureForm == Failure.ToolTipFormCount - 1 ? failure : null);
        }

        var form = shortcutForms[formIndex ?? (shortcutForms.Length - 1)];
        if (form != ShortcutTipForm.Shortest)
        {
            var text = form switch
            {
                ShortcutTipForm.Whole => ShortcutWords.ToolTipLine(problem),
                ShortcutTipForm.CompactWhy => ShortcutWords.ToolTipLineWithCompactWhy(problem),
                _ => ShortcutWords.ToolTipLineWithoutWhy(problem),
            };
            return (text, null, null);
        }

        var useCompactForms = shortcutForms.Contains(ShortcutTipForm.CompactWhy);
        var shortest = useCompactForms
            ? ShortcutWords.ToolTipLineShortest(problem, useCompactForms: true)
            : ShortcutWords.ToolTipLineShortest(problem);
        if (formIndex is null && useCompactForms)
        {
            // A short reason, such as "Alt+Q types @.", can cost less than the line that only names the shortcut.
            var withWhy = ShortcutWords.ToolTipLineWithCompactWhy(problem);
            shortest = withWhy.Length < shortest.Length ? withWhy : shortest;
        }

        return (shortest, problem, null);
    }

    /// <summary>
    /// Failures cut to their act, when even those lines do not fit: those of one kind share one line where the newest of
    /// them stood (<see cref="Failure.ToolTipActs"/>), so each act is still named rather than the oldest leaving (G33, G35).
    /// </summary>
    private static (string Text, ShortcutProblem? Shortest, Failure? ShortestFailure)[] GroupShortestFailureLines(
        (string Text, ShortcutProblem? Shortest, Failure? ShortestFailure)[] lines)
    {
        var grouped = lines.ToList();
        foreach (var kind in lines.Where(line => line.ShortestFailure is not null).GroupBy(line => line.ShortestFailure!.Refused))
        {
            var members = kind.ToList();
            if (members.Count < 2)
            {
                continue;
            }

            var at = grouped.IndexOf(members[0]);
            grouped[at] = (Failure.ToolTipActs(members.Select(line => line.ShortestFailure!).ToList()), null, null);
            foreach (var member in members.Skip(1))
            {
                grouped.Remove(member);
            }
        }

        return [.. grouped];
    }

    private static string GroupShortestShortcutLines((string Text, ShortcutProblem? Shortest, Failure? ShortestFailure)[] lines, string text)
    {
        // A shared label frees room for all four shortcuts beside Pause, with room for other failures when their names are short.
        var shortest = lines.Where(line => line.Shortest is not null).ToArray();
        if (shortest.Length < 2)
        {
            return text;
        }

        var grouped = ShortcutWords.ToolTipUnavailableShortcuts(shortest.Select(line => line.Shortest!));
        if (grouped.Length >= shortest.Sum(line => line.Text.Length + 1) - 1)
        {
            return text;
        }

        return string.Join('\n', lines.Where(line => line.Shortest is null).Select(line => line.Text).Append(grouped));
    }

    /// <summary>
    /// How much of the shortcut lines a way of shortening them keeps, compared element by element: which keep their why,
    /// the newest first, then which keep their act and combination, the newest first.
    /// </summary>
    private static int[] ShorteningScore(IEnumerable<ShortcutTipForm> formsNewestFirst) =>
    [
        .. formsNewestFirst.Select(form => form is ShortcutTipForm.Whole or ShortcutTipForm.CompactWhy ? 1 : 0),
        .. formsNewestFirst.Select(form => form is ShortcutTipForm.Whole or ShortcutTipForm.ActAndCombination ? 1 : 0),
    ];

    /// <summary>The open shortcut while a press of it would summon the window (G33); null otherwise, so the tooltip never offers a key that does nothing.</summary>
    private Shortcut? OpenShortcutSummoning() => shortcuts is null ? null : ActingShortcut.Of(shortcuts, ShortcutAct.Open);
}
