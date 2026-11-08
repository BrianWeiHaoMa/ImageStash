using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using ImageStash.Files;
using ImageStash.Media;
using ImageStash.Seams;
using ImageStash.Store;
using ImageStash.Window;
using ImageStash.Windows.Seams;
using ImageStash.Windows.Tests.Support;
using static ImageStash.Windows.Tests.Support.ShownWindow;

namespace ImageStash.Windows.Tests.Window;

/// <summary>
/// Take a screenshot over the real screen, overlay, clipboard and notifications, the stash window hidden as a person
/// has it while typing in a chat (G14): a real drag inside a pattern of the check's own saves exactly its pixels at
/// those physical coordinates as a PNG item with the auto name (F3, F12); the clipboard then holds the file, its PNG
/// bytes and an opaque CF_DIBV5 (G8), no use recorded (G10); the capture notification above the tray shows the
/// picture, says it is on the clipboard, its measured size, the name field and Undo, which sends the file to the
/// Recycle Bin (G16, G24, G29); the chat keeps the keystrokes afterwards; with Also copy off the clipboard is not
/// touched and the notification says in the stash only. Through a real press of <c>Ctrl+PrtScn</c>, registered as
/// ImageStash binds its shortcuts (F5): over the chat, the screen frozen within a second, a notification showing then
/// left out of the saved pixels (S4), Recent unchanged, and a second press cancelled with Esc; over the flyout, the
/// flyout hidden within a second and back with its query and selection (G3); held by another program at start, named
/// while the tray's entry still takes one (G33, G34). These checks show the pattern at (560, 160), or one under a
/// notification shown above a point below the primary display's middle, the overlay over the primary display, and drag
/// the mouse across it.
/// </summary>
[TestClass]
public sealed class ScreenshotTests
{
    private const ushort VkEscape = 0x1B;
    private static readonly Shortcut TakeScreenshot = Shortcut.Parse("Ctrl+PrtScn").Value!;

