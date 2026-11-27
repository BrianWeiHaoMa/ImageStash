using Avalonia.Controls;
using Avalonia.VisualTree;
using ImageStash.Seams;
using ImageStash.Window;
using ImageStash.Windows.Seams;
using ImageStash.Windows.Tests.Support;
using static ImageStash.Windows.Tests.Support.ShownSettings;

namespace ImageStash.Windows.Tests.Window;

/// <summary>
/// The notification that a shortcut another program really holds is unavailable, as Windows shows it (G33, G16): both
/// capture-tool defaults held at start are each said above the tray while a chat window keeps the foreground, and each
/// Choose another, clicked with the real mouse, brings the one Settings page forward on its own shortcut's field (G32).
/// These checks hold Ctrl+Alt+Space and Ctrl+PrtScn from a PowerShell of their own, show a chat window of their own at
/// (100, 100), click the notification's buttons and close the page.
/// </summary>
[TestClass]
public sealed class ShortcutNotificationTests
{
    private static readonly Shortcut Open = Shortcut.Parse("Ctrl+Alt+Space").Value!;
    private static readonly Shortcut TakeScreenshot = Shortcut.Parse("Ctrl+PrtScn").Value!;

    [TestMethod]
    public async Task EachTakenShortcutIsSaidAboveTheTrayWithoutTheFocusAndChooseAnotherBringsSettingsForwardOnItsRow()
    {
        using var pointer = TestMouse.PutBackAfterwards();
        using var settings = new SettingsFolder();
        TrayDesktop.UseProductTheme();
        using var tray = new WindowsTray();
        using var chat = TargetApp.Start("ImageStash check chat", DisplayPlaces.At(DisplayPlaces.Primary(), 100, 100));
        var notifications = new Notices(new StoppedClock());
        var notification = TrayDesktop.Get(() => new Notification(notifications, new WindowsQuietWindows(tray), new WindowsScreen(), _ => { }));
        SettingsWindow? page = null;
        var told = new ShortcutNotifications(notifications, act => page = SettingsWindow.Open(settings.TitleBars, page, () => settings.Page(), act));
        chat.ClickTitleBar();
        using (var openHolder = OtherProgramHoldingAHotKey.Start(Open))
        using (var screenshotHolder = OtherProgramHoldingAHotKey.Start(TakeScreenshot))
        {
            Assert.IsNotNull(openHolder, "the open shortcut free for the check's holder");
            Assert.IsNotNull(screenshotHolder, "the screenshot shortcut free for the check's holder");
            // Told as the app tells it, each unavailable shortcut as the policy names it at start.
            TrayDesktop.Run(() =>
            {
                settings.Shortcuts.Start();
                foreach (var problem in settings.Named)
                {
                    told.Tell(problem);
                }

                // Finish the cards' entrances on the stopped clock before clicking their buttons.
                NoticeMotion.Advance(notifications, notification.Stack, NoticeMotion.Entrance);
            });
        }

        try
        {
            await Task.Delay(500);
            var (texts, chooses) = TrayDesktop.Get(() =>
            {
                var window = notification.Shown!;
                return (window.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text).ToList(),
                    window.GetVisualDescendants().OfType<Button>().Where(button => button.Content as string == ShortcutNotifications.ChooseAnother).ToList());
            });
            Assert.AreEqual(chat.Window, FrontWindow.GetForegroundWindow(), "the chat keeps the foreground (G16)");
            CollectionAssert.IsSubsetOf(new[]
            {
                "Ctrl+Alt+Space is used by another app, so Open ImageStash has no shortcut.",
                "Ctrl+PrtScn is used by another app, so Take a screenshot has no shortcut.",
            }, texts, "each taken shortcut said above the tray");
            Assert.HasCount(2, chooses, "each with its own Choose another");

            // Take a screenshot's button first: its own row, not the first unavailable shortcut's.
            TestMouse.Click(TestMouse.Centre(ChooseAnotherBeside(notification, "Ctrl+PrtScn is used by another app, so Take a screenshot has no shortcut.")));
            await ShownWindow.Eventually(() => TrayDesktop.Get(() => page is { IsActive: true } && Control(page, SettingsPage.TakeScreenshotTitle).IsFocused));
            var first = page!;

            TestMouse.Click(TestMouse.Centre(ChooseAnotherBeside(notification, "Ctrl+Alt+Space is used by another app, so Open ImageStash has no shortcut.")));
            await ShownWindow.Eventually(() => TrayDesktop.Get(() => page is { IsActive: true } && Control(page, SettingsPage.OpenTitle).IsFocused));
            Assert.AreSame(first, page, "brought forward on the open shortcut's row, not opened twice (G32)");
        }
        finally
        {
            TrayDesktop.Run(() => page?.Close());
            TrayDesktop.Run(() =>
            {
                notifications.Shown.ToList().ForEach(notifications.Close);
                // Closing the notices starts their fades over the look's exit.
                NoticeMotion.Advance(notifications, notification.Stack, NoticeMotion.Exit);
            });
        }
    }

    /// <summary>The Choose another on the card saying <paramref name="words"/>.</summary>
    private static Button ChooseAnotherBeside(Notification notification, string words) => TrayDesktop.Get(() =>
    {
        var text = notification.Shown!.GetVisualDescendants().OfType<TextBlock>().Single(block => block.Text == words);
        return text.GetVisualAncestors().OfType<Control>()
            .Select(card => card.GetVisualDescendants().OfType<Button>().Where(button => button.Content as string == ShortcutNotifications.ChooseAnother).ToList())
            .First(buttons => buttons.Count > 0)
            .Single();
    });
}
