using ImageStash.Files;
using ImageStash.Seams;
using ImageStash.Store;

namespace ImageStash.Tests.Support;

/// <summary>
/// A Recycle Bin that moves each file, or each folder whole, into a folder of its own and returns the moved path as its
/// identity; a held file, or a folder holding one, is retried and then refused as the real one does, the paths
/// <see cref="Refusing"/> picks are refused, and those <see cref="RemovingForGood"/> picks are deleted and answered done
/// with no identity, as Windows answers for a file its bin cannot take. Removing an entry deletes what was moved; an
/// identity this bin did not give back is refused, as are those <see cref="RefusingRemoval"/> picks.
/// </summary>
public sealed class TestRecycleBin(string folder) : IRecycleBin
{
    public string Folder { get; } = folder;

    /// <summary>Every path the store asked to recycle, in order.</summary>
    public List<string> Asked { get; } = [];

    /// <summary>Every identity the store asked to remove for good, in order.</summary>
    public List<string> Removed { get; } = [];

    public Predicate<string> Refusing { get; set; } = _ => false;

    public Predicate<string> RefusingRemoval { get; set; } = _ => false;

    /// <summary>The paths, files or folders, removed for good instead of recycled (G29).</summary>
    public Predicate<string> RemovingForGood { get; set; } = _ => false;

    private readonly HashSet<string> given = [];

    /// <summary>Runs before each file is recycled, on the store's thread.</summary>
    public Action<string>? BeforeEach { get; set; }

    public IReadOnlyList<Outcome<RecycledFile>> Recycle(IReadOnlyList<string> paths, TimeSpan heldFileRetry)
    {
        Directory.CreateDirectory(Folder);
        return paths.Select(path =>
        {
            Asked.Add(path);
            BeforeEach?.Invoke(path);
            if (Refusing(path))
            {
                return new Failure("Move to the Recycle Bin", Path.GetFileName(path), "the test Recycle Bin refuses it");
            }

            if (RemovingForGood(path))
            {
                var removed = Directory.Exists(path)
                    ? StashImportFolders.RetryFolderStep("Move to the Recycle Bin", path, heldFileRetry, () => Directory.Delete(path, recursive: true))
                    : HeldFileRetry.Run("Move to the Recycle Bin", path, heldFileRetry, () =>
                    {
                        if (!File.Exists(path))
                        {
                            throw new FileNotFoundException(null, path);
                        }

                        File.Delete(path);
                    });
                return removed.Succeeded ? Outcome<RecycledFile>.Of(new RecycledFile(path, null)) : removed.Failure;
            }

            var identity = Path.Combine(Folder, Guid.NewGuid().ToString("N") + "-" + Path.GetFileName(path));
            if (Directory.Exists(path))
            {
                var moved = StashImportFolders.RetryFolderStep("Move to the Recycle Bin", path, heldFileRetry, () => Directory.Move(path, identity));
                if (!moved.Succeeded)
                {
                    return moved.Failure;
                }

                given.Add(identity);
                return Outcome<RecycledFile>.Of(new RecycledFile(path, identity));
            }

            return HeldFileRetry.Run("Move to the Recycle Bin", path, heldFileRetry, () =>
            {
                File.Move(path, identity);
                given.Add(identity);
                return new RecycledFile(path, identity);
            });
        }).ToList();
    }

    public IReadOnlyList<Outcome> Remove(IReadOnlyList<string> recycledIdentities, TimeSpan heldFileRetry) =>
        recycledIdentities.Select(identity =>
        {
            Removed.Add(identity);
            var name = Path.GetFileName(identity);
            if (!given.Contains(identity))
            {
                return new Failure("Remove from the Recycle Bin", name, "the test Recycle Bin did not give it back");
            }

            if (RefusingRemoval(identity))
            {
                return new Failure("Remove from the Recycle Bin", name, "the test Recycle Bin refuses to remove it");
            }

            if (Directory.Exists(identity))
            {
                return StashImportFolders.RetryFolderStep("Remove from the Recycle Bin", identity, heldFileRetry, () => Directory.Delete(identity, recursive: true));
            }

            return HeldFileRetry.Run("Remove from the Recycle Bin", identity, heldFileRetry, () =>
            {
                if (!File.Exists(identity))
                {
                    throw new FileNotFoundException(null, identity);
                }

                File.Delete(identity);
            });
        }).ToList();
}
