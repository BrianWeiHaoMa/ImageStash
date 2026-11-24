namespace ImageStash.Seams;

/// <summary>Starting ImageStash when the user signs in (F1).</summary>
public interface IStartWithWindows
{
    /// <summary>
    /// Registers this executable path to start at sign-in, given <paramref name="argument"/>, replacing whatever was
    /// registered before.
    /// </summary>
    Outcome Set(string executablePath, string argument);

    /// <summary>Removes the registration; done when there was none.</summary>
    Outcome Clear();
}
