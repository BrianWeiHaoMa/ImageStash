using System.Diagnostics;
using Avalonia.Input;
using Avalonia.Threading;
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
/// Stash clipboard image over the real clipboard seam and the signed-in user's real clipboard, into a stash of the
/// check's own beside a real stash window (G13): a PNG, a bitmap a program placed, a GDI bitmap and a PNG beside a file
/// list each saved as one still PNG item named like a screenshot, the file list untouched, and announced above the tray
/// (F3, F12, G16); the window's query and an open preview left as they were; files alone, words, a web address and an
/// emptied clipboard each no image, nothing added (N2); a clipboard another program holds waited on while it is let go
/// within the retry and named past it (E3, G35); and a real press of its shortcut, with another program's window in
/// front, saving the image and announcing it while that window keeps the foreground and the keystrokes (F5, G33).
/// </summary>
[TestClass]
public sealed class StashClipboardImageTests
{
    private const string StillOnly = "A pasted picture is always a still; to add an animated one, drag its file in.";

    [TestMethod]
    public async Task EachImageTheClipboardOffersBecomesOneStillPngItemAndIsAnnouncedAboveTheTray()
    {
        using var shown = Open();
        await shown.OpenStash();
        var windowNotices = TrayDesktop.Get(() => shown.Notices.Shown.Count);
        var clock = new StoppedClock();
        var act = Act(shown, clock);
        var name = AutoName.Screenshot(clock.GetLocalNow());
        var png = File.ReadAllBytes(shown.Picture("stash copied.png"));
        var listed = shown.Picture("stash listed.png");
        var listedBytes = File.ReadAllBytes(listed);
        // ImageStash's own CF_DIBV5 of 2×1: an opaque red pixel and a half-transparent blue one, bottom-up.
        var dib = Dib([0x00, 0x00, 0xFF, 0xFF, 0xFF, 0x00, 0x00, 0x80]);
        byte[] gdi = [0xD3, 0xD3, 0xD3, 0x00, 0xE9, 0x69, 0x69, 0x80, 0x00, 0xFF, 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF];

        TestClipboard.Put((TestClipboard.Png, png));
        await Stash(act);
        TestClipboard.Put((TestClipboard.CfDibV5, dib));
        await Stash(act);
        TestClipboard.PutBitmap(2, 2, gdi);
        await Stash(act);
        TestClipboard.Put(TestClipboard.FileList(listed), (TestClipboard.Png, png));
        await Stash(act);

        var items = TrayDesktop.Get(() => shown.Listing.Store!.All()).GetAwaiter().GetResult().Value!.OrderBy(item => item.Id).ToList();
        CollectionAssert.AreEqual(new[] { name + ".png", name + " 2.png", name + " 3.png", name + " 4.png" }, items.Select(item => item.FileName).ToList(), "one still PNG item per act, the files beside the last not imported (G13)");
        Assert.IsTrue(items.All(item => item.Name == name && item.Facts.Format == ImageFormat.Png && !item.Facts.Animated && item.Tags.Count == 0));
        CollectionAssert.AreEqual(png, File.ReadAllBytes(items[0].FilePath), "the clipboard's PNG as it is, its transparency with it");
        Assert.AreEqual((6, 4), (items[0].Facts.Width, items[0].Facts.Height));
        CollectionAssert.AreEqual(new byte[] { 0x00, 0x00, 0xFF, 0xFF, 0xFF, 0x00, 0x00, 0x80 }, CaptureDesk.FirstFrame(items[1].FilePath).Pixels, "the bitmap encoded as PNG, its alpha kept (F3)");
        CollectionAssert.AreEqual(gdi, CaptureDesk.FirstFrame(items[2].FilePath).Pixels, "the GDI bitmap as Windows converts it");
        CollectionAssert.AreEqual(png, File.ReadAllBytes(items[3].FilePath), "the PNG beside the file list");
        CollectionAssert.AreEqual(listedBytes, File.ReadAllBytes(listed), "the listed file untouched");
        TrayDesktop.Run(() =>
        {
            CollectionAssert.AreEqual(Enumerable.Repeat($"Saved “{name}” to your stash. {StillOnly}", 4).ToList(), shown.Notifications.Shown.Select(notice => notice.Text).ToList());
            Assert.HasCount(windowNotices, shown.Notices.Shown, "nothing in the window, which the act does not open");
            Assert.IsFalse(shown.Window.IsActive, "the act opens nothing and activates nothing");
        });
    }

