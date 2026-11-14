using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using ImageStash.Windows.Seams;
using ImageStash.Windows.Tests.Support;

namespace ImageStash.Windows.Tests.Seams;

/// <summary>
/// The window standing just in front of a window of ImageStash's, past the windows it owns, another of ImageStash's
/// counting, or none; the window put back behind it once minimised from hidden and shown afresh, as a capture's end
/// shows the pinned window (G3), or, that window gone meanwhile, behind the window in front now. The windows stand off
/// every display and are never activated, the order arranged and read directly, but for the last check's chat, brought
/// in front by a real click.
/// </summary>
[TestClass]
public sealed class WindowsWindowOrderTests
{
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;

    [TestMethod]
    public void TheWindowInFrontIsFoundPastTheWindowsItOwns()
    {
        using var other = TargetApp.Start("ImageStash check in front", new PixelPoint(-8000, 400), editBox: false);
        var ours = TrayDesktop.Get(() => Shown());
        var owned = TrayDesktop.Get(() => Shown(ours));
        try
        {
            ToFront(Handle(ours));
            ToFront(other.Window);
            Assert.IsTrue(FrontWindow.StandsInFrontOf(Handle(owned), Handle(ours)) && FrontWindow.StandsInFrontOf(other.Window, Handle(owned)), "the window it owns between it and the other app's");

            Assert.AreEqual(other.Window, TrayDesktop.Get(() => new WindowsWindowOrder().InFrontOf(ours)), "the other app's window, not the one it owns");
        }
        finally
        {
            TrayDesktop.Run(() => { owned.Close(); ours.Close(); });
        }
    }

    [TestMethod]
    public void AnotherWindowOfImageStashsInFrontIsTheOneInFront()
    {
        var (ours, page) = TrayDesktop.Get(() => (Shown(), Shown()));
        try
        {
            ToFront(Handle(ours));
            ToFront(Handle(page));

            Assert.AreEqual(Handle(page), TrayDesktop.Get(() => new WindowsWindowOrder().InFrontOf(ours)), "as the Settings page, which it does not own");
        }
        finally
        {
            TrayDesktop.Run(() => { page.Close(); ours.Close(); });
        }
    }

    [TestMethod]
    public void AWindowInFrontOfEveryOtherWindowHasNoneInFront()
    {
        using var other = TargetApp.Start("ImageStash check behind", new PixelPoint(-8000, 400), editBox: false);
        var ours = TrayDesktop.Get(() => Shown());
        try
        {
            ToFront(other.Window);
            ToFront(Handle(ours));

            Assert.IsNull(TrayDesktop.Get(() => new WindowsWindowOrder().InFrontOf(ours)), "only topmost windows stand in front of it");
        }
        finally
        {
            TrayDesktop.Run(ours.Close);
        }
    }

    [TestMethod]
    public void ShownAfreshAfterAMinimiseFromHiddenTheWindowIsPutBackBehindTheOneThatStoodInFront()
    {
        using var other = TargetApp.Start("ImageStash check in front", new PixelPoint(-8000, 400), editBox: false);
        var ours = TrayDesktop.Get(() => Shown());
        try
        {
            ToFront(Handle(ours));
            ToFront(other.Window);
            var order = new WindowsWindowOrder();
            var inFront = TrayDesktop.Get(() => order.InFrontOf(ours));
            Assert.AreEqual(other.Window, inFront);

            MinimisedAndShownAfresh(ours);
            TrayDesktop.Run(() => order.PutBack(ours, inFront!.Value));

            Assert.IsTrue(FrontWindow.StandsInFrontOf(other.Window, Handle(ours)), "the other app's window in front of it again");
            Assert.AreEqual(other.Window, TrayDesktop.Get(() => order.InFrontOf(ours)), "directly");
        }
        finally
        {
            TrayDesktop.Run(ours.Close);
        }
    }

    [TestMethod]
    public void TheWindowThatStoodInFrontGoneMeanwhileTheWindowIsPutBackBehindTheWindowInFrontNow()
    {
        using var pointer = TestMouse.PutBackAfterwards();
        using var chat = TargetApp.Start("ImageStash check chat", DisplayPlaces.At(DisplayPlaces.Primary(), 100, 100), editBox: false);
        TargetApp? gone = TargetApp.Start("ImageStash check gone", new PixelPoint(-8000, 400), editBox: false);
        var ours = TrayDesktop.Get(() => Shown());
        try
        {
            ToFront(Handle(ours));
            ToFront(gone.Window);
            var order = new WindowsWindowOrder();
            var inFront = TrayDesktop.Get(() => order.InFrontOf(ours));
            Assert.AreEqual(gone.Window, inFront);

            chat.ClickTitleBar();
            gone.Dispose();
            gone = null;
            MinimisedAndShownAfresh(ours);
            TrayDesktop.Run(() => order.PutBack(ours, inFront!.Value));

            Assert.IsTrue(FrontWindow.StandsInFrontOf(chat.Window, Handle(ours)), "the chat, in front now, in front of it");
        }
        finally
        {
            gone?.Dispose();
            TrayDesktop.Run(ours.Close);
        }
    }

    /// <summary>A window of the check's own, as ImageStash's would be, shown off every display without activation, owned by <paramref name="owner"/> when given.</summary>
    private static Avalonia.Controls.Window Shown(Avalonia.Controls.Window? owner = null)
    {
        var window = new Avalonia.Controls.Window { Width = 300, Height = 200, ShowActivated = false, Position = new PixelPoint(-8000, 100) };
        if (owner is null)
        {
            window.Show();
        }
        else
        {
            window.Show(owner);
        }

        return window;
    }

    /// <summary>As CaptureReturn minimises the pinned window from hidden at a capture's start and shows it afresh at its end.</summary>
    private static void MinimisedAndShownAfresh(Avalonia.Controls.Window window)
    {
        TrayDesktop.Run(() =>
        {
            window.Hide();
            window.WindowState = WindowState.Minimized;
            window.Show();
        });
        TrayDesktop.Run(() =>
        {
            window.Hide();
            window.WindowState = WindowState.Normal;
            window.Show();
        });
    }

    private static nint Handle(Avalonia.Controls.Window window) => TrayDesktop.Get(() => window.TryGetPlatformHandle()!.Handle);

    /// <summary>Puts <paramref name="window"/> in front of every window that is not topmost, activating nothing.</summary>
    private static void ToFront(nint window) => Assert.IsTrue(SetWindowPos(window, 0, 0, 0, 0, 0, SwpNoSize | SwpNoMove | SwpNoActivate));

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint window, nint insertAfter, int x, int y, int width, int height, uint flags);
}
