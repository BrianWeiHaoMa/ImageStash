using System.Buffers.Binary;
using System.Numerics;
using ImageStash.Media;

namespace ImageStash.Windows.Seams;

/// <summary>
/// A clipboard bitmap (CF_DIB or CF_DIBV5) as one still frame in the boundary pixel contract, whatever its depth: its
/// header (BITMAPINFOHEADER, BITMAPV4HEADER or BITMAPV5HEADER, told apart by its first DWORD), its color masks and
/// color table, then its rows, each padded to a DWORD, bottom-up unless its height is negative, or run-length encoded
/// at 8 or 4 bits; its colors brought into sRGB by <see cref="WindowsDibColorSpace"/>. Bytes in, a frame or a failure
/// naming why out, never an exception; no clipboard is touched here.
/// </summary>
public static class WindowsDib
{
    private const uint BiRgb = 0;
    private const uint BiRle8 = 1;
    private const uint BiRle4 = 2;
    private const uint BiBitfields = 3;

    public static Outcome<MediaFrame> Convert(byte[] dib)
    {
        try
        {
            return ConvertNow(dib);
        }
        catch (OutOfMemoryException)
        {
            return Unreadable("it is too large to hold in memory");
        }
    }

    private static Outcome<MediaFrame> ConvertNow(byte[] dib)
    {
        if (dib.Length < 40)
        {
            return Unreadable("it is too short to hold a bitmap header");
        }

        var headerSize = U32(dib, 0);
        if (headerSize is not (40 or 108 or 124))
        {
            return Unreadable($"its header is of a kind not supported ({headerSize} bytes)");
        }

        if (dib.Length < headerSize)
        {
            return Unreadable("it ends inside its header");
        }

        var width = BinaryPrimitives.ReadInt32LittleEndian(dib.AsSpan(4));
        var height = BinaryPrimitives.ReadInt32LittleEndian(dib.AsSpan(8));
        var bits = BinaryPrimitives.ReadUInt16LittleEndian(dib.AsSpan(14));
        var compression = U32(dib, 16);
        var sizeImage = U32(dib, 20);
        var colorsUsed = U32(dib, 32);
        if (width <= 0 || height is 0 or int.MinValue)
        {
            return Unreadable("its width or height is not a size");
        }

        if (compression is not (BiRgb or BiRle8 or BiRle4 or BiBitfields))
        {
            return Unreadable("it is compressed in a way that is not supported");
        }

        if (bits is not (1 or 4 or 8 or 16 or 24 or 32))
        {
            return Unreadable($"its depth of {bits} bits per pixel is not supported");
        }

        if ((compression == BiBitfields && bits is not (16 or 32)) || (compression == BiRle8 && bits != 8) || (compression == BiRle4 && bits != 4))
        {
            return Unreadable($"its compression does not fit {bits} bits per pixel");
        }

        if (compression is BiRle8 or BiRle4 && height < 0)
        {
            return Unreadable("its run-length encoded rows are stored top-down");
        }

        if (headerSize == 40 && compression == BiBitfields && dib.Length < 52)
        {
            return Unreadable("it ends inside its color masks");
        }

        // Checked before any size is multiplied out, so no arithmetic below can overflow (G35).
        var rows = Math.Abs(height);
        if ((long)width * rows > Array.MaxLength / 4)
        {
            return Unreadable("it is too large to read");
        }

        var (red, green, blue) = compression == BiBitfields
            ? (U32(dib, 40), U32(dib, 44), U32(dib, 48))
            : bits == 16 ? (0x7C00u, 0x03E0u, 0x001Fu) : (0x00FF0000u, 0x0000FF00u, 0x000000FFu);

        // A V4 or V5 header declares its alpha mask: at 32 bits whatever its compression (Edge sets it under BI_RGB),
        // at 16 bits with its masks.
        var declaredAlpha = headerSize >= 108 && (bits == 32 || (bits == 16 && compression == BiBitfields)) ? U32(dib, 52) : 0;
        var stride = ((long)width * bits + 31) / 32 * 4;
        var rowBytes = compression is BiRle8 or BiRle4 ? 0 : stride * rows;

        // Every depth may carry a color table of biClrUsed entries; up to 8 bits one of 2^bits is the default.
        var tableEntries = colorsUsed != 0 ? colorsUsed : bits <= 8 ? 1L << bits : 0;
        long start = headerSize;
        if (compression == BiBitfields && (headerSize == 40 || HoldsMasksTable(dib, headerSize, red, green, blue, tableEntries * 4 + rowBytes)))
        {
            start += 12;
        }

        var tableStart = start;
        start += tableEntries * 4;
        if (start + rowBytes > dib.Length)
        {
            return Unreadable("it ends before its last row");
        }

        if (sizeImage != 0 && sizeImage < rowBytes)
        {
            return Unreadable("its stated size cannot hold its rows");
        }

        var palette = bits <= 8 ? Palette(dib, tableStart, (int)Math.Min(tableEntries, 1L << bits), bits) : [];
        var pixels = new byte[width * rows * 4];
        if (compression is BiRle8 or BiRle4)
        {
            RunLengthDecoded(dib, (int)start, bits, width, rows, palette, pixels);
        }
        else
        {
            var alpha = declaredAlpha != 0 ? Channel.Of(declaredAlpha) : bits == 32 ? UnclaimedAlpha(dib, start, stride, width, rows, red | green | blue) : null;
            Uncompressed(dib, (int)start, stride, bits, width, rows, height < 0, (Channel.Of(red), Channel.Of(green), Channel.Of(blue)), alpha, palette, pixels);
            if (alpha is not null && IsPremultiplied(pixels))
            {
                Unpremultiply(pixels);
            }
        }

        WindowsDibColorSpace.ToSrgb(dib, headerSize, pixels, width, rows);
        return Outcome<MediaFrame>.Of(new MediaFrame(width, rows, 0, pixels));
    }

