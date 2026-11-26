using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using ImageStash.Look;

namespace ImageStash.Window;

/// <summary>The paused status and its action, in the stash footer and above the Settings groups.</summary>
internal sealed class PausedShortcutsLine : StackPanel
{
    internal PausedShortcutsLine(Action resume, bool focusable)
    {
        Orientation = Orientation.Horizontal;
        VerticalAlignment = VerticalAlignment.Center;
        Spacing = 6;
        IsVisible = false;
        Children.Add(new TextBlock
        {
            Text = "Shortcuts are paused",
            FontSize = focusable ? 14 : 11.5,
            VerticalAlignment = VerticalAlignment.Center,
        });
        Children.Add(new TextBlock
        {
            Text = "·",
            VerticalAlignment = VerticalAlignment.Center,
        });
        // The stash footer keeps focus in the search box or a preview field. Settings includes Resume in its tab order.
        Resume = new Button
        {
            Content = "Resume",
            FontSize = 12,
            Padding = new Thickness(8, 4),
            VerticalAlignment = VerticalAlignment.Center,
            Focusable = focusable,
        };
        Resume.Classes.Add(LookStyles.IconButton);
        AutomationProperties.SetName(Resume, "Resume");
        Resume.Click += (_, _) => resume();
        Children.Add(Resume);
    }

    internal static PausedShortcutsLine ForSettings(SettingsPage page) =>
        new(page.ResumeShortcuts, focusable: true)
        {
            Margin = new Thickness(0, 8, 0, 4),
            IsVisible = page.ShortcutsPaused,
        };

    internal Button Resume { get; }
}
