using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using ImageStash.Capture;
using ImageStash.Look;
using ImageStash.Tests.Support;

namespace ImageStash.Tests.Capture;

/// <summary>
/// Where the recording's bar goes (G15): beneath the region, centred on it, while the display's working area has room
/// below; above it when there is room above and none below; over its bottom edge, inside it, when there is room on
/// neither side; always within the working area and never wider than it, so never under the taskbar. And the bar itself,
/// through its own controls: laid out once, Cancel worded at its left end and Record at its right, Record becoming Stop
/// in the same place, every part holding the same looks in both states so the bar is one size; the time against the cap,
/// the size so far and the record shortcut's words; the second click of a double-click on Record not ending the take;
/// Cancel acting at its click; no focus and no tooltip; the saving named alone after Stop; its words readable on its
/// grounds in both takes.
/// </summary>
[TestClass]
public sealed class RecordingBarTests
{
    /// <summary>The primary display's working area above a 48-pixel taskbar.</summary>
    private static readonly PixelRect WorkingArea = new(0, 0, 1280, 752);

    private static readonly PixelSize Bar = new(400, 44);

    private const int Gap = 14;

    [TestMethod]
    public void WithRoomBelowTheBarIsBeneathTheRegionCentredOnIt()
    {
        Assert.AreEqual(new PixelRect(300, 414, 400, 44), RecordingBar.Place(new PixelRect(200, 100, 600, 300), WorkingArea, Bar, Gap));
        Assert.AreEqual(new PixelRect(300, 708, 400, 44), RecordingBar.Place(new PixelRect(200, 100, 600, 594), WorkingArea, Bar, Gap), "just room below: the bar's bottom on the working area's");
    }

    [TestMethod]
    public void WithNoRoomBelowTheBarIsAboveTheRegion()
    {
        Assert.AreEqual(new PixelRect(300, 342, 400, 44), RecordingBar.Place(new PixelRect(200, 400, 600, 320), WorkingArea, Bar, Gap), "the region's bottom within the gap and the bar's height of the working area's");
        Assert.AreEqual(new PixelRect(300, 242, 400, 44), RecordingBar.Place(new PixelRect(200, 300, 600, 500), WorkingArea, Bar, Gap), "a region reaching over the taskbar: above it, never under the taskbar");
    }

    [TestMethod]
    public void WithRoomOnNeitherSideTheBarIsOverTheRegionsBottomEdgeInsideIt()
    {
        Assert.AreEqual(new PixelRect(300, 694, 400, 44), RecordingBar.Place(new PixelRect(200, 0, 600, 752), WorkingArea, Bar, Gap), "a region as tall as the working area: inside it, bottom-aligned");
        Assert.AreEqual(new PixelRect(440, 694, 400, 44), RecordingBar.Place(new PixelRect(0, 0, 1280, 800), WorkingArea, Bar, Gap), "the whole display: inside it, above the taskbar");
        Assert.AreEqual(new PixelRect(300, 0, 400, 44), RecordingBar.Place(new PixelRect(200, 10, 600, 40), new PixelRect(0, 0, 1280, 80), Bar, Gap), "never above the working area's top");
    }

    [TestMethod]
    public void TheBarStaysWithinTheWorkingAreaAcross()
    {
        Assert.AreEqual(new PixelRect(0, 194, 400, 44), RecordingBar.Place(new PixelRect(0, 100, 120, 80), WorkingArea, Bar, Gap), "a region at the left edge narrower than the bar: the bar's left is the working area's");
        Assert.AreEqual(new PixelRect(880, 194, 400, 44), RecordingBar.Place(new PixelRect(1200, 100, 80, 80), WorkingArea, Bar, Gap), "at the right edge: the bar's right is the working area's");
        var second = new PixelRect(1280, 0, 1920, 1152);
        Assert.AreEqual(new PixelRect(1280, 314, 400, 44), RecordingBar.Place(new PixelRect(1290, 200, 100, 100), second, Bar, Gap), "on a second display, within its own working area");
    }

    [TestMethod]
    public void TheBarReadsTheTimeAgainstTheCapAndTheSizeSoFar()
    {
        Assert.AreEqual("0:00.0", RecordingBar.Time(0));
        Assert.AreEqual("0:01.6", RecordingBar.Time(1_650), "tenths, never rounded up past the time recorded");
        Assert.AreEqual("1:01.2", RecordingBar.Time(61_250));
        Assert.AreEqual("0:30", RecordingBar.Time(30_000, tenths: false), "the cap without tenths");
        Assert.AreEqual("~0 KB", RecordingBar.Size(0), "nothing written yet");
        Assert.AreEqual("~614 KB", RecordingBar.Size(629_146));
        Assert.AreEqual("~1.1 MB", RecordingBar.Size(1_153_434));
    }

