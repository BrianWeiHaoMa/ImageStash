using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.VisualTree;
using ImageStash.Window;
using ImageStash.Windows.Tests.Support;
using static ImageStash.Windows.Tests.Support.CaptureDesk;
using static ImageStash.Windows.Tests.Support.ShownWindow;

namespace ImageStash.Windows.Tests.Window;

/// <summary>
/// Every notice's ×, above the tray and inside the window, the one close button (G16, F10): named Close, the close glyph
/// of Segoe Fluent Icons, never focused, drawn so it stands out from its card's ground under the dark theme and under
/// the light; a real click on it closes that card alone, and above the tray leaves the chat its keystrokes. These checks
/// show a notification above the tray, a chat window of their own and the stash window, and click them.
/// </summary>
[TestClass]
public sealed class NoticeCardTests
{
    [TestInitialize]
    public void UseProductTheme() => TrayDesktop.UseProductTheme();

    [TestMethod]
    public async Task TheCaptureCardsAndThePlainNotificationsXShowUnderBothThemesAndEachClosesItsOwnCard()
    {
        using var desk = await CaptureDesk.Open(["stash beside.png"]);
        var card = await desk.Raise(desk.Items().Single());
        TrayDesktop.Run(() => desk.Notifications.Raise("Copied · press Ctrl+V"));
        await Eventually(() => TrayDesktop.Get(() => Cards(desk).Count == 2));
        Thread.Sleep(500);
        var window = TrayDesktop.Get(() => desk.Notification.Shown!);
        var (captureClose, plainClose) = TrayDesktop.Get(() => (CloseButton(card), CloseButton(Cards(desk)[1])));

        await TheXStandsOutUnderBothThemes(window, captureClose, "the capture card");
        await TheXStandsOutUnderBothThemes(window, plainClose, "the plain notification");

        TestMouse.Click(TestMouse.Centre(captureClose));
        await Eventually(() => TrayDesktop.Get(() => desk.Notifications.Shown.Count == 1));
        TrayDesktop.Run(() => Assert.AreEqual("Copied · press Ctrl+V", desk.Notifications.Shown.Single().Text, "the capture card's × closes it alone"));
        desk.ChatKeepsTheKeystrokes("after a click on the capture card's ×");
        TestMouse.Click(TestMouse.Centre(plainClose));
        await Eventually(() => TrayDesktop.Get(() => desk.Notification.Shown is null));
        desk.ChatKeepsTheKeystrokes("after a click on the plain notification's ×");
        Assert.HasCount(1, desk.Items(), "× only closes the cards");
    }

    [TestMethod]
    public async Task ANoticeInsideTheWindowCarriesTheSameXUnderBothThemesAndItClosesThatNoticeAlone()
    {
        using var shown = ShownWindow.Open();
        await shown.OpenStash(shown.Picture("noticed.png"));
        var (added, other) = TrayDesktop.Get(() => (shown.Notices.Shown.Single(), shown.Notices.Raise("A second notice")));
        await Eventually(() => TrayDesktop.Get(() => NoticeCards(shown).Count == 2));
        var close = TrayDesktop.Get(() => CloseButton(NoticeCards(shown).Single(card => Texts(card).Contains(added.Text))));

        await TheXStandsOutUnderBothThemes(shown.Window, close, "the window's notice");

        using var pointer = TestMouse.PutBackAfterwards();
        TestMouse.Click(TestMouse.Centre(close));
        await Eventually(() => TrayDesktop.Get(() => shown.Notices.Shown.Count == 1));
        TrayDesktop.Run(() => Assert.AreSame(other, shown.Notices.Shown.Single(), "the × closes its own notice alone"));
    }

    private static List<Control> Cards(CaptureDesk desk) =>
        ((StackPanel)((ScrollViewer)desk.Notification.Shown!.Content!).Content!).Children.ToList();

    /// <summary>The cards of the notices showing in the stash window.</summary>
    private static List<Border> NoticeCards(ShownWindow shown) =>
        shown.Window.GetVisualDescendants().OfType<Border>().Where(border => border.Child is StackPanel row
            && row.Children.OfType<Button>().Any(button => Avalonia.Automation.AutomationProperties.GetName(button) == "Close")).ToList();

    private static List<string> Texts(Control control) =>
        control.GetSelfAndVisualDescendants().OfType<TextBlock>().Select(text => text.Text ?? "").ToList();

    /// <summary>
    /// The × is the close glyph of Segoe Fluent Icons, never focused, and under each theme, rendered as the window shows
    /// it, a dozen pixels and more inside the button stand out from the card's ground by half the range or more.
    /// </summary>
    private static async Task TheXStandsOutUnderBothThemes(Avalonia.Controls.Window window, Button close, string what)
    {
        TrayDesktop.Run(() =>
        {
            var glyph = (TextBlock)close.Content!;
            Assert.AreEqual("", glyph.Text, $"{what}: the close glyph (F10)");
            Assert.AreEqual("Segoe Fluent Icons", glyph.FontFamily.Name, what);
            Assert.IsFalse(close.Focusable, $"{what}: the × never takes focus (G16)");
        });
        var before = TrayDesktop.Get(() => Application.Current!.RequestedThemeVariant);
        try
        {
            foreach (var theme in new[] { ThemeVariant.Dark, ThemeVariant.Light })
            {
                TrayDesktop.Run(() => Application.Current!.RequestedThemeVariant = theme);
                var rendered = await RenderedWindow.Of(window);
                var (marks, ground) = TrayDesktop.Get(() => Marks(rendered, RenderedWindow.Place(window, close)));
                Assert.IsGreaterThan(12, marks, $"{what} under the {theme} theme: pixels of the × standing out from the ground {ground:X8} (G16)");
            }
        }
        finally
        {
            TrayDesktop.Run(() => Application.Current!.RequestedThemeVariant = before);
        }
    }

    /// <summary>How many pixels inside <paramref name="area"/> differ from its commonest colour, the ground, by 128 or more in a channel.</summary>
    private static (int Marks, uint Ground) Marks(RenderedWindow rendered, Rect area)
    {
        var pixels = new List<uint>();
        for (var y = (int)Math.Ceiling(area.Y); y < (int)(area.Y + area.Height); y++)
        {
            for (var x = (int)Math.Ceiling(area.X); x < (int)(area.X + area.Width); x++)
            {
                pixels.Add(rendered.At(x, y));
            }
        }

        var ground = pixels.GroupBy(pixel => pixel).MaxBy(group => group.Count())!.Key;
        return (pixels.Count(pixel => Enumerable.Range(0, 3).Any(channel => Math.Abs((int)((pixel >> (8 * channel)) & 0xFF) - (int)((ground >> (8 * channel)) & 0xFF)) >= 128)), ground);
    }
}
