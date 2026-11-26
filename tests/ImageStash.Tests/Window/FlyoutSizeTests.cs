using Avalonia;
using ImageStash.Seams;
using ImageStash.Store;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// The flyout's size once its border is dragged (G2: "The flyout and the pinned window are each resized by dragging the
/// border, and flyout size, and pinned size and position, are remembered separately"): kept apart from the pinned size,
/// in the units and extent every showing reads back, so a summon from the shortcut or the tray, an unpin and a restart
/// each show it to the pixel on a display of any scaling (E1); shown whole inside a display too small for it, never
/// below the minimum, the remembered size kept (G1); read from a file an earlier build wrote (G31).
/// </summary>
[TestClass]
public sealed class FlyoutSizeTests
{
    private static readonly TimeSpan ShortRetry = TimeSpan.FromMilliseconds(300);
    private static readonly Display At100 = new(new PixelRect(0, 0, 1920, 1080), new PixelRect(0, 0, 1920, 1032), 1, IsPrimary: true);
    private static readonly Display At150 = new(new PixelRect(1920, 0, 2560, 1440), new PixelRect(1920, 0, 2560, 1392), 1.5, IsPrimary: false);
    private static readonly Display[] Mixed = [At100, At150];

    [TestMethod]
    public void AFlyoutResizeIsKeptAsTheFlyoutSizeAndAPinnedOneAsThePinnedSizeEachLeavingTheOther()
    {
        var before = new Settings { FlyoutSize = new WindowSize(500, 700), PinnedSize = new WindowSize(900, 650), PinnedPosition = new WindowPosition(40, 30) };

        var flyout = WindowChrome.Remembered(before, pinned: false, new WindowSize(612, 733));
        var pinned = WindowChrome.Remembered(before, pinned: true, new WindowSize(1000, 720));

        Assert.AreEqual(new WindowSize(612, 733), flyout.FlyoutSize, "a drag of the flyout's border is the flyout's size");
        Assert.AreEqual(before with { FlyoutSize = new WindowSize(612, 733) }, flyout, "and nothing else: the pinned size and position stay (G2)");
        Assert.AreEqual(new WindowSize(1000, 720), pinned.PinnedSize, "a drag of the pinned border is the pinned size");
        Assert.AreEqual(before with { PinnedSize = new WindowSize(1000, 720) }, pinned, "and the flyout's size stays");
    }

    [TestMethod]
    [DataRow(1.0)]
    [DataRow(1.25)]
    [DataRow(1.5)]
    [DataRow(1.75)]
    public void ADraggedSizeComesBackToThePixelAtTheSummonTheUnpinAndARestart(double scaling)
    {
        var display = new Display(new PixelRect(0, 0, 3840, 2160), new PixelRect(0, 0, 3840, 2112), scaling, IsPrimary: true);
        using var scratch = new ScratchFolder();
        for (var width = 1000; width < 1012; width++)
        {
            var dragged = new PixelSize(width, width + 211);
            // As the chrome keeps it: the whole window Windows reports, in device-independent units.
            var kept = WindowChrome.Remembered(new Settings(), pinned: false, new WindowSize(dragged.Width / scaling, dragged.Height / scaling));
            Settings.Write(scratch.Path, kept, ShortRetry);
            var restarted = Settings.Load(scratch.Path, ShortRetry).Settings.FlyoutSize;

            Assert.AreEqual(dragged, Summon.AtAnchor(new SummonAnchor(null, Caret: new PixelRect(1500, 1900, 2, 30)), [display], restarted).Size, $"{dragged} beside the caret");
            Assert.AreEqual(dragged, Summon.AtAnchor(new SummonAnchor(new PixelPoint(200, 200)), [display], restarted).Size, $"{dragged} at the pointer");
            Assert.AreEqual(dragged, Summon.AboveTrayIcon(new PixelRect(3600, 2112, 32, 48), [display], restarted).Size, $"{dragged} above the tray");
            Assert.AreEqual(dragged, Summon.Unpinned(new PixelPoint(300, 100), [display], restarted).Size, $"{dragged} after an unpin");
        }
    }

