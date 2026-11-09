using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ImageStash.Windows.Seams;
using ImageStash.Windows.Tests.Support;

namespace ImageStash.Windows.Tests.Seams;

/// <summary>
/// The real notification area and pointer supply fresh physical placement hints, or no hint; a quiet window is
/// activated by a field's take and by nothing else, whichever window is in front (G16); a quiet window shown only in
/// parts is there only in them, and what lies beneath takes the rest (G15).
/// </summary>
[TestClass]
public sealed class WindowsQuietWindowsTests
{
    /// <summary>Templated, so a click finds the field or the button it is aimed at, whichever check ran first.</summary>
    [TestInitialize]
    public void UseProductTheme() => TrayDesktop.UseProductTheme();

    [TestMethod]
    public Task AShownTrayHasAPositiveRectangleInsideTheVirtualDesktop() => TrayDesktop.WithShownTray(tray =>
    {
        var rectangle = new WindowsQuietWindows(tray).TrayIconRectangle();

        Assert.IsNotNull(rectangle);
        Assert.IsTrue(rectangle.Value.Width > 0 && rectangle.Value.Height > 0);
        var desktop = TrayDesktop.VirtualDesktop;
        Assert.IsTrue(desktop.Width > 0 && desktop.Height > 0);
        Assert.IsTrue(desktop.Contains(rectangle.Value), $"Tray {rectangle} lies outside virtual desktop {desktop}.");
    });

    [TestMethod]
    public void ThePointerIsSomewhereOnTheVirtualDesktop() => TrayDesktop.Run(() =>
    {
        using var tray = new WindowsTray();

        var pointer = new WindowsQuietWindows(tray).PointerPosition();

        Assert.IsNotNull(pointer);
        var desktop = TrayDesktop.VirtualDesktop;
        Assert.IsTrue(new PixelRect(desktop.X, desktop.Y, desktop.Width + 1, desktop.Height + 1).Contains(pointer.Value), $"Pointer {pointer} lies outside virtual desktop {desktop}.");
    });

    [TestMethod]
    public void TheWindowInFrontIsReadWithItsRectangle()
    {
        using var pointer = TestMouse.PutBackAfterwards();
        using var tray = new WindowsTray();
        using var other = TargetApp.Start("ImageStash check in front", DisplayPlaces.At(DisplayPlaces.Primary(), 100, 100));
        TestMouse.Click(DisplayPlaces.At(DisplayPlaces.Primary(), 300, 112));
        FrontWindow.WaitUntilInFront(other.Window);

        Assert.AreEqual(DisplayPlaces.Rect(DisplayPlaces.Primary(), 100, 100, 400, 260), new WindowsQuietWindows(tray).FrontWindowRectangle(), "where the window in front lies, so the active display is known (G1)");
    }

