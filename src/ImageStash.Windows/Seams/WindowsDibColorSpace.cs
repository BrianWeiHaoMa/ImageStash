using System.Buffers.Binary;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace ImageStash.Windows.Seams;

/// <summary>
/// A clipboard bitmap's colors brought into sRGB, as the pixel contract has them: a V4 or V5 header's calibrated RGB
/// space (LCS_CALIBRATED_RGB: its primaries' CIE XYZ endpoints and a gamma per channel) or a V5 header's embedded ICC
/// profile is converted through Skia. sRGB, the Windows color space, a linked profile (a file the bitmap names, which
/// is not opened), a profile Skia cannot read, a calibrated space without endpoints or gammas, and a 40-byte header are
/// taken as sRGB already, as Windows itself takes a bitmap with no color space information.
/// </summary>
public static class WindowsDibColorSpace
{
    private const uint CalibratedRgb = 0;
    private const uint ProfileEmbedded = 0x4D424544; // 'MBED'
    private const uint ProfileLinked = 0x4C494E4B; // 'LINK'

    /// <summary>The Bradford cone response, and its inverse, with which a white point is adapted to D50 as ICC does.</summary>
    private static readonly double[,] Bradford = { { 0.8951, 0.2664, -0.1614 }, { -0.7502, 1.7135, 0.0367 }, { 0.0389, -0.0685, 1.0296 } };
    private static readonly double[,] BradfordInverse = { { 0.9869929, -0.1470543, 0.1599627 }, { 0.4323053, 0.5183603, 0.0492912 }, { -0.0085287, 0.0400428, 0.9684867 } };
    private static readonly double[] D50 = [0.9642, 1.0, 0.8249];

    /// <summary>Converts <paramref name="pixels"/>, BGRA with straight alpha, in place, from the color space <paramref name="dib"/>'s header declares.</summary>
    public static void ToSrgb(byte[] dib, uint headerSize, byte[] pixels, int width, int height)
    {
        if (headerSize < 108)
        {
            return;
        }

        var type = U32(dib, 56);
        if (type == CalibratedRgb)
        {
            Calibrated(dib, pixels, width, height);
        }
        else if (type == ProfileEmbedded && headerSize == 124)
        {
            Embedded(dib, pixels, width, height);
        }
    }

    /// <summary>Where a V5 header's embedded or linked profile starts, bV5ProfileData counted from the header's start; null when it names none.</summary>
    public static long? ProfileStart(byte[] dib, uint headerSize) =>
        headerSize == 124 && U32(dib, 56) is ProfileEmbedded or ProfileLinked ? U32(dib, 112) : null;

