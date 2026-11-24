using ImageStash.Media;

namespace ImageStash.Tests.Support;

/// <summary>A clip as Media's GIF encoder handed it on, checked against Media's own reading of the file it wrote.</summary>
public static class WrittenClips
{
    /// <summary>
    /// <paramref name="clip"/> is <paramref name="expected"/>'s file, frame count and length, and states the facts and the
    /// content hash that <see cref="MediaDecoder.ReadFacts"/> reads from that file (G12, G19, F3).
    /// </summary>
    public static void AreAsRead(GifClip expected, GifClip? clip, string? because = null)
    {
        Assert.IsNotNull(clip, because);
        Assert.AreEqual(expected, clip with { Written = null }, because);
        var read = MediaDecoder.ReadFacts(clip.WorkPath!, "clip", TestStash.ShortRetry);
        Assert.IsTrue(read.Succeeded, read.Failure?.ToString());
        Assert.IsNotNull(clip.Written, "a clip written to its file states its facts and hash");
        Assert.AreEqual(read.Value!.Facts, clip.Written.Facts, $"the facts Media reads from the file. {because}");
        Assert.AreEqual(Convert.ToHexString(read.Value.Sha256), Convert.ToHexString(clip.Written.Sha256), $"the file's content hash. {because}");
    }
}
