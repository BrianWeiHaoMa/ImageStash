using System.Drawing;

namespace ImageStash.Media;

/// <summary>
/// Media's median cut (the G14, G15 entry): the pixels of one image of a GIF, too colourful for one palette, quantized
/// from those pixels alone into at most <see cref="PaletteSize"/> entries, without dithering, so every pixel of one colour
/// gets one index. Each colour is counted in a box of 32 steps a channel; the box whose pixels and widest side weigh most
/// is cut across that side where half its pixels lie, until the palette is full or no box can be cut; each box's entry is
/// the mean of the exact colours of its pixels, so a box of one colour keeps it exactly. A pixel below half alpha takes
/// the last entry, transparent; every other one counts by its straight colour, its alpha not read (G21). One instance
/// serves one encoder: its tables are kept for the whole clip (R7), and calls take turns.
/// </summary>
public sealed class MedianCut
{
    /// <summary>The most entries a palette holds, the transparent one among them: a GIF's local colour table.</summary>
    public const int PaletteSize = 256;

    private const uint OpaqueFrom = 128;

    // Five bits of each channel: 32,768 boxes of one step, the finest a cut makes.
    private const int Steps = 32;

    private readonly int[] counts = new int[Steps * Steps * Steps];

    // The exact colours' sums in each step, blue, green and red, for the entries' means.
    private readonly long[] sums = new long[Steps * Steps * Steps * 3];

    private readonly byte[] entryOf = new byte[Steps * Steps * Steps];
    private readonly long[] slices = new long[Steps];
    private readonly Box[] boxes = new Box[PaletteSize];

    /// <summary>
    /// Quantizes the <paramref name="area"/> of <paramref name="pixels"/>, rows of <paramref name="stride"/> pixels each
    /// read as B | G &lt;&lt; 8 | R &lt;&lt; 16 | A &lt;&lt; 24: each pixel's entry into <paramref name="indices"/>, row by row
    /// with no gap, and the entries into <paramref name="palette"/>, opaque colours read the same way, then 0 for the
    /// transparent entry when a pixel takes it. Returns how many entries the palette holds.
    /// </summary>
    public int Quantize(ReadOnlySpan<uint> pixels, int stride, Rectangle area, Span<byte> indices, Span<uint> palette)
    {
        Array.Clear(counts);
        Array.Clear(sums);
        var transparent = false;
        for (var y = 0; y < area.Height; y++)
        {
            foreach (var pixel in pixels.Slice((area.Top + y) * stride + area.Left, area.Width))
            {
                if (pixel >> 24 < OpaqueFrom)
                {
                    transparent = true;
                    continue;
                }

                var step = StepOf(pixel);
                counts[step]++;
                sums[step * 3] += pixel & 0xFF;
                sums[step * 3 + 1] += pixel >> 8 & 0xFF;
                sums[step * 3 + 2] += pixel >> 16 & 0xFF;
            }
        }

        var cut = transparent ? Cut(PaletteSize - 1) : Cut(PaletteSize);
        for (var i = 0; i < cut; i++)
        {
            palette[i] = Entry(boxes[i], (byte)i);
        }

        var transparentEntry = (byte)cut;
        if (transparent)
        {
            palette[cut] = 0;
        }

        var at = 0;
        for (var y = 0; y < area.Height; y++)
        {
            foreach (var pixel in pixels.Slice((area.Top + y) * stride + area.Left, area.Width))
            {
                indices[at++] = pixel >> 24 < OpaqueFrom ? transparentEntry : entryOf[StepOf(pixel)];
            }
        }

        return transparent ? cut + 1 : cut;
    }

    private static int StepOf(uint pixel) => (int)((pixel >> 19 & 31) << 10 | (pixel >> 11 & 31) << 5 | (pixel >> 3 & 31));

    /// <summary>Cuts the counted colours into at most <paramref name="most"/> boxes; returns how many there are, none when no pixel is opaque.</summary>
    private int Cut(int most)
    {
        var whole = Tightened(new Box(0, Steps - 1, 0, Steps - 1, 0, Steps - 1, 0));
        if (whole.Count == 0)
        {
            return 0;
        }

        boxes[0] = whole;
        var made = 1;
        while (made < most)
        {
            var heaviest = 0;
            for (var i = 1; i < made; i++)
            {
                if (boxes[i].Weight > boxes[heaviest].Weight)
                {
                    heaviest = i;
                }
            }

            if (boxes[heaviest].Weight == 0)
            {
                break;
            }

            (boxes[heaviest], boxes[made]) = Halves(boxes[heaviest]);
            made++;
        }

        return made;
    }