    [TestMethod]
    public async Task TheWindowsQueryAndItsOpenPreviewStayAsTheyWere()
    {
        using var shown = Open();
        var kept = (await shown.OpenStash(shown.Picture("stash kept.png"))).Single();
        var act = Act(shown, new StoppedClock());
        TrayDesktop.Run(() =>
        {
            shown.Search.Text = "kept";
            shown.OpenPreview(kept);
        });
        await Eventually(() => TrayDesktop.Get(() => shown.Listing.Query == "kept" && shown.Preview.Item?.Id == kept.Id));

        TestClipboard.Put((TestClipboard.Png, File.ReadAllBytes(shown.Picture("stash beside the preview.png"))));
        await Stash(act);

        TrayDesktop.Run(() =>
        {
            Assert.AreEqual(kept.Id, shown.Preview.Item?.Id, "the preview stays open on its item");
            Assert.AreEqual("kept", shown.Search.Text);
            Assert.AreEqual("kept", shown.Listing.Query, "a capture resets nothing of the window");
            CollectionAssert.AreEqual(new[] { kept.Id }, shown.Listing.Selection.ToList());
            Assert.StartsWith("Saved “Screenshot ", shown.Notifications.Shown.Single().Text);
        });
        Assert.HasCount(2, TrayDesktop.Get(() => shown.Listing.Store!.All()).GetAwaiter().GetResult().Value!);
    }

    [TestMethod]
    public async Task FilesAloneWordsAWebAddressAndAnEmptiedClipboardAreNoImageAndAddNothing()
    {
        using var shown = Open();
        await shown.OpenStash();
        var act = Act(shown, new StoppedClock());
        var listed = shown.Picture("stash files only.png");

        TestClipboard.Put(TestClipboard.FileList(listed));
        await Stash(act);
        TestClipboard.Put(TestClipboard.Text("pasted words"));
        await Stash(act);
        TestClipboard.Put(TestClipboard.Text("https://example.com/cat.gif"));
        await Stash(act);
        TestClipboard.Put();
        await Stash(act);

        TrayDesktop.Run(() => CollectionAssert.AreEqual(
            Enumerable.Repeat("Stash clipboard image failed: the clipboard, it holds no image", 4).ToList(),
            shown.Notifications.Shown.Select(notice => notice.Text).ToList(),
            "files do not count, and nothing is fetched (G13, N2)"));
        Assert.IsEmpty(TrayDesktop.Get(() => shown.Listing.Store!.All()).GetAwaiter().GetResult().Value!);
    }

    [TestMethod]
    public async Task AClipboardAnotherProgramHoldsIsWaitedOnWhileItIsLetGoAndNamedPastTheRetry()
    {
        using var shown = Open();
        await shown.OpenStash();
        var act = Act(shown, new StoppedClock());
        TestClipboard.Put((TestClipboard.Png, File.ReadAllBytes(shown.Picture("stash held.png"))));

        using (var holder = OtherProgramHoldingTheClipboard.Start())
        {
            var stashing = Stash(act);
            await Task.Delay(600);
            Assert.IsFalse(stashing.IsCompleted, "the act waits while the clipboard is held");
            holder.Release();
            await stashing;
        }

        Assert.HasCount(1, TrayDesktop.Get(() => shown.Listing.Store!.All()).GetAwaiter().GetResult().Value!, "saved once it was let go");

        var clock = Stopwatch.StartNew();
        using (var holder = OtherProgramHoldingTheClipboard.Start())
        {
            await Stash(act);
            clock.Stop();
            holder.Release();
        }

        Assert.IsGreaterThanOrEqualTo(1900, clock.ElapsedMilliseconds, "retried for HeldClipboard.Retry before it was named");
        TrayDesktop.Run(() => Assert.AreEqual("Stash clipboard image failed: the clipboard, another program is holding the clipboard open", shown.Notifications.Shown[^1].Text));
        Assert.HasCount(1, TrayDesktop.Get(() => shown.Listing.Store!.All()).GetAwaiter().GetResult().Value!, "nothing added");
    }

