namespace ImageStash.Windows.Tests.Support;

/// <summary>Small files in a fresh folder under %TEMP%, for a write to put on the clipboard; removed afterwards.</summary>
public sealed class CopiedFiles : IDisposable
{
    private readonly string folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ImageStash.Windows.Tests", Guid.NewGuid().ToString("N"));

    public CopiedFiles() => Directory.CreateDirectory(folder);

    public string Path(string name)
    {
        var path = System.IO.Path.Combine(folder, name);
        File.WriteAllBytes(path, [1, 2, 3]);
        return path;
    }

    public void Dispose() => Directory.Delete(folder, recursive: true);
}
