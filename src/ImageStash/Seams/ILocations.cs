namespace ImageStash.Seams;

/// <summary>The default places ImageStash keeps its files for the signed-in user (F6).</summary>
public interface ILocations
{
    /// <summary>The stash folder used until Settings moves it.</summary>
    string DefaultStashFolder { get; }

    /// <summary>The folder holding the settings file and everything else outside the stash.</summary>
    string SettingsFolder { get; }
}
