using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using ImageStash.Capture;
using ImageStash.Seams;
using ImageStash.Windows.Seams;
using ImageStash.Windows.Tests.Support;
using static ImageStash.Windows.Tests.Support.ShownWindow;

namespace ImageStash.Windows.Tests.Capture;

/// <summary>
/// The recording's frame and bar on the real desktop (G15, F4, S4), first showing a region waiting, then the same two
/// windows taking the recording's look: shown over a chat of the check's own, they leave the chat in front with the
/// keystrokes, and a click inside the region reaches the chat; the band and the label are the frame's, the region inside
/// it a hole; the label names the size alone while the region waits and REC with it once it records; the bar offers
/// Record and Cancel while the region waits and the readings, Stop and Cancel once it records, Stop and Cancel standing
/// where they stood while the readings narrow and widen; the frame and the bar are
/// in no grab in either look, which shows what lies beneath them; the bar goes over the region's bottom edge when there
/// is room on neither side, and, wider once it records, is placed again by its rule within the working area; a real
/// click on Record, Stop or Cancel runs its action and leaves the chat in front; a shape Windows refuses for either look
/// is that look's failure with nothing left shown; Saving leaves no button; Close leaves no window. These checks show a
/// chat at (100, 100) with the frame over it, a pattern at (560, 160) with the frame over it, a pattern at the bottom of
/// the primary display's working area under a frame as tall as it, and a frame at the working area's bottom right; they
/// click inside the frame, on Record, Stop and Cancel, type a letter into the chat, and read the screen's pixels there.
/// </summary>
[TestClass]
public sealed class RecordingFrameTests
{
    private const uint GaRoot = 2;

