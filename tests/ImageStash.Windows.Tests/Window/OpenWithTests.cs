using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using ImageStash.Files;
using ImageStash.Store;
using ImageStash.Window;
using ImageStash.Windows.Seams;
using ImageStash.Windows.Tests.Editor;
using ImageStash.Windows.Tests.Support;
using SkiaSharp;
using static ImageStash.Windows.Tests.Support.ShownWindow;

namespace ImageStash.Windows.Tests.Window;

/// <summary>
/// Open with… in the real stash window, on the shell's own dialog (G27, S16, F4): chosen from the item menu or the
/// preview's ⋯, the dialog opens modal to the window on the item's copy, and the flyout stays while it is up; once it
/// has closed, the flyout hides when focus went to another window, which closes the dialog by itself, and stays when
/// the dialog closed back to it (G2), and the pinned window stays either way; with two of its dialogs up, the flyout
/// stays until the last has closed. The copy rewritten by a program shows in the tile's pixels, drawn and displayed, at
/// its next showing, the notification above the tray says so, and its Undo, clicked, shows the previous picture again
/// with the rewrite in the Recycle Bin (G27, Product); the preview open meanwhile shows the rewrite too. These checks
/// open the shell's Open-with dialog and close it as a cancel, end an OpenWith.exe host it leaves running, click a chat
/// window of their own, read the screen's pixels, and remove from the Recycle Bin the one picture an Undo sends there.
/// </summary>
[TestClass]
public sealed class OpenWithTests
{
    private static readonly SKColor Before = new(200, 40, 40);
    private static readonly SKColor Rewritten = new(30, 90, 210);

    [TestMethod]
    public async Task TheFlyoutStaysWhileTheDialogIsUpAndHidesOnceItHasClosedWithFocusInAnotherWindow()
    {
        using var pointer = TestMouse.PutBackAfterwards();
        using var shown = ShownWindow.Open();
        using var dialogs = new OpenWithDialog();
        using var chat = TargetApp.Start("ImageStash check chat", DisplayPlaces.At(DisplayPlaces.Primary(), 40, 40));
        var item = (await shown.OpenStash(Flat(shown, "open with hiding.png", Before))).Single();
        await FlyoutInFront(shown);

        var dialog = await OpenWithFromTheMenu(shown, item);
        // The shell's dialog closes itself, as a cancel, once another window comes to the front (observed on Windows 11
        // build 26300); one that stays up is closed here, so either way the call returns with focus in the chat.
        chat.ClickTitleBar();
        await Task.Delay(500);
        if (OpenWithDialog.Up(dialog))
        {
            TrayDesktop.Run(() => Assert.IsTrue(shown.Window.IsVisible && shown.Window.DialogUp, "the flyout stays while its dialog is up, focus elsewhere or not (G2)"));
            await OpenWithDialog.Close(dialog);
        }

        await Eventually(() => TrayDesktop.Get(() => !shown.Window.DialogUp && !shown.Window.IsVisible));
        Assert.AreEqual(chat.Window, FrontWindow.GetForegroundWindow(), "the window the person went to keeps the foreground");
    }

    [TestMethod]
    public async Task TheFlyoutStaysWhenTheDialogClosesBackToItAndARewriteShowsInTheTileWhoseNotificationsUndoShowsThePreviousPicture()
    {
        using var pointer = TestMouse.PutBackAfterwards();
        using var shown = ShownWindow.Open();
        using var dialogs = new OpenWithDialog();
        using var tray = new WindowsTray();
        var notification = AboveTheTray(shown, tray);
        var item = (await shown.OpenStash(Flat(shown, "open with tile.png", Before))).Single();
        await FlyoutInFront(shown);
        await TileShows(shown, item.Id, Before);
        try
        {
            var dialog = await OpenWithFromTheMenu(shown, item);
            await OpenWithDialog.Close(dialog);
            await Eventually(() => TrayDesktop.Get(() => !shown.Window.DialogUp));
            await Task.Delay(500);
            TrayDesktop.Run(() => Assert.IsTrue(shown.Window.IsVisible && shown.Window.IsActive, "the dialog closed back to the flyout, which stays (G2)"));

            var rewrite = File.ReadAllBytes(Flat(shown, "open with tile rewrite.png", Rewritten));
            await RewriteWhenFree(CopyOf(shown, item.Id), rewrite);
            var words = $"Replaced “{item.Name}” with the picture another program saved.";
            await Eventually(() => TrayDesktop.Get(() => notification.Shown?.Title == words));
            await TileShows(shown, item.Id, Rewritten);

            var undo = TrayDesktop.Get(() => notification.Shown!.GetVisualDescendants().OfType<Button>().Single(button => button.Content as string == "Undo"));
            TestMouse.Click(TestMouse.Centre(undo));
            await Eventually(() => TrayDesktop.Get(() => notification.Shown is null));
            await TileShows(shown, item.Id, Before);
            var root = shown.Listing.Store!.Folder.Root;
            await Eventually(() => RecycleBinEntries.Within(root).Count > 0);
            var recycled = RecycleBinEntries.Within(root);
            try
            {
                Assert.HasCount(1, recycled, "one picture in the Recycle Bin");
                CollectionAssert.AreEqual(rewrite, File.ReadAllBytes(recycled[0].Recycled), "the outside version (G27)");
            }
            finally
            {
                foreach (var entry in recycled)
                {
                    File.Delete(entry.Recycled);
                    File.Delete(entry.Record);
                }
            }
        }
        finally
        {
            // A check that failed with the notification up takes it down without its expiry, which would recycle the previous original.
            TrayDesktop.Run(() =>
            {
                foreach (var notice in shown.Notifications.Shown.ToList())
                {
                    shown.Notifications.Dismiss(notice);
                }
            });
        }
    }

