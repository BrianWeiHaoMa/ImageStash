using Avalonia;
using Avalonia.Controls;
using ImageStash.Media;
using ImageStash.Seams;

namespace ImageStash.Tests.Support;

/// <summary>
/// The Screen seam for the portable checks: preset displays (one 1280 × 800 at scaling 1, primary, unless a check sets
/// others), each with a preset picture or a refusal, and every read recorded; nothing of it reaches a real screen.
/// Each grab hands out a copy of its display's picture, as the seam's frame is the caller's, and keeps only a weak
/// reference to it, so a check can tell whether the caller let it go. Grabs may come from any thread, one at a time.
/// </summary>
public sealed class RecordingScreen : IScreen
{
    public static readonly Display Primary = new(new PixelRect(0, 0, 1280, 800), new PixelRect(0, 0, 1280, 752), 1, IsPrimary: true);

    private readonly Dictionary<Display, Outcome<MediaFrame>> pictures = [];

    public IReadOnlyList<Display> Shown { get; set; } = [Primary];

    /// <summary>How often the displays were asked for.</summary>
    public int DisplayReads { get; private set; }

    /// <summary>Every rectangle asked for, in order.</summary>
    public List<PixelRect> Grabs { get; } = [];

    /// <summary>Whether each grab, in order, was asked to draw the pointer in.</summary>
    public List<bool> PointerAsked { get; } = [];

    /// <summary>
    /// When set, grab n (counted from zero) takes its display's picture, or a refusal, from this instead of the preset,
    /// so successive grabs differ as a live screen's do; a check may also pass time or look at its caller inside it, as
    /// a real grab takes time.
    /// </summary>
    public Func<int, Outcome<MediaFrame>>? EachGrab { get; set; }

    /// <summary>Every frame handed out, weakly.</summary>
    public List<WeakReference<MediaFrame>> Handed { get; } = [];

    /// <summary>The pixels of every frame handed out, weakly, so a caller keeping them without their frame shows too.</summary>
    public List<WeakReference<byte[]>> HandedPixels { get; } = [];

    /// <summary>Every window asked to be left out of captures.</summary>
    public List<TopLevel> Exclusions { get; } = [];

    /// <summary>
    /// Gives <paramref name="display"/> a picture of its size in which no two pixels are alike, so a cut from the wrong
    /// place or a flipped row shows; <paramref name="seed"/> sets it apart from another display's.
    /// </summary>
    public MediaFrame Paint(Display display, int seed)
    {
        var (width, height) = (display.Bounds.Width, display.Bounds.Height);
        var pixels = new byte[4L * width * height];
        for (var i = 0; i < width * height; i++)
        {
            var value = i * 7919 + seed * 104729;
            (pixels[i * 4], pixels[i * 4 + 1], pixels[i * 4 + 2], pixels[i * 4 + 3]) = ((byte)value, (byte)(value >> 8), (byte)(value >> 16), 255);
        }

        var frame = new MediaFrame(width, height, 0, pixels);
        pictures[display] = Outcome<MediaFrame>.Of(frame);
        return frame;
    }

    /// <summary>Makes every grab of <paramref name="display"/> the platform's refusal <paramref name="failure"/>.</summary>
    public void Refuse(Display display, Failure failure) => pictures[display] = failure;

    public IReadOnlyList<Display> Displays()
    {
        DisplayReads++;
        return Shown;
    }

    public Outcome<MediaFrame> Grab(PixelRect rectangle, bool withPointer)
    {
        int index;
        lock (Grabs)
        {
            index = Grabs.Count;
            Grabs.Add(rectangle);
            PointerAsked.Add(withPointer);
        }

        var display = Shown.FirstOrDefault(d => d.Bounds.Contains(rectangle) && rectangle.Width > 0 && rectangle.Height > 0);
        if (display is null)
        {
            return new Failure("Grab the screen", $"the screen area at {rectangle.X}, {rectangle.Y} ({rectangle.Width} × {rectangle.Height})", "it does not lie on one display");
        }

        var picture = EachGrab is { } each ? each(index)
            : pictures.TryGetValue(display, out var preset) ? preset
            : Outcome<MediaFrame>.Of(Paint(display, Shown.ToList().IndexOf(display)));
        if (!picture.Succeeded)
        {
            return picture.Failure;
        }

        var copy = Cut(picture.Value!, new PixelRect(rectangle.X - display.Bounds.X, rectangle.Y - display.Bounds.Y, rectangle.Width, rectangle.Height));
        lock (Grabs)
        {
            Handed.Add(new WeakReference<MediaFrame>(copy));
            HandedPixels.Add(new WeakReference<byte[]>(copy.Pixels));
        }

        return Outcome<MediaFrame>.Of(copy);
    }

    public Outcome ExcludeFromCapture(TopLevel window)
    {
        Exclusions.Add(window);
        return Outcome.Done;
    }

    /// <summary>The pixels of <paramref name="area"/>, in the frame's own coordinates, as a frame of their own.</summary>
    public static MediaFrame Cut(MediaFrame frame, PixelRect area)
    {
        var pixels = new byte[4L * area.Width * area.Height];
        for (var y = 0; y < area.Height; y++)
        {
            Array.Copy(frame.Pixels, ((long)(area.Y + y) * frame.Width + area.X) * 4, pixels, (long)y * area.Width * 4, area.Width * 4);
        }

        return new MediaFrame(area.Width, area.Height, 0, pixels);
    }
}