    [TestMethod]
    public async Task OverAChatTheFrameAndTheBarLeaveItInFrontWithTheKeystrokesAndTheRegionClickableWaitingAndRecording()
    {
        using var desk = await FrameDesk.Open();
        var region = DisplayPlaces.Rect(DisplayPlaces.Primary(), 150, 160, 300, 150);

        desk.Show(region);
        var (frame, bar) = await desk.Shown();

        Assert.AreEqual(frame, Under(new PixelPoint(region.X - 1, region.Y + DisplayPlaces.Scaled(DisplayPlaces.Primary(), 70))), "the band on the region's left is the frame's");
        Assert.AreEqual(frame, Under(new PixelPoint(region.X + DisplayPlaces.Scaled(DisplayPlaces.Primary(), 120), region.Y - 2)), "the band on the region's top is the frame's");
        var window = Rectangle(frame);
        Assert.AreEqual(frame, Under(new PixelPoint(window.X + DisplayPlaces.Scaled(DisplayPlaces.Primary(), 6), window.Y + DisplayPlaces.Scaled(DisplayPlaces.Primary(), 6))), "the label, at the frame window's top-left, is the frame's");
        Assert.AreEqual(desk.Chat.Window, Under(new PixelPoint(window.Right - DisplayPlaces.Scaled(DisplayPlaces.Primary(), 6), window.Y + DisplayPlaces.Scaled(DisplayPlaces.Primary(), 6))), "beside the label, above the band, the chat");
        Assert.AreEqual(desk.Chat.Window, Under(region.Center), "inside the region, the chat: the region stays live (G15)");
        desk.Chat.KeepsTheKeystrokes("with the frame and the bar up while the region waits");
        var pressed = desk.Chat.Messages().Count;
        TestMouse.Click(region.Center);
        await Eventually(() => desk.Chat.Messages().Skip(pressed).Contains("WM_LBUTTONDOWN:0x1"));
        Assert.AreEqual(desk.Chat.Window, FrontWindow.GetForegroundWindow(), "a click inside the region is the chat's own");
        // The chat's edit box holds the mouse from its button-down until it has handled the button-up, as a person's
        // click ends before the pointer moves on; a click sent before then would be the chat's too.
        await Eventually(() => desk.Chat.Messages().Skip(pressed).Contains("WM_LBUTTONUP:0x0"));
        Thread.Sleep(300);
        TrayDesktop.Run(() =>
        {
            Assert.AreEqual(RegionSelection.Label(region), desk.Frame.LabelWords, "the size alone, no REC, while the region waits (G15)");
            CollectionAssert.AreEqual(new[] { "Up to 0:30" }, desk.Frame.Bar!.Words.ToList(), "no readings before the recording runs");
            Assert.IsNull(desk.Frame.Bar.StopButton, "no Stop while the region waits");
        });
        Assert.IsGreaterThanOrEqualTo(region.Bottom, Rectangle(bar).Y, "the bar beneath the region");
        foreach (var button in TrayDesktop.Get(() => new[] { desk.Frame.Bar!.RecordButton!, desk.Frame.Bar!.CancelButton! }))
        {
            Assert.AreEqual(bar, Under(TestMouse.Centre(button)), $"{TrayDesktop.Get(() => Avalonia.Automation.AutomationProperties.GetName(button))} lies inside the bar's window, sized by its theme");
        }

        TrayDesktop.Run(() => Assert.AreEqual(RecordingBar.RecordName, Avalonia.Automation.AutomationProperties.GetName(desk.Frame.Bar!.RecordButton!)));
        TestMouse.Click(TestMouse.Centre(TrayDesktop.Get(() => desk.Frame.Bar!.RecordButton!)));
        await Eventually(() => desk.Records == 1);
        desk.Chat.KeepsTheKeystrokes("after a click on Record");
        TestMouse.Click(TestMouse.Centre(TrayDesktop.Get(() => desk.Frame.Bar!.CancelButton!)));
        await Eventually(() => desk.Cancels == 1);
        desk.Chat.KeepsTheKeystrokes("after a click on Cancel while the region waits");
        Assert.AreEqual((1, 0), (desk.Records, desk.Stops), "Cancel ran only its own action");

        desk.Record();
        await Eventually(() => TrayDesktop.Get(() => desk.Frame.Bar!.Words).SequenceEqual(["0:01.6", "/ 0:30", "~614 KB"]));
        TrayDesktop.Run(() =>
        {
            Assert.AreEqual(RecordingFrame.Label(region), desk.Frame.LabelWords, "REC once it records (G15)");
            Assert.IsNull(desk.Frame.Bar!.RecordButton, "no Record once it records");
            Assert.AreEqual(bar, desk.Frame.BarWindow!.TryGetPlatformHandle()!.Handle, "the same bar window, changed (F4)");
            Assert.AreEqual(frame, desk.Frame.FrameWindow!.TryGetPlatformHandle()!.Handle, "the same frame window");
        });
        Thread.Sleep(300);
        Assert.AreEqual(desk.Chat.Window, FrontWindow.GetForegroundWindow(), "the chat still in front: the change took no activation");
        Assert.AreEqual(frame, Under(new PixelPoint(window.X + DisplayPlaces.Scaled(DisplayPlaces.Primary(), 6), window.Y + DisplayPlaces.Scaled(DisplayPlaces.Primary(), 6))), "the label, at the frame window's top-left, is the frame's");
        Assert.AreEqual(desk.Chat.Window, Under(region.Center), "inside the region, the chat, while it records");
        Assert.IsGreaterThanOrEqualTo(region.Bottom, Rectangle(bar).Y, "the bar beneath the region");
        foreach (var button in TrayDesktop.Get(() => new[] { desk.Frame.Bar!.StopButton!, desk.Frame.Bar!.CancelButton! }))
        {
            Assert.AreEqual(bar, Under(TestMouse.Centre(button)), $"{TrayDesktop.Get(() => Avalonia.Automation.AutomationProperties.GetName(button))} lies inside the bar's window, sized again for the wider look");
        }

        TestMouse.Click(TestMouse.Centre(TrayDesktop.Get(() => desk.Frame.Bar!.StopButton!)));
        await Eventually(() => desk.Stops == 1);
        desk.Chat.KeepsTheKeystrokes("after a click on Stop");
        TestMouse.Click(TestMouse.Centre(TrayDesktop.Get(() => desk.Frame.Bar!.CancelButton!)));
        await Eventually(() => desk.Cancels == 2);
        desk.Chat.KeepsTheKeystrokes("after a click on Cancel");
        Assert.AreEqual((1, 1), (desk.Records, desk.Stops), "Cancel ran only its own action");

        TrayDesktop.Run(desk.Frame.Saving);
        Assert.IsEmpty(TrayDesktop.Get(() => desk.Frame.Bar!.GetVisualDescendants().OfType<Button>().ToList()), "no Stop or Cancel while the clip is saved (G35)");
        CollectionAssert.AreEqual(new[] { RecordingBar.SavingWords }, TrayDesktop.Get(() => desk.Frame.Bar!.Words).ToList());
        TrayDesktop.Run(desk.Frame.Close);
        await desk.NoneLeft(frame, bar);
        Assert.IsEmpty(desk.Named);
    }