    [TestMethod]
    public async Task ThePinnedWindowStaysWhetherTheDialogClosesWithFocusInAnotherWindowOrBackToIt()
    {
        using var pointer = TestMouse.PutBackAfterwards();
        using var shown = ShownWindow.Open();
        using var dialogs = new OpenWithDialog();
        using var chat = TargetApp.Start("ImageStash check chat", DisplayPlaces.At(DisplayPlaces.Primary(), 40, 40));
        var item = (await shown.OpenStash(Flat(shown, "open with pinned.png", Before))).Single();
        shown.Settings.Change(settings => settings with { PinnedPosition = new WindowPosition(DisplayPlaces.Scaled(DisplayPlaces.Primary(), 700), DisplayPlaces.Scaled(DisplayPlaces.Primary(), 40)), PinnedSize = new WindowSize(540, 560) });
        TrayDesktop.Run(shown.Window.SummonPinned);
        await Task.Delay(300);
        await ClickToFront(shown);

        var dialog = await OpenWithFromTheMenu(shown, item);
        chat.ClickTitleBar();
        if (OpenWithDialog.Up(dialog))
        {
            await OpenWithDialog.Close(dialog);
        }

        await Eventually(() => TrayDesktop.Get(() => !shown.Window.DialogUp));
        await Task.Delay(500);
        TrayDesktop.Run(() => Assert.IsTrue(shown.Window.IsVisible, "pinned, it stays with focus in another window (G2)"));

        await ClickToFront(shown);
        dialog = await OpenWithFromTheMenu(shown, item);
        await OpenWithDialog.Close(dialog);
        await Eventually(() => TrayDesktop.Get(() => !shown.Window.DialogUp));
        await Task.Delay(500);
        TrayDesktop.Run(() => Assert.IsTrue(shown.Window.IsVisible, "and with the dialog closed back to it"));
    }

    [TestMethod]
    public async Task ThePreviewsMenuOpensTheDialogWithThePreviewStillOpenWhichShowsTheRewriteOnceAdopted()
    {
        using var pointer = TestMouse.PutBackAfterwards();
        using var shown = ShownWindow.Open();
        using var dialogs = new OpenWithDialog();
        var item = (await shown.OpenStash(Flat(shown, "open with preview.png", Before))).Single();
        await FlyoutInFront(shown);
        TrayDesktop.Run(() => shown.OpenPreview(item));
        await PreviewShows(shown, Before);

        TrayDesktop.Run(() =>
        {
            CollectionAssert.Contains(shown.ClickMore(), "Open with…");
            shown.ClickMenuEntry("Open with…");
        });
        var dialog = await DialogInFront(shown);
        TrayDesktop.Run(() => Assert.IsTrue(shown.Preview.IsOpen && shown.Preview.Item?.Id == item.Id, "the preview still open on its item (G19)"));
        await OpenWithDialog.Close(dialog);
        await Eventually(() => TrayDesktop.Get(() => !shown.Window.DialogUp));

        await RewriteWhenFree(CopyOf(shown, item.Id), File.ReadAllBytes(Flat(shown, "open with preview rewrite.png", Rewritten)));
        await PreviewShows(shown, Rewritten);
        TrayDesktop.Run(() => Assert.IsTrue(shown.Preview.IsOpen && shown.Preview.Item?.Id == item.Id, "still on its item"));
    }

