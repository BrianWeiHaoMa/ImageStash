using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using ImageStash.Media;
using ImageStash.Window;

namespace ImageStash.Windows.Tests.Support;

/// <summary>
/// A window's content as the real renderer draws it, in physical pixels, colours as BGRA with full alpha; for the stash
/// window, with where the preview's picture area is and where its picture shows in it.
/// </summary>
public sealed record RenderedWindow(uint[] Pixels, int Width, double Scaling, Rect Area, Rect Picture)
{
    /// <summary>Renders the shown window's content once its layout and rendering have settled.</summary>
    public static async Task<RenderedWindow> Of(ShownWindow shown)
    {
        await Task.Delay(200);
        return TrayDesktop.Get(() =>
        {
            var (pixels, width, root) = Render(shown.Window);
            var scaling = shown.Window.RenderScaling;
            var picture = shown.PreviewPicture;
            var corner = picture.TranslatePoint(default, root)!.Value;
            var shownAt = picture.Image is { } image ? ShownPicture.Fit(image.PixelSize, picture.Bounds.Size, scaling) : default;
            return new RenderedWindow(pixels, width, scaling,
                new Rect(corner * scaling, picture.Bounds.Size * scaling), new Rect((corner + shownAt.Position) * scaling, shownAt.Size * scaling));
        });
    }

    /// <summary>Renders any window's content, a notification's among them, once its layout and rendering have settled.</summary>
    public static async Task<RenderedWindow> Of(Avalonia.Controls.Window window)
    {
        await Task.Delay(200);
        return TrayDesktop.Get(() =>
        {
            var (pixels, width, _) = Render(window);
            return new RenderedWindow(pixels, width, window.RenderScaling, default, default);
        });
    }

    /// <summary>Where <paramref name="control"/> lies in the rendered content, in physical pixels.</summary>
    public static Rect Place(Avalonia.Controls.Window window, Control control) =>
        new(control.TranslatePoint(default, (Control)window.Content!)!.Value * window.RenderScaling, control.Bounds.Size * window.RenderScaling);

    private static (uint[] Pixels, int Width, Control Root) Render(Avalonia.Controls.Window window)
    {
        window.UpdateLayout();
        var root = (Control)window.Content!;
        var scaling = window.RenderScaling;
        var size = new PixelSize((int)Math.Ceiling(root.Bounds.Width * scaling), (int)Math.Ceiling(root.Bounds.Height * scaling));
        using var target = new RenderTargetBitmap(size, new Vector(96 * scaling, 96 * scaling));
        target.Render(root);
        var pixels = new uint[size.Width * size.Height];
        var pinned = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            target.CopyPixels(new PixelRect(size), pinned.AddrOfPinnedObject(), pixels.Length * 4, size.Width * 4);
        }
        finally
        {
            pinned.Free();
        }

        return (pixels.Select(pixel => pixel | 0xFF000000).ToArray(), size.Width, root);
    }

    public uint At(int x, int y) => Pixels[y * Width + x];

    /// <summary>The fill of the preview's picture area beside its picture.</summary>
    public uint Background => At((int)Area.X + 2, (int)(Area.Y + Area.Height / 2));

    public int Count(uint color) => Pixels.Count(pixel => pixel == color);

    /// <summary>Every colour inside the rectangle, in physical pixels.</summary>
    public HashSet<uint> Colors(double x, double y, double width, double height)
    {
        var colors = new HashSet<uint>();
        for (var row = (int)Math.Ceiling(y); row < (int)(y + height); row++)
        {
            for (var column = (int)Math.Ceiling(x); column < (int)(x + width); column++)
            {
                colors.Add(At(column, row));
            }
        }

        return colors;
    }

    /// <summary>The smallest rectangle holding every pixel of <paramref name="color"/>, by its inclusive edges.</summary>
    public (int Left, int Top, int Right, int Bottom) Bounds(uint color) => Bounds(color, new Rect(0, 0, Width, Pixels.Length / Width));

    /// <summary>
    /// The smallest rectangle holding every pixel of <paramref name="color"/> inside <paramref name="within"/>, in physical
    /// pixels, by its inclusive edges: a colour the window shows elsewhere too (the accent, on its buttons) left out.
    /// </summary>
    public (int Left, int Top, int Right, int Bottom) Bounds(uint color, Rect within)
    {
        var (left, top, right, bottom) = (int.MaxValue, int.MaxValue, -1, -1);
        for (var i = 0; i < Pixels.Length; i++)
        {
            var (x, y) = (i % Width, i / Width);
            if (Pixels[i] == color && within.Contains(new Point(x, y)))
            {
                (left, top, right, bottom) = (Math.Min(left, x), Math.Min(top, y), Math.Max(right, x), Math.Max(bottom, y));
            }
        }

        return (left, top, right, bottom);
    }

    /// <summary>How many pixels differ from <paramref name="other"/>'s.</summary>
    public int Differences(RenderedWindow other) => Pixels.Zip(other.Pixels).Count(pair => pair.First != pair.Second);
}
