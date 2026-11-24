using Avalonia.Input;
using ImageStash.Seams;
using ImageStash.Store;

namespace ImageStash.Window;

/// <summary>
/// One of the Settings page's four press-to-record shortcut fields (G32, F5): idle, it shows its act's shortcut as the
/// settings spell it; recording, ImageStash's own shortcuts are released, so the press reaches the field instead of
/// acting (a registered one would swallow it), and each key pressed with its modifiers is a candidate, which the
/// shortcuts rebind or refuse. A refusal leaves the field recording, its words under the row at once and the shortcuts
/// still released, so the next press is the retry and a press of another act's combination is refused, not run; the
/// recording ends once a candidate is taken, or on <see cref="Cancel"/> (Esc, its ×, Tab, focus leaving, the page
/// closing), and the shortcuts are registered again. While its shortcut is not F5's default the row offers
/// <see cref="Reset"/>, which sets that default as a press of it would be. Its row shows one warning: the refusal while
/// the field still records, else a Reset's refusal until the shortcuts next change, else why its shortcut is unavailable
/// (G33), each in <see cref="ShortcutWords"/>' guidance (G35).
/// </summary>
public sealed class ShortcutRecording
{
    private readonly GlobalShortcuts shortcuts;
    private readonly Action<ShortcutRecording> beginning;
    private readonly Action changed;
    private IDisposable? suspension;
    private ShortcutProblem? refusal;
    private ShortcutProblem? resetRefusal;

    /// <param name="beginning">Told before a recording begins, so the page ends any other field's first.</param>
    /// <param name="changed">Told when the field's state, text or warning changed.</param>
    public ShortcutRecording(ShortcutAct act, string title, GlobalShortcuts shortcuts, Action<ShortcutRecording> beginning, Action changed)
    {
        Act = act;
        Title = title;
        this.shortcuts = shortcuts;
        this.beginning = beginning;
        this.changed = changed;
    }

    public ShortcutAct Act { get; }

    /// <summary>The row's title, which names the field's control.</summary>
    public string Title { get; }

    public bool IsRecording => suspension is not null;

    /// <summary>The shortcut as the settings spell it: its text in F5's spelling, or the setting as written when it is not one.</summary>
    public string Text => shortcuts.ShortcutOf(Act)?.Text ?? shortcuts.TextOf(Act);

    /// <summary>
    /// The refusal of the recording in progress, else the last Reset's refusal until the shortcuts next change, else why
    /// the field's shortcut is unavailable, else null.
    /// </summary>
    public ShortcutProblem? Warning => refusal ?? resetRefusal ?? shortcuts.UnavailableReason(Act);

    /// <summary>F5's default for the field's act, as the settings spell it (<see cref="Shortcuts"/>, its one home).</summary>
    public string DefaultText => GlobalShortcuts.TextIn(new Shortcuts(), Act);

    /// <summary>
    /// Whether the row offers Reset: while its shortcut is not F5's default, a setting that is no shortcut, or is empty,
    /// included. Read afresh each time, so it follows a remap, an import's settings and a Reset itself.
    /// </summary>
    public bool ResetOffered => shortcuts.ShortcutOf(Act) is not { } current || current != Default;

    private Shortcut Default => Shortcut.Parse(DefaultText).Value!;

    /// <summary>The words under the field's row: <see cref="Warning"/> as guidance, or null.</summary>
    public string? WarningWords => Warning is { } warning ? ShortcutWords.Row(warning) : null;

    /// <summary>Starts recording, ending any other field's recording first, and releases ImageStash's shortcuts meanwhile.</summary>
    public void Begin()
    {
        if (IsRecording)
        {
            return;
        }

        beginning(this);
        resetRefusal = null;
        suspension = shortcuts.Suspend();
        changed();
    }

    /// <summary>
    /// The row's Reset: every recording on the page ended first, this one's included, then F5's default for the act taken
    /// as a recorded press of it is, under every refusal of G32 (another of the four using it, another program holding
    /// it, typing on the layout in use): taken, it is registered, or bound and left released under Pause until the toggle
    /// back, and written; refused, the row says why and the act keeps its shortcut. However it ends, no field records and
    /// every shortcut due is registered again.
    /// </summary>
    public void Reset()
    {
        beginning(this);
        Cancel();
        ShortcutProblem? refused;
        using (shortcuts.Suspend())
        {
            refused = shortcuts.Rebind(Act, Default);
        }

        // Kept only once the shortcuts are registered again, whose changes would otherwise forget it at once.
        resetRefusal = refused;
        changed();
    }

    /// <summary>
    /// The shortcuts changed (a remap, a Reset, an import's settings, Pause, one becoming unavailable): a Reset's refusal
    /// spoke of the shortcuts as they were, and may no longer hold, so the row says it no more (G35).
    /// </summary>
    public void ShortcutsChanged() => resetRefusal = null;

    /// <summary>
    /// A key pressed while recording, with the modifiers held: a modifier alone changes nothing; any other key completes
    /// the candidate, which is rebound, ending the recording, or refused, the field recording on.
    /// </summary>
    public void Press(Key key, KeyModifiers modifiers)
    {
        if (!IsRecording || IsModifier(key))
        {
            return;
        }

        Judged(shortcuts.Rebind(Act, Shortcut.Of(new KeyGesture(key, modifiers))));
    }

    /// <summary>
    /// A key the field saw only coming up (Windows withholds PrtScn's press; another program's hot key takes the press of
    /// the combination it holds), <paramref name="readings"/> the modifiers that may have been held at its press, the
    /// likeliest first, and <paramref name="letGoFirst"/> when one of the likeliest's came up before the key: the reading
    /// another program holds is refused on the spot (G32), else the likeliest is taken as a press, or, let go first,
    /// judged, refused and never recorded; a refusal leaves the field recording, as at a press seen going down.
    /// </summary>
    public void PressSeenComingUp(Key key, IReadOnlyList<KeyModifiers> readings, bool letGoFirst)
    {
        if (!IsRecording || IsModifier(key))
        {
            return;
        }

        Judged(shortcuts.JudgeSeenComingUp(Act, readings.Select(modifiers => Shortcut.Of(new KeyGesture(key, modifiers))).ToList(), letGoFirst));
    }

    /// <summary>Ends the recording with nothing changed, its refusal no longer said: the row says only what is still true.</summary>
    public void Cancel()
    {
        if (IsRecording)
        {
            End();
        }
    }

    /// <summary>A candidate taken ends the recording; one refused is said under the row while the field records on.</summary>
    private void Judged(ShortcutProblem? refused)
    {
        if (refused is null)
        {
            End();
            return;
        }

        refusal = refused;
        changed();
    }

    private void End()
    {
        var ending = suspension!;
        suspension = null;
        refusal = null;
        ending.Dispose();
        changed();
    }

    /// <summary>The modifier <paramref name="key"/> is, or none.</summary>
    public static KeyModifiers ModifierOf(Key key) => key switch
    {
        Key.LeftCtrl or Key.RightCtrl => KeyModifiers.Control,
        Key.LeftAlt or Key.RightAlt => KeyModifiers.Alt,
        Key.LeftShift or Key.RightShift => KeyModifiers.Shift,
        Key.LWin or Key.RWin => KeyModifiers.Meta,
        _ => KeyModifiers.None,
    };

    private static bool IsModifier(Key key) => key == Key.None || ModifierOf(key) != KeyModifiers.None;
}
