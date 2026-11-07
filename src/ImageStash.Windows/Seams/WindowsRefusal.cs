using System.ComponentModel;

namespace ImageStash.Windows.Seams;

/// <summary>Why Windows refused a call, in the words a failure carries (G35).</summary>
internal static class WindowsRefusal
{
    /// <summary>A Win32 error code in words; 0, when Windows set none, said so.</summary>
    public static string ReasonFor(int error) =>
        error == 0 ? "Windows refused it without saying why" : new Win32Exception(error).Message;
}