    [TestMethod]
    public async Task ARealPressOverTheChatSavesTheDragWithNothingOfTheNotificationShowingAndTheChatKeepsTheKeystrokes()
    {
        using var desk = await CaptureDesk.Open(["used before the press.png"], midScreen: true);
        var used = desk.Items().Single();
        await TrayDesktop.Get(() => desk.Shown.Listing.Store!.RecordUses([used], desk.Clock.GetUtcNow()));
        var recent = await RecentIds(desk);
        using var pressed = CaptureShortcuts.Register(desk);
        Assert.IsEmpty(pressed.Named(), "all four registered; another program, or a running ImageStash, holds none");
        using var pattern = PatternWindow.Show(DisplayPlaces.UnderNotification(DisplayPlaces.Primary()));
        await pattern.OnScreen(new WindowsScreen());
        TrayDesktop.Run(() => desk.Notifications.Raise("A notification of the check's own"));
        await Eventually(() => TrayDesktop.Get(() => desk.Notification.Shown is not null));
        // Placed again once shown, at the next idle (Notification.Show).
        await Task.Delay(500);
        var standing = TrayDesktop.Get(() => new PixelRect(desk.Notification.Shown!.Position, PixelSize.FromSize(desk.Notification.Shown.ClientSize, desk.Notification.Shown.RenderScaling)));
        var around = new PixelRect(standing.X - 20, standing.Y - 20, standing.Width + 40, standing.Height + 40);
        Assert.IsTrue(pattern.Bounds.Contains(around), $"the notification at {standing} stands inside the pattern");
        Assert.AreEqual(desk.NotificationHandle, GetAncestor(WindowFromPoint(new NativePoint(standing.Center.X, standing.Center.Y)), GaRoot), "and over it");
        Assert.AreEqual(desk.Chat.Window, FrontWindow.GetForegroundWindow(), "the chat in front at the press");
        var displays = new WindowsScreen().Displays().Count;

        var clock = Stopwatch.StartNew();
        TestHotKeys.Press(TakeScreenshot);
        await Eventually(() => TrayDesktop.Get(() => desk.Overlay.Shown.Count == displays));
        var took = clock.ElapsedMilliseconds;
        await desk.OverlayInFront();
        var region = await CaptureDesk.Drag(around.TopLeft, new PixelPoint(around.Right - 1, around.Bottom - 1));
        await pressed.Ended(1);

        Assert.IsLessThan(1000, took, "every display frozen and dimmed within a second of the press (G14)");
        var item = desk.Items().Single(each => each.Id != used.Id);
        var name = AutoName.Screenshot(desk.Clock.GetLocalNow());
        Assert.AreEqual(name, item.Name, "the auto name in local time (F12)");
        Assert.AreEqual(new MediaFacts(ImageFormat.Png, region.Width, region.Height, false, 1, 0), item.Facts, "a still PNG of the region (F3)");
        var differences = pattern.Differences(CaptureDesk.FirstFrame(item.FilePath), region);
        Assert.IsEmpty(differences, $"the pattern's pixels, nothing of the notification standing over them (G14, S4): {string.Join("; ", differences.Take(5))}");
        var read = TestClipboard.Read();
        CollectionAssert.AreEqual(new[] { item.FilePath }, read.Files.ToList(), "the file (G8)");
        CollectionAssert.AreEqual(File.ReadAllBytes(item.FilePath), read.Png, "its PNG bytes");
        var rows = read.DibV5![124..];
        Assert.IsTrue(Enumerable.Range(0, region.Width * region.Height).All(pixel => rows[pixel * 4 + 3] == 255), "an opaque CF_DIBV5: every alpha byte 255");
        Assert.AreEqual(1L, desk.Shown.Uses(), "the capture records no use (G10)");
        CollectionAssert.AreEqual(recent, await RecentIds(desk), "Recent unchanged (G4, G10)");
        var card = await desk.CardShown(name);
        var notice = TrayDesktop.Get(() => desk.Notifications.Shown.Single(each => each.Text.StartsWith("Saved")));
        Assert.AreEqual($"Saved “{name}” to your stash. It is on the clipboard too.", notice.Text, "in the stash and on the clipboard (G16)");
        Assert.AreEqual("Undo", notice.UndoLabel);
        await Eventually(() => TrayDesktop.Get(() => card.Size) == FileSize.Words(new FileInfo(item.FilePath).Length));
        TrayDesktop.Run(() => Assert.AreEqual(name, card.Field.Shown, "the name as a field"));
        desk.ChatKeepsTheKeystrokes("after the capture");

        var shown = TrayDesktop.Get(() => desk.Notifications.Shown.Count);
        TestHotKeys.Press(TakeScreenshot);
        await desk.OverlayInFront();
        TestHotKeys.Tap(VkEscape);
        await pressed.Ended(2);

        Assert.HasCount(2, desk.Items(), "Esc adds nothing (G14)");
        TrayDesktop.Run(() =>
        {
            Assert.IsEmpty(desk.Overlay.Shown, "no overlay left (F4)");
            Assert.HasCount(shown, desk.Notifications.Shown, "and nothing said (E3)");
        });
        desk.ChatKeepsTheKeystrokes("after the cancelled capture");
    }