    private static void Uncompressed(byte[] dib, int start, long stride, int bits, int width, int rows, bool topDown, (Channel Red, Channel Green, Channel Blue) channels, Channel? alpha, byte[] palette, byte[] pixels)
    {
        for (var y = 0; y < rows; y++)
        {
            var source = (int)(start + (topDown ? y : rows - 1 - y) * stride);
            var target = y * width * 4;
            for (var x = 0; x < width; x++, target += 4)
            {
                switch (bits)
                {
                    case 32:
                        Masked(U32(dib, source + x * 4), channels, alpha, pixels, target);
                        break;
                    case 24:
                        (pixels[target], pixels[target + 1], pixels[target + 2], pixels[target + 3]) = (dib[source + x * 3], dib[source + x * 3 + 1], dib[source + x * 3 + 2], 255);
                        break;
                    case 16:
                        Masked(BinaryPrimitives.ReadUInt16LittleEndian(dib.AsSpan(source + x * 2)), channels, alpha, pixels, target);
                        break;
                    default:
                        var perByte = 8 / bits;
                        var index = (dib[source + x / perByte] >> (8 - bits * (x % perByte + 1))) & ((1 << bits) - 1);
                        palette.AsSpan(index * 4, 4).CopyTo(pixels.AsSpan(target));
                        break;
                }
            }
        }
    }

    /// <summary>
    /// BI_RLE8 or BI_RLE4 (Bitmap Compression): runs of one index, or of two alternating ones at 4 bits; escapes for the
    /// end of a line, the end of the bitmap, a move right and up, and a word-aligned run of indexes as they are; rows
    /// bottom-up. A pixel no run reaches stays transparent, as browsers show such a bitmap; a run past the bitmap's edge
    /// is cut there, and data that stops early ends the picture.
    /// </summary>
    private static void RunLengthDecoded(byte[] dib, int start, int bits, int width, int rows, byte[] palette, byte[] pixels)
    {
        var (x, y, at) = (0, 0, start);
        while (at + 1 < dib.Length && y < rows)
        {
            int count = dib[at], value = dib[at + 1];
            at += 2;
            // The column stops at the edge, where a run is cut, so no line, however long, counts it past an int (G35).
            if (count > 0)
            {
                for (var i = 0; i < count && x < width; i++)
                {
                    Put(bits == 8 ? value : i % 2 == 0 ? value >> 4 : value & 0x0F, x++, y);
                }

                continue;
            }

            switch (value)
            {
                case 0:
                    (x, y) = (0, y + 1);
                    break;
                case 1:
                    return;
                case 2:
                    if (at + 1 >= dib.Length)
                    {
                        return;
                    }

                    (x, y, at) = (Math.Min(x + dib[at], width), y + dib[at + 1], at + 2);
                    break;
                default:
                    var length = bits == 8 ? value : (value + 1) / 2;
                    for (var i = 0; i < value && x < width && at + (bits == 8 ? i : i / 2) < dib.Length; i++)
                    {
                        var packed = dib[at + (bits == 8 ? i : i / 2)];
                        Put(bits == 8 ? packed : i % 2 == 0 ? packed >> 4 : packed & 0x0F, x++, y);
                    }

                    at += length + (length % 2);
                    break;
            }
        }

        void Put(int index, int column, int row)
        {
            if (column < width && row < rows)
            {
                palette.AsSpan(index * 4, 4).CopyTo(pixels.AsSpan(((rows - 1 - row) * width + column) * 4));
            }
        }
    }

