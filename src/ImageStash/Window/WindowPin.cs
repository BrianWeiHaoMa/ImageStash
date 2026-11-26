using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using ImageStash.Look;

namespace ImageStash.Window;

/// <summary>
/// The pin (G2): one toggle, shown at the right end of whichever head is in sight, the grid's, the preview's or the
/// editor's, so a person sees one pin and whatever reads the window's names finds one control named "Keep open" or
/// "Unpin". Its glyph, tip and name follow the window's state here alone. A click on it takes no focus, so a name being
/// typed in the preview or letters being typed into a text layer keep it (G19, G25).
/// </summary>
public sealed class WindowPin
{
    public WindowPin()
    {
        // Spaced from the button before it in whichever head it stands in, as the head's other buttons are.
        Button = new ToggleButton { Width = 36, Height = 36, Padding = default, Margin = new Thickness(6, 0, 0, 0), Focusable = false };
        Button.Classes.Add(LookStyles.IconButton);
        Show(pinned: false);
    }

    /// <summary>The one control; its click is the window's to answer.</summary>
    public ToggleButton Button { get; }

    /// <summary>Shows whether the window is pinned: checked, with the glyph and words of the act a click would make.</summary>
    public void Show(bool pinned)
    {
        var words = pinned ? "Unpin" : "Keep open";
        Button.IsChecked = pinned;
        Button.Content = IconGlyph.Of(pinned ? "" : "", 16);
        ToolTipRule.Set(Button, words);
        AutomationProperties.SetName(Button, words);
    }

    /// <summary>Moves the pin into <paramref name="slot"/>, a head's place for it, out of the head it stood in, so it never stands in two.</summary>
    public void MoveTo(Decorator slot)
    {
        if (ReferenceEquals(Button.Parent, slot))
        {
            return;
        }

        if (Button.Parent is Decorator from)
        {
            from.Child = null;
        }

        slot.Child = Button;
    }
}
