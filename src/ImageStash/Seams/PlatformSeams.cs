namespace ImageStash.Seams;

/// <summary>Every seam the platform entry point implements and hands to <see cref="App"/>; nothing in this assembly constructs one.</summary>
public sealed record PlatformSeams(
    ITray Tray,
    IRecycleBin RecycleBin,
    IStartWithWindows StartWithWindows,
    ILocations Locations,
    IOneInstance OneInstance,
    IQuietWindows QuietWindows,
    IClipboardAndDrops ClipboardAndDrops,
    IDialogsAndExplorer DialogsAndExplorer,
    IShortcuts Shortcuts,
    ITarget Target,
    IScreen Screen,
    IFonts Fonts,
    IBorderDrag BorderDrag);