    [TestMethod]
    public async Task TheBarAsksWindowsForItsOwnRoundedCornersWaitingAndRecordingAndTheFrameKeepsItsSquareOnes()
    {
        using var desk = await FrameDesk.Open();
        desk.Show(DisplayPlaces.Rect(DisplayPlaces.Primary(), 600, 180, 160, 60));
        var (frame, bar) = await desk.Shown();

        Assert.AreEqual(CornerPreference.Round, CornerPreference.Of(bar), "the bar rounded as Windows 11 rounds its own windows while the region waits (F13)");
        Assert.AreEqual(CornerPreference.Default, CornerPreference.Of(frame), "the frame drawn in parts round the region left as it was (the recording area is fine)");
        desk.Record();
        await Task.Delay(300);
        Assert.AreEqual(CornerPreference.Round, CornerPreference.Of(bar), "and once it records");

        TrayDesktop.Run(desk.Frame.Close);
        await desk.NoneLeft(frame, bar);
    }

    [TestMethod]
    public async Task TheFrameAndTheBarAreInNoGrabWaitingOrRecordingWhichShowsWhatLiesBeneathThem()
    {
        using var desk = await FrameDesk.Open(PatternWindow.Place);
        var region = DisplayPlaces.Rect(DisplayPlaces.Primary(), 600, 180, 160, 60);

        desk.Show(region);
        var (frame, bar) = await desk.Shown();

        foreach (var look in new[] { "while the region waits", "once it records" })
        {
            if (look == "once it records")
            {
                desk.Record();
                await Task.Delay(300);
            }

            Assert.AreEqual(frame, Under(new PixelPoint(region.X - 1, region.Y + DisplayPlaces.Scaled(DisplayPlaces.Primary(), 30))), $"the band over the pattern {look}");
            Assert.IsTrue(Rectangle(bar).Intersects(desk.Pattern!.Bounds), $"the bar over the pattern {look}: {Rectangle(bar)}");
            var grab = desk.Screen.Grab(desk.Pattern.Bounds, withPointer: false);
            Assert.IsTrue(grab.Succeeded, grab.Failure?.Sentence);
            var differences = desk.Pattern.Differences(grab.Value!, desk.Pattern.Bounds);
            Assert.IsEmpty(differences, $"the pattern where the band, the label and the bar lie {look} (G15, S4): {string.Join("; ", differences.Take(5))}");
        }

        TrayDesktop.Run(desk.Frame.Close);
        await desk.NoneLeft(frame, bar);
    }

    [TestMethod]
    public async Task ForARegionAsTallAsTheWorkingAreaTheBarIsOverItsBottomEdgeAndInNoGrab()
    {
        var area = TrayDesktop.Get(() => new WindowsScreen().Displays().Single(display => display.IsPrimary).WorkingArea);
        var region = new PixelRect(area.X + DisplayPlaces.Scaled(DisplayPlaces.Primary(), 360), area.Y, DisplayPlaces.Scaled(DisplayPlaces.Primary(), 480), area.Height);
        using var desk = await FrameDesk.Open(new PixelRect(region.X, area.Bottom - DisplayPlaces.Scaled(DisplayPlaces.Primary(), 180), DisplayPlaces.Scaled(DisplayPlaces.Primary(), 480), DisplayPlaces.Scaled(DisplayPlaces.Primary(), 170)));

        desk.Show(region);
        var (frame, bar) = await desk.Shown();

        foreach (var look in new[] { "while the region waits", "once it records" })
        {
            if (look == "once it records")
            {
                desk.Record();
                await Task.Delay(300);
            }

            var placed = Rectangle(bar);
            Assert.IsTrue(placed.X >= region.X && placed.Right <= region.Right && placed.Y > area.Bottom - DisplayPlaces.Scaled(DisplayPlaces.Primary(), 120) && placed.Bottom <= area.Bottom - DisplayPlaces.Scaled(DisplayPlaces.Primary(), 10), $"inside the region over its bottom edge, above the taskbar, {look}: {placed} in {region}");
            Assert.IsTrue(placed.Intersects(desk.Pattern!.Bounds), $"the bar over the pattern {look}: {placed}");
            var grab = desk.Screen.Grab(desk.Pattern.Bounds, withPointer: false);
            Assert.IsTrue(grab.Succeeded, grab.Failure?.Sentence);
            var differences = desk.Pattern.Differences(grab.Value!, desk.Pattern.Bounds);
            Assert.IsEmpty(differences, $"the pattern under the bar and the band {look} (G15, S4): {string.Join("; ", differences.Take(5))}");
        }

        TrayDesktop.Run(desk.Frame.Close);
        await desk.NoneLeft(frame, bar);
    }

