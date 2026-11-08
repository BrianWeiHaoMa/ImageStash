using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ImageStash.Files;
using ImageStash.Seams;
using ImageStash.Store;
using ImageStash.Window;
using ImageStash.Windows.Seams;
using ImageStash.Windows.Tests.Support;
using static ImageStash.Windows.Tests.Support.ShownSettings;

namespace ImageStash.Windows.Tests.Window;

/// <summary>
/// The Settings page on the desktop: one window titled "ImageStash settings" whose controls carry G32's row names in
/// order, opened centred and wholly inside the primary display's working area (E1), one at a time, brought forward when
/// asked again, closed by a real Esc, and then made afresh (F4); at its smallest size every control, heading and warning
/// lies whole in view or is scrolled to, no word cut (L2, E1); its controls change the settings in effect, a refused
/// act is drawn under its row, and the start's read failure and a refused write at the top (G35); a screenshot started
/// while it shows leaves it showing (G3); the open shortcut pressed over it summons and presses none of its buttons.
/// </summary>
[TestClass]
public sealed class SettingsWindowTests
{
    private const ushort VkEscape = 0x1B;

    [TestMethod]
    public async Task SettingsChosenInTheRealTrayMenuBringsTheOnePageBehindAnotherWindowToTheFront()
    {
        using var settings = new SettingsFolder();
        using var pointer = TestMouse.PutBackAfterwards();
        TrayDesktop.UseProductTheme();
        SettingsWindow? open = null;
        var tray = new WindowsTray();
        // Beside the page, centred on a display of any size, so the click that brings it in front lands on it.
        using var other = FrontWindow.Show(new PixelPoint(0, 400));
        try
        {
            TrayDesktop.Run(() => tray.Show([new TrayMenuEntry("Settings", () => open = SettingsWindow.Open(open, () => settings.Page()), MenuClosed: () => open?.KeepForward()), new TrayMenuEntry("Quit ImageStash", () => { })], "ImageStash menu's Settings check", () => { }));
            // Explorer registers the icon while the UI thread keeps pumping.
            await Task.Delay(1000);
            other.ClickToFront();
            TrayDesktop.ChooseFromRealMenu(tray, 0, 2);
            await ShownWindow.Eventually(() => TrayDesktop.Get(() => open is { IsActive: true }));
            // Past the step that follows the menu's closing, which would answer the next click's window by coming forward again.
            await Task.Delay(300);
            var first = open!;
            var handle = TrayDesktop.Get(() => first.TryGetPlatformHandle()!.Handle);
            var before = ProcessWindows.Visible();

            other.ClickToFront();
            TrayDesktop.ChooseFromRealMenu(tray, 0, 2);
            FrontWindow.WaitUntilInFront(handle);
            await Task.Delay(300);

            Assert.AreEqual(handle, FrontWindow.GetForegroundWindow(), "the page stays in front once the menu has gone (G32)");
            Assert.AreSame(first, open, "asked again, the open page (F4)");
            Assert.AreEqual(before, ProcessWindows.Visible(), "no second page (F4)");
        }
        finally
        {
            TrayDesktop.Run(() =>
            {
                open?.Close();
                tray.Dispose();
            });
        }
    }

