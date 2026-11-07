using ImageStash.Media;
using ImageStash.Store;

namespace ImageStash.Tests.Store;

/// <summary>F3 and N7: an edited item's finished picture is in its original's format, except WebP, which becomes a GIF for an animation and a PNG for a still.</summary>
[TestClass]
public sealed class FinishedFormatTests
{
    [TestMethod]
    [DataRow(ImageFormat.Png, false, ImageFormat.Png, ".png")]
    [DataRow(ImageFormat.Jpeg, false, ImageFormat.Jpeg, ".jpg")]
    [DataRow(ImageFormat.Gif, false, ImageFormat.Gif, ".gif")]
    [DataRow(ImageFormat.Gif, true, ImageFormat.Gif, ".gif")]
    [DataRow(ImageFormat.WebP, false, ImageFormat.Png, ".png")]
    [DataRow(ImageFormat.WebP, true, ImageFormat.Gif, ".gif")]
    public void TheFinishedPictureIsInTheOriginalsFormatExceptWebP(ImageFormat original, bool animated, ImageFormat finished, string extension)
    {
        var format = FinishedFormat.Of(new MediaFacts(original, 8, 6, animated, animated ? 3 : 1, animated ? 600 : 0));

        Assert.AreEqual(finished, format);
        Assert.AreEqual(extension, FinishedFormat.Extension(format));
    }
}
