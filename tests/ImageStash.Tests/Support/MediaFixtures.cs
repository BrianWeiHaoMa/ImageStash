using ImageStash.Media;
using SkiaSharp;

namespace ImageStash.Tests.Support;

/// <summary>
/// The pictures <c>Fixtures/make_fixtures.py</c> writes, copied beside the checks. A check that changes one writes its
/// variant into a <see cref="ScratchFolder"/>.
/// </summary>
public static class MediaFixtures
{
    public static string Path(string name) => System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    public static byte[] Bytes(string name) => File.ReadAllBytes(Path(name));

    /// <summary>A small opaque PNG whose bytes differ from every other seed's, for checks that need many different pictures.</summary>
    public static byte[] DistinctPng(int seed, int width = 4, int height = 3)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque));
        bitmap.Erase(new SKColor((byte)seed, (byte)(seed >> 8), (byte)(seed >> 16)));
        return bitmap.Encode(SKEncodedImageFormat.Png, 100).ToArray();
    }

    /// <summary>The first frame of the picture at <paramref name="path"/>, decoded at its own size.</summary>
    public static MediaFrame FirstFrame(string path)
    {
        using var decoder = MediaDecoder.Open(path, System.IO.Path.GetFileName(path), TestStash.ShortRetry).Value!;
        return decoder.NextFrame(Math.Max(decoder.Width, decoder.Height)).Value!;
    }

    /// <summary>Every frame of the picture at <paramref name="path"/>, in order, each decoded at its own size with its duration.</summary>
    public static List<MediaFrame> Frames(string path)
    {
        using var decoder = MediaDecoder.Open(path, System.IO.Path.GetFileName(path), TestStash.ShortRetry).Value!;
        var frames = new List<MediaFrame>();
        while (decoder.NextFrame(Math.Max(decoder.Width, decoder.Height)).Value is { } frame)
        {
            frames.Add(frame);
        }

        return frames;
    }

    /// <summary>The alpha of the pixel at (<paramref name="x"/>, <paramref name="y"/>) of a frame in the boundary pixel contract.</summary>
    public static byte Alpha(MediaFrame frame, int x, int y) => frame.Pixels[(y * frame.Width + x) * 4 + 3];

    /// <summary>Every fixture that is a whole picture, with its format.</summary>
    public static IEnumerable<object[]> Whole =>
    [
        ["still.png", ImageFormat.Png], ["transparent.png", ImageFormat.Png], ["translucent.png", ImageFormat.Png],
        ["invisible-blue.png", ImageFormat.Png], ["animated.png", ImageFormat.Png], ["huge.png", ImageFormat.Png], ["photo.jpg", ImageFormat.Jpeg],
        ["photo.jpeg", ImageFormat.Jpeg], ["progressive.jpg", ImageFormat.Jpeg], ["still.gif", ImageFormat.Gif],
        ["animated.gif", ImageFormat.Gif], ["long.gif", ImageFormat.Gif], ["restore-previous.gif", ImageFormat.Gif],
        ["still.webp", ImageFormat.WebP], ["transparent.webp", ImageFormat.WebP], ["animated.webp", ImageFormat.WebP],
        .. Enumerable.Range(1, 8).Select(orientation => new object[] { $"oriented-{orientation}.jpg", ImageFormat.Jpeg }),
        ["oriented-6.webp", ImageFormat.WebP], ["oriented-6.png", ImageFormat.Png],
    ];

    /// <summary>
    /// The colour of each quadrant of the oriented photos as stored, <c>A</c> top-left, <c>B</c> top-right, <c>C</c>
    /// bottom-left, <c>D</c> bottom-right, each 20 × 12 of the stored 40 × 24 (<c>oriented-&lt;n&gt;.jpg</c>, <c>.webp</c>, <c>.png</c>).
    /// </summary>
    public static IReadOnlyDictionary<char, (byte R, byte G, byte B)> StoredQuadrants { get; } = new Dictionary<char, (byte R, byte G, byte B)>
    {
        ['A'] = (230, 30, 30), ['B'] = (30, 200, 30), ['C'] = (30, 30, 230), ['D'] = (240, 220, 20),
    };

    /// <summary>
    /// For each EXIF orientation of <c>oriented-&lt;n&gt;.jpg</c>, the stored quadrant the upright picture shows at its
    /// top-left, top-right, bottom-left and bottom-right, as Pillow's <c>exif_transpose</c> places them (<c>make_fixtures.py</c>).
    /// </summary>
    public static IReadOnlyDictionary<int, string> UprightQuadrants { get; } = new Dictionary<int, string>
    {
        [1] = "ABCD", [2] = "BADC", [3] = "DCBA", [4] = "CDAB", [5] = "ACBD", [6] = "CADB", [7] = "DBCA", [8] = "BDAC",
    };
}
