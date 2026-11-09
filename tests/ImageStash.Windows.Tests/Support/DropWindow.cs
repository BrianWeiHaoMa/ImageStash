using System.Runtime.InteropServices;
using Avalonia.Input;
using Avalonia.Threading;
using ImageStash.Windows.Seams;

namespace ImageStash.Windows.Tests.Support;

/// <summary>
/// A small topmost window of the check's own under the mouse pointer, taking file drops, from which the seam's drag
/// starts. The system's drag asks the window under the pointer only on a mouse move whose message says the left
/// button is held, and then drops, or leaves a window that takes nothing, as it finds the button itself up; a move
/// saying no button is held ends it before any window is asked (probed on Windows 11 build 26300). So a mouse move
/// posted to the window holding the drag's capture, saying the button is held while no button is, drops it where the
/// pointer is: here, which is checked first, else the drag is cancelled with Esc and the check is inconclusive rather
/// than dropping files on another window.
/// </summary>
public sealed class DropWindow : IDisposable
{
    private const uint MouseMove = 0x0200;
    private const nint LeftButton = 0x0001;
    private const uint KeyDown = 0x0100;
    private const nint Escape = 0x1B;

    private readonly Avalonia.Controls.Window window;
    private readonly CursorPoint cursor;
    private readonly nint handle;
    private readonly uint uiThread;

    private DropWindow(Avalonia.Controls.Window window, CursorPoint cursor)
    {
        this.window = window;
        this.cursor = cursor;
        handle = window.TryGetPlatformHandle()!.Handle;
        uiThread = GetCurrentThreadId();
    }

    public bool Entered { get; private set; }

    public DragDropEffects? Offered { get; private set; }

    public List<string>? Dropped { get; private set; }

    public static async Task<DropWindow> UnderTheCursor(bool accept)
    {
        Assert.IsTrue(GetCursorPos(out var cursor));
        var shown = TrayDesktop.Get(() =>
        {
            var window = new Avalonia.Controls.Window
            {
                Topmost = true,
                ShowActivated = false,
                ShowInTaskbar = false,
                WindowDecorations = Avalonia.Controls.WindowDecorations.None,
                Width = 160,
                Height = 160,
                // Centred on the cursor: half its layout size, 80, scaled with the display.
                Position = new Avalonia.PixelPoint(cursor.X - DisplayPlaces.Scaled(DisplayPlaces.Primary(), 80), cursor.Y - DisplayPlaces.Scaled(DisplayPlaces.Primary(), 80)),
                Content = new Avalonia.Controls.Border { Background = Avalonia.Media.Brushes.White },
            };
            window.Show();
            var drop = new DropWindow(window, cursor);
            DragDrop.SetAllowDrop(window, true);
            window.AddHandler(DragDrop.DragEnterEvent, (_, e) =>
            {
                drop.Entered = true;
                drop.Offered = e.DragEffects;
                e.DragEffects = accept ? DragDropEffects.Copy : DragDropEffects.None;
            });
            window.AddHandler(DragDrop.DragOverEvent, (_, e) => e.DragEffects = accept ? DragDropEffects.Copy : DragDropEffects.None);
            window.AddHandler(DragDrop.DropEvent, (_, e) =>
            {
                using var contents = new WindowsClipboardAndDrops().ReadDrop(e.DataTransfer);
                drop.Dropped = contents.Items.Select(item => item.LocalPath!).ToList();
                e.DragEffects = accept ? DragDropEffects.Copy : DragDropEffects.None;
            });
            return drop;
        });
        await Task.Delay(300);
        if (!shown.InFront())
        {
            shown.Dispose();
            Assert.Inconclusive("Another window is in front at the mouse pointer, so no drag was started.");
        }

        return shown;
    }

    /// <summary>The seam's drag as the grid starts it, on the UI thread from a left-button press in the window, ended by <see cref="EndDrag"/>.</summary>
    public async Task<Outcome<bool>> Drag(IReadOnlyList<string> paths)
    {
        using var dragReturned = new CancellationTokenSource();
        var ending = EndDrag(dragReturned.Token);
        var dragged = await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var properties = new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed);
            var press = new PointerPressedEventArgs(window, new Avalonia.Input.Pointer(Avalonia.Input.Pointer.GetNextFreeId(), PointerType.Mouse, true), window, default, 0, properties, KeyModifiers.None, 1);
            return new WindowsClipboardAndDrops().StartDrag(window, press, paths);
        });
        dragReturned.Cancel();
        if (await ending)
        {
            Assert.Inconclusive("The mouse pointer left the check's window before the drop, so the drag was cancelled.");
        }

        return dragged;
    }

    public void Dispose() => TrayDesktop.Run(window.Close);

    /// <summary>
    /// Once the drag holds the mouse, drops it here by a posted mouse move, or cancels it with Esc when the pointer is
    /// no longer over this window; true when it cancelled. A drag that never starts is left alone.
    /// </summary>
    private async Task<bool> EndDrag(CancellationToken dragReturned)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!dragReturned.IsCancellationRequested)
        {
            var info = new GuiThreadInfo { Size = Marshal.SizeOf<GuiThreadInfo>() };
            if (GetGUIThreadInfo(uiThread, ref info) && info.Capture != 0 && info.Capture != handle)
            {
                var here = InFront();
                PostMessage(info.Capture, here ? MouseMove : KeyDown, here ? LeftButton : Escape, 0);
                return !here;
            }

            Assert.IsLessThan(deadline, DateTime.UtcNow, "the drag neither started nor returned");
            await Task.Delay(50, CancellationToken.None);
        }

        return false;
    }

    /// <summary>Whether the pointer is still where the window was put and this window is the one in front there.</summary>
    private bool InFront() =>
        GetCursorPos(out var now) && (now.X, now.Y) == (cursor.X, cursor.Y) && GetAncestor(WindowFromPoint(now), 2) == handle;

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool GetGUIThreadInfo(uint thread, ref GuiThreadInfo info);

    [DllImport("user32.dll", EntryPoint = "PostMessageW", ExactSpelling = true)]
    private static extern bool PostMessage(nint window, uint message, nint wParam, nint lParam);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool GetCursorPos(out CursorPoint point);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint WindowFromPoint(CursorPoint point);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint GetAncestor(nint window, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    private struct CursorPoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GuiThreadInfo
    {
        public int Size;
        public uint Flags;
        public nint Active;
        public nint Focus;
        public nint Capture;
        public nint MenuOwner;
        public nint MoveSize;
        public nint Caret;
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