    [TestMethod]
    public async Task AtTheWorkingAreasBottomRightTheBarStaysWithinItWaitingAndIsPlacedAgainForItsWiderLookOnceItRecords()
    {
        var area = TrayDesktop.Get(() => new WindowsScreen().Displays().Single(display => display.IsPrimary).WorkingArea);
        // Right edge on the working area's, bottom a few pixels above its bottom: no room below, room above.
        var region = new PixelRect(area.Right - DisplayPlaces.Scaled(DisplayPlaces.Primary(), 240), area.Bottom - DisplayPlaces.Scaled(DisplayPlaces.Primary(), 166), DisplayPlaces.Scaled(DisplayPlaces.Primary(), 240), DisplayPlaces.Scaled(DisplayPlaces.Primary(), 160));
        using var desk = await FrameDesk.Open();

        desk.Show(region);
        var (frame, bar) = await desk.Shown();

        var waiting = Rectangle(bar);
        Assert.IsTrue(Within(waiting, area) && waiting.Bottom <= region.Y, $"the waiting bar above the region, there being no room below, and within the working area (G15): {waiting} in {area}");
        desk.Record();
        await Task.Delay(300);
        var recording = Rectangle(bar);
        Assert.IsGreaterThan(waiting.Width, recording.Width, $"the recording's look is wider: {waiting.Width} then {recording.Width}");
        Assert.IsTrue(Within(recording, area) && recording.Bottom <= region.Y, $"placed again for its new size: still above the region and wholly within the working area, never run past its edge from where the narrower bar stood (G15): {recording} in {area}");
        foreach (var button in TrayDesktop.Get(() => new[] { desk.Frame.Bar!.StopButton!, desk.Frame.Bar!.CancelButton! }))
        {
            var centre = TestMouse.Centre(button);
            Assert.IsTrue(area.Contains(centre) && Under(centre) == bar, $"{TrayDesktop.Get(() => Avalonia.Automation.AutomationProperties.GetName(button))} inside the working area and the bar's window: {centre}");
        }

        TestMouse.Click(TestMouse.Centre(TrayDesktop.Get(() => desk.Frame.Bar!.StopButton!)));
        await Eventually(() => desk.Stops == 1);
        TestMouse.Click(TestMouse.Centre(TrayDesktop.Get(() => desk.Frame.Bar!.CancelButton!)));
        await Eventually(() => desk.Cancels == 1);
        desk.Chat.KeepsTheKeystrokes("after clicks on Stop and Cancel at the working area's edge");
        TrayDesktop.Run(desk.Frame.Close);
        await desk.NoneLeft(frame, bar);
    }