    [TestMethod]
    public async Task ARealPressWithTheFlyoutInFrontHidesItWithinASecondAndBringsItBackWithItsQueryAndSelection()
    {
        using var desk = await CaptureDesk.Open(["pressed kept one.png", "pressed kept two.png"]);
        var shown = desk.Shown;
        using var pattern = PatternWindow.Show();
        await pattern.OnScreen(new WindowsScreen());
        using var pressed = CaptureShortcuts.Register(desk);
        await desk.FlyoutInFront();
        TrayDesktop.Run(() => shown.Search.Text = "pressed");
        await Eventually(() => TrayDesktop.Get(() => shown.Listing.Settled && shown.Listing.Items.Count == 2));
        TrayDesktop.Run(() => shown.Listing.Focus(shown.Listing.Items[1].Id));
        await Eventually(() => TrayDesktop.Get(() => shown.Listing.Selection.SequenceEqual([shown.Listing.Items[1].Id])));
        var (selection, handle) = TrayDesktop.Get(() => (shown.Listing.Selection.ToList(), shown.Window.TryGetPlatformHandle()!.Handle));

        var clock = Stopwatch.StartNew();
        TestHotKeys.Press(TakeScreenshot);
        await Eventually(() => TrayDesktop.Get(() => desk.Overlay.Shown.Count > 0 && !shown.Window.IsVisible));
        var took = clock.ElapsedMilliseconds;
        await desk.OverlayInFront();
        var region = await CaptureDesk.Drag(pattern.Blocks[0].Block.Position + new PixelVector(20, 20), pattern.Blocks[3].Block.Position + new PixelVector(80, 50));
        await pressed.Ended(1);

        Assert.IsLessThan(1000, took, "the flyout hidden and the screen dimmed within a second of the press (G3, G14)");
        await Eventually(() => FrontWindow.GetForegroundWindow() == handle && TrayDesktop.Get(() => shown.Window.IsActive));
        TrayDesktop.Run(() =>
        {
            Assert.IsTrue(shown.Window.IsVisible, "the flyout back once the capture ended (G3)");
            Assert.AreEqual("pressed", shown.Search.Text, "with its query");
            CollectionAssert.AreEqual(selection, shown.Listing.Selection.ToList(), "and its selection, as the listing's refresh keeps them");
        });
        var item = desk.Items().Single(each => each.Name.StartsWith("Screenshot "));
        Assert.IsEmpty(pattern.Differences(CaptureDesk.FirstFrame(item.FilePath), region), "the pattern's pixels, nothing of the flyout");
    }

    [TestMethod]
    public async Task HeldByAnotherProgramAtStartTheShortcutIsNamedAndTheTraysEntryStillTakesAScreenshot()
    {
        var held = OtherProgramHoldingAHotKey.Start(TakeScreenshot);
        Assert.IsNotNull(held, "the check's holder took Ctrl+PrtScn");
        try
        {
            using var desk = await CaptureDesk.Open();
            using var pressed = CaptureShortcuts.Register(desk);

            CollectionAssert.AreEqual(new[] { "Register the screenshot shortcut failed: Ctrl+PrtScn, another program holds it" }, pressed.Named(), "which shortcut is unavailable and why, for the tooltip (G33)");
            TestHotKeys.Press(TakeScreenshot);
            await Task.Delay(500);
            TrayDesktop.Run(() => Assert.IsEmpty(desk.Overlay.Shown, "the press is the other program's"));

            // The tray's Take a screenshot runs the act on the UI thread, as the desk starts it.
            var taking = desk.Take();
            await desk.OverlayInFront();
            TestHotKeys.Tap(VkEscape);
            await taking;

            Assert.IsEmpty(desk.Items(), "Esc saves nothing (G14)");
            TrayDesktop.Run(() =>
            {
                Assert.IsEmpty(desk.Overlay.Shown, "no overlay left (F4)");
                Assert.IsEmpty(desk.Notifications.Shown, "and nothing said (E3)");
            });
            Assert.AreEqual(0, pressed.Taken, "the shortcut never took one");
            desk.ChatKeepsTheKeystrokes("after the cancelled capture");
        }
        finally
        {
            held.Dispose();
        }
    }
    [TestMethod]
    public async Task ADragInsideThePatternSavesItsPixelsCopiesThemAndAnnouncesThemWhileTheChatKeepsTheKeystrokes()
    {
        using var desk = await CaptureDesk.Open();
        using var pattern = PatternWindow.Show();
        await pattern.OnScreen(new WindowsScreen());

        var taking = desk.Take();
        await desk.OverlayInFront();
        var region = await CaptureDesk.Drag(pattern.Blocks[0].Block.Position + new PixelVector(20, 20), pattern.Blocks[3].Block.Position + new PixelVector(80, 50));
        await taking;

        var item = desk.Items().Single();
        var name = AutoName.Screenshot(desk.Clock.GetLocalNow());
        Assert.AreEqual(name, item.Name, "the auto name in local time (F12)");
        Assert.AreEqual(new MediaFacts(ImageFormat.Png, region.Width, region.Height, false, 1, 0), item.Facts, "a still PNG of the region (F3)");
        var differences = pattern.Differences(CaptureDesk.FirstFrame(item.FilePath), region);
        Assert.IsEmpty(differences, $"the pattern's pixels at those physical coordinates (G14): {string.Join("; ", differences.Take(5))}");
        var read = TestClipboard.Read();
        CollectionAssert.AreEqual(new[] { item.FilePath }, read.Files.ToList(), "the file (G8)");
        CollectionAssert.AreEqual(File.ReadAllBytes(item.FilePath), read.Png, "its PNG bytes");
        var dib = read.DibV5!;
        Assert.AreEqual((124, region.Width, region.Height), (BitConverter.ToInt32(dib, 0), BitConverter.ToInt32(dib, 4), BitConverter.ToInt32(dib, 8)));
        var rows = dib[124..];
        Assert.HasCount(region.Width * region.Height * 4, rows);
        Assert.IsTrue(Enumerable.Range(0, region.Width * region.Height).All(pixel => rows[pixel * 4 + 3] == 255), "an opaque bitmap: every alpha byte 255");
        Assert.AreEqual(0L, desk.Shown.Uses(), "a capture is no use (G10)");
        var card = await desk.CardShown(name);
        var notice = TrayDesktop.Get(() => desk.Notifications.Shown.Single());
        Assert.AreEqual($"Saved “{name}” to your stash. It is on the clipboard too.", notice.Text, "in the stash and on the clipboard (G16)");
        Assert.AreEqual("Undo", notice.UndoLabel);
        await Eventually(() => TrayDesktop.Get(() => card.Size) == FileSize.Words(new FileInfo(item.FilePath).Length));
        TrayDesktop.Run(() =>
        {
            Assert.IsNotNull(card.Picture, "the picture");
            Assert.AreEqual(name, card.Field.Shown, "the name as a field");
        });
        desk.ChatKeepsTheKeystrokes("after the capture");

        await TrayDesktop.Get(() => desk.Notifications.Undo(notice));

        Assert.IsEmpty(desk.Items(), "Undo removes the capture");
        Assert.IsFalse(File.Exists(item.FilePath));
        var entries = RecycleBinEntries.Of(item.FilePath);
        Assert.HasCount(1, entries, "its file in the Recycle Bin (G29)");
        foreach (var entry in entries)
        {
            File.Delete(entry.Recycled);
            File.Delete(entry.Record);
        }
    }