    [TestMethod]
    public async Task AClickThatTakesTheActivationBringsTheWindowInFrontWithTheKeystrokesAndItsLastHoldGivesThemBack()
    {
        using var pointer = TestMouse.PutBackAfterwards();
        using var tray = new WindowsTray();
        using var chat = TargetApp.Start("ImageStash check chat", DisplayPlaces.At(DisplayPlaces.Primary(), 100, 100));
        using var other = TargetApp.Start("ImageStash check other", DisplayPlaces.At(DisplayPlaces.Primary(), 100, 420));
        var quiet = new WindowsQuietWindows(tray);
        var takes = new List<Outcome<IDisposable>>();
        var (window, box) = TrayDesktop.Get(() =>
        {
            var box = new TextBox();
            var window = new Avalonia.Controls.Window
            {
                Content = box,
                Width = 300,
                Height = 80,
                Position = DisplayPlaces.At(DisplayPlaces.Primary(), 700, 100),
                WindowDecorations = WindowDecorations.None,
                ShowInTaskbar = false,
                Topmost = true,
            };
            // As a field's click takes it: from the user's own click, which lets ImageStash set the foreground.
            box.AddHandler(InputElement.PointerPressedEvent, (_, _) => takes.Add(quiet.TakeActivation(window)), RoutingStrategies.Tunnel);
            quiet.ShowWithoutActivating(window);
            return (window, box);
        });
        try
        {
            var handle = TrayDesktop.Get(() => window.TryGetPlatformHandle()!.Handle);
            var centre = TrayDesktop.Get(() => box.PointToScreen(new Point(box.Bounds.Width / 2, box.Bounds.Height / 2)));
            chat.ClickTitleBar();

            TestMouse.Click(centre);
            FrontWindow.WaitUntilInFront(handle);
            TestHotKeys.Tap('A');
            await ShownWindow.Eventually(() => TrayDesktop.Get(() => box.Text) == "a");
            Assert.IsFalse(chat.Messages().Contains("WM_KEYDOWN:0x41"), "the letter went to the window that took the activation");

            TestMouse.Click(centre);
            Thread.Sleep(300);
            var holds = TrayDesktop.Get(() => takes.Select(taken => taken.Value!).ToList());
            Assert.HasCount(2, holds);
            Assert.IsTrue(TrayDesktop.Get(() => takes.All(taken => taken.Succeeded)));
            TrayDesktop.Run(holds[0].Dispose);
            Thread.Sleep(300);
            Assert.AreEqual(handle, FrontWindow.GetForegroundWindow(), "a hold remains, so it keeps the activation");
            TrayDesktop.Run(holds[1].Dispose);
            FrontWindow.WaitUntilInFront(chat.Window);
            TrayDesktop.Run(holds[1].Dispose);
            var typedBefore = chat.Messages().Count;
            TestHotKeys.Tap('B');
            await ShownWindow.Eventually(() => chat.Messages().Skip(typedBefore).Contains("WM_KEYDOWN:0x42"));

            TestMouse.Click(centre);
            FrontWindow.WaitUntilInFront(handle);
            other.ClickTitleBar();
            TrayDesktop.Run(TrayDesktop.Get(() => takes[2].Value!).Dispose);
            Thread.Sleep(500);
            Assert.AreEqual(other.Window, FrontWindow.GetForegroundWindow(), "the window the user put in front keeps it: nothing is moved");
        }
        finally
        {
            TrayDesktop.Run(window.Close);
        }
    }

    [TestMethod]
    public async Task AClickOnAQuietWindowWithAWindowOfTheSameThreadInFrontMovesNoActivationAndTheTargetStillAnswersTheAppBeforeIt()
    {
        using var pointer = TestMouse.PutBackAfterwards();
        using var tray = new WindowsTray();
        using var targets = new WindowsTarget();
        using var chat = TargetApp.Start("ImageStash check chat", DisplayPlaces.At(DisplayPlaces.Primary(), 100, 100));
        // ImageStash's own window in front, on the UI thread the quiet window runs on, as the stash window is.
        using var own = FrontWindow.Show(DisplayPlaces.At(DisplayPlaces.Primary(), 100, 400));
        var quiet = new WindowsQuietWindows(tray);
        var takes = new List<Outcome<IDisposable>>();
        var (clicks, activations, deactivations) = (0, 0, 0);
        var (window, button, box) = TrayDesktop.Get(() =>
        {
            var button = new Button { Content = "Undo", Focusable = false };
            var box = new TextBox();
            var window = QuietWindow(new StackPanel { Children = { button, box } });
            button.Click += (_, _) => clicks++;
            window.Activated += (_, _) => activations++;
            box.AddHandler(InputElement.PointerPressedEvent, (_, _) => takes.Add(quiet.TakeActivation(window)), RoutingStrategies.Tunnel);
            quiet.ShowWithoutActivating(window);
            return (window, button, box);
        });
        try
        {
            var handle = TrayDesktop.Get(() => window.TryGetPlatformHandle()!.Handle);
            chat.ClickTitleBar();
            own.ClickToFront();
            await ShownWindow.Eventually(() => targets.LastBeforeMine()?.Handle == chat.Window);
            TrayDesktop.Run(() => own.Window.Deactivated += (_, _) => deactivations++);

            var at = TestMouse.Centre(button);
            Assert.AreEqual(0, FrontWindow.ElsewhereInFront(own.Handle, () => TestMouse.Click(at), 500), "the own window in front throughout the click (G16)");
            TrayDesktop.Run(() =>
            {
                Assert.AreEqual(1, clicks, "the click reached the button");
                Assert.AreEqual(0, deactivations, "the own window was never deactivated, so its hide rule has nothing to judge (G2)");
                Assert.AreEqual(0, activations, "the quiet window was never activated, not even within its thread");
                Assert.IsFalse(window.IsActive);
                Assert.IsTrue(own.Window.IsActive);
            });
            Assert.AreEqual(own.Handle, FrontWindow.ActiveHandle(), "the thread's active window unchanged");
            Assert.AreEqual(own.Handle, FrontWindow.FocusedHandle(), "and its focus");
            Assert.AreEqual(chat.Window, targets.LastBeforeMine()?.Handle, "the window before ImageStash's is still the app it came from (G8)");
            TestHotKeys.Tap('R');
            await ShownWindow.Eventually(() => own.Keys().Contains(Key.R));

            TestMouse.Click(TestMouse.Centre(box));
            FrontWindow.WaitUntilInFront(handle);
            TestHotKeys.Tap('A');
            await ShownWindow.Eventually(() => TrayDesktop.Get(() => box.Text) == "a");
            var hold = TrayDesktop.Get(() => takes.Single().Value!);
            TrayDesktop.Run(() => Assert.AreEqual(1, activations, "the field's take is the one activation"));
            TrayDesktop.Run(hold.Dispose);
            FrontWindow.WaitUntilInFront(own.Handle);
            await ShownWindow.Eventually(() => TrayDesktop.Get(() => own.Window.IsActive));
        }
        finally
        {
            TrayDesktop.Run(window.Close);
        }
    }

