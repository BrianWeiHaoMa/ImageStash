namespace ImageStash.Seams;

/// <summary>Starting ImageStash when the user signs in (F1).</summary>
public interface IStartWithWindows
{
    /// <summary>Registers this executable path to start at sign-in, replacing whatever path was registered before.</summary>
    Outcome Set(string executablePath);

    /// <summary>Removes the registration; done when there was none.</summary>
    Outcome Clear();
}
