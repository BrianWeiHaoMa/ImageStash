using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ImageStash.Media;
using ImageStash.Seams;

namespace ImageStash.Windows.Tests.Support;

/// <summary>
/// A window of the check's own painting flat colour blocks at a known place on the desktop, in physical pixels, so a
/// check reading the screen's pixels knows what each one should be: topmost, never activated, without a frame or a
/// taskbar button. The pattern is four blocks, two across and two down, each colour with its three channels apart, so
/// a swapped channel or a flipped row shows; a flat window is one block of one colour. Colours are BGRA read as one
/// little-endian number, 0xAARRGGBB.
/// </summary>
public sealed class PatternWindow : IDisposable
{
    /// <summary>The four blocks' colours: top left, top right, bottom left, bottom right.</summary>
    public static readonly uint[] PatternColours = [0xFFC8280A, 0xFF0AB43C, 0xFF1E3CDC, 0xFFF0C814];

    /// <summary>Where the pattern goes unless a check says otherwise: on the primary display, clear of the check's other windows.</summary>
    public static readonly PixelRect Place = new(560, 160, 240, 160);

    // A block's edge, and the window's, may be drawn blended or rounded: a pixel this close to one is not judged.
    private const int Margin = 3;

    private const uint WsExNoActivate = 0x08000000;

    private PatternWindow(Avalonia.Controls.Window window, IReadOnlyList<(PixelRect Block, uint Colour)> blocks, PixelRect bounds)
    {
        Window = window;
        Blocks = blocks;
        Bounds = bounds;
    }

    public Avalonia.Controls.Window Window { get; }

    /// <summary>Each block with its colour, in physical pixels on the desktop.</summary>
    public IReadOnlyList<(PixelRect Block, uint Colour)> Blocks { get; }

    public PixelRect Bounds { get; }

    /// <summary>
    /// Shows the four-block pattern over <paramref name="bounds"/> (<see cref="Place"/> by default), first running
    /// <paramref name="beforeShowing"/> on the window, whose handle then exists.
    /// </summary>
    public static PatternWindow Show(PixelRect? bounds = null, Action<Avalonia.Controls.Window>? beforeShowing = null)
    {
        var at = bounds ?? Place;
        var (left, top) = (at.Width / 2, at.Height / 2);
        return ShowBlocks(at, beforeShowing,
        [
            (new PixelRect(at.X, at.Y, left, top), PatternColours[0]),
            (new PixelRect(at.X + left, at.Y, at.Width - left, top), PatternColours[1]),
            (new PixelRect(at.X, at.Y + top, left, at.Height - top), PatternColours[2]),
            (new PixelRect(at.X + left, at.Y + top, at.Width - left, at.Height - top), PatternColours[3]),
        ]);
    }

    /// <summary>Shows one block of <paramref name="colour"/> over <paramref name="bounds"/>, first running <paramref name="beforeShowing"/> on the window.</summary>
    public static PatternWindow Flat(PixelRect bounds, uint colour, Action<Avalonia.Controls.Window>? beforeShowing = null) =>
        ShowBlocks(bounds, beforeShowing, [(bounds, colour)]);

    /// <summary>The colour the window paints at <paramref name="point"/> on the desktop; null outside it or near an edge.</summary>
    public uint? Expected(PixelPoint point) =>
        Blocks.FirstOrDefault(b => new PixelRect(b.Block.X + Margin, b.Block.Y + Margin, b.Block.Width - 2 * Margin, b.Block.Height - 2 * Margin).Contains(point)) is { Block.Width: > 0 } found
            ? found.Colour
            : null;

    /// <summary>The colour of a frame's pixel, as BGRA read as one little-endian number.</summary>
    public static uint ColourAt(MediaFrame frame, int x, int y) => BitConverter.ToUInt32(frame.Pixels, (y * frame.Width + x) * 4);

    /// <summary>
    /// Every pixel of <paramref name="grab"/>, taken of <paramref name="grabbed"/> on the desktop, where this window's
    /// colour is known and differs from it: none while the window shows there; "x, y: found, expected" each.
    /// </summary>
    public List<string> Differences(MediaFrame grab, PixelRect grabbed)
    {
        if ((grab.Width, grab.Height) != (grabbed.Width, grabbed.Height))
        {
            return [$"the grab is {grab.Width} × {grab.Height}, the area {grabbed.Width} × {grabbed.Height}"];
        }

        if (!Enumerable.Range(0, grabbed.Width * grabbed.Height).Any(i => Expected(new PixelPoint(grabbed.X + i % grabbed.Width, grabbed.Y + i / grabbed.Width)) is not null))
        {
            return [$"no pixel of {grabbed} is one this window paints"];
        }

        var differences = new List<string>();
        for (var y = 0; y < grab.Height; y++)
        {
            for (var x = 0; x < grab.Width; x++)
            {
                if (Expected(new PixelPoint(grabbed.X + x, grabbed.Y + y)) is { } expected && ColourAt(grab, x, y) != expected)
                {
                    differences.Add($"{grabbed.X + x}, {grabbed.Y + y}: {ColourAt(grab, x, y):X8}, {expected:X8}");
                }
            }
        }

        return differences;
    }

    /// <summary>Waits until a grab through <paramref name="screen"/> shows the whole window, so it is drawn and composed.</summary>
    public async Task OnScreen(IScreen screen)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (screen.Grab(Bounds, withPointer: false) is var grab && (!grab.Succeeded || Differences(grab.Value!, Bounds) is { Count: > 0 }))
        {
            var differences = grab.Succeeded ? Differences(grab.Value!, Bounds) : [grab.Failure.Sentence];
            Assert.IsLessThan(deadline, DateTime.UtcNow, $"the window at {Bounds} is not on the screen: {differences.Count} pixels differ, as found and expected: {string.Join("; ", differences.Take(5))}");
            await Task.Delay(50);
        }
    }

    public void Dispose() => TrayDesktop.Run(Window.Close);

    private static PatternWindow ShowBlocks(PixelRect bounds, Action<Avalonia.Controls.Window>? beforeShowing, (PixelRect Block, uint Colour)[] blocks) => TrayDesktop.Get(() =>
    {
        var window = new Avalonia.Controls.Window
        {
            Title = "ImageStash check pattern",
            Topmost = true,
            ShowActivated = false,
            ShowInTaskbar = false,
            CanResize = false,
            WindowDecorations = WindowDecorations.None,
            Position = bounds.Position,
        };
        // Sized in device-independent units by the scaling of the display it lies on, so it covers exactly these physical pixels.
        var scaling = window.Screens.ScreenFromPoint(bounds.Position)?.Scaling ?? 1;
        var canvas = new Canvas();
        foreach (var (block, colour) in blocks)
        {
            var fill = new Border { Background = new SolidColorBrush(Color.FromUInt32(colour)), Width = block.Width / scaling, Height = block.Height / scaling };
            Canvas.SetLeft(fill, (block.X - bounds.X) / scaling);
            Canvas.SetTop(fill, (block.Y - bounds.Y) / scaling);
            canvas.Children.Add(fill);
        }

        window.Content = canvas;
        window.Width = bounds.Width / scaling;
        window.Height = bounds.Height / scaling;
        // Never activated means a window Windows cannot activate either: closing the overlay of two displays, Windows hands
        // the activation to the topmost window beneath it, which would take the chat's keystrokes.
        Win32Properties.AddWindowStylesCallback(window, (style, extended) => (style, extended | WsExNoActivate));
        beforeShowing?.Invoke(window);
        window.Show();
        return new PatternWindow(window, blocks, bounds);
    });
}
