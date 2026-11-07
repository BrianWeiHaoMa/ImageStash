using ImageStash.Media;

namespace ImageStash.Tests.Media;

/// <summary>
/// F3 and N4, by name alone: the five extensions of the four formats, in any case, are taken; videos and sound files
/// are refused as such, and an Internet shortcut as a link (N2); anything else is refused by its extension, and a
/// name without one is refused too.
/// </summary>
[TestClass]
public sealed class FormatByNameTests
{
    [TestMethod]
    [DataRow("A.PNG", ImageFormat.Png)]
    [DataRow("b.Jpeg", ImageFormat.Jpeg)]
    [DataRow("photo.jpg", ImageFormat.Jpeg)]
    [DataRow("dance.GIF", ImageFormat.Gif)]
    [DataRow("cat.webp", ImageFormat.WebP)]
    [DataRow("two.dots.png", ImageFormat.Png)]
    public void TheFourFormatsAreTakenByTheirExtensionsInAnyCase(string name, ImageFormat format)
    {
        Assert.AreEqual(new FormatVerdict(format, null), FormatByName.Judge(name));
        Assert.IsTrue(FormatByName.Judge(name).Supported);
    }

    [TestMethod]
    [DataRow("clip.mp4", "a video")]
    [DataRow("clip.WebM", "a video")]
    [DataRow("clip.mov", "a video")]
    [DataRow("song.mp3", "a sound file")]
    [DataRow("song.WAV", "a sound file")]
    [DataRow("cat pictures.URL", "a link")]
    [DataRow("notes.txt", "a .txt file")]
    [DataRow("picture.bmp", "a .bmp file")]
    [DataRow("photo.png.exe", "a .exe file")]
    [DataRow("README", "a file without an extension")]
    [DataRow("trailing.", "a file without an extension")]
    public void AnythingElseIsRefusedWithTheTypeToName(string name, string type)
    {
        var verdict = FormatByName.Judge(name);

        Assert.AreEqual(new FormatVerdict(null, type), verdict);
        Assert.IsFalse(verdict.Supported);
    }
}
