using ImageStash.Seams;
using ImageStash.Window;

namespace ImageStash.Tests.Support;

/// <summary>
/// The stash window's parts (<see cref="ListedStash"/>) with the tray menu on a <see cref="RecordingTray"/>, the
/// notifications that a shortcut became unavailable, raised on the parts' notifications above the tray, and the
/// Settings page, all on one policy over one recording Shortcuts seam, composed as the app composes them: the tray menu
/// and the notifications told what becomes unavailable and following before the shortcuts start, some combinations
/// already another program's then; over a real store that begins empty. The walk through shortcuts and Settings runs on it.
/// </summary>
public sealed class StashWithTray : IDisposable
{
    private StashWithTray(ListedStash listed, RecordingTray tray)
    {
        Listed = listed;
        Tray = tray;
    }

    public ListedStash Listed { get; }

    public RecordingTray Tray { get; }

    /// <summary>
    /// Each ask for Settings, by the tray menu's Settings or a notification's Choose another, with the row it asked the
    /// page to open on, as the app's one ask takes it: the tray's, the first unavailable shortcut's (G33).
    /// </summary>
    public List<ShortcutAct?> SettingsAskedOn { get; } = [];

    /// <param name="held">Combinations another program holds from before the shortcuts start.</param>
    public static async Task<StashWithTray> Open(params Shortcut[] held)
    {
        var tray = new RecordingTray(toolTipLength: 127);
        StashWithTray? walked = null;
        var listed = await ListedStash.OpenWithTray(parts => new TrayMenu(tray,
            () => _ = parts.Summon(),
            () => _ = parts.Screenshot.Take(),
            () => _ = parts.RecordClip.StartOrStop(),
            () => _ = parts.StashClipboardImage.Stash(),
            () => walked!.SettingsAskedOn.Add(walked.Listed.Shortcuts.FirstUnavailable),
            () => { }),
            parts => new ShortcutNotifications(parts.Notifications, act => walked!.SettingsAskedOn.Add(act)), held);
        walked = new StashWithTray(listed, tray);
        return walked;
    }

    /// <summary>The Settings page over the same shortcuts and settings.</summary>
    public SettingsPage Page() => Listed.Page();

    public void Dispose() => Listed.Dispose();
}