    [TestMethod]
    public async Task TheButtonOpensThePageThenBringsItForwardTheFlyoutStayingUntilThePageLosesFocusToAnotherApp()
    {
        using var settings = new SettingsFolder();
        using var pointer = TestMouse.PutBackAfterwards();
        using var shown = ShownWindow.Open();
        await shown.OpenStash();
        using var other = FrontWindow.Show(new PixelPoint(100, 400));
        SettingsWindow? open = null;
        var flyout = shown.Window;
        TrayDesktop.Run(() =>
        {
            // As the app wires them.
            flyout.SettingsRequested += () =>
            {
                var was = open;
                open = SettingsWindow.Open(was, () => settings.Page());
                if (!ReferenceEquals(was, open))
                {
                    flyout.Follow(open);
                }
            };
            flyout.Hide();
            flyout.ShowActivated = true;
            flyout.SummonFromTray();
        });
        var handle = TrayDesktop.Get(() => flyout.TryGetPlatformHandle()!.Handle);
        FrontWindow.WaitUntilInFront(handle);
        try
        {
            TestMouse.Click(TestMouse.Centre(TrayDesktop.Get(() => shown.SettingsButton)));
            await ShownWindow.Eventually(() => TrayDesktop.Get(() => open is { IsActive: true } page && InsideWorkingArea(page)));
            var first = open!;
            await Task.Delay(500);
            TrayDesktop.Run(() => Assert.IsTrue(flyout.IsVisible, "focus went to ImageStash's own page, not another app (G2)"));

            TestMouse.Click(TestMouse.Centre(TrayDesktop.Get(() => shown.SettingsButton)));
            await ShownWindow.Eventually(() => FrontWindow.GetForegroundWindow() == TrayDesktop.Get(() => first.TryGetPlatformHandle()!.Handle));
            await Task.Delay(300);
            Assert.AreSame(first, open, "asked again, the open page, brought forward (G32, F4)");

            other.ClickToFront();
            await ShownWindow.Eventually(() => TrayDesktop.Get(() => !flyout.IsVisible));
        }
        finally
        {
            TrayDesktop.Run(() => open?.Close());
        }
    }

    [TestMethod]
    public async Task OnePageAtATimeTitledWithItsRowsInOrderCentredInsideTheWorkingAreaAndClosedByEsc()
    {
        using var settings = new SettingsFolder();
        using var pointer = TestMouse.PutBackAfterwards();
        TrayDesktop.UseProductTheme();
        SettingsWindow? open = null;
        var before = ProcessWindows.Visible();
        try
        {
            var first = open = TrayDesktop.Get(() => SettingsWindow.Open(open, () => settings.Page()));
            await ShownWindow.Eventually(() => TrayDesktop.Get(() => first.IsVisible && first.FrameSize is not null && InsideWorkingArea(first)));
            // The page places itself once more, its frame then known, at this priority (SettingsWindow.ShowCentred): on a
            // working area taller than the page, it is inside it before then, centred as if it had no frame.
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

            TrayDesktop.Run(() =>
            {
                Assert.AreEqual("ImageStash settings", first.Title);
                CollectionAssert.AreEqual(Titles, Named(first), "each row's control named by its title, in G32's order");
                CollectionAssert.DoesNotContain(first.GetVisualDescendants().OfType<Control>().Select(AutomationProperties.GetName).ToList(), "Look", "one look: no row chooses a take of it (G32)");
                var area = first.Screens.Primary!.WorkingArea;
                var outer = CaptureDesk.Outer(first);
                Assert.IsLessThanOrEqualTo(2, Math.Abs(outer.Center.X - area.Center.X), $"centred across the primary display: {outer} in {area}");
            });
            Assert.AreEqual(before + 1, ProcessWindows.Visible(), "one window");

            TestMouse.Click(TestMouse.Centre(TrayDesktop.Get(() => Heading(first, "Recording"))));
            await ShownWindow.Eventually(() => TrayDesktop.Get(() => first.IsActive));
            TrayDesktop.Run(() => first.WindowState = WindowState.Minimized);
            var second = open = TrayDesktop.Get(() => SettingsWindow.Open(open, () => settings.Page()));

            Assert.AreSame(first, second, "asked again, the open page (F4)");
            await ShownWindow.Eventually(() => TrayDesktop.Get(() => first.WindowState == WindowState.Normal && first.IsActive));
            Assert.AreEqual(before + 1, ProcessWindows.Visible(), "brought forward, no second window (F4)");

            var lengths = TrayDesktop.Get(() => (ComboBox)Control(first, SettingsPage.MaximumLengthTitle));
            TestMouse.Click(TestMouse.Centre(lengths));
            await ShownWindow.Eventually(() => TrayDesktop.Get(() => lengths.IsDropDownOpen));
            TestHotKeys.Tap(VkEscape);
            await ShownWindow.Eventually(() => TrayDesktop.Get(() => !lengths.IsDropDownOpen));
            await Task.Delay(300);
            Assert.IsFalse(TrayDesktop.Get(() => first.HasClosed), "Esc with the list open closes the list alone: one thing at a time, as in the stash window (G2)");

            TestHotKeys.Tap(VkEscape);
            await ShownWindow.Eventually(() => TrayDesktop.Get(() => first.HasClosed));
            Assert.AreEqual(before, ProcessWindows.Visible(), "Esc closed it");

            var third = open = TrayDesktop.Get(() => SettingsWindow.Open(open, () => settings.Page()));
            Assert.AreNotSame(first, third, "after Esc, a fresh page");
            await ShownWindow.Eventually(() => TrayDesktop.Get(() => third.IsVisible && InsideWorkingArea(third)));
        }
        finally
        {
            TrayDesktop.Run(() => open?.Close());
        }
    }