    [TestMethod]
    public async Task WithTwoOfItsDialogsUpTheFlyoutStaysUntilTheLastHasClosed()
    {
        // Open with… asked again while the first copy is still being made (its picture held, retried as G35 says) puts a
        // second dialog up beside the first; the check's own dialogs stay up until it closes them.
        var dialogs = new PresetDialogs();
        var answers = new List<TaskCompletionSource<Outcome>>();
        dialogs.OpenWithAnswer = () =>
        {
            var answer = new TaskCompletionSource<Outcome>(TaskCreationOptions.RunContinuationsAsynchronously);
            answers.Add(answer);
            return answer.Task;
        };
        using var shown = ShownWindow.Open(dialogs: dialogs);
        var item = (await shown.OpenStash(Flat(shown, "open with twice.png", Before))).Single();
        foreach (var asked in new[] { 1, 2 })
        {
            TrayDesktop.Run(() =>
            {
                shown.RightClick(item.Id);
                shown.ClickMenuEntry("Open with…");
            });
            await Eventually(() => TrayDesktop.Get(() => answers.Count == asked));
        }

        TrayDesktop.Run(() => answers[0].SetResult(Outcome.Done));
        await Task.Delay(500);
        // Focus is with the dialog still up, another program's window.
        TrayDesktop.Run(shown.Deactivate);
        await Task.Delay(500);
        TrayDesktop.Run(() =>
        {
            Assert.IsTrue(shown.Window.DialogUp, "a dialog it opened is still up");
            Assert.IsTrue(shown.Window.IsVisible, "so the flyout stays (G2)");
        });

        TrayDesktop.Run(() => answers[1].SetResult(Outcome.Done));
        await Eventually(() => TrayDesktop.Get(() => !shown.Window.DialogUp && !shown.Window.IsVisible));
    }

    /// <summary>A flat picture of <paramref name="color"/>, large enough for its tile and the preview to show it, in the check's folder outside the stash.</summary>
    private static string Flat(ShownWindow shown, string name, SKColor color) => shown.Picture(name, 160, 120, (_, _) => color);

    /// <summary>
    /// The flyout at the top right of the primary display at its minimum height, clear of the notification above the tray
    /// and of the chat at the top left, clicked to the front as a person brings it there before choosing from its menu.
    /// </summary>
    private static async Task FlyoutInFront(ShownWindow shown)
    {
        TrayDesktop.Run(() =>
        {
            var area = shown.Window.Screens.Primary!.WorkingArea;
            shown.Window.Height = WindowSize.Minimum.Height;
            shown.Window.Position = new PixelPoint(area.Right - (int)(shown.Window.Width * shown.Window.RenderScaling) - 20, area.Y + 20);
        });
        await Task.Delay(300);
        await ClickToFront(shown);
    }

    /// <summary>A real click on the search box, then until the stash window is in front and active.</summary>
    private static async Task ClickToFront(ShownWindow shown)
    {
        var handle = TrayDesktop.Get(() => shown.Window.TryGetPlatformHandle()!.Handle);
        TestMouse.Click(TestMouse.Centre(TrayDesktop.Get(() => shown.Search)));
        await Eventually(() => FrontWindow.GetForegroundWindow() == handle && TrayDesktop.Get(() => shown.Window.IsActive));
    }

    /// <summary>Open with… chosen in the item's menu: the dialog, once in front.</summary>
    private static async Task<nint> OpenWithFromTheMenu(ShownWindow shown, StashItem item)
    {
        TrayDesktop.Run(() =>
        {
            CollectionAssert.Contains(shown.RightClick(item.Id), "Open with…");
            shown.ClickMenuEntry("Open with…");
        });
        return await DialogInFront(shown);
    }

    /// <summary>
    /// The shell's dialog in front, modal to the stash window, which has lost the foreground to it and, once that loss has
    /// been judged, still shows with its dialog flag up (G2, S16).
    /// </summary>
    private static async Task<nint> DialogInFront(ShownWindow shown)
    {
        var dialog = await OpenWithDialog.InFront();
        Assert.AreEqual("OpenWith", OpenWithDialog.ProcessOf(dialog), "the shell's own dialog (S16)");
        Assert.IsFalse(OpenWithDialog.TakesInput(TrayDesktop.Get(() => shown.Window.TryGetPlatformHandle()!.Handle)), "modal to the stash window");
        await Task.Delay(500);
        TrayDesktop.Run(() =>
        {
            Assert.IsTrue(shown.Window.DialogUp, "the window's dialog flag up while the dialog is");
            Assert.IsFalse(shown.Window.IsActive, "the window lost the foreground to the dialog");
            Assert.IsTrue(shown.Window.IsVisible, "and stays while its dialog is up (G2)");
        });
        return dialog;
    }