    /// <summary>
    /// The bar holds room for its widest readings (G15): every size reading up to a clip of 10,000 MB and every elapsed
    /// reading up to the cap is no longer than the reading it reserves, and one as long differs from it only in digits,
    /// which the bar draws all of one width, so no reading runs under Stop. The desktop checks measure the bar itself.
    /// </summary>
    [TestMethod]
    public void TheBarReservesItsWidestReadingsSoEveryLaterOneFitsInTheirRoom()
    {
        Assert.AreEqual("~10000.0 MB", RecordingBar.WidestSize);
        var sizes = Enumerable.Range(0, 2001).Select(step => step * (10_000L * 1024 * 1024 / 2000))
            .Concat([1, 1023, 1024 * 1024 - 1, 9_999L * 1024 * 1024, 10_485_655_143, 10_485_759_999]);
        foreach (var bytes in sizes)
        {
            AssertFits(RecordingBar.Size(bytes), RecordingBar.WidestSize, $"{bytes} bytes");
        }

        foreach (var cap in new[] { 10, 15, 30, 60, 90, 600 }.Select(seconds => TimeSpan.FromSeconds(seconds)))
        {
            var widest = RecordingBar.WidestElapsed(cap);
            for (var elapsed = 0L; elapsed <= cap.TotalMilliseconds; elapsed += 100)
            {
                AssertFits(RecordingBar.Time(elapsed), widest, $"{elapsed} ms under a cap of {cap}");
            }
        }

        Assert.AreEqual("1:00.0", RecordingBar.WidestElapsed(TimeSpan.FromSeconds(60)), "the 60-second cap's own reading, as long as its last tenth's");
    }

    [TestMethod]
    public void TheBarIsNeverWiderThanTheWorkingArea()
    {
        Assert.AreEqual(new PixelRect(0, 214, 1280, 44), RecordingBar.Place(new PixelRect(0, 100, 200, 100), WorkingArea, new PixelSize(1400, 44), Gap), "the bar's place cut to the working area's width; the words give way inside it");
    }

    /// <summary>
    /// Record becomes Stop in the same place: the one button offering Record while the region waits offers Stop once it
    /// records, at the bar's right end, and every part of the bar holds the same looks in both states (the readings at
    /// their widest, the shortcut's words as long as the wait's), so the bar measured in either is one size and nothing
    /// in it moves at Record or as a reading changes.
    /// </summary>
    [TestMethod]
    public void RecordBecomesStopInTheSamePlaceAndEveryPartHoldsTheSameLooksInBothStates() => ControlThread.Run(() =>
    {
        var (bar, _) = NewBar();
        var record = bar.RecordButton!;
        var row = (Grid)bar.Child!;
        var held = Held(row);

        Assert.AreSame(record, row.Children.Single(part => Grid.GetColumn(part) == row.ColumnDefinitions.Count - 1), "Record at the bar's right end");
        Assert.AreEqual(RecordingBar.RecordName, AutomationProperties.GetName(record));
        CollectionAssert.IsSubsetOf(new[] { RecordingBar.WidestElapsed(Cap), RecordingBar.WidestSize, RecordingBar.StartsAndStops(RecordShortcut), "Record", "Stop" }, held,
            $"room held from the wait for the widest readings, the wait's words and both buttons' looks: {string.Join(" | ", held)}");
        bar.Recording();
        bar.Refresh(new Readings(1_650, 629_146));

        Assert.AreSame(record, bar.StopButton, "the button that offered Record offers Stop, in its place");
        Assert.IsNull(bar.RecordButton, "no Record once it records");
        Assert.AreEqual(RecordingBar.StopName, AutomationProperties.GetName(record), "found by its name, Stop, as the scripts find it");
        Assert.AreSame(row, bar.Child, "the same row, not a wider one in its place");
        CollectionAssert.AreEqual(held, Held(row), "every part holds the looks it held while the region waited");
    });

    [TestMethod]
    public void CancelIsWordedAtTheBarsOtherEndAwayFromStop() => ControlThread.Run(() =>
    {
        var (bar, _) = NewBar();
        var cancel = bar.CancelButton!;
        var row = (Grid)bar.Child!;

        foreach (var look in new[] { "while the region waits", "once it records" })
        {
            if (look == "once it records")
            {
                bar.Recording();
            }

            Assert.AreSame(cancel, bar.CancelButton);
            Assert.AreEqual(0, Grid.GetColumn(cancel), $"Cancel at the bar's left end {look}");
            Assert.AreEqual(row.ColumnDefinitions.Count - 1, Grid.GetColumn(bar.RecordButton ?? bar.StopButton!), $"Record or Stop at the other {look}");
            Assert.AreEqual("Cancel", ((TextBlock)cancel.Content!).Text, $"under the word Cancel, not a bare glyph, {look}");
            Assert.AreEqual(RecordingBar.CancelName, AutomationProperties.GetName(cancel), "found by its name, as the scripts find it");
        }
    });