    /// <summary>
    /// The box cut across its widest side, green first, then red, then blue among equals, after the step where the pixels
    /// counted from its low end first reach half; it was tightened, so both its end steps hold pixels and neither half is empty.
    /// </summary>
    private (Box Low, Box High) Halves(Box box)
    {
        var (r, g, b) = (box.R1 - box.R0, box.G1 - box.G0, box.B1 - box.B0);
        var axis = g >= r && g >= b ? 1 : r >= b ? 2 : 0;
        var (from, to) = axis switch { 1 => (box.G0, box.G1), 2 => (box.R0, box.R1), _ => (box.B0, box.B1) };
        Array.Clear(slices);
        for (var ri = box.R0; ri <= box.R1; ri++)
        {
            for (var gi = box.G0; gi <= box.G1; gi++)
            {
                for (var bi = box.B0; bi <= box.B1; bi++)
                {
                    slices[axis switch { 1 => gi, 2 => ri, _ => bi }] += counts[ri << 10 | gi << 5 | bi];
                }
            }
        }

        var cutAfter = from;
        for (long below = slices[from]; cutAfter < to - 1 && 2 * below < box.Count; below += slices[cutAfter])
        {
            cutAfter++;
        }

        var (low, high) = axis switch
        {
            1 => (box with { G1 = cutAfter }, box with { G0 = cutAfter + 1 }),
            2 => (box with { R1 = cutAfter }, box with { R0 = cutAfter + 1 }),
            _ => (box with { B1 = cutAfter }, box with { B0 = cutAfter + 1 }),
        };
        return (Tightened(low), Tightened(high));
    }

    /// <summary>The box shrunk to the steps that hold pixels, with their count; a box holding none keeps its bounds and counts 0.</summary>
    private Box Tightened(Box box)
    {
        int r0 = Steps, r1 = -1, g0 = Steps, g1 = -1, b0 = Steps, b1 = -1;
        long count = 0;
        for (var ri = box.R0; ri <= box.R1; ri++)
        {
            for (var gi = box.G0; gi <= box.G1; gi++)
            {
                for (var bi = box.B0; bi <= box.B1; bi++)
                {
                    var n = counts[ri << 10 | gi << 5 | bi];
                    if (n > 0)
                    {
                        count += n;
                        (r0, r1) = (Math.Min(r0, ri), Math.Max(r1, ri));
                        (g0, g1) = (Math.Min(g0, gi), Math.Max(g1, gi));
                        (b0, b1) = (Math.Min(b0, bi), Math.Max(b1, bi));
                    }
                }
            }
        }

        return count == 0 ? box : new Box(r0, r1, g0, g1, b0, b1, count);
    }

    /// <summary>The box's entry, the mean of its pixels' exact colours rounded, opaque; its steps marked with <paramref name="index"/>.</summary>
    private uint Entry(Box box, byte index)
    {
        long blue = 0, green = 0, red = 0;
        for (var ri = box.R0; ri <= box.R1; ri++)
        {
            for (var gi = box.G0; gi <= box.G1; gi++)
            {
                for (var bi = box.B0; bi <= box.B1; bi++)
                {
                    var step = ri << 10 | gi << 5 | bi;
                    entryOf[step] = index;
                    (blue, green, red) = (blue + sums[step * 3], green + sums[step * 3 + 1], red + sums[step * 3 + 2]);
                }
            }
        }

        return 0xFF000000 | Mean(red, box.Count) << 16 | Mean(green, box.Count) << 8 | Mean(blue, box.Count);
    }

    private static uint Mean(long sum, long count) => (uint)((sum + count / 2) / count);

    /// <summary>
    /// A box of steps, each bound inclusive, and how many pixels it holds; it weighs its count times its widest side in
    /// steps, so a box of one step, which cannot be cut, weighs nothing.
    /// </summary>
    private readonly record struct Box(int R0, int R1, int G0, int G1, int B0, int B1, long Count)
    {
        public long Weight => Count * Math.Max(R1 - R0, Math.Max(G1 - G0, B1 - B0));
    }
}
