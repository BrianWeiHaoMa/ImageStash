using System.Security;
using ImageStash.Seams;
using Microsoft.Win32;

namespace ImageStash.Windows.Seams;

/// <summary>A value in the current user's <c>Run</c> key, which starts it at that user's sign-in without administrator rights (S8).</summary>
public sealed class WindowsStartWithWindows : IStartWithWindows
{
    public const string ProductValueName = "ImageStash";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private readonly string valueName;

    /// <param name="valueName">The Run value's name; checks pass a name of their own so the product's entry is untouched.</param>
    public WindowsStartWithWindows(string valueName) => this.valueName = valueName;

    public Outcome Set(string executablePath) => Change("Start with Windows", () =>
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        key.SetValue(valueName, $"\"{executablePath}\"", RegistryValueKind.String);
    });

    public Outcome Clear() => Change("Stop starting with Windows", () =>
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        key?.DeleteValue(valueName, throwOnMissingValue: false);
    });

    private Outcome Change(string action, Action change)
    {
        try
        {
            change();
            return Outcome.Done;
        }
        catch (Exception e) when (e is UnauthorizedAccessException or SecurityException or IOException)
        {
            return new Failure(action, $"Run entry {valueName}", "Windows refused the change");
        }
    }
}