    [TestMethod]
    [DataRow("at the working area")]
    [DataRow("below the minimum")]
    [DataRow("far above the working area")]
    public void ARememberedSizeShowsWhollyInsideEachDisplayNeverBelowTheMinimumByEveryAnchor(string which)
    {
        foreach (var display in Mixed)
        {
            var area = display.WorkingArea;
            var size = which switch
            {
                "at the working area" => new WindowSize(area.Width / display.Scaling, area.Height / display.Scaling),
                "below the minimum" => new WindowSize(200, 150),
                _ => new WindowSize(6000, 5000),
            };
            var places = new Dictionary<string, PixelRect>
            {
                ["above the tray"] = Summon.AboveTrayIcon(new PixelRect(area.Right - 120, area.Bottom, 32, 48), Mixed, size),
                ["beside a caret"] = Summon.AtAnchor(new SummonAnchor(null, Caret: new PixelRect(area.X + area.Width / 2, area.Y + area.Height / 2, 2, 30)), Mixed, size),
                ["at the pointer"] = Summon.AtAnchor(new SummonAnchor(new PixelPoint(area.X + 40, area.Y + 40)), Mixed, size),
                ["centred on the active display"] = Summon.AtAnchor(new SummonAnchor(null, FrontWindow: new PixelRect(area.X + 10, area.Y + 10, 400, 300)), Mixed, size),
                ["after an unpin"] = Summon.Unpinned(new PixelPoint(area.Right - 50, area.Bottom - 50), Mixed, size),
            };
            var least = Summon.MinimumOn(display, default);
            foreach (var (anchor, place) in places)
            {
                var what = $"{which}, {anchor}, at {display.Scaling * 100} %: {place}";
                Assert.IsTrue(area.Contains(place), $"{what}: wholly inside the working area (G1)");
                Assert.IsGreaterThanOrEqualTo((int)Math.Round(least.Width * display.Scaling), place.Width, $"{what}: never narrower than the minimum (G20)");
                Assert.IsGreaterThanOrEqualTo((int)Math.Round(least.Height * display.Scaling), place.Height, $"{what}: never shorter than the minimum");
                // Beside a caret it is narrowed instead, so the caret stays in sight (SummonTests).
                if (which == "at the working area" && anchor != "beside a caret")
                {
                    Assert.AreEqual(area.Size, place.Size, $"{what}: at the size remembered, which the display holds");
                }
            }
        }
    }

    [TestMethod]
    public void AShowingNarrowedBesideACaretLeavesTheRememberedSizeForTheNextSummon()
    {
        // Scaled up so far that no side of a caret has room for the flyout's width (as SummonTests' narrowed case).
        var cramped = new Display(new PixelRect(0, 0, 1280, 800), new PixelRect(0, 0, 1280, 752), 2, IsPrimary: true);
        var remembered = new WindowSize(560, 600);

        var narrowed = Summon.AtAnchor(new SummonAnchor(null, Caret: new PixelRect(620, 380, 2, 40)), [cramped], remembered);
        var next = Summon.AtAnchor(new SummonAnchor(null, Caret: new PixelRect(1500, 900, 2, 30)), [At100], remembered);

        Assert.IsLessThan(1120, narrowed.Width, $"narrowed for that showing: {narrowed}");
        Assert.AreEqual(new PixelSize(560, 600), next.Size, "the next summon, with room, shows the size remembered, not the narrowed one");
    }

    [TestMethod]
    public void ASettingsFileFromAnEarlierBuildOpensTheFlyoutAtTheSizeItHolds()
    {
        using var scratch = new ScratchFolder();
        // As a build before the flyout could be resized wrote it, or a person by hand.
        File.WriteAllText(scratch.File(Settings.FileName), """{"version":1,"theme":"dark","flyoutSize":{"width":612.5,"height":701}}""");

        var load = Settings.Load(scratch.Path, ShortRetry);

        Assert.IsNull(load.Failure);
        Assert.AreEqual(new WindowSize(612.5, 701), load.Settings.FlyoutSize, "kept as written (G31)");
        Assert.AreEqual(WindowSize.PinnedDefault, load.Settings.PinnedSize, "the pinned size it lacks takes its default");
        Assert.AreEqual(new PixelSize(919, 1052), Summon.AtAnchor(new SummonAnchor(new PixelPoint(2000, 100)), Mixed, load.Settings.FlyoutSize).Size, "and the summon shows it, here at 150 %");
    }
}