    [TestMethod]
    public void AQuietWindowWithoutAHoldIsActivatedAndFocusedByNoOtherPathAndAHoldLetsItThrough()
    {
        using var pointer = TestMouse.PutBackAfterwards();
        using var tray = new WindowsTray();
        using var own = FrontWindow.Show(DisplayPlaces.At(DisplayPlaces.Primary(), 100, 400));
        var quiet = new WindowsQuietWindows(tray);
        var (activations, deactivations) = (0, 0);
        var window = TrayDesktop.Get(() =>
        {
            var window = QuietWindow(new TextBox());
            window.Activated += (_, _) => activations++;
            quiet.ShowWithoutActivating(window);
            return window;
        });
        try
        {
            var handle = TrayDesktop.Get(() => window.TryGetPlatformHandle()!.Handle);
            own.ClickToFront();
            TrayDesktop.Run(() => own.Window.Deactivated += (_, _) => deactivations++);

            // From the UI thread both windows run on: through the foreground, as Avalonia's Activate asks, then through the focus.
            TrayDesktop.Run(window.Activate);
            Thread.Sleep(300);
            Assert.AreEqual(own.Handle, FrontWindow.GetForegroundWindow(), "an activation without a hold is refused (G16)");
            TrayDesktop.Run(() => SetFocus(handle));
            Thread.Sleep(300);
            Assert.AreEqual(own.Handle, FrontWindow.GetForegroundWindow(), "and so is the focus");
            Assert.AreEqual(own.Handle, FrontWindow.ActiveHandle());
            Assert.AreEqual(own.Handle, FrontWindow.FocusedHandle());
            TrayDesktop.Run(() =>
            {
                Assert.AreEqual(0, activations, "the quiet window was never activated");
                Assert.AreEqual(0, deactivations, "the own window was never deactivated");
                Assert.IsTrue(own.Window.IsActive && !window.IsActive);
            });

            var taken = TrayDesktop.Get(() => quiet.TakeActivation(window));
            Assert.IsTrue(taken.Succeeded, "a take brings it in front");
            FrontWindow.WaitUntilInFront(handle);
            Assert.AreEqual(handle, FrontWindow.FocusedHandle(), "with the keystrokes");
            TrayDesktop.Run(() => Assert.IsTrue(window.IsActive));
            TrayDesktop.Run(taken.Value!.Dispose);
            FrontWindow.WaitUntilInFront(own.Handle);
        }
        finally
        {
            TrayDesktop.Run(window.Close);
        }
    }