    [TestMethod]
    public async Task AFrameWindowsRefusesItsShapeIsThatShowingsFailureWithNothingLeftShown()
    {
        using var desk = await FrameDesk.Open();
        var primary = desk.Screen.Displays().Single(display => display.IsPrimary);

        var shown = TrayDesktop.Get(() => new RecordingFrame(new ShapeRefused(new WindowsQuietWindows(desk.Tray), from: 1), desk.Screen, desk.Named.Add)
            .Show(new SelectedRegion(primary, DisplayPlaces.Rect(primary, 150, 160, 300, 150)), TimeSpan.FromSeconds(30), () => { }, () => { }, () => { }));

        Assert.AreEqual(new Failure("Record a clip", RecordingFrame.Title, "Windows would not show the frame around the region"), shown.Failure, "a frame shown whole would cover the region (G15)");
        await Eventually(() => FindWindowW(null, RecordingFrame.Title) == 0 && FindWindowW(null, RecordingBar.Title) == 0);
        desk.Chat.KeepsTheKeystrokes("after the refusal");

        var refusedLater = TrayDesktop.Get(() => new RecordingFrame(new ShapeRefused(new WindowsQuietWindows(desk.Tray), from: 2), desk.Screen, desk.Named.Add));
        var waited = TrayDesktop.Get(() => refusedLater.Show(new SelectedRegion(primary, DisplayPlaces.Rect(primary, 150, 160, 300, 150)), TimeSpan.FromSeconds(30), () => { }, () => { }, () => { }));
        Assert.IsTrue(waited.Succeeded, waited.Failure?.Sentence);
        await Eventually(() => FindWindowW(null, RecordingFrame.Title) != 0 && FindWindowW(null, RecordingBar.Title) != 0);
        var recorded = TrayDesktop.Get(() => refusedLater.Recording(new FixedReadings()));

        Assert.AreEqual(new Failure("Record a clip", RecordingFrame.Title, "Windows would not show the frame around the region"), recorded.Failure, "the recording look's shape refused is its failure (G15)");
        Assert.IsNull(TrayDesktop.Get(() => refusedLater.FrameWindow ?? (object?)refusedLater.BarWindow), "both windows let go");
        await Eventually(() => FindWindowW(null, RecordingFrame.Title) == 0 && FindWindowW(null, RecordingBar.Title) == 0);
        desk.Chat.KeepsTheKeystrokes("after the recording look's refusal");
        Assert.IsEmpty(desk.Named);
    }

    [TestMethod]
    public async Task TheBarIsLightOrDarkByTheThemeAndFollowsAChangeWhileItShows()
    {
        using var desk = await FrameDesk.Open();
        try
        {
            TrayDesktop.Run(() => Application.Current!.RequestedThemeVariant = ThemeVariant.Light);
            desk.Show(DisplayPlaces.Rect(DisplayPlaces.Primary(), 150, 160, 300, 150));
            var (frame, bar) = await desk.Shown();
            var looks = new List<(string Theme, (Color Ground, Color Window, IReadOnlyList<Color> Words) Look)> { ("light, waiting", TrayDesktop.Get(desk.BarLook)) };
            TrayDesktop.Run(() => Application.Current!.RequestedThemeVariant = ThemeVariant.Dark);
            looks.Add(("dark, waiting", TrayDesktop.Get(desk.BarLook)));
            desk.Record();
            looks.Add(("dark, recording", TrayDesktop.Get(desk.BarLook)));
            TrayDesktop.Run(() => Application.Current!.RequestedThemeVariant = ThemeVariant.Light);
            looks.Add(("light, recording", TrayDesktop.Get(desk.BarLook)));

            Assert.IsTrue(looks.All(each => Lightness(each.Look.Ground) > 0.5 == each.Theme.StartsWith("light", StringComparison.Ordinal)),
                $"light under the light theme and dark under the dark (F10): {string.Join(", ", looks.Select(each => $"{each.Look.Ground} {each.Theme}"))}");
            foreach (var (theme, look) in looks)
            {
                Assert.AreEqual(look.Ground, look.Window, $"the bar's window is the bar's own colour under the {theme} theme, no sliver of another");
                Assert.IsTrue(look.Words.Count > 0 && look.Words.All(words => Math.Abs(Lightness(words) - Lightness(look.Ground)) >= 0.4),
                    $"every word and Cancel's glyph read against the ground under the {theme} theme: {string.Join(", ", look.Words)} on {look.Ground}");
            }

            TrayDesktop.Run(desk.Frame.Close);
            await desk.NoneLeft(frame, bar);
        }
        finally
        {
            TrayDesktop.Run(() => Application.Current!.RequestedThemeVariant = ThemeVariant.Default);
        }
    }

