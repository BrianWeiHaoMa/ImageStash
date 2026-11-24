using ImageStash.Windows.Seams;
using Microsoft.Win32;

namespace ImageStash.Windows.Tests.Seams;

/// <summary>
/// F1, S8: the user's Run entry, a command line Windows runs at sign-in, names the running executable in quotes, so a
/// path holding a space still starts, then the argument that marks the start at sign-in (State: Settings), after every
/// set; it is gone after a clear.
/// </summary>
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
    public void SetReplacesAnEarlierBuildsBarePathWithTheQuotedExecutableAndTheArgument()
    {
        using (var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true))
        {
            key.SetValue(valueName, @"""C:\Moved away\ImageStash.Windows.exe""");
        }

        var executable = @"C:\Users\some one\My Apps\ImageStash\ImageStash.Windows.exe";
        var outcome = new WindowsStartWithWindows(valueName).Set(executable, Launch.SignInArgument);

        Assert.IsTrue(outcome.Succeeded);
        Assert.AreEqual($"\"{executable}\" {Launch.SignInArgument}", RunValue(), "the path in quotes, a space, the argument (S8)");
    }

    [TestMethod]
    public void ClearRemovesTheEntryAndIsDoneWhenThereIsNone()
    {
        var seam = new WindowsStartWithWindows(valueName);
        seam.Set(@"C:\Apps\ImageStash.Windows.exe", Launch.SignInArgument);

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
