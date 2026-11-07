using ImageStash.Windows.Seams;
using Microsoft.Win32;

namespace ImageStash.Windows.Tests.Seams;

/// <summary>F1, S8: the user's Run entry names the running executable after every set and is gone after a clear.</summary>
[TestClass]
public sealed class WindowsStartWithWindowsTests
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    // A value of the check's own in the real Run key, so the product's ImageStash entry is never touched.
    private readonly string valueName = "ImageStash.Tests." + Guid.NewGuid().ToString("N");

    [TestCleanup]
    public void RemoveTheChecksValue()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        key?.DeleteValue(valueName, throwOnMissingValue: false);
    }

    [TestMethod]
    public void SetReplacesAWrongPathWithTheQuotedExecutable()
    {
        using (var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true))
        {
            key.SetValue(valueName, @"""C:\Moved away\ImageStash.Windows.exe""");
        }

        var executable = @"C:\Users\someone\Apps\ImageStash\ImageStash.Windows.exe";
        var outcome = new WindowsStartWithWindows(valueName).Set(executable);

        Assert.IsTrue(outcome.Succeeded);
        Assert.AreEqual($"\"{executable}\"", RunValue());
    }

    [TestMethod]
    public void ClearRemovesTheEntryAndIsDoneWhenThereIsNone()
    {
        var seam = new WindowsStartWithWindows(valueName);
        seam.Set(@"C:\Apps\ImageStash.Windows.exe");

        Assert.IsTrue(seam.Clear().Succeeded);
        Assert.IsNull(RunValue());
        Assert.IsTrue(seam.Clear().Succeeded);
        Assert.IsNull(RunValue());
    }

    private string? RunValue()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(valueName) as string;
    }
}