    [TestMethod]
    public async Task ARealPressOfItsShortcutWithAnotherAppInFrontAddsOneItemAndThatAppKeepsTheKeystrokes()
    {
        using var pointer = TestMouse.PutBackAfterwards();
        using var shown = Open();
        await shown.OpenStash();
        using var tray = new WindowsTray();
        var quiet = new WindowsQuietWindows(tray);
        var notification = TrayDesktop.Get(() => new Notification(shown.Notifications, quiet, new WindowsScreen(), _ => { }));
        var act = Act(shown, LocalClock.Instance, quiet);
        var png = File.ReadAllBytes(shown.Picture("stash pressed.png"));
        using var seam = new WindowsShortcuts();
        var named = new List<string>();
        var onUiThread = false;
        // As ImageStash binds its shortcuts, the act being the tray entry's.
        var shortcuts = TrayDesktop.Get(() => new GlobalShortcuts(
            SettingsInMemory.Defaults(),
            GlobalShortcuts.Bindings(() => { }, () => { }, () => { }, () =>
            {
                onUiThread = Dispatcher.UIThread.CheckAccess();
                _ = act.Stash();
            }),
            seam,
            failure => named.Add(failure.Sentence),
            action => Dispatcher.UIThread.Post(action)));
        TrayDesktop.Run(shortcuts.Start);
        Assert.IsEmpty(named, "all four registered; another program, or a running ImageStash, holds none");
        // Hidden, as it is while a person types in a chat: the act shows it nowhere.
        TrayDesktop.Run(shown.Window.Hide);
        using var chat = TargetApp.Start("ImageStash check chat", DisplayPlaces.At(DisplayPlaces.Primary(), 100, 100));
        TestClipboard.Put((TestClipboard.Png, png));
        chat.ClickTitleBar();
        try
        {
            var before = DateTimeOffset.Now;
            var clock = Stopwatch.StartNew();
            TestHotKeys.Press(Shortcut.Parse("Ctrl+Alt+V").Value!);
            await Eventually(() => TrayDesktop.Get(() => shown.Notifications.Shown.Count == 1 && notification.Shown is not null));
            var took = clock.ElapsedMilliseconds;
            var after = DateTimeOffset.Now;
            var typedBefore = chat.Messages().Count;
            TestHotKeys.Press(new Shortcut(new KeyGesture(Key.A), "A"));
            Thread.Sleep(300);

            Assert.IsLessThan(1000, took, "saved and announced within a second of the press");
            Assert.IsTrue(onUiThread, "the act runs on the UI thread");
            var item = TrayDesktop.Get(() => shown.Listing.Store!.All()).GetAwaiter().GetResult().Value!.Single();
            var atThePress = Enumerable.Range(0, (int)Math.Ceiling((after - before).TotalSeconds) + 1).Select(second => AutoName.Screenshot(before.AddSeconds(second))).ToList();
            CollectionAssert.Contains(atThePress, item.Name, "named by the local time at the press (F12)");
            Assert.AreEqual(ImageFormat.Png, item.Facts.Format);
            CollectionAssert.AreEqual(png, File.ReadAllBytes(item.FilePath), "the clipboard's PNG as it is");
            TrayDesktop.Run(() => Assert.AreEqual($"Saved “{item.Name}” to your stash. {StillOnly}", shown.Notifications.Shown.Single().Text));
            Assert.AreEqual(chat.Window, FrontWindow.GetForegroundWindow(), "nothing took the foreground (G13, G16)");
            CollectionAssert.Contains(chat.Messages().Skip(typedBefore).ToList(), "WM_KEYDOWN:0x41", "the letter typed after the press reaches the app in front");
            TrayDesktop.Run(() => Assert.IsFalse(shown.Window.IsVisible, "the act opens no window (G13)"));
        }
        finally
        {
            TrayDesktop.Run(() =>
            {
                foreach (var notice in shown.Notifications.Shown.ToList())
                {
                    shown.Notifications.Close(notice);
                }
            });
        }
    }

    /// <summary>
    /// The act as the product builds it, over the window's listing and notifications and the real seam, on the UI thread;
    /// its capture card activated through <paramref name="quietWindows"/>, the seam that shows its notification.
    /// </summary>
    internal static StashClipboardImage Act(ShownWindow shown, TimeProvider clock, IQuietWindows? quietWindows = null) => TrayDesktop.Get(() =>
        new StashClipboardImage(shown.Listing, shown.Notifications, new CaptureNotification(shown.Notifications, shown.Listing, shown.Handoff, quietWindows ?? new NoTrayIcon(), HeldFileRetry.Duration),
            new WindowsClipboardAndDrops(), clock, HeldClipboard.Retry, HeldFileRetry.Duration));

    /// <summary>The tray entry's act, started on the UI thread as the tray runs it, awaited here.</summary>
    private static Task Stash(StashClipboardImage act) => TrayDesktop.Get(act.Stash);

    /// <summary>A 32-bit CF_DIBV5 of one row, BI_BITFIELDS with an alpha mask, as ImageStash's own write places it.</summary>
    private static byte[] Dib(byte[] row)
    {
        var dib = new byte[124 + row.Length];
        BitConverter.GetBytes(124).CopyTo(dib, 0);
        BitConverter.GetBytes(row.Length / 4).CopyTo(dib, 4);
        BitConverter.GetBytes(1).CopyTo(dib, 8);
        BitConverter.GetBytes((ushort)1).CopyTo(dib, 12);
        BitConverter.GetBytes((ushort)32).CopyTo(dib, 14);
        BitConverter.GetBytes(3u).CopyTo(dib, 16);
        BitConverter.GetBytes((uint)row.Length).CopyTo(dib, 20);
        uint[] masks = [0x00FF0000, 0x0000FF00, 0x000000FF, 0xFF000000];
        for (var i = 0; i < 4; i++)
        {
            BitConverter.GetBytes(masks[i]).CopyTo(dib, 40 + i * 4);
        }

        BitConverter.GetBytes(0x73524742u).CopyTo(dib, 56);
        row.CopyTo(dib, 124);
        return dib;
    }
}
