using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using ImageStash.Look;
using ImageStash.Seams;
using ImageStash.Store;

namespace ImageStash.Window;

/// <summary>
/// The stash window's <c>+</c> button and its menu of ways in: Import files…, Paste image, Take a screenshot and Record a
/// clip, the ones that work without a drag, so the pointer alone can fill the stash.
/// </summary>
public static class AddMenu
{
    /// <summary>
    /// The button, named "Add to your stash": Import files… runs <paramref name="pickFiles"/>, the window's own picker;
    /// Paste image runs <paramref name="paste"/>; the captures raise <paramref name="screenshotRequested"/> and
    /// <paramref name="recordRequested"/>, the shortcuts' acts, once the menu has closed (G14, G15).
    /// </summary>
    public static Button Button(SettingsKeeper settings, ClipboardPaste paste, Action pickFiles, Action screenshotRequested, Action recordRequested)
    {
        var importFiles = new MenuItem { Header = "Import files…", InputGesture = new KeyGesture(Key.O, KeyModifiers.Control) };
        importFiles.Click += (_, _) => pickFiles();
        // Neither enabled nor worded by what the clipboard holds: only the paste reads it (N9).
        var pasteImage = new MenuItem { Header = "Paste image", InputGesture = new KeyGesture(Key.V, KeyModifiers.Control) };
        pasteImage.Click += (_, _) => _ = paste.Paste();
        var takeScreenshot = new MenuItem { Header = Screenshot.Action };
        // Once the menu has closed, so the screen the capture freezes holds nothing of it (G14).
        takeScreenshot.Click += (_, _) => Dispatcher.UIThread.Post(() => screenshotRequested(), DispatcherPriority.Background);
        var recordClip = new MenuItem { Header = RecordClip.Action };
        recordClip.Click += (_, _) => Dispatcher.UIThread.Post(() => recordRequested(), DispatcherPriority.Background);
        var menu = new MenuFlyout { Items = { importFiles, pasteImage, takeScreenshot, recordClip } };
        ShowCaptureShortcuts(menu, settings);
        // Again at each opening, so a shortcut remapped in Settings meanwhile shows (G34).
        menu.Opening += (opening, _) => ShowCaptureShortcuts((MenuFlyout)opening!, settings);
        var add = new Button { Content = IconGlyph.Of("", 16), Width = 36, Height = 36, Padding = default, Focusable = false, Flyout = menu };
        add.Classes.Add(LookStyles.IconButton);
        ToolTipRule.Set(add, "Add to your stash");
        AutomationProperties.SetName(add, "Add to your stash");
        return add;
    }

    /// <summary>The <c>+</c> menu's captures, each with its shortcut as the settings read it, as the tray menu shows it, registered or not (F5).</summary>
    private static void ShowCaptureShortcuts(MenuFlyout menu, SettingsKeeper settings)
    {
        foreach (var entry in menu.Items.OfType<MenuItem>().Where(entry => entry.Header is Screenshot.Action or RecordClip.Action))
        {
            entry.InputGesture = Shortcut.Parse(entry.Header is Screenshot.Action ? settings.Current.Shortcuts.Screenshot : settings.Current.Shortcuts.Record).Value?.Gesture;
        }
    }
}