    [TestMethod]
    public void AWindowShownOnlyInPartsIsHitInThemAndWhatLiesBeneathIsHitElsewhere()
    {
        using var pointer = TestMouse.PutBackAfterwards();
        using var tray = new WindowsTray();
        using var beneath = TargetApp.Start("ImageStash check beneath", DisplayPlaces.At(DisplayPlaces.Primary(), 600, 50), editBox: false);
        beneath.ClickTitleBar();
        var quiet = new WindowsQuietWindows(tray);
        var window = TrayDesktop.Get(() =>
        {
            var window = QuietWindow(new Border { Background = Avalonia.Media.Brushes.Red });
            quiet.ShowWithoutActivating(window);
            return window;
        });
        try
        {
            var handle = TrayDesktop.Get(() => window.TryGetPlatformHandle()!.Handle);

            var shaped = TrayDesktop.Get(() => quiet.ShowOnly(window, [DisplayPlaces.Rect(DisplayPlaces.Primary(), 0, 0, 40, 20), DisplayPlaces.Rect(DisplayPlaces.Primary(), 100, 50, 30, 30)]));

            Assert.IsTrue(shaped.Succeeded, shaped.Failure?.Sentence);
            Thread.Sleep(300);
            Assert.AreEqual(handle, Under(DisplayPlaces.At(DisplayPlaces.Primary(), 710, 110)), "a point in the first part");
            Assert.AreEqual(handle, Under(DisplayPlaces.At(DisplayPlaces.Primary(), 815, 165)), "a point in the second part");
            Assert.AreEqual(beneath.Window, Under(DisplayPlaces.At(DisplayPlaces.Primary(), 900, 140)), "a point outside the parts: the window beneath (G15)");
            Assert.AreEqual(beneath.Window, Under(DisplayPlaces.At(DisplayPlaces.Primary(), 745, 110)), "just beside the first part");
        }
        finally
        {
            TrayDesktop.Run(window.Close);
        }
    }

    [TestMethod]
    public void ATrayNeverShownHasNoRectangle() => TrayDesktop.Run(() =>
    {
        using var tray = new WindowsTray();

        Assert.IsNull(new WindowsQuietWindows(tray).TrayIconRectangle());
    });

    [TestMethod]
    public Task ADisposedTrayHasNoRectangle() => TrayDesktop.WithShownTray(tray =>
    {
        tray.Dispose();

        Assert.IsNull(new WindowsQuietWindows(tray).TrayIconRectangle());
    });

    [TestMethod]
    public Task ASecondReadAfterDisposalDoesNotReturnTheFirstRectangle() => TrayDesktop.WithShownTray(tray =>
    {
        var windows = new WindowsQuietWindows(tray);
        Assert.IsNotNull(windows.TrayIconRectangle());

        tray.Dispose();

        Assert.IsNull(windows.TrayIconRectangle());
    });

    [TestMethod]
    public void AWindowWhoseCornersWereRoundedAsksWindowsForItsOwnRoundingAndOneNotAskedLeavesItToWindows()
    {
        using var tray = new WindowsTray();
        var quiet = new WindowsQuietWindows(tray);
        var (rounded, square) = TrayDesktop.Get(() =>
        {
            var rounded = QuietWindow(new Border { Background = Avalonia.Media.Brushes.Red });
            quiet.RoundCorners(rounded);
            quiet.ShowWithoutActivating(rounded);
            var square = QuietWindow(new Border { Background = Avalonia.Media.Brushes.Red });
            square.Position = DisplayPlaces.At(DisplayPlaces.Primary(), 700, 260);
            quiet.ShowWithoutActivating(square);
            return (rounded, square);
        });
        try
        {
            // Read back from Windows, which then draws it rounded where it rounds windows at all: this console, without
            // a GPU, rounds none, a framed window's included (probed).
            Assert.AreEqual(CornerPreference.Round, CornerPreference.Of(TrayDesktop.Get(() => rounded.TryGetPlatformHandle()!.Handle)), "Windows 11's own rounding asked for (F13)");
            Assert.AreEqual(CornerPreference.Default, CornerPreference.Of(TrayDesktop.Get(() => square.TryGetPlatformHandle()!.Handle)), "a window not asked is left to Windows, which leaves a borderless one square");
        }
        finally
        {
            TrayDesktop.Run(() =>
            {
                rounded.Close();
                square.Close();
            });
        }
    }

    /// <summary>A small topmost window without decorations or a taskbar button at (700, 100), as a notification is made.</summary>
    private static Avalonia.Controls.Window QuietWindow(Control content) => new()
    {
        Content = content,
        Width = 300,
        Height = 120,
        Position = DisplayPlaces.At(DisplayPlaces.Primary(), 700, 100),
        WindowDecorations = WindowDecorations.None,
        ShowInTaskbar = false,
        Topmost = true,
    };

    /// <summary>The top-level window Windows finds under <paramref name="point"/> on the desktop.</summary>
    private static nint Under(PixelPoint point) => GetAncestor(WindowFromPoint(new ScreenPoint { X = point.X, Y = point.Y }), GaRoot);

    private const uint GaRoot = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct ScreenPoint
    {
        public int X, Y;
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint SetFocus(nint window);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint WindowFromPoint(ScreenPoint point);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint GetAncestor(nint window, uint flags);
}
