using ImageStash.Seams;
using ImageStash.Store;
using ImageStash.Window;

namespace ImageStash;

/// <summary>
/// The start's last steps, once the stash window exists, hidden (G1): the window shown as the kind of start asks (G2, G3),
/// and the stash opened where Settings says (G28, State: Every change).
/// </summary>
/// <param name="openStore">A stash folder opened as the start opens it: its pending steps resolved and its Open with… copies classified.</param>
/// <param name="applyTravelling">The travelling settings put in effect in the running app, as after a live import (G36).</param>
/// <param name="summonFromTray">The tray's summon, which a left-click on the tray icon and a second launch run too (G3, G34).</param>
/// <param name="summonPinned">The window opened pinned, as on G2's first run.</param>
internal sealed class StartOpening(
    TrayMenu trayMenu,
    Listing listing,
    ILocations locations,
    IRecycleBin recycleBin,
    TimeSpan heldFileRetry,
    Func<string, Task<StoreOpening>> openStore,
    Func<Settings, Outcome> applyTravelling,
    Action summonFromTray,
    Action summonPinned)
{
    /// <summary>
    /// A start given <paramref name="args"/>, after the settings it read and the keeper it made of them: a later launch by
    /// hand shows the window at once, as a tray click would, which then shows the opening, the stash, or why it did not open
    /// (G3, G35); a first run shows nothing until it knows whether its stash is empty, then opens pinned over an empty one
    /// whatever the start, and otherwise, launched by hand, shows the window as a tray click would (G2, G3). The start at
    /// sign-in opens no stash window but G2's. <paramref name="opened"/> keeps the store the moment it opens.
    /// </summary>
    public async Task Run(IReadOnlyList<string> args, SettingsLoad settings, SettingsKeeper keeper, Action<StashStore> opened)
    {
        var byHand = !Launch.IsAtSignIn(args);
        if (byHand && !settings.FirstRun)
        {
            summonFromTray();
        }

        var store = await OpenStash(settings, keeper, opened);
        if (store is not null && await OpensPinnedAtStart(settings, store))
        {
            summonPinned();
        }
        else if (byHand && settings.FirstRun)
        {
            summonFromTray();
        }
    }

    /// <summary>
    /// Opens the stash where Settings says, after the tray, so ImageStash shows it is alive while a held file is
    /// retried, and on the store's own thread, so the tray answers meanwhile; first finishing what a Move killed at any
    /// moment left (a partial copy deleted, an old folder's removal finished) and what an import's record left (the import
    /// finished, the stash staying closed while it cannot be), so the folder Settings names is the one whole stash (G28,
    /// State: Move, Import stash…). Nothing can reach the stash until it has opened and resolved its pending steps
    /// (G28); what could not be finished, opened or resolved is named in the tooltip (G35). The store, or null when it did not open.
    /// </summary>
    private async Task<StashStore?> OpenStash(SettingsLoad settings, SettingsKeeper keeper, Action<StashStore> opened)
    {
        var folder = StashLocation(settings, locations);
        if (!folder.Succeeded)
        {
            trayMenu.Name(folder.Failure);
            listing.StashFailed(folder.Failure);
            return null;
        }

        return await OpenAtStart(folder.Value!, settings, keeper, opened);
    }

    private async Task<StashStore?> OpenAtStart(string folder, SettingsLoad settings, SettingsKeeper keeper, Action<StashStore> opened)
    {
        foreach (var failure in await StashMove.FinishAtStart(keeper, folder, heldFileRetry))
        {
            trayMenu.Name(failure);
        }

        foreach (var failure in await StashImport.FinishAtStart(keeper, recycleBin, folder, heldFileRetry))
        {
            trayMenu.Name(failure);
        }

        // Theme, Start with Windows and the shortcuts were applied from the file read before the import was finished.
        trayMenu.NameIfFailed(ApplyFinishedImport(TravellingSettings.Of(settings.Settings), keeper.Current, applyTravelling));

        // An import still recorded may have renamed the stash away: opening its folder would make an empty stash there.
        if (keeper.Current.ImportPending is not null)
        {
            var unfinished = new Failure(StashImport.FinishAction, Path.GetFileName(folder), TransferReasons.Unfinished);
            trayMenu.Name(unfinished);
            listing.StashFailed(unfinished);
            return null;
        }

        return await Opened(await openStore(folder), opened);
    }

    /// <summary>
    /// Hands the opened stash to the window, which until now showed the opening and nothing of the stash (State: Every
    /// change), or names why it did not open there.
    /// </summary>
    private async Task<StashStore?> Opened(StoreOpening opening, Action<StashStore> opened)
    {
        var store = NameOpening(opening, trayMenu);
        if (store is null)
        {
            listing.StashFailed(opening.Failures[0]);
            return null;
        }

        opened(store);
        await listing.StashOpened(store);
        return store;
    }

    /// <summary>
    /// After the start finished an import a kill interrupted (State: Import stash…): Theme, Start with Windows and the
    /// shortcuts were applied from <paramref name="applied"/>, read before, so when the import's record has replaced them
    /// since, <paramref name="now"/> is applied as after a live import (G36); otherwise nothing is.
    /// </summary>
    internal static Outcome ApplyFinishedImport(TravellingSettings applied, Settings now, Func<Settings, Outcome> applyTravelling) =>
        TravellingSettings.Of(now) == applied ? Outcome.Done : applyTravelling(now);

    /// <summary>
    /// G2: the window opens pinned at start only on the first run, and only when the stash holds nothing, asked of the
    /// store itself, so a query typed into a window summoned during the opening does not hide that it is empty.
    /// </summary>
    internal static async Task<bool> OpensPinnedAtStart(SettingsLoad settings, StashStore store) =>
        settings.FirstRun && await store.All() is { Succeeded: true, Value.Count: 0 };

    /// <summary>
    /// Where Settings puts the stash, or the default location. While the settings file cannot be read, where the stash
    /// is is unknown, and it stays closed: one opened at the default instead would take imports that the next start,
    /// reading the real location, would not show (G28).
    /// </summary>
    internal static Outcome<string> StashLocation(SettingsLoad settings, ILocations locations) =>
        settings.AsSaved
            ? Outcome<string>.Of(settings.Settings.StashFolder ?? locations.DefaultStashFolder)
            : new Failure("Open the stash", Settings.FileName, "the settings could not be read, so where the stash is is unknown");

    /// <summary>Names every failure the opening reports and hands back the store, or null when it did not open.</summary>
    internal static StashStore? NameOpening(StoreOpening opening, TrayMenu trayMenu)
    {
        foreach (var failure in opening.Failures)
        {
            trayMenu.Name(failure);
        }

        return opening.Store;
    }
}
