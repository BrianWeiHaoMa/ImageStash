using ImageStash.Files;
using ImageStash.Media;
using ImageStash.Tests.Support;

namespace ImageStash.Tests.Media;

/// <summary>
/// The picture header's read of a file's facts without decoding it: what a full decode answers, for every whole fixture,
/// and a header that declares more pixels than this PC decodes; bytes it cannot read refused as unreadable and a file
/// held past the retry named as held (G35, E4).
/// </summary>
[TestClass]
public sealed class PictureHeaderTests
{
    private static readonly TimeSpan Retry = TimeSpan.FromSeconds(5);

    [TestMethod]
    public void ThePictureHeaderAnswersTheFactsAFullReadAnswersWithoutDecoding()
    {
        foreach (var row in MediaFixtures.Whole.Where(row => (string)row[0] != "huge.png"))
        {
            var fixture = (string)row[0];
            var header = PictureHeader.Read(MediaFixtures.Path(fixture), fixture, Retry);
            Assert.AreEqual(Facts(fixture), header.Value, header.Failure?.Sentence ?? fixture);
        }

        // A header that declares more pixels than this PC decodes still answers what it declares: nothing is allocated.
        Assert.AreEqual(new MediaFacts(ImageFormat.Png, 60000, 60000, false, 1, 0), PictureHeader.Read(MediaFixtures.Path("huge.png"), "huge.png", Retry).Value);
    }

    [TestMethod]
    public void ThePictureHeaderNamesBytesItCannotReadAndAFileHeldPastTheRetry()
    {
        using var scratch = new ScratchFolder();
        var garbage = scratch.File("garbage.jpg");
        File.WriteAllBytes(garbage, [.. Enumerable.Range(0, 300).Select(i => (byte)(i * 31))]);
        var held = Copy(scratch, "oriented-6.jpg");

        AssertUnreadable(PictureHeader.Read(garbage, "noise.jpg", Retry).Failure, "noise.jpg");
        using (new FileStream(held, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.AreEqual(new Failure("Read the picture", "portrait.jpg", HeldFileRetry.HeldReason), PictureHeader.Read(held, "portrait.jpg", TimeSpan.FromMilliseconds(300)).Failure);
        }

        Assert.AreEqual(new MediaFacts(ImageFormat.Jpeg, 24, 40, false, 1, 0), PictureHeader.Read(held, "portrait.jpg", Retry).Value, "read once let go");
    }

    private static MediaFacts Facts(string fixture)
    {
        var read = MediaDecoder.ReadFacts(MediaFixtures.Path(fixture), fixture, Retry);
        Assert.IsTrue(read.Succeeded, read.Failure?.ToString());
        return read.Value!.Facts;
    }

    private static void AssertUnreadable(Failure? failure, string name)
    {
        Assert.AreEqual(new Failure("Read the picture", name, MediaDecoder.UnreadableReason), failure);
        Assert.IsTrue(MediaDecoder.IsUnreadable(failure!));
    }

    private static string Copy(ScratchFolder scratch, string fixture)
    {
        var path = scratch.File(Guid.NewGuid().ToString("N") + Path.GetExtension(fixture));
        File.Copy(MediaFixtures.Path(fixture), path);
        return path;
    }
}
