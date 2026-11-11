using ImageStash.Media;

namespace ImageStash.Tests.Support;

/// <summary>
/// Opaque frames in the boundary pixel contract (32-bit BGRA, straight alpha, top-down, packed) built by a check, and
/// their pixels read as one 32-bit value each, for checks that hand an encoder frames and compare what comes back.
/// </summary>
public static class SyntheticFrames
{
    /// <summary>A frame of 48 colours in 8 × 8 squares, shifted by <paramref name="seed"/>, so frames differ and fit one palette.</summary>
    public static MediaFrame Pattern(int seed, int width, int height) => Frame(width, height, (x, y) =>
    {
        var n = (x / 8 + y / 8 * 8 + seed * 5) % 48;
        return ((byte)(n * 5), (byte)(255 - n * 4), (byte)(n * 11 % 256));
    });

    /// <summary>Every pixel one of <paramref name="colours"/> opaque colours at random.</summary>
    public static MediaFrame Noise(int seed, int colours, int width, int height)
    {
        var random = new Random(seed);
        var palette = Enumerable.Range(0, colours).Select(_ => ((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256))).ToArray();
        return Frame(width, height, (_, _) => palette[random.Next(colours)]);
    }

    /// <summary>
    /// Thousands of colours at random, the same every call, each channel moved on by 64 a <paramref name="step"/>, so every
    /// pixel differs from the frame of the step before and after it, as a playing video changes every pixel.
    /// </summary>
    public static MediaFrame Busy(int step, int width, int height)
    {
        var random = new Random(1);
        var moved = (byte)(64 * step);
        return Frame(width, height, (_, _) => ((byte)(random.Next(256) + moved), (byte)(random.Next(256) + moved), (byte)(random.Next(256) + moved)));
    }

    public static MediaFrame Frame(int width, int height, Func<int, int, (byte B, byte G, byte R)> colour)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var (b, g, r) = colour(x, y);
                var i = (y * width + x) * 4;
                (pixels[i], pixels[i + 1], pixels[i + 2], pixels[i + 3]) = (b, g, r, 255);
            }
        }

        return new MediaFrame(width, height, 0, pixels);
    }

    /// <summary>A copy of <paramref name="frame"/> with a rectangle painted one colour.</summary>
    public static MediaFrame Paint(MediaFrame frame, int left, int top, int width, int height, (byte B, byte G, byte R) colour)
    {
        var pixels = (byte[])frame.Pixels.Clone();
        Fill(pixels, frame.Width, left, top, width, height, colour);
        return frame with { Pixels = pixels };
    }

    /// <summary>Paints a rectangle of the frame <paramref name="pixels"/>, <paramref name="frameWidth"/> pixels wide, one opaque colour.</summary>
    public static void Fill(byte[] pixels, int frameWidth, int left, int top, int width, int height, (byte B, byte G, byte R) colour)
    {
        for (var y = top; y < top + height; y++)
        {
            for (var x = left; x < left + width; x++)
            {
                var i = (y * frameWidth + x) * 4;
                (pixels[i], pixels[i + 1], pixels[i + 2], pixels[i + 3]) = (colour.B, colour.G, colour.R, 255);
            }
        }
    }

    /// <summary>The pixel at (<paramref name="x"/>, <paramref name="y"/>) as B | G &lt;&lt; 8 | R &lt;&lt; 16 | A &lt;&lt; 24.</summary>
    public static uint Pixel(MediaFrame frame, int x, int y) => BitConverter.ToUInt32(frame.Pixels, (y * frame.Width + x) * 4);

    /// <summary>An opaque colour as <see cref="Pixel"/> reads it.</summary>
    public static uint Color(byte b, byte g, byte r) => (uint)(b | g << 8 | r << 16 | 255 << 24);
}