    [TestMethod]
    [DataRow("Light")]
    [DataRow("Dark")]
    public void HoveredOrPressedRecordAndStopStayRedWithWhiteWordsAndCancelsGlyphReadsOnItsGround(string theme) => TrayDesktop.Run(() =>
    {
        Application.Current!.RequestedThemeVariant = theme == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        var bar = new RecordingBar(TimeSpan.FromSeconds(30), () => { }, () => { }, () => { });
        var window = new Avalonia.Controls.Window { Width = 300, Height = 60, ShowActivated = false, Position = new PixelPoint(-8000, 700), Content = bar };
        window.Show();
        try
        {
            var record = bar.RecordButton!;
            var cancel = bar.CancelButton!;
            bar.Recording();
            foreach (var key in new[] { "ButtonBackgroundPointerOver", "ButtonBackgroundPressed" })
            {
                foreach (var red in new[] { record, bar.StopButton! })
                {
                    var ground = Color(red.Resources[key]);
                    Assert.IsGreaterThan(ground.G * 2, (int)ground.R, $"{key} is a red under the {theme} theme: {ground}");
                    Assert.IsGreaterThanOrEqualTo(0.4, Math.Abs(Lightness(Colors.White) - Lightness(ground)), $"white words read on {ground} under the {theme} theme");
                }

                var words = key.Replace("Background", "Foreground");
                Assert.AreEqual(Colors.White, Color(record.Resources[words]));
                Assert.IsGreaterThanOrEqualTo(0.4, Math.Abs(Lightness(Color(cancel.Resources[words])) - Lightness(Color(cancel.Resources[key]))), $"Cancel's glyph under the {theme} theme");
            }
        }
        finally
        {
            window.Close();
            Application.Current!.RequestedThemeVariant = ThemeVariant.Default;
        }
    });

    [TestMethod]
    [DataRow(30)]
    [DataRow(60)]
    public void StopAndCancelStandWhereTheyStoodWhileTheReadingsNarrowAndWiden(int capSeconds) => TrayDesktop.Run(() => StandStill(capSeconds * 1000));

    private static void StandStill(long cap)
    {
        var bar = new RecordingBar(TimeSpan.FromMilliseconds(cap), () => { }, () => { }, () => { });
        var window = new Avalonia.Controls.Window { ShowActivated = false, Position = new PixelPoint(-8000, 700), Content = bar };
        window.Show();
        try
        {
            bar.Recording();
            // Sized as the frame sizes the bar's window once it records: to the bar measured at its widest readings.
            bar.Measure(Size.Infinity);
            (window.Width, window.Height) = (bar.DesiredSize.Width, bar.DesiredSize.Height);
            Point[]? first = null;
            var readingsInTurn = new[] { new Readings(0, 0), new Readings(cap - 100, 5_452_595), new Readings(12_300, 629_146), new Readings(cap, 10_485_655_143), new Readings(cap - 100, 10_485_760_000), new Readings(1_000, 1_000) };
            foreach (var readings in readingsInTurn)
            {
                bar.Refresh(readings);
                window.UpdateLayout();
                var words = string.Join(" ", bar.Words);
                var now = new[] { bar.StopButton!, bar.CancelButton! }.Select(button => button.TranslatePoint(default, bar)!.Value).ToArray();
                first ??= now;
                Assert.AreEqual(first[0].X, now[0].X, 0.01, $"Stop where it stood, the readings \"{words}\"");
                Assert.AreEqual(first[1].X, now[1].X, 0.01, $"Cancel where it stood, the readings \"{words}\"");
                var readingsEnd = bar.GetVisualDescendants().OfType<TextBlock>().Where(text => text.FindAncestorOfType<Button>() is null)
                    .Max(text => text.TranslatePoint(new Point(text.Bounds.Width, 0), bar)!.Value.X);
                Assert.IsLessThanOrEqualTo(now[0].X, readingsEnd, $"Stop clear of the readings \"{words}\"");
            }
        }
        finally
        {
            window.Close();
        }
    }

    [TestMethod]
    public async Task ThePointerRestingOnRecordAndStopShowsTheThemesRedWithItsWordsReadable()
    {
        using var desk = await FrameDesk.Open();
        desk.Show(DisplayPlaces.Rect(DisplayPlaces.Primary(), 150, 160, 300, 150));
        var (frame, bar) = await desk.Shown();
        void Rests(Func<Button> button, string what)
        {
            var on = TrayDesktop.Get(button);
            TestMouse.MoveTo(TestMouse.Centre(on));
            Thread.Sleep(300);
            TrayDesktop.Run(() =>
            {
                var ground = Color(on.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.ContentPresenter>().First().Background);
                Assert.IsGreaterThan(ground.G * 2, (int)ground.R, $"{what}'s ground hovered: {ground}");
                Assert.AreEqual(Colors.White, Color(on.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.ContentPresenter>().First().Foreground), $"{what}'s words hovered");
            });
        }

        Rests(() => desk.Frame.Bar!.RecordButton!, "Record");
        desk.Record();
        Rests(() => desk.Frame.Bar!.StopButton!, "Stop");
        TrayDesktop.Run(desk.Frame.Close);
        await desk.NoneLeft(frame, bar);
    }

