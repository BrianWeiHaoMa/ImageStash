using System.Text;

namespace ImageStash.Tests.Support;

/// <summary>
/// What a GIF file holds, read block by block from its bytes rather than through a decoder: each image's position, size,
/// delay in centiseconds, disposal and local palette, the NETSCAPE loop count, and whether the trailer was reached.
/// </summary>
public static class GifImages
{
    public sealed record Image(int Left, int Top, int Width, int Height, int DelayCs, int Disposal, int PaletteSize);

    public sealed record Blocks(int ScreenWidth, int ScreenHeight, int? LoopCount, IReadOnlyList<Image> Images, bool Trailer);

    /// <summary>GIF's "do not dispose": the image stays under the next.</summary>
    public const int Keep = 1;

    /// <summary>GIF's "restore to background": the image's area is cleared before the next is drawn.</summary>
    public const int RestoreToBackground = 2;

    public static Blocks Read(string path) => Read(File.ReadAllBytes(path));

    public static Blocks Read(byte[] gif)
    {
        Assert.AreEqual("GIF89a", Encoding.ASCII.GetString(gif, 0, 6));
        var screenWidth = Word(gif, 6);
        var screenHeight = Word(gif, 8);
        var i = 13 + TableBytes(gif[10]);
        int? loop = null;
        int delay = 0, disposal = 0;
        var images = new List<Image>();
        while (i < gif.Length)
        {
            switch (gif[i])
            {
                case 0x21:
                    var label = gif[i + 1];
                    i += 2;
                    if (label == 0xF9)
                    {
                        disposal = (gif[i + 1] >> 2) & 7;
                        delay = Word(gif, i + 2);
                    }
                    else if (label == 0xFF && Encoding.ASCII.GetString(gif, i + 1, 11) == "NETSCAPE2.0")
                    {
                        loop = Word(gif, i + 14);
                    }

                    i = AfterSubBlocks(gif, i);
                    break;
                case 0x2C:
                    var palette = TableBytes(gif[i + 9]) / 3;
                    images.Add(new Image(Word(gif, i + 1), Word(gif, i + 3), Word(gif, i + 5), Word(gif, i + 7), delay, disposal, palette));
                    (delay, disposal) = (0, 0);
                    // The descriptor, the local table, the LZW minimum code size, then the data's sub-blocks.
                    i = AfterSubBlocks(gif, i + 10 + palette * 3 + 1);
                    break;
                case 0x3B:
                    return new Blocks(screenWidth, screenHeight, loop, images, Trailer: true);
                default:
                    Assert.Fail($"byte {gif[i]:X2} at {i} starts no GIF block");
                    break;
            }
        }

        return new Blocks(screenWidth, screenHeight, loop, images, Trailer: false);
    }

    private static int Word(byte[] gif, int at) => gif[at] | gif[at + 1] << 8;

    /// <summary>The bytes of the colour table a packed field announces, 0 when it has none.</summary>
    private static int TableBytes(byte packed) => (packed & 0x80) == 0 ? 0 : 3 << ((packed & 7) + 1);

    private static int AfterSubBlocks(byte[] gif, int at)
    {
        while (at < gif.Length && gif[at] != 0)
        {
            at += gif[at] + 1;
        }

        return at + 1;
    }
}
