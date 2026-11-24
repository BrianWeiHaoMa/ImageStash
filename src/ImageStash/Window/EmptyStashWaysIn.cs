using ImageStash.Seams;
using ImageStash.Store;

namespace ImageStash.Window;

/// <summary>
/// One way into the empty stash: the Segoe Fluent Icons glyph leading its row (F10), its words, its shortcut as
/// <see cref="Shortcut.Text"/> writes it, or null while the setting is no shortcut, the act a click on its row runs, and
/// whether that shortcut is unavailable now, which the row says beside it (G33).
/// </summary>
public sealed record WayIn(string Glyph, string Words, string? Shortcut, Action Act, bool ShortcutUnavailable = false);

/// <summary>
/// The acts the window hands its empty stash's rows, each the one act its key, its <c>+</c> menu's entry or the tray
/// menu's entry already runs, so that act's own rules hold from the row too (G12 to G15, G35, G36).
/// </summary>
public sealed record WaysInActs(Action Paste, Action Screenshot, Action Record, Action StashClipboardImage, Action ImportFiles);

/// <summary>
/// What the empty stash says (G2): the drop line, then every way in, in order, each with its shortcut as the settings
/// read it, registered or not, as the tray menu and the <c>+</c> menu show it, one unavailable said so beside it (G33);
/// then the line saying how ImageStash opens again and that closing the window keeps it running (G3), which offers the
/// open shortcut only while it is available. No line is enabled or worded by what the clipboard holds (N9).
/// </summary>
public static class EmptyStashWaysIn
{
    public const string Heading = "Your stash is empty";

    public const string DropLine = "Drop images and GIFs here, from a browser, a chat or File Explorer";

    /// <summary>A way in whose setting is no shortcut: the tray menu still runs it (G33).</summary>
    public const string FromTrayMenu = "from the tray menu";

    /// <summary>
    /// The ways in over <paramref name="shortcuts"/>, each running its act of <paramref name="acts"/>: the window's own
    /// <c>Ctrl+V</c> and <c>Ctrl+O</c>, the three global ones from the settings, each marked while
    /// <paramref name="unavailable"/>, the policy's judgment, says its shortcut is.
    /// </summary>
    public static IReadOnlyList<WayIn> Of(Shortcuts shortcuts, WaysInActs acts, Func<ShortcutAct, bool> unavailable) =>
    [
        // A clipboard, a dashed clip, a record button, a picture, an arrow coming in.
        new("\uE77F", "Paste a copied image", Shortcut.Of(StashKeys.PasteKey).Text, acts.Paste),
        new("\uE924", "Take a screenshot", TextOf(shortcuts.Screenshot), acts.Screenshot, unavailable(ShortcutAct.Screenshot)),
        new("\uE7C8", "Record a clip", TextOf(shortcuts.Record), acts.Record, unavailable(ShortcutAct.Record)),
        new("\uE91B", "Stash the clipboard image", TextOf(shortcuts.StashClipboardImage), acts.StashClipboardImage, unavailable(ShortcutAct.StashClipboardImage)),
        new("\uE8B5", "Import files", Shortcut.Of(StashKeys.ImportFilesKey).Text, acts.ImportFiles),
    ];

    /// <summary>
    /// The line under the ways in: the open shortcut as the settings read it, when it is one, and the tray icon open
    /// ImageStash from anywhere, and closing the window keeps it running (G1, G3, G34); while
    /// <paramref name="openUnavailable"/>, the shortcut is named as unavailable and only the icon is offered (G33).
    /// </summary>
    public static string OpenLine(Shortcuts shortcuts, bool openUnavailable) =>
        (TextOf(shortcuts.Open) is { } open
            ? openUnavailable ? ShortcutWords.OpenLineUnavailable(open) : $"Open ImageStash from any app with {open}, or from its icon in the tray."
            : "Open ImageStash from its icon in the tray.")
        + " Closing this window keeps it running.";

    private static string? TextOf(string setting) => Shortcut.Parse(setting).Value?.Text;
}