    private static Color Color(object? brush) => brush is ISolidColorBrush solid ? solid.Color : Colors.Transparent;

    private static double Lightness(Color color) => (0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B) / 255;

    private static nint Under(PixelPoint point) => GetAncestor(WindowFromPoint(new ScreenPoint { X = point.X, Y = point.Y }), GaRoot);

    private static PixelRect Rectangle(nint window)
    {
        Assert.IsTrue(GetWindowRect(window, out var rectangle));
        return new PixelRect(rectangle.Left, rectangle.Top, rectangle.Right - rectangle.Left, rectangle.Bottom - rectangle.Top);
    }

    /// <summary>Whether a window at <paramref name="place"/> lies within <paramref name="area"/>, but for the pixel the frame gives a window on its right and bottom rather than fall one short.</summary>
    private static bool Within(PixelRect place, PixelRect area) => place.X >= area.X && place.Y >= area.Y && place.Right <= area.Right + 1 && place.Bottom <= area.Bottom + 1;

    /// <summary>
    /// A chat of the check's own in front at (100, 100), a pattern where a check asks for one, and the live frame over
    /// the real seams, its readings fixed at 1.6 s and 614 KB, its Record, Stop and Cancel counted.
    /// </summary>
    private sealed class FrameDesk : IDisposable
    {
        private readonly IDisposable pointer;
        private readonly WindowsTray tray;
        private int records;
        private int stops;
        private int cancels;

        private FrameDesk(IDisposable pointer, WindowsTray tray, TargetApp chat, PatternWindow? pattern)
        {
            this.pointer = pointer;
            this.tray = tray;
            Chat = chat;
            Pattern = pattern;
            Frame = TrayDesktop.Get(() => new RecordingFrame(new WindowsQuietWindows(tray), Screen, Named.Add));
        }

        public TargetApp Chat { get; }

        public WindowsTray Tray => tray;

        public PatternWindow? Pattern { get; }

        public WindowsScreen Screen { get; } = new();

        public List<Failure> Named { get; } = [];

        public RecordingFrame Frame { get; }

        public int Records => Volatile.Read(ref records);

        public int Stops => Volatile.Read(ref stops);

        public int Cancels => Volatile.Read(ref cancels);

        public static async Task<FrameDesk> Open(PixelRect? pattern = null)
        {
            TrayDesktop.UseProductTheme();
            var pointer = TestMouse.PutBackAfterwards();
            var shown = pattern is { } bounds ? PatternWindow.Show(bounds) : null;
            if (shown is not null)
            {
                await shown.OnScreen(new WindowsScreen());
            }

            var chat = TargetApp.Start("ImageStash check chat", DisplayPlaces.At(DisplayPlaces.Primary(), 100, 100));
            chat.ClickTitleBar();
            return new FrameDesk(pointer, new WindowsTray(), chat, shown);
        }

        /// <summary>Shows the frame around <paramref name="region"/> on the primary display waiting to be recorded, as the session does at the release.</summary>
        public void Show(PixelRect region) => TrayDesktop.Run(() =>
        {
            var primary = Screen.Displays().Single(display => display.IsPrimary);
            var shown = Frame.Show(new SelectedRegion(primary, region), TimeSpan.FromSeconds(30),
                () => Interlocked.Increment(ref records), () => Interlocked.Increment(ref stops), () => Interlocked.Increment(ref cancels));
            Assert.IsTrue(shown.Succeeded, shown.Failure?.Sentence);
        });

        /// <summary>The frame takes its recording look, as the session has it do at Record.</summary>
        public void Record() => TrayDesktop.Run(() =>
        {
            var started = Frame.Recording(new FixedReadings());
            Assert.IsTrue(started.Succeeded, started.Failure?.Sentence);
        });