    [TestMethod]
    public void TheBarNamesTheRecordShortcutAsItStandsAndNoneWithoutOne() => ControlThread.Run(() =>
    {
        var (bar, _) = NewBar();
        CollectionAssert.AreEqual(new[] { "Up to 0:30", "Ctrl+Shift+PrtScn starts and stops" }, bar.Words.ToList(), "while the region waits");

        bar.Recording();
        bar.Refresh(new Readings(1_650, 629_146));
        CollectionAssert.AreEqual(new[] { "0:01.6", "/ 0:30", "~614 KB", "Ctrl+Shift+PrtScn stops" }, bar.Words.ToList(), "while it records");

        bar.NameShortcut("Ctrl+Alt+R");
        CollectionAssert.AreEqual(new[] { "0:01.6", "/ 0:30", "~614 KB", "Ctrl+Alt+R stops" }, bar.Words.ToList(), "a remap followed while it shows");
        bar.NameShortcut(null);
        CollectionAssert.AreEqual(new[] { "0:01.6", "/ 0:30", "~614 KB" }, bar.Words.ToList(), "none while it would not act");

        var (unnamed, _) = NewBar(shortcut: null);
        CollectionAssert.AreEqual(new[] { "Up to 0:30" }, unnamed.Words.ToList(), "none from the start");
    });

    [TestMethod]
    public void TheSecondClickOfADoubleClickOnRecordDoesNotEndTheTakeAndALaterClickOnStopDoes() => ControlThread.Run(() =>
    {
        var clock = new SteppedClock();
        var (bar, clicks) = NewBar(clock: clock);
        var button = bar.RecordButton!;

        Click(button);
        // The recording look, as the frame gives it once the recording has started.
        bar.Recording();
        clock.Pass(TimeSpan.FromMilliseconds(200));
        Click(button);
        Assert.AreEqual((1, 0), (clicks.Records, clicks.Stops), "the second click of a double-click lands on Stop and is let go");
        clock.Pass(TimeSpan.FromMilliseconds(300));
        Click(button);
        Assert.AreEqual(0, clicks.Stops, "at the double-click's time, still its click");
        clock.Pass(TimeSpan.FromMilliseconds(1));
        Click(button);
        Assert.AreEqual((1, 1), (clicks.Records, clicks.Stops), "after it, a click on Stop ends the recording (G15)");
    });

    [TestMethod]
    public void TheDoubleClickWaitedOutIsThePlatformsAndNeverLessThanHalfASecond()
    {
        Assert.AreEqual(TimeSpan.FromMilliseconds(500), RecordingBar.DoubleClickTime(null), "none named: Windows' default");
        Assert.AreEqual(TimeSpan.FromMilliseconds(500), RecordingBar.DoubleClickTime(TimeSpan.Zero), "none, or a very short time: still half a second");
        Assert.AreEqual(TimeSpan.FromMilliseconds(500), RecordingBar.DoubleClickTime(TimeSpan.FromMilliseconds(200)));
        Assert.AreEqual(TimeSpan.FromMilliseconds(900), RecordingBar.DoubleClickTime(TimeSpan.FromMilliseconds(900)), "a slower double-click the person set is theirs");
    }

    [TestMethod]
    public void StopActsAtOnceWhenTheRecordingWasNotStartedFromTheBar() => ControlThread.Run(() =>
    {
        var (bar, clicks) = NewBar();

        // Started by the shortcut or the tray: no click on Record to wait out.
        bar.Recording();
        Click(bar.StopButton!);

        Assert.AreEqual((0, 1), (clicks.Records, clicks.Stops));
    });

    [TestMethod]
    public void CancelActsWithinItsClickWhileTheRegionWaitsAndWhileItRecords() => ControlThread.Run(() =>
    {
        var (bar, clicks) = NewBar();

        Click(bar.CancelButton!);
        Assert.AreEqual(1, clicks.Cancels, "while the region waits, within the click");
        Click(bar.RecordButton!);
        bar.Recording();
        Click(bar.CancelButton!);
        Assert.AreEqual((1, 0, 2), (clicks.Records, clicks.Stops, clicks.Cancels), "the moment after Record too, no double-click waited out (G15)");
    });

