using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using ImageStash.Seams;
using ImageStash.Window;
using ImageStash.Windows.Tests.Support;
using static ImageStash.Windows.Tests.Support.ShownSettings;

namespace ImageStash.Windows.Tests.Window;

/// <summary>
/// The Settings page's shortcut fields on the desktop: real presses reach a field while ImageStash's shortcuts are
/// released, a refusal drawn under its row and the four registered again, and the page opens on an unavailable
/// shortcut's row, its field focused for the keyboard alone (G32, G33, F5).
/// </summary>
[TestClass]
public sealed class SettingsWindowShortcutFieldTests
{
    private const ushort VkEscape = 0x1B;
    private const ushort VkReturn = 0x0D;
    private const ushort VkBack = 0x08;
    private const ushort VkShift = 0x10;
    private const ushort VkControl = 0x11;
    private const ushort VkF9 = 0x78;
    private const ushort VkTab = 0x09;

    [TestMethod]
    public async Task RealPressesReachAFieldWhileImageStashsShortcutsAreReleasedAndARefusalIsDrawnUnderItsRow()
    {
        using var settings = new SettingsFolder();
        using var pointer = TestMouse.PutBackAfterwards();
        TrayDesktop.UseProductTheme();
        TrayDesktop.Run(settings.Shortcuts.Start);
        Assert.IsEmpty(settings.Named, "all four registered; another program, or a running ImageStash, holds none");
        var open = Shortcut.Parse("Ctrl+Alt+Space").Value!;
        var held = Shortcut.Parse("Ctrl+Shift+F9").Value!;
        var free = Shortcut.Parse("Ctrl+Shift+F8").Value!;
        var window = TrayDesktop.Get(() => SettingsWindow.Open(null, () => settings.Page()));
        try
        {
            await ShownWindow.Eventually(() => TrayDesktop.Get(() => window.IsVisible && window.FrameSize is not null && InsideWorkingArea(window)));

            await Record(window, SettingsPage.TakeScreenshotTitle);
            TestHotKeys.Press(open);
            await ShownWindow.Eventually(() => TrayDesktop.Get(() => WarningUnder(window, SettingsPage.TakeScreenshotTitle)) is not null);
            Assert.AreEqual("Register the screenshot shortcut failed: Ctrl+Alt+Space, the open shortcut uses it too", TrayDesktop.Get(() => WarningUnder(window, SettingsPage.TakeScreenshotTitle)),
                "the press reached the field and was refused on the spot (G32)");
            Assert.AreEqual(0, settings.Pressed(ShortcutAct.Open), "and summoned nothing");
            Assert.IsFalse(SystemMenuShown(), "no window menu opened by the press");
            TestHotKeys.Press(open);
            await ShownWindow.Eventually(() => settings.Pressed(ShortcutAct.Open) == 1);
            await Task.Delay(300);
            Assert.IsFalse(TrayDesktop.Get(() => window.Page.ShortcutFields.Any(field => field.IsRecording)),
                "the open shortcut pressed while a field has the focus summons and begins no recording, which would release all four");

            await Record(window, SettingsPage.RecordClipTitle);
            TestHotKeys.Press(Shortcut.Parse("Ctrl+PrtScn").Value!);
            await ShownWindow.Eventually(() => TrayDesktop.Get(() => WarningUnder(window, SettingsPage.RecordClipTitle)) is not null);
            Assert.AreEqual("Register the record shortcut failed: Ctrl+PrtScn, the screenshot shortcut uses it too", TrayDesktop.Get(() => WarningUnder(window, SettingsPage.RecordClipTitle)),
                "PrtScn, which Windows delivers no KeyDown for, records (F5)");
            Assert.AreEqual(0, settings.Pressed(ShortcutAct.Screenshot));

            using (var holder = OtherProgramHoldingAHotKey.Start(held))
            {
                Assert.IsNotNull(holder, "Ctrl+Shift+F9 free for the check's holder");
                await Record(window, SettingsPage.TakeScreenshotTitle);
                TestHotKeys.Press(held);
                await ShownWindow.Eventually(() => TrayDesktop.Get(() => WarningUnder(window, SettingsPage.TakeScreenshotTitle))?.Contains("F9") == true);
                Assert.AreEqual("Register the screenshot shortcut failed: Ctrl+Shift+F9, another program holds it", TrayDesktop.Get(() => WarningUnder(window, SettingsPage.TakeScreenshotTitle)),
                    "a combination another app holds is refused and said so (Done when 5)");

                await Record(window, SettingsPage.RecordClipTitle);
                TestHotKeys.PressReleasingModifiersFirst(held);
                await ShownWindow.Eventually(() => TrayDesktop.Get(() => WarningUnder(window, SettingsPage.RecordClipTitle))?.Contains("F9") == true);
                Assert.AreEqual("Register the record shortcut failed: Ctrl+Shift+F9, another program holds it", TrayDesktop.Get(() => WarningUnder(window, SettingsPage.RecordClipTitle)),
                    "let go modifiers first, the held combination is still refused on the spot (G32)");
                Assert.IsFalse(TrayDesktop.Get(() => window.Page.ShortcutField(SettingsPage.RecordClipTitle)!.IsRecording));
                Assert.AreEqual(Shortcut.Parse("Ctrl+Shift+PrtScn").Value, TrayDesktop.Get(() => settings.Shortcuts.ShortcutOf(ShortcutAct.Record)), "never plain F9, which nobody pressed (G32, F5)");
            }

            var heldWithoutShift = Shortcut.Parse("Ctrl+F9").Value!;
            using (var holder = OtherProgramHoldingAHotKey.Start(heldWithoutShift))
            {
                Assert.IsNotNull(holder, "Ctrl+F9 free for the check's holder");
                await Record(window, SettingsPage.StashClipboardImageTitle);
                TestHotKeys.Tap(VkShift);
                TestHotKeys.Press(heldWithoutShift);
                await ShownWindow.Eventually(() => TrayDesktop.Get(() => WarningUnder(window, SettingsPage.StashClipboardImageTitle)) is not null);
                Assert.AreEqual("Register the Stash clipboard image shortcut failed: Ctrl+F9, another program holds it", TrayDesktop.Get(() => WarningUnder(window, SettingsPage.StashClipboardImageTitle)),
                    "a Shift tapped and let go before the combination is no part of it (G32)");

                // Ctrl held from before the click that begins the recording, so its KeyDown never reaches the field, and let go before F9.
                using (var ctrl = TestHotKeys.Hold(VkControl))
                {
                    await Record(window, SettingsPage.TakeScreenshotTitle);
                    using var f9 = TestHotKeys.Hold(VkF9);
                    ctrl.Dispose();
                }

                await ShownWindow.Eventually(() => TrayDesktop.Get(() => !window.Page.ShortcutField(SettingsPage.TakeScreenshotTitle)!.IsRecording));
                await Task.Delay(300);
                Assert.AreEqual("Register the screenshot shortcut failed: Ctrl+F9, another program holds it", TrayDesktop.Get(() => WarningUnder(window, SettingsPage.TakeScreenshotTitle)),
                    "Ctrl held through the click is part of the press (G32)");
                Assert.AreEqual(Shortcut.Parse("Ctrl+PrtScn").Value, TrayDesktop.Get(() => settings.Shortcuts.ShortcutOf(ShortcutAct.Screenshot)), "never plain F9, which nobody pressed (F5)");
            }

            // Enter and Backspace type their own characters in every program (G32's "types a character").
            foreach (var (key, name) in new[] { (VkReturn, "Enter"), (VkBack, "Back") })
            {
                await Record(window, SettingsPage.OpenTitle);
                TestHotKeys.Tap(key);
                await ShownWindow.Eventually(() => TrayDesktop.Get(() => WarningUnder(window, SettingsPage.OpenTitle)) is not null && !TrayDesktop.Get(() => window.Page.ShortcutField(SettingsPage.OpenTitle)!.IsRecording));
                Assert.AreEqual($"Register the open shortcut failed: {name}, {ShortcutReasons.TypesACharacter}", TrayDesktop.Get(() => WarningUnder(window, SettingsPage.OpenTitle)));
                Assert.AreEqual(open, TrayDesktop.Get(() => settings.Shortcuts.ShortcutOf(ShortcutAct.Open)));
            }

            await Record(window, SettingsPage.TakeScreenshotTitle);
            TestHotKeys.Press(free);
            await ShownWindow.Eventually(() => TrayDesktop.Get(() => WarningUnder(window, SettingsPage.TakeScreenshotTitle)) is null);
            Assert.AreEqual(free, TrayDesktop.Get(() => settings.Shortcuts.ShortcutOf(ShortcutAct.Screenshot)), "taken (G32)");
            Assert.AreEqual("Ctrl+Shift+F8", settings.Keeper.Current.Shortcuts.Screenshot);
            CollectionAssert.AreEqual(new[] { "Ctrl", "Shift", "F8" }, TrayDesktop.Get(() => KeysShown(window, SettingsPage.TakeScreenshotTitle)), "the field shows it");
            TestHotKeys.Press(free);
            await ShownWindow.Eventually(() => settings.Pressed(ShortcutAct.Screenshot) == 1);

            // Tab and Esc are the page's keys, never read as a press (G32): Tab moves on, as anywhere on the page, and Esc
            // ends the recording alone (G2's one thing at a time); neither leaves a warning on the row the remap cleared.
            await Record(window, SettingsPage.TakeScreenshotTitle);
            TestHotKeys.Tap(VkTab);
            await ShownWindow.Eventually(() => TrayDesktop.Get(() => !Control(window, SettingsPage.TakeScreenshotTitle).IsFocused));
            Assert.IsFalse(TrayDesktop.Get(() => window.Page.ShortcutField(SettingsPage.TakeScreenshotTitle)!.IsRecording), "Tab moved on, which ends the recording");
            await Task.Delay(300);
            Assert.IsNull(TrayDesktop.Get(() => WarningUnder(window, SettingsPage.TakeScreenshotTitle)), "Tab is no press (G32)");
            Assert.AreEqual(free, TrayDesktop.Get(() => settings.Shortcuts.ShortcutOf(ShortcutAct.Screenshot)));

            await Record(window, SettingsPage.TakeScreenshotTitle);
            TestHotKeys.Tap(VkEscape);
            await ShownWindow.Eventually(() => TrayDesktop.Get(() => !window.Page.ShortcutField(SettingsPage.TakeScreenshotTitle)!.IsRecording));
            await Task.Delay(300);
            Assert.IsFalse(TrayDesktop.Get(() => window.HasClosed), "Esc during a recording ends the recording alone (G2's one thing at a time)");
            Assert.IsNull(TrayDesktop.Get(() => WarningUnder(window, SettingsPage.TakeScreenshotTitle)), "Esc is no press (G32)");
            Assert.AreEqual(free, TrayDesktop.Get(() => settings.Shortcuts.ShortcutOf(ShortcutAct.Screenshot)));
            TestHotKeys.Press(open);
            await ShownWindow.Eventually(() => settings.Pressed(ShortcutAct.Open) == 2);
            TestHotKeys.Tap(VkEscape);
            await ShownWindow.Eventually(() => TrayDesktop.Get(() => window.HasClosed));
        }
        finally
        {
            TrayDesktop.Run(window.Close);
        }
    }