    /// <summary>The profile at bV5ProfileData (counted from the header's start), bV5ProfileSize bytes long.</summary>
    private static void Embedded(byte[] dib, byte[] pixels, int width, int height)
    {
        long offset = U32(dib, 112), size = U32(dib, 116);
        if (size == 0 || offset + size > dib.Length)
        {
            return;
        }

        // Parsed first: Skia's CreateIcc throws for a profile it cannot parse, rather than answering null.
        using var profile = SKColorSpaceIccProfile.Create(dib.AsSpan((int)offset, (int)size));
        using var space = profile is null ? null : SKColorSpace.CreateIcc(profile);
        if (space is null || space.IsSrgb)
        {
            return;
        }

        Convert((byte[])pixels.Clone(), new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Unpremul, space), pixels);
    }

    /// <summary>
    /// Each channel made linear by its own gamma (16.16 fixed point), then the primaries (2.30 fixed point XYZ, their sum
    /// the white point) adapted to D50 for Skia, which encodes the result as sRGB.
    /// </summary>
    private static void Calibrated(byte[] dib, byte[] pixels, int width, int height)
    {
        var primaries = new double[3, 3];
        for (var channel = 0; channel < 3; channel++)
        {
            for (var axis = 0; axis < 3; axis++)
            {
                primaries[axis, channel] = BinaryPrimitives.ReadInt32LittleEndian(dib.AsSpan(60 + channel * 12 + axis * 4)) / (double)(1 << 30);
            }
        }

        var gammas = new[] { U32(dib, 96) / 65536.0, U32(dib, 100) / 65536.0, U32(dib, 104) / 65536.0 };
        var white = new[] { Row(primaries, 0), Row(primaries, 1), Row(primaries, 2) };
        if (gammas.Any(gamma => gamma <= 0) || white[1] <= 0 || Math.Abs(Determinant(primaries)) < 1e-9)
        {
            return;
        }

        var cone = Multiply(Bradford, white);
        var target = Multiply(Bradford, D50);
        var scale = new double[3, 3];
        for (var i = 0; i < 3; i++)
        {
            scale[i, i] = target[i] / cone[i];
        }

        var toD50 = Multiply(Multiply(Multiply(BradfordInverse, scale), Bradford), primaries);
        if (toD50.Cast<double>().Any(value => !double.IsFinite(value)))
        {
            return;
        }

        var gamut = new SKColorSpaceXyz(
            (float)toD50[0, 0], (float)toD50[0, 1], (float)toD50[0, 2],
            (float)toD50[1, 0], (float)toD50[1, 1], (float)toD50[1, 2],
            (float)toD50[2, 0], (float)toD50[2, 1], (float)toD50[2, 2]);
        using var space = SKColorSpace.CreateRgb(SKColorSpaceTransferFn.Linear, gamut);
        if (space is null)
        {
            return;
        }

        // Linear light as RGBA floats, the gamma per channel applied here since Skia's transfer function is one for all.
        var linear = new float[width * height * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            linear[i] = (float)Math.Pow(pixels[i + 2] / 255.0, gammas[0]);
            linear[i + 1] = (float)Math.Pow(pixels[i + 1] / 255.0, gammas[1]);
            linear[i + 2] = (float)Math.Pow(pixels[i] / 255.0, gammas[2]);
            linear[i + 3] = pixels[i + 3] / 255f;
        }

        Convert(linear, new SKImageInfo(width, height, SKColorType.RgbaF32, SKAlphaType.Unpremul, space), pixels);
    }

    /// <summary>Skia's conversion of <paramref name="source"/> into <paramref name="pixels"/>, BGRA, straight, sRGB; left as it was if Skia refuses.</summary>
    private static void Convert(Array source, SKImageInfo sourceInfo, byte[] pixels)
    {
        var target = new SKImageInfo(sourceInfo.Width, sourceInfo.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul, SKColorSpace.CreateSrgb());
        var pinnedSource = GCHandle.Alloc(source, GCHandleType.Pinned);
        var pinnedTarget = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            using var pixmap = new SKPixmap(sourceInfo, pinnedSource.AddrOfPinnedObject(), sourceInfo.RowBytes);
            pixmap.ReadPixels(target, pinnedTarget.AddrOfPinnedObject(), target.RowBytes);
        }
        finally
        {
            pinnedSource.Free();
            pinnedTarget.Free();
        }
    }

    private static double Row(double[,] matrix, int row) => matrix[row, 0] + matrix[row, 1] + matrix[row, 2];

    private static double Determinant(double[,] m) =>
        m[0, 0] * (m[1, 1] * m[2, 2] - m[1, 2] * m[2, 1]) - m[0, 1] * (m[1, 0] * m[2, 2] - m[1, 2] * m[2, 0]) + m[0, 2] * (m[1, 0] * m[2, 1] - m[1, 1] * m[2, 0]);

    private static double[] Multiply(double[,] m, double[] v) =>
        [m[0, 0] * v[0] + m[0, 1] * v[1] + m[0, 2] * v[2], m[1, 0] * v[0] + m[1, 1] * v[1] + m[1, 2] * v[2], m[2, 0] * v[0] + m[2, 1] * v[1] + m[2, 2] * v[2]];

    private static double[,] Multiply(double[,] a, double[,] b)
    {
        var product = new double[3, 3];
        for (var i = 0; i < 3; i++)
        {
            for (var j = 0; j < 3; j++)
            {
                product[i, j] = a[i, 0] * b[0, j] + a[i, 1] * b[1, j] + a[i, 2] * b[2, j];
            }
        }

        return product;
    }

    private static uint U32(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));
}
