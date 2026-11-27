using ImageStash.Seams;
using ImageStash.Window;
using ImageStash.Windows.Tests.Support;
using static ImageStash.Windows.Tests.Support.ShownSettings;

namespace ImageStash.Windows.Tests.Window;

/// <summary>
/// The Settings page on the desktop refusing a key every app needs and offering Reset: a real press of Delete alone in a
/// field is refused on the spot, nothing registered for it (G32); a row off F5's default shows Reset, which a click and
/// the keyboard alone each take, putting the default back registered, its row's title still naming the field alone (F4,
/// F5, F10).
/// </summary>
[TestClass]
public sealed class SettingsWindowShortcutResetTests
{
    private const ushort VkDelete = 0x2E;
    private const ushort VkEscape = 0x1B;
    private const ushort VkSpace = 0x20;
    private const ushort VkShift = 0x10;
    private const ushort VkTab = 0x09;

    private static readonly Shortcut TakeScreenshot = Shortcut.Parse("Ctrl+PrtScn").Value!;
    private static readonly Shortcut Free = Shortcut.Parse("Ctrl+Shift+F8").Value!;

    [TestMethod]
    public async Task ARealPressOfDeleteAloneIsRefusedOnTheSpotWithNothingRegisteredForIt()
    {
        using var settings = new SettingsFolder();
        using var pointer = TestMouse.PutBackAfterwards();
        TrayDesktop.UseProductTheme();
        TrayDesktop.Run(settings.Shortcuts.Start);
        Assert.IsEmpty(settings.Named, "all four registered; another program, or a running ImageStash, holds none");
        var window = TrayDesktop.Get(() => SettingsWindow.Open(settings.TitleBars, null, () => settings.Page()));
        try
        {
            await ShownWindow.Eventually(() => TrayDesktop.Get(() => window.IsVisible && window.FrameSize is not null));

            await Record(window, SettingsPage.TakeScreenshotTitle);
            TestHotKeys.Tap(VkDelete);
            await ShownWindow.Eventually(() => TrayDesktop.Get(() => WarningUnder(window, SettingsPage.TakeScreenshotTitle)) is not null);

            Assert.AreEqual("Add Ctrl, Alt or Win: Delete alone is needed by every app.", TrayDesktop.Get(() => WarningUnder(window, SettingsPage.TakeScreenshotTitle)),
                "refused on the spot, naming the key and what to add (G32, G35)");
            Assert.IsTrue(TrayDesktop.Get(() => window.Page.ShortcutField(SettingsPage.TakeScreenshotTitle)!.IsRecording), "the field still records");
            using (var holder = OtherProgramHoldingAHotKey.Start(Shortcut.Parse("Delete").Value!))
            {
                Assert.IsNotNull(holder, "Delete is free for another program: ImageStash registered nothing for it (G32)");
            }

            Assert.AreEqual("Ctrl+PrtScn", settings.Keeper.Current.Shortcuts.Screenshot, "nothing written");
            TestHotKeys.Tap(VkEscape);
            await ShownWindow.Eventually(() => TrayDesktop.Get(() => !window.Page.ShortcutField(SettingsPage.TakeScreenshotTitle)!.IsRecording));
            Assert.IsNull(TrayDesktop.Get(() => WarningUnder(window, SettingsPage.TakeScreenshotTitle)), "after Esc the row says only what is still true");
            Assert.IsEmpty(settings.Named, "the refusal made nothing unavailable");
            TestHotKeys.Press(TakeScreenshot);
            await ShownWindow.Eventually(() => settings.Pressed(ShortcutAct.Screenshot) == 1);
        }
        finally
        {
            TrayDesktop.Run(window.Close);
        }
    }

    [TestMethod]
    public async Task ResetByAClickAndByTheKeyboardAlonePutsTheDefaultBackAndLeavesTheRow()
    {
        using var settings = new SettingsFolder();
        using var pointer = TestMouse.PutBackAfterwards();
        TrayDesktop.UseProductTheme();
        TrayDesktop.Run(settings.Shortcuts.Start);
        Assert.IsEmpty(settings.Named, "all four registered; another program, or a running ImageStash, holds none");
        var window = TrayDesktop.Get(() => SettingsWindow.Open(settings.TitleBars, null, () => settings.Page()));
        try
        {
            await ShownWindow.Eventually(() => TrayDesktop.Get(() => window.IsVisible && window.FrameSize is not null));
            var reset = TrayDesktop.Get(() => Control(window, ShortcutResetButton.NameOf(window.Page.ShortcutField(ShortcutAct.Screenshot))));
            var field = TrayDesktop.Get(() => Control(window, SettingsPage.TakeScreenshotTitle));
            Assert.IsInstanceOfType<ShortcutField>(field, "the row's title names its field alone, never its Reset (G32)");
            Assert.IsFalse(TrayDesktop.Get(() => reset.IsVisible), "on F5's default the row offers no Reset");

            await Record(window, SettingsPage.TakeScreenshotTitle);
            TestHotKeys.Press(Free);
            await ShownWindow.Eventually(() => TrayDesktop.Get(() => reset.IsVisible));

            TestMouse.Click(TestMouse.Centre(reset));
            await ShownWindow.Eventually(() => TrayDesktop.Get(() => settings.Shortcuts.ShortcutOf(ShortcutAct.Screenshot)) == TakeScreenshot);
            Assert.AreEqual("Ctrl+PrtScn", settings.Keeper.Current.Shortcuts.Screenshot, "written");
            await ShownWindow.Eventually(() => TrayDesktop.Get(() => !reset.IsVisible));
            Assert.IsTrue(TrayDesktop.Get(() => window.IsActive), "Reset opened nothing (F4)");
            TestHotKeys.Press(TakeScreenshot);
            await ShownWindow.Eventually(() => settings.Pressed(ShortcutAct.Screenshot) == 1);

            await Record(window, SettingsPage.TakeScreenshotTitle);
            TestHotKeys.Press(Free);
            await ShownWindow.Eventually(() => TrayDesktop.Get(() => reset.IsVisible && field.IsFocused));
            using (TestHotKeys.Hold(VkShift))
            {
                TestHotKeys.Tap(VkTab);
            }

            await ShownWindow.Eventually(() => TrayDesktop.Get(() => reset.IsFocused));
            TestHotKeys.Tap(VkSpace);
            await ShownWindow.Eventually(() => TrayDesktop.Get(() => settings.Shortcuts.ShortcutOf(ShortcutAct.Screenshot)) == TakeScreenshot);
            await ShownWindow.Eventually(() => TrayDesktop.Get(() => field.IsFocused));
            await Task.Delay(300);
            Assert.IsFalse(TrayDesktop.Get(() => window.Page.ShortcutFields.Any(each => each.IsRecording)), "the focus back on the field begins no recording");
            Assert.IsEmpty(settings.Named, "nothing unavailable");
            TestHotKeys.Press(TakeScreenshot);
            await ShownWindow.Eventually(() => settings.Pressed(ShortcutAct.Screenshot) == 2);
        }
        finally
        {
            TrayDesktop.Run(window.Close);
        }
    }
}
