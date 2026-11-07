using System.Buffers.Binary;

namespace ImageStash.Media;

/// <summary>
/// G35's entry: a file that ends before its format's own end is unreadable (PNG's IEND chunk, GIF's trailer, JPEG's
/// end-of-image marker, the length WebP's RIFF header declares). The codec cannot be trusted for this: a GIF cut
/// between frames and an animated WebP cut short decode with success and fewer frames (S11). This walks the file's
/// structure without decoding pixels; bytes after the end are tolerated, as every codec ignores them.
/// </summary>
internal static class TruncationCheck
{
    /// <summary>
    /// The format the stream's first bytes name, when the stream holds that format's whole file; otherwise null.
    /// Reads from the stream's start and leaves it anywhere.
    /// </summary>
    public static ImageFormat? WholeFormat(Stream stream)
    {
        var format = Sniff(stream);
        stream.Position = 0;
        var walk = new Walk(stream);
        var whole = format switch
        {
            ImageFormat.Png => PngEnds(walk),
            ImageFormat.Jpeg => JpegEnds(walk),
            ImageFormat.Gif => GifEnds(walk),
            ImageFormat.WebP => WebPEnds(stream),
            _ => false,
        };
        return whole ? format : null;
    }

    private static ImageFormat? Sniff(Stream stream)
    {
        stream.Position = 0;
        Span<byte> head = stackalloc byte[12];
        if (stream.ReadAtLeast(head, head.Length, throwOnEndOfStream: false) < head.Length)
        {
            return null;
        }

        if (head[..8].SequenceEqual(PngSignature))
        {
            return ImageFormat.Png;
        }

        if (head[0] == 0xFF && head[1] == 0xD8 && head[2] == 0xFF)
        {
            return ImageFormat.Jpeg;
        }

        if (head[..6].SequenceEqual("GIF87a"u8) || head[..6].SequenceEqual("GIF89a"u8))
        {
            return ImageFormat.Gif;
        }

        return head[..4].SequenceEqual("RIFF"u8) && head[8..12].SequenceEqual("WEBP"u8) ? ImageFormat.WebP : null;
    }

    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>Chunk by chunk, each its length, type, data and CRC, to a whole IEND chunk.</summary>
    private static bool PngEnds(Walk walk)
    {
        if (!walk.Skip(PngSignature.Length))
        {
            return false;
        }

        Span<byte> head = stackalloc byte[8];
        while (walk.Read(head))
        {
            var length = BinaryPrimitives.ReadUInt32BigEndian(head);
            if (!walk.Skip(length + 4L))
            {
                return false;
            }

            if (head[4..].SequenceEqual("IEND"u8))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Block by block (extensions and images, each a run of sub-blocks) to the trailer.</summary>
    private static bool GifEnds(Walk walk)
    {
        Span<byte> screen = stackalloc byte[13];
        if (!walk.Read(screen) || !SkipColorTable(walk, screen[10]))
        {
            return false;
        }

        Span<byte> image = stackalloc byte[9];
        while (true)
        {
            switch (walk.Next())
            {
                case 0x3B:
                    return true;
                case 0x21:
                    if (walk.Next() < 0 || !SkipSubBlocks(walk))
                    {
                        return false;
                    }

                    break;
                case 0x2C:
                    if (!walk.Read(image) || !SkipColorTable(walk, image[8]) || walk.Next() < 0 || !SkipSubBlocks(walk))
                    {
                        return false;
                    }

                    break;
                default:
                    return false;
            }
        }
    }

    private static bool SkipColorTable(Walk walk, byte packed) => (packed & 0x80) == 0 || walk.Skip(3L << ((packed & 7) + 1));

    private static bool SkipSubBlocks(Walk walk)
    {
        while (true)
        {
            var size = walk.Next();
            if (size <= 0)
            {
                return size == 0;
            }

            if (!walk.Skip(size))
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Marker by marker, skipping each segment by its length and each scan's entropy-coded data (where a 0xFF is
    /// followed by 0x00 or a restart marker), to the end-of-image marker. Stray bytes between segments are passed over
    /// as decoders do.
    /// </summary>
    private static bool JpegEnds(Walk walk)
    {
        if (!walk.Skip(2))
        {
            return false;
        }

        Span<byte> length = stackalloc byte[2];
        var marker = NextMarker(walk);
        while (marker >= 0)
        {
            if (marker == 0xD9)
            {
                return true;
            }

            if (marker is not (0x01 or (>= 0xD0 and <= 0xD7)))
            {
                if (!walk.Read(length))
                {
                    return false;
                }

                var size = BinaryPrimitives.ReadUInt16BigEndian(length);
                if (size < 2 || !walk.Skip(size - 2))
                {
                    return false;
                }
            }

            marker = NextMarker(walk);
        }

        return false;
    }

    private static int NextMarker(Walk walk)
    {
        while (walk.SkipPast(0xFF))
        {
            var marker = walk.Next();
            while (marker == 0xFF)
            {
                marker = walk.Next();
            }

            if (marker != 0x00)
            {
                return marker;
            }
        }

        return -1;
    }

    /// <summary>The RIFF header declares the length of everything after its first eight bytes.</summary>
    private static bool WebPEnds(Stream stream)
    {
        stream.Position = 4;
        Span<byte> size = stackalloc byte[4];
        return stream.ReadAtLeast(size, size.Length, throwOnEndOfStream: false) == size.Length
            && stream.Length >= 8L + BinaryPrimitives.ReadUInt32LittleEndian(size);
    }

    /// <summary>Forward reading through a buffer of its own, so a walk over a large file costs little more than the read.</summary>
    private sealed class Walk(Stream stream)
    {
        private readonly byte[] buffer = new byte[64 * 1024];
        private int start;
        private int end;

        /// <summary>The next byte, or -1 at the end.</summary>
        public int Next() => start < end || Fill() ? buffer[start++] : -1;

        public bool Read(Span<byte> into)
        {
            for (var i = 0; i < into.Length; i++)
            {
                var value = Next();
                if (value < 0)
                {
                    return false;
                }

                into[i] = (byte)value;
            }

            return true;
        }

        /// <summary>Passes over <paramref name="count"/> bytes; false when the stream ends first.</summary>
        public bool Skip(long count)
        {
            var buffered = end - start;
            if (count <= buffered)
            {
                start += (int)count;
                return true;
            }

            start = end;
            var target = stream.Position + count - buffered;
            if (target > stream.Length)
            {
                return false;
            }

            stream.Position = target;
            return true;
        }

        /// <summary>Passes over everything up to and including the next <paramref name="value"/>; false when there is none.</summary>
        public bool SkipPast(byte value)
        {
            while (true)
            {
                var found = buffer.AsSpan(start, end - start).IndexOf(value);
                if (found >= 0)
                {
                    start += found + 1;
                    return true;
                }

                start = end;
                if (!Fill())
                {
                    return false;
                }
            }
        }

        private bool Fill()
        {
            start = 0;
            end = stream.Read(buffer, 0, buffer.Length);
            return end > 0;
        }
    }
}
