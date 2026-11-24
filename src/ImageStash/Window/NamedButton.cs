using Avalonia.Automation;
using Avalonia.Controls;

namespace ImageStash.Window;

/// <summary>A Settings button whose words are also its automation name, so UI Automation and the checks find it by what it says.</summary>
internal static class NamedButton
{
    public static Button Of(string words, Action clicked)
    {
        var button = new Button { Content = words };
        AutomationProperties.SetName(button, words);
        button.Click += (_, _) => clicked();
        return button;
    }
}
