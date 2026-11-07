using ImageStash.Files;
using ImageStash.Layers;
using ImageStash.Media;
using ImageStash.Store;
using ImageStash.Tests.Support;
using ImageStash.Window;

namespace ImageStash.Tests.Window;

/// <summary>
/// The preview's facts (G19): the type, dimensions, the file's size on disk and, for an animation only, its duration;
/// when the item was added and how often and how recently it was used, never used saying so; on an edited item, its
/// layer kinds and that the original is kept, from the list the store records; every time the store's UTC shown in
/// local time (What crosses every boundary).
/// </summary>
[TestClass]
public sealed class PreviewFactsTests
{
    /// <summary>A zone ten hours ahead of UTC, so a day read in UTC would differ from the local one.</summary>
    private static readonly TimeZoneInfo Ahead = TimeZoneInfo.CreateCustomTimeZone("ten ahead", TimeSpan.FromHours(10), "ten ahead", "ten ahead");

    private static readonly DateTimeOffset Now = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void AnAnimationStatesItsTypeSizeFileLengthAndDurationThenWhenAddedAndItsUses()
    {
        var gif = Item(new MediaFacts(ImageFormat.Gif, 480, 300, true, 22, 2200), addedAt: new DateTimeOffset(2026, 10, 11, 20, 0, 0, TimeSpan.Zero));

        var lines = PreviewFacts.Lines(gif, 1_782_579, new UseHistory(23, Now.AddMinutes(-4)), LayerList.Empty, Now, Ahead);

        CollectionAssert.AreEqual(new[] { "GIF · 480 × 300 · 1.7 MB · 2.2 s", "Added Oct 12, 2026 · Used 23 times, last 4 min ago" }, lines.ToList(),
            "added at 20:00 UTC on Oct 11 is Oct 12 in a zone ten hours ahead: the local day, never the UTC one");
    }

    [TestMethod]
    public void AStillStatesNoDurationAndAnItemNeverUsedSaysSo()
    {
        var png = Item(new MediaFacts(ImageFormat.Png, 6, 4, false, 1, 0), addedAt: Now.AddDays(-30));

        var lines = PreviewFacts.Lines(png, 88, new UseHistory(0, null), LayerList.Empty, Now, TimeZoneInfo.Utc);

        CollectionAssert.AreEqual(new[] { "PNG · 6 × 4 · 1 KB", "Added Sep 15, 2026 · Never used" }, lines.ToList());
    }

    [TestMethod]
    public void OneUseSaysOnceAndTheLatestUseReadsHowLongAgo()
    {
        var used = new UseHistory(1, Now.AddSeconds(-20));

        Assert.AreEqual("Used once, just now", PreviewFacts.Use(used, Now));
        Assert.AreEqual("Used 2 times, last 3 hr ago", PreviewFacts.Use(new UseHistory(2, Now.AddHours(-3).AddMinutes(-10)), Now));
        Assert.AreEqual("Used 5 times, last yesterday", PreviewFacts.Use(new UseHistory(5, Now.AddHours(-30)), Now));
        Assert.AreEqual("Used 5 times, last 6 days ago", PreviewFacts.Use(new UseHistory(5, Now.AddDays(-6)), Now));
        Assert.AreEqual("Used once, just now", PreviewFacts.Use(new UseHistory(1, Now.AddMinutes(2)), Now), "a use stamped ahead of this clock is not in the future");
        Assert.AreEqual("Never used", PreviewFacts.Use(new UseHistory(0, null), Now));
    }

    [TestMethod]
    public void TheDayAddedIsTodayOrYesterdayByTheLocalCalendarElseItsDate()
    {
        var lateLocalEvening = new DateTimeOffset(2026, 10, 15, 13, 0, 0, TimeSpan.Zero);
        var afterLocalMidnight = new DateTimeOffset(2026, 10, 15, 15, 0, 0, TimeSpan.Zero);

        Assert.AreEqual("today", PreviewFacts.Day(Now.AddHours(-1), Now, TimeZoneInfo.Utc));
        Assert.AreEqual("yesterday", PreviewFacts.Day(Now.AddDays(-1), Now, TimeZoneInfo.Utc));
        Assert.AreEqual("Oct 13, 2026", PreviewFacts.Day(Now.AddDays(-2), Now, TimeZoneInfo.Utc));
        Assert.AreEqual("yesterday", PreviewFacts.Day(lateLocalEvening, afterLocalMidnight, Ahead),
            "23:00 there is yesterday at 01:00 there, though both are Oct 15 in UTC");
        Assert.AreEqual("today", PreviewFacts.Day(lateLocalEvening, afterLocalMidnight, TimeZoneInfo.Utc));
    }

