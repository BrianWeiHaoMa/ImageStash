using ImageStash.Seams;

namespace ImageStash.Window;

/// <summary>
/// The Run entry's one rule (F1): while Start with Windows is on, the entry is set to the running executable's path, so a
/// moved folder still starts; while it is off, the entry is removed. Settings' switch, the start and an import's settings
/// taking effect all apply it (G32, G36); each names a refusal in its own place, so the rule only answers it.
/// </summary>
public static class StartWithWindowsRule
{
    /// <summary>The Run entry set to <paramref name="executablePath"/> while <paramref name="on"/>, else cleared; the seam's answer.</summary>
    public static Outcome Apply(bool on, IStartWithWindows startWithWindows, string executablePath) =>
        on ? startWithWindows.Set(executablePath) : startWithWindows.Clear();
}