        /// <summary>The frame's and the bar's handles once both are on the screen, neither of them in front.</summary>
        public async Task<(nint Frame, nint Bar)> Shown()
        {
            var (frame, bar) = TrayDesktop.Get(() => (Frame.FrameWindow!.TryGetPlatformHandle()!.Handle, Frame.BarWindow!.TryGetPlatformHandle()!.Handle));
            await Eventually(() => IsWindowVisible(frame) && IsWindowVisible(bar));
            Thread.Sleep(300);
            Assert.AreEqual(Chat.Window, FrontWindow.GetForegroundWindow(), "the chat still in front: neither window took the activation");
            Assert.AreEqual(RecordingFrame.Title, FrontWindow.NativeText(frame));
            Assert.AreEqual(RecordingBar.Title, FrontWindow.NativeText(bar));
            return (frame, bar);
        }

        /// <summary>
        /// The bar as it is painted now: the ground that shows behind its words (its own, else its window's where its own
        /// is clear), its window's ground, and the colour of each word and glyph drawn on that ground (the red Stop, white
        /// on its own red, aside). UI thread.
        /// </summary>
        public (Color Ground, Color Window, IReadOnlyList<Color> Words) BarLook()
        {
            static Color Of(IBrush? brush) => brush is ISolidColorBrush solid ? solid.Color : Colors.Transparent;
            var bar = Frame.Bar!;
            var window = Of(Frame.BarWindow!.Background);
            var own = Of(bar.Background);
            var words = bar.GetVisualDescendants().OfType<TextBlock>()
                .Where(text => text.FindAncestorOfType<Button>() is not { } button || button == bar.CancelButton)
                .Select(text => Of(text.Foreground)).ToList();
            return (own.A == 0 ? window : own, window, words);
        }

        /// <summary>Neither window left on the desktop, and none of the process titled as either (F4).</summary>
        public async Task NoneLeft(nint frame, nint bar)
        {
            Assert.IsNull(TrayDesktop.Get(() => Frame.FrameWindow));
            Assert.IsNull(TrayDesktop.Get(() => Frame.BarWindow));
            await Eventually(() => !IsWindow(frame) && !IsWindow(bar));
            Assert.AreEqual(nint.Zero, FindWindowW(null, RecordingFrame.Title), "no frame window left");
            Assert.AreEqual(nint.Zero, FindWindowW(null, RecordingBar.Title), "no bar window left");
        }

        public void Dispose()
        {
            // A check that failed with the frame up closes it, so it covers no later check.
            TrayDesktop.Run(Frame.Close);
            Pattern?.Dispose();
            Chat.Dispose();
            tray.Dispose();
            pointer.Dispose();
        }
    }

    /// <summary>The real seam, but Windows refusing a window its shape from the <paramref name="from"/>th asking on.</summary>
    private sealed class ShapeRefused(WindowsQuietWindows real, int from) : IQuietWindows
    {
        private int asked;

        public void ShowWithoutActivating(Avalonia.Controls.Window window) => real.ShowWithoutActivating(window);

        public Outcome ShowOnly(Avalonia.Controls.Window window, IReadOnlyList<PixelRect> parts) =>
            ++asked >= from ? new Failure("Show the window in parts", window.Title ?? "", "Windows would not give it that shape") : real.ShowOnly(window, parts);

        public Outcome<IDisposable> TakeActivation(Avalonia.Controls.Window window) => real.TakeActivation(window);

        public void RoundCorners(Avalonia.Controls.Window window) => real.RoundCorners(window);

        public PixelRect? TrayIconRectangle() => real.TrayIconRectangle();

        public PixelPoint? PointerPosition() => real.PointerPosition();

        public PixelRect? FrontWindowRectangle() => real.FrontWindowRectangle();
    }

    private sealed class FixedReadings : IRecordingReadings
    {
        public long ElapsedMs => 1_650;

        public long BytesWritten => 629_146;
    }

    private sealed record Readings(long ElapsedMs, long BytesWritten) : IRecordingReadings;

    [StructLayout(LayoutKind.Sequential)]
    private struct ScreenPoint
    {
        public int X, Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint WindowFromPoint(ScreenPoint point);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint GetAncestor(nint window, uint flags);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, out NativeRect rect);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint window);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern nint FindWindowW(string? className, string windowName);
}
