using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using ImageStash.Capture;
using ImageStash.Seams;
using ImageStash.Windows.Seams;
using ImageStash.Windows.Tests.Support;
using static ImageStash.Windows.Tests.Support.ShownWindow;

namespace ImageStash.Windows.Tests.Capture;

/// <summary>
/// The recording bar laid out once, on the real desktop at the primary display's own scaling, which a person sets to
/// 150 % for this check (`tools/display-scale.ps1 -Set 150`): Stop's rectangle on the screen is the one Record held, and
/// the bar keeps its place and size, from the wait to the recording; beside a region at each edge of the working area,
/// naming the longest shortcut a person can record, the bar lies inside the working area and every control whole inside
/// the bar, Cancel at its left end under its word and Record or Stop at its right (G15). These checks show the frame and
/// the bar around regions at the primary display's middle and edges, and read their places; they click nothing.
/// </summary>
[TestClass]
public sealed class RecordingBarLayoutTests
{
    [TestMethod]
    public async Task StopOnTheScreenIsWhereRecordWasAndTheBarKeepsItsPlaceAndSize()
    {
        using var desk = new BarDesk();
        desk.Show(DisplayPlaces.Rect(DisplayPlaces.Primary(), 300, 200, 400, 250), "Ctrl+Shift+PrtScn");
        await desk.Shown();
        var (record, waiting) = TrayDesktop.Get(() => (desk.OnScreen(desk.Frame.Bar!.RecordButton!), desk.OnScreen(desk.Frame.Bar!)));

        desk.Record();
        var (stop, recording) = TrayDesktop.Get(() => (desk.OnScreen(desk.Frame.Bar!.StopButton!), desk.OnScreen(desk.Frame.Bar!)));

        Assert.AreEqual(record, stop, "Stop where Record was, so the pointer that clicked Record rests on Stop");
        Assert.AreEqual(waiting, recording, "the bar neither measured nor placed again");

        // A pause or a remap while the bar shows reaches the live bar, which keeps its place.
        TrayDesktop.Run(() => desk.Frame.NameShortcut("Ctrl+Alt+R"));
        CollectionAssert.Contains(TrayDesktop.Get(() => desk.Frame.Bar!.Words).ToList(), "Ctrl+Alt+R stops");
        TrayDesktop.Run(() => desk.Frame.NameShortcut(null));
        Assert.IsFalse(TrayDesktop.Get(() => desk.Frame.Bar!.Words).Any(words => words.Contains("stops", StringComparison.Ordinal)), "no shortcut named while it would not act");
        Assert.AreEqual(recording, TrayDesktop.Get(() => desk.OnScreen(desk.Frame.Bar!)), "the bar where it stood");
    }

