using System.Text.RegularExpressions;
using ImageStash.Store;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests;

/// <summary>
/// The running build, read in one place from what <c>&lt;Version&gt;</c> in <c>Directory.Build.props</c> gave the build:
/// the Settings page's foot names it as a release is named, in text alone (N2, N8), the export's manifest from the same
/// home, and no source file writes the version itself.
/// </summary>
[TestClass]
public sealed class RunningBuildTests
{
    private static readonly string Root = RepositoryRoot.Above(AppContext.BaseDirectory) ?? throw new InvalidOperationException("No repository above the checks.");

    private static string Given => Regex.Match(File.ReadAllText(Path.Combine(Root, "Directory.Build.props")), "<Version>([^<]+)</Version>").Groups[1].Value;

    [TestMethod]
    public void TheSettingsFootNamesTheBuildAsDirectoryBuildPropsGivesIt()
    {
        Assert.IsGreaterThan(0, Given.Length, "Directory.Build.props gives a version");

        Assert.AreEqual($"ImageStash {Given}", SettingsPage.VersionLine, "as a release is named: no fourth part, no commit");
        Assert.AreEqual(typeof(RunningBuild).Assembly.GetName().Version!.ToString(), StashArchive.Build, "the manifest's four parts, read from the same home");
        StringAssert.StartsWith(StashArchive.Build, Given.Split('-')[0], "the same version");
    }

    [TestMethod]
    public void NoSourceFileWritesTheVersion()
    {
        var written = new Regex($@"(?<![\d.]){Regex.Escape(Given)}(?![\d.])");

        var writing = Directory.EnumerateFiles(Path.Combine(Root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => part is "obj" or "bin"))
            .Where(path => written.IsMatch(File.ReadAllText(path)))
            .ToList();

        Assert.IsEmpty(writing, $"the version lives only in Directory.Build.props: {string.Join(", ", writing)}");
    }
}