    [TestMethod]
    public async Task OpenedOnAnUnavailableShortcutsRowItsFieldHasTheFocusAndTheKeyboardAloneRecordsAnother()
    {
        var open = Shortcut.Parse("Ctrl+Alt+Space").Value!;
        using var settings = new SettingsFolder();
        TrayDesktop.UseProductTheme();
        SettingsWindow? window = null;
        using (var holder = OtherProgramHoldingAHotKey.Start(open))
        {
            Assert.IsNotNull(holder, "the open shortcut free for the check's holder");
            TrayDesktop.Run(settings.Shortcuts.Start);
        }

        try
        {
            var unavailable = TrayDesktop.Get(() => settings.Shortcuts.UnavailableReason(ShortcutAct.Open));
            Assert.AreEqual("Register the open shortcut failed: Ctrl+Alt+Space, another program holds it", unavailable?.Sentence);
            window = TrayDesktop.Get(() => SettingsWindow.Open(null, () => settings.Page(), settings.Shortcuts.FirstUnavailable));
            var field = TrayDesktop.Get(() => Control(window, SettingsPage.OpenTitle));
            await ShownWindow.Eventually(() => TrayDesktop.Get(() => window.IsActive && field.IsFocused));

            Assert.AreEqual(unavailable!.Sentence, TrayDesktop.Get(() => WarningUnder(window, SettingsPage.OpenTitle)), "Settings opens on that row with why (G33)");
            Assert.IsFalse(TrayDesktop.Get(() => window.Page.ShortcutField(SettingsPage.OpenTitle)!.IsRecording), "focused, not yet recording");
            TestHotKeys.Tap(VkReturn);
            await ShownWindow.Eventually(() => TrayDesktop.Get(() => window.Page.ShortcutField(SettingsPage.OpenTitle)!.IsRecording));
            TestHotKeys.Press(open);
            await ShownWindow.Eventually(() => TrayDesktop.Get(() => WarningUnder(window, SettingsPage.OpenTitle)) is null);

            Assert.IsNull(TrayDesktop.Get(() => settings.Shortcuts.FirstUnavailable), "registered once its holder let go and it was changed in Settings (G33)");
            TestHotKeys.Press(open);
            await ShownWindow.Eventually(() => settings.Pressed(ShortcutAct.Open) == 1);
        }
        finally
        {
            TrayDesktop.Run(() => window?.Close());
        }
    }

    /// <summary>A real click on the field titled <paramref name="title"/>, until it records with its page in front.</summary>
    private static async Task Record(SettingsWindow window, string title)
    {
        var field = TrayDesktop.Get(() => Control(window, title));
        TrayDesktop.Run(field.BringIntoView);
        await Task.Delay(100);
        TestMouse.Click(TestMouse.Centre(field));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => window.IsActive && window.Page.ShortcutField(title)!.IsRecording));
    }

    /// <summary>The keys the field titled <paramref name="title"/> shows while idle.</summary>
    private static List<string> KeysShown(SettingsWindow window, string title) =>
        Control(window, title).GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible && text.FontFamily.Name != "Segoe Fluent Icons").Select(text => text.Text!).ToList();

    /// <summary>Whether a window menu (class #32768) is up anywhere on the desktop.</summary>
    private static bool SystemMenuShown() => FindWindowW("#32768", null) is var menu && menu != 0 && IsWindowVisible(menu);

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, ExactSpelling = true)]
    private static extern nint FindWindowW(string? className, string? windowName);

    [System.Runtime.InteropServices.DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool IsWindowVisible(nint window);
}
