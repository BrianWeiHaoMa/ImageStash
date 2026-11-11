using Avalonia.Controls;
using Avalonia.Input;
using ImageStash.Store;
using ImageStash.Window;
using ImageStash.Windows.Tests.Support;
using SkiaSharp;
using static ImageStash.Windows.Tests.Support.ShownSettings;

namespace ImageStash.Windows.Tests.Window;

/// <summary>
/// The thumbnail size across the Settings page and the stash window's grid on the desktop (G6, G32): the page's list of
/// the five sizes, a choice there laid out by the grid at once with nothing else done; a size stepped in the grid by the
/// keys or Ctrl+wheel shown in the open page's list as it is reached, the page writing nothing back; the list reached with
/// Tab and changed from the keyboard (G7); and, while a Move is asked, the list taking no change yet showing the grid's
/// steps (R9).
/// </summary>
[TestClass]
public sealed class ThumbnailSizePickerTests
{
    private const ushort VkTab = 0x09, VkDown = 0x28, VkControl = 0x11;

    [TestMethod]
    public async Task TheGridLaysItsRowsOutAtTheSizeChosenInThePagesListWithNothingElseDone()
    {
        using var desk = await Desk.Open();
        TrayDesktop.Run(() =>
        {
            var list = List(desk.Page);
            CollectionAssert.AreEqual(new[] { "Small", "Medium", "Large", "Extra large", "Huge" }, list.Items.OfType<ComboBoxItem>().Select(item => item.Content).ToList(), "the five, smallest first (G32)");
            Assert.AreEqual("Medium", Shown(list), "the size in effect");
            list.SelectedItem = list.Items.OfType<ComboBoxItem>().Last();
        });

        await desk.GridAt(ThumbnailSize.Huge);
        TrayDesktop.Run(() => Assert.AreEqual(ThumbnailSize.Huge, desk.Shown.Settings.Current.ThumbnailSize, "remembered (G6)"));
    }

    [TestMethod]
    public async Task EachSizeSteppedInTheGridByItsKeysOrCtrlWheelShowsInTheOpenPagesListAndThePageWritesNothingBack()
    {
        using var pointer = TestMouse.PutBackAfterwards();
        using var desk = await Desk.Open();
        var told = new List<ThumbnailSize>();
        TrayDesktop.Run(() => desk.Shown.Window.ThumbnailSizing.Changed += () => told.Add(desk.Shown.Window.ThumbnailSizing.Current));

        foreach (var (size, words) in new[] { (ThumbnailSize.Large, "Large"), (ThumbnailSize.ExtraLarge, "Extra large") })
        {
            TrayDesktop.Run(() => StepUp(desk.Shown));
            await desk.GridAt(size);
            await ShownWindow.Eventually(() => TrayDesktop.Get(() => Shown(List(desk.Page)) == words));
        }

        // A real wheel over the grid with Ctrl held, a notch at a time: up to the largest and once more, held there, then
        // down to the smallest and once more, held there.
        var over = TestMouse.Centre(desk.Shown.Grid);
        using (TestHotKeys.Hold(VkControl))
        {
            foreach (var (notch, size, words) in new[]
            {
                (-1, ThumbnailSize.Huge, "Huge"), (-1, ThumbnailSize.Huge, "Huge"), (1, ThumbnailSize.ExtraLarge, "Extra large"), (1, ThumbnailSize.Large, "Large"),
                (1, ThumbnailSize.Medium, "Medium"), (1, ThumbnailSize.Small, "Small"), (1, ThumbnailSize.Small, "Small"),
            })
            {
                TestMouse.WheelDown(over, notch);
                await desk.GridAt(size);
                await ShownWindow.Eventually(() => TrayDesktop.Get(() => Shown(List(desk.Page)) == words));
            }
        }

        CollectionAssert.AreEqual(
            new[] { ThumbnailSize.Large, ThumbnailSize.ExtraLarge, ThumbnailSize.Huge, ThumbnailSize.ExtraLarge, ThumbnailSize.Large, ThumbnailSize.Medium, ThumbnailSize.Small },
            TrayDesktop.Get(() => told.ToList()),
            "each step once and the notch past each end none, nothing more: the page's list, shown each size, changed nothing itself (G32)");
    }