    /// <summary>The notifications above the tray the window's Open with… raises, shown as ImageStash shows and wires them.</summary>
    private static Notification AboveTheTray(ShownWindow shown, WindowsTray tray) => TrayDesktop.Get(() =>
    {
        var quiet = new WindowsQuietWindows(tray);
        var notification = new Notification(shown.Notifications, quiet, new WindowsScreen(), _ => { });
        shown.Window.Follow(notification, new CaptureNotification(shown.Notifications, shown.Listing, shown.Handoff, quiet, HeldFileRetry.Duration));
        return notification;
    });

    /// <summary>The path of the item's Open with… copy, as Store records its session.</summary>
    private static string CopyOf(ShownWindow shown, long id) =>
        shown.Listing.Store!.OpenWithSessions().GetAwaiter().GetResult().Value!.Sessions.Single(session => session.Id == id).CopyPath;

    /// <summary>
    /// Writes <paramref name="bytes"/> over the copy as a program saving in place does, tried again for up to five seconds
    /// while the window's classification holds it without sharing for a moment, as a program saving again would.
    /// </summary>
    private static async Task RewriteWhenFree(string copy, byte[] bytes)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (true)
        {
            try
            {
                File.WriteAllBytes(copy, bytes);
                return;
            }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                await Task.Delay(10);
            }
        }
    }

    /// <summary>
    /// Waits up to ten seconds for the centre of the item's tile to be <paramref name="color"/>, both as the window draws
    /// it and as the desktop displays it there.
    /// </summary>
    private static async Task TileShows(ShownWindow shown, long id, SKColor color)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (true)
        {
            TrayDesktop.Run(() => Assert.IsTrue(shown.Window.IsVisible, "the window shows"));
            var rendered = await RenderedWindow.Of(shown);
            var (tile, onScreen) = TrayDesktop.Get(() => (EditingInTheWindowTests.Tile(shown, id), shown.TileOnScreen(id)));
            var (drawn, displayed) = (rendered.At((int)tile.Center.X, (int)tile.Center.Y), Displayed(onScreen));
            if (drawn == (uint)color && displayed == (uint)color)
            {
                return;
            }

            Assert.IsLessThan(deadline, DateTime.UtcNow, $"the tile is drawn {drawn:X8} and displayed {displayed:X8}, not {(uint)color:X8}");
        }
    }

    /// <summary>
    /// Waits up to ten seconds for the centre of the preview's picture to be <paramref name="color"/>, both as the window
    /// draws it and as the desktop displays it there.
    /// </summary>
    private static async Task PreviewShows(ShownWindow shown, SKColor color)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (true)
        {
            TrayDesktop.Run(() => Assert.IsTrue(shown.Window.IsVisible, "the window shows"));
            var rendered = await RenderedWindow.Of(shown);
            var corner = TrayDesktop.Get(() => shown.PreviewPicture.PointToScreen(default));
            var (centre, area) = (rendered.Picture.Center, rendered.Area);
            var drawn = rendered.Picture.Width > 0 ? rendered.At((int)centre.X, (int)centre.Y) : 0;
            var displayed = rendered.Picture.Width > 0 ? Displayed(new PixelPoint(corner.X + (int)(centre.X - area.X), corner.Y + (int)(centre.Y - area.Y))) : 0;
            if (drawn == (uint)color && displayed == (uint)color)
            {
                return;
            }

            Assert.IsLessThan(deadline, DateTime.UtcNow, $"the preview is drawn {drawn:X8} and displayed {displayed:X8}, not {(uint)color:X8}");
        }
    }

    /// <summary>The colour the desktop displays at <paramref name="at"/>, in physical pixels, grabbed as the Screen seam grabs it.</summary>
    private static uint Displayed(PixelPoint at)
    {
        var grab = new WindowsScreen().Grab(new PixelRect(at, new PixelSize(1, 1)), withPointer: false);
        Assert.IsTrue(grab.Succeeded, grab.Failure?.Sentence);
        return PatternWindow.ColourAt(grab.Value!, 0, 0);
    }
}
