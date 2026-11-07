using Avalonia;
using Avalonia.Controls;
using ImageStash.Store;
using ImageStash.Window;
using ImageStash.Windows.Seams;
using SkiaSharp;

namespace ImageStash.Windows.Tests.Support;

/// <summary>
/// A stash carried into the shown window by Import stash… (G36): an export of a stash of the check's own, its import over
/// the window's parts, the leftovers it leaves removed (the archive's folder and the replaced stash's real Recycle Bin
/// entry), the notices an import waits for closed, and every tile read until it shows its picture.
/// </summary>
public static class TransferredStash
{
    /// <summary>
    /// An export of a stash of the check's own holding a flat 8 × 6 picture for each of <paramref name="pictures"/>, in
    /// order, so their identities count from 1, written in a folder of its own under %TEMP%.
    /// </summary>
    public static async Task<string> ExportOf(ShownWindow shown, params (string Name, SKColor Colour)[] pictures)
    {
        var folder = Path.Combine(Path.GetTempPath(), "imagestash-export-check-" + Guid.NewGuid().ToString("N"));
        var store = (await StashStore.Open(Path.Combine(folder, "stash"), new WindowsRecycleBin(), global::ImageStash.Files.HeldFileRetry.Duration, TimeProvider.System)).Store!;
        try
        {
            var listing = new Listing(_ => { });
            await listing.StashOpened(store);
            var import = new Import(listing, new Notices(TimeProvider.System), global::ImageStash.Files.HeldFileRetry.Duration);
            foreach (var (name, colour) in pictures)
            {
                var source = Path.Combine(folder, name + ".png");
                File.Copy(shown.Picture(name + ".png", 8, 6, (_, _) => colour), source);
                await import.Drop(new global::ImageStash.Seams.DropContents([new global::ImageStash.Seams.DroppedItem(name + ".png", source, null, IsFolder: false)], false, false, []));
            }

            var keeper = new SettingsKeeper(folder, Settings.Load(folder, global::ImageStash.Files.HeldFileRetry.Duration), global::ImageStash.Files.HeldFileRetry.Duration, SettingsKeeper.Quiet, _ => { });
            var archive = Path.Combine(folder, "export.zip");
            var exported = await store.Export(keeper, archive);
            Assert.IsTrue(exported.Succeeded, exported.Failure?.Sentence);
            return archive;
        }
        finally
        {
            store.Dispose();
        }
    }

    /// <summary>Import stash… of <paramref name="archive"/> over the shown window's parts, chosen, offered and confirmed on the UI thread.</summary>
    public static async Task<Outcome> Import(ShownWindow shown, string archive)
    {
        var dialogs = new PresetDialogs { PickedFile = archive };
        var transferring = TrayDesktop.Get(() => new Transferring(shown.Listing, shown.Notices, shown.Settings, dialogs, new WindowsRecycleBin(), shown.Handoff, shown.OpenWith,
            shown.Moving, _ => Outcome.Done, TimeProvider.System, global::ImageStash.Files.HeldFileRetry.Duration));
        var chosen = await TrayDesktop.Get(() => transferring.ChooseArchive(shown.Window));
        Assert.IsTrue(chosen.Succeeded, chosen.Failure?.Sentence);
        return await TrayDesktop.Get(() => transferring.ConfirmImport());
    }

    /// <summary>The archive's folder deleted, and the replaced stash the import sent to the real Recycle Bin removed from it for good.</summary>
    public static void RemoveImportsLeftovers(ShownWindow shown, string archive)
    {
        Directory.Delete(Path.GetDirectoryName(archive)!, recursive: true);
        var stash = TrayDesktop.Get(() => shown.Listing.Store?.Folder.Root);
        if (stash is null)
        {
            return;
        }

        foreach (var entry in RecycleBinEntries.Within(Path.GetDirectoryName(stash)!))
        {
            if (Directory.Exists(entry.Recycled))
            {
                Directory.Delete(entry.Recycled, recursive: true);
            }
            else
            {
                File.Delete(entry.Recycled);
            }

            File.Delete(entry.Record);
        }
    }

    /// <summary>Every notice closed, so nothing an import waits for stands.</summary>
    public static void CloseNotices(ShownWindow shown) => TrayDesktop.Run(() =>
    {
        foreach (var notice in shown.Notices.Shown.ToList())
        {
            shown.Notices.Close(notice);
        }
    });

    /// <summary>
    /// Renders the window again until every tile of an item named in <paramref name="colours"/> shows that colour in its
    /// middle, and fails, naming the first that do not, after ten seconds.
    /// </summary>
    public static async Task EveryTileShows(ShownWindow shown, Dictionary<string, uint> colours, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (true)
        {
            var rendered = await RenderedWindow.Of(shown);
            var wrong = TrayDesktop.Get(() =>
            {
                var root = (Control)shown.Window.Content!;
                var items = shown.Listing.Items.ToDictionary(item => item.Id);
                return shown.Grid.Rows.SelectMany(row => row.Tiles)
                    .Where(tile => colours.ContainsKey(items[tile.Id].Name))
                    .Select(tile => (Name: items[tile.Id].Name, At: shown.Grid.TranslatePoint(new Point(tile.X + tile.Width / 2, tile.Y + tile.Height / 2), root)!.Value * rendered.Scaling))
                    .Select(tile => (tile.Name, Colour: rendered.At((int)tile.At.X, (int)tile.At.Y)))
                    .Where(tile => tile.Colour != colours[tile.Name])
                    .Select(tile => $"{tile.Name}: {tile.Colour:X8}, not {colours[tile.Name]:X8}")
                    .Concat(colours.Keys.Where(name => !items.Values.Any(item => item.Name == name)).Select(name => $"{name}: not listed"))
                    .ToList();
            });
            if (wrong.Count == 0)
            {
                return;
            }

            Assert.IsLessThan(deadline, DateTime.UtcNow, $"{what}: {string.Join("; ", wrong)}");
            await Task.Delay(100);
        }
    }
}