    [TestMethod]
    public async Task TheListIsReachedWithTabAndChangedFromTheKeyboard()
    {
        using var desk = await Desk.Open();
        var handle = TrayDesktop.Get(() => desk.Page.TryGetPlatformHandle()!.Handle);
        TrayDesktop.Run(desk.Page.Activate);
        FrontWindow.WaitUntilInFront(handle);
        TrayDesktop.Run(() => Control(desk.Page, SettingsPage.ThemeTitle).Focus());

        TestHotKeys.Tap(VkTab);
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => List(desk.Page).IsFocused));
        TestHotKeys.Tap(VkDown);

        await desk.GridAt(ThumbnailSize.Large);
        TrayDesktop.Run(() => Assert.AreEqual("Large", Shown(List(desk.Page)), "Down chose the next size (G7)"));
    }

    [TestMethod]
    public async Task WhileAMoveIsAskedTheListTakesNoChangeYetShowsASizeSteppedInTheGrid()
    {
        var chosen = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "imagestash-size-move-check-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            using var desk = await Desk.Open(moving: true);
            desk.Dialogs.PickedFolder = chosen;
            // A notice whose Undo still stands, as a Delete's does: the Move waits for it (State: Move).
            var standing = TrayDesktop.Get(() => desk.Shown.Notices.Raise("Deleted “size a”.", "Undo", () => Task.FromResult(Outcome.Done)));
            TrayDesktop.Run(() => ShownWindow.RaiseClick((Button)Control(desk.Page, SettingsPage.MoveWords)));
            await ShownWindow.Eventually(() => TrayDesktop.Get(() => desk.Page.Page.MoveAsked));

            TrayDesktop.Run(() =>
            {
                Assert.IsFalse(List(desk.Page).IsEffectivelyEnabled, "the list takes no change while the Move is asked, as every row (R9)");
                StepUp(desk.Shown);
            });
            await desk.GridAt(ThumbnailSize.Large);
            await ShownWindow.Eventually(() => TrayDesktop.Get(() => Shown(List(desk.Page)) == "Large"));

            TrayDesktop.Run(() => desk.Shown.Notices.Close(standing));
            await ShownWindow.Eventually(() => TrayDesktop.Get(() => !desk.Page.Page.MoveAsked && desk.Shown.Listing.Settled));
            TrayDesktop.Run(() => Assert.IsTrue(List(desk.Page).IsEffectivelyEnabled, "and takes changes again once it has ended"));
        }
        finally
        {
            Directory.Delete(chosen, recursive: true);
        }
    }

    private static ComboBox List(SettingsWindow page) => (ComboBox)Control(page, SettingsPage.ThumbnailSizeTitle);

    /// <summary>Ctrl with + pressed in the stash window's search box, which keys are routed by (G6).</summary>
    private static void StepUp(ShownWindow shown)
    {
        shown.Search.Focus();
        ShownWindow.Press(shown.Search, Key.OemPlus, KeyModifiers.Control);
    }

    private static string? Shown(ComboBox list) => (list.SelectedItem as ComboBoxItem)?.Content as string;

    /// <summary>A stash window of the check's own with three pictures, and the Settings page open beside it over its thumbnail size.</summary>
    private sealed class Desk : IDisposable
    {
        private Desk(SettingsFolder settings, PresetDialogs dialogs, ShownWindow shown, SettingsWindow page)
        {
            Settings = settings;
            Dialogs = dialogs;
            Shown = shown;
            Page = page;
        }

        public SettingsFolder Settings { get; }

        /// <summary>The stash window's pickers, Move…'s folder picker among them.</summary>
        public PresetDialogs Dialogs { get; }

        public ShownWindow Shown { get; }

        public SettingsWindow Page { get; }

        /// <param name="moving">The page's Move… as the stash window asks it, instead of over a stash that never opens.</param>
        public static async Task<Desk> Open(bool moving = false)
        {
            var settings = new SettingsFolder();
            var dialogs = new PresetDialogs();
            var shown = ShownWindow.Open(dialogs: dialogs);
            await shown.OpenStash(
                shown.Picture("size a.png", 40, 40, (_, _) => SKColors.Red),
                shown.Picture("size b.png", 40, 41, (_, _) => SKColors.Green),
                shown.Picture("size c.png", 40, 42, (_, _) => SKColors.Blue));
            TransferredStash.CloseNotices(shown);
            var page = TrayDesktop.Get(() => SettingsWindow.Open(null, () => settings.Page(moving: moving ? shown.Moving : null, thumbnails: shown.Window.ThumbnailSizing)));
            await ShownWindow.Eventually(() => TrayDesktop.Get(() => page.IsVisible));
            await GridAtFor(shown, ThumbnailSize.Medium);
            return new Desk(settings, dialogs, shown, page);
        }

        /// <summary>Waits until the settings hold <paramref name="size"/> and the grid's rows are those its justified rows make at that size.</summary>
        public Task GridAt(ThumbnailSize size) => GridAtFor(Shown, size);

        public void Dispose()
        {
            TrayDesktop.Run(Page.Close);
            Shown.Dispose();
            Settings.Dispose();
        }

        private static Task GridAtFor(ShownWindow shown, ThumbnailSize size) =>
            ShownWindow.Eventually(() => TrayDesktop.Get(() =>
            {
                var laid = JustifiedRows.Layout(shown.Listing.Items, shown.Grid.Bounds.Width, JustifiedRows.RowHeight(size));
                return shown.Settings.Current.ThumbnailSize == size && shown.Grid.Rows.Count == laid.Count
                    && shown.Grid.Rows.Zip(laid).All(rows => Math.Abs(rows.First.Height - rows.Second.Height) < 0.5 && rows.First.Tiles.Count == rows.Second.Tiles.Count);
            }));
    }
}
