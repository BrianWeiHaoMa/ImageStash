using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using ImageStash.Look;

namespace ImageStash.Window;

/// <summary>
/// A shortcut row's small Reset, beside its field, shown only while the row's shortcut is not F5's default
/// (<see cref="ShortcutRecording.ResetOffered"/>): flat, in the look's own words, so it reads in both looks (F10); a
/// control of the row that Tab reaches and that opens nothing (F4). Its automation name says Reset and the row, never
/// the row's title alone, which names only the field (G32). Once a Reset it had the focus for is taken and it hides, the
/// focus goes to the row's field, without beginning a recording, so the keyboard keeps its place.
/// </summary>
public sealed class ShortcutResetButton : Button
{
    public const string Words = "Reset";

    private readonly ShortcutRecording recording;

    public ShortcutResetButton(ShortcutRecording recording, ShortcutField field)
    {
        this.recording = recording;
        Content = Words;
        FontSize = 12.5;
        Padding = new Thickness(8, 4);
        Classes.Add(LookStyles.IconButton);
        AutomationProperties.SetName(this, NameOf(recording));
        ToolTipRule.Set(this, $"Back to {recording.DefaultText}, the default");
        Click += (_, e) =>
        {
            e.Handled = true;
            var hadFocus = IsKeyboardFocusWithin;
            recording.Reset();
            if (hadFocus && !recording.ResetOffered)
            {
                field.Focus(NavigationMethod.Tab);
            }
        };
        Show();
    }

    protected override Type StyleKeyOverride => typeof(Button);

    /// <summary>The automation name of <paramref name="recording"/>'s Reset: the act and its default, never the row's title alone.</summary>
    public static string NameOf(ShortcutRecording recording) => $"Reset {recording.Title} to {recording.DefaultText}";

    /// <summary>Shown while the row's shortcut is not F5's default.</summary>
    public void Show() => IsVisible = recording.ResetOffered;
}
