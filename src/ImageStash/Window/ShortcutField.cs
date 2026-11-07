using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ImageStash.Look;
using Avalonia.Data;

namespace ImageStash.Window;

/// <summary>
/// A Settings shortcut field (L2), named by its row's title: idle, the shortcut's keys,
/// and a click, Enter or Space begins recording; recording, "Press a new shortcut…" in an accent ring, with a × that
/// ends it. A press reaches its <see cref="ShortcutRecording"/> from the key's KeyDown, or from its KeyUp when its
/// KeyDown never reached the field: PrtScn's, which Windows takes, and a combination's another program holds as its hot
/// key, which that program takes, so the field still refuses it (G32). Which modifiers were held at such a press is
/// known only as the states the field saw them pass through while recording (PrtScn's, those since its last modifier
/// KeyDown): each is a reading, another program's hold of any is refused on the spot, and when one of the modifiers
/// held at the field's last KeyDown came up before the key the press is judged and refused but never recorded. Each is
/// marked handled so no other control acts on it, and so is the KeyUp of every key whose KeyDown a recording took or
/// never reached the field: a Button clicks on Space's KeyUp while focused, which would begin a recording once a Space
/// combination ended one, or once the open shortcut was pressed while the field had the focus. Esc alone and Tab, with
/// or without Shift, are never read as a press (G32): Esc ends the recording alone, the page staying open (G2's one
/// thing at a time), and Tab and Shift+Tab move on as anywhere on the page, so a recording never traps the keyboard and
/// no shortcut is bound to the key that moves focus; focus leaving ends it.
/// </summary>
public sealed class ShortcutField : Button
{
    public const string RecordingWords = "Press a new shortcut…";

