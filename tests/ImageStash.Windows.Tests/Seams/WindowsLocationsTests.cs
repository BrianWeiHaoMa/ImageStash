using ImageStash.Windows.Seams;

namespace ImageStash.Windows.Tests.Seams;

/// <summary>F6: settings under %LOCALAPPDATA%\ImageStash and the default stash in its stash folder, one per Windows account.</summary>
[TestClass]
public sealed class WindowsLocationsTests
{
    [TestMethod]
    public void BothFoldersAreUnderTheUsersLocalAppData()
    {
        var localAppData = Environment.GetEnvironmentVariable("LOCALAPPDATA")!;
        var locations = new WindowsLocations();

        Assert.AreEqual(Path.Combine(localAppData, "ImageStash"), locations.SettingsFolder, ignoreCase: true);
        Assert.AreEqual(Path.Combine(localAppData, "ImageStash", "stash"), locations.DefaultStashFolder, ignoreCase: true);
    }
}
