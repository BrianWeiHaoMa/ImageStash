using ImageStash.Store;
using ImageStash.Window;

namespace ImageStash.Tests.Support;

/// <summary>
/// The start's last steps (<see cref="StartOpening"/>) run as the app runs them once its window exists, given a start's
/// arguments, the settings it read and the stash where they put it, with the window's two summons recorded: each show
/// asked, in order, with the stash's state as the listing held it at that moment.
/// </summary>
public sealed class StartShows
{
    public const string Tray = "tray";
    public const string Pinned = "pinned";

    private StartShows(Listing listing) => Listing = listing;

    /// <summary>Each show the start asked: <see cref="Tray"/>, the tray's summon, or <see cref="Pinned"/>, the first run's.</summary>
    public List<(string Show, StashState State)> Asked { get; } = [];

    /// <summary>The window's listing, which the start hands the stash or the reason it did not open.</summary>
    public Listing Listing { get; }

    /// <summary>The shows asked, without the states.</summary>
    public string[] Shows => Asked.Select(asked => asked.Show).ToArray();

    /// <summary>
    /// A start with <paramref name="args"/>, after the settings read as <paramref name="settings"/> and kept by
    /// <paramref name="keeper"/>, whose default stash is <paramref name="stash"/>'s folder; the stash opens through
    /// <paramref name="openStore"/>, else as <see cref="TestStash.OpenAt"/> opens it.
    /// </summary>
    public static async Task<StartShows> Run(IReadOnlyList<string> args, SettingsLoad settings, SettingsKeeper keeper, TestStash stash, Func<string, Task<StoreOpening>>? openStore = null)
    {
        var trayMenu = new TrayMenu(new RecordingTray(toolTipLength: 1000), () => { }, () => { }, () => { }, () => { }, () => { }, () => { });
        trayMenu.Show(new GlobalShortcuts(StartedShortcuts.SettingsInMemory(), GlobalShortcuts.Bindings(() => { }, () => { }, () => { }, () => { }), new RecordingShortcuts(), trayMenu.NameUnavailableShortcut, action => action()));
        var shows = new StartShows(new Listing(_ => { }));
        var start = new StartOpening(trayMenu, shows.Listing, new TestLocations(stash.Folder.Root, stash.Outside("settings")), stash.RecycleBin, stash.Retry,
            openStore ?? stash.OpenAt, _ => Outcome.Done,
            summonFromTray: () => shows.Asked.Add((Tray, shows.Listing.State)),
            summonPinned: () => shows.Asked.Add((Pinned, shows.Listing.State)));
        await start.Run(args, settings, keeper, _ => { });
        return shows;
    }
}
