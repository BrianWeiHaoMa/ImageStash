using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using ImageStash.Media;
using ImageStash.Seams;

namespace ImageStash.Windows.Tests.Support;

/// <summary>
/// A window of the check's own counting up every 100 ms from its showing, in flat colours a GIF keeps exactly, so a
/// check reads the count back from any picture of it: a row of eight cells across its top, each one bit of the count,
/// the highest on the left, red for one and blue for zero; beneath them a green field for the pointer, or a
/// notification, to stand over. Topmost, never activated, without a frame or a taskbar button, at a known place in
/// physical pixels. Colours are BGRA read as one little-endian number, 0xAARRGGBB.
/// </summary>
public sealed class CounterWindow : IDisposable
{
    public const uint One = 0xFFC8280A;
    public const uint Zero = 0xFF1E3CDC;
    public const uint Field = 0xFF0AB43C;

    /// <summary>How often the count goes up.</summary>
    public static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(100);

    private const int Bits = 8;
    private const int CellHeight = 40;

    // A field's edge, and the window's, may be drawn blended: a pixel this close to one is not judged.
    private const int Margin = 3;

    private const uint WsExNoActivate = 0x08000000;

    private readonly DispatcherTimer timer;

    private CounterWindow(Avalonia.Controls.Window window, DispatcherTimer timer, PixelRect bounds)
    {
        Window = window;
        this.timer = timer;
        Bounds = bounds;
    }

    public Avalonia.Controls.Window Window { get; }

    /// <summary>The window, in physical pixels on the desktop.</summary>
    public PixelRect Bounds { get; }

    /// <summary>The green field beneath the cells, in physical pixels on the desktop.</summary>
    public PixelRect FieldBounds => new(Bounds.X, Bounds.Y + CellHeight, Bounds.Width, Bounds.Height - CellHeight);

    /// <summary>Shows it over <paramref name="bounds"/>, counting from zero.</summary>
    public static CounterWindow Show(PixelRect bounds) => TrayDesktop.Get(() =>
    {
        var window = new Avalonia.Controls.Window
        {
            Title = "ImageStash check counter",
            Topmost = true,
            ShowActivated = false,
            ShowInTaskbar = false,
            CanResize = false,
            WindowDecorations = WindowDecorations.None,
            Position = bounds.Position,
        };
        // Sized in device-independent units by the scaling of the display it lies on, so it covers exactly these physical pixels.
        var scaling = window.Screens.ScreenFromPoint(bounds.Position)?.Scaling ?? 1;
        var canvas = new Canvas { Background = new SolidColorBrush(Color.FromUInt32(Field)) };
        var cells = new List<Border>();
        var cellWidth = bounds.Width / Bits;
        for (var bit = 0; bit < Bits; bit++)
        {
            var cell = new Border { Background = new SolidColorBrush(Color.FromUInt32(Zero)), Width = cellWidth / scaling, Height = CellHeight / scaling };
            Canvas.SetLeft(cell, bit * cellWidth / scaling);
            canvas.Children.Add(cell);
            cells.Add(cell);
        }

        window.Content = canvas;
        window.Width = bounds.Width / scaling;
        window.Height = bounds.Height / scaling;
        var count = 0;
        var timer = new DispatcherTimer(Tick, DispatcherPriority.Render, (_, _) =>
        {
            count = (count + 1) % (1 << Bits);
            for (var bit = 0; bit < Bits; bit++)
            {
                var set = (count >> (Bits - 1 - bit) & 1) == 1;
                cells[bit].Background = new SolidColorBrush(Color.FromUInt32(set ? One : Zero));
            }
        });
        // Never activated means a window Windows cannot activate either: closing the overlay of two displays, Windows hands
        // the activation to the topmost window beneath it, which would take the chat's keystrokes.
        Win32Properties.AddWindowStylesCallback(window, (style, extended) => (style, extended | WsExNoActivate));
        window.Show();
        timer.Start();
        return new CounterWindow(window, timer, bounds);
    });

    /// <summary>
    /// The count a picture of <paramref name="pictured"/>, an area of the desktop holding the cells, shows: read from each
    /// cell's middle; null when a cell there is neither colour, as a torn or blended picture would show it.
    /// </summary>
    public int? CountIn(MediaFrame picture, PixelRect pictured)
    {
        var cellWidth = Bounds.Width / Bits;
        var count = 0;
        for (var bit = 0; bit < Bits; bit++)
        {
            var colour = PatternWindow.ColourAt(picture, Bounds.X + bit * cellWidth + cellWidth / 2 - pictured.X, Bounds.Y + CellHeight / 2 - pictured.Y);
            if (colour is not (One or Zero))
            {
                return null;
            }

            count = count << 1 | (colour == One ? 1 : 0);
        }

        return count;
    }

    /// <summary>
    /// Every pixel of a picture of <paramref name="pictured"/> inside <paramref name="area"/> of the field, away from its
    /// edges, that is not the field's green: "x, y: found" each, in desktop coordinates.
    /// </summary>
    public List<string> FieldDifferences(MediaFrame picture, PixelRect pictured, PixelRect area)
    {
        var field = FieldBounds;
        var judged = new PixelRect(field.X + Margin, field.Y + Margin, field.Width - 2 * Margin, field.Height - 2 * Margin).Intersect(area).Intersect(pictured);
        var differences = new List<string>();
        for (var y = judged.Y; y < judged.Bottom; y++)
        {
            for (var x = judged.X; x < judged.Right; x++)
            {
                if (PatternWindow.ColourAt(picture, x - pictured.X, y - pictured.Y) is var found && found != Field)
                {
                    differences.Add($"{x}, {y}: {found:X8}");
                }
            }
        }

        return differences;
    }

    /// <summary>Waits until a grab through <paramref name="screen"/> shows the field green and a count in the cells, so it is drawn and composed.</summary>
    public async Task OnScreen(IScreen screen)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (screen.Grab(Bounds, withPointer: false) is var grab && (!grab.Succeeded || CountIn(grab.Value!, Bounds) is null || FieldDifferences(grab.Value!, Bounds, Bounds).Count > 0))
        {
            Assert.IsLessThan(deadline, DateTime.UtcNow, $"the counter at {Bounds} is not on the screen");
            await Task.Delay(50);
        }
    }

    public void Dispose() => TrayDesktop.Run(() =>
    {
        timer.Stop();
        Window.Close();
    });
}