    [TestMethod]
    public async Task AtItsSmallestEveryControlHeadingAndWarningLiesWholeInsideThePageOrIsScrolledToWithNoWordCut()
    {
        // A file that cannot be read, so the start's failure stands at the top beside a refused write.
        using var settings = new SettingsFolder("""{"version":1,"theme":""");
        TrayDesktop.UseProductTheme();
        var window = TrayDesktop.Get(() => SettingsWindow.Open(null, () => settings.Page()));
        try
        {
            await ShownWindow.Eventually(() => TrayDesktop.Get(() => window.IsVisible && window.FrameSize is not null && InsideWorkingArea(window)));
            var heldWrite = new Failure("Save settings", Store.Settings.FileName, HeldFileRetry.HeldReason);
            var refusedShortcut = TrayDesktop.Get(() =>
            {
                settings.Run.Refusal = new Failure("Stop starting with Windows", "Run entry ImageStash", "Windows refused the change");
                Control(window, SettingsPage.StartWithWindowsTitle).SetValue(ToggleSwitch.IsCheckedProperty, false);
                var field = window.Page.ShortcutField(SettingsPage.StashClipboardImageTitle)!;
                field.Begin();
                field.Press(Key.V, KeyModifiers.Control);
                // Last, since the page's next change takes it away.
                window.Page.WriteFailed(heldWrite);
                window.Width = 1;
                window.Height = 1;
                return field.Warning!.Sentence;
            });
            var minimum = new Size(SettingsWindow.MinimumSize.Width, SettingsWindow.MinimumSize.Height);
            await ShownWindow.Eventually(() => TrayDesktop.Get(() => window.ClientSize == minimum));

            TrayDesktop.Run(() =>
            {
                Assert.AreEqual(minimum, window.ClientSize, "the page at its smallest (L2)");
                var viewer = (ScrollViewer)window.Content!;
                Assert.AreEqual(new Rect(minimum), new Rect(viewer.TranslatePoint(default, window)!.Value, viewer.Bounds.Size), "the scroller fills the page");
                StringAssert.StartsWith(Top(window), "Read settings failed: settings.json, ");
                Assert.AreEqual("Stop starting with Windows failed: Run entry ImageStash, Windows refused the change", WarningUnder(window, SettingsPage.StartWithWindowsTitle));
                Assert.AreEqual("Register the Stash clipboard image shortcut failed: Ctrl+V, it is the paste keystroke the handoff sends", refusedShortcut);
                Assert.AreEqual(refusedShortcut, WarningUnder(window, SettingsPage.StashClipboardImageTitle), "every kind of warning the page draws, showing at once");
            });
            var parts = TrayDesktop.Get(() => window.GetVisualDescendants().OfType<Control>()
                .Where(part => part.IsEffectivelyVisible && (Titles.Contains(AutomationProperties.GetName(part)) || part is TextBlock { Text.Length: > 0 } || part is TextPresenter { Text.Length: > 0 }))
                .ToList());
            TrayDesktop.Run(() =>
            {
                CollectionAssert.IsSubsetOf(Titles, parts.Select(AutomationProperties.GetName).ToList(), "every row's control shows");
                var words = parts.OfType<TextBlock>().Select(text => text.Text).ToList();
                CollectionAssert.IsSubsetOf(window.Page.Rows.Select(row => row.Group).Distinct().ToList(), words, "every group's heading shows");
                CollectionAssert.IsSubsetOf(new[] { refusedShortcut, "Stop starting with Windows failed: Run entry ImageStash, Windows refused the change", heldWrite.Sentence }, words, "every warning shows");
                Assert.IsTrue(words.Any(each => each!.StartsWith("Read settings failed: settings.json, ", StringComparison.Ordinal)), "the read failure shows");
            });
            foreach (var part in parts)
            {
                TrayDesktop.Run(part.BringIntoView);
                TrayDesktop.Run(window.UpdateLayout);
                TrayDesktop.Run(() => WholeInView(window, part));
            }
        }
        finally
        {
            TrayDesktop.Run(window.Close);
        }
    }