    [TestMethod]
    public async Task BesideARegionAtEachEdgeNamingTheLongestShortcutTheBarAndEveryControlStayWhole()
    {
        var longest = "Win+Ctrl+Alt+Shift+" + Enum.GetNames<Key>().MaxBy(name => name.Length);
        var area = TrayDesktop.Get(() => new WindowsScreen().Displays().Single(display => display.IsPrimary).WorkingArea);
        var side = DisplayPlaces.Scaled(DisplayPlaces.Primary(), 160);
        var regions = new Dictionary<string, PixelRect>
        {
            ["at the left edge"] = new(area.X, area.Y + side, side, side),
            ["at the right edge"] = new(area.Right - side, area.Y + side, side, side),
            ["at the top edge"] = new(area.X + 2 * side, area.Y, side, side),
            ["at the bottom edge"] = new(area.X + 2 * side, area.Bottom - side, side, side),
        };
        foreach (var (where, region) in regions)
        {
            using var desk = new BarDesk();
            desk.Show(region, longest);
            await desk.Shown();
            foreach (var look in new[] { "while the region waits", "once it records" })
            {
                if (look == "once it records")
                {
                    desk.Record();
                }

                var (bar, cancel, primary, cancelWord, words) = TrayDesktop.Get(() => (
                    desk.OnScreen(desk.Frame.Bar!),
                    desk.OnScreen(desk.Frame.Bar!.CancelButton!),
                    desk.OnScreen(desk.Frame.Bar!.RecordButton ?? desk.Frame.Bar!.StopButton!),
                    ((TextBlock)desk.Frame.Bar!.CancelButton!.Content!).Text,
                    desk.Frame.Bar!.Words.ToList()));
                Assert.IsTrue(Within(bar, area), $"{where}, {look}: the bar {bar} inside the working area {area} (G15)");
                Assert.IsTrue(Within(cancel, bar) && Within(primary, bar), $"{where}, {look}: Cancel {cancel} and Record or Stop {primary} whole inside the bar {bar}");
                Assert.IsTrue(cancel.Right < primary.X, $"{where}, {look}: Cancel at the bar's left, away from Record or Stop at its right");
                Assert.AreEqual("Cancel", cancelWord);
                var named = look == "once it records" ? RecordingBar.Stops(longest) : RecordingBar.StartsAndStops(longest);
                CollectionAssert.Contains(words, named, $"{where}, {look}: on a working area of 1280 units across, even the longest shortcut is named whole: {string.Join(" | ", words)}");
            }
        }
    }

    /// <summary>Whether <paramref name="inner"/> lies within <paramref name="outer"/>, but for the pixel the frame gives a window on its right and bottom rather than fall one short.</summary>
    private static bool Within(PixelRect inner, PixelRect outer) =>
        inner.X >= outer.X && inner.Y >= outer.Y && inner.Right <= outer.Right + 1 && inner.Bottom <= outer.Bottom + 1;

    private sealed record Readings(long ElapsedMs, long BytesWritten) : IRecordingReadings;

    /// <summary>The live frame and bar over the real seams, on the primary display, its acts doing nothing.</summary>
    private sealed class BarDesk : IDisposable
    {
        private readonly WindowsTray tray;

        public BarDesk()
        {
            TrayDesktop.UseProductTheme();
            tray = new WindowsTray();
            Frame = TrayDesktop.Get(() => new RecordingFrame(new WindowsQuietWindows(tray), new WindowsScreen(), Named.Add, new PacedClock()));
        }

        public RecordingFrame Frame { get; }

        public List<Failure> Named { get; } = [];

        /// <summary>The frame and the bar around <paramref name="region"/>, waiting, the bar naming <paramref name="shortcut"/>.</summary>
        public void Show(PixelRect region, string shortcut) => TrayDesktop.Run(() =>
        {
            var primary = new WindowsScreen().Displays().Single(display => display.IsPrimary);
            var shown = Frame.Show(new SelectedRegion(primary, region), TimeSpan.FromSeconds(30), shortcut, () => { }, () => { }, () => { });
            Assert.IsTrue(shown.Succeeded, shown.Failure?.Sentence);
        });

        /// <summary>Until the bar's window shows, laid out at its display's scaling.</summary>
        public async Task Shown()
        {
            await Eventually(() => TrayDesktop.Get(() => Frame.BarWindow is { IsVisible: true }));
            TrayDesktop.Run(() => Frame.BarWindow!.UpdateLayout());
        }

        /// <summary>The recording look, as the session gives it at Record, laid out.</summary>
        public void Record() => TrayDesktop.Run(() =>
        {
            Assert.IsTrue(Frame.Recording(new Readings(1_650, 629_146)).Succeeded);
            Frame.BarWindow!.UpdateLayout();
        });

        /// <summary>Where <paramref name="control"/> lies on the desktop, in physical pixels. UI thread.</summary>
        public PixelRect OnScreen(Control control) =>
            new(control.PointToScreen(default), control.PointToScreen(new Point(control.Bounds.Width, control.Bounds.Height)));

        public void Dispose()
        {
            TrayDesktop.Run(Frame.Close);
            tray.Dispose();
        }
    }
}