    private readonly ShortcutRecording recording;
    private readonly StackPanel keys = new() { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock prompt = new() { Text = RecordingWords, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock edit = IconGlyph.Of("", 12);
    private readonly Button stop;
    // Keys whose KeyDown reached the field and whose KeyUp has not, and of those the ones a recording took.
    private readonly HashSet<Key> down = [];
    private readonly HashSet<Key> taken = [];
    // The modifiers the field knew held since its last KeyDown while recording, or since the recording began: those held at
    // that KeyDown, and those a modifier's KeyUp shows were held, one held down from before the recording among them, whose
    // KeyDown the field never saw. They are those of a press it never saw, unless one of them came up before that press.
    // A release leaves it, so it still names them once they are up.
    private KeyModifiers chord;
    // The modifier states the field saw, in order, since the recording began, and the one its last modifier KeyDown left:
    // a press it never saw came in one of them.
    private readonly List<KeyModifiers> states = [];
    private int pressedAt;
    private IDisposable? ring;

    public ShortcutField(ShortcutRecording recording)
    {
        this.recording = recording;
        AutomationProperties.SetName(this, recording.Title);
        MinWidth = 170;
        MinHeight = 32;
        Padding = new Thickness(8, 2, 6, 2);
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        // The field's own ground and edge, beneath the recording's ring, which takes the edge while it shows.
        Bind(BackgroundProperty, this.GetResourceObservable(LookKeys.ShortcutFieldGround), BindingPriority.Style);
        Bind(BorderBrushProperty, this.GetResourceObservable(LookKeys.ShortcutFieldBorder), BindingPriority.Style);
        prompt.Bind(TextBlock.ForegroundProperty, prompt.GetResourceObservable(LookKeys.FaintWords));
        edit.Opacity = 0.7;
        edit.VerticalAlignment = VerticalAlignment.Center;
        edit.Margin = new Thickness(8, 0, 0, 0);
        // Not focusable: a click on it leaves the focus, and so the recording, in the field until it ends it.
        stop = new Button { Content = IconGlyph.Of("", 10), Focusable = false, Padding = new Thickness(6), BorderThickness = default };
        stop.Classes.Add(LookStyles.IconButton);
        AutomationProperties.SetName(stop, "Stop recording a shortcut");
        ToolTipRule.Set(stop, "Stop recording a shortcut");
        stop.Click += (_, e) =>
        {
            e.Handled = true;
            recording.Cancel();
        };
        var line = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var left = new Panel { Children = { keys, prompt } };
        Grid.SetColumn(edit, 1);
        Grid.SetColumn(stop, 1);
        line.Children.Add(left);
        line.Children.Add(edit);
        line.Children.Add(stop);
        Content = line;
        Click += (_, _) =>
        {
            // A click while recording begins nothing, so it forgets nothing the field saw held.
            if (!recording.IsRecording)
            {
                chord = KeyModifiers.None;
                states.Clear();
                pressedAt = 0;
            }

            recording.Begin();
        };
        LostFocus += (_, _) =>
        {
            down.Clear();
            taken.Clear();
            recording.Cancel();
        };
        Show();
    }

    protected override Type StyleKeyOverride => typeof(Button);

    /// <summary>Draws the field's state as its recording has it now.</summary>
    public void Show()
    {
        var recordingNow = recording.IsRecording;
        keys.IsVisible = edit.IsVisible = !recordingNow;
        prompt.IsVisible = stop.IsVisible = recordingNow;
        keys.Children.Clear();
        foreach (var key in recording.Text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            keys.Children.Add(KeyCap(key));
        }

        if (recordingNow && ring is null)
        {
            BorderThickness = new Thickness(2);
            ring = Bind(BorderBrushProperty, this.GetResourceObservable(LookKeys.AccentRing));
        }
        else if (!recordingNow && ring is not null)
        {
            ring.Dispose();
            ring = null;
            ClearValue(BorderThicknessProperty);
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        down.Add(e.Key);
        if (!recording.IsRecording)
        {
            taken.Remove(e.Key);
            base.OnKeyDown(e);
            return;
        }

        var modifier = ShortcutRecording.ModifierOf(e.Key);
        if (modifier != KeyModifiers.None)
        {
            Saw(e.KeyModifiers & ~modifier);
            Saw(e.KeyModifiers | modifier);
            pressedAt = states.Count - 1;
        }

        chord = e.KeyModifiers | modifier;
        if (!IsThePagesKey(e.Key, e.KeyModifiers))
        {
            recording.Press(e.Key, e.KeyModifiers);
        }
        else if (e.Key == Key.Tab)
        {
            taken.Remove(e.Key);
            base.OnKeyDown(e);
            return;
        }
        else
        {
            recording.Cancel();
        }

        taken.Add(e.Key);
        e.Handled = true;
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        var modifier = ShortcutRecording.ModifierOf(e.Key);
        if (recording.IsRecording && modifier != KeyModifiers.None)
        {
            chord |= modifier | e.KeyModifiers;
            Saw(e.KeyModifiers | modifier);
            Saw(e.KeyModifiers & ~modifier);
        }

        if (!down.Remove(e.Key))
        {
            // Never the Button's: it would click on a Space whose KeyDown another program's hot key took, ImageStash's
            // open shortcut among them, and begin a recording nobody asked for.
            if (recording.IsRecording && modifier == KeyModifiers.None)
            {
                PressSeenComingUp(e.Key, e.KeyModifiers);
            }

            e.Handled = true;
            return;
        }

        if (taken.Remove(e.Key))
        {
            e.Handled = true;
            return;
        }

        base.OnKeyUp(e);
    }

    /// <summary>
    /// A key whose KeyDown never reached the field, let go with <paramref name="stillHeld"/>: the likeliest reading is the
    /// chord when one of its modifiers came up before the key, else what is still held; every state the field saw since
    /// the recording began is a reading too, for PrtScn every state since the field's last modifier KeyDown.
    /// </summary>
    private void PressSeenComingUp(Key key, KeyModifiers stillHeld)
    {
        Saw(stillHeld);
        var letGoFirst = (chord & ~stillHeld) != KeyModifiers.None;
        var likeliest = letGoFirst ? chord : stillHeld;
        // Its KeyDown, which alone moves the focus, went to another program's hot key; Esc alone still ends the recording.
        if (IsThePagesKey(key, likeliest))
        {
            if (key == Key.Escape)
            {
                recording.Cancel();
            }

            return;
        }

        List<KeyModifiers> readings = [likeliest, stillHeld];
        // Windows withholds PrtScn's KeyDown whether or not anyone holds it, and G32 reads PrtScn with the modifiers held
        // from the last modifier's press until it comes up, so the states before the field's last modifier KeyDown are
        // only modifiers on their way, and a screenshot tool holding plain PrtScn no reason to refuse the combination.
        // Any other key's KeyDown was taken by whoever holds the combination pressed, in whichever state it came.
        readings.AddRange(Enumerable.Reverse(states.Skip(key == Key.PrintScreen ? pressedAt : 0)));

        recording.PressSeenComingUp(key, readings.Where(modifiers => !IsThePagesKey(key, modifiers)).Distinct().ToList(), letGoFirst);
    }

    /// <summary>Esc alone, and Tab with or without Shift: the page's keys, which G32 never reads as a press.</summary>
    private static bool IsThePagesKey(Key key, KeyModifiers modifiers) =>
        (key == Key.Escape && modifiers == KeyModifiers.None) || (key == Key.Tab && modifiers is KeyModifiers.None or KeyModifiers.Shift);

    private void Saw(KeyModifiers state)
    {
        if (states.Count == 0 || states[^1] != state)
        {
            states.Add(state);
        }
    }

    /// <summary>One key of the shortcut, drawn as a key cap.</summary>
    private static Border KeyCap(string name)
    {
        var cap = new Border
        {
            Child = new TextBlock { Text = name, FontSize = 12, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center },
            MinWidth = 30,
            Height = 24,
            Padding = new Thickness(8, 0),
            BorderThickness = new Thickness(1),
        };
        ((TextBlock)cap.Child).HorizontalAlignment = HorizontalAlignment.Center;
        cap.Bind(Border.BackgroundProperty, cap.GetResourceObservable(LookKeys.KeyCapGround));
        cap.Bind(Border.BorderBrushProperty, cap.GetResourceObservable(LookKeys.KeyCapBorder));
        cap.Bind(Border.CornerRadiusProperty, cap.GetResourceObservable(LookKeys.ControlRadius));
        cap.Child.Bind(TextBlock.ForegroundProperty, cap.GetResourceObservable(LookKeys.KeyCapWords));
        return cap;
    }
}
