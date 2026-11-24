using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Threading;
using ImageStash.Look;
using ImageStash.Seams;
using ImageStash.Store;

namespace ImageStash.Window;

/// <summary>
/// The stash window's <c>+</c> button and its menu of ways in: Import files…, Paste image, Take a screenshot and Record a
/// clip, the ones that work without a drag, so the pointer alone can fill the stash; a capture whose shortcut is
/// unavailable says so beside it and still runs (G33, G34).
/// </summary>
public static class AddMenu
{
    /// <summary>
    /// The button, named "Add to your stash": Import files… runs <paramref name="pickFiles"/>, the window's own picker;
    /// Paste image runs <paramref name="paste"/>; the captures raise <paramref name="screenshotRequested"/> and
    /// <paramref name="recordRequested"/>, the shortcuts' acts, once the menu has closed (G14, G15); a capture's entry is
    /// marked at each opening while <paramref name="unavailable"/> says its shortcut is.
    /// </summary>
    public static Button Button(SettingsKeeper settings, Func<ShortcutAct, bool> unavailable, ClipboardPaste paste, Action pickFiles, Action screenshotRequested, Action recordRequested)
    {
        var importFiles = new MenuItem { Header = "Import files…", InputGesture = StashKeys.ImportFilesKey };
        importFiles.Click += (_, _) => pickFiles();
        // Neither enabled nor worded by what the clipboard holds: only the paste reads it (N9).
        var pasteImage = new MenuItem { Header = "Paste image", InputGesture = StashKeys.PasteKey };
        pasteImage.Click += (_, _) => _ = paste.Paste();
        var takeScreenshot = new MenuItem { Header = Screenshot.Action };
        // Once the menu has closed, so the screen the capture freezes holds nothing of it (G14).
        takeScreenshot.Click += (_, _) => Dispatcher.UIThread.Post(() => screenshotRequested(), DispatcherPriority.Background);
        var recordClip = new MenuItem { Header = RecordClip.Action };
        recordClip.Click += (_, _) => Dispatcher.UIThread.Post(() => recordRequested(), DispatcherPriority.Background);
        var menu = new MenuFlyout { Items = { importFiles, pasteImage, takeScreenshot, recordClip } };
        ShowCaptureShortcuts(takeScreenshot, recordClip, settings, unavailable);
        // Again at each opening, so a shortcut remapped in Settings, or one unavailable or working again, meanwhile shows (G34).
        menu.Opening += (_, _) => ShowCaptureShortcuts(takeScreenshot, recordClip, settings, unavailable);
        var add = new Button { Content = IconGlyph.Of("", 16), Width = 36, Height = 36, Padding = default, Focusable = false, Flyout = menu };
        add.Classes.Add(LookStyles.IconButton);
        ToolTipRule.Set(add, "Add to your stash");
        AutomationProperties.SetName(add, "Add to your stash");
        return add;
    }

    /// <summary>
    /// The <c>+</c> menu's captures, each with its shortcut as the settings read it, as the tray menu shows it, registered
    /// or not (F5), and its words saying so while it is unavailable (<see cref="ShortcutWords.EntryMarked"/>).
    /// </summary>
    private static void ShowCaptureShortcuts(MenuItem takeScreenshot, MenuItem recordClip, SettingsKeeper settings, Func<ShortcutAct, bool> unavailable)
    {
        foreach (var (entry, act, words, text) in new[]
        {
            (takeScreenshot, ShortcutAct.Screenshot, Screenshot.Action, settings.Current.Shortcuts.Screenshot),
            (recordClip, ShortcutAct.Record, RecordClip.Action, settings.Current.Shortcuts.Record),
        })
        {
            entry.Header = unavailable(act) ? ShortcutWords.EntryMarked(words) : words;
            entry.InputGesture = Shortcut.Parse(text).Value?.Gesture;
        }
    }
}