    [TestMethod]
    public async Task ItsControlsChangeTheSettingsInEffectAndARefusedActIsDrawnUnderItsRow()
    {
        using var settings = new SettingsFolder();
        TrayDesktop.UseProductTheme();
        var window = TrayDesktop.Get(() => SettingsWindow.Open(null, () => settings.Page()));
        try
        {
            await ShownWindow.Eventually(() => TrayDesktop.Get(() => window.IsVisible));
            TrayDesktop.Run(() =>
            {
                Control(window, SettingsPage.AlsoCopyTitle).SetValue(ToggleSwitch.IsCheckedProperty, false);
                var lengths = (ComboBox)Control(window, SettingsPage.MaximumLengthTitle);
                CollectionAssert.AreEqual(new[] { "10 seconds", "15 seconds", "30 seconds", "60 seconds" }, lengths.Items.OfType<ComboBoxItem>().Select(item => item.Content).ToList());
                Assert.AreEqual("30 seconds", ((ComboBoxItem)lengths.SelectedItem!).Content, "G32's default shown");
                lengths.SelectedIndex = 3;
                Control(window, SettingsPage.ShowPointerTitle).SetValue(ToggleSwitch.IsCheckedProperty, false);
                Assert.AreEqual((false, 60, false), (settings.Keeper.Current.AlsoCopyCapture, settings.Keeper.Current.RecordingMaxSeconds, settings.Keeper.Current.RecordPointer), "each in effect at once (G32)");
                Assert.AreEqual(settings.DefaultStash, ((TextBox)Control(window, SettingsPage.StashFolderTitle)).Text, "the folder in use");

                ShownWindow.RaiseClick((Button)Control(window, "Open folder"));
                CollectionAssert.AreEqual(new[] { settings.DefaultStash }, settings.Dialogs.OpenedFolders);
                Assert.IsNull(WarningUnder(window, SettingsPage.StashFolderTitle));

                settings.Run.Refusal = new Failure("Stop starting with Windows", "Run entry ImageStash", "Windows refused the change");
                Control(window, SettingsPage.StartWithWindowsTitle).SetValue(ToggleSwitch.IsCheckedProperty, false);
                Assert.AreEqual("Stop starting with Windows failed: Run entry ImageStash, Windows refused the change", WarningUnder(window, SettingsPage.StartWithWindowsTitle), "drawn under its row (G35)");
                Assert.IsFalse(((ToggleSwitch)Control(window, SettingsPage.StartWithWindowsTitle)).IsChecked, "the choice shown as recorded");
                settings.Run.Refusal = null;
                Control(window, SettingsPage.StartWithWindowsTitle).SetValue(ToggleSwitch.IsCheckedProperty, true);
                Assert.IsNull(WarningUnder(window, SettingsPage.StartWithWindowsTitle), "gone with the row's next success");
                CollectionAssert.AreEqual(new[] { "clear", $"set {SettingsFolder.Executable}" }, settings.Run.Calls);
            });
        }
        finally
        {
            TrayDesktop.Run(window.Close);
        }
    }

