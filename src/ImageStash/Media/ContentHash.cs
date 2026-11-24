using System.Security.Cryptography;

namespace ImageStash.Media;

/// <summary>
/// The content hash (Composition: Media), its one home: the SHA-256 of a file's bytes, which G12's duplicate test and an
/// original's check against the store's record compare (How G12 holds).
/// </summary>
public static class ContentHash
{
    /// <summary>The hash of the bytes from <paramref name="stream"/>'s position to its end, read through it. A read error throws.</summary>
    public static byte[] Of(Stream stream) => SHA256.HashData(stream);

    /// <summary>
    /// The same hash taken as a file is written: each write's bytes appended in the order they reach the file, which the
    /// writer fills front to back without seeking back, finished once the file is whole. It holds the hash's state alone,
    /// never the bytes (R7).
    /// </summary>
    public static IncrementalHash WhileWriting() => IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
}