    [TestMethod]
    public void TheTypeNamesTheFormatAndAnAnimatedWebP()
    {
        Assert.AreEqual("JPG", PreviewFacts.Type(new MediaFacts(ImageFormat.Jpeg, 1, 1, false, 1, 0)));
        Assert.AreEqual("GIF", PreviewFacts.Type(new MediaFacts(ImageFormat.Gif, 1, 1, false, 1, 0)), "a still GIF is a GIF");
        Assert.AreEqual("WebP", PreviewFacts.Type(new MediaFacts(ImageFormat.WebP, 1, 1, false, 1, 0)));
        Assert.AreEqual("Animated WebP", PreviewFacts.Type(new MediaFacts(ImageFormat.WebP, 1, 1, true, 3, 600)));
    }

    [TestMethod]
    public void AFileWhoseLengthCouldNotBeReadLeavesTheSizeOutAndKeepsTheRest()
    {
        var gif = Item(new MediaFacts(ImageFormat.Gif, 8, 6, true, 3, 600), addedAt: Now);

        Assert.AreEqual("GIF · 8 × 6 · 0.6 s", PreviewFacts.Lines(gif, null, new UseHistory(0, null), LayerList.Empty, Now, TimeZoneInfo.Utc)[0]);
    }

    [TestMethod]
    public void AFileHeldOpenWithoutSharingStillGivesItsLength()
    {
        using var scratch = new ScratchFolder();
        var path = scratch.File("held.png");
        File.WriteAllBytes(path, new byte[3000]);

        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.AreEqual(3000L, PreviewFacts.FileLength(path), "read from the directory entry, which a program holding the file does not hide (G24)");
        }
    }

    [TestMethod]
    public void AFileThatIsGoneGivesNoLengthRatherThanNothingsLength()
    {
        using var scratch = new ScratchFolder();

        Assert.IsNull(PreviewFacts.FileLength(scratch.File("gone.png")), "no size to state, not a size of 0 that would read as 1 KB");
        Assert.IsNull(PreviewFacts.FileLength(scratch.Path), "a folder is no file to measure");
    }

    [TestMethod]
    public async Task AnItemTheStoreSavedStatesItsRendersTypeSizeMeasuredLengthAndDuration()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var still = (await stash.SaveEdit(await stash.ImportFixture("still", "still.webp"), EditedItems.Cropped)).Value!;
        var clip = (await stash.SaveEdit(await stash.ImportFixture("clip", "animated.webp"), new LayerList([new TrimLayer(new FrameRange(1, 2))]))).Value!;
        var listed = (await stash.All()).ToDictionary(item => item.Id);
        var (stillLength, clipLength) = (new FileInfo(stash.Folder.InRoot("still.png")).Length, new FileInfo(stash.Folder.InRoot("clip.gif")).Length);
        var clipFrame = MediaFixtures.FirstFrame(stash.Folder.InRoot("clip.gif"));

        Assert.AreEqual((stillLength, clipLength), (PreviewFacts.FileLength(listed[still.Id].FilePath), PreviewFacts.FileLength(listed[clip.Id].FilePath)), "the measured size is the render's file (G24)");
        CollectionAssert.AreEqual(new[] { $"PNG · 4 × 3 · {FileSize.Words(stillLength)}", "Added Oct 16, 2026 · Never used" },
            PreviewFacts.Lines(listed[still.Id], stillLength, new UseHistory(0, null), LayerList.Empty, stash.Clock.Now.AddDays(30), TimeZoneInfo.Utc).ToList(), "the cropped render's type and size, not the WebP's 8 × 6");
        CollectionAssert.AreEqual(new[] { $"GIF · {clipFrame.Width} × {clipFrame.Height} · {FileSize.Words(clipLength)} · 0.5 s", "Added Oct 16, 2026 · Never used" },
            PreviewFacts.Lines(listed[clip.Id], clipLength, new UseHistory(0, null), LayerList.Empty, stash.Clock.Now.AddDays(30), TimeZoneInfo.Utc).ToList(), "the trimmed clip's 200 + 300 ms, not the original's 600");
    }

    [TestMethod]
    public void AnEditedItemsThirdLineNamesEachKindOnceInListOrderAndThatTheOriginalIsKept()
    {
        var png = Item(new MediaFacts(ImageFormat.Png, 8, 6, false, 1, 0), addedAt: Now);
        var layers = new LayerList(
        [
            new HideLayer(new PixelRect(0, 0, 2, 2), HideKind.Blur, null),
            new DrawMarkLayer(MarkKind.Box, [], new PixelRect(1, 1, 3, 3), new Rgba(255, 0, 0, 255), Thickness.Thin, null),
            new HideLayer(new PixelRect(4, 2, 2, 2), HideKind.Black, null),
        ]);

        var lines = PreviewFacts.Lines(png, 88, new UseHistory(0, null), layers, Now, TimeZoneInfo.Utc);

        CollectionAssert.AreEqual(new[] { "PNG · 8 × 6 · 1 KB", "Added today · Never used", "Edited here: hide, draw mark · the original is kept" }, lines.ToList(),
            "G19: its layer kinds, each once, in list order, by F7's names");
        Assert.HasCount(2, PreviewFacts.Lines(png, 88, new UseHistory(0, null), LayerList.Empty, Now, TimeZoneInfo.Utc), "an unedited item has no third line");
    }

    [TestMethod]
    public void EveryKindHasF7sName()
    {
        var png = Item(new MediaFacts(ImageFormat.Png, 8, 6, false, 1, 0), addedAt: Now);
        var layers = new LayerList(
        [
            new CropLayer(new PixelRect(0, 0, 8, 6)),
            new TextLayer(TextPreset.Label, "hi", "Segoe UI", 12, new Rgba(0, 0, 0, 255), Outline.Auto, new PixelRect(0, 0, 4, 2), 0, null),
            new CaptionBarLayer("hey", "Segoe UI", 12, new Rgba(0, 0, 0, 255), Outline.None),
            new ShrinkLayer(Shrink.Half),
        ]);

        Assert.AreEqual("Edited here: crop, text, caption bar, shrink · the original is kept", PreviewFacts.Lines(png, 88, new UseHistory(0, null), layers, Now, TimeZoneInfo.Utc)[2]);
    }

    [TestMethod]
    public async Task TheLayersNamedAreTheCommittedListEvenWhileALiveFailureShowsThePictureOnDisk()
    {
        using var stash = new TestStash();
        await stash.OpenCleanly();
        var cat = await EditedItems.FirstSaveCommitted(stash);
        var plain = await stash.ImportFixture("plain", "transparent.png");
        var listed = (await stash.All()).ToDictionary(item => item.Id);

        var edited = await PreviewFacts.EditedLayers(stash.Store!, listed[cat.Id]);
        var unedited = await PreviewFacts.EditedLayers(stash.Store!, listed[plain.Id]);

        Assert.AreEqual(EditedItems.BlackBoxAndBox, edited.Value, "the list the store records and the next start renders, though Edit opens on the original's (State: A live failure)");
        Assert.AreEqual(LayerList.Empty, unedited.Value);
        stash.SqlWhileOpen("UPDATE items SET layers = '{' WHERE id = $id", ("$id", cat.Id));
        Assert.StartsWith("Read the item's layers failed: cat, ", (await PreviewFacts.EditedLayers(stash.Store!, listed[cat.Id])).Failure!.Sentence, "a list the store cannot read is named (G35)");
    }

    private static StashItem Item(MediaFacts facts, DateTimeOffset addedAt) =>
        TestItems.Sized(1, facts.Width, facts.Height) with { Facts = facts, AddedAt = addedAt };
}
