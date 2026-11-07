using Avalonia.Input;
using ImageStash.Seams;

namespace ImageStash.Window;

/// <summary>
/// One of the Settings page's four press-to-record shortcut fields (G32, F5): idle, it shows its act's shortcut as the
/// settings spell it; recording, ImageStash's own shortcuts are released, so the press reaches the field instead of
/// acting (a registered one would swallow it), and the first key pressed with its modifiers is the candidate, which the
/// shortcuts rebind or refuse; the recording then ends, as it does on <see cref="Cancel"/>, and the shortcuts are
/// registered again. Its row shows one warning: its last refusal since the page opened, else why its shortcut is
/// unavailable (G33), each as the failure's sentence (G35); a rebind clears both.
/// </summary>
public sealed class ShortcutRecording
{
    private readonly GlobalShortcuts shortcuts;
    private readonly Action<ShortcutRecording> beginning;
    private readonly Action changed;
    private IDisposable? suspension;
    private Failure? refusal;

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
    public string Text => shortcuts.TextOf(Act);

    /// <summary>The field's last refusal since the page opened, else why its shortcut is unavailable, else null.</summary>
    public Failure? Warning => refusal ?? shortcuts.UnavailableReason(Act);

    /// <summary>Starts recording, ending any other field's recording first, and releases ImageStash's shortcuts meanwhile.</summary>
    public void Begin()
    {
        if (IsRecording)
        {
            return;
        }

        beginning(this);
        suspension = shortcuts.Suspend();
        changed();
    }

    /// <summary>
    /// A key pressed while recording, with the modifiers held: a modifier alone changes nothing; any other key completes
    /// the candidate, which is rebound or refused, and ends the recording.
    /// </summary>
    public void Press(Key key, KeyModifiers modifiers)
    {
        if (!IsRecording || IsModifier(key))
        {
            return;
        }

        Ended(shortcuts.Rebind(Act, Shortcut.Of(new KeyGesture(key, modifiers))));
    }

    /// <summary>
    /// A key the field saw only coming up (Windows withholds PrtScn's press; another program's hot key takes the press of
    /// the combination it holds), <paramref name="readings"/> the modifiers that may have been held at its press, the
    /// likeliest first, and <paramref name="letGoFirst"/> when one of the likeliest's came up before the key: the reading
    /// another program holds is refused on the spot (G32), else the likeliest is taken as a press, or, let go first,
    /// judged, refused and never recorded; the recording ends either way.
    /// </summary>
    public void PressSeenComingUp(Key key, IReadOnlyList<KeyModifiers> readings, bool letGoFirst)
    {
        if (!IsRecording || IsModifier(key))
        {
            return;
        }

        Ended(shortcuts.JudgeSeenComingUp(Act, readings.Select(modifiers => Shortcut.Of(new KeyGesture(key, modifiers))).ToList(), letGoFirst));
    }

    /// <summary>Ends the recording with nothing changed.</summary>
    public void Cancel()
    {
        if (IsRecording)
        {
            End();
        }
    }

    private void Ended(Outcome outcome)
    {
        refusal = outcome.Succeeded ? null : outcome.Failure;
        End();
    }

    private void End()
    {
        var ending = suspension!;
        suspension = null;
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
