using ImageStash.Media;

namespace ImageStash.Tests.Support;

/// <summary>Animated GIFs a check makes: written by the product's one GIF encoder, or one of them with a frame made unreadable.</summary>
public static class SyntheticClips
{
    /// <summary>A GIF of <paramref name="count"/> frames of <see cref="SyntheticFrames.Pattern"/>, each lasting <paramref name="durationMs"/>.</summary>
    public static byte[] Gif(int count, int width, int height, int durationMs = 100)
    {
        using var stream = new MemoryStream();
        var clip = GifEncoder.Open(stream, width, height, "clip").Value!;
        for (var i = 0; i < count; i++)
        {
            Assert.IsTrue(clip.Append(SyntheticFrames.Pattern(i, width, height), (long)i * durationMs).Succeeded, $"frame {i}");
        }

        Assert.IsNotNull(clip.Stop((long)count * durationMs).Value);
        return stream.ToArray();
    }

    /// <summary>
    /// A clip of <paramref name="count"/> frames made quickly: a GIF of two whole frames of <paramref name="width"/> ×
    /// <paramref name="height"/>, its second frame, its timing included, repeated until there are <paramref name="count"/>.
    /// </summary>
    public static byte[] Long(int count, int width, int height, int durationMs = 20)
    {
        var two = Gif(2, width, height, durationMs);
        var (start, end) = Image(two, 1);
        using var clip = new MemoryStream();
        clip.Write(two, 0, end);
        for (var i = 2; i < count; i++)
        {
            clip.Write(two, start, end - start);
        }

        clip.Write(two, end, two.Length - end);
        return clip.ToArray();
    }

    /// <summary>
    /// A copy of <paramref name="gif"/> whose image <paramref name="index"/> (counted from zero) holds codes its LZW table
    /// cannot have: the file keeps every frame and its trailer, and that frame alone does not decode.
    /// </summary>
    public static byte[] WithFrameUnreadable(byte[] gif, int index)
    {
        var bytes = (byte[])gif.Clone();
        var (start, _) = Image(bytes, index);
        var at = start;
        while (bytes[at] == 0x21)
        {
            at = SkipSubBlocks(bytes, at + 2);
        }

        var data = at + 10 + ColourTable(bytes[at + 9]) + 1;
        // Every code all ones: far beyond the few entries the table has at the start of the image.
        Array.Fill(bytes, (byte)0xFF, data + 1, bytes[data]);
        return bytes;
    }

    /// <summary>Where image <paramref name="index"/>'s blocks lie, from the extensions before it to the end of its data.</summary>
    private static (int Start, int End) Image(byte[] bytes, int index)
    {
        var at = 13 + ColourTable(bytes[10]);
        var start = at;
        for (var image = 0; ; )
        {
            switch (bytes[at])
            {
                case 0x21:
                    at = SkipSubBlocks(bytes, at + 2);
                    break;
                case 0x2C:
                    at = SkipSubBlocks(bytes, at + 10 + ColourTable(bytes[at + 9]) + 1);
                    if (image++ == index)
                    {
                        return (start, at);
                    }

                    start = at;
                    break;
                default:
                    throw new InvalidDataException($"the GIF has no image {index}");
            }
        }
    }

    private static int ColourTable(byte flags) => (flags & 0x80) == 0 ? 0 : 3 << ((flags & 7) + 1);

    private static int SkipSubBlocks(byte[] bytes, int at)
    {
        while (bytes[at] != 0)
        {
            at += bytes[at] + 1;
        }

        return at + 1;
    }
}
