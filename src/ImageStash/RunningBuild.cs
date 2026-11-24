using System.Reflection;

namespace ImageStash;

/// <summary>
/// The build that is running, read in one place from what the build wrote into this assembly from <c>&lt;Version&gt;</c> in
/// <c>Directory.Build.props</c>, so nothing in the source writes a version: the Settings page's foot shows it as a release
/// is named (N8 admits the line, with no link and no check, N2), and an export's manifest names it as it always has.
/// </summary>
public static class RunningBuild
{
    /// <summary>The version as <c>&lt;Version&gt;</c> gives it and a release is named by, three parts and any label.</summary>
    public static string Version { get; } = ReleaseVersion(typeof(RunningBuild).Assembly);

    /// <summary>The assembly's four-part version, the form an export's manifest has always carried.</summary>
    public static string AssemblyVersion { get; } = typeof(RunningBuild).Assembly.GetName().Version?.ToString() ?? "unknown";

    /// <summary>
    /// The informational version without the "+" and source revision the SDK appends to <c>&lt;Version&gt;</c>, a version
    /// with a label kept whole; else the assembly version's first three parts.
    /// </summary>
    private static string ReleaseVersion(Assembly assembly) =>
        assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] is { Length: > 0 } given
            ? given
            : assembly.GetName().Version?.ToString(3) ?? "unknown";
}
