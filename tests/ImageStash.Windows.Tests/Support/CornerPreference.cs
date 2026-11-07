using System.Runtime.InteropServices;

namespace ImageStash.Windows.Tests.Support;

/// <summary>
/// The corner preference Windows holds for a window (DWMWA_WINDOW_CORNER_PREFERENCE), read back: what a window asked
/// to be rounded as Windows 11 rounds its own windows. Windows draws it only where it rounds windows at all.
/// </summary>
public static class CornerPreference
{
    /// <summary>DWMWCP_DEFAULT: left to Windows, which leaves a borderless window square.</summary>
    public const int Default = 0;

    /// <summary>DWMWCP_ROUND: rounded as Windows rounds its own windows.</summary>
    public const int Round = 2;

    private const int WindowCornerPreference = 33;

    /// <summary>The preference Windows holds for <paramref name="window"/>; a failed read fails the check.</summary>
    public static int Of(nint window)
    {
        var result = DwmGetWindowAttribute(window, WindowCornerPreference, out var preference, sizeof(int));
        Assert.AreEqual(0, result, $"Windows did not answer the window's corner preference: 0x{result:X8}");
        return preference;
    }

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmGetWindowAttribute(nint window, int attribute, out int value, int size);
}