    [TestMethod]
    public async Task ALengthTheListLacksIsShownAndLeavesTheListOnceAnotherIsPicked()
    {
        using var settings = new SettingsFolder("""{"version":1,"recordingMaxSeconds":45}""");
        TrayDesktop.UseProductTheme();
        var window = TrayDesktop.Get(() => SettingsWindow.Open(null, () => settings.Page()));
        try
        {
            await ShownWindow.Eventually(() => TrayDesktop.Get(() => window.IsVisible));
            TrayDesktop.Run(() =>
            {
                var lengths = (ComboBox)Control(window, SettingsPage.MaximumLengthTitle);
                CollectionAssert.AreEqual(new[] { "10 seconds", "15 seconds", "30 seconds", "45 seconds", "60 seconds" }, lengths.Items.OfType<ComboBoxItem>().Select(item => item.Content).ToList(), "the file's own length in its place (G31)");
                Assert.AreEqual("45 seconds", ((ComboBoxItem)lengths.SelectedItem!).Content);

                lengths.SelectedIndex = 2;

                CollectionAssert.AreEqual(new[] { "10 seconds", "15 seconds", "30 seconds", "60 seconds" }, lengths.Items.OfType<ComboBoxItem>().Select(item => item.Content).ToList());
                Assert.AreEqual("30 seconds", ((ComboBoxItem)lengths.SelectedItem!).Content, "the pick kept as the list loses the old length");
                Assert.AreEqual(30, settings.Keeper.Current.RecordingMaxSeconds);
            });
        }
        finally
        {
            TrayDesktop.Run(window.Close);
        }
    }

    [TestMethod]
    public async Task AStartThatCouldNotReadTheFileIsNamedAtTheTopOfThePage()
    {
        using var settings = new SettingsFolder("""{"version":1,"theme":""");
        TrayDesktop.UseProductTheme();
        var window = TrayDesktop.Get(() => SettingsWindow.Open(null, () => settings.Page()));
        try
        {
            await ShownWindow.Eventually(() => TrayDesktop.Get(() => window.IsVisible));

            var top = TrayDesktop.Get(() => Top(window));

            StringAssert.StartsWith(top, "Read settings failed: settings.json, ", "the read failure first on the page (G35)");
            StringAssert.EndsWith(top, SettingsPage.NotKept, "with what that means for its changes");
        }
        finally
        {
            TrayDesktop.Run(window.Close);
        }
    }

    [TestMethod]
    public async Task AWriteRefusedAfterItsRetryIsNamedAtTheTopUntilTheNextChange()
    {
        using var settings = new SettingsFolder();
        TrayDesktop.UseProductTheme();
        var window = TrayDesktop.Get(() => SettingsWindow.Open(null, () => settings.Page()));
        try
        {
            await ShownWindow.Eventually(() => TrayDesktop.Get(() => window.IsVisible));
            TrayDesktop.Run(() =>
            {
                var refused = new Failure("Save settings", Store.Settings.FileName, HeldFileRetry.HeldReason);
                window.Page.WriteFailed(refused);
                Assert.AreEqual(refused.Sentence, Top(window), "the keeper's refusal named at the top of the page (G35)");
                Control(window, SettingsPage.ShowPointerTitle).SetValue(ToggleSwitch.IsCheckedProperty, false);
                Assert.AreEqual("Shortcuts", Top(window), "gone once the next change writes again: the first group's heading is the top");
            });
        }
        finally
        {
            TrayDesktop.Run(window.Close);
        }
    }

    [TestMethod]
    public async Task AScreenshotStartedWhileThePageShowsLeavesItShowing()
    {
        using var settings = new SettingsFolder();
        using var desk = await CaptureDesk.Open();
        var window = TrayDesktop.Get(() => SettingsWindow.Open(null, () => settings.Page()));
        try
        {
            await ShownWindow.Eventually(() => TrayDesktop.Get(() => window.IsVisible));
            var taking = desk.Take();
            await desk.OverlayInFront();

            TrayDesktop.Run(() => Assert.IsTrue(window.IsVisible && window.WindowState == WindowState.Normal, "neither hidden nor minimised for the capture (G3)"));
            TestHotKeys.Tap(VkEscape);
            await taking.WaitAsync(TimeSpan.FromSeconds(10));
            TrayDesktop.Run(() => Assert.IsTrue(window.IsVisible && !window.HasClosed, "still showing once the capture ended"));
        }
        finally
        {
            TrayDesktop.Run(window.Close);
        }
    }

