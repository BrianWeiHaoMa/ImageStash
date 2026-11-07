namespace ImageStash.Tests.Support;

/// <summary>The repository's root, found from the checks' own folder: checks that read the sources or the tools start there.</summary>
public static class RepositoryRoot
{
    /// <summary>The folder at or above <paramref name="start"/> that holds ImageStash.sln, or null.</summary>
    public static string? Above(string start)
    {
        for (var folder = new DirectoryInfo(start); folder is not null; folder = folder.Parent)
        {
            if (File.Exists(Path.Combine(folder.FullName, "ImageStash.sln")))
                return folder.FullName.TrimEnd(Path.DirectorySeparatorChar);
        }
        return null;
    }
}
