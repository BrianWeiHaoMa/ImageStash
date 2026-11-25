using ImageStash.Seams;

namespace ImageStash.Tests.Support;

/// <summary>
/// A picture dropped as a browser hands one over, a stream with no path on this PC, whose bytes the check releases: the
/// import reading it waits until <see cref="Release"/>, never on a sleep, so a check can act on the window while the
/// batch runs and before it ends.
/// </summary>
public sealed class HeldBackFile
{
    private readonly TaskCompletionSource requested = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <param name="path">The picture whose bytes are handed over once released.</param>
    /// <param name="name">The name the drop gives it; by default the picture's own.</param>
    public HeldBackFile(string path, string? name = null) =>
        Item = new DroppedItem(name ?? Path.GetFileName(path), null, async () =>
        {
            requested.TrySetResult();
            await released.Task;
            return File.OpenRead(path);
        }, IsFolder: false);

    /// <summary>The dropped file.</summary>
    public DroppedItem Item { get; }

    /// <summary>A drop of <see cref="Item"/> alone.</summary>
    public DropContents Drop => new([Item], false, false, []);

    /// <summary>Completes when the import asks for this file's bytes.</summary>
    public Task Requested => requested.Task;

    /// <summary>Hands the file's bytes over: the batch reading it goes on.</summary>
    public void Release() => released.TrySetResult();
}