    /// <summary>
    /// Whether the 12 bytes after a V4 or V5 header repeat its three masks and the block has room for them besides
    /// what follows: the CF_DIBV5 Windows makes from a CF_BITMAP carries them there again before its rows (probed). A
    /// profile starting where the rows end without them says there are none, as a packed DIB's profile follows its
    /// bits (BITMAPV5HEADER), whatever its first pixels hold.
    /// </summary>
    private static bool HoldsMasksTable(byte[] dib, uint headerSize, uint red, uint green, uint blue, long after) =>
        dib.Length >= headerSize + 12 + after
        && WindowsDibColorSpace.ProfileStart(dib, headerSize) != headerSize + after
        && (U32(dib, (int)headerSize), U32(dib, (int)headerSize + 4), U32(dib, (int)headerSize + 8)) == (red, green, blue);

    /// <summary>The color table as BGRA, opaque, 2^bits entries; an index past the table's own entries is black.</summary>
    private static byte[] Palette(byte[] dib, long tableStart, int entries, int bits)
    {
        var palette = new byte[(1 << bits) * 4];
        for (var i = 0; i < palette.Length / 4; i++)
        {
            if (i < entries)
            {
                dib.AsSpan((int)tableStart + i * 4, 3).CopyTo(palette.AsSpan(i * 4));
            }

            palette[i * 4 + 3] = 255;
        }

        return palette;
    }

    /// <summary>
    /// A 32-bit DIB's alpha without a declared mask: the bits no color mask claims, when any pixel's are nonzero; else
    /// none, and the picture is opaque.
    /// </summary>
    private static Channel? UnclaimedAlpha(byte[] dib, long start, long stride, int width, int rows, uint colors)
    {
        var unclaimed = ~colors;
        if (unclaimed == 0)
        {
            return null;
        }

        for (var y = 0; y < rows; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if ((U32(dib, (int)(start + y * stride) + x * 4) & unclaimed) != 0)
                {
                    return Channel.Of(unclaimed);
                }
            }
        }

        return null;
    }

    private static void Masked(uint value, (Channel Red, Channel Green, Channel Blue) channels, Channel? alpha, byte[] pixels, int target)
    {
        pixels[target] = channels.Blue.Read(value);
        pixels[target + 1] = channels.Green.Read(value);
        pixels[target + 2] = channels.Red.Read(value);
        pixels[target + 3] = alpha is { } a ? a.Read(value) : (byte)255;
    }

    /// <summary>
    /// Premultiplied, as GDI's alpha bitmaps and Edge's are, when no channel exceeds its pixel's alpha and some
    /// translucent pixel has color; straight otherwise, as ImageStash's own and WinForms' are (probed). A picture of
    /// only transparent and opaque pixels reads the same either way.
    /// </summary>
    private static bool IsPremultiplied(byte[] pixels)
    {
        var translucentWithColor = false;
        for (var i = 0; i < pixels.Length; i += 4)
        {
            var a = pixels[i + 3];
            if (pixels[i] > a || pixels[i + 1] > a || pixels[i + 2] > a)
            {
                return false;
            }

            translucentWithColor |= a is > 0 and < 255 && (pixels[i] | pixels[i + 1] | pixels[i + 2]) != 0;
        }

        return translucentWithColor;
    }

    /// <summary>Each channel × 255 / alpha, rounded, at most 255; a pixel with alpha 0 keeps its channels.</summary>
    private static void Unpremultiply(byte[] pixels)
    {
        for (var i = 0; i < pixels.Length; i += 4)
        {
            var a = pixels[i + 3];
            if (a is 0 or 255)
            {
                continue;
            }

            for (var c = i; c < i + 3; c++)
            {
                pixels[c] = (byte)Math.Min(255, (pixels[c] * 255 + a / 2) / a);
            }
        }
    }

    private static uint U32(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));

    private static Failure Unreadable(string why) => WindowsClipboardAndDrops.ReadFailure($"its picture could not be read: {why}");

    /// <summary>One color mask's bits, scaled to 8 bits: 0x1F of 5 bits is 0xFF.</summary>
    private readonly record struct Channel(uint Mask, int Shift, uint Max)
    {
        public static Channel Of(uint mask) =>
            mask == 0 ? default : new Channel(mask, BitOperations.TrailingZeroCount(mask), mask >> BitOperations.TrailingZeroCount(mask));

        public byte Read(uint value)
        {
            if (Max == 0)
            {
                return 0;
            }

            var bits = (value & Mask) >> Shift;
            return Max == 255 ? (byte)bits : (byte)(((ulong)bits * 255 + Max / 2) / Max);
        }
    }
}