    [TestMethod]
    public async Task TheOpenShortcutPressedOverThePageSummonsAndPressesNoneOfItsFocusedButtons()
    {
        using var settings = new SettingsFolder();
        TrayDesktop.UseProductTheme();
        TrayDesktop.Run(settings.Shortcuts.Start);
        Assert.IsEmpty(settings.Named, "all four registered; another program, or a running ImageStash, holds none");
        var open = Shortcut.Parse("Ctrl+Alt+Space").Value!;
        var window = TrayDesktop.Get(() => SettingsWindow.Open(null, () => settings.Page()));
        try
        {
            await ShownWindow.Eventually(() => TrayDesktop.Get(() => window.IsVisible && window.IsActive));
            var startWithWindows = TrayDesktop.Get(() => ((ToggleSwitch)Control(window, SettingsPage.StartWithWindowsTitle)).IsChecked);
            var pressed = 0;
            foreach (var focused in new[] { "Open folder", SettingsPage.StartWithWindowsTitle })
            {
                TrayDesktop.Run(() => Assert.IsTrue(Control(window, focused).Focus(NavigationMethod.Tab), $"{focused} takes the focus"));
                // The hot key takes Space's press; its release still reaches the window in front, as a person's does.
                TestHotKeys.Press(open);
                await ShownWindow.Eventually(() => settings.Pressed(ShortcutAct.Open) == pressed + 1);
                pressed++;
                await Task.Delay(300);
                TrayDesktop.Run(() =>
                {
                    Assert.IsEmpty(settings.Dialogs.OpenedFolders, $"the shortcut summoned and pressed no button, {focused} focused (G1, F5)");
                    Assert.AreEqual(startWithWindows, ((ToggleSwitch)Control(window, SettingsPage.StartWithWindowsTitle)).IsChecked, $"nor turned a switch, {focused} focused");
                    Assert.IsEmpty(settings.Run.Calls);
                });
            }
        }
        finally
        {
            TrayDesktop.Run(window.Close);
        }
    }

    /// <summary>The first words the page shows, its glyphs aside.</summary>
    private static string Top(SettingsWindow window) =>
        window.GetVisualDescendants().OfType<TextBlock>().First(text => text.IsEffectivelyVisible && !string.IsNullOrEmpty(text.Text) && text.FontFamily.Name != "Segoe Fluent Icons").Text!;

    /// <summary>
    /// <paramref name="part"/>, scrolled to, lies whole in the page's view, left of the vertical scroll bar, and inside
    /// every ancestor that clips it; a part with words has room for all of them (L2, E1, G35).
    /// </summary>
    private static void WholeInView(SettingsWindow window, Control part)
    {
        var viewer = (ScrollViewer)window.Content!;
        var bar = viewer.GetVisualDescendants().OfType<ScrollBar>().FirstOrDefault(each => each.Orientation == Orientation.Vertical && each.IsEffectivelyVisible);
        var right = bar is null ? viewer.Bounds.Width : bar.TranslatePoint(default, viewer)!.Value.X;
        var box = new Rect(part.TranslatePoint(default, viewer)!.Value, part.Bounds.Size);
        var what = $"{part.GetType().Name} “{AutomationProperties.GetName(part) ?? (part as TextBlock)?.Text ?? (part as TextPresenter)?.Text}” at {box}";
        Assert.IsTrue(box.X >= -0.5 && box.Y >= -0.5 && box.Right <= right + 0.5 && box.Bottom <= viewer.Bounds.Height + 0.5,
            $"{what} inside the {viewer.Bounds.Size} view, its scroll bar from x {right} (L2, E1)");
        foreach (var clipping in part.GetVisualAncestors().OfType<Control>().TakeWhile(each => each != viewer).Where(each => each.ClipToBounds))
        {
            var within = new Rect(clipping.TranslatePoint(default, viewer)!.Value, clipping.Bounds.Size);
            Assert.IsTrue(within.Inflate(0.5).Contains(box), $"{what} inside the {clipping.GetType().Name} at {within} that clips it");
        }

        var words = part switch { TextBlock text => text.TextLayout, TextPresenter presenter => presenter.TextLayout, _ => null };
        if (words is not null)
        {
            Assert.IsTrue(words.Width <= part.Bounds.Width + 0.5 && words.Height <= part.Bounds.Height + 0.5,
                $"{what}: its words, {words.Width} × {words.Height}, fit its box, none cut (G35)");
        }
    }

    private static TextBlock Heading(SettingsWindow window, string group) =>
        window.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text == group);
}
