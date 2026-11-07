using ImageStash.Seams;

namespace ImageStash.Windows.Seams;

/// <summary>Everything under <c>%LOCALAPPDATA%\ImageStash</c>, which gives each Windows account its own stash (F6).</summary>
public sealed class WindowsLocations : ILocations
{
    public string SettingsFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify),
        "ImageStash");

    public string DefaultStashFolder => Path.Combine(SettingsFolder, "stash");
}
