using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using ImageStash.Seams;
using ImageStash.Window;
using ImageStash.Windows.Seams;
using ImageStash.Windows.Tests.Support;

namespace ImageStash.Windows.Tests.Window;

/// <summary>
/// The two places a notification stands as Windows shows them (G16, F4, G8): a capture's above the tray, and a paste that
/// could not be made, asked while the stash window stood on another display, at that display's bottom-right
/// working-area corner, both at once; each window shown without taking the foreground, topmost, without a taskbar
/// button, rounded and left out of screen captures (G15, S4). The other display is stood in for by the left third of the
/// primary one, so the check runs on a PC with one display; two real displays of different scaling are a person's check.
/// A screenshot's card on another display stands at that display's corner the same way, and its field takes focus
/// without hiding the flyout.
/// </summary>
[TestClass]
public sealed class NotificationPlacesTests
{
    private const uint WsExNoActivate = 0x08000000;
    private const uint WsExTopmost = 0x00000008;
    private const uint WdaExcludeFromCapture = 0x11;

    [TestInitialize]
    public void UseProductTheme() => TrayDesktop.UseProductTheme();

    [TestMethod]
    public async Task APasteThatCouldNotBeMadeStandsOnTheOtherDisplayWhileACapturesStaysAboveTheTrayAndNeitherTakesTheForeground()
    {
        using var pointer = TestMouse.PutBackAfterwards();
        using var tray = new WindowsTray();
        using var chat = TargetApp.Start("ImageStash check chat", DisplayPlaces.At(DisplayPlaces.Primary(), 100, 100));
        var primary = DisplayPlaces.Primary();
        var other = new Display(
            new PixelRect(primary.Bounds.X, primary.Bounds.Y, primary.Bounds.Width / 3, primary.Bounds.Height),
            new PixelRect(primary.WorkingArea.X, primary.WorkingArea.Y, primary.WorkingArea.Width / 3, primary.WorkingArea.Height),
            primary.Scaling,
            IsPrimary: false);
        var notifications = new Notices(TimeProvider.System);
        var stacks = new NotificationStacks(notifications);
        var fallback = new PasteFallback(stacks);
        var quiet = new TrayIconMidScreen(new WindowsQuietWindows(tray));
        var aboveTray = TrayDesktop.Get(() => new Notification(notifications, quiet, new WindowsScreen(), _ => { }));
        Notification? elsewhere = null;
        Notices? standing = null;
        // As the app draws each other display's notifications.
        stacks.Opened += (stack, display) => (standing, elsewhere) = (stack, new Notification(stack, quiet, new WindowsScreen(), _ => { }, () => display));
        chat.ClickTitleBar();

        TrayDesktop.Run(() =>
        {
            notifications.Raise("Screenshot 2026-11-26 15.00.00 is in the stash", "Undo", () => Task.FromResult(Outcome.Done));
            fallback.Raise(PasteTargetReasons.HigherRights, "Relay", other);
        });
        Thread.Sleep(600);

        var tray1 = Read(aboveTray);
        var other1 = Read(elsewhere!);
        Assert.AreEqual(chat.Window, FrontWindow.GetForegroundWindow(), "the app in front keeps the foreground (G16)");
        foreach (var (shown, which) in new[] { (tray1, "above the tray"), (other1, "on the other display") })
        {
            Assert.IsFalse(shown.Active, which);
            Assert.IsFalse(shown.InTaskbar, $"{which}: no taskbar button");
            Assert.AreEqual(WsExTopmost, shown.Extended & WsExTopmost, which);
            Assert.AreEqual(WsExNoActivate, shown.Extended & WsExNoActivate, which);
            Assert.AreEqual(CornerPreference.Round, CornerPreference.Of(shown.Handle), $"{which}: rounded (F13)");
            Assert.IsTrue(GetWindowDisplayAffinity(shown.Handle, out var affinity) && affinity == WdaExcludeFromCapture, $"{which}: left out of captures (G15, S4)");
        }

        Assert.AreNotEqual(tray1.Handle, other1.Handle, "two windows");
        Assert.AreEqual("Copied · press Ctrl+V", FrontWindow.NativeText(other1.Handle), "found by its first line, as the published paste checks find it");
        Assert.IsTrue(other.WorkingArea.Contains(other1.Outer), $"{other1.Outer} wholly inside the other display's working area {other.WorkingArea}");
        var gap = DisplayPlaces.Scaled(other, (int)Summon.IconGap);
        Assert.IsLessThanOrEqualTo(2, Math.Abs(other.WorkingArea.Right - gap - other1.Outer.Right), "at its right edge");
        Assert.IsLessThanOrEqualTo(2, Math.Abs(other.WorkingArea.Bottom - gap - other1.Outer.Bottom), "at its bottom edge");
        Assert.IsTrue(primary.WorkingArea.Contains(tray1.Outer), tray1.Outer.ToString());
        Assert.IsGreaterThan(other.WorkingArea.Right, tray1.Outer.X, "the capture's stays above the stand-in tray icon, off the other display");
        TrayDesktop.Run(() => Assert.IsTrue(notifications.OffersUndo(notifications.Shown.Single()), "with its Undo"));

        TrayDesktop.Run(() => standing!.Close(standing.Shown.Single()));
        // This check uses real time. The other window closes after its card fades over the look's exit.
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => elsewhere!.Shown is null));
        TrayDesktop.Run(() => Assert.IsNull(elsewhere!.Shown, "its × closes the other display's window"));
        Assert.AreEqual(tray1.Outer, Read(aboveTray).Outer, "and the capture's stays where it was");
        TrayDesktop.Run(() => notifications.Close(notifications.Shown.Single()));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => aboveTray.Shown is null));
    }

    [TestMethod]
    public async Task AScreenshotsCardOnAnotherDisplayStandsAtItsCornerAndAClickIntoItsFieldKeepsTheFlyout()
    {
        using var desk = await CaptureDesk.Open(["corner card.png"]);
        var primary = DisplayPlaces.Primary();
        var other = new Display(
            new PixelRect(primary.Bounds.X, primary.Bounds.Y, primary.Bounds.Width / 3, primary.Bounds.Height),
            new PixelRect(primary.WorkingArea.X, primary.WorkingArea.Y, primary.WorkingArea.Width / 3, primary.WorkingArea.Height),
            primary.Scaling,
            IsPrimary: false);
        await desk.FlyoutInFront();

        var item = desk.Items().Single();
        TrayDesktop.Run(() => desk.Capture.Raise(item, CaptureKind.Screenshot, onClipboard: false, lengthMs: null, on: other));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => desk.Elsewhere.SingleOrDefault()?.Shown is { IsVisible: true }));
        Thread.Sleep(500);
        var elsewhere = desk.Elsewhere.Single();
        var shown = Read(elsewhere);

        Assert.IsNull(TrayDesktop.Get(() => desk.Notification.Shown), "nothing above the tray");
        Assert.IsFalse(shown.Active, "shown without the foreground (G16)");
        Assert.IsTrue(GetWindowDisplayAffinity(shown.Handle, out var affinity) && affinity == WdaExcludeFromCapture, "left out of captures (G15, S4)");
        Assert.IsTrue(other.WorkingArea.Contains(shown.Outer), $"{shown.Outer} wholly inside the other display's working area {other.WorkingArea}");
        var gap = DisplayPlaces.Scaled(other, (int)Summon.IconGap);
        Assert.IsLessThanOrEqualTo(2, Math.Abs(other.WorkingArea.Right - gap - shown.Outer.Right), "at its right edge");
        Assert.IsLessThanOrEqualTo(2, Math.Abs(other.WorkingArea.Bottom - gap - shown.Outer.Bottom), "at its bottom edge");

        var card = TrayDesktop.Get(() => elsewhere.Shown!.GetVisualDescendants().OfType<CaptureCard>().Single());
        TestMouse.Click(TestMouse.Centre(TrayDesktop.Get(() => CaptureDesk.NameButton(card))));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => card.Field.IsEditing && CaptureDesk.NameBox(card).IsFocused));
        Thread.Sleep(300);

        Assert.IsTrue(TrayDesktop.Get(() => desk.Shown.Window.IsVisible), "the field's focus is ImageStash's: the flyout stays (G2, G16)");
        TrayDesktop.Run(() => desk.Stacks.All[1].Close(desk.Stacks.All[1].Shown.Single()));
        await ShownWindow.Eventually(() => TrayDesktop.Get(() => elsewhere.Shown is null));
    }

    private static (nint Handle, bool Active, bool InTaskbar, uint Extended, PixelRect Outer) Read(Notification notification) => TrayDesktop.Get(() =>
    {
        var window = notification.Shown!;
        var handle = window.TryGetPlatformHandle()!.Handle;
        return (handle, window.IsActive, window.ShowInTaskbar, (uint)GetWindowLongPtrW(handle, -20),
            new PixelRect(window.Position, PixelSize.FromSize(window.FrameSize ?? window.ClientSize, window.RenderScaling)));
    });

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint GetWindowLongPtrW(nint window, int index);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowDisplayAffinity(nint window, out uint affinity);
}