    [TestMethod]
    public async Task WithAlsoCopyOffTheClipboardIsUntouchedAndTheNotificationSaysInTheStashOnly()
    {
        using var desk = await CaptureDesk.Open();
        using var pattern = PatternWindow.Show();
        await pattern.OnScreen(new WindowsScreen());
        desk.Shown.Settings.Change(settings => settings with { AlsoCopyCapture = false });
        var sequence = GetClipboardSequenceNumber();

        var taking = desk.Take();
        await desk.OverlayInFront();
        await CaptureDesk.Drag(pattern.Blocks[0].Block.Position + new PixelVector(20, 20), pattern.Blocks[0].Block.Position + new PixelVector(60, 50));
        await taking;

        var item = desk.Items().Single();
        Assert.AreEqual(sequence, GetClipboardSequenceNumber(), "the clipboard untouched (G14)");
        await desk.CardShown(item.Name);
        TrayDesktop.Run(() => Assert.AreEqual($"Saved “{item.Name}” to your stash.", desk.Notifications.Shown.Single().Text, "nothing of the clipboard (G16)"));
        desk.ChatKeepsTheKeystrokes("after the capture");
    }

    /// <summary>The items Recent lists, by identity, as the store answers.</summary>
    private static async Task<List<long>> RecentIds(CaptureDesk desk) =>
        (await TrayDesktop.Get(() => desk.Shown.Listing.Store!.Recent())).Value!.Select(item => item.Id).ToList();

    private const uint GaRoot = 2;

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern uint GetClipboardSequenceNumber();

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint WindowFromPoint(NativePoint point);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint GetAncestor(nint window, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct NativePoint(int X, int Y);
}