    [TestMethod]
    public void TheButtonsTakeNoFocusAndOpenNoTooltip() => ControlThread.Run(() =>
    {
        var (bar, _) = NewBar();
        foreach (var button in new[] { bar.RecordButton!, bar.CancelButton! })
        {
            Assert.IsFalse(button.Focusable, $"{AutomationProperties.GetName(button)} takes no focus from the app being recorded (G15)");
            Assert.IsNull(ToolTip.GetTip(button), $"{AutomationProperties.GetName(button)} opens no tooltip, a window of its own (F4)");
        }
    });

    [TestMethod]
    public void AfterStopTheBarNamesOnlyTheSaving() => ControlThread.Run(() =>
    {
        var (bar, _) = NewBar();
        bar.Recording();

        bar.Saving();

        CollectionAssert.AreEqual(new[] { RecordingBar.SavingWords }, bar.Words.ToList(), "G35");
        Assert.IsNull(bar.RecordButton ?? bar.StopButton ?? bar.CancelButton, "no Record, Stop or Cancel while the clip is saved");
    });

    /// <summary>Words given less room than their whole width show not at all, never cut, and take no room from what stands beside them.</summary>
    [TestMethod]
    public void WordsGiveWayWholeWhereTheirRoomIsShortOfThem() => ControlThread.Run(() =>
    {
        var words = new Border { Width = 300, Height = 20 };
        var place = new RecordingBar.GiveWay { Child = words };

        place.Measure(new Size(200, 44));
        place.Arrange(new Rect(0, 0, 200, 44));
        Assert.AreEqual(200, place.DesiredSize.Width, "asks no more than the room");
        Assert.AreEqual(0, words.Opacity, "short of room: not shown");

        place.Measure(new Size(300, 44));
        place.Arrange(new Rect(0, 0, 300, 44));
        Assert.AreEqual(1, words.Opacity, "room for the whole: shown");
    });

    [TestMethod]
    [DataRow("Light")]
    [DataRow("Dark")]
    public void TheBarsWordsAndCancelsWordReadOnItsGroundRestingHoveredAndPressed(string theme) => ControlThread.Run(() =>
    {
        var look = Looks.All[0].Resources(theme == "Light" ? ThemeVariant.Light : ThemeVariant.Dark, Color.FromRgb(0x00, 0x67, 0xC0));
        Color Of(string key) => ((ISolidColorBrush)look[key]!).Color;

        foreach (var ground in new[] { LookKeys.BarGround, LookKeys.BarHoverGround })
        {
            var contrast = Take.Contrast(Of(LookKeys.BarWords), Of(ground));
            Assert.IsGreaterThanOrEqualTo(4.5, contrast, $"{theme}: the bar's words, Cancel's among them, on {ground} at {contrast:0.00} to 1 (F10)");
        }
    });

    private static void AssertFits(string reading, string reserved, string what)
    {
        Assert.IsLessThanOrEqualTo(reserved.Length, reading.Length, $"{what}: \"{reading}\" longer than \"{reserved}\"");
        if (reading.Length == reserved.Length)
        {
            Assert.IsTrue(reading.Zip(reserved).All(pair => pair.First == pair.Second || (char.IsAsciiDigit(pair.First) && char.IsAsciiDigit(pair.Second))),
                $"{what}: \"{reading}\" differs from \"{reserved}\" in more than digits");
        }
    }

    private static readonly TimeSpan Cap = TimeSpan.FromSeconds(30);

    private const string RecordShortcut = "Ctrl+Shift+PrtScn";

    private static (RecordingBar Bar, Clicks Clicks) NewBar(string? shortcut = RecordShortcut, TimeProvider? clock = null)
    {
        var clicks = new Clicks();
        var bar = new RecordingBar(Cap, shortcut, () => clicks.Records++, () => clicks.Stops++, () => clicks.Cancels++, clock ?? new SteppedClock(), RecordingBar.DefaultDoubleClick);
        return (bar, clicks);
    }

    /// <summary>A click on <paramref name="button"/> as a person's ends, its Click raised.</summary>
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    /// <summary>What a part holds room for, shown or not, in order: words (a readout's widest), marks, and the looks inside buttons.</summary>
    private static List<string> Held(Control part) => part switch
    {
        SteadyReadout readout => [readout.Widest ?? readout.Text ?? ""],
        TextBlock text => [text.Text ?? ""],
        Ellipse mark => [$"mark {mark.Width} × {mark.Height}"],
        Panel panel => panel.Children.SelectMany(Held).ToList(),
        Decorator { Child: { } child } => Held(child),
        ContentControl { Content: Control content } => Held(content),
        _ => [part.GetType().Name],
    };

    private sealed class Clicks
    {
        public int Records { get; set; }

        public int Stops { get; set; }

        public int Cancels { get; set; }
    }

    private sealed record Readings(long ElapsedMs, long BytesWritten) : IRecordingReadings;
}
