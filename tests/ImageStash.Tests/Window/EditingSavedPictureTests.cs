using System.Drawing;
using ImageStash.Editor;
using ImageStash.Files;
using ImageStash.Layers;
using ImageStash.Media;
using ImageStash.Store;
using ImageStash.Tests.Support;
using ImageStash.Window;
using static ImageStash.Tests.Support.EditorSessions;

namespace ImageStash.Tests.Window;

/// <summary>
/// The saved picture as every consumer sees it, headless (F3, N7, E4, G6, G8, G19, G22, G24): each original format saved
/// with a layer, its listed facts, badge, preview lines, handoff and thumbnail the saved file's; the footer's size and a
/// still's estimate the saved file's under crop, caption bars and Shrink; a trimmed, ranged clip keeping exactly its
/// kept frames at their own delays; an output past its format's side refused by Save and Save as new item alike.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class EditingSavedPictureTests
{
    [TestMethod]
    [DataRow("transparent.png", ImageFormat.Png, false)]
    [DataRow("photo.jpg", ImageFormat.Jpeg, false)]
    [DataRow("still.gif", ImageFormat.Gif, false)]
    [DataRow("animated.gif", ImageFormat.Gif, true)]
    [DataRow("transparent.webp", ImageFormat.Png, false)]
    [DataRow("still.webp", ImageFormat.Png, false)]
    [DataRow("animated.webp", ImageFormat.Gif, true)]
    [DataRow("animated.png", ImageFormat.Png, false)]
    public async Task EveryOriginalWithLayersReachesEveryConsumer(string fixture, ImageFormat format, bool animated)
    {
        using var listed = await ListedStash.Open();
        var item = await listed.Stash.ImportFixture("picture", fixture);
        var original = File.ReadAllBytes(item.FilePath);
        await listed.Listing.Refresh();
        Assert.IsTrue((await listed.Editing.Open(item, EditOrigin.Preview)).Succeeded);
        var session = listed.Editing.Session!;
        session.Tool = session.Hide();
        session.Hide().Choose(HideKind.Black);
        session.DragThrough((3, 3), (6, 5));
        Assert.HasCount(1, session.Layers.Layers);
        var layers = session.Layers;
        await Until(() => !session.Estimating && session.EstimatedBytes.HasValue, "estimate missing");
        var estimate = session.EstimatedBytes;
        var dimensions = session.OutputSize;
        Assert.IsTrue((await listed.Editing.Save()).Succeeded);
        var saved = listed.Listing.Items.Single();
        Assert.AreEqual(format, saved.Facts.Format, "F3 finished format");
        Assert.AreEqual(animated, saved.Facts.Animated, "F3 animation classification");
        Assert.AreEqual(dimensions, new PixelSize(saved.Facts.Width, saved.Facts.Height), "G24 footer");
        if (!animated) Assert.AreEqual(estimate, new FileInfo(saved.FilePath).Length, "Composition Media exact still estimate");
        var edit = (await listed.Stash.Store!.ForEdit(saved.Id)).Value!;
        CollectionAssert.AreEqual(original, File.ReadAllBytes(edit.OriginalPath), "F6 untouched original");
        Assert.AreEqual(layers, edit.Layers);
        await Consumers(listed, saved, layers);
    }

    [TestMethod]
    [DataRow(1, Shrink.ThreeQuarter)]
    [DataRow(1, Shrink.Half)]
    [DataRow(2, Shrink.ThreeQuarter)]
    [DataRow(2, Shrink.Half)]
    public async Task CropCaptionBarsAndShrinkAgreeWithSavedBytes(int bars, Shrink shrink)
    {
        using var listed = await ListedStash.Open();
        var item = await listed.Stash.ImportPicture("caption", "caption.png", StillInTheEditor.Gradient());
        await listed.Listing.Refresh();
        await listed.Editing.Open(item, EditOrigin.Grid);
        var session = listed.Editing.Session!;
        session.Tool = session.Crop();
        session.DragThrough((0, 0), (10, 8));
        Assert.AreEqual(new PixelRect(10, 8, 54, 40), session.Crop().Frame);
        session.Tool = session.Text();
        session.Text().Choose(TextKind.CaptionBar);
        for (var i = 0; i < bars; i++)
        {
            session.Click(40, 30);
            session.SetTypedText(i == 0 ? "First caption wraps over the cropped picture" : "Second bar\nAnother line");
            session.EndTyping();
        }
        Assert.HasCount(bars, session.Layers.Layers.OfType<CaptionBarLayer>());
        session.Shrink = shrink;
        var dimensions = session.OutputSize;
        var layers = session.Layers;
        await Until(() => !session.Estimating && session.EstimatedBytes.HasValue, "caption estimate missing");
        var estimate = session.EstimatedBytes;
        Assert.IsTrue((await listed.Editing.Save()).Succeeded);
        var saved = listed.Listing.Items.Single();
        Assert.AreEqual(dimensions, new PixelSize(saved.Facts.Width, saved.Facts.Height), "G24 footer dimensions versus saved file");
        Assert.AreEqual(estimate, new FileInfo(saved.FilePath).Length, "Composition Media exact still estimate");
        await Consumers(listed, saved, layers);
    }

    [TestMethod]
    [DataRow("animated.gif")]
    [DataRow("animated.webp")]
    public async Task TrimAndRangesKeepOriginalFramesAndDelays(string fixture)
    {
        using var listed = await ListedStash.Open();
        var item = await listed.Stash.ImportFixture("clip", fixture);
        var frames = MediaFixtures.Frames(item.FilePath);
        await listed.Listing.Refresh();
        await listed.Editing.Open(item, EditOrigin.Grid);
        var session = listed.Editing.Session!;
        session.Pause();
        session.Tool = session.Hide();
        session.Hide().Choose(HideKind.Black);
        session.DragThrough((3, 3), (6, 5));
        session.DragBarEnd(RangeEnd.First, 2);
        session.DragBarEnd(RangeEnd.Last, 2);
        session.Tool = session.Trim();
        session.Trim().DragHandle(RangeEnd.First, 1);
        var layers = session.Layers;
        Assert.AreEqual(new FrameRange(2, 2), layers.Layers.OfType<HideLayer>().Single().Range);
        Assert.IsTrue((await listed.Editing.Save()).Succeeded);
        var saved = listed.Listing.Items.Single();
        var finished = MediaFixtures.Frames(saved.FilePath);
        Assert.HasCount(2, finished, "G22 exactly frames between handles");
        CollectionAssert.AreEqual(frames.Skip(1).Select(f => f.DurationMs).ToArray(), finished.Select(f => f.DurationMs).ToArray(), "G22 original timing");
        CollectionAssert.AreEqual(frames.Skip(1).Select(f => f.DurationMs / 10).ToArray(), GifImages.Read(saved.FilePath).Images.Select(f => f.DelayCs).ToArray());
        CollectionAssert.AreEqual(frames[1].Pixels, finished[0].Pixels, "uncovered kept frame unchanged");
        Assert.AreEqual(0xff000000u, SyntheticFrames.Pixel(finished[1], 4, 4), "ranged hide at original index 2");
        Assert.AreEqual(SyntheticFrames.Pixel(frames[2], 0, 0), SyntheticFrames.Pixel(finished[1], 0, 0), "kept frame outside layer");
        await Consumers(listed, saved, layers);
    }

    [TestMethod]
    [DataRow(ImageFormat.Png, 1000000, false)]
    [DataRow(ImageFormat.Png, 1000000, true)]
    [DataRow(ImageFormat.Jpeg, 65500, false)]
    [DataRow(ImageFormat.Jpeg, 65500, true)]
    [DataRow(ImageFormat.Gif, 65535, false)]
    [DataRow(ImageFormat.Gif, 65535, true)]
    public async Task CaptionPastEncoderSideLimitIsRefusedByBothSavePaths(ImageFormat format, int limit, bool asNew)
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var fixture = format switch { ImageFormat.Jpeg => "photo.jpg", ImageFormat.Gif => "still.gif", _ => "still.png" };
        var item = await stash.ImportFixture("too tall", fixture);
        var bytes = File.ReadAllBytes(item.FilePath);
        // A valid live layer list, as may be restored from the editor's Cancel notice; the caption exceeds E4
        // before composition allocates its output surface.
        var layers = List(new CaptionBarLayer("X", "Segoe UI", limit, ColorChoices.Red, Outline.None));
        using var session = await Open(stash, item, startOn: layers);
        var result = asNew ? await session.SaveAsNewItem() : await session.Save();
        Assert.AreEqual(EditEnding.Refused, result.Ending, "E4 oversized output must be refused");
        StringAssert.Contains(result.Failure!.Reason, "at most", "G35 refusal explains format limit");
        Assert.IsTrue(session.IsOpen);
        Assert.AreEqual(layers, session.Layers);
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(item.FilePath));
        Assert.IsEmpty(stash.WorkFiles());
        Assert.HasCount(1, (await stash.Store!.All()).Value!);
    }

    private static async Task Consumers(ListedStash listed, StashItem item, LayerList layers)
    {
        var read = MediaDecoder.ReadFacts(item.FilePath, item.Name, TestStash.ShortRetry);
        Assert.IsTrue(read.Succeeded, read.Failure?.Sentence);
        Assert.AreEqual(read.Value!.Facts, item.Facts, "listed facts equal finished file");
        var badge = item.Facts.Animated ? item.Facts.Format == ImageFormat.WebP ? "WEBP" : "GIF" : null;
        Assert.AreEqual(badge, TileBadge.Of(item), "G6 badge names handoff format");
        var lines = PreviewFacts.Lines(item, PreviewFacts.FileLength(item.FilePath), new UseHistory(0, null), layers, listed.Stash.Clock.GetUtcNow(), TimeZoneInfo.Utc);
        StringAssert.Contains(lines[0], $"{item.Facts.Width} × {item.Facts.Height}");
        StringAssert.Contains(lines[0], FileSize.Words(new FileInfo(item.FilePath).Length));
        StringAssert.Contains(lines[0], PreviewFacts.Type(item.Facts));
        StringAssert.Contains(lines[2], "Edited here:");
        if (item.Facts.Animated) StringAssert.Contains(lines[0], ClipLength.Words(item.Facts.DurationMs));
        Assert.AreEqual(layers, (await PreviewFacts.EditedLayers(listed.Stash.Store!, item)).Value);
        Assert.IsTrue((await listed.Handoff.HandOver(null!, [item], null, () => Assert.Fail("headless copy hides nothing"))).Succeeded);
        var content = listed.Clipboard.Written.Last().Content;
        CollectionAssert.AreEqual(new[] { item.FilePath }, content.FilePaths.ToArray(), "G8 finished file");
        if (item.Facts.Animated)
        {
            Assert.IsNull(content.Png);
            Assert.IsNull(content.Bitmap);
        }
        else
        {
            Assert.IsNotNull(content.Png);
            Assert.IsNotNull(content.Bitmap);
            CollectionAssert.AreEqual(MediaFixtures.FirstFrame(item.FilePath).Pixels, content.Bitmap.Pixels, "G8 bitmap retains saved alpha");
        }
        var errors = new List<Failure>();
        var thumbs = new Thumbnails<MediaFrame>(1024 * 1024, 1, TestStash.ShortRetry, f => f, a => a(), _ => { }, errors.Add);
        var key = ThumbnailKey.Of(item, ThumbnailSize.Medium);
        thumbs.Want([new ThumbnailRequest(key, item.FilePath, item.Name, Math.Max(item.Facts.Width, item.Facts.Height))]);
        await thumbs.Idle();
        Assert.IsEmpty(errors);
        CollectionAssert.AreEqual(MediaFixtures.FirstFrame(item.FilePath).Pixels, thumbs.Get(key)!.Pixels, "thumbnail is finished picture");
    }

    [TestMethod]
    public async Task WebPTrimmedToOneFrameThenCopiedAndWidenedStaysInOriginalsFinishedFormat()
    {
        using var listed = await ListedStash.Open();
        var source = await listed.Stash.ImportFixture("web clip", "animated.webp");
        var original = File.ReadAllBytes(source.FilePath);
        await listed.Listing.Refresh();
        await listed.Editing.Open(source, EditOrigin.Grid);
        var session = listed.Editing.Session!;
        session.Tool = session.Trim();
        session.Trim().DragHandle(RangeEnd.Last, 1);
        session.Trim().DragHandle(RangeEnd.First, 1);
        var firstLayers = session.Layers;
        Assert.IsTrue((await listed.Editing.Save()).Succeeded);
        var first = listed.Listing.Items.Single();
        Assert.AreEqual(new MediaFacts(ImageFormat.Gif, 8, 6, false, 1, 0), first.Facts);
        var firstBytes = File.ReadAllBytes(first.FilePath);
        await Consumers(listed, first, firstLayers);

        await listed.Editing.Open(first, EditOrigin.Preview);
        session = listed.Editing.Session!;
        Assert.IsTrue(session.Animated, "the original still has all its frames");
        session.Tool = session.Trim();
        session.Trim().Reset();
        session.Tool = session.Hide();
        session.Hide().Choose(HideKind.Black);
        session.DragThrough((3, 3), (6, 5));
        var copiedLayers = session.Layers;
        Assert.IsTrue((await listed.Editing.SaveAsNewItem()).Succeeded);
        Assert.IsTrue(listed.Editing.IsOpen);
        var copy = listed.Listing.Items.Single(i => i.Id != first.Id);
        Assert.AreEqual(copy.Id, session.Item.Id);
        Assert.AreEqual(new MediaFacts(ImageFormat.Gif, 8, 6, true, 3, 600), copy.Facts, "F3 derive from original, not one-frame render");
        CollectionAssert.AreEqual(firstBytes, File.ReadAllBytes(first.FilePath), "source finished picture unchanged");
        CollectionAssert.AreEqual(original, File.ReadAllBytes(session.Edit.OriginalPath), "new original is a WebP copy");
        await Consumers(listed, copy, copiedLayers);
        session.Shrink = Shrink.Half;
        var finalLayers = session.Layers;
        Assert.IsTrue((await listed.Editing.Save()).Succeeded);
        var final = listed.Listing.Items.Single(i => i.Id == copy.Id);
        Assert.AreEqual(new MediaFacts(ImageFormat.Gif, 4, 3, true, 3, 600), final.Facts);
        await Consumers(listed, final, finalLayers);
    }

    [TestMethod]
    public async Task LongerTrimWithRangedHideKeepsEveryFrameIncludingZeroDelay()
    {
        using var stream = new MemoryStream();
        var clip = GifEncoder.Open(stream, 32, 24, "original").Value!;
        long time = 0;
        var durations = new[] { 30, 100, 0, 20, 40, 70, 10, 0, 80, 120, 50, 20, 90, 40, 60, 100 };
        for (var i = 0; i < durations.Length; i++)
        {
            Assert.IsTrue(clip.Append(SyntheticFrames.Pattern(i, 32, 24), time).Succeeded);
            time += durations[i];
        }
        Assert.IsTrue(clip.Stop(time).Succeeded);
        using var listed = await ListedStash.Open();
        var item = await listed.Stash.ImportPicture("long clip", "long.gif", stream.ToArray());
        var originals = MediaFixtures.Frames(item.FilePath);
        await listed.Listing.Refresh();
        await listed.Editing.Open(item, EditOrigin.Grid);
        var session = listed.Editing.Session!;
        session.Pause();
        session.Tool = session.Hide();
        session.Hide().Choose(HideKind.Black);
        session.DragThrough((3, 3), (6, 5));
        session.DragBarEnd(RangeEnd.First, 5);
        session.DragBarEnd(RangeEnd.Last, 8);
        session.Tool = session.Trim();
        session.Trim().DragHandle(RangeEnd.First, 3);
        session.Trim().DragHandle(RangeEnd.Last, 12);
        var layers = session.Layers;
        Assert.IsTrue((await listed.Editing.Save()).Succeeded);
        var saved = listed.Listing.Items.Single();
        var frames = MediaFixtures.Frames(saved.FilePath);
        Assert.HasCount(10, frames);
        CollectionAssert.AreEqual(durations.Skip(3).Take(10).ToArray(), frames.Select(f => f.DurationMs).ToArray());
        CollectionAssert.AreEqual(durations.Skip(3).Take(10).Select(ms => ms / 10).ToArray(), GifImages.Read(saved.FilePath).Images.Select(f => f.DelayCs).ToArray());
        for (var i = 0; i < frames.Count; i++)
        {
            var index = i + 3;
            var expected = index is >= 5 and <= 8 ? SyntheticFrames.Paint(originals[index], 3, 3, 3, 2, (0, 0, 0)) : originals[index];
            CollectionAssert.AreEqual(expected.Pixels, frames[i].Pixels, $"G22 frame {index} and its range");
        }
        await Consumers(listed, saved, layers);
    }
}
