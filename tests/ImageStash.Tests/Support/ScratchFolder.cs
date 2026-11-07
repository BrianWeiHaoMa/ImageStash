namespace ImageStash.Tests.Support;

/// <summary>A fresh folder under %TEMP% for one check, outside OneDrive, removed afterwards.</summary>
public sealed class ScratchFolder : IDisposable
{
    public ScratchFolder() => Directory.CreateDirectory(Path);

    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ImageStash.Tests", Guid.NewGuid().ToString("N"));

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose() => Directory.Delete(Path, recursive: true);
}
