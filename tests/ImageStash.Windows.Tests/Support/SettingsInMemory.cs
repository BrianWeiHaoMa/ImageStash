using ImageStash.Files;
using ImageStash.Store;

namespace ImageStash.Windows.Tests.Support;

/// <summary>Settings at their defaults, held in memory and never written, for checks of parts that read them but are not about them.</summary>
public static class SettingsInMemory
{
    public static SettingsKeeper Defaults() =>
        new("", new SettingsLoad(new Settings(), null, AsSaved: false), HeldFileRetry.Duration, TimeSpan.Zero, _ => { });
}
